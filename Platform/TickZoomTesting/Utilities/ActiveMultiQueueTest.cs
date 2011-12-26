using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using TickZoom.TickUtil;

namespace TickZoom.Utilities
{
    using System;
    using System.IO;
    using System.Threading;
    using NUnit.Framework;

    [TestFixture]
    public class ActiveMultiQueueTest
    {
        private ActiveMultiQueue<FastQueueEntry<int>> list;
        private ActiveMultiQueue<FastQueueEntry<int>> list2;
        private int nextValue;
        private volatile bool stopThread;
        private Exception threadException;
        private long removeCounter;
        private long addCounter;
        private long readCounter;
        private long addCounter2;
        private long readCounter2;
        private long readFailureCounter;
        private long addFailureCounter;
        private long maxListCount;
        private int[] outputValues = new int[1000000];

        [SetUp]
        public void Setup()
        {
            stopThread = false;
            list = new ActiveMultiQueue<FastQueueEntry<int>>();
            list2 = new ActiveMultiQueue<FastQueueEntry<int>>();
            nextValue = 0;
            addCounter = 0;
            removeCounter = 0;
            readCounter = 0;
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
            list.Enqueue(new FastQueueEntry<int>(3, 0L));
            Assert.AreEqual(1, list.Count);
            list.Enqueue(new FastQueueEntry<int>(2, 0L));
            Assert.AreEqual(2, list.Count);
            list.Enqueue(new FastQueueEntry<int>(5, 0L));
            Assert.AreEqual(3, list.Count);
            list.Enqueue(new FastQueueEntry<int>(6, 0L));
            Assert.AreEqual(4, list.Count);
            list.Enqueue(new FastQueueEntry<int>(1, 0L));
            Assert.AreEqual(5, list.Count);
            var value = list.Dequeue();
            Assert.AreEqual(3, value.Entry);
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
                list.Enqueue(new FastQueueEntry<int>(++nextValue, 0L));
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
                list.Enqueue(new FastQueueEntry<int>((int)++addCounter, 0L));
            }
            Assert.AreEqual(addCounter, 1000, "add counter");
            Console.Out.WriteLine("addCounter " + addCounter);
            try
            {
                list.Enqueue(new FastQueueEntry<int>((int)++addCounter, 0L));
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
                list.Enqueue(new FastQueueEntry<int>(nextValue, 0L));
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
            Assert.AreEqual(500,readCounter, "remove counter");
            Console.Out.WriteLine("readCounter " + readCounter);
        }

        [Test]
        public void TestReaderWriterSpeed()
        {
            var speedThread = new Thread(SpeedTestLoop2);
            speedThread.Name = "Speed Test";
            speedThread.Start();
            Thread.Sleep(5000);
            stopThread = true;
            speedThread.Join();
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Console.Out.WriteLine("readCounter " + readCounter);
            Console.Out.WriteLine("readCounter2 " + readCounter2);
            Console.Out.WriteLine("total readCounter " + (readCounter + readCounter2));
            Console.Out.WriteLine("addCounter " + addCounter);
            Console.Out.WriteLine("addCounter2 " + addCounter2);
            for (var i = 0; i < readCounter && i < outputValues.Length; i++)
            {
                if( outputValues[i] != i)
                {
                    Assert.AreEqual(i,outputValues[i],"output value " + i);
                }
            }
        }

        [Test]
        public void TestReaderWriterSpeed2Thread()
        {
            var speedThread1 = new Thread(SpeedTestLoop);
            speedThread1.Name = "Speed Test 1";
            var speedThread2 = new Thread(SpeedTestLoop2);
            speedThread2.Name = "Speed Test 2";
            speedThread1.Start();
            speedThread2.Start();
            Thread.Sleep(5000);
            stopThread = true;
            speedThread1.Join();
            speedThread2.Join();
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Console.Out.WriteLine("readCounter " + readCounter);
            Console.Out.WriteLine("readCounter2 " + readCounter2);
            Console.Out.WriteLine("totoal readCounter " + (readCounter + readCounter2));
            Console.Out.WriteLine("addCounter " + addCounter);
            Console.Out.WriteLine("addCounter2 " + addCounter2);
            Console.Out.WriteLine("readFailure" + readFailureCounter);
            Console.Out.WriteLine("addFailure" + addFailureCounter);
            for (var i = 0; i < 1000 && i < outputValues.Length; i++)
            {
                Console.Out.WriteLine(i);
            }
        }

        private void SpeedTestLoop()
        {
            try
            {
                var input = default(FastQueueEntry<int>);
                var output = default(FastQueueEntry<int>);
                while (!stopThread)
                {
                    for (var i = 0; i < 500; )
                    {
                        //input.Entry = addCounter;
                        //if (list.TryEnqueue(connection, input))
                        //{
                            ++addCounter;
                            i++;
                        //}
                        //else
                        //{
                        //    ++addFailureCounter;
                        //}
                    }
                    for (var i = 0; i < 500; )
                    {
                        //if (list.TryDequeue(connection, out output))
                        //{
                            var index = readCounter++;
                        //    if (index < outputValues.Length)
                        //    {
                        //        outputValues[index] = output.Entry;
                        //    }
                            i++;
                        //}
                        //else
                        //{
                        //    ++readFailureCounter;
                        //}
                    }
                }
            }
            catch( Exception ex)
            {
                threadException = ex;
            }
        }

        private void SpeedTestLoop2()
        {
            try
            {
                var input = default(FastQueueEntry<int>);
                var output = default(FastQueueEntry<int>);
                while (!stopThread)
                {
                    for (var i = 0; i < 500; )
                    {
                        //input.Entry = addCounter;
                        //if (list2.TryEnqueue(connection, input))
                        //{
                            ++addCounter2;
                            i++;
                        //}
                        //else
                        //{
                        //    ++addFailureCounter;
                        //}
                    }
                    for (var i = 0; i < 500; )
                    {
                        //if (list2.TryDequeue(connection, out output))
                        //{
                            var index = readCounter2++;
                        //    if (index < outputValues.Length)
                        //    {
                        //        outputValues[index] = output.Entry;
                        //    }
                            i++;
                        //}
                        //else
                        //{
                        //    ++readFailureCounter2;
                        //}
                    }
                }
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        }

        [Test]
        public void TestReaderWriterSafety()
        {

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
                    if (item.Entry != readCounter)
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
            entry.Entry = (int)addCounter;
            entry.utcTime = 0L;
            while (list.TryEnqueue(entry))
            {
                var count = list.Count;
                if (list.Count > maxListCount)
                {
                    maxListCount = count;
                }
                ++addCounter;
                entry.Entry = (int)addCounter;
            }
        }

    }
}