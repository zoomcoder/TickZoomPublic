﻿using System.Configuration;
using TickZoom.Api;

namespace TickZoom.Starters
{
    public class FIXNegativeStarter : RealTimeStarterBase
    {
        public FIXNegativeStarter()
        {
            SyncTicks.Enabled = true;
            ConfigurationManager.AppSettings.Set("ProviderAddress", "InProcess");
        }

        public override void Run(ModelLoaderInterface loader)
        {
            parallelMode = ParallelMode.RealTime;
            Factory.SysLog.RegisterHistorical("FIXSimulator", GetDefaultLogConfig());
            Factory.SysLog.RegisterRealTime("FIXSimulator", GetDefaultLogConfig());
            Config = "WarehouseTest.config";
            Address = "inprocess";
            AddProvider("MBTFIXProvider/Simulate");
            SetupProviderServiceConfig();
            using (var fixServer = (FIXSimulator)Factory.FactoryLoader.Load(typeof(FIXSimulator), "MBTFIXProvider", "Negative"))
            {
                base.Run(loader);
            }
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