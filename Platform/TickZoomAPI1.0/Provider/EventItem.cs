namespace TickZoom.Api
{
    public struct EventItem
    {
        public Receiver Receiver;
        public SymbolInfo Symbol;
        public int EventType;
        public object EventDetail;
        public Provider Recipient;
        public Task RecipientTask;

        public EventItem( SymbolInfo symbol, int eventType, object detail)
        {
            this.Receiver = null;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(SymbolInfo symbol, int eventType)
        {
            this.Receiver = null;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(int eventType, object detail)
        {
            this.Receiver = null;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(int eventType)
        {
            this.Receiver = null;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(Receiver receiver, SymbolInfo symbol, int eventType, object detail)
        {
            this.Receiver = receiver;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(Receiver receiver, SymbolInfo symbol, int eventType)
        {
            this.Receiver = receiver;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(Receiver receiver, int eventType, object detail)
        {
            this.Receiver = receiver;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(Receiver receiver, int eventType)
        {
            this.Receiver = receiver;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public override string ToString()
        {
            var result = Receiver == null ? "" : Receiver.ToString();
            result += Symbol + " " + (EventType) EventType + " " + EventDetail;
            if( Recipient != null)
            {
                result += " " + Recipient;
            }
            if (RecipientTask != null)
            {
                result += " " + RecipientTask;
            }
            return result;

        }
    }
}