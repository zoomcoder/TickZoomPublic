using System;
using System.IO;
using System.Text;
using TickZoom.Api;
using TickZoom.Examples;

namespace TickZoom.Common
{
    public class MonteCarloPass
    {
        public double MaxRunUp;
        public double MaxDrawDown;
        public double MaxInventorySize;
        public double ProfitLoss;
        public int iterations = 5000;
        private SymbolInfo symbol;
        private InventoryGroupTest.PriceChange[] priceChanges;
        private int seed;
        private bool isRandom = true;
        private int priceChangeTicks = 10;

        public bool IsRandom
        {
            get { return isRandom; }
            set { isRandom = value; }
        }

        public int PriceChangeTicks
        {
            get { return priceChangeTicks; }
            set { priceChangeTicks = value; }
        }

        public override string ToString()
        {
            return Round(MaxRunUp) + "," + Round(MaxDrawDown) + "," + MaxInventorySize + "," + Round(ProfitLoss);
        }

        public MonteCarloPass(SymbolInfo symbol, InventoryGroupTest.PriceChange[] priceChanges)
        {
            this.symbol = symbol;
            this.priceChanges = priceChanges;
        }

        public void RandomPass(int seed, bool writeOutput)
        {
            RandomPass(seed,writeOutput,false);
        }

        public void RandomPass(int seed, bool writeOutput, bool debug)
        {
            this.seed = seed;
            var random = new Random(seed);
            if (debug) writeOutput = true;
            var inventory = (InventoryGroup) new InventoryGroupDefault(symbol);
            inventory.Retrace = .60;
            inventory.StartingLotSize = 1000;
            inventory.RoundLotSize = 1000;
            inventory.MinimumLotSize = 1000;
            inventory.MaximumLotSize = inventory.MinimumLotSize * 10;
            inventory.Goal = 1000;
            var first = true;
            var sb = writeOutput ? new StringBuilder() : null;
            var price = 1.7000D;
            var lastPrice = price;
            var spread = symbol.MinimumTick * 10;
            var bid = Math.Round(price - spread, 5);
            var offer = Math.Round(price + spread, 5);
            try
            {

                for (var i = 0; i < iterations; i++)
                {
                    if (price < lastPrice)
                    {
                        offer = Math.Round(price + spread, 5);
                    }
                    if (price > lastPrice)
                    {
                        bid = Math.Round(price - spread, 5);
                    }

                    inventory.CalculateBidOffer(bid, offer);
                    bid = inventory.Bid;
                    offer = inventory.Offer;
                    var amountToOffer = inventory.OfferSize;
                    var amountToBid = inventory.BidSize;

                    lastPrice = price;
                    if( isRandom)
                    {
                        price = Round(NextPrice(random, price));
                    }
                    else
                    {
                        price = Round(price + PriceChangeTicks*symbol.MinimumTick);
                    }

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
                    var actualSpread = Round(offer - bid);
                    var line = Round(price) + "," + Round(bid) + "," + Round(offer) + "," + Round(actualSpread) + "," + amountToBid + "," +
                               amountToOffer + "," + change + "," + inventory.Size + "," + pandl + "," +
                               cumulativeProfit + inventory.ToString();
                    if (writeOutput) sb.AppendLine(line);
                    if (writeOutput && !debug) Console.WriteLine(line);
                }
                ProfitLoss = inventory.CumulativeProfit + inventory.CurrentProfitLoss(price);
            }
            finally
            {
                if (writeOutput)
                {
                    if( !debug || MaxInventorySize > 150000)
                    {
                        var line = "Price,Bid,Offer,Spread,BidQuantity,OfferCuantity,Change,Position,PandL,CumPandL"+inventory.ToHeader();
                        sb.Insert(0,line + Environment.NewLine);
                        var appDataFolder = Factory.Settings["AppDataFolder"];
                        var file = appDataFolder + Path.DirectorySeparatorChar + "Random.csv";
                        File.WriteAllText(file, sb.ToString());
                    }
                    if( debug && MaxInventorySize > 150000)
                    {
                        throw new ApplicationException("MaxInventory was " + MaxInventorySize + " at random seed: " + seed);
                    }
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
}