using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public enum TickFileMode
    {
        Read,
        Write
    }
    public class TickFile : IDisposable
    {
        private SymbolInfo symbol;
        private long lSymbol;
        private int dataVersion;
        private MemoryStream memory;
        private BinaryReader dataIn = null;
        private FileStream fs = null;
        private byte[] buffer;
        private bool quietMode;
        static object fileLocker = new object();
        bool bulkFileLoad;
        static Dictionary<SymbolInfo, byte[]> fileBytesDict = new Dictionary<SymbolInfo, byte[]>();
        private string fileName;
        private Log log;
        private bool debug;
        private bool trace;
        string appDataFolder;
        string priceDataFolder;
        private bool eraseFileToStart = false;
        private TaskLock memoryLocker = new TaskLock();
        private Action writeFileAction;
        private TickFileMode mode;
        private Stream bufferedStream;
        private long tickCount;
        private long maxCount = long.MaxValue;
        private TimeStamp startTime = TimeStamp.MinValue;
        private TimeStamp endTime = TimeStamp.MaxValue;
        private bool endOfData;

        public TickFile()
        {
            memory = new MemoryStream();
            memory.SetLength(TickImpl.minTickSize);
            buffer = memory.GetBuffer();
            var property = "PriceDataFolder";
            priceDataFolder = Factory.Settings[property];
            if (priceDataFolder == null)
            {
                throw new ApplicationException("Must set " + property + " property in app.config");
            }
            property = "AppDataFolder";
            appDataFolder = Factory.Settings[property];
            if (appDataFolder == null)
            {
                throw new ApplicationException("Must set " + property + " property in app.config");
            }
            writeFileAction = WriteToFile;
        }

        private void InitLogging()
        {
            log = Factory.SysLog.GetLogger("TickZoom.TickUtil.TickFile." + mode + "." + Symbol.Symbol.StripInvalidPathChars());
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
        }

        public void Initialize(string folderOrfile, string symbolFile, TickFileMode mode)
        {
            string[] symbolParts = symbolFile.Split(new char[] { '.' });
            string _symbol = symbolParts[0];
            this.mode = mode;
            symbol = Factory.Symbol.LookupSymbol(_symbol);
            InitLogging();
            var dataFolder = folderOrfile.Contains(@"Test\") ? appDataFolder : priceDataFolder;
            var filePath = dataFolder + "\\" + folderOrfile;
            if (Directory.Exists(filePath))
            {
                fileName = filePath + "\\" + symbolFile.StripInvalidPathChars() + ".tck";
            }
            else if (File.Exists(folderOrfile))
            {
                fileName = folderOrfile;
            }
            else
            {
                Directory.CreateDirectory(filePath);
                fileName = filePath + "\\" + symbolFile.StripInvalidPathChars() + ".tck";
                //throw new ApplicationException("Requires either a file or folder to read data. Tried both " + folderOrfile + " and " + filePath);
            }
            CheckFileExtension();
            if (debug) log.Debug("File Name = " + fileName);
            OpenFile();
        }

        public void Initialize(string fileName, TickFileMode mode)
        {
            this.mode = mode;
            this.fileName = fileName = Path.GetFullPath(fileName);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (Symbol == null)
            {
                symbol = Factory.Symbol.LookupSymbol(baseName.Replace("_Tick", ""));
                lSymbol = Symbol.BinaryIdentifier;
            }
            InitLogging();
            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            CheckFileExtension();
            if (debug) log.Debug("File Name = " + fileName);
            OpenFile();
        }

        private void OpenFile()
        {
            lSymbol = Symbol.BinaryIdentifier;
            switch (mode)
            {
                case TickFileMode.Read:
                    OpenFileForReading();
                    break;
                case TickFileMode.Write:
                    OpenFileForWriting();
                    break;
                default:
                    throw new ApplicationException("Unknown file mode: " + mode);
            }
        }

        private void OpenFileForWriting()
        {
            if (EraseFileToStart)
            {
                File.Delete(fileName);
                log.Notice("TickWriter file was erased to begin writing.");
            }
            fs = new FileStream(fileName, FileMode.Append, FileAccess.Write, FileShare.Read, 1024, FileOptions.WriteThrough);
            log.Debug("OpenFileForWriting()");
            memory = new MemoryStream();
        }

        void LogInfo(string logMsg)
        {
            if (!quietMode)
            {
                log.Notice(logMsg);
            }
            else
            {
                log.Debug(logMsg);
            }
        }

        private string FindFile(string path)
        {
            string directory = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);
            string[] paths = Directory.GetFiles(directory, name, SearchOption.AllDirectories);
            if (paths.Length == 0)
            {
                return null;
            }
            else if (paths.Length > 1)
            {
                throw new FileNotFoundException("Sorry, found multiple files with name: " + name + " under directory: " + directory);
            }
            else
            {
                return paths[0];
            }
        }

        private FastQueue<MemoryStream> streamsToWrite = Factory.Parallel.FastQueue<MemoryStream>("TickFileDirtyPages");
        private FastQueue<MemoryStream> streamsAvailable = Factory.Parallel.FastQueue<MemoryStream>("TickFileAvailable");

        private void MoveMemoryToQueue()
        {
            using (memoryLocker.Using())
            {
                streamsToWrite.Enqueue(memory, 0L);
            }
            if (streamsAvailable.Count == 0)
            {
                memory = new MemoryStream();
            }
            else
            {
                using (memoryLocker.Using())
                {
                    streamsAvailable.Dequeue(out memory);
                }
            }
        }

        public bool TryWriteTick(TickIO tickIO)
        {
            TryCompleteAsyncWrite();
            if (trace) log.Trace("Writing to file buffer: " + tickIO);
            tickIO.ToWriter(memory);
            if (memory.Position > 5000)
            {
                MoveMemoryToQueue();
                TryCompleteAsyncWrite();
                if( writeFileResult == null)
                {
                    writeFileResult = writeFileAction.BeginInvoke(null, null);
                }
            }
            return true;
        }

        public void WriteTick(TickIO tickIO)
        {
            tickIO.ToWriter(memory);
            if (memory.Position > 5000)
            {
                WriteToFile();
            }
        }

        public void OpenFileForReading()
        {
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    if (!quietMode)
                    {
                        LogInfo("Reading from file: " + FileName);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(FileName));

                    if (bulkFileLoad)
                    {
                        byte[] filebytes;
                        lock (fileLocker)
                        {
                            if (!fileBytesDict.TryGetValue(Symbol, out filebytes))
                            {
                                filebytes = File.ReadAllBytes(FileName);
                                fileBytesDict[Symbol] = filebytes;
                            }
                        }
                        bufferedStream = new MemoryStream(filebytes);
                    }
                    else
                    {
                        fs = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        bufferedStream = new BufferedStream(fs, 15 * 1024);
                    }

                    dataIn = new BinaryReader(bufferedStream, Encoding.Unicode);

                    if (!quietMode || debug)
                    {
                        if (debug) log.Debug("Starting to read data.");
                    }
                    break;
                }
                catch (Exception e)
                {
                    if (e is CollectionTerminatedException) {
                        log.Warn("Reader queue was terminated.");
                    } else if (e is ThreadAbortException) {
                        //	
                    } else if (e is FileNotFoundException) {
                        log.Error("ERROR: " + e.Message);
                    } else {
                        log.Error("ERROR: " + e);
                        Factory.Parallel.Sleep(1000);
                    }
                }
            }
        }

        private void CheckFileExtension()
        {
            string locatedFile = FindFile(fileName);
            if (locatedFile == null)
            {
                if (fileName.Contains("_Tick.tck"))
                {
                    locatedFile = FindFile(FileName.Replace("_Tick.tck", ".tck"));
                }
                else
                {
                    locatedFile = FindFile(FileName.Replace(".tck", "_Tick.tck"));
                }
                if (locatedFile != null)
                {
                    fileName = locatedFile;
                    log.Warn("Deprecated: Please use new style .tck file names by removing \"_Tick\" from the name.");
                }
                else if( mode == TickFileMode.Read)
                {
                    throw new FileNotFoundException("Sorry, unable to find the file: " + FileName);
                }
                else
                {
                    log.Warn("File was not found. Will create it. " + fileName);
                }
            }
            else
            {
                fileName = locatedFile;
            }
        }

        public void GetLastTick(TickIO lastTickIO)
        {
            Stream stream;
            stream = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            dataIn = new BinaryReader(stream, Encoding.Unicode);
            int count = 0;
            try
            {
                while (stream.Position < stream.Length)
                {
                    if (!TryReadTick(lastTickIO))
                    {
                        break;
                    }
                    count++;
                }
            }
            catch (ObjectDisposedException)
            {
                // Only partial tick was read at the end of the file.
                // Another writer must not have completed.
                log.Warn("ObjectDisposedException returned from tickIO.FromReader(). Incomplete last tick. Ignoring.");
            }
            catch
            {
                log.Error("Error reading tick #" + count);
            }
        }

        public bool TryReadTick(TickIO tickIO)
        {
            if( dataIn == null || tickCount > MaxCount || endOfData)
            {
                return false;
            }
            try
            {
                do
                {
                    tickIO.SetSymbol(lSymbol);
                    byte size = dataIn.ReadByte();
                    // Check for old style prior to version 8 where
                    // single byte version # was first.
                    if (dataVersion < 8 && size < 8)
                    {
                        dataVersion = tickIO.FromReader((byte) size, dataIn);
                    }
                    else
                    {
                        // Subtract the size byte.
                        //if (dataIn.BaseStream.Position + size - 1 > length) {
                        //    return false;
                        //}
                        int count = 1;
                        memory.SetLength(size);
                        memory.GetBuffer()[0] = size;
                        while (count < size)
                        {
                            var bytesRead = dataIn.Read(buffer, count, size - count);
                            if (bytesRead == 0)
                            {
                                return false;
                            }
                            count += bytesRead;
                        }
                        memory.Position = 0;
                        dataVersion = tickIO.FromReader(memory);
                    }
                    var utcTime = new TimeStamp(tickIO.lUtcTime);
                    if (utcTime > EndTime)
                    {
                        endOfData = true;
                        return false;
                    }
                    tickIO.SetTime(utcTime);
                    tickCount++;
                } while (tickIO.UtcTime < StartTime);
                return true;
            }
            catch (EndOfStreamException ex)
            {
                return false;
            }
        }

        private IAsyncResult writeFileResult;

        private void TryCompleteAsyncWrite()
        {
            if (writeFileResult != null && writeFileResult.IsCompleted)
            {
                writeFileAction.EndInvoke(writeFileResult);
                writeFileResult = null;
            }
        }

        public void Flush()
        {
            if (debug) log.Debug("Before flush memory " + memory.Position);
            while (memory.Position > 0 || streamsToWrite.Count > 0 || writeFileResult != null)
            {
                if( memory.Position > 0)
                {
                    MoveMemoryToQueue();
                }
                TryCompleteAsyncWrite();
                if (writeFileResult == null)
                {
                    writeFileResult = writeFileAction.BeginInvoke(null, null);
                }
                while (writeFileResult != null)
                {
                    TryCompleteAsyncWrite();
                    Thread.Sleep(100);
                }
            }
            if (debug) log.Debug("After flush memory " + memory.Position);
        }

        private int origSleepSeconds = 3;
        private int currentSleepSeconds = 3;
        private long writeCounter = 0;
        private object writeToFileLocker = new object();
        private void WriteToFile()
        {
            int errorCount = 0;
            if( streamsToWrite.Count == 0) return;
            if( !Monitor.TryEnter(writeToFileLocker))
            {
                throw new InvalidOperationException("Only one thread at a time allowed for this method.");
            }
            try
            {
                do
                {
                    MemoryStream memory;
                    try
                    {
                        using (memoryLocker.Using())
                        {
                            streamsToWrite.Peek(out memory);
                        }
                        if (debug) log.Debug("Writing buffer size: " + memory.Position);
                        fs.Write(memory.GetBuffer(), 0, (int)memory.Position);
                        fs.Flush();
                        memory.Position = 0;
                        using (memoryLocker.Using())
                        {
                            streamsToWrite.Dequeue(out memory);
                            streamsAvailable.Enqueue(memory, 0L);
                        }
                        if (errorCount > 0)
                        {
                            log.Notice(Symbol + ": Retry successful.");
                        }
                        errorCount = 0;
                        currentSleepSeconds = origSleepSeconds;
                    }
                    catch (IOException e)
                    {
                        errorCount++;
                        log.Debug(Symbol + ": " + e.Message + "\nPausing " + currentSleepSeconds + " seconds before retry.");
                        Factory.Parallel.Sleep(3000);
                    }
                } while (errorCount > 0);
            }
            finally
            {
                Monitor.Exit(writeToFileLocker);
            }
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
            if (!isDisposed)
            {
                isDisposed = true;
                lock (taskLocker)
                {
                    if (debug) log.Debug("Dispose()");
                    if( bufferedStream != null)
                    {
                        bufferedStream.Flush();
                    }
                    if (dataIn != null)
                    {
                        if (debug) log.Debug("Closing dataIn.");
                        dataIn.Close();
                    }

                    if( fs != null && mode == TickFileMode.Read)
                    {
                        fs.Close();
                        fs = null;
                        log.Info("Closed file " + fileName);
                    }

                    if (fs != null && mode == TickFileMode.Write)
                    {
                        Flush();
                        CloseFile(fs);
                        fs = null;
                        log.Info("Flushed and closed file " + fileName);
                    }
                    if (debug) log.Debug("Exiting Close()");
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FlushFileBuffers(SafeFileHandle hFile);

        private void CloseFile(FileStream fs)
        {
            if (fs != null)
            {
                if (debug) log.Debug("CloseFile() at with length " + fs.Length);
                fs.Flush();
                if (!FlushFileBuffers(fs.SafeFileHandle))   // Flush OS file cache to disk.
                {
                    Int32 err = Marshal.GetLastWin32Error();
                    throw new Win32Exception(err, "Win32 FlushFileBuffers returned error for " + fs.Name);
                }
                fs.Close();
            }
        }

        public long Length
        {
            get { return dataIn == null ? 0 : dataIn.BaseStream.Length; }
        }

        public long Position
        {
            get { return dataIn == null ? 0 : dataIn.BaseStream.Position; }
        }

        public int DataVersion
        {
            get { return dataVersion; }
        }

        public bool QuietMode
        {
            get { return quietMode; }
            set { quietMode = value; }
        }

        public bool BulkFileLoad
        {
            get { return bulkFileLoad; }
            set { bulkFileLoad = value; }
        }

        public string FileName
        {
            get { return fileName; }
        }

        public SymbolInfo Symbol
        {
            get { return symbol; }
        }

        public bool EraseFileToStart
        {
            get { return eraseFileToStart; }
            set { eraseFileToStart = value; }
        }

        public long WriteCounter
        {
            get { return writeCounter; }
        }

        public long MaxCount
        {
            get { return maxCount; }
            set { maxCount = value; }
        }

        public TimeStamp StartTime
        {
            get { return startTime; }
            set { startTime = value; }
        }

        public TimeStamp EndTime
        {
            get { return endTime; }
            set { endTime = value; }
        }
    }
}