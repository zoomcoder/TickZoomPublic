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
using System.Security.Cryptography;

using TickZoom.Api;
using TickZoom.MBTQuotes;

namespace TickZoom.MBTFIX
{
	public class MBTProvider : TestableProvider
	{
	    private static readonly Log log = Factory.SysLog.GetLogger(typeof (MBTProvider));
	    private static readonly bool debug = log.IsDebugEnabled;
		private Provider fixProvider;
		private Provider quotesProvider;
		
		public MBTProvider(string configName) {
			fixProvider = Factory.Parallel.SpawnProvider(typeof(MBTFIXProvider),configName);
			quotesProvider = Factory.Parallel.SpawnProvider(typeof(MBTQuotesProvider),configName);
		}

	    public bool IsFinalized
	    {
	        get { return fixProvider.IsFinalized && quotesProvider.IsFinalized && isFinalized; }
	    }

	    public void SendEvent( Receiver receiver, SymbolInfo symbol, int eventType, object eventDetail) {
			switch( (EventType) eventType) {
				case EventType.PositionChange:
					fixProvider.SendEvent(receiver,symbol,eventType,eventDetail);
					break;
				case EventType.StopSymbol:
				case EventType.StartSymbol:
				case EventType.Disconnect:
				case EventType.Connect:
                case EventType.RemoteShutdown:
                    quotesProvider.SendEvent(receiver, symbol, eventType, eventDetail);
                    fixProvider.SendEvent(receiver, symbol, eventType, eventDetail);
                    break;
                case EventType.Terminate:
					Dispose();
					break; 
				default:
					throw new ApplicationException("Unexpected event type: " + (EventType) eventType);
			}
		}
		
	 	private volatile bool isDisposed;
	    private bool isFinalized;
	    public void Dispose() 
	    {
	        Dispose(true);
	        GC.SuppressFinalize(this);      
	    }
	
	    protected virtual void Dispose(bool disposing)
	    {
       		if( !isDisposed) {
	            isDisposed = true;   
	            if (disposing) {
                    if( debug) log.Debug("Disposing()");
                    if (quotesProvider != null)
                    {
                        if (debug) log.Debug("Disposing quotes provider");
                        quotesProvider.Dispose();
                    }
	                isFinalized = true;
	            }
    		}
	    }
		
	}
}
