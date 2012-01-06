using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace TickZoom.Utilities.TickZoom.Utilities
{
    [TestFixture]
    public class MultiCoreTest
    {
        public class ThreadTester 
        {
            private Thread speedThread;
            private static long[] staticArray = new long[2048];
            private long addCounter;
            private long readCounter;
            private long performanceCounter;
            internal Exception threadException;
            private int count;
            private int index;
            private long endCount = 200000000L;
            public ThreadTester(int count)
            {
                this.count = count;
                this.index = count*256;
                speedThread = new Thread(SpeedTestLoop);
            }

            public void Run()
            {
                speedThread.Start();
            }

            public void Join()
            {
                speedThread.Join();
                if (threadException != null)
                {
                    throw new Exception("Thread failed: ", threadException);
                }
            }

            public long Output()
            {
                return performanceCounter;
            }

            private void SpeedTestLoop()
            {
                try
                {
                    while (true)
                    {
                        for (var i = 0; i < 500; i++)
                        {
                            ++staticArray[index];
                            ++addCounter;
                        }
                        for (var i = 0; i < 500; i++)
                        {
                            ++readCounter;
                            ++performanceCounter;
                            if( performanceCounter > endCount)
                            {
                                return;
                            }
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

        [SetUp]
        public void Setup()
        {
            threadTesters = new ThreadTester[4];
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
            //TestCores(3);
        }

        [Test]
        public void QuadCoreTest()
        {
            //TestCores(4);
        }

        public void TestCores(int numCores)
        {
            for( var i=0; i<numCores; i++)
            {
                threadTesters[i] = new ThreadTester(i + 1);
                var tester = threadTesters[i];
                tester.Run();
            }
            var stopwatch = Stopwatch.StartNew();

            var sum = 0L;
            for (var i = 0; i < numCores; i++)
            {
                var tester = threadTesters[i];
                tester.Join();
                sum += tester.Output();
            }
            var milliseconds = stopwatch.ElapsedMilliseconds;
            var result = (double)sum/milliseconds;
            Console.Out.WriteLine("Milliseconds " + milliseconds + ", operations per millisecond " + result);
        }

    }
}