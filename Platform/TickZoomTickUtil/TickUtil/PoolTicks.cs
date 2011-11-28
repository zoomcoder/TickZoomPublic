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
        private Stack<TickBinaryBox> _items = new Stack<TickBinaryBox>(10);
        private SimpleLock _sync = new SimpleLock();
        private int count = 0;
        private ActiveList<TickBinaryBox> _freed = new ActiveList<TickBinaryBox>();
        private int enqueDiagnoseMetric;
        private int pushDiagnoseMetric;
        private static int nextPoolId = 0;
        private long nextTickId = 0;

        public PoolTicks()
        {
            var id = Interlocked.Increment(ref nextPoolId);
            enqueDiagnoseMetric = Diagnose.RegisterMetric("PoolTicks-Enqueue-"+id);
            pushDiagnoseMetric = Diagnose.RegisterMetric("PoolTicks-Push-"+id);
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
            if (_items.Count == 0)
            {
                return CreateInternal();
            }
            using (_sync.Using())
            {
                if( _items.Count > 0)
                {
                    var result = _items.Pop();
                    result.ResetReference();
                    return result;
                }
            }
            return CreateInternal();
        }

        public void Free(TickBinaryBox item)
        {
            if( item.TickBinary.Id == 0)
            {
                throw new InvalidOperationException("TickBinary id must be non-zero to be freed.");
            }
            if (Diagnose.TraceTicks)
            {
                var binary = item.TickBinary;
                Diagnose.AddTick(enqueDiagnoseMetric,ref binary);
            }
            var node = new ActiveListNode<TickBinaryBox>(item);
            using (_sync.Using())
            {
                _freed.AddFirst(node);
            }
            if (_freed.Count > 10)
            {
                Stack<TickBinaryBox> newItems = null;
                if( _items.Count+1 >= _itemsCapacity)
                {
                    _itemsCapacity *= 2;
                    newItems = new Stack<TickBinaryBox>(_itemsCapacity);
                    log.Info("Capacity increased to " + _itemsCapacity);
                }
                using (_sync.Using())
                {
                    if (newItems != null)
                    {
                        while (_items.Count > 0)
                        {
                            newItems.Push(_items.Pop());
                        }
                        _items = newItems;
                    }
                    var freed = _freed.RemoveLast().Value;
                    _items.Push(freed);
                }
            }
        }

        public void Clear()
        {
            using(_sync.Using()) {
                _items.Clear();
            }
        }
		
        public int Count {
            get { return count; }
        }
    }
}