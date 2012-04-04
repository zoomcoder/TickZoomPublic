#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.Starters
{
    public class FIXSimulatorStarter : RealTimeStarterBase
    {
        private static readonly Log log = Factory.SysLog.GetLogger(typeof (FIXSimulatorStarter));
		public FIXSimulatorStarter() {
			SyncTicks.Enabled = true;
			ConfigurationManager.AppSettings.Set("ProviderAddress","InProcess");
		}
		
		public override void Run(ModelLoaderInterface loader)
		{
            SetupSymbolData();
		    Factory.Provider.StartSockets();
            parallelMode = ParallelMode.RealTime;
            Factory.SysLog.RegisterHistorical("FIXSimulator", GetDefaultLogConfig());
            Factory.SysLog.RegisterRealTime("FIXSimulator", GetDefaultLogConfig());
            Config = "WarehouseTest.config";
		    Address = "inprocess";
#if !USE_LIME
            var provider = "MBTFIXProvider/Simulate";
            var fixAssembly = "MBTFIXProvider";
            var fixSimulator = "MBTProviderSimulator";
#else
            var provider = "LimeProvider/Simulate";
            var fixAssembly = "LimeProvider";
            var fixStmulator = "LimeFIXSimulator";
#endif
            AddProvider(provider);
            SetupProviderServiceConfig();
            var providerManager = Factory.Parallel.SpawnProvider("ProviderCommon", "ProviderManager");
            providerManager.SendEvent(new EventItem(EventType.SetConfig, "WarehouseTest"));
            using (Factory.Parallel.SpawnProvider(fixAssembly, fixSimulator, "Simulate", ProjectProperties))
            { 
				base.Run(loader);
			}
            Factory.Provider.ShutdownSockets();
        }

        public void SetupSymbolData()
        {
            string appDataFolder = Factory.Settings["AppDataFolder"];
            var realTimeDirectory = appDataFolder + Path.DirectorySeparatorChar +
                            "Test" + Path.DirectorySeparatorChar +
                            "MockProviderData";
            var historicalDirectory = appDataFolder + Path.DirectorySeparatorChar +
                            "Test" + Path.DirectorySeparatorChar +
                            "ServerCache";
            DeleteDirectory(realTimeDirectory);
            DeleteDirectory(historicalDirectory);
            Directory.CreateDirectory(realTimeDirectory);
            Directory.CreateDirectory(historicalDirectory);
            foreach (var symbol in ProjectProperties.Starter.SymbolProperties)
            {
                CopySymbol(historicalDirectory,realTimeDirectory,symbol.Symbol);
            }
        }

        public static void DeleteDirectory(string path)
        {
            var errors = new List<Exception>();
            var errorCount = 0;
            while (errorCount < 30)
            {
                try
                {
                    if( Directory.Exists(path))
                    {
                        Directory.Delete(path,true);
                    }
                    errors.Clear();
                    break;
                }
                catch (Exception ex)
                {
                    log.Info("Delete " + path + " error " + errorCount + ": " + ex.Message);
                    errors.Add(ex);
                    Thread.Sleep(1000);
                    errorCount++;
                }
            }
            if (errors.Count > 0)
            {
                var ex = errors[errors.Count - 1];
                throw new IOException("Can't delete " + path, ex);
            }
        }

        public void CopySymbol(string historical, string realTime, string symbol)
        {
            while (true)
            {
                try
                {
                    symbol = symbol.StripInvalidPathChars();
                    string appData = Factory.Settings["AppDataFolder"];
                    var fromDirectory = appData + Path.DirectorySeparatorChar +
                                    "Test" + Path.DirectorySeparatorChar +
                                    "DataCache";
                    var files = Directory.GetFiles(fromDirectory, symbol + ".tck", SearchOption.AllDirectories);
                    if( files.Length > 1)
                    {
                        var sb = new StringBuilder();
                        foreach (var file in files)
                        {
                            sb.AppendLine(file);
                        }
                        throw new ApplicationException("Sorry more than one file matches " + symbol + ".tck:\n" + sb);
                    }
                    else if( files.Length == 1)
                    {
                        var fromFile = files[0];
                        var realTimeFile = realTime + Path.DirectorySeparatorChar + symbol + ".tck";
                        var historyFile = historical + Path.DirectorySeparatorChar + symbol + ".tck";
                        if( ProjectProperties.Simulator.WarmStartTime < ProjectProperties.Starter.EndTime)
                        {
                            SplitAndCopy(fromFile, historyFile, realTimeFile, ProjectProperties.Simulator.WarmStartTime);
                        }
                        else
                        {
                            File.Copy(fromFile, realTimeFile);
                        }
                    }
                    break;
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("Copy " + symbol + " to simulator failed. Retrying...", ex);
                }
            }
            Thread.Sleep(2000);
        }

        private void SplitAndCopy(string fromFile, string historyFile, string realTimeFile, TimeStamp cutoverTime)
        {
            // Setup historical
            var subproc = Factory.Provider.SubProcess();
            subproc.ExecutableName = "tzdata.exe";
            subproc.AddArgument("filter");
            subproc.AddArgument(fromFile);
            subproc.AddArgument(historyFile);
            subproc.AddArgument(ProjectProperties.Starter.StartTime.ToString());
            subproc.AddArgument(cutoverTime.ToString());
            subproc.Run();

            // Setup real time
            subproc = Factory.Provider.SubProcess();
            subproc.ExecutableName = "tzdata.exe";
            subproc.AddArgument("filter");
            subproc.AddArgument(fromFile);
            subproc.AddArgument(realTimeFile);
            subproc.AddArgument(cutoverTime.ToString());
            subproc.AddArgument(ProjectProperties.Starter.EndTime.ToString());
            subproc.Run();
        }

        private string GetDefaultLogConfig()
        {
			return @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<configuration>
 <log4net>
    <root>
	<level value=""INFO"" />
	<appender-ref ref=""FileAppender"" />
	<appender-ref ref=""ConsoleAppender"" />
    </root>
    <logger name=""StatsLog"">
        <level value=""INFO"" />
    	<additivity value=""false"" />
	<appender-ref ref=""StatsLogAppender"" />
    </logger>
    <logger name=""TradeLog"">
        <level value=""INFO"" />
    	<additivity value=""false"" />
	<appender-ref ref=""TradeLogAppender"" />
    </logger>
    <logger name=""TransactionLog.Performance"">
        <level value=""INFO"" />
    	<additivity value=""false"" />
	<appender-ref ref=""TransactionLogAppender"" />
    </logger>
    <logger name=""BarDataLog"">
        <level value=""INFO"" />
    	<additivity value=""false"" />
	<appender-ref ref=""BarDataLogAppender"" />
    </logger>
    <logger name=""TickZoom.Common"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.FIX"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.MBTFIX"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.MBTQuotes"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.Engine.SymbolReceiver"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.ProviderService"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.Engine.EngineKernel"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.Internals.OrderGroup"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.Internals.OrderManager"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.Engine.SymbolController"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.Interceptors.FillSimulatorPhysical"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.Interceptors.FillHandlerDefault"">
        <level value=""INFO"" />
    </logger>
    <logger name=""TickZoom.Common.OrderAlgorithmDefault"">
        <level value=""INFO"" />
    </logger>
 </log4net>
</configuration>
";				
		}
	}
}