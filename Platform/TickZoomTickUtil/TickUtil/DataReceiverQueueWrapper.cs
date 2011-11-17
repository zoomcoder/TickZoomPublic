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

        public void Enqueue(EventItem item, long utcTime)
        {
            var binary = (TickBinaryBox)item.EventDetail;
            tickQueue.Enqueue(ref binary.TickBinary);
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
}