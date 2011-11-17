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
    public class DataReceiverQueueWrapper : ReceiveEventQueue
    {
        private Pool<TickBinaryBox> tickPool;
        private TickQueue tickQueue;
        private SymbolInfo symbol;

        public DataReceiverQueueWrapper( SymbolInfo symbol, Pool<TickBinaryBox> pool, TickQueue queue)
        {
            this.symbol = symbol;
            this.tickPool = pool;
            this.tickQueue = queue;
        }

        public StartEnqueue StartEnqueue
        {
            get { return tickQueue.StartEnqueue; }
            set { tickQueue.StartEnqueue = value; }
        }

        public void Clear()
        {
            tickQueue.Clear();
        }

        public void ReleaseCount()
        {
            tickQueue.ReleaseCount();
        }

        public bool Enqueue(EventItem item, long utcTime)
        {
            return TryEnqueue(item, utcTime);
        }

        public bool TryEnqueue(EventItem item, long utcTime)
        {
            var result = false;
            var binary = (TickBinaryBox)item.EventDetail;
            if (tickQueue.TryEnqueue(ref binary.TickBinary))
            {
                tickPool.Free(binary);
                result = true;
            }
            return result;
        }

        public string Name
        {
            get { return tickQueue.Name; }
        }

        public string GetStats()
        {
            return tickQueue.GetStats();
        }

        public int Count
        {
            get { return tickQueue.Count; }
        }

        public void SetException(Exception ex)
        {
            tickQueue.SetException(ex);
        }

        public bool IsStarted
        {
            get { return tickQueue.IsStarted; }
        }

        public int Capacity
        {
            get { return tickQueue.Capacity; }
        }

        public bool IsFull
        {
            get { return tickQueue.IsFull; }
        }

        public SymbolInfo Symbol
        {
            get { return symbol; }
        }

        public void ConnectInbound(Task task)
        {
            tickQueue.ConnectInbound(task);
        }

        public void ConnectOutbound(Task task)
        {
            tickQueue.ConnectOutbound(task);
        }

        public void Dispose()
        {
            tickQueue.Dispose();
        }

    }

	public class DataReceiverDefault : Receiver {
	   	static readonly Log log = Factory.SysLog.GetLogger(typeof(DataReceiverDefault));
	   	readonly bool debug = log.IsDebugEnabled;
	    private TickQueue readQueue = new TickQueueImpl("DataReceiverDefault",1000);
        Provider sender;
	    private DataReceiverQueueWrapper wrapper;

        public ReceiveEventQueue GetQueue(SymbolInfo symbol)
        {
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
