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
	public class RealTimeStarter : RealTimeStarterBase
	{
		Log log = Factory.SysLog.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
		
		public override void Run(ModelLoaderInterface loader)
		{
			parallelMode = ParallelMode.RealTime;
            Factory.SysLog.RegisterRealTime("RealTime", GetDefaultLogConfig()); // This creates the config if not exists.
            Factory.SysLog.RegisterHistorical("Historical", HistoricalStarter.GetDefaultLogConfig()); // This creates the config if not exists.
            Factory.SysLog.ReconfigureForRealTime();
            base.Run(loader);
		}
	
		public override void Run(ModelInterface model)
		{
			ProjectProperties.Starter.EndTime = TimeStamp.MaxValue;
			base.Run(model);
		}
		
		public static string GetDefaultLogConfig() {
			return @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<configuration>
 <log4net>
    <root>
	<level value=""INFO"" />
	<appender-ref ref=""FileAppender"" />
    </root>
    <logger name=""TestLog"">
        <level value=""INFO"" />
    </logger>
 </log4net>
</configuration>
";				
		}
	}
}
