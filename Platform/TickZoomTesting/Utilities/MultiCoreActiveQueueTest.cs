using System;
using System.Threading;
using NUnit.Framework;
using TickZoom.TickUtil;

namespace TickZoom.Utilities.TickZoom.Utilities
{
    public class SimpleClass<T>
    {
        public void Foo()
        {
            
        }

        public void Bar()
        {
            
        }
    }
    [TestFixture]
    public class MultiCoreActiveQueueTest
    {
        public class ThreadTester
        {
            private Thread speedThread;
            private static long addCounter;
            private static long readCounter;
            internal Exception threadException;
            internal volatile bool stopThread;
            private int count;
            private ActiveMultiQueue<long> queue;
            public ThreadTester(int count, ActiveMultiQueue<long> queue)
            {
                this.count = count;
                this.queue = queue;
                speedThread = new Thread(SpeedTestLoop);
            }

            public void Run()
            {
                speedThread.Start();
            }

            public void Stop()
            {
                stopThread = true;
                speedThread.Join();
                if (threadException != null)
                {
                    throw new Exception("Thread failed: ", threadException);
                }
            }

            public long Output()
            {
                return readCounter;
            }

            private void SpeedTestLoop()
            {
                try
                {
                    while (!stopThread)
                    {
                        for (var i = 0; i < 500; i++)
                        {
                            var counter = Interlocked.Increment(ref addCounter) - 1;
                            queue.TryEnqueue(addCounter);
                            //++addCounter;
                        }
                        for (var i = 0; i < 500; i++)
                        {
                            var counter = Interlocked.Increment(ref readCounter) - 1;
                            long value;
                            queue.TryDequeue(out value);
                            //++readCounter;
                        }
                    }
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            }
        }

        private ThreadTester[] threadTesters;
        private ActiveMultiQueue<long> queue;

        [SetUp]
        public void Setup()
        {
            queue = new ActiveMultiQueue<long>();
            threadTesters = new ThreadTester[4];
            for (var i = 0; i < threadTesters.Length; i++)
            {
                threadTesters[i] = new ThreadTester(i + 1,queue);
            }
        }


        [Test]
        public void SingleCoreTest()
        {
            TestCores(1);
        }

        [Test]
        public void DualCoreTest()
        {
            TestCores(2);
        }

        [Test]
        public void TriCoreTest()
        {
            TestCores(3);
        }

        [Test]
        public void QuadCoreTest()
        {
            TestCores(4);
        }

        public void TestCores(int numCores)
        {
            for (var i = 0; i < numCores; i++)
            {
                var tester = threadTesters[i];
                tester.Run();
            }
            Thread.Sleep(5000);
            var sum = 0L;
            for (var i = 0; i < numCores; i++)
            {
                var tester = threadTesters[i];
                tester.Stop();
                sum += tester.Output();
            }
            Console.Out.WriteLine("Total readCounter " + sum);
        }

    }
}