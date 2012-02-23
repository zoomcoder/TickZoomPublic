using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
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
    public interface InventoryGroup
    {
        double BreakEven { get; }
        int Size { get; }
        int MinimumLotSize { get; set; }
        int MaximumLotSize { get; set; }
        int RoundLotSize { get; set; }
        double Retrace { get; set; }
        int Goal { get; set; }
        int StartingLotSize { get; set; }
        double CumulativeProfit { get; }
        int ProfitTicks { get; set; }
        void CalculateBid( double price, out double bid, out int bidSize);
        void CalculateOffer( double price, out double offer, out int offerSize);
        void Change( double price, int positionChange);
        double CurrentProfitLoss(double price);
    }

    public class InventoryGroupMaster : InventoryGroup
    {
        private List<InventoryGroupDefault> inventories = new List<InventoryGroupDefault>();
        private double retrace = 0.60D;
        private int profitTicks = 20;
        private SymbolInfo symbol;
        private int roundLotSize = 1;
        private int startingLotSize = 1;
        private int minimumLotSize;
        private int maximumLotSize=int.MaxValue;
        private int _goal = 1;

        public InventoryGroupMaster(SymbolInfo symbol)
        {
            var inventory = new InventoryGroupDefault(symbol);
            inventories.Add(inventory);
            ApplySettings();
        }

        public void CalculateBid(double price, out double bid, out int bidSize)
        {
            var inventory = inventories[0];
            inventory.CalculateBid(price, out bid, out bidSize);
        }

        public void CalculateOffer(double price, out double offer, out int offerSize)
        {
            var inventory = inventories[0];
            inventory.CalculateOffer(price,out offer, out offerSize);
        }

        public void Change(double price, int positionChange)
        {
            var inventory = inventories[0];
            inventory.Change(price, positionChange);
        }

        public int Size
        {
            get
            {
                int result = 0;
                foreach (var inventory in inventories)
                {
                    result += inventory.Size;
                }
                return result;
            }
        }

        public double CumulativeProfit
        {
            get
            {
                double result = 0D;
                foreach (var inventory in inventories)
                {
                    result += inventory.CumulativeProfit;
                }
                return result;
            }
        }

        public double CurrentProfitLoss(double price)
        {
            double result = 0D;
            foreach( var inventory in inventories)
            {
                result += inventory.CurrentProfitLoss(price);
            }
            return result;
        }

        public double BreakEven
        {
            get { throw new NotImplementedException(); }
        }

        public double Retrace
        {
            get { return retrace; }
            set { retrace = value; ApplySettings(); }
        }

        public int ProfitTicks
        {
            get { return profitTicks; }
            set { profitTicks = value; ApplySettings(); }
        }

        public int RoundLotSize
        {
            get { return roundLotSize; }
            set { roundLotSize = value; ApplySettings(); }
        }

        public int StartingLotSize
        {
            get { return startingLotSize; }
            set { startingLotSize = value; ApplySettings(); }
        }

        public int MinimumLotSize
        {
            get { return minimumLotSize; }
            set { minimumLotSize = value; ApplySettings(); }
        }

        public int MaximumLotSize
        {
            get { return maximumLotSize; }
            set { maximumLotSize = value; ApplySettings(); }
        }

        public int Goal
        {
            get { return _goal; }
            set { _goal = value; }
        }

        private void ApplySettings()
        {
            foreach (var inventory in inventories)
            {
                inventory.Retrace = Retrace;
                inventory.ProfitTicks = ProfitTicks;
                inventory.RoundLotSize = RoundLotSize;
                inventory.StartingLotSize = StartingLotSize;
                inventory.MinimumLotSize = MinimumLotSize;
                inventory.MaximumLotSize = MaximumLotSize;
                inventory.Goal = Goal;
            }
        }

    }
}