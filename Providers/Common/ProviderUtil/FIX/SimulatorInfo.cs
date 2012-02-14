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
        private int Frequency = 50;
        public int NextSequence = 100;
        public int Counter;
        public SimulatorInfo( SimulatorType type)
        {
            log.Register(this);
            this.Type = type;
        }
        public void UpdateNext(int sequence, Random random, int handlersCount)
        {
            NextSequence = sequence + random.Next(Frequency * handlersCount) + Frequency;
            if (debug) log.Debug("Set " + ToString() + " sequence for = " + NextSequence);
        }
        public bool CheckSequence(int sequence)
        {
            return Enabled && Counter < MaxFailures && sequence >= NextSequence;
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
        ReceiveFailed,
    }
}