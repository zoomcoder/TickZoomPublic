using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NUnit.Framework;
using TickZoom.TickUtil;

namespace TickZoom.Utilities
{
    [TestFixture]
    public class ActiveQueueTest
    {
        private ActiveQueue<FastQueueEntry<int>> list;
        private int nextValue = 0;
        private Exception threadException;
        private int addCounter;
        private long removeCounter;
        private long readCounter;
        private long addFailureCounter;
        private long readFailureCounter;
        private Random random = new Random();
        private long maxListCount = 0;

        [SetUp]
        public void Setup()
        {
            list = new ActiveQueue<FastQueueEntry<int>>();
            nextValue = 0;
            addCounter = 0;
            removeCounter = 0;
            readCounter = 0;
            addFailureCounter = 0;
            readFailureCounter = 0;
            maxListCount = 0;
        }

        [Test]
        public void TestAddFirst()
        {
            AddToList();
            Assert.AreEqual(1, list.Count);
            AddToList();
            Assert.AreEqual(2, list.Count);
            AddToList();
            Assert.AreEqual(3, list.Count);
            AddToList();
            Assert.AreEqual(4, list.Count);
            AddToList();
            Assert.AreEqual(5, list.Count);
        }

        [Test]
        public void MemoryStreamExperiment()
        {
            var memory = new MemoryStream();
            memory.SetLength(181);
            memory.Position = 0;
            var pos = memory.Position;
        }

        [Test]
        public void TestEnqueue()
        {
            list.Enqueue(new FastQueueEntry<int>(3,0L));
            Assert.AreEqual(1, list.Count);
            list.Enqueue(new FastQueueEntry<int>(2, 0L));
            Assert.AreEqual(2, list.Count);
            list.Enqueue(new FastQueueEntry<int>(5,0L));
            Assert.AreEqual(3, list.Count);
            list.Enqueue(new FastQueueEntry<int>(6,0L));
            Assert.AreEqual(4, list.Count);
            list.Enqueue(new FastQueueEntry<int>(1,0L));
            Assert.AreEqual(5, list.Count);
            var value = list.Dequeue();
            Assert.AreEqual(3,value.Entry);
            value = list.Dequeue();
            Assert.AreEqual(2, value.Entry);
            value = list.Dequeue();
            Assert.AreEqual(5, value.Entry);
            value = list.Dequeue();
            Assert.AreEqual(6, value.Entry);
            value = list.Dequeue();
            Assert.AreEqual(1, value.Entry);
        }

        [Test]
        public void TestRemoveWhileReading()
        {
            for (var i = 0; i < 50; i++)
            {
                list.Enqueue(new FastQueueEntry<int>(++nextValue,0L));
            }
            FastQueueEntry<int> item = default(FastQueueEntry<int>);
            while (list.Count > 0)
            {
                item = list.Dequeue();
                if (item.Entry == 35)
                {
                    break;
                }
            }
            Assert.AreEqual(35, item.Entry, "found item");
        }

        [Test]
        public void TestAddThread()
        {
            for (var i = 0; i < 1000; i++)
            {
                list.Enqueue(new FastQueueEntry<int>(++addCounter,0L));
            }
            Assert.AreEqual(addCounter, 1000, "add counter");
            Console.Out.WriteLine("addCounter " + addCounter);
            try
            {
                list.Enqueue(new FastQueueEntry<int>(++addCounter,0L));
                Assert.Fail("expected queue is full exception");
            }
            catch (ApplicationException)
            {
                // queue is full
            }
        }

        [Test]
        public void TestReaderThread()
        {
            for (var i = 0; i < 900; i++)
            {
                list.Enqueue(new FastQueueEntry<int>(nextValue,0L));
                ++nextValue;
            }
            ReadFromList2();
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Assert.AreEqual(readCounter, 900, "remove counter");
            Console.Out.WriteLine("readCounter " + readCounter);
        }

        [Test]
        public void TestReaderWriterSafety()
        {
            for (var i = 0; i < 100; i++)
            {
                list.Enqueue(new FastQueueEntry<int>(addCounter,0L));
                ++addCounter;
            }

            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }

            var stopWatch = new Stopwatch();
            stopWatch.Start();
            for (var i = 0L; i < 10000L; i++)
            {
                for( var j=0; j<1000; j++)
                {
                    AddToList();
                }
                ReadFromList2();
            }
            var elapsed = stopWatch.ElapsedMilliseconds;

            Assert.Less(readFailureCounter, 5, "read failure");
            Console.Out.WriteLine("readFailure " + readFailureCounter);
            Console.Out.WriteLine("readCounter " + readCounter);
            Console.Out.WriteLine("addCounter " + addCounter);
            Console.Out.WriteLine("removeCounter " + removeCounter);
            Console.Out.WriteLine("maxListCount " + maxListCount);
            Console.Out.WriteLine("final count " + list.Count);
            Console.Out.WriteLine("Elapsed time " + elapsed + "ms");
            Console.Out.WriteLine("Items per ms " + (readCounter/elapsed));
        }


        public bool ReadFromList2()
        {
            var result = false;
            if (list.Count == 0)
            {
                return result;
            }
            else
            {
                FastQueueEntry<int> item;
                while (list.TryDequeue(out item))
                {
                    if( item.Entry != readCounter)
                    {
                        Assert.AreEqual(readCounter, item.Entry);
                    }
                    ++readCounter;
                    result = true;
                }
            }
            return result;
        }

        public void AddToList()
        {
            var entry = default(FastQueueEntry<int>);
            entry.Entry = addCounter;
            entry.utcTime = 0L;
            if (list.TryEnqueue(entry)) 
            {
                var count = list.Count;
                if (list.Count > maxListCount)
                {
                    maxListCount = count;
                }
                ++addCounter;
            }
        }

    }
}