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
using System.IO;
using System.Reflection;
using System.Threading;

using TickZoom.Api;
using TickZoom.TickUtil;

namespace TickZoom.TZData
{
	public class Filter : Command
	{
		public Filter() {
		}
		
		public override void Run(string[] args)
		{
			if( args.Length != 2 && args.Length != 4) {
				Output("Filter Usage:");
                Usage();
                return;
			}
		    var field = 0;
			string from = args[field++];
            string to = args[field++];
			TimeStamp startTime;
			TimeStamp endTime;
			if( args.Length > field) {
                startTime = new TimeStamp(args[field++]);
                endTime = new TimeStamp(args[field++]);
			} else {
				startTime = TimeStamp.MinValue;
				endTime = TimeStamp.MaxValue;
			}
			FilterFile(from,to,startTime,endTime);
		}
		
		private void FilterFile(string inputPath, string outputPath, TimeStamp startTime, TimeStamp endTime)
		{
		    using( var reader = Factory.TickUtil.TickFile())
		    using( var writer = Factory.TickUtil.TickFile())
		    {
                writer.Initialize(outputPath, TickFileMode.Write);
                reader.Initialize(inputPath, TickFileMode.Read);
                TickIO firstTick = Factory.TickUtil.TickIO();
                TickIO lastTick = Factory.TickUtil.TickIO();
                TickIO prevTick = Factory.TickUtil.TickIO();
                long count = 0;
                long dups = 0;
                TickIO tickIO = Factory.TickUtil.TickIO();
                try
                {
                    while (reader.TryReadTick(tickIO))
                    {

                        if (tickIO.Time >= startTime)
                        {
                            if (tickIO.Time > endTime) break;
                            if (count == 0)
                            {
                                prevTick.Copy(tickIO);
                                prevTick.IsSimulateTicks = true;
                                firstTick.Copy(tickIO);
                                firstTick.IsSimulateTicks = true;
                            }
                            count++;
                            //						if( tickIO.Bid == prevTick.Bid && tickIO.Ask == prevTick.Ask) {
                            //							dups++;
                            //						} else {
                            //							Elapsed elapsed = tickIO.Time - prevTick.Time;
                            prevTick.Copy(tickIO);
                            prevTick.IsSimulateTicks = true;
                            //							if( elapsed.TotalMilliseconds < 5000) {
                            //								fast++;
                            //							} else {
                            writer.WriteTick(prevTick);
                            //							}	
                            //						}
                        }
                    }
                }
                catch (QueueException ex)
                {
                    if (ex.EntryType != EventType.EndHistorical)
                    {
                        throw new ApplicationException("Unexpected QueueException: " + ex);
                    }
                }
                lastTick.Copy(tickIO);
                Output(reader.Symbol + ": " + count + " ticks.");
                Output("From " + firstTick.Time + " to " + lastTick.Time);
                Output(dups + " duplicates elimated.");
            }
		}

		public override string[] UsageLines() {
			return new string[] { AssemblyName + " filter <fromfile> <tofile> [<starttimestamp> <endtimestamp>]" };
		}
		
	}
}
