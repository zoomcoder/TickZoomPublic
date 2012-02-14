using System;
using TickZoom.Api;

namespace TickZoom.FIX
{
    public class SimulatorInfo : LogAware
    {
        private static Log log = Factory.SysLog.GetLogger(typeof(FIXSimulatorSupport));
        private volatile bool debug;
        private volatile bool trace;
        private volatile bool verbose;
        public virtual void RefreshLogLevel()
        {
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
            verbose = log.IsVerboseEnabled;
        }
        public SimulatorType Type;
        public int MaxFailures;
        public bool Enabled;
        public bool Active;
        public int Frequency = 50;
        public int NextSequence = 100;
        public int Counter;
        private Random random;
        private Func<int> getSymbolCount;
        public SimulatorInfo( SimulatorType type, Random random, Func<int> getSymbolCount)
        {
            log.Register(this);
            this.Type = type;
            this.random = random;
            this.getSymbolCount = getSymbolCount;
        }
        public void UpdateNext(int sequence)
        {
            NextSequence = sequence + random.Next(Frequency * getSymbolCount()) + Frequency;
            if (debug) log.Debug("Set " + Type + " sequence for = " + NextSequence);
        }
        public bool CheckSequence(int sequence)
        {
            var result = Enabled && Counter < MaxFailures && sequence >= NextSequence;
            if( result && debug)
            {
                log.Debug("Sequence " + sequence + " >= " + Type + " sequence " + NextSequence + " so causing negative test.");
            }
            return result;
        }
        public bool CheckFrequency()
        {
            var result = Enabled && Counter < MaxFailures && random.Next(Frequency * getSymbolCount()) == 1;
            if( result)
            {
                log.Debug("Random " + Type + " occured so causing negative test.");
            }
            return result;
        }
        public override string ToString()
        {
            return Type.ToString();
        }
    }
    public enum SimulatorType
    {
        ReceiveDisconnect,
        SendDisconnect,
        SendServerOffline,
        ReceiveServerOffline,
        BlackHole,
        CancelBlackHole,
        ReceiveFailed,
        SystemOffline
    }
}