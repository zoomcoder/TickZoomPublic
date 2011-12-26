using System;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public class ActiveMultiQueue<T>
    {
        private int capacity;
        private int totalCount;
        private SingleQueue[] queues;
        public int readFailure;

        private class SingleQueue
        {
            internal int dequeueLock;
            internal int enqueueLock;
            internal T[] queue;
            internal int head;
            internal int tail;
            internal int count;
            internal int EnqueueCount;
            internal int DequeueCount;
            internal SingleQueue(int capacity)
            {
                queue = new T[capacity];
            }
        }

        public ActiveMultiQueue()
            : this(1000)
        {
        }

        public ActiveMultiQueue(int capacity)
        {
            this.capacity = capacity;
            queues = new SingleQueue[Environment.ProcessorCount];
            for( var i=0; i<queues.Length; i++)
            {
                queues[i] = new SingleQueue(capacity);
            }
        }

        public int Count
        {
            get { return totalCount; }
        }

        public void Enqueue(T item)
        {
            if (!TryEnqueue(item))
            {
                throw new ApplicationException("Queue is full.");
            }
        }

        public bool TryEnqueue(T item)
        {
            if (totalCount >= capacity)
            {
                return false;
            }

            for( var i = 0; i<queues.Length; i++)
            {
                var queue = queues[i];
                if (queue.count < capacity && Interlocked.CompareExchange(ref queue.enqueueLock, 1, 0) == 0)
                {
                    if (queue.count >= capacity)
                    {
                        queue.enqueueLock = 0;
                        continue;
                    }
                    queue.queue[queue.head] = item;
                    ++queue.head;
                    if (queue.head == capacity)
                    {
                        queue.head = 0;
                    }
                    ++queue.count;
                    Interlocked.Increment(ref queue.count);
                    queue.EnqueueCount = Interlocked.Increment(ref totalCount);
                    queue.enqueueLock = 0;
                    return true;
                }
            }
            return false;
        }

        public T Dequeue()
        {
            T item;
            if (!TryDequeue(out item))
            {
                throw new ApplicationException("Queue is empty");
            }
            return item;
        }

        public void Clear()
        {
            T item;
            //while (TryDequeue(out item)) ;
        }

        public bool TryDequeue(out T item)
        {
            if( totalCount == 0)
            {
                item = default(T);
                return false;
            }
            for (var i = 0; i < queues.Length; i++)
            {
                var queue = queues[0];
                if (queue.count > 0 && Interlocked.CompareExchange(ref queue.dequeueLock, 1, 0) == 0)
                {
                    if (queue.count == 0)
                    {
                        queue.dequeueLock = 0;
                        continue;
                    }
                    item = queue.queue[queue.tail];
                    queue.tail++;
                    if (queue.tail == capacity)
                    {
                        queue.tail = 0;
                    }
                    --queue.count;
                    Interlocked.Decrement(ref queue.count);
                    queue.DequeueCount = Interlocked.Decrement(ref totalCount);
                    queue.dequeueLock = 0;
                    return true;
                }
            }
            item = default(T);
            return false;
        }

        public T Peek()
        {
            T item;
            if (!TryPeek(out item))
            {
                throw new ApplicationException("Queue is empty");
            }
            return item;
        }

        public bool TryPeek(out T item)
        {
            if (totalCount == 0)
            {
                item = default(T);
                return false;
            }
            for (var i = 0; i < queues.Length; i++)
            {
                var queue = queues[i];
                if (queue.count > 0 && Interlocked.CompareExchange(ref queue.dequeueLock, 1, 0) == 0)
                {
                    if( queue.count == 0)
                    {
                        queue.dequeueLock = 0;
                        continue;
                    }
                    item = queue.queue[queue.tail];
                    queue.dequeueLock = 0;
                    return true;
                }
            }
            item = default(T);
            return false;
        }
    }
}