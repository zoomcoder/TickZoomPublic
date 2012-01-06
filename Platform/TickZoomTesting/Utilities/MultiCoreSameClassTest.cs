using System;
using System.Threading;
using NUnit.Framework;

namespace TickZoom.Utilities.TickZoom.Utilities
{
    [TestFixture]
    public class MultiCoreSameClassTest
    {
        private ThreadTester threadTester;
        public class ThreadTester
        {
            private Thread[] speedThread = new Thread[4];
            private long[] addCounter = new long[4];
            private long[] readCounter = new long[4];
            internal Exception threadException;
            internal volatile bool stopThread;
            private int count;

            public ThreadTester(int count)
            {
                for( var i=0; i<speedThread.Length; i++)
                {
                    speedThread[i] = new Thread(SpeedTestLoop);
                }
                this.count = count;
            }

            public void Run()
            {
                for (var i = 0; i < count; i++)
                {
                    speedThread[i].Start(i);
                }
            }

            public void Stop()
            {
                stopThread = true;
                for (var i = 0; i < count; i++)
                {
                    speedThread[i].Join();
                }
                if (threadException != null)
                {
                    throw new Exception("Thread failed: ", threadException);
                }
            }

            public void Output()
            {
                var readSum = 0L;
                var addSum = 0L;
                for (var i = 0; i < count; i++)
                {
                    readSum += readCounter[i];
                    addSum += addCounter[i];
                }
                Console.Out.WriteLine("Thread readCounter " + readSum + ", addCounter " + addSum);
            }

            private void SpeedTestLoop(object indexarg)
            {
                var index = (int) indexarg;
                try
                {
                    while (!stopThread)
                    {
                        for (var i = 0; i < 500; i++)
                        {
                            ++addCounter[index];
                        }
                        for (var i = 0; i < 500; i++)
                        {
                            ++readCounter[index];
                        }
                    }
                }
                catch (Exception ex)
                {
                    threadException = ex;
                }
            }
        }

        [SetUp]
        public void Setup()
        {
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
            threadTester = new ThreadTester(numCores);
            threadTester.Run();
            Thread.Sleep(5000);
            threadTester.Stop();
            threadTester.Output();
        }
    }
}