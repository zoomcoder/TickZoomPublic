using System.Runtime.InteropServices;

namespace TickZoom.Examples
{
    public enum InventoryStatus
    {
        Paused,
        Flat,
        Long,
        Short,
        Complete
    }

    public interface InventoryGroup
    {
        int Size { get; }
        int MinimumLotSize { get; set; }
        int MaximumLotSize { get; set; }
        int RoundLotSize { get; set; }
        double Retrace { get; set; }
        int Goal { get; set; }
        int StartingLotSize { get; set; }
        double CumulativeProfit { get; }
        int ProfitTicks { get; set; }
        double Bid { get; }
        double Offer { get; }
        int OfferSize { get; }
        int BidSize { get; }
        InventoryType Type { get; set; }
        double ExtremePrice { get; }
        double BeginningPrice { get; }
        double BreakEven { get; }
        void CalculateBidOffer(double bid, double offer);
        void Change( double price, int positionChange);
        double CurrentProfitLoss(double price);
        string ToHeader();
        void UpdateBidAsk(double marketBid, double marketAsk);
    }
}