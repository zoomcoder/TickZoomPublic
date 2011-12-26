using System;
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
        private volatile bool stopThread = false;
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
            stopThread = false;
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
            var readThread = new Thread(ReadFromListLoop);
            readThread.Start();
            Thread.Sleep(5000);
            stopThread = true;
            readThread.Join();
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

            var addThread = new Thread(AddToListLoop);
            addThread.Name = "Test Adding";
            var readThread = new Thread(ReadFromListLoop);
            readThread.Name = "Test Reading";
            addThread.Start();
            readThread.Start();
            Thread.Sleep(5000);
            stopThread = true;
            addThread.Join();
            readThread.Join();
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Assert.Less(readFailureCounter, 5, "read failure");
            Assert.Greater(readCounter, 1000000, "read counter");
            Assert.Greater(addCounter, 4000, "add counter");
            Console.Out.WriteLine("readFailure " + readFailureCounter);
            Console.Out.WriteLine("readCounter " + readCounter);
            Console.Out.WriteLine("addCounter " + addCounter);
            Console.Out.WriteLine("removeCounter " + removeCounter);
            Console.Out.WriteLine("maxListCount " + maxListCount);
            Console.Out.WriteLine("final count " + list.Count);
        }

        public void AddToListLoop()
        {
            try
            {
                while (!stopThread)
                {
                    AddToList();
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref threadException, ex);
            }
        }

        public void ReadFromListLoop()
        {
            try
            {
                while (!stopThread)
                {
                    ReadFromList2();
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref threadException, ex);
            }
        }

        public void ReadFromList2()
        {
            if (list.Count == 0)
            {
                return;
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
                }
            }
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