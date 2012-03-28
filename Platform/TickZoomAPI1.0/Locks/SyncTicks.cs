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
using System.ComponentModel;
using System.Threading;

namespace TickZoom.Api
{
	public unsafe struct SyncTicksState
	{
        public const int MaxNameLength = 256;
	    public bool enabled;
	    public int pageCount;
	    public bool frozen;
        public bool success;
	    public int currentTestNameLength;
	    public fixed char currentTestName[MaxNameLength];
	}
    
    /// <summary>
	/// This class is only used during unit tests for assisting
	/// in simulating a live trading environment when sending limit,
	/// stop, and other orders to the broker.
	/// </summary>
	/// 

	[CLSCompliant(false)]
	public unsafe static class SyncTicks {
		private static Log log = Factory.SysLog.GetLogger(typeof(SyncTicks));
        private static TickSyncDirectory directory;
		private static Dictionary<long,TickSync> tickSyncs;
		private static object locker = new object();
		private static int mockTradeCount = 0;

        private static TickSyncDirectory Directory
        {
            get
            {
                if (directory == null)
                {
                    lock (locker)
                    {
                        if (directory == null)
                        {
                            directory = new TickSyncDirectory();
                        }
                    }
                }
                return directory;
            }
        }
		
		public static Dictionary<long, TickSync> TickSyncs {
			get { 
				if( tickSyncs == null) {
					lock( locker) {
						if( tickSyncs == null) {
							tickSyncs = new Dictionary<long,TickSync>();
						}
					}
				}
				return tickSyncs;
			}
		}
		
		public static TickSync GetTickSync(long symbolBinaryId) {
			TickSync tickSync;
			lock( locker) {
				if( TickSyncs.TryGetValue(symbolBinaryId, out tickSync)) {
				   	return tickSync;
				} else
				{
				    var tickSyncPtr = Directory.GetTickSync(symbolBinaryId);
					tickSync = new TickSync(symbolBinaryId, tickSyncPtr);
					TickSyncs.Add(symbolBinaryId,tickSync);
					return tickSync;
				}
			}
		}
		
		public static void LogStatus() {
			log.Info("TickSync status...");
			foreach( var kvp in TickSyncs)
			{
			    var tickSync = kvp.Value;
			    var symbol = Factory.Symbol.LookupSymbol(tickSync.SymbolBinaryId);
				log.Info(symbol + " " + tickSync);
			}
		}
		
		
		public static bool Enabled {
			get { return (*Directory.SyncTicksState).enabled; }
            set
            {
                if( (*Directory.SyncTicksState).enabled != value)
                {
                    (*Directory.SyncTicksState).enabled = value;
                }
                if (value)
                {
                    Frozen = false;
                    Success = true;
                }
            }
		}

        public static bool Frozen
        {
            get { return (*Directory.SyncTicksState).frozen; }
            set
            {
                if ((*Directory.SyncTicksState).frozen != value)
                {
                    log.Debug("Frozen flag changed from " + (*Directory.SyncTicksState).frozen + " to " + value);
                    (*Directory.SyncTicksState).frozen = value;
                }
            }
        }

        public static bool Success
        {
            get { return (*Directory.SyncTicksState).success; }
            set
            {
                if ((*Directory.SyncTicksState).success!= value)
                {
                    log.Debug("Success flag changed from " + (*Directory.SyncTicksState).success + " to " + value);
                    (*Directory.SyncTicksState).success = value;
                }
            }
        }

        public static string CurrentTestName
        {
            get
            {
                var length = (*Directory.SyncTicksState).currentTestNameLength;
                var buffer = (*Directory.SyncTicksState).currentTestName;
                return new string(buffer, 0, length);
            }
            set
            {
                if( value == null)
                {
                    value = "EmptyTestName";
                }
                var array = value.ToCharArray();
                if (array.Length > SyncTicksState.MaxNameLength)
                {
                    Array.Resize(ref array, SyncTicksState.MaxNameLength);
                }
                var start = (*Directory.SyncTicksState).currentTestName;
                for( int i=0; i<array.Length; i++)
                {
                    start[i] = array[i];
                }
                (*Directory.SyncTicksState).currentTestNameLength = array.Length;
            }
        }

        public static int MockTradeCount
        {
			get { return mockTradeCount; }
			set { mockTradeCount = value; }
		}
	}
}
