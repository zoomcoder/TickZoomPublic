using System;
using System.IO;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public class FileBlock
    {
        private static readonly Log log = Factory.SysLog.GetLogger(typeof (FileBlock));
        private static readonly bool debug = log.IsDebugEnabled;
        private static readonly bool trace = log.IsTraceEnabled;
        private MemoryStream memory = new MemoryStream();
        private TickFileBlocked.TickBlockHeader tickBlockHeader;
        private int tickBlockHeaderSize;
        private int blockSize;
        private int dataVersion;
        public unsafe FileBlock(int blockSize)
        {
            if( blockSize == 0)
            {
                throw new ArgumentException("blocksize cannot be zero");
            }
            this.blockSize = blockSize;
            tickBlockHeaderSize = sizeof(TickFileBlocked.TickBlockHeader);
            memory.SetLength(tickBlockHeaderSize);
        }

        public long LastUtcTimeStamp
        {
            get { return tickBlockHeader.lastUtcTimeStamp; }
        }

        public bool HasData
        {
            get { return memory.Position > tickBlockHeaderSize; }
        }

        public int DataVersion
        {
            get { return dataVersion; }
        }

        public void ReserveHeader()
        {
            memory.SetLength(tickBlockHeaderSize);
            memory.Position = tickBlockHeaderSize;
        }

        public bool TryWriteTick(TickIO tickIO)
        {
            var result = true;
            var tempPosition = memory.Position;
            tickIO.ToWriter(memory);
            if (tickBlockHeader.firstUtcTimeStamp == 0L)
            {
                tickBlockHeader.firstUtcTimeStamp = tickIO.lUtcTime;
            }
            if (memory.Position > blockSize)
            {
                memory.Position = tempPosition;
                memory.SetLength(tempPosition);
                result = false;
            }
            else
            {
                tickBlockHeader.lastUtcTimeStamp = tickIO.lUtcTime;
            }
            return result;
        }

        public unsafe void ReadNextBlock(FileStream fs)
        {
            var tempPosition = fs.Position;
            var tempLength = fs.Length;
            memory.SetLength(blockSize);
            var buffer = memory.GetBuffer();
            memory.Position = 0;
            while (memory.Position < memory.Length)
            {
                var bytesRead = fs.Read(buffer, (int)memory.Position, (int)(blockSize - memory.Position));
                if (bytesRead == 0)
                {
                    throw new EndOfStreamException("Cannot read past the end of the stream.");
                }
                memory.Position += bytesRead;
            }

            fixed (byte* bptr = buffer)
            {
                tickBlockHeader = *((TickFileBlocked.TickBlockHeader*)bptr);
            }
            memory.Position = tickBlockHeaderSize;
            if (!tickBlockHeader.VerifyChecksum())
            {
                throw new InvalidOperationException("Tick block header checksum failed at " + tempPosition + ", length " + tempLength + ", current length " + fs.Length + ", current position " + fs.Position + ": " + fs.Name + "\n" + BitConverter.ToString(memory.GetBuffer(),0,blockSize));
            }
        }

        public unsafe void WriteHeader()
        {
            if (memory.Length < tickBlockHeaderSize)
            {
                throw new InvalidOperationException("Insufficient byte to write tick block header. Expected: " + tickBlockHeaderSize + " but was: " + memory.Length);
            }
            tickBlockHeader.blockHeader.type = TickFileBlocked.BlockType.TickBlock;
            tickBlockHeader.blockHeader.version = 1;
            tickBlockHeader.blockHeader.length = (int)memory.Position;
            tickBlockHeader.SetChecksum();
            fixed (byte* bptr = memory.GetBuffer())
            {
                *((TickFileBlocked.TickBlockHeader*)bptr) = tickBlockHeader;
            }
        }

        public bool TryReadTick(TickIO tickIO)
        {
            try
            {
                if( memory.Position >= tickBlockHeader.blockHeader.length)
                {
                    return false;
                }
                tickIO.SetSymbol(tickIO.lSymbol);
                var size = memory.GetBuffer()[memory.Position];
                dataVersion = tickIO.FromReader(memory);
                var utcTime = new TimeStamp(tickIO.lUtcTime);
                tickIO.SetTime(utcTime);
                return true;
            }
            catch (EndOfStreamException ex)
            {
                return false;
            }
            catch( IndexOutOfRangeException)
            {
                int x = 0;
                return false;
            }
        }

        public void Write(FileStream fs)
        {
            var errorCount = 0;
            var sleepSeconds = 3;
            do
            {
                try
                {
                    if (debug) log.Debug("Writing buffer size " + memory.Position);
                    WriteHeader();
                    if (memory.Length < blockSize)
                    {
                        memory.SetLength(blockSize);
                    }
                    fs.Write(memory.GetBuffer(), 0, (int)memory.Length);
                    fs.Flush();
                    memory.Position = 0;
                    if (errorCount > 0)
                    {
                        log.Notice("Retry successful.");
                    }
                    errorCount = 0;
                }
                catch (IOException e)
                {
                    errorCount++;
                    log.Debug(e.Message + "\nPausing " + sleepSeconds + " seconds before retry.");
                    Factory.Parallel.Sleep(3);
                }
            } while (errorCount > 0);
        }
    }
}