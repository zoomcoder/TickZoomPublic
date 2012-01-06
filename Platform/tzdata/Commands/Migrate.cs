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
using System.IO;
using System.Reflection;
using System.Threading;

using TickZoom.Api;
using TickZoom.TickUtil;

namespace TickZoom.TZData
{
    public class Migrate : Command
	{
		public override void Run(string[] args)
		{
			if( args.Length != 2) {
				Output("Migrate Usage:");
                Output("tzdata " + Usage());
				return;
			}
			MigrateFile(args[1],args[0]);
		}
		
		private void MigrateFile(string file, string symbol) {
			if( File.Exists(file + ".back")) {
                Output("A backup file already exists. Please delete it first at: " + file + ".back");
				return;
			}
			using( var reader = new TickFile())
			{
                using (var writer = new TickFile())
                {
                    writer.EraseFileToStart = true;
                    reader.Initialize(file, symbol, TickFileMode.Read);

                    writer.Initialize(file + ".temp", TickFileMode.Write);

                    TickIO firstTick = Factory.TickUtil.TickIO();
                    TickIO tickIO = Factory.TickUtil.TickIO();
                    int count = 0;
                    bool first = true;
                    try
                    {
                        while (reader.TryReadTick(tickIO))
                        {
                            writer.WriteTick(tickIO);
                            if (first)
                            {
                                firstTick.Copy(tickIO);
                                first = false;
                            }
                            count++;
                        }
                    }
                    catch (QueueException ex)
                    {
                        if (ex.EntryType != EventType.EndHistorical)
                        {
                            throw new ApplicationException("Unexpected QueueException: " + ex);
                        }
                    }
                    Output(reader.Symbol + ": Migrated " + count + " ticks from " + firstTick.Time + " to " + tickIO.Time);
                }
            }
			Alter.MoveFile( file, file + ".back");
            Alter.MoveFile(file + ".temp", file);
		}
		
		public override string[] Usage() {
			List<string> lines = new List<string>();
			string name = Assembly.GetEntryAssembly().GetName().Name;
			lines.Add( name + " migrate <symbol> <file>");
			return lines.ToArray();
		}
	}
}
