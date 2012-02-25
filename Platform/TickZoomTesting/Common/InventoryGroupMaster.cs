using System;
using System.Collections.Generic;
using TickZoom.Api;
using System.Text;

namespace TickZoom.Common
{
    public class InventoryGroupMaster : InventoryGroup
    {
        private ActiveList<InventoryGroupDefault> active = new ActiveList<InventoryGroupDefault>();
        private ActiveList<InventoryGroupDefault> inventories = new ActiveList<InventoryGroupDefault>();
        private double retrace = 0.60D;
        private int profitTicks = 20;
        private SymbolInfo symbol;
        private int roundLotSize = 1;
        private int startingLotSize = 1;
        private int minimumLotSize;
        private int maximumLotSize=int.MaxValue;
        private int _goal = 1;
        private InventoryGroupDefault bidOwner;
        private InventoryGroupDefault offerOwner;
        private InventoryType type;
        private double maximumSpread;
        private int maxInventories = 1;

        public InventoryGroupMaster(SymbolInfo symbol)
        {
            this.symbol = symbol;
            var inventory = new InventoryGroupDefault(symbol,1);
            active.AddLast(inventory);
            inventories.AddLast(inventory);
            ApplySettings();
            maximumSpread = 100*symbol.MinimumTick;
        }

        private int bidOffset = 0;
        private int bidIncrease = 1;
        private int offerOffset = 0;
        private int offerIncrease = 1;

        private void AdjustBidOffset()
        {
            bidOffset += bidIncrease;
            ++bidIncrease;
            if( offerOffset > 0)
            {
                offerIncrease = 1;
                offerOffset = 0;
            }
        }

        private void AdjustOfferOffset()
        {
            offerOffset += offerIncrease;
            ++offerIncrease;
            if( bidOffset > 0)
            {
                bidIncrease = 1;
                bidOffset = 0;
            }
        }

        public void CalculateBidOffer(double marketBid, double marketOffer)
        {
            marketBid -= 10*symbol.MinimumTick*bidOffset;
            marketOffer += 10 * symbol.MinimumTick * offerOffset;
            bidOwner = offerOwner = active.First.Value;
            var spread = 0D;
            for( var current = active.First; current != null; current = current.Next)
            {
                var inventory = current.Value;
                inventory.CalculateBidOffer(marketBid, marketOffer);
                CompareBidOffer(inventory);
                spread = offerOwner.Offer - bidOwner.Bid;
            }
            if (spread > maximumSpread)
            {
                var previous = active.Last.Value;
                var inventory = TryResumePausedInventory();
                if (inventory == null && inventories.Count < maxInventories)
                {
                    inventory = AddNewInventory();
                }
                if( inventory != null)
                {
                    ControlDirection(inventory,previous);
                    inventory.CalculateBidOffer(marketBid, marketOffer);
                    CompareBidOffer(inventory);
                }
            }
        }

        private InventoryGroupDefault TryResumePausedInventory()
        {
            for (var current = inventories.First; current != null; current = current.Next)
            {
                var inventory = current.Value;
                if (inventory.Status == InventoryStatus.Paused)
                {
                    inventory.Resume();
                    active.AddLast(inventory);
                    return inventory;
                }
            }
            return null;
        }

        private void ControlDirection(InventoryGroupDefault inventory, InventoryGroupDefault last)
        {
            switch (last.Status)
            {
                case InventoryStatus.Flat:
                    inventory.Type = InventoryType.Either;
                    break;
                case InventoryStatus.Long:
                    inventory.Type = InventoryType.Short;
                    break;
                case InventoryStatus.Short:
                    inventory.Type = InventoryType.Long;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Unexpected inventory status: " + last.Status);
            }
        }

        private InventoryGroupDefault AddNewInventory()
        {
            var id = active.Count + 1;
            var inventory = new InventoryGroupDefault(symbol,id);
            active.AddLast(inventory);
            inventories.AddLast(inventory);
            ApplySettings();
            return inventory;
        }

        private void CompareBidOffer(InventoryGroupDefault inventory)
        {
            if (inventory.Bid > bidOwner.Bid && inventory.BidSize > 0)
            {
                bidOwner = inventory;
            }
            if (inventory.Offer < offerOwner.Offer && inventory.OfferSize > 0)
            {
                offerOwner = inventory;
            }
        }

        public void Change(double price, int positionChange)
        {
            InventoryGroupDefault changeInventory;
            if( positionChange > 0)
            {
                changeInventory = bidOwner;
                //if( maxInventories > 1) AdjustBidOffset();
            }
            else if( positionChange < 0)
            {
                changeInventory = offerOwner;
                //if (maxInventories > 1) AdjustOfferOffset();
            }
            else
            {
                throw new InvalidOperationException("Change position must be either greater or less than zero.");
            }
            changeInventory.Change(price, positionChange);
            if( changeInventory.Status == InventoryStatus.Flat)
            {
                changeInventory.Pause();
                active.Remove(changeInventory);
                EnsureActiveInventory();
            }
        }

        private void EnsureActiveInventory()
        {
            if( active.Count == 0)
            {
                var inventory = TryResumePausedInventory();
                inventory.Type = InventoryType.Either;
                if( inventory == null)
                {
                    throw new InvalidOperationException("Unable to find a pause inventory.");
                }
            }
        }

        public int Size
        {
            get
            {
                int result = 0;
                for(var current = inventories.First; current != null; current = current.Next)
                {
                    var inventory = current.Value;
                    result += inventory.Size;
                }
                return result;
            }
        }

        public string ToHeader()
        {
            var sb = new StringBuilder();
            for (var current = inventories.First; current != null; current = current.Next)
            {
                var inventory = current.Value;
                sb.Append(",");
                sb.Append(inventory.ToHeader());
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (var current = inventories.First; current != null; current = current.Next)
            {
                var inventory = current.Value;
                sb.Append(",");
                sb.Append(inventory.ToString());
            }
            return sb.ToString();
        }

        public double CumulativeProfit
        {
            get
            {
                double result = 0D;
                for (var current = inventories.First; current != null; current = current.Next)
                {
                    var inventory = current.Value;
                    result += inventory.CumulativeProfit;
                }
                return result;
            }
        }

        public double CurrentProfitLoss(double price)
        {
            double result = 0D;
            for (var current = inventories.First; current != null; current = current.Next)
            {
                var inventory = current.Value;
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

        public double Bid
        {
            get { return bidOwner.Bid; }
        }

        public double Offer
        {
            get { return offerOwner.Offer; }
        }

        public int BidSize
        {
            get { return bidOwner.BidSize; }
        }

        public int OfferSize
        {
            get { return offerOwner.OfferSize; }
        }

        public InventoryType Type
        {
            get { return type; }
            set { 
                throw new NotImplementedException("Cannot change type of master inventory.");
            }
        }

        private void ApplySettings()
        {
            for (var current = active.First; current != null; current = current.Next)
            {
                var inventory = current.Value;
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