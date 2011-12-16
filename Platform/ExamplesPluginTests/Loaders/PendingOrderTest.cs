using System;
using NUnit.Framework;
using TickZoom.Api;
using TickZoom.Common;
using TickZoom.Examples;

namespace Loaders
{
    [TestFixture]
    public class PendingOrderTest 
    {
        Log log = Factory.SysLog.GetLogger(typeof(PendingOrderTest));
        private StrategyBaseTest strategyBaseTest;
        public PendingOrderTest()
        {
            var settings = new AutoTestSettings
                               {
                                   Mode = AutoTestMode.SimulateFIX,
                                   Name = "ExampleReversalTest",
                                   Loader = new ExampleReversalLoader(),
                                   Symbols = "TestPending",
                                   StoreKnownGood = false,
                                   ShowCharts = false,
                                   StartTime = new TimeStamp(1800, 1, 1),
                                   EndTime = new TimeStamp(2009, 6, 10),
                                   IntervalDefault = Intervals.Minute1,
                               };
            strategyBaseTest = new StrategyBaseTest( settings);
        }

        [TestFixtureSetUp]
        public void RunStrategy()
        {
            Assert.Ignore();
            try
            {
                strategyBaseTest.RunStrategy();
            }
            catch (Exception ex)
            {
                log.Error("Setup error.", ex);
                throw;
            }
        }

        [Test]
        public void CheckForException()
        {
            
        }

    }
}