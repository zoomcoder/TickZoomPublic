using System;
using System.Collections.Generic;

namespace TickZoom.Api
{
    public interface PhysicalOrderCache : IDisposable
    {
        void SetOrder(CreateOrChangeOrder order);
        CreateOrChangeOrder RemoveOrder(string clientOrderId);
        IEnumerable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol);
        IEnumerable<CreateOrChangeOrder> GetOrders(Func<CreateOrChangeOrder, bool> select);
        bool TryGetOrderById(string brokerOrder, out CreateOrChangeOrder order);
        bool TryGetOrderBySequence(int sequence, out CreateOrChangeOrder order);
        CreateOrChangeOrder GetOrderById(string brokerOrder);
        bool TryGetOrderBySerial(long logicalSerialNumber, out CreateOrChangeOrder order);
        CreateOrChangeOrder GetOrderBySerial(long logicalSerialNumber);
        bool HasCancelOrder(PhysicalOrder order);
        bool HasCreateOrder(CreateOrChangeOrder order);
        void ResetLastChange();
        void SetActualPosition(SymbolInfo symbol, long position);
        long GetActualPosition(SymbolInfo symbol);
        long IncreaseActualPosition(SymbolInfo symbol, long increase);
        void SetStrategyPosition(SymbolInfo symbol, int strategyId, long position);
        long GetStrategyPosition(int strategyId);
        void SyncPositions(Iterable<StrategyPosition> strategyPositions);
        string StrategyPositionsToString();
        string SymbolPositionsToString();
        string OrdersToString();
        List<CreateOrChangeOrder> GetOrdersList(Func<CreateOrChangeOrder, bool> func);
        void CheckForExcessiveRejects(CreateOrChangeOrder order, string reason);
        void ClearRejects();
    }

    public interface PhysicalOrderStore : PhysicalOrderCache
    {
        string DatabasePath { get; }
        long SnapshotRolloverSize { get; set; }
        int RemoteSequence { get; }
        int LocalSequence { get; }
        void RequestSnapshot();
        void WaitForSnapshot();
        void ForceSnapshot();
        bool Recover();
        void UpdateLocalSequence(int localSequence);
        void UpdateRemoteSequence(int remoteSequence);
        void SetSequences(int remoteSequence, int localSequence);
        TimeStamp LastSequenceReset { get; set; }
        bool IsBusy { get; }
        int Count();
        void IncrementUpdateCount();
        void TrySnapshot();
        IDisposable BeginTransaction();
        void EndTransaction();
        void AssertAtomic();
    }
}