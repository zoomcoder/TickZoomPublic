using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using TickZoom.Api;
using TickZoom.TickUtil;

namespace TickZoom.Utilities
{
    [TestFixture]
    public class FastQueueTest
    {
        private static readonly Log log = Factory.Log.GetLogger(typeof (FastQueueTest));
        private static readonly bool debug = log.IsDebugEnabled;
        private FastQueueImpl<int> producerQueue;
        private FastQueueImpl<int> queue;
        private int nextValue;
        private volatile bool stopThread;
        private Exception threadException;
        private int addCounter;
        private long removeCounter;
        private long readCounter;
        private long readFailureCounter;
        private long maxListCount;
        private Task producerTask;
        private Task writeTask;
        private Task readTask;

        [SetUp]
        public void Setup()
        {
            stopThread = false;
            queue = new FastQueueImpl<int>("TestQueue");
            producerQueue = new FastQueueImpl<int>("TestProducer");

            producerTask = Factory.Parallel.Loop("TestProducer", OnException, ProducerTask);
            producerTask.Scheduler = Scheduler.RoundRobin;
            producerQueue.ConnectOutbound(producerTask);

            writeTask = Factory.Parallel.Loop("TestWriter", OnException, WriteTask);
            writeTask.Scheduler = Scheduler.EarliestTime;
            producerQueue.ConnectInbound(writeTask);
            queue.ConnectOutbound(writeTask);

            readTask = Factory.Parallel.Loop("TestReader", OnException, ReadTask);
            readTask.Scheduler = Scheduler.EarliestTime;
            queue.ConnectInbound(readTask);

            nextValue = 0;
            addCounter = 0;
            removeCounter = 0;
            readCounter = 0;
            readFailureCounter = 0;
            maxListCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            if( producerTask != null)
            {
                producerTask.Stop();
                producerTask.Join();
            }

            if( writeTask != null)
            {
                writeTask.Stop();
                writeTask.Join();
            }

            if( readTask != null)
            {
                readTask.Stop();
                readTask.Join();
            }
        }

        private void OnException( Exception ex)
        {
            threadException = ex;
        }

        private bool produceFlag = true;
        private Yield ProducerTask()
        {
            //log.Info("Producer: Producing item " + addCounter);
            var result = Yield.NoWork.Repeat;
            if (produceFlag)
            {
                var count = 0;
                while (count < 10 && producerQueue.TryEnqueue(addCounter, 0L))
                {
                    ++addCounter;
                    ++count;
                    result = Yield.DidWork.Repeat;
                }
                if (producerQueue.Count > 600)
                {
                    produceFlag = false;
                }
            } 
            else if (producerQueue.Count < 300)
            {
                produceFlag = true;
            }

            return result;
        }

        private Yield WriteTask()
        {
            int item;
            producerQueue.Dequeue(out item);
            //log.Info("Writer: Transfering item " + item);
            queue.Enqueue(item, 0L);
            if (queue.Count > maxListCount)
            {
                maxListCount = queue.Count;
            }
            return Yield.DidWork.Repeat;
        }

        private Yield ReadTask()
        {
            int item;
            queue.Dequeue(out item);
            //log.Info("Reader: Read item " + item);
            if (item != readCounter)
            {
                Assert.AreEqual(readCounter, item);
            }
            ++readCounter;
            if( readCounter % 100000 == 0)
            {
                log.Info("Read " + readCounter);
            }
            return Yield.DidWork.Repeat;
        }

        //[Test]
        //public void TestAddFirst()
        //{
        //    AddToList();
        //    Assert.AreEqual(1, queue.Count);
        //    AddToList();
        //    Assert.AreEqual(2, queue.Count);
        //    AddToList();
        //    Assert.AreEqual(3, queue.Count);
        //    AddToList();
        //    Assert.AreEqual(4, queue.Count);
        //    AddToList();
        //    Assert.AreEqual(5, queue.Count);
        //}

        [Test]
        public void MemoryStreamExperiment()
        {
            var memory = new MemoryStream();
            memory.SetLength(181);
            memory.Position = 0;
            var pos = memory.Position;
        }

        //[Test]
        //public void TestEnqueue()
        //{
        //    queue.Enqueue(3, 0L);
        //    Assert.AreEqual(1, queue.Count);
        //    queue.Enqueue(2, 0L);
        //    Assert.AreEqual(2, queue.Count);
        //    queue.Enqueue(5, 0L);
        //    Assert.AreEqual(3, queue.Count);
        //    queue.Enqueue(6, 0L);
        //    Assert.AreEqual(4, queue.Count);
        //    queue.Enqueue(1, 0L);
        //    int value;
        //    Assert.AreEqual(5, queue.Count);
        //    queue.Dequeue(out value);
        //    Assert.AreEqual(3, value);
        //    queue.Dequeue(out value);
        //    Assert.AreEqual(2, value);
        //    queue.Dequeue(out value);
        //    Assert.AreEqual(5, value);
        //    queue.Dequeue(out value);
        //    Assert.AreEqual(6, value);
        //    queue.Dequeue(out value);
        //    Assert.AreEqual(1, value);
        //}

        //[Test]
        //public void TestRemoveWhileReading()
        //{
        //    for (var i = 0; i < 50; i++)
        //    {
        //        queue.Enqueue(Interlocked.Increment(ref nextValue),0L);
        //    }
        //    var item = 0;
        //    while( queue.Count > 0)
        //    {
        //        queue.Dequeue(out item);
        //        if (item == 35)
        //        {
        //            break;
        //        }
        //    }
        //    Assert.AreEqual(35, item, "found item");
        //}

        //[Test]
        //public void TestAddThread()
        //{
        //    for (var i = 0; i < 1000; i++)
        //    {
        //        queue.Enqueue((int)Interlocked.Increment(ref addCounter), 0L);
        //    }
        //    Assert.AreEqual(addCounter, 1000, "add counter");
        //    Console.Out.WriteLine("addCounter " + addCounter);
        //    try
        //    {
        //        queue.Enqueue((int)Interlocked.Increment(ref addCounter), 0L);
        //        Assert.Fail("expected queue is full exception");
        //    } catch( ApplicationException)
        //    {
        //        // queue is full
        //    }
        //}

        //[Test]
        //public void TestReaderThread()
        //{
        //    for (var i = 0; i < 900; i++)
        //    {
        //        queue.Enqueue(nextValue, 0L);
        //        ++nextValue;
        //    }
        //    var readThread = new Thread(ReadFromListLoop);
        //    readThread.Start();
        //    Thread.Sleep(5000);
        //    stopThread = true;
        //    readThread.Join();
        //    if (threadException != null)
        //    {
        //        throw new Exception("Thread failed: ", threadException);
        //    }
        //    Assert.AreEqual(readCounter, 900, "remove counter");
        //    Console.Out.WriteLine("readCounter " + readCounter);
        //}

        //[Test]
        //public void TestRemoveThread()
        //{
        //    for (var i = 0; i < 1000; i++)
        //    {
        //        queue.Enqueue(Interlocked.Increment(ref nextValue),0L);
        //    }
        //    var removeThread = new Thread(DequeueFromListLoop);
        //    removeThread.Start();
        //    Thread.Sleep(5000);
        //    stopThread = true;
        //    removeThread.Join();
        //    if (threadException != null)
        //    {
        //        throw new Exception("Thread failed: ", threadException);
        //    }
        //    Assert.AreEqual(removeCounter, 1000, "remove counter");
        //    Console.Out.WriteLine("removeCounter " + removeCounter);
        //}


        [Test]
        public void TestReaderWriterSafety()
        {
            producerQueue.ConnectInbound(writeTask);
            queue.ConnectInbound(readTask);
            producerTask.Start();
            writeTask.Start();
            readTask.Start();
            Thread.Sleep(5000);
            producerTask.Stop();
            producerTask.Join();
            Thread.Sleep(1000);
            writeTask.Stop();
            writeTask.Join();
            readTask.Stop();
            readTask.Join();
            //Assert.AreEqual(0, producerTask., "producer inbound");
            //Assert.AreEqual(0, producerTask.OutboundCounter, "producer outbound");
            //Assert.AreEqual(0, writeTask.InboundCounter, "writer inbound");
            //Assert.AreEqual(0, writeTask.OutboundCounter, "writer outbound");
            //Assert.AreEqual(0, readTask.InboundCounter, "reader inbound");
            //Assert.AreEqual(0, readTask.InboundCounter, "reader outbound");
            //Assert.AreEqual(0, readTask.OutboundCounter);
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Assert.Less(readFailureCounter, 5, "read failure");
            Assert.Greater(readCounter, 4000, "read counter");
            Assert.Greater(addCounter, 4000, "add counter");
            Console.Out.WriteLine("readFailure " + readFailureCounter);
            Console.Out.WriteLine("readCounter " + readCounter);
            Console.Out.WriteLine("addCounter " + addCounter);
            Console.Out.WriteLine("removeCounter " + removeCounter);
            Console.Out.WriteLine("maxListCount " + maxListCount);
            Console.Out.WriteLine("final count " + queue.Count);
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
            if (queue.Count == 0)
            {
                return;
            }
            else
            {
                int item;
                while (queue.TryDequeue(out item))
                {
                    if (item != readCounter)
                    {
                        Assert.AreEqual(readCounter, item);
                    }
                    ++readCounter;
                }
            }
        }

        public void AddToList()
        {
            if( queue.TryEnqueue(addCounter, 0L))
            {
                var count = queue.Count;
                if (queue.Count > maxListCount)
                {
                    maxListCount = count;
                }
                Interlocked.Increment(ref addCounter);
            }
        }

        public void DequeueFromListLoop()
        {
            try
            {
                while (!stopThread)
                {
                    DequeueFromList();
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref threadException, ex);
            }
        }

        public void DequeueFromList()
        {
            if (queue.Count == 0)
            {
                return;
            }
            else
            {
                int item;
                while( queue.TryDequeue(out item)) {

                    Interlocked.Increment(ref removeCounter);
                }
            }
        }
    }
}