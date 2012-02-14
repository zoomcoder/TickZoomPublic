using System;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public class DataReceiverQueueWrapper : ReceiveEventQueue
    {
        private static readonly Log log = Factory.Log.GetLogger(typeof (DataReceiverQueueWrapper));
        private bool debug = log.IsDebugEnabled;
        private Pool<TickBinaryBox> tickPool;
        private TickQueue tickQueue;
        private SymbolInfo symbol;

        public DataReceiverQueueWrapper( SymbolInfo symbol, Pool<TickBinaryBox> pool, TickQueue queue)
        {
            this.symbol = symbol;
            tickPool = pool;
            tickQueue = queue;
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

        public void Enqueue(EventItem item, long utcTime)
        {
            var eventType = (EventType) item.EventType;
            if( eventType == EventType.Tick)
            {
                var binary = (TickBinaryBox)item.EventDetail;
                tickQueue.Enqueue(ref binary.TickBinary);
                binary.Free();
            }
            else if( eventType == EventType.EndHistorical)
            {
                var queueItem = new QueueItem();
                queueItem.Symbol = item.Symbol.BinaryIdentifier;
                queueItem.EventType = eventType;
                queueItem.EventDetail = item.EventDetail;
                tickQueue.Enqueue( queueItem, utcTime);
            }
            else
            {
                if( debug) log.Debug("Ignoring event from Reader: " + eventType);
            }
        }

        public bool TryEnqueue(EventItem item, long utcTime)
        {
            throw new NotImplementedException();
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
            set { tickQueue.Capacity = value;  }
        }

        public bool IsFull
        {
            get { return tickQueue.IsFull; }
        }

        public bool IsEmpty
        {
            get { return tickQueue.IsEmpty; }
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


        #region Queue Members


        public bool DisableRelease
        {
            get { return false; }
            set { throw new NotImplementedException(); }
        }

        #endregion
    }
}