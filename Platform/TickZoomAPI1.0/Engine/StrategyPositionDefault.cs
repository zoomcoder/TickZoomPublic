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

        public StrategyPositionDefault(int id, SymbolInfo symbol)
        {
            this._id = id;
            this._symbol = symbol;
            if( trace) log.Trace("New StrategyPosition");
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
            if (trace) log.Trace("SetExpectedPosition() strategy " + Id + " for " + Symbol + " position change from " + this.position + " to " + position + ".");
            this.position = position;
        }

        public void TrySetPosition( long position)
        {
            if (position != this.position)
            {
                if (debug) log.Debug("Strategy " + _id + " for " + _symbol + " actual position changed from " + this.position + " to " + position + ".");
                this.position = position;
            }
            else
            {
                if (debug) log.Debug("Unchanged strategy " + _id + " for " + _symbol + ". Actual position " + this.position + ".");
            }
        }

        public override string ToString()
        {
            return "Strategy " + Id + ", " + _symbol + ", position " + position ;
        }
    }
}