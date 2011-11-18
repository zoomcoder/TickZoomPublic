using System;
using TickZoom.Api;

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

        public void ReleaseCount()
        {
            tickQueue.ReleaseCount();
        }

        public void Enqueue(EventItem item, long utcTime)
        {
            var eventType = (EventType) item.EventType;
            if( eventType == EventType.Tick)
            {
                var binary = (TickBinaryBox)item.EventDetail;
                tickQueue.Enqueue(ref binary.TickBinary);
                tickPool.Free(binary);
            }
            else
            {
                var queueItem = new QueueItem();
                queueItem.Symbol = item.Symbol.BinaryIdentifier;
                queueItem.EventType = (int) eventType;
                queueItem.EventDetail = item.EventDetail;
                tickQueue.Enqueue( queueItem, utcTime);
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
}