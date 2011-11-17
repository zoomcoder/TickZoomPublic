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
using TickZoom.Api;

//using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public class DataReceiverDefault : Receiver {
	   	static readonly Log log = Factory.SysLog.GetLogger(typeof(DataReceiverDefault));
	   	readonly bool debug = log.IsDebugEnabled;
	    private TickQueue readQueue = new TickQueueImpl("DataReceiverDefault",1000);
        Provider sender;
	    private DataReceiverQueueWrapper wrapper;

        public ReceiveEventQueue GetQueue(SymbolInfo symbol)
        {
            if (debug) log.Debug("GetQueue for " + symbol);
            if (symbol.BinaryIdentifier != wrapper.Symbol.BinaryIdentifier)
            {
                throw new ApplicationException("Requested " + symbol + " but expected " + wrapper.Symbol);
            }
            return wrapper;
        }
        
		private ReceiverState receiverState = ReceiverState.Ready;
		
		public ReceiverState OnGetReceiverState(SymbolInfo symbol) {
			return receiverState;
		}
		
		public DataReceiverDefault(Provider sender, SymbolInfo symbol) {
			this.sender = sender;
		    var tickPool = Factory.TickUtil.TickPool(symbol);
            wrapper = new DataReceiverQueueWrapper(symbol,tickPool,readQueue);
			readQueue.StartEnqueue = Start;
		}
		
		private void Start() {
			sender.SendEvent(this,null,(int)EventType.Connect,null);
		}
		
		public bool OnEvent(SymbolInfo symbol, int eventType, object eventDetail) {
            throw new NotImplementedException();
		}
		
		public TickQueue ReadQueue {
			get { return readQueue; }
		}
		
		public void Dispose() {
			
		}
	}
}
