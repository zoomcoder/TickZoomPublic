using System;
using TickZoom.Api;

namespace TickZoom.Common
{
    public class InventoryGroupDefault : InventoryGroup
    {
        #region fields
        private int id;
        private TransactionPairBinary binary;
        private TransactionPairBinary counter;
        private double breakEven = double.NaN;
        private double retrace = 0.60D;
        private int profitTicks = 20;
        private SymbolInfo symbol;
        private ProfitLoss profitLoss;
        private int roundLotSize = 1;
        private int startingLotSize = 1;
        private int minimumLotSize;
        private int maximumLotSize=int.MaxValue;
        private int _goal = 1;
        private double currentProfit;
        private double cumulativeProfit;
        private InventoryType type = InventoryType.Either;
        private InventoryStatus status = InventoryStatus.Flat;
        private double extremePrice;
        #endregion

        public InventoryGroupDefault( SymbolInfo symbol) : this( symbol, 1)
        {
            
        }

        public void CalculateLongBidOffer(double marketBid, double marketOffer)
        {
            //if( marketBid < extremePrice && Size < Goal)
            //{
            //    bid = marketBid;
            //    bidSize = minimumLotSize;
            //}
            //else
            {
                bidSize = minimumLotSize;
                bid = PriceToChange(bidSize);
            }

            offerSize = minimumLotSize;
            offer = PriceToChange(-offerSize);
            offer = Math.Max(offer, bid + 20*symbol.MinimumTick);

            offer = Math.Max(offer, marketOffer);
            bid = Math.Min(bid, marketBid);

            var priceToClose = PriceToClose();
            if (offer >= priceToClose)
            {
                offerSize = Math.Abs(Size);
            }

        }

        public void CalculateShortBidOffer(double marketBid, double marketOffer)
        {
            //if (marketOffer > extremePrice && Size < Goal)
            //{
            //    offer = marketOffer;
            //    offerSize = minimumLotSize;
            //}
            //else
            {
                offerSize = minimumLotSize;
                offer = PriceToChange(-offerSize);
            }

            bidSize = minimumLotSize;
            bid = PriceToChange(bidSize);
            bid = Math.Max(bid, offer + 20*symbol.MinimumTick);

            offer = Math.Max(offer, marketOffer);
            bid = Math.Min(bid, marketBid);


            var priceToClose = PriceToClose();
            if( bid <= priceToClose)
            {
                bidSize = Math.Abs(Size);
            }
        }

        public double PriceToChange(int quantity)
        {
            var retraceComplement = 1 - Retrace;
            var totalValue = (binary.CurrentPosition * breakEven);
            var upper = retrace * extremePrice * (quantity + binary.CurrentPosition) - totalValue;
            var lower = retrace * quantity - retraceComplement * binary.CurrentPosition;
            var price = upper / lower;
            return price;
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

        public InventoryGroupDefault(SymbolInfo symbol, int id)
        {
            this.symbol = symbol;
            this.id = id;
            profitLoss = symbol.ProfitLoss;
        }

        public double BreakEven
        {
            get { return breakEven; }
        }

        private void TrackAdverseExcursion(double price)
        {
            if( binary.CurrentPosition == 0)
            {
                extremePrice = 0D;
            }
            else if (extremePrice == 0D)
            {
                extremePrice = price;
            }
            else if (binary.CurrentPosition > 0 && price > extremePrice)
            {
                extremePrice = price;
            }
            else if (binary.CurrentPosition < 0 && price < extremePrice)
            {
                extremePrice = price;
            }
        }

        public void Change( double price, int positionChange)
        {
            var newPosition = binary.CurrentPosition + positionChange;
            binary.Change(price, positionChange);
            TryChangeCounter(price, positionChange);
            TrackAdverseExcursion(price);
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
                CalcBreakEven();
                currentProfit = profitLoss.CalculateProfit(binary.CurrentPosition, binary.AverageEntryPrice, price);
            }
            binary.UpdatePrice(price);
            SetStatus();
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
                    if( counter.Completed)
                    {
                        var closedPoints = counter.ClosedPoints; 
                        if( closedPoints > 0)
                        {
                            var positionSize = Math.Abs(binary.CurrentPosition);
                            var points = extremePrice * positionSize;
                            points -= closedPoints;
                            extremePrice = points / positionSize;
                        }
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
                            var points = extremePrice * positionSize;
                            points += closedPoints;
                            extremePrice = points / positionSize;
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
                return breakEven + ProfitTicks*symbol.MinimumTick;
            }
            if( binary.CurrentPosition < 0)
            {
                return breakEven - ProfitTicks*symbol.MinimumTick;
            }
            throw new InvalidOperationException("Inventory must be long or short to calculate PriceToClose");
        }

        public int HowManyToClose(double price)
        {
            if( binary.CurrentPosition > 0)
            {
                var closePrice = breakEven + ProfitTicks*symbol.MinimumTick;
                if (price > closePrice)
                {
                    return binary.CurrentPosition;
                }
            }
            if (binary.CurrentPosition < 0)
            {
                var closePrice = breakEven - ProfitTicks * symbol.MinimumTick;
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
                   binary.CurrentPosition + "," + Round(extremePrice) + "," + Round(breakEven) + "," + Round(currentProfit) + "," + Round(cumulativeProfit);
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
            return profitLoss.CalculateProfit(binary.CurrentPosition, binary.AverageEntryPrice, price);
        }

        public double CurrentProfitLoss(double price)
        {
            currentProfit = CalcProfit(binary, price);
            return currentProfit;
        }

    }
}