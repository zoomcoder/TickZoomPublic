using System;

namespace TickZoom.Api
{
    public class StrategyPositionDefault : StrategyPosition
    {
        private static readonly Log log = Factory.SysLog.GetLogger(typeof (StrategyPositionDefault));
        private static readonly bool debug = log.IsDebugEnabled;
        private static readonly bool trace = log.IsTraceEnabled;
        private int _id;
        private SymbolInfo _symbol;
        private long position;
        private long recency;

        public StrategyPositionDefault(int id, SymbolInfo symbol)
        {
            this._id = id;
            this._symbol = symbol;
            if( trace) log.Trace("New StrategyPosition");
        }

        public long Recency
        {
            get { return recency; }
            set { recency = value; }
        }

        public long ExpectedPosition
        {
            get { return this.position; }
        }

        public SymbolInfo Symbol
        {
            get { return _symbol; }
        }

        public int Id
        {
            get { return _id; }
        }

        public void SetExpectedPosition(long position)
        {
            if (trace) log.Trace("SetExpectedPosition() strategy " + Id + " for " + Symbol + " position change from " + this.position + " to " + position + ". Recency " + this.recency + " to " + recency);
            this.position = position;
        }

        public void TrySetPosition( long position, long recency)
        {
            if (recency == 0L)
            {
                throw new InvalidOperationException("Recency must be non-zero.");
            }
            if (recency >= this.recency)
            {
                if (position != this.position)
                {
                    if (debug) log.Debug("Strategy " + _id + " for " + _symbol + " actual position changed from " + this.position + " to " + position + ". Recency " + this.recency + " to " + recency);
                    this.position = position;
                }
                else
                {
                    if (debug) log.Debug("Unchanged strategy " + _id + " for " + _symbol + ". Actual position " + this.position + ". Recency " + this.recency + " to " + recency);
                }
                this.recency = recency;
            }
            else if (position != this.position)
            {
                if (debug) log.Debug("Rejected change of strategy " + Id + " for " + Symbol + " actual position " + this.position + " to " + position + ".  Recency " + recency + " wasn't newer than " + this.recency);
            }
        }

        public override string ToString()
        {
            return "Strategy " + Id + ", " + _symbol + ", position " + position + ", recency " + recency;
        }
    }
}