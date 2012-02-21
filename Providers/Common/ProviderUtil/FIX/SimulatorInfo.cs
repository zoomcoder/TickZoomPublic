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
        public int Frequency = 50;
        public int NextSequence = 100;
        private int counter;
        private Random random;
        private Func<int> getSymbolCount;

        // Repetitions
        private bool isRepeating;
        private int repeatCounter;
        private int repleatCurrentMax;
        public int MaxRepetitions = 0;
        private SymbolInfo repeatingSymbol;

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
            var result = Enabled && counter < MaxFailures && sequence >= NextSequence;
            if( result)
            {
                ++counter;
                if (debug) log.Debug("Sequence " + sequence + " >= " + Type + " sequence " + NextSequence + " so causing negative test.");
            }
            return result;
        }

        public bool CheckFrequencyAndSymbol(SymbolInfo symbol)
        {
            var result = false;
            if (!Enabled) return result;
            if (isRepeating)
            {
                if( symbol != null && symbol.BinaryIdentifier != repeatingSymbol.BinaryIdentifier)
                {
                    return false;
                }
                repeatCounter++;
                if (repeatCounter < repleatCurrentMax)
                {
                    if( debug)
                    {
                        var symbolText = symbol != null ? "For " + symbol + ": " : "";
                        log.Debug(symbolText + "Repeating " + Type + " negative test. Repeat count " + repeatCounter);
                    }
                    result = true;
                }
                else
                {
                    isRepeating = false;
                }
                return result;
            }
            var symbolCount = getSymbolCount();
            var randomValue = random.Next(Frequency * symbolCount);
            result = counter < MaxFailures && randomValue == 1;
            if (result)
            {
                ++counter;
                if( debug)
                {
                    var symbolText = symbol != null ? "For " + symbol + ": " : "";
                    log.Debug(symbolText + "Random " + Type + " occured so causing negative test.");
                }
                if (MaxRepetitions > 0)
                {
                    repleatCurrentMax = random.Next(MaxRepetitions);
                    repeatCounter = 0;
                    isRepeating = true;
                    repeatingSymbol = symbol;
                }
            }
            return result;
        }

        public bool CheckFrequency()
        {
            return CheckFrequencyAndSymbol(null);
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
        SystemOffline,
        RejectSymbol
    }
}