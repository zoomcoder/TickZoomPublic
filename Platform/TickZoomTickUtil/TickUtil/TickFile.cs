using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public class TickFile : IDisposable
    {
        private SymbolInfo symbol;
        private long lSymbol;
        private int dataVersion;
        private MemoryStream memory;
        private BinaryReader dataIn = null;
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
        }

        public void Initialize(string folderOrfile, string symbolFile)
        {
            string[] symbolParts = symbolFile.Split(new char[] { '.' });
            string _symbol = symbolParts[0];
            symbol = Factory.Symbol.LookupSymbol(_symbol);
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
            OpenFile();
        }

        public void Initialize(string fileName)
        {
            this.fileName = fileName = Path.GetFullPath(fileName);
            CheckFileExtension();
            if (debug)
                log.Debug("File Name = " + fileName);
            if (debug)
                log.Debug("Setting start method on reader queue.");
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (Symbol == null)
            {
                symbol = Factory.Symbol.LookupSymbol(baseName.Replace("_Tick", ""));
                lSymbol = Symbol.BinaryIdentifier;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            OpenFile();
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

        public void OpenFile()
        {
            log = Factory.SysLog.GetLogger("TickZoom.TickUtil.TickFile." + Symbol.Symbol.StripInvalidPathChars());
            debug = log.IsDebugEnabled;
            trace = log.IsTraceEnabled;
            lSymbol = Symbol.BinaryIdentifier;
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    if (!quietMode)
                    {
                        LogInfo("Reading from file: " + FileName);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(FileName));

                    Stream stream;
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
                        stream = new MemoryStream(filebytes);
                    }
                    else
                    {
                        var fileStream = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        stream = new BufferedStream(fileStream, 15 * 1024);
                    }

                    dataIn = new BinaryReader(stream, Encoding.Unicode);

                    if (!quietMode || debug)
                    {
                        if (debug) log.Debug("Starting to read data.");
                    }
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
                    }
                    Dispose();
                }
                Factory.Parallel.Sleep(1000);
            }
        }

        private void CheckFileExtension()
        {
            string locatedFile = FindFile(FileName);
            if (locatedFile == null)
            {
                if (FileName.Contains("_Tick.tck"))
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
                else
                {
                    throw new FileNotFoundException("Sorry, unable to find the file: " + FileName);
                }
            }
            fileName = locatedFile;
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
            try
            {
                tickIO.SetSymbol(lSymbol);
                byte size = dataIn.ReadByte();
                // Check for old style prior to version 8 where
                // single byte version # was first.
                if (dataVersion < 8 && size < 8)
                {
                    dataVersion = tickIO.FromReader((byte)size, dataIn);
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
                tickIO.SetTime(utcTime);
                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
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
                    if (dataIn != null)
                    {
                        if (dataIn.BaseStream != null)
                        {
                            if (debug) log.Debug("Closing base stream.");
                            dataIn.BaseStream.Close();
                        }
                        if (debug) log.Debug("Closing dataIn.");
                        dataIn.Close();
                    }
                }
            }
        }
        public long Length
        {
            get { return dataIn.BaseStream.Length; }
        }

        public long Position
        {
            get { return dataIn.BaseStream.Position; }
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
    }
}