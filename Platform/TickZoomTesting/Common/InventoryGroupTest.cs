using System;
using NUnit.Framework;
using TickZoom.Api;
using System.Windows.Forms;
using System.Text;
using System.IO;
using System.Collections.Generic;

namespace TickZoom.Common
{
    [TestFixture]
    public class InventoryGroupTest
    {
        private SymbolInfo symbol = Factory.Symbol.LookupSymbol("MSFT");
        Log log = Factory.SysLog.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private PriceChange[] priceChanges;
        [SetUp]
        public void TestSetup()
        {
            var lines = File.ReadAllLines(@"..\..\Platform\TickZoomTesting\Common\EURUSD.data");
            priceChanges = new PriceChange[lines.Length];
            for( var i=0; i<lines.Length; i++)
            {
                var line = lines[i];
                var values = line.Split(',');
                var pc = new PriceChange();
                if( values.Length < 2)
                {
                    throw new ApplicationException("Failed to parse " + line + " on line " + (i + 1));
                }
                if( !double.TryParse(values[0], out pc.Spread))
                {
                    throw new ApplicationException("Failed to parse " + line + " on line " + (i+1));
                }
                if (!double.TryParse(values[1], out pc.Change))
                {
                    throw new ApplicationException("Failed to parse " + line + " on line " + (i + 1));
                }
                priceChanges[i] = pc;
            }
            symbol = Factory.Symbol.LookupSymbol("MSFT");
        }

        [Test]
        public void TestBreakEvenCalc()
        {
            var inventory = new InventoryGroupDefault(symbol);
            inventory.Change(10, 1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(9, 666);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 9.6);
            inventory.Change(8, 555);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 9.2);
        }

        [Test]
        public void TestLongAddQuantity()
        {
            var inventory = new InventoryGroupDefault(symbol);
            inventory.Change(10, 1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(9, 666);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 9.6);
            var howManyToAdd = inventory.HowManyToAdd(7);
            Assert.AreEqual(740, howManyToAdd);
            inventory.Change(7, howManyToAdd);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 8.8);
        }

        [Test]
        public void TestLongAddPrice()
        {
            var inventory = new InventoryGroupDefault(symbol);
            inventory.Change(10, 1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(9, 666);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 9.6);
            var howManyToAdd = 740;
            var priceToAdd = inventory.PriceToAdd(howManyToAdd);
            Assert.AreEqual(7, Math.Round(priceToAdd));
            inventory.Change(7, howManyToAdd);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 8.8);
        }

        [Test]
        public void TestShortAddQuantity()
        {
            var inventory = new InventoryGroupDefault(symbol);
            inventory.Change(10, -1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(11, -666);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 10.4);
            var howManyToAdd = inventory.HowManyToAdd(13);
            Assert.AreEqual(-740, howManyToAdd);
            inventory.Change(13, howManyToAdd);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 11.2);
        }

        [Test]
        public void TestShortAddPrice()
        {
            var inventory = new InventoryGroupDefault(symbol);
            inventory.Change(10, -1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(11, -666);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 10.4);
            var howManyToAdd = -740;
            var priceToAdd = inventory.PriceToAdd(howManyToAdd);
            Assert.AreEqual(13, Math.Round(priceToAdd));
            inventory.Change(13, howManyToAdd);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 11.2);
        }

        [Test]
        public void TestLongClose()
        {
            var inventory = new InventoryGroupDefault(symbol);
            inventory.Change(10, 1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(9, 666);
            Assert.AreEqual(9.6, Math.Round(inventory.BreakEven, 2),"break even 1");
            inventory.Change(8, 555);
            Assert.AreEqual(9.2, Math.Round(inventory.BreakEven, 2),"break even 2");
            var price = 6D;
            int howManyToClose;
            inventory.CalculateOffer(price, out price, out howManyToClose);
            Assert.AreEqual(Math.Round(9.4,2),Math.Round(price,2), "close price");
            Assert.AreEqual(2221, howManyToClose, "close size");
        }

        [Test]
        public void TestShortClose()
        {
            var inventory = new InventoryGroupDefault(symbol);
            inventory.Change(10, -1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(11, -666);
            Assert.AreEqual(10.4, Math.Round(inventory.BreakEven, 2));
            inventory.Change(12, -555);
            Assert.AreEqual(10.8, Math.Round(inventory.BreakEven, 2));
            double price = 15D;
            int howManyToClose;
            inventory.CalculateBid(price, out price, out howManyToClose);
            Assert.AreEqual(10.6, Round(price));
            Assert.AreEqual(-2221, howManyToClose);
        }

        [Test]
        public void TestLongBidOffer()
        {
            symbol = Factory.Symbol.LookupSymbol("EUR/USD");
            var inventory = new InventoryGroupDefault(symbol);
            inventory.Retrace = .60;
            inventory.RoundLotSize = 1000;
            inventory.MaximumLotSize = 1000;
            inventory.MinimumLotSize = 1000;
            inventory.Goal = 5000;
            Console.WriteLine("Price,Quantity,Cumulative,BreakEven,PandL");
            var first = true;
            var sb = new StringBuilder();
            for (var price = 1.7000D; price > 1.4000; price -= 10*symbol.MinimumTick )
            {
                var amountToBid = 0;
                inventory.CalculateBid(price, out price, out amountToBid);
                inventory.Change(price, amountToBid);
                var pandl = inventory.CurrentProfitLoss(price);
                sb.AppendLine(Round(price)+","+amountToBid+","+inventory.Size+","+Round(inventory.BreakEven)+","+Round(pandl));
            }
        }

        [Test]
        public void TestShortBidOffer()
        {
            symbol = Factory.Symbol.LookupSymbol("EUR/USD");
            var inventory = new InventoryGroupDefault(symbol);
            inventory.Retrace = .60;
            inventory.RoundLotSize = 1000;
            inventory.MaximumLotSize = 1000;
            inventory.MinimumLotSize = 1000;
            inventory.Goal = 5000;
            Console.WriteLine("Price,Quantity,Cumulative,BreakEven,PandL");
            var first = true;
            var sb = new StringBuilder();
            var price = 1.7000D;
            for (var i = 0; i < 3000; i++ )
            {
                var amountToOffer = 0;
                inventory.CalculateOffer(price, out price, out amountToOffer);
                inventory.Change(price, -amountToOffer);
                var pandl = inventory.CurrentProfitLoss(price);
                sb.AppendLine(Round(price) + "," + amountToOffer + "," + inventory.Size + "," + Round(inventory.BreakEven) + "," + Round(pandl));
                price += symbol.MinimumTick*10;
            }
            Console.Write(sb.ToString());
        }

        [Test]
        public void TestRandomTrading()
        {
            symbol = Factory.Symbol.LookupSymbol("EUR/USD");
            Random random = new Random(12349871);
            var monteCarloPass = new MonteCarloPass(symbol,priceChanges);
            monteCarloPass.RandomPass(random,true);
        }

        [Test]
        public void TestRandomTradingZeroOutput()
        {
            symbol = Factory.Symbol.LookupSymbol("EUR/USD");
            Random random = new Random(12349871);
            var monteCarloPass = new MonteCarloPass(symbol, priceChanges);
            monteCarloPass.RandomPass(random, false);
        }

        [Test]
        public void MonteCarloTesting()
        {
            symbol = Factory.Symbol.LookupSymbol("EUR/USD");
            Random random = new Random(12349871);
            var list = new List<MonteCarloPass>();
            for( var i=1; i<500; i++)
            {
                var monteCarloPass = new MonteCarloPass(symbol, priceChanges);
                monteCarloPass.RandomPass(random, false, false);
                //Console.WriteLine(monteCarloPass);
                list.Add(monteCarloPass);
            }
            var maxRunUp = 0D;
            var maxDrawDown = 0D;
            var maxInventory = 0D;
            var maxProfit = 0D;
            var profitableCount = 0;
            var profitSum = 0D;
            foreach( var pass in list)
            {
                if( pass.MaxRunUp > maxRunUp)
                {
                    maxRunUp = pass.MaxRunUp;
                }
                if( pass.MaxDrawDown < maxDrawDown)
                {
                    maxDrawDown = pass.MaxDrawDown;
                }
                if( pass.MaxInventorySize > maxInventory)
                {
                    maxInventory = pass.MaxInventorySize;
                }
                if( pass.ProfitLoss > maxProfit)
                {
                    maxProfit = pass.ProfitLoss;
                }
                if( pass.ProfitLoss > 0)
                {
                    profitableCount++;
                }
                profitSum += pass.ProfitLoss;
            }
            Console.WriteLine("Max Run Up " + Round(maxRunUp));
            Console.WriteLine("Max DrawDown " + Round(maxDrawDown));
            Console.WriteLine("Max Inventory " + Round(maxInventory));
            Console.WriteLine("Max Profit " + Round(maxProfit));
            Console.WriteLine("Profitable % " + Round(profitableCount*100/list.Count));
            Console.WriteLine("Total Profit " + Round(profitSum));
        }

        public double Round(double price)
        {
            return Math.Round(price, symbol.MinimumTickPrecision);
        }

        public struct PriceChange
        {
            public double Spread;
            public double Change;
            public PriceChange( double spread, double change)
            {
                Spread = spread;
                Change = change;
            }
        }

    }
}
