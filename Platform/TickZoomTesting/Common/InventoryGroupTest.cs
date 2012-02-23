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
        }

        [Test]
        public void TestBreakEvenCalc()
        {
            var inventory = new InventoryGroup(symbol);
            inventory.Change(10, 1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(9, 666);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 9.6);
            inventory.Change(8, 555);
            Assert.AreEqual(Math.Round(inventory.BreakEven, 2), 9.2);
        }

        [Test]
        public void TestLongAdd()
        {
            var inventory = new InventoryGroup(symbol);
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
        public void TestShortAdd()
        {
            var inventory = new InventoryGroup(symbol);
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
        public void TestLongClose()
        {
            var inventory = new InventoryGroup(symbol);
            inventory.Change(10, 1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(9, 666);
            Assert.AreEqual(9.6, Math.Round(inventory.BreakEven, 2));
            inventory.Change(8, 555);
            Assert.AreEqual(9.2, Math.Round(inventory.BreakEven, 2));
            var howManyToClose = inventory.CalcOfferQuantity(6);
            Assert.AreEqual(0, howManyToClose);
            howManyToClose = inventory.CalcOfferQuantity(11);
            Assert.AreEqual(2221, howManyToClose);
        }

        [Test]
        public void TestShortClose()
        {
            var inventory = new InventoryGroup(symbol);
            inventory.Change(10, -1000);
            Assert.AreEqual(inventory.BreakEven, 10);
            inventory.Change(11, -666);
            Assert.AreEqual(10.4, Math.Round(inventory.BreakEven, 2));
            inventory.Change(12, -555);
            Assert.AreEqual(10.8, Math.Round(inventory.BreakEven, 2));

            var howManyToClose = inventory.CalcBidQuantity(15);
            Assert.AreEqual(0, howManyToClose);
            howManyToClose = inventory.CalcBidQuantity(9);
            Assert.AreEqual(2221, howManyToClose);
        }

        [Test]
        public void TestLongBidOffer()
        {
            symbol = Factory.Symbol.LookupSymbol("EUR/USD");
            var inventory = new InventoryGroup(symbol);
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
                var amountToBid = inventory.CalcBidQuantity(price);
                inventory.Change(price,amountToBid);
                var pandl = inventory.CurrentProfitLoss(price);
                sb.AppendLine(Round(price)+","+amountToBid+","+inventory.Size+","+Round(inventory.BreakEven)+","+Round(pandl));
            }
        }

        [Test]
        public void TestShortBidOffer()
        {
            symbol = Factory.Symbol.LookupSymbol("EUR/USD");
            var inventory = new InventoryGroup(symbol);
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
                var amountToOffer = inventory.CalcOfferQuantity(price);
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
            for( var i=1; i<100; i++)
            {
                var monteCarloPass = new MonteCarloPass(symbol, priceChanges);
                monteCarloPass.RandomPass(random, false);
                Console.WriteLine(monteCarloPass);
                list.Add(monteCarloPass);
            }
        }

        public double Round(double price)
        {
            return Math.Round(price, symbol.MinimumTickPrecision);
        }

        public class MonteCarloPass
        {
            public double MaxRunUp;
            public double MaxDrawDown;
            public double MaxInventorySize;
            public double ProfitLoss;
            private SymbolInfo symbol;
            private PriceChange[] priceChanges;

            public override string ToString()
            {
                return Round(MaxRunUp) + "," + Round(MaxDrawDown) + "," + MaxInventorySize + "," + Round(ProfitLoss);
            }

            public MonteCarloPass(SymbolInfo symbol, PriceChange[] priceChanges)
            {
                this.symbol = symbol;
                this.priceChanges = priceChanges;
            }

            public void RandomPass(Random random, bool writeOutput)
            {
                var inventory = new InventoryGroup(symbol);
                inventory.Retrace = .60;
                inventory.StartingLotSize = 1000;
                inventory.RoundLotSize = 1000;
                inventory.MaximumLotSize = int.MaxValue;
                inventory.MinimumLotSize = 1000;
                inventory.Goal = 5000;
                var first = true;
                var sb = writeOutput ? new StringBuilder() : null;
                var line = "Price,Bid,Offer,BidQuantity,OfferCuantity,Change,Position,BreakEven,PandL,CumPandL";
                if (writeOutput) sb.AppendLine(line);
                var price = 1.7000D;
                var lastPrice = price;
                var spread = symbol.MinimumTick * 10;
                var bid = Math.Round(price - spread, 5);
                var offer = Math.Round(price + spread, 5);
                try
                {

                    for (var i = 0; i < 3000; i++)
                    {
                        if (price < lastPrice)
                        {
                            offer = Math.Round(price + spread, 5);
                        }
                        if (price > lastPrice)
                        {
                            bid = Math.Round(price - spread, 5);
                        }

                        var amountToOffer = inventory.CalcOfferQuantity(offer);
                        var amountToBid = inventory.CalcBidQuantity(bid);

                        lastPrice = price;
                        //price += symbol.MinimumTick*-10;
                        price = Math.Round(NextPrice(random, price), 5);

                        var change = 0;
                        if (price <= bid)
                        {
                            change = amountToBid;
                            bid = Math.Round(price - spread, 5);
                        }
                        else if (price >= offer)
                        {
                            change = -amountToOffer;
                            offer = Math.Round(price + spread, 5);
                        }

                        if (change != 0)
                        {
                            inventory.Change(price, change);
                        }

                        var pandl = Round(inventory.CurrentProfitLoss(price));
                        var cumulativeProfit = Round(inventory.CumulativeProfit);
                        var totalPanL = pandl + cumulativeProfit;
                        if (totalPanL > MaxRunUp)
                        {
                            MaxRunUp = totalPanL;
                        }
                        if (totalPanL < MaxDrawDown)
                        {
                            MaxDrawDown = totalPanL;
                        }
                        if( Math.Abs(inventory.Size) >  MaxInventorySize)
                        {
                            MaxInventorySize = Math.Abs(inventory.Size);
                        }
                        line = Round(price) + "," + bid + "," + offer + "," + amountToBid + "," + amountToOffer + "," +
                               change + "," + inventory.Size + "," + Round(inventory.BreakEven) + "," + Round(pandl) + "," +
                               cumulativeProfit;
                        if (writeOutput) sb.AppendLine(line);
                        if (writeOutput) Console.WriteLine(line);
                    }
                    ProfitLoss = inventory.CumulativeProfit + inventory.CurrentProfitLoss(price);
                }
                finally
                {
                    if (writeOutput)
                    {
                        var appDataFolder = Factory.Settings["AppDataFolder"];
                        var file = appDataFolder + Path.DirectorySeparatorChar + "Random.csv";
                        File.WriteAllText(file, sb.ToString());
                    }
                }
            }

            private double NextPrice(Random r, double last)
            {
                var index = r.Next(priceChanges.Length);
                var pc = priceChanges[index];
                return last + pc.Change;
            }

            public double Round(double price)
            {
                return Math.Round(price, symbol.MinimumTickPrecision);
            }

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
