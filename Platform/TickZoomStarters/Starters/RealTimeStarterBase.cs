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
using System.Configuration;
using System.IO;

using TickZoom.Api;
using TickZoom.Common;

namespace TickZoom.Starters
{
	public class RealTimeStarterBase : StarterCommon
	{
		Log log = Factory.SysLog.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		public override void Run(ModelInterface model)
		{
            SetupProviderServiceConfig();
		    Factory.Provider.StartSockets();
            runMode = RunMode.RealTime;
            try
            {
                base.Run(model);
            }
            finally
            {
                parallelMode = ParallelMode.Normal;
                Factory.Provider.ShutdownSockets();
            }


            //switch (Address.ToLower())
            //{
            //    case "inprocess":
            //        {
            //            var service = Factory.Provider.ProviderService();
            //            if (Config != null)
            //            {
            //                service.SetConfig(Config);
            //            }
            //            try
            //            {
            //                service.OnStart();
            //                base.Run(model);
            //            }
            //            finally
            //            {
            //                service.OnStop();
            //                parallelMode = ParallelMode.Normal;
            //            }
            //        }
            //        break;
            //    case "subprocess":
            //        {
            //            var service = Factory.Provider.Subprocess();
            //            service.ExecutableName = "TickZoomWarehouse.exe";
            //            service.AddArgument("--config WarehouseTest --run");
            //            service.TryStart();
            //            try
            //            {
            //                base.Run(model);
            //            }
            //            finally
            //            {
            //                service.TryKill();
            //                parallelMode = ParallelMode.Normal;
            //            }
            //        }
            //        break;
            //    default:
            //        break;
            //}
		}

		public void SetupProviderServiceConfig()
		{
			try {
                var storageFolder = Factory.Settings["AppDataFolder"];
                var providersPath = Path.Combine(storageFolder, "Providers");
                var configPath = Path.Combine(providersPath, "ProviderService");
                var configFile = Path.Combine(configPath, "WarehouseTest.config");
                var warehouseConfig = new ConfigFile(configFile);
                warehouseConfig.SetValue("ServerCacheFolder", "Test\\ServerCache");
                var provider = ProviderPlugins[0];
                warehouseConfig.SetValue("ProviderAssembly", provider);
                warehouseConfig.SetValue("ProviderAddress", "inprocess");
                warehouseConfig.SetValue("ProviderPort", "6491");
			} catch( Exception ex) {
				log.Error("Setup error.",ex);
				throw ex;
			}
		}
	}
}
