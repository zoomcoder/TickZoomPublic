namespace TickZoom.Api
{
    public interface StrategyPosition
    {
        long Recency { get; }
        long ExpectedPosition { get; }
        SymbolInfo Symbol { get; }
        int Id { get; }
        void SetExpectedPosition(long position);
        void TrySetPosition(long position, long recency);
    }
}