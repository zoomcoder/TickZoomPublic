namespace TickZoom.Api
{
    public interface StrategyPosition
    {
        long ExpectedPosition { get; }
        SymbolInfo Symbol { get; }
        int Id { get; }
        void SetExpectedPosition(long position);
        void TrySetPosition(long position);
    }
}