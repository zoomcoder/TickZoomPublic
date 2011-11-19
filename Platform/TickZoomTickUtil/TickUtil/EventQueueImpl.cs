using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public class EventQueueImpl : FastQueueImpl<EventItem>, EventQueue
    {
        private SymbolInfo symbol;
        public EventQueueImpl(SymbolInfo symbol, string name)
            : base(name + "." + symbol)
        {
            this.symbol = symbol;
        }

        public SymbolInfo Symbol
        {
            get { return symbol; }
        }
    }
}