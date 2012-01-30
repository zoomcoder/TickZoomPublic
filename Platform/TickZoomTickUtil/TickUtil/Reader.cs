#region Copyright
/*
 * Software: TickZoom Trading Platform
 * Copyright 2009 M. Wayne Walter
 * 
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 2.1 of the License, or (at your option) any later version.
 * 
 * Business use restricted to 30 days except as otherwise stated in
 * in your Service Level Agreement (SLA).
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, see <http://www.tickzoom.org/wiki/Licenses>
 * or write to Free Software Foundation, Inc., 51 Franklin Street,
 * Fifth Floor, Boston, MA  02110-1301, USA.
 * 
 */
#endregion

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public abstract class Reader
	{
		BackgroundWorker backgroundWorker;
	    private Log log;
	    private bool debug;
	    private bool trace;
		long progressDivisor = 1;
		private Elapsed sessionStart = new Elapsed(8, 0, 0);
		private Elapsed sessionEnd = new Elapsed(12, 0, 0);
		bool excludeSunday = true;
		bool logProgress = false;
		private Agent agent;
		protected Task fileReaderTask;
		private static object readerListLocker = new object();
		private static List<Reader> readerList = new List<Reader>();
		Progress progress = new Progress();
		private Pool<TickBinaryBox> tickBoxPool;
		private TickBinaryBox box;
	    private int diagnoseMetric;
	    private TickFile tickFile;

		public Reader()
		{
			lock(readerListLocker) {
				readerList.Add(this);
			}
		    tickFile = Factory.TickUtil.TickFile();
		}

        public void Initialize(Task task)
        {
            this.fileReaderTask = task;
            fileReaderTask.Scheduler = Scheduler.RoundRobin;
            fileReaderTask.Start();
        }

		bool CancelPending {
			get { return backgroundWorker != null && backgroundWorker.CancellationPending; }
		}

		[Obsolete("Pass symbol string instead of SymbolInfo", true)]
		public void Initialize(string _folder, SymbolInfo symbolInfo)
		{
			throw new NotImplementedException("Please use the Initialize() method with a string for the symbol which gets used as part of the file name.");
		}

		public void Initialize(string folderOrfile, string symbolFile)
		{
            tickFile.Initialize(folderOrfile, symbolFile, TickFileMode.Read);
			PrepareTask();
		}


		public void Initialize(string fileName)
		{
		    tickFile.Initialize(fileName,TickFileMode.Read);
			PrepareTask();
		}

		public void GetLastTick(TickIO tickIO)
		{
            tickFile.GetLastTick(tickIO);
		}

        private SimpleLock startLocker = new SimpleLock();
		public virtual void Start(EventItem eventItem)
		{
            using (startLocker.Using())
            {
                if (!isTaskPrepared)
                {
    				throw new ApplicationException("Read must be Initialized before Start() can be called and after GetLastTick() the reader must be disposed.");
	    		}
                if (!isStarted)
                {
                    this.agent = eventItem.Agent;
                    var symbol = tickFile.Symbol;
                    if (debug) log.Debug("Start called.");
                    start = Factory.TickCount;
                    diagnoseMetric = Diagnose.RegisterMetric("Reader." + symbol.Symbol.StripInvalidPathChars());
                    var tempQueue = Factory.Parallel.FastQueue<int>(symbol + " Reader Nominal Queue");
                    var tempConnectionId = 0;
                    fileReaderTask.ConnectInbound(tempQueue, out tempConnectionId);
                    fileReaderTask.IncreaseInbound(tempConnectionId,0L);
                    isStarted = true;
                }
            }
		}

		private void OnException(Exception ex)
		{
			ErrorDetail detail = new ErrorDetail();
			detail.ErrorMessage = ex.ToString();
            log.Error(detail.ErrorMessage);
		}

		public void Stop(EventItem eventItem)
		{
			if (debug) log.Debug("Stop(" + agent + ")");
			Dispose();
		}

		public bool LogTicks = false;

		TickImpl tickIO = new TickImpl();
		long lastTime = 0;

		TickBinary tick = new TickBinary();
		bool isDataRead = false;
		bool isFirstTick = true;
		long nextUpdate = 0;
	    private long count = 0;
		protected volatile int tickCount = 0;
		long start;
		bool isTaskPrepared = false;
		bool isStarted = false;
		
		private void PrepareTask()
		{
		    var symbol = tickFile.Symbol;
	        log = Factory.SysLog.GetLogger("TickZoom.TickUtil.Reader."+symbol.Symbol.StripInvalidPathChars());
	        debug = log.IsDebugEnabled;
	        trace = log.IsTraceEnabled;
		    tickBoxPool = Factory.Parallel.TickPool(symbol);
            progressDivisor = tickFile.Length / 20;
            progressCallback("Loading bytes...", tickFile.Position, tickFile.Length);
            isTaskPrepared = true;
		}

        void LogInfo(string logMsg)
        {
            if (!tickFile.QuietMode)
            {
                log.Notice(logMsg);
            }
            else
            {
                log.Debug(logMsg);
            }
        }
		
		public virtual Yield Invoke()
		{
            EventItem eventItem;
            if( fileReaderTask.Filter.Receive(out eventItem))
            {
                switch( eventItem.EventType)
                {
                    default:
                        throw new ApplicationException("Unexpected event: " + eventItem);
                }
            }
            if (!isStarted) return Yield.NoWork.Repeat;
            lock (taskLocker)
            {
				if (isDisposed)
				{
                    return Yield.Terminate;
                }
				try
				{
				    var loopCount = 0;
                    while (!CancelPending && loopCount < 1000)
					{
                        ++loopCount;
                        if( !tickFile.TryReadTick(tickIO) )
                        {
                            if (debug) log.Debug("Finished reading to file length: " + tickFile.Length);
                            return SendFinish();
                        }

						tick = tickIO.Extract();
						isDataRead = true;

						if (Factory.TickCount > nextUpdate) {
							try {
								progressCallback("Loading bytes...", tickFile.Position, tickFile.Length);
							} catch (Exception ex) {
								log.Debug("Exception on progressCallback: " + ex.Message);
							}
							nextUpdate = Factory.TickCount + 2000;
						}

						if (MaxCount > 0 && Count > MaxCount) {
							if (debug)
								log.Debug("Ending data read because count reached " + MaxCount + " ticks.");
						    return SendFinish();
						}

						if (IsAtEnd(tick))
						{
						    return SendFinish();
						}

						if (IsAtStart(tick)) {
							count = Count + 1;
							if (debug && Count < 10) {
								log.Debug("Read a tick " + tickIO);
							} else if (trace) {
								log.Trace("Read a tick " + tickIO);
							}
                            tick.Symbol = tickFile.Symbol.BinaryIdentifier;

							if (tick.UtcTime <= lastTime) {
								tick.UtcTime = lastTime + 1;
							}
							lastTime = tick.UtcTime;

							if (isFirstTick) {
								isFirstTick = false;
							    StartEvent();
							} else {
								tickCount++;
							}
							
							box = tickBoxPool.Create();
						    var tickId = box.TickBinary.Id;
							box.TickBinary = tick;
						    box.TickBinary.Id = tickId;

						    TickEvent();
						}
						tickCount++;

					}
				}
                catch (ObjectDisposedException)
				{
				    return SendFinish();
				}
				return Yield.DidWork.Repeat;
			}
		}

        private void StartEvent()
		{
		    var item = new EventItem(tickFile.Symbol,EventType.StartHistorical);
            agent.SendEvent(item);
            LogInfo("Starting loading for " + tickFile.Symbol + " from " + tickIO.ToPosition());
		}

		private void TickEvent()
		{
			if( box == null) {
				throw new ApplicationException("Box is null.");
			}
            var item = new EventItem(tickFile.Symbol, EventType.Tick, box);
            agent.SendEvent(item);
            if (Diagnose.TraceTicks) Diagnose.AddTick(diagnoseMetric, ref box.TickBinary);
            box = null;
		}

		private Yield SendFinish()
		{
            var item = new EventItem(tickFile.Symbol, EventType.EndHistorical);
            agent.SendEvent(item);
            if (debug) log.Debug("EndHistorical for " + tickFile.Symbol);
			try {
				if (isDataRead) {
                    LogInfo("Processing ended for " + tickFile.Symbol + " at " + tickIO.ToPosition());
				}
				long end = Factory.TickCount;
				LogInfo("Processed " + Count + " ticks in " + (end - start) + " ms.");
				try {
					progressCallback("Processing complete.", tickFile.Length, tickFile.Length);
				} catch (Exception ex) {
					log.Debug("Exception on progressCallback: " + ex.Message);
				}
				if (debug)
					log.Debug("calling Agent.OnEvent(symbol,EventType.EndHistorical)");
			} catch (ThreadAbortException) {

			} catch (FileNotFoundException ex) {
				log.Error("ERROR: " + ex.Message);
			} catch (Exception ex) {
				log.Error("ERROR: " + ex);
			} finally {
                Dispose();
			}
			return Yield.Terminate;
		}

        private volatile bool isFinalized;
        public bool IsFinalized
        {
            get { return isFinalized; }
        }

        private volatile bool isDisposed = false;
        private object taskLocker = new object();
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!isDisposed) {
				isDisposed = true;
				lock (taskLocker) {
					if (fileReaderTask != null) {
						fileReaderTask.Stop();
						fileReaderTask.Join();
					}
					if (tickFile != null) {
						tickFile.Dispose();
					}
					lock( readerListLocker) {
						readerList.Remove(this);
					}
				    isFinalized = true;
				}
			}
		}

		public void CloseAll()
		{
			lock( readerListLocker) {
				for (int i = 0; i < readerList.Count; i++) {
					readerList[i].Dispose();
				}
				readerList.Clear();
			}
		}

		void progressCallback(string text, Int64 current, Int64 final)
		{
            if( final > 0 && final > current)
            {
                var percent = current * 100 / final;
                log.Info(tickFile.Symbol + ": " + text + " " + percent + "% complete.");
            }
            else
            {
                log.Info(tickFile.Symbol + ": " + text);
            }
            if (backgroundWorker != null && !backgroundWorker.CancellationPending && backgroundWorker.WorkerReportsProgress)
            {
				progress.UpdateProgress(text, current, final);
				backgroundWorker.ReportProgress(0, progress);
			}
		}

        public bool IsAtEnd(TickBinary tick)
        {
            return tick.UtcTime >= tickFile.EndTime.Internal;
        }

        public bool IsAtStart(TickBinary tick)
        {
            return tick.UtcTime > tickFile.StartTime.Internal && tickCount >= tickFile.StartCount;
        }

        public long StartCount
        {
            get { return tickFile.StartCount; }
            set { tickFile.StartCount = value; }
        }

        public TimeStamp StartTime
        {
            get { return tickFile.StartTime; }
            set { tickFile.StartTime = value; }
        }

        public TimeStamp EndTime
        {
            get { return tickFile.EndTime; }
            set { tickFile.EndTime = value; }
        }

        public BackgroundWorker BackgroundWorker {
			get { return backgroundWorker; }
			set { backgroundWorker = value; }
		}

		public Elapsed SessionStart {
			get { return sessionStart; }
			set { sessionStart = value; }
		}

		public Elapsed SessionEnd {
			get { return sessionEnd; }
			set { sessionEnd = value; }
		}

		public bool ExcludeSunday {
			get { return excludeSunday; }
			set { excludeSunday = value; }
		}

		public string FileName {
			get { return tickFile.FileName; }
		}

		public SymbolInfo Symbol {
            get { return tickFile.Symbol; }
		}

		public bool LogProgress {
			get { return logProgress; }
			set { logProgress = value; }
		}

		public long MaxCount {
			get { return tickFile.MaxCount; }
			set { tickFile.MaxCount = value; }
		}

		public bool QuietMode {
			get { return tickFile.QuietMode; }
			set { tickFile.QuietMode = value; }
		}

		public TickIO LastTick {
			get { return tickIO; }
		}

	    public long Count
	    {
	        get { return count; }
	    }

        public int DataVersion
        {
            get { return tickFile.DataVersion; }
        }

	}
}
