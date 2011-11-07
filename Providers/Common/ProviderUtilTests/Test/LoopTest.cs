using System.Collections.Generic;
using NUnit.Framework;
using TickZoom.Api;

namespace Test
{
    [TestFixture]
    public class LoopTest
    {
        private static Log log = Factory.SysLog.GetLogger(typeof(LoopTest));
		
        public class TestItem {
            public int Number;
        }
		
        [Test]
        public void ConfigFileTest() {
            string configFileName = @"./Test.config";
            var configFile = new ConfigFile(configFileName);
            var expected = "32";
            configFile.SetValue("Another/Simulate/MarkWalter",expected);
            var value = configFile.GetValue("Another/Simulate/MarkWalter");
            Assert.AreEqual(expected,value);
        }

        [Test]
        public void LinkedListTest() {
            ActiveList<TestItem> linked = new ActiveList<TestItem>();
            List<TestItem> list = new List<TestItem>();
            int size = 1000;
            int iterations = 100000;
            for( int i= 0; i< size; i++) {
                linked.AddLast(new TestItem());
                list.Add(new TestItem());
            }
            long startTime = Factory.TickCount;
            for( int j=0; j< iterations; j++) {
                for( int i= 0; i<list.Count; i++) {
                    TestItem item = list[i];
                    item.Number += 2;
                }
            }
            long endTime = Factory.TickCount;
            log.Notice("for list took " + (endTime - startTime));
			
            startTime = Factory.TickCount;
            for( int j=0; j< iterations; j++) {
                foreach( var item in list) {
                    item.Number += 2;
                }
            }
            endTime = Factory.TickCount;
            log.Notice("foreach list took " + (endTime - startTime));

            startTime = Factory.TickCount;
            for( int j=0; j< iterations; j++) {
                var next = linked.First;
                for (var current = next; current != null; current = next)
                {
                    next = current.Next;
                    var item = current.Value;
                    item.Number += 2;
                }
            }
            endTime = Factory.TickCount;
            log.Notice("foreach linked took " + (endTime - startTime));
			
            startTime = Factory.TickCount;
            for( int j=0; j< iterations; j++) {
                for( var node = linked.First; node != null; node = node.Next) {
                    node.Value.Number += 2;
                }
            }
            endTime = Factory.TickCount;
            log.Notice("for on linked took " + (endTime - startTime));
			
            startTime = Factory.TickCount;
            for( int j=0; j< iterations; j++) {
                var next = linked.First;
                for( var node = next; node != null; node = next) {
                    next = node.Next;
                    node.Value.Number += 2;
                }
            }
            endTime = Factory.TickCount;
            log.Notice("lambda on linked took " + (endTime - startTime));
        }
    }
}