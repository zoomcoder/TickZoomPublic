using System;
using System.Threading;

namespace TickZoom.TickUtil
{
    public class ActiveQueue<T>
    {
        private int capacity;
        private T[] queue;
        private int count;
        internal int EnqueueCount;
        internal int DequeueCount;
        private int head;
        private int tail;

        public ActiveQueue()
            : this(1000)
        {
        }

        public ActiveQueue(int capacity)
        {
            this.capacity = capacity;
            queue = new T[capacity];
        }

        public int Count
        {
            get { return count; }
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
            if (count >= capacity)
            {
                return false;
            }
            queue[head] = item;
            ++head;
            if (head == capacity)
            {
                head = 0;
            }
            EnqueueCount = Interlocked.Increment(ref count);
            return true;
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
            while (TryDequeue(out item)) ;
        }

        public bool TryDequeue(out T item)
        {
            if (count == 0)
            {
                item = default(T);
                return false;
            }
            item = queue[tail];
            tail++;
            if (tail == capacity)
            {
                tail = 0;
            }
            DequeueCount = Interlocked.Decrement(ref count);
            return true;
        }

        public T Peek()
        {
            T item;
            if( !TryPeek(out item))
            {
                throw new ApplicationException("Queue is empty");
            }
            return item;
        }

        public bool TryPeek(out T item)
        {
            if (count == 0)
            {
                item = default(T);
                return false;
            }
            item = queue[tail];
            return true;
        }
    }
}