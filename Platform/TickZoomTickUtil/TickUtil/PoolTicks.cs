using System;
using System.Collections.Generic;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public class PoolTicks : Pool<TickBinaryBox>
    {
        private static Log log = Factory.Log.GetLogger(typeof (PoolTicks));
        private int _itemsCapacity = 10;
        private int count = 0;
        private ActiveQueue<TickBinaryBox> _items = new ActiveQueue<TickBinaryBox>(10000);
        private static int nextPoolId = 0;
        private long nextTickId = 0;

        public PoolTicks()
        {
            var id = Interlocked.Increment(ref nextPoolId);
        }

        private TickBinaryBox CreateInternal()
        {
            Interlocked.Increment(ref count);
            var box = new TickBinaryBox(this);
            box.TickBinary.Id = Interlocked.Increment(ref nextTickId);
            return box;
        }

        public TickBinaryBox Create()
        {
            if (_items.Count < 100)
            {
                return CreateInternal();
            }
            TickBinaryBox box;
            if( _items.TryDequeue(out box))
            {
                box.ResetReference();
                return box;
            }
            return CreateInternal();
        }

        public void Free(TickBinaryBox item)
        {
            if( item.TickBinary.Id == 0)
            {
                throw new InvalidOperationException("TickBinary id must be non-zero to be freed.");
            }
            _items.Enqueue(item);
        }

        public void Clear()
        {
            _items.Clear();
        }
		
        public int Count {
            get { return count; }
        }
    }
}