using System;
using System.Collections.Generic;

namespace TickZoom.Api
{
    public struct PhysicalOrderLock : IDisposable
    {
        private PhysicalOrderStore lockedCache;
        public PhysicalOrderLock(PhysicalOrderStore cache)
        {
            lockedCache = cache;
        }
        public void Dispose()
        {
            lockedCache.Unlock();
        }
    }
    public interface PhysicalOrderCache : IDisposable
    {
        void SetOrder(CreateOrChangeOrder order);
        CreateOrChangeOrder RemoveOrder(string clientOrderId);
        Iterable<CreateOrChangeOrder> GetActiveOrders(SymbolInfo symbol);
        List<CreateOrChangeOrder> GetOrders(Func<CreateOrChangeOrder, bool> select);
        bool TryGetOrderById(string brokerOrder, out CreateOrChangeOrder order);
        bool TryGetOrderBySequence(int sequence, out CreateOrChangeOrder order);
        CreateOrChangeOrder GetOrderById(string brokerOrder);
        bool TryGetOrderBySerial(long logicalSerialNumber, out CreateOrChangeOrder order);
        CreateOrChangeOrder GetOrderBySerial(long logicalSerialNumber);
        bool HasCancelOrder(PhysicalOrder order);
        bool HasCreateOrder(CreateOrChangeOrder order);
        IDisposable Lock();
        void Unlock();
        void ResetLastChange();
        void SetActualPosition(SymbolInfo symbol, long position);
        long GetActualPosition(SymbolInfo symbol);
        long IncreaseActualPosition(SymbolInfo symbol, long increase);
        void SetStrategyPosition(SymbolInfo symbol, int strategyId, long position);
        long GetStrategyPosition(int strategyId);
        void SyncPositions(Iterable<StrategyPosition> strategyPositions);
        string StrategyPositionsToString();
        string SymbolPositionsToString();
        void AssertAtomic();
        string OrdersToString();
    }

    public interface PhysicalOrderStore : PhysicalOrderCache
    {
        string DatabasePath { get; }
        long SnapshotRolloverSize { get; set; }
        int RemoteSequence { get; }
        int LocalSequence { get; }
        void ForceSnapShot();
        void WaitForSnapshot();
        bool Recover();
        void UpdateLocalSequence(int localSequence);
        void UpdateRemoteSequence(int remoteSequence);
        void SetSequences(int remoteSequence, int localSequence);
        TimeStamp LastSequenceReset { get; set; }
        bool IsBusy { get; }
        int Count();
        void IncrementUpdateCount();
    }
}