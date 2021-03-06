using System;
using TickZoom.Api;

namespace TickZoom.Examples
{
    public class InventoryGroupDefault : InventoryGroup
    {
        #region fields

        private static Log log = Factory.SysLog.GetLogger(typeof (InventoryGroupDefault));
        private bool debug = log.IsDebugEnabled;
        private bool trace = log.IsTraceEnabled;
        private int id;
        private TransactionPairBinary binary;
        private TransactionPairBinary counter;
        private double breakEven = double.NaN;
        private double retrace = 0.60D;
        private int profitTicks = 20;
        private SymbolInfo symbol;
        private ProfitLoss2 profitLoss;
        private int roundLotSize = 1;
        private int startingLotSize = 1;
        private int minimumLotSize;
        private int maximumLotSize=int.MaxValue;
        private int _goal = 1;
        private double currentProfit;
        private double cumulativeProfit;
        private InventoryType type = InventoryType.Either;
        private InventoryStatus status = InventoryStatus.Flat;
        private double beginningPrice;
        private double maxSpread;
        #endregion

        public void UpdateBidAsk(double marketBid, double marketAsk)
        {
            if( binary.CurrentPosition > 0)
            {
                binary.UpdatePrice(marketAsk);
                TrackExcursions(marketAsk);
            }
            if( binary.CurrentPosition < 0)
            {
                binary.UpdatePrice(marketBid);
                TrackExcursions(marketBid);
            }
        }

        public double ExtremePrice
        {
            get
            {
                if( binary.CurrentPosition > 0)
                {
                    return binary.MinPrice;
                }
                if( binary.CurrentPosition < 0)
                {
                    return binary.MaxPrice;
                }
                return 0D;
            }
        }

        public InventoryGroupDefault( SymbolInfo symbol) : this( symbol, 1)
        {
            
        }

        public InventoryGroupDefault(SymbolInfo symbol, int id)
        {
            this.symbol = symbol;
            this.id = id;
            profitLoss = symbol.ProfitLoss as ProfitLoss2;
            if (profitLoss == null)
            {
                var message = "Requires ProfitLoss2 interface for calculating profit and loss.";
                log.Error(message);
                throw new ApplicationException(message);
            }
        }

        public void CalculateLongBidOffer(double marketBid, double marketOffer)
        {
            //if (marketBid < beginningPrice && Size < Goal)
            //{
            //    bid = marketBid;
            //    bidSize = minimumLotSize;
            //}
            //else
            {
                bidSize = minimumLotSize;
                bid = PriceToChange(bidSize,marketBid);
            }

            offerSize = minimumLotSize;
            offer = PriceToChange(-offerSize,marketOffer);
            offer = Math.Max(offer, marketOffer);

            var priceToClose = PriceToClose();
            if (offer >= priceToClose)
            {
                offer = priceToClose;
                offerSize = Math.Abs(Size);
            }

            bid = Math.Min(bid, BreakEven - 10*symbol.MinimumTick);
            offer = Math.Max(offer, bid + 5 * symbol.MinimumTick);

            if (trace) log.Trace("Long: Beginning " + BeginningPrice + ", break even " + BreakEven + ", min price " + binary.MinPrice + ", bid " + bid + ", offer " + offer + ", position " + binary.CurrentPosition);
            if (binary.CurrentPosition != 0)
            {
                AssertGreaterOrEqual(BeginningPrice, BreakEven, "beginning >= break even");
                AssertGreaterOrEqual(Round(BreakEven), Round(bid), "break even >= bid");
                AssertGreater(offer, bid, "offer > bid");
            }
            var spread = offer - bid;
            if( spread > maxSpread)
            {
                maxSpread = spread;
            }

            if( additionalTicks > 0)
            {
                bid -= additionalTicks * symbol.MinimumTick;
            }
        }

        private void AssertGreater(double expected, double actual, string message)
        {
            if( expected <= actual)
            {
                var error = "Expected " + expected + " greater than " + actual + " for " + message;
                log.Error(error);
                throw new ApplicationException(error);
            }
        }

        private void AssertGreaterOrEqual(double expected, double actual, string message)
        {
            if (expected < actual)
            {
                var error = "Expected " + expected + " greater than or equal to " + actual + " for " + message;
                log.Error(error);
                throw new ApplicationException(error);
            }
        }
        private void AssertLessThan(double expected, double actual, string message)
        {
            if (expected >= actual)
            {
                var error = "Expected " + expected + " less then " + actual + " for " + message;
                log.Error(error);
                throw new ApplicationException(error);
            }
        }

        public void CalculateShortBidOffer(double marketBid, double marketOffer)
        {
            //if (marketOffer > beginningPrice && Math.Abs(Size) < Goal)
            //{
            //    offer = marketOffer;
            //    offerSize = minimumLotSize;
            //}
            //else
            {
                offerSize = minimumLotSize;
                offer = PriceToChange(-offerSize,marketOffer);
            }

            bidSize = minimumLotSize;
            bid = PriceToChange(bidSize,marketBid);
            bid = Math.Min(bid, marketBid);

            var priceToClose = PriceToClose();
            if( bid <= priceToClose)
            {
                bid = priceToClose;
                bidSize = Math.Abs(Size);
            }

            offer = Math.Max(offer, BreakEven + 10*symbol.MinimumTick);
            bid = Math.Min(bid, offer - 5*symbol.MinimumTick);

            if (trace) log.Trace("Short: Beginning " + BeginningPrice + ", break even " + BreakEven + ", max price " + binary.MaxPrice + ", bid " + bid + "/ offer " + offer + ", position " + binary.CurrentPosition + ", market bid " + marketBid + "/ offer " + marketOffer);
            if (binary.CurrentPosition != 0)
            {
                AssertGreaterOrEqual(Round(offer), Round(BreakEven), "break even > bid");
                AssertGreater(offer, bid, "offer > bid");
                AssertGreaterOrEqual(BreakEven, BeginningPrice, "break even > beginning");
                //AssertGreaterOrEqual(binary.MaxPrice, breakEven, "MaxPrice >= break even");
            }
            var spread = offer - bid;
            if (spread > maxSpread)
            {
                maxSpread = spread;
            }

            if( additionalTicks > 0)
            {
                offer += additionalTicks * symbol.MinimumTick;
            }
        }

        public double PriceToChange(int quantity, double price)
        {
            var retraceComplement = 1 - Retrace;
            var totalValue = (binary.CurrentPosition * breakEven);
            var upper = retrace * BeginningPrice * (quantity + binary.CurrentPosition) - totalValue;
            var lower = retrace * quantity - retraceComplement * binary.CurrentPosition;
            var result = upper / lower;
            return result;
        }

        public int QuantityToChange(double price)
        {
            var favorableExcursion = binary.CurrentPosition > 0 ? binary.MaxPrice : binary.MinPrice;
            var adverseExcursion = binary.CurrentPosition > 0 ? Math.Min(price, binary.MinPrice) : Math.Max(price, binary.MaxPrice);
            var retraceComplement = 1 - Retrace;
            var r_favorable = Retrace * favorableExcursion;
            var r_adverse = retraceComplement * adverseExcursion;

            var upper = binary.CurrentPosition * (r_favorable + r_adverse - breakEven);
            var lower = Retrace * (adverseExcursion - favorableExcursion);
            var quantity = upper / lower;
            return (int)quantity;
        }

        public double BreakEven
        {
            get { return breakEven; }
        }

        private void TrackExcursions(double price)
        {
            if (binary.CurrentPosition == 0)
            {
                beginningPrice = 0D;
            }
            else if (BeginningPrice == 0D)
            {
                beginningPrice = price;
            }
            else if (binary.CurrentPosition > 0)
            {
                if( price > BeginningPrice)
                {
                    beginningPrice = price;
                }
                if (BreakEven > BeginningPrice)
                {
                    beginningPrice = BreakEven + 10*symbol.MinimumTick;
                }
            }
            else if (binary.CurrentPosition < 0)
            {
                if (BreakEven < BeginningPrice)
                {
                    beginningPrice = BreakEven - 10 * symbol.MinimumTick;
                }
                if (price < BeginningPrice)
                {
                    beginningPrice = price;
                }
            }
        }

        private int lastSize;
        private int additionalTicks = 0;
        private int increaseTicks = 0;
        public void Change( double price, int positionChange)
        {
            var newPosition = binary.CurrentPosition + positionChange;
            binary.Change(price, positionChange);
            if (trace) log.Trace("Changed " + positionChange + " at " + price + ", position " + binary.CurrentPosition);
            CalcBreakEven();
            TrackExcursions(price);
            TryChangeCounter(price, positionChange);
            if (newPosition == 0)
            {
                breakEven = double.NaN;
                var pandl = CalcProfit(binary, price);
                cumulativeProfit = CumulativeProfit + pandl;
                currentProfit = 0D;
                binary = default(TransactionPairBinary);
                counter = default(TransactionPairBinary);
                breakEven = double.NaN;
            }
            else
            {
                currentProfit = profitLoss.CalculateProfit(binary.CurrentPosition, binary.AverageEntryPrice, price);
            }
            binary.UpdatePrice(price);
            SetStatus();
            //CalcAdditionalTicks();
        }

        private void CalcAdditionalTicks()
        {
            var currentSize = Math.Abs(Size);
            if (currentSize > lastSize)
            {
                increaseTicks += 2;
                additionalTicks += increaseTicks;
            }
            else if (currentSize < lastSize)
            {
                increaseTicks = 0;
                additionalTicks = 0;
            }
            lastSize = currentSize;
        }

        private void TryChangeCounter(double price, int positionChange)
        {
            if (Status == InventoryStatus.Long) 
            {
                if( positionChange < 0)
                {
                    counter.Change(price, positionChange);
                }
                else if( counter.CurrentPosition < 0)
                {
                    positionChange = Math.Min(positionChange, -counter.CurrentPosition);
                    counter.Change(price, positionChange);
                    var closedPoints = counter.ClosedPoints; 
                    if( closedPoints > 0)
                    {
                        var positionSize = Math.Abs(binary.CurrentPosition);
                        var points = BeginningPrice * positionSize;
                        points -= closedPoints;
                        var newExtreme = points / positionSize;
                        beginningPrice = newExtreme;
                        counter.ClosedPoints = 0;
                    }
                    if (counter.Completed)
                    {
                        counter = default(TransactionPairBinary);
                    }
                }
            }
            if (Status == InventoryStatus.Short)
            {
                if( positionChange > 0)
                {
                    counter.Change(price, positionChange);
                }
                else if( counter.CurrentPosition > 0)
                {
                    positionChange = Math.Max(positionChange, -counter.CurrentPosition);
                    counter.Change(price, positionChange);
                    if( counter.Completed)
                    {
                        var closedPoints = counter.ClosedPoints;
                        if( closedPoints > 0)
                        {
                            var positionSize = Math.Abs(binary.CurrentPosition);
                            var points = BeginningPrice * positionSize;
                            points += closedPoints;
                            beginningPrice = points / positionSize;
                        }
                        counter = default(TransactionPairBinary);
                    }
                }
            }
        }

        private void CalcBreakEven()
        {
            var size = Math.Abs(binary.CurrentPosition);
            if (size == 0)
            {
                breakEven = double.NaN;
                return;
            }
            var sign = -Math.Sign(binary.CurrentPosition);
            var openPoints = binary.AverageEntryPrice.ToLong() * size;
            var closedPoints = binary.ClosedPoints.ToLong() * sign;
            var grossProfit = openPoints + closedPoints;
            var transaction = 0; // size * commission * sign;
            var expectedTransaction = 0; // size * commission * sign;
            var result = (grossProfit - transaction - expectedTransaction) / size;
            result = ((result + 5000) / 10000) * 10000;
            breakEven = result.ToDouble();
        }

        public double PriceToClose()
        {
            if( binary.CurrentPosition > 0)
            {
                return BreakEven + ProfitTicks*symbol.MinimumTick;
            }
            if( binary.CurrentPosition < 0)
            {
                return BreakEven - ProfitTicks*symbol.MinimumTick;
            }
            throw new InvalidOperationException("Inventory must be long or short to calculate PriceToClose");
        }

        public int HowManyToClose(double price)
        {
            if( binary.CurrentPosition > 0)
            {
                var closePrice = BreakEven + ProfitTicks*symbol.MinimumTick;
                if (price > closePrice)
                {
                    return binary.CurrentPosition;
                }
            }
            if (binary.CurrentPosition < 0)
            {
                var closePrice = BreakEven - ProfitTicks * symbol.MinimumTick;
                if (price < closePrice)
                {
                    return binary.CurrentPosition;
                }
            }
            return 0;
        }

        private int Clamp( int size)
        {
            size = Math.Abs(size);
            size = size < MinimumLotSize ? 0 : Math.Min(MaximumLotSize, size);
            size = size/RoundLotSize*RoundLotSize;
            return size;
        }

        public string ToHeader()
        {
            return "Type#" + id + ",Status#" + id + ",Bid#" + id + ",Offer#" + id + ",Spread#" + id + ",BidQuantity#" + id + ",OfferCuantity#" + id +
                       ",Position#" + id + ",Extreme#" +id + ",BreakEven#" + id + ",PandL#" + id + ",CumPandL#" + id + "";
        }

        public override string ToString()
        {
            return Type + "," + Status + "," + Round(bid) + "," + Round(offer) + "," + Round(offer - bid) + "," + bidSize + "," + offerSize + "," +
                   binary.CurrentPosition + "," + Round(BeginningPrice) + "," + Round(BreakEven) + "," + Round(currentProfit) + "," + Round(cumulativeProfit);
        }

        public double Round(double price)
        {
            return Math.Round(price, symbol.MinimumTickPrecision);
        }

        private void SetStatus()
        {
            if (binary.CurrentPosition == 0)
            {
                status = InventoryStatus.Flat;
            }
            else if (binary.Completed)
            {
                status = InventoryStatus.Complete;
            }
            else if (binary.CurrentPosition > 0)
            {
                status = InventoryStatus.Long;
            }
            else
            {
                status = InventoryStatus.Short;
            }
        }

        public void Pause()
        {
            if( status != InventoryStatus.Flat)
            {
                throw new InvalidOperationException("Inventory must be flat in order to be paused.");
            }
            status = InventoryStatus.Paused;
        }

        public void Resume()
        {
            if (status != InventoryStatus.Paused)
            {
                throw new InvalidOperationException("Inventory must be paused in order to be resumed.");
            }
            status = InventoryStatus.Flat;
        }

        public InventoryStatus Status
        {
            get { return status; }
        }

        public int Size
        {
            get { return binary.CurrentPosition; }
        }

        public int MinimumLotSize
        {
            get { return minimumLotSize; }
            set { minimumLotSize = value; }
        }

        public int MaximumLotSize
        {
            get { return maximumLotSize; }
            set { maximumLotSize = value; }
        }

        public int RoundLotSize
        {
            get { return roundLotSize; }
            set { roundLotSize = value; }
        }

        public double Retrace
        {
            get { return retrace; }
            set { retrace = value; }
        }

        public int Goal
        {
            get { return _goal; }
            set { _goal = value; }
        }

        public int StartingLotSize
        {
            get { return startingLotSize; }
            set { startingLotSize = value; }
        }

        public double CumulativeProfit
        {
            get { return cumulativeProfit; }
        }

        public int ProfitTicks
        {
            get { return profitTicks; }
            set { profitTicks = value; }
        }

        public int BidSize
        {
            get { return bidSize; }
        }

        public int OfferSize
        {
            get { return offerSize; }
        }

        public double Offer
        {
            get { return offer; }
        }

        public double Bid
        {
            get { return bid; }
        }

        public InventoryType Type
        {
            get { return type; }
            set { type = value; }
        }

        public double BeginningPrice
        {
            get { return beginningPrice; }
        }

        private double bid;
        private double offer;
        private int offerSize;
        private int bidSize;

        public void CalculateBidOffer(double marketBid, double marketOffer)
        {
            bid = marketBid;
            offer = marketOffer;
            bidSize = 0;
            offerSize = 0;
            switch (Status)
            {
                case InventoryStatus.Paused:
                    return;
                case InventoryStatus.Flat:
                    switch (type)
                    {
                        case InventoryType.Short:
                            bidSize = 0;
                            offerSize = startingLotSize;
                            break;
                        case InventoryType.Long:
                            bidSize = startingLotSize;
                            offerSize = 0;
                            break;
                        case InventoryType.Either:
                            bidSize = startingLotSize;
                            offerSize = startingLotSize;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException("Unexpected inventory type: " + type);
                    }
                    return;
                case InventoryStatus.Long:
                    CalculateLongBidOffer(marketBid, marketOffer);
                    return;
                case InventoryStatus.Short:
                    CalculateShortBidOffer(marketBid, marketOffer);
                    return;
                case InventoryStatus.Complete:
                default:
                    throw new InvalidOperationException("Unexpected status: " + Status);
            }
        }

        private double CalcProfit( TransactionPairBinary binary, double price)
        {
            double profit;
            double exit;
            profitLoss.CalculateProfit(binary, out profit, out exit);
            return profit;
        }

        public double CurrentProfitLoss(double price)
        {
            currentProfit = CalcProfit(binary, price);
            return currentProfit;
        }

    }
}