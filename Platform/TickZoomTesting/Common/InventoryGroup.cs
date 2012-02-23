using System;
using TickZoom.Api;

namespace TickZoom.Common
{
    public enum Direction
    {
        Flat,
        Long,
        Short,
        Complete
    }

    public class InventoryGroup
    {
        private TransactionPairBinary binary;
        private double breakEven = double.NaN;
        private double retrace = 0.60D;
        private int profitTicks;
        private SymbolInfo symbol;
        private ProfitLoss profitLoss;
        private int roundLotSize = 1;
        private int startingLotSize = 1;
        private int minimumLotSize;
        private int maximumLotSize=int.MaxValue;
        private int _goal = 1;
        private double cumulativeProfit;

        public Direction Direction
        {
            get
            {
                if( binary.CurrentPosition == 0)
                {
                    return Direction.Flat;
                }
                else if( binary.Completed)
                {
                    return Direction.Complete;
                }
                else if( binary.CurrentPosition > 0)
                {
                    return Direction.Long;
                }
                else
                {
                    return Direction.Short;
                }
            }
        }

        public InventoryGroup( SymbolInfo symbol)
        {
            this.symbol = symbol;
            profitLoss = symbol.ProfitLoss;
        }

        public double BreakEven
        {
            get { return breakEven; }
        }

        public void Change( double price, int positionChange)
        {
            var newPosition = binary.CurrentPosition + positionChange;
            if( binary.Completed)
            {
                throw new InvalidOperationException("Inventory already closed.");
            }
            if( binary.CurrentPosition == 0)
            {
                binary.Enter(newPosition, price, Factory.Parallel.UtcNow, Factory.Parallel.UtcNow, 0, 0, 0);
                CalcBreakEven();
            }
            else if( newPosition == 0)
            {
                binary.Exit(price, Factory.Parallel.UtcNow, Factory.Parallel.UtcNow, 0, 0, 0);
                breakEven = double.NaN;
                var pandl = CurrentProfitLoss(price);
                cumulativeProfit = CumulativeProfit + pandl;
                binary = default(TransactionPairBinary);
                breakEven = double.NaN;
            }
            else
            {
                binary.ChangeSize(newPosition, price);
                CalcBreakEven();
            }
            binary.UpdatePrice(price);
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

        public int HowManyToAdd(double price)
        {
            var retraceComplement = 1 - Retrace;
            var r_entry = Retrace*binary.EntryPrice;
            var r_price = retraceComplement*price;

            var upper = binary.CurrentPosition*(r_entry + r_price - breakEven);
            var lower = Retrace*(price - binary.EntryPrice);
            var quantity = upper / lower;
            return (int) quantity;
        }

        public int HowManyToClose(double price)
        {
            if( binary.CurrentPosition > 0)
            {
                var closePrice = breakEven + profitTicks*symbol.MinimumTick;
                if (price > closePrice)
                {
                    return binary.CurrentPosition;
                }
            }
            if (binary.CurrentPosition < 0)
            {
                var closePrice = breakEven - profitTicks * symbol.MinimumTick;
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

        public int CalcBidQuantity( double price)
        {
            switch (Direction)
            {
                case Direction.Flat:
                    return startingLotSize;
                case Direction.Long:
                    if( Size < Goal)
                    {
                        return startingLotSize;
                    }
                    if( price < binary.MinPrice)
                    {
                        return Clamp(HowManyToAdd(price));
                    }
                    return 0;
                case Direction.Short:
                    return Clamp(HowManyToClose(price));
                case Direction.Complete:
                default:
                    throw new InvalidOperationException("Unexpected status: " + Direction);
            }
        }

        public int CalcOfferQuantity( double price)
        {
            switch (Direction)
            {
                case Direction.Flat:
                    return startingLotSize;
                case Direction.Long:
                    return Clamp(HowManyToClose(price));
                case Direction.Short:
                    if (Size < Goal)
                    {
                        return startingLotSize;
                    }
                    if( price > binary.MaxPrice)
                    {
                        return Clamp(HowManyToAdd(price));
                    }
                    return 0;
                case Direction.Complete:
                default:
                    throw new InvalidOperationException("Unexpected status: " + Direction);
            }
        }

        public double CurrentProfitLoss(double price)
        {
            return profitLoss.CalculateProfit(binary.CurrentPosition, binary.AverageEntryPrice, price);
        }

    }
}