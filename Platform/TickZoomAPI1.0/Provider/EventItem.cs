namespace TickZoom.Api
{
    public struct EventItem
    {
        public Agent Agent;
        public SymbolInfo Symbol;
        public EventType EventType;
        public object EventDetail;
        public Agent Recipient;

        public EventItem( SymbolInfo symbol, EventType eventType, object detail)
        {
            this.Agent = null;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
        }

        public EventItem(SymbolInfo symbol, EventType eventType)
        {
            this.Agent = null;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
        }

        public EventItem(EventType eventType, object detail)
        {
            this.Agent = null;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
        }

        public EventItem(EventType eventType)
        {
            this.Agent = null;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
        }

        public EventItem(AgentPerformer performer, SymbolInfo symbol, EventType eventType, object detail) : this(Factory.Parallel.GetAgent(performer), symbol, eventType, detail) { }

        public EventItem(Agent sender, SymbolInfo symbol, EventType eventType, object detail)
        {
            this.Agent = sender;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
        }

        public EventItem(AgentPerformer performer, SymbolInfo symbol, EventType eventType) : this(Factory.Parallel.GetAgent(performer), symbol, eventType) { }

        public EventItem(Agent sender, SymbolInfo symbol, EventType eventType)
        {
            this.Agent = sender;
            this.Symbol = symbol;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
        }

        public EventItem(AgentPerformer performer, EventType eventType, object detail) : this(Factory.Parallel.GetAgent(performer), eventType, detail) { }

        public EventItem(Agent sender, EventType eventType, object detail)
        {
            this.Agent = sender;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = detail;
            this.Recipient = null;
        }

        public EventItem(AgentPerformer sender, EventType eventType) : this( Factory.Parallel.GetAgent(sender), eventType)
        {

        }

        public EventItem(Agent sender, EventType eventType)
        {
            this.Agent = sender;
            this.Symbol = null;
            this.EventType = eventType;
            this.EventDetail = null;
            this.Recipient = null;
        }

        public override string ToString()
        {
            var result = Agent == null ? "" : Agent + " ";
            result += Symbol + " " + EventType + " " + EventDetail;
            if( Recipient != null)
            {
                result += " " + Recipient;
            }
            return result;

        }
    }
}