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
	/// <summary>
	/// Description of TickArray.
	/// </summary>
	public class TickWriterDefault : TickWriter
	{
		private BackgroundWorker backgroundWorker;
   		private int maxCount = 0;
   		private SymbolInfo symbol = null;
		private Task appendTask = null;
		protected TickQueue writeQueue;
		private static readonly Log log = Factory.SysLog.GetLogger(typeof(TickWriterDefault));
		private readonly bool debug = log.IsDebugEnabled;
		private readonly bool trace = log.IsTraceEnabled;
		private bool logProgress = false;
		private bool isInitialized = false;
		private string priceDataFolder;
		private string appDataFolder;
		private Progress progress = new Progress();
	    private TickFile tickFile;
	    private bool eraseFileToStart;
		
		public TickWriterDefault(bool eraseFileToStart)
		{
		    this.eraseFileToStart = eraseFileToStart;
            tickFile = Factory.TickUtil.TickFile();
			writeQueue = Factory.Parallel.TickQueue(typeof(TickWriter));
			writeQueue.StartEnqueue = Start;
			var property = "PriceDataFolder";
			priceDataFolder = Factory.Settings[property];
			if (priceDataFolder == null) {
				throw new ApplicationException("Must set " + property + " property in app.config");
			}
			property = "AppDataFolder";
			appDataFolder = Factory.Settings[property];
			if (appDataFolder == null) {
				throw new ApplicationException("Must set " + property + " property in app.config");
			}
		}
		
		public void Start() {
			
		}
		
		public void Pause() {
			log.Notice("Disk I/O for " + symbol + " is temporarily paused.");
			appendTask.Pause();
		}
		
		public void Resume() {
			log.Notice("Disk I/O for " + symbol + " has resumed.");
		}
		
		bool CancelPending {
			get { return backgroundWorker !=null && backgroundWorker.CancellationPending; }
		}
		
		public void Initialize(string folderOrfile, string _symbol) {
            tickFile.Initialize(folderOrfile, _symbol, TickFileMode.Write);
            if (!CancelPending)
            {
				StartAppendThread();
			}
			isInitialized = true;
		}

        [Obsolete("Please pass string symbol instead of SymbolInfo.", true)]
		public void Initialize(string _folder, SymbolInfo _symbol) {
			isInitialized = false;
		}
		
		[Obsolete("Please call Initialize( folderOrfile, symbol) instead.",true)]
		public void Initialize(string filePath) {
			isInitialized = false;
		}
		
		private void OnException( Exception ex) {
			log.Error( ex.Message, ex);
		}

	    private QueueFilter filter;

		protected virtual void StartAppendThread() {
			string baseName = Path.GetFileNameWithoutExtension(tickFile.FileName);
			appendTask = Factory.Parallel.Loop(baseName + " writer",OnException, Invoke);
		    filter = appendTask.GetFilter();
			appendTask.Scheduler = Scheduler.EarliestTime;
			writeQueue.ConnectInbound(appendTask);
			appendTask.Start();
		}
		
		TickBinary tick = new TickBinary();
		TickIO tickIO = new TickImpl();

	    private long appendCounter = 0;
		
		protected virtual Yield Invoke()
		{
		    EventItem eventItem;
            if( filter.Receive(out eventItem))
            {
                switch( (EventType) eventItem.EventType)
                {
                    case EventType.Shutdown:
                        Dispose();
                        filter.Pop();
                        break;
                    default:
                        throw new ApplicationException("Unexpected event: " + eventItem);
                }
            }
			var result = Yield.NoWork.Repeat;
			try {
				if( writeQueue.Count == 0) {
					return result;
				}
				while( writeQueue.Count > 0) {
                    if (!writeQueue.TryPeek(ref tick))
                    {
						break;
    				}
		    		tickIO.Inject(tick);
                    if( tickFile.TryWriteTick(tickIO))
                    {
                        writeQueue.TryDequeue(ref tick);
                    }
                    result = Yield.DidWork.Repeat;
				}
	    		return result;
		    } catch (QueueException ex) {
				if( ex.EntryType == EventType.Terminate) {
                    log.Notice("Last tick written: " + tickIO);
                    if( debug) log.Debug("Exiting, queue terminated.");
					Finalize();
					return Yield.Terminate;
				} else {
					Exception exception = new ApplicationException("Queue returned unexpected: " + ex.EntryType);
					writeQueue.SetException(exception);
					writeQueue.Dispose();
					Dispose();
					throw ex;
				}
			} catch( Exception ex) {
				writeQueue.SetException(ex);
				writeQueue.Dispose();
				Dispose();
				throw;
    		}
		}

        public void Flush()
        {
            if( debug) log.Debug("Before flush write queue " + writeQueue.Count);
            tickFile.Flush();
            if (debug) log.Debug("After flush write queue " + writeQueue.Count);
        }

	    private long tickCount = 0;
		
		public void Add(TickIO tick) {
			while( !TryAdd(tick)) {
				Thread.Sleep(1);
			}
		}
		
		public bool TryAdd(TickIO tickIO) {
			if( !isInitialized) {
				throw new ApplicationException("Please initialized TickWriter first.");
			}
			TickBinary tick = tickIO.Extract();
			var result = writeQueue.TryEnqueue(ref tick);
            if( result)
            {
                Interlocked.Increment(ref appendCounter);
            }
		    return result;
		}
		
		[Obsolete("Please discontinue use of CanReceive() and simple check the return value of TryAdd() instaed to find out if the add was succesful.",true)]
		public bool CanReceive {
			get {
				return true;
			}
		}
		
		public bool LogTicks = false;
		
		void progressCallback( string text, Int64 current, Int64 final) {
			if( backgroundWorker != null && backgroundWorker.WorkerReportsProgress) {
				progress.UpdateProgress(text,current,final);
				backgroundWorker.ReportProgress(0, progress);
			}
		}
		
		public void Close() {
			Dispose();
		}
		
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private volatile bool isDisposed = false;
		protected virtual void Dispose(bool disposing)
		{
			if (!isDisposed) {
				isDisposed = true;
				if( !isInitialized)
				{
				    return;
				}
				if( debug) log.Debug("Dispose()");
				if( appendTask != null) {
					if( writeQueue != null) {
                        try
                        {
                            if (debug) log.Debug("Sending event " + EventType.Terminate + " to tickwriter queue.");
                            while (!writeQueue.TryEnqueue(EventType.Terminate, symbol))
                            {
                                Thread.Sleep(1);
                            }
                        }
                        catch( QueueException ex)
                        {
                            if( ex.EntryType == EventType.Terminate)
                            {
                                Finalize();
                            }
                            else
                            {
                                throw;
                            }
                        }
					}
				}
			}
		}

	    private bool isFinalized;

        private void Finalize()
        {
            if (debug) log.Debug("Finalize()");
            Flush();
            var count = tickFile.WriteCounter;
            var append = Interlocked.Read(ref appendCounter);
            if (debug) log.Debug("Only " + count + " writes before closeFile but " + append + " appends.");
            if (tickFile != null)
            {
                tickFile.Dispose();
            }
            isFinalized = true;
        }

 		public BackgroundWorker BackgroundWorker {
			get { return backgroundWorker; }
			set { backgroundWorker = value; }
		}
		
		public string FileName {
			get { return tickFile.FileName; }
		}
	    
		public SymbolInfo Symbol {
			get { return symbol; }
		}
		
		public bool LogProgress {
			get { return logProgress; }
			set { logProgress = value; }
		}
   		
		public int MaxCount {
			get { return maxCount; }
			set { maxCount = value; }
		}
		
		public TickQueue WriteQueue {
			get { return writeQueue; }
		}

	    public bool IsFinalized
	    {
	        get { return isFinalized; }
	    }
	}
}
