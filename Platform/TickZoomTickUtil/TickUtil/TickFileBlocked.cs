using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public class TickFileBlocked : TickFile
    {
        private TickFileLegacy legacy = new TickFileLegacy();
        private bool isLegacy;
        private SymbolInfo symbol;
        private long lSymbol;
        private int dataVersion;
        private FileStream fs = null;
        private bool quietMode;
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
        private long tickCount;
        private long maxCount = long.MaxValue;
        private TimeStamp startTime = TimeStamp.MinValue;
        private TimeStamp endTime = TimeStamp.MaxValue;
        private bool endOfData = true;
        private FileBlock fileBlock;
        private long startCount;
        private bool isInitialized;

        public unsafe TickFileBlocked()
        {
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
            log = Factory.SysLog.GetLogger("TickZoom.TickUtil.TickFileBlocked." + mode + "." + symbol.Symbol.StripInvalidPathChars());
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
            try
            {
                OpenFile();
            }
            catch( InvalidOperationException)
            {
                CloseFileForReading();
                // Must a be a legacy format
                isLegacy = true;
                legacy.Initialize(folderOrfile,symbolFile,mode);
            }
            isInitialized = true;
        }

        public void Initialize(string fileName, TickFileMode mode)
        {
            this.mode = mode;
            this.fileName = fileName = Path.GetFullPath(fileName);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (symbol == null)
            {
                symbol = Factory.Symbol.LookupSymbol(baseName.Replace("_Tick", ""));
                lSymbol = symbol.BinaryIdentifier;
            }
            InitLogging();
            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            CheckFileExtension();
            if (debug) log.Debug("File Name = " + fileName);
            try
            {
                OpenFile();
            }
            catch (InvalidOperationException)
            {
                CloseFileForReading();
                // Must a be a legacy format
                isLegacy = true;
                legacy.Initialize(fileName, mode);
            }
            isInitialized = true;
        }

        private void OpenFile()
        {
            lSymbol = symbol.BinaryIdentifier;
            switch (mode)
            {
                case TickFileMode.Read:
                    OpenFileForReading();
                    ReadNextTickBlock();
                    break;
                case TickFileMode.Write:
                    OpenFileForWriting();
                    fileBlock.ReserveHeader();
                    break;
                default:
                    throw new ApplicationException("Unknown file mode: " + mode);
            }
            endOfData = false;
        }

        private void OpenFileForWriting()
        {
            if (eraseFileToStart)
            {
                log.Notice("TickWriter file will be erased to begin writing.");
                CreateFileForWriting();
            }
            else
            {
                if( File.Exists(fileName))
                {
                    // Read the file header.
                    try
                    {
                        OpenFileForReading();
                    }
                    finally
                    {
                        CloseFileForReading();
                    }
                    OpenFileForAppending();
                }
                else
                {
                    CreateFileForWriting();
                }
            }
        }

        private void OpenFileForAppending()
        {
            fs = new FileStream(fileName, FileMode.Append, FileAccess.Write, FileShare.Read, 1024, FileOptions.WriteThrough);
            log.Debug("OpenFileForWriting()");
        }

        private unsafe void CreateFileForWriting()
        {
            fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.Read, 1024, FileOptions.WriteThrough);
            log.Debug("OpenFileForWriting()");
            fileHeader.blockHeader.version = 1;
            fileHeader.blockHeader.type = BlockType.FileHeader;
            fileHeader.blockSize = 5 * 1024;
            fileHeader.utcTimeStamp = Factory.Parallel.UtcNow.Internal;
            fileHeader.SetChecksum();
            var headerBytes = new byte[fileHeader.blockSize];
            fixed( byte *bptr = headerBytes)
            {
                *((TickFileHeader*) bptr) = fileHeader;
            }
            fs.Write(headerBytes, 0, fileHeader.blockSize);
            fileBlock = new FileBlock(fileHeader.blockSize);
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

        private FastQueue<FileBlock> streamsToWrite = Factory.Parallel.FastQueue<FileBlock>("TickFileDirtyPages");
        private FastQueue<FileBlock> streamsAvailable = Factory.Parallel.FastQueue<FileBlock>("TickFileAvailable");

        private void MoveMemoryToQueue()
        {
            using (memoryLocker.Using())
            {
                streamsToWrite.Enqueue(fileBlock, 0L);
            }
            if (streamsAvailable.Count == 0)
            {
                fileBlock = new FileBlock(fileHeader.blockSize);
            }
            else
            {
                using (memoryLocker.Using())
                {
                    streamsAvailable.Dequeue(out fileBlock);
                }
            }
        }

        public bool TryWriteTick(TickIO tickIO)
        {
            if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
            if (isLegacy) return legacy.TryWriteTick(tickIO);
            TryCompleteAsyncWrite();
            if (trace) log.Trace("Writing to file buffer: " + tickIO);
            if( !fileBlock.TryWriteTick(tickIO))
            {
                MoveMemoryToQueue();
                fileBlock.ReserveHeader();
                tickIO.ResetCompression();
                if( !fileBlock.TryWriteTick(tickIO))
                {
                    throw new InvalidOperationException("After creating new block, writing tick failed.");
                }
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
            if (!isInitialized) throw new InvalidOperationException("Please call one of the Initialize() methods first.");
            TryWriteTick(tickIO);
        }

        public enum BlockType : short
        {
            FileHeader,
            TickBlock
        }

        public struct FileBlockHeader
        {
            public short version;
            public BlockType type;
            public int length;
            public long checkSum;
            public long CalcChecksum()
            {
                return (short)type ^ version ^ length;
            }
            public override string ToString()
            {
                return "FileBlock( version " + version + ", type " + type + ", length " + length + ", checksum " +
                       checkSum + ")";
            }
        }

        public struct TickBlockHeader
        {
            public FileBlockHeader blockHeader;
            public long firstUtcTimeStamp;
            public long lastUtcTimeStamp;
            public long checkSum;
            public bool VerifyChecksum()
            {
                var expectedChecksum = CalcChecksum();
                return expectedChecksum == checkSum;
            }
            public void SetChecksum()
            {
                checkSum = CalcChecksum();
            }
            private long CalcChecksum()
            {
                return blockHeader.CalcChecksum() ^ firstUtcTimeStamp ^ lastUtcTimeStamp;
            }
            public override string ToString()
            {
                return blockHeader + ", first " + new TimeStamp(firstUtcTimeStamp) + ", last " + new TimeStamp(lastUtcTimeStamp) +
                    ", checksum " + checkSum;
            }
        }

        public struct TickFileHeader
        {
            public FileBlockHeader blockHeader;
            public long utcTimeStamp;
            public long checkSum;
            public int blockSize;
            public bool VerifyChecksum()
            {
                var expectedChecksum = CalcChecksum();
                return expectedChecksum == checkSum;
            }
            public void SetChecksum()
            {
                checkSum = CalcChecksum();
            }
            private long CalcChecksum()
            {
                return blockHeader.CalcChecksum() ^ blockSize ^ utcTimeStamp;
            }
        }

        private TickFileHeader fileHeader;

        private unsafe void OpenFileForReading()
        {
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    if (!quietMode)
                    {
                        LogInfo("Reading from file: " + fileName);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(fileName));

                    fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var headerBytes = new byte[sizeof(TickFileHeader)];
                    var headerSize = sizeof (TickFileHeader);
                    var readBytes = fs.Read(headerBytes, 0, headerSize);
                    if (readBytes != headerSize)
                    {
                        throw new InvalidOperationException("Number of header bytes " + readBytes + " differs from size of the header " + headerSize);
                    }
                    fixed( byte *headerPtr = headerBytes)
                    {
                        fileHeader = *((TickFileHeader*) headerPtr);
                    }
                    if (!fileHeader.VerifyChecksum())
                    {
                        throw new InvalidOperationException("Checksum failed for file header.");
                    }

                    // Read the entire header block including all padding.
                    fs.Seek(fileHeader.blockSize, SeekOrigin.Begin);
                    // Verify the version number.
                    switch (fileHeader.blockHeader.version)
                    {
                        case 1:
                            break;
                        default:
                            throw new InvalidOperationException("Unrecognized tick file version " + fileHeader.blockHeader.version);
                    }

                    if (!quietMode || debug)
                    {
                        if (debug) log.Debug("Starting to read data.");
                    }
                    fileBlock = new FileBlock(fileHeader.blockSize);
                    break;
                }
                catch( InvalidOperationException)
                {
                    throw;
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

        private unsafe void ReadNextTickBlock()
        {
            do
            {
                fileBlock.ReadNextBlock(fs);
            } while (fileBlock.LastUtcTimeStamp < startTime.Internal);
        }


        private void CheckFileExtension()
        {
            string locatedFile = FindFile(fileName);
            if (locatedFile == null)
            {
                if (fileName.Contains("_Tick.tck"))
                {
                    locatedFile = FindFile(fileName.Replace("_Tick.tck", ".tck"));
                }
                else
                {
                    locatedFile = FindFile(fileName.Replace(".tck", "_Tick.tck"));
                }
                if (locatedFile != null)
                {
                    fileName = locatedFile;
                    log.Warn("Deprecated: Please use new style .tck file names by removing \"_Tick\" from the name.");
                }
                else if( mode == TickFileMode.Read)
                {
                    throw new FileNotFoundException("Sorry, unable to find the file: " + fileName);
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
            if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
            if (isLegacy)
            {
                legacy.GetLastTick(lastTickIO);
                return; 
            }
            OpenFileForReading();
            var length = fs.Length;
            if( length % fileHeader.blockSize != 0)
            {
                throw new InvalidOperationException("File size " + length + " isn't not an even multiple of block size " + fileHeader.blockSize);
            }
            fs.Seek(- fileHeader.blockSize, SeekOrigin.End);
            ReadNextTickBlock();
            while( TryReadTick(lastTickIO))
            {
                // Read till last tick in the last block.
            }

        }

        public bool TryReadTick(TickIO tickIO)
        {
            if (!isInitialized) return false;
            if (isLegacy) return legacy.TryReadTick(tickIO);
            if( tickCount > MaxCount || endOfData)
            {
                return false;
            }
            try
            {
                do
                {
                    tickIO.SetSymbol(lSymbol);
                    if( !fileBlock.TryReadTick(tickIO))
                    {
                        ReadNextTickBlock();
                        if (!fileBlock.TryReadTick(tickIO))
                        {
                            throw new InvalidOperationException("Unable to write the first tick in a new block.");
                        }
                    }
                    dataVersion = fileBlock.DataVersion;
                    if (tickIO.lUtcTime > EndTime.Internal)
                    {
                        endOfData = true;
                        return false;
                    }
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
            if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
            if (isLegacy)
            {
                legacy.Flush();
                return;
            }
            while (fileBlock.HasData || streamsToWrite.Count > 0 || writeFileResult != null)
            {
                if (fileBlock.HasData)
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
        }

        private long writeCounter = 0;
        private object writeToFileLocker = new object();
        private void WriteToFile()
        {
            if( streamsToWrite.Count == 0) return;
            if( !Monitor.TryEnter(writeToFileLocker))
            {
                throw new InvalidOperationException("Only one thread at a time allowed for this method.");
            }
            try
            {
                while (streamsToWrite.Count > 0)
                {
                    FileBlock fileBlock;
                    using (memoryLocker.Using())
                    {
                        streamsToWrite.Peek(out fileBlock);
                    }
                    if (debug) log.Debug(streamsToWrite.Count + " blocks in queue.");
                    fileBlock.Write(fs);
                    using (memoryLocker.Using())
                    {
                        streamsToWrite.Dequeue(out fileBlock);
                        streamsAvailable.Enqueue(fileBlock, 0L);
                    }
                }
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
            if( isLegacy)
            {
                legacy.Dispose();
                return;
            }
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

                    if( mode == TickFileMode.Write)
                    {
                        CloseFileForWriting();
                    }

                    if (mode == TickFileMode.Read)
                    {
                        CloseFileForReading();
                    }

                    if (debug) log.Debug("Exiting Close()");
                }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool FlushFileBuffers(SafeFileHandle hFile);

        private void CloseFileForReading()
        {
            if (fs != null)
            {
                if (debug) log.Debug("CloseFileForReading()");
                fs.Close();
                fs = null;
                log.Info("Closed file " + fileName);
            }
        }

        private void CloseFileForWriting()
        {
            if (fs != null)
            {
                if (debug) log.Debug("CloseFileForWriting() at with length " + fs.Length);
                Flush();
                fs.Flush();
                if (!FlushFileBuffers(fs.SafeFileHandle))   // Flush OS file cache to disk.
                {
                    Int32 err = Marshal.GetLastWin32Error();
                    throw new Win32Exception(err, "Win32 FlushFileBuffers returned error for " + fs.Name);
                }
                fs.Close();
                fs = null;
                log.Info("Flushed and closed file " + fileName);
            }
        }

        public long Length
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy) return legacy.Length;
                return fs.Length;
            }
        }

        public long Position
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy) return legacy.Position;
                return fs.Position;
            }
        }

        public int DataVersion
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy) return legacy.DataVersion;
                return dataVersion;
            }
        }

        public int BlockVersion
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                return fileHeader.blockHeader.version;
            }
        }

        public bool QuietMode
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy) return legacy.QuietMode;
                return quietMode;
            }
            set
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy)
                {
                    legacy.QuietMode = value;
                    return;
                }
                quietMode = value;
            }
        }

        public string FileName
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy) return legacy.FileName;
                return fileName;
            }
        }

        public SymbolInfo Symbol
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy) return legacy.Symbol;
                return symbol;
            }
        }

        public bool EraseFileToStart
        {
            get
            {
                if (isLegacy) return legacy.EraseFileToStart;
                return eraseFileToStart;
            }
            set
            {
                if (isInitialized) throw new InvalidStateException("Please set EraseFileToStart before any Initialize() method.");
                legacy.EraseFileToStart = value;
                eraseFileToStart = value;
            }
        }

        public long WriteCounter
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy) return legacy.WriteCounter;
                return writeCounter;
            }
        }

        public long MaxCount
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy) return legacy.MaxCount;
                return maxCount;
            }
            set
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy)
                {
                    legacy.MaxCount = value;
                    return;
                }
                maxCount = value;
            }
        }

        public long StartCount
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                return startCount;
            }
            set
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                startCount = value;
            }
        }

        public TimeStamp StartTime
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy) return legacy.StartTime;
                return startTime;
            }
            set
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                if (isLegacy)
                {
                    legacy.StartTime = value;
                }
                startTime = value;
            }
        }

        public TimeStamp EndTime
        {
            get
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                return endTime;
            }
            set
            {
                if (!isInitialized) throw new InvalidStateException("Please call one of the Initialize() methods first.");
                endTime = value;
            }
        }
    }
}