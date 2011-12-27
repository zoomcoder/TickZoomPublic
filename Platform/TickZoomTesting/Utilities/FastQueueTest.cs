using System;
using System.Diagnostics;
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
            if (item != readCounter)
            {
                Assert.AreEqual(readCounter, item);
            }
            ++readCounter;
            var iterations = 1000000L;
            iterations = 1000000000L;
            if( readCounter > iterations)
            {
                return Yield.Terminate;
            }
            if( readCounter % 100000 == 0)
            {
                log.Info("Read " + readCounter);
            }
            return Yield.DidWork.Repeat;
        }

        [Test]
        public void TestReaderWriterSafety()
        {
            producerQueue.ConnectInbound(writeTask);
            queue.ConnectInbound(readTask);
            producerTask.Start();
            writeTask.Start();
            readTask.Start();
            var sw = new Stopwatch();
            sw.Start();
            readTask.Join();
            var elapsed = sw.ElapsedMilliseconds;
            producerTask.Stop();
            producerTask.Join();
            Thread.Sleep(1000);
            writeTask.Stop();
            writeTask.Join();
            if (threadException != null)
            {
                throw new Exception("Thread failed: ", threadException);
            }
            Assert.Less(readFailureCounter, 5, "read failure");
            Console.Out.WriteLine("readFailure " + readFailureCounter);
            Console.Out.WriteLine("readCounter " + readCounter);
            Console.Out.WriteLine("addCounter " + addCounter);
            Console.Out.WriteLine("removeCounter " + removeCounter);
            Console.Out.WriteLine("maxListCount " + maxListCount);
            Console.Out.WriteLine("final count " + queue.Count);
            Console.Out.WriteLine("Elapsed time " + elapsed + "ms");
            Console.Out.WriteLine("Items per ms " + (readCounter/elapsed));
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