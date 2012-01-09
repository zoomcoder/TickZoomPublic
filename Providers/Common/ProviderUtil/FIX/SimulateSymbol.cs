using System;
using TickZoom.Api;

namespace TickZoom.FIX
{
    public interface SimulateSymbol : AgentPerformer
    {
        bool IsOnline { get; set; }
        int ActualPosition { get; }
        FillSimulator FillSimulator { get; }
        SymbolInfo Symbol { get; }
        void CreateOrder(CreateOrChangeOrder order);
        void TryProcessAdjustments();
        void ChangeOrder(CreateOrChangeOrder order);
        void CancelOrder(CreateOrChangeOrder order);
        CreateOrChangeOrder GetOrderById(string clientOrderId);
    }
}