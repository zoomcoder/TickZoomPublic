using System;
using TickZoom.Api;

namespace TickZoom.FIX
{
    public class SimulatorInfo : LogAware
    {
        private static Log log = Factory.SysLog.GetLogger(typeof(SimulatorInfo));
        private volatile bool debug;
        private volatile bool trace;
        private volatile bool verbose;
        private volatile int lastSequence;

        public int Counter
        {
            get { return counter; }
        }

        public long AttemptCounter
        {
            get { return attemptCounter; }
        }

        public int Minimum
        {
            get { return minimum; }
            set { minimum = value; }
        }

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
        public int NextSequence = 0;
        private int attemptCounter;
        private int minimum = 2;
        private int counter;
        private Random random;
        private Func<int> getSymbolCount;

        // Repetitions
        private bool isRepeating;
        private int repeatCounter;
        private int repeatCurrentMax;
        public int MaxRepetitions = 0;
        private SymbolInfo repeatingSymbol;

        public SimulatorInfo( SimulatorType type, Random random, Func<int> getSymbolCount)
        {
            log.Register(this);
            this.Type = type;
            this.random = random;
            this.getSymbolCount = getSymbolCount;
        }

        private void NextRandom(int sequence)
        {
            NextSequence = sequence + random.Next(Frequency * getSymbolCount() / 3) + Frequency;
            if (debug) log.Debug("Set " + Type + " sequence for = " + NextSequence);
        }

        public void UpdateNext(int sequence)
        {
            if( lastSequence == 0)
            {
                NextRandom(sequence);
            }
            else
            {
                var remaining = NextSequence - lastSequence;
                NextSequence = sequence + remaining;
                if (debug) log.Debug("Set " + Type + " sequence for = " + NextSequence);
                lastSequence = sequence;
            }
        }

        public bool CheckSequence(int sequence)
        {
            if( SyncTicks.Frozen) return false;
            if (NextSequence == 0)
            {
                throw new ApplicationException("NextSequence was never initialized.");
            }
            lastSequence = sequence;
            var result = false;
            if (!Enabled) return result;
            ++attemptCounter;
            result = Counter < MaxFailures && sequence >= NextSequence;
            if( result)
            {
                counter = Counter + 1;
                if (debug) log.Debug("Sequence " + sequence + " >= " + Type + " sequence " + NextSequence + " so causing negative test. " +
                    SyncTicks.CurrentTestName + " attempts " + attemptCounter + ", count " + counter);
                NextRandom(sequence);
            }
            return result;
        }

        public bool CheckFrequencyAndSymbol(SymbolInfo symbol)
        {
            if (SyncTicks.Frozen) return false;
            if (NextSequence == 0)
            {
                NextRandom(attemptCounter);
            }
            var result = false;
            if (!Enabled) return result;
            ++attemptCounter;
            if (isRepeating)
            {
                if( symbol != null && symbol.BinaryIdentifier != repeatingSymbol.BinaryIdentifier)
                {
                    return false;
                }
                repeatCounter++;
                if (repeatCounter < repeatCurrentMax)
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
            result = Counter < MaxFailures && attemptCounter >= NextSequence;
            if (result)
            {
                counter = Counter + 1;
                NextRandom(attemptCounter);
                if( debug)
                {
                    var symbolText = symbol != null ? "For " + symbol + ": " : "";
                    if( debug) log.Debug(symbolText + "Random " + Type + " occurred so causing negative test. " + 
                        SyncTicks.CurrentTestName + " attempts " + attemptCounter + ", count " + counter);
                }
                if (MaxRepetitions > 0)
                {
                    repeatCurrentMax = random.Next(MaxRepetitions) + MaxRepetitions;
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
}