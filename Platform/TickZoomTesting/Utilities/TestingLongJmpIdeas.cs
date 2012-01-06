using System;
using System.Threading;
using Fibers;
using NUnit.Framework;

namespace TickZoom.Utilities
{
    public class FiberTask : Fiber 
    {
        public FiberTask()
        {

        }
        public FiberTask(FiberTask mainTask)
            : base(mainTask)
        {
        
        }

        protected override void Run()
        {
            while (true)
            {
                Console.WriteLine("Top of worker loop.");
                try
                {
                    Work();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception: " + ex.Message);
                }
                Console.WriteLine("After the exception.");
                Work();
            }
        }

        private void Work()
        {
            Console.WriteLine("Doing work on fiber: " + GetHashCode() + ", thread id: " + Thread.CurrentThread.ManagedThreadId);
            ++counter;
            Console.WriteLine("Incremented counter " + counter);
            if (counter == 2)
            {
                Console.WriteLine("Throwing an exception.");
                throw new InvalidCastException("Just a test exception.");
            }
            Yield();
        }

        public static int counter;
    }

    [TestFixture]
    public class TestingFibers
    {
        [Test]
        public void TestIdeas()
        {
            var fiberTasks = new System.Collections.Generic.List<FiberTask>();
            var mainFiber = new FiberTask();
            for( var i=0; i< 5; i++)
            {
                fiberTasks.Add(new FiberTask(mainFiber));
            }
            for (var i = 0; i < fiberTasks.Count; i++)
            {
                Console.WriteLine("Resuming " + i);
                var fiberTask = fiberTasks[i];
                if( !fiberTask.Resume())
                {
                    Console.WriteLine("Fiber " + i + " was disposed.");
                    fiberTasks.RemoveAt(i);
                    i--;
                }
            }
            for (var i = 0; i < fiberTasks.Count; i++)
            {
                Console.WriteLine("Disposing " + i);
                fiberTasks[i].Dispose();
            }
        }
    }
}