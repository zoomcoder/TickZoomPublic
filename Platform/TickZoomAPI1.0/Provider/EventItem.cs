namespace TickZoom.Api
{
    public struct EventItem
    {
        public Agent Agent;
        public SymbolInfo Symbol;
        public EventType EventType;
        public object EventDetail;
        public Agent Recipient;
        public Task RecipientTask;

        public EventItem( SymbolInfo symbol, EventType eventType, object detail)
        {
            this.Agent = null;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(SymbolInfo symbol, EventType eventType)
        {
            this.Agent = null;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(EventType eventType, object detail)
        {
            this.Agent = null;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(EventType eventType)
        {
            this.Agent = null;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(Agent agent, SymbolInfo symbol, EventType eventType, object detail)
        {
            this.Agent = agent;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(Agent agent, SymbolInfo symbol, EventType eventType)
        {
            this.Agent = agent;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(Agent agent, EventType eventType, object detail)
        {
            this.Agent = agent;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public EventItem(Agent agent, EventType eventType)
        {
            this.Agent = agent;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
            this.RecipientTask = null;
        }

        public override string ToString()
        {
            var result = Agent == null ? "" : Agent.ToString();
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