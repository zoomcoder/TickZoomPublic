using System;
using System.IO;
using System.Text;
using TickZoom.Api;

namespace TickZoom.TickUtil
{
    public interface TickSerializer
    {
        void ToWriter(ref TickBinary binary, MemoryStream writer);
        int FromReader(ref TickBinary binary, MemoryStream reader);
        int FromReader(ref TickBinary binary, byte dataVersion, BinaryReader reader);
    }

    unsafe public class TickSerializerDefault : TickSerializer
    {
        public const int minTickSize = 256;

        // Older formats were already multiplied by 1000.
        public const long OlderFormatConvertToLong = 1000000;
        private static readonly Log log = Factory.SysLog.GetLogger(typeof(TickSerializerDefault));
        private static readonly bool trace = log.IsTraceEnabled;
        private static readonly bool debug = log.IsDebugEnabled;
        private static readonly bool verbose = log.IsVerboseEnabled;
        private bool isCompressStarted;
        private long pricePrecision;
        private int priceDecimals;

        TickBinary lastBinary;

        public enum BinaryField
        {
            Time = 1,
            Bid,
            Ask,
            Price,
            Size,
            BidSize,
            AskSize,
            ContentMask,
            Precision,
            Strike,
            OptionExpiration,
            Reset = 30,
            Empty = 31
        }
        public enum FieldSize
        {
            Byte = 1,
            Short,
            Int,
            Long,
        }

        private unsafe bool WriteField2(BinaryField fieldEnum, byte** ptr, long diff)
        {
            var field = (byte)((byte)fieldEnum << 3);
            if (diff == 0)
            {
                return false;
            }
            else if (diff >= sbyte.MinValue && diff <= sbyte.MaxValue)
            {
                field |= (byte)FieldSize.Byte;
                *(*ptr) = field; (*ptr)++;
                *(sbyte*)(*ptr) = (sbyte)diff; (*ptr) += sizeof(sbyte);
            }
            else if (diff >= short.MinValue && diff <= short.MaxValue)
            {
                field |= (byte)FieldSize.Short;
                *(*ptr) = field; (*ptr)++;
                *(short*)(*ptr) = (short)diff; (*ptr) += sizeof(short);
            }
            else if (diff >= int.MinValue && diff <= int.MaxValue)
            {
                field |= (byte)FieldSize.Int;
                *(*ptr) = field; (*ptr)++;
                *(int*)(*ptr) = (int)diff; (*ptr) += sizeof(int);
            }
            else
            {
                field |= (byte)FieldSize.Long;
                *(*ptr) = field; (*ptr)++;
                *(long*)(*ptr) = diff; (*ptr) += sizeof(long);
            }
            return true;
        }

        private unsafe bool WriteField(BinaryField fieldEnum, byte** ptr, long diff)
        {
            var field = (byte)((byte)fieldEnum << 3);
            if (diff == 0)
            {
                return false;
            }
            else if (diff >= byte.MinValue && diff <= byte.MaxValue)
            {
                field |= (byte)FieldSize.Byte;
                *(*ptr) = field; (*ptr)++;
                *(*ptr) = (byte)diff; (*ptr)++;
            }
            else if (diff >= short.MinValue && diff <= short.MaxValue)
            {
                field |= (byte)FieldSize.Short;
                *(*ptr) = field; (*ptr)++;
                *(short*)(*ptr) = (short)diff; (*ptr) += sizeof(short);
            }
            else if (diff >= int.MinValue && diff <= int.MaxValue)
            {
                field |= (byte)FieldSize.Int;
                *(*ptr) = field; (*ptr)++;
                *(int*)(*ptr) = (int)diff; (*ptr) += sizeof(int);
            }
            else
            {
                field |= (byte)FieldSize.Long;
                *(*ptr) = field; (*ptr)++;
                *(long*)(*ptr) = diff; (*ptr) += sizeof(long);
            }
            return true;
        }

        private unsafe void WriteBidSize(ref TickBinary binary, byte field, int i, byte** ptr)
        {
            fixed (ushort* lp = lastBinary.DepthBidLevels)
            fixed (ushort* p = binary.DepthBidLevels)
            {
                var diff = *(p + i) - *(lp + i);
                if (diff != 0)
                {
                    *(*ptr) = (byte)(field | i); (*ptr)++;
                    *(short*)(*ptr) = (short)diff; (*ptr) += sizeof(short);
                    *(lp + i) = *(p + i);
                }
            }
        }

        private unsafe void WriteAskSize(ref TickBinary binary, byte field, int i, byte** ptr)
        {
            fixed (ushort* lp = lastBinary.DepthAskLevels)
            fixed (ushort* p = binary.DepthAskLevels)
            {
                var diff = *(p + i) - *(lp + i);
                if (diff != 0)
                {
                    *(*ptr) = (byte)(field | i); (*ptr)++;
                    *(short*)(*ptr) = (short)diff; (*ptr) += sizeof(short);
                    *(lp + i) = *(p + i);
                }
            }
        }
        private void SetPricePrecision(ref TickBinary binary)
        {
            var symbol = Factory.Symbol.LookupSymbol(binary.Symbol);
            var minimumTick = symbol.MinimumTick;
            priceDecimals = 0;
            while ((long)minimumTick != minimumTick)
            {
                minimumTick *= 10;
                priceDecimals++;
            }
            var temp = Math.Pow(0.1, symbol.MinimumTickPrecision);
            pricePrecision = temp.ToLong();
        }

        private TickIO toWriterTickIO = Factory.TickUtil.TickIO();
        private unsafe void ToWriterVersion11(ref TickBinary binary, MemoryStream writer)
        {
            if (verbose) log.Verbose("Before Cx " + lastBinary);
            //var tempBinary = lastBinary;
            var dataVersion = (byte)11;
            writer.SetLength(writer.Position + minTickSize);
            byte[] buffer = writer.GetBuffer();
            fixed (byte* fptr = &buffer[writer.Position])
            {
                byte* ptr = fptr;
                ptr++; // Save space for size header.
                *(ptr) = dataVersion; ptr++;
                ptr++; // Save space for checksum.
                if (pricePrecision == 0L)
                {
                    SetPricePrecision(ref binary);
                    if (debug) log.Debug("Writing decimal places used in price compression.");
                    WriteField2(BinaryField.Precision, &ptr, pricePrecision);
                }
                if (!isCompressStarted)
                {
                    if (debug) log.Debug("Writing Reset token during tick compression.");
                    WriteField2(BinaryField.Reset, &ptr, 1);
                    isCompressStarted = true;
                    if (verbose) log.Verbose("Reset Dx " + lastBinary);
                }
                WriteField2(BinaryField.ContentMask, &ptr, binary.contentMask - lastBinary.contentMask);
                lastBinary.contentMask = binary.contentMask;

                var diff = (binary.UtcTime - lastBinary.UtcTime);
                WriteField2(BinaryField.Time, &ptr, diff);
                lastBinary.UtcTime = binary.UtcTime;

                if (binary.IsQuote)
                {
                    WriteField2(BinaryField.Bid, &ptr, binary.Bid / pricePrecision - lastBinary.Bid);
                    WriteField2(BinaryField.Ask, &ptr, binary.Ask / pricePrecision - lastBinary.Ask);
                    lastBinary.Bid = binary.Bid / pricePrecision;
                    lastBinary.Ask = binary.Ask / pricePrecision;
                }
                if (binary.IsTrade)
                {
                    WriteField2(BinaryField.Price, &ptr, binary.Price / pricePrecision - lastBinary.Price);
                    WriteField2(BinaryField.Size, &ptr, binary.Size - lastBinary.Size);
                    lastBinary.Price = binary.Price / pricePrecision;
                    lastBinary.Size = binary.Size;
                }
                if (binary.IsOption)
                {
                    WriteField2(BinaryField.Strike, &ptr, binary.Strike / pricePrecision - lastBinary.Strike);
                    lastBinary.Strike = binary.Strike / pricePrecision;
                    diff = (binary.UtcOptionExpiration - lastBinary.UtcOptionExpiration);
                    WriteField2(BinaryField.OptionExpiration, &ptr, diff);
                    lastBinary.UtcOptionExpiration = binary.UtcOptionExpiration;
                }
                if (binary.HasDepthOfMarket)
                {
                    var field = (byte)((byte)BinaryField.BidSize << 3);
                    fixed (ushort* usptr = binary.DepthBidLevels)
                        for (int i = 0; i < TickBinary.DomLevels; i++)
                        {
                            WriteBidSize(ref binary, field, i, &ptr);
                        }
                    field = (byte)((byte)BinaryField.AskSize << 3);
                    fixed (ushort* usptr = binary.DepthAskLevels)
                        for (int i = 0; i < TickBinary.DomLevels; i++)
                        {
                            WriteAskSize(ref binary, field, i, &ptr);
                        }
                }
                int length = (int) (ptr - fptr);
                writer.Position += length;
                writer.SetLength(writer.Position);
                *fptr = (byte)(ptr - fptr);
                var checkSum = CalcChecksum(ref binary, "Cx");
                *(fptr + 2) = checkSum;
                //lastBinary = binary;
                if (verbose)
                {
                    toWriterTickIO.Inject(binary);
                    log.Verbose("Cx tick: " + toWriterTickIO);
                }
                //FromFileVersion11(ref tempBinary, fptr + 2, length-2);
            }
        }

        private unsafe void ToWriterVersion10(ref TickBinary binary, MemoryStream writer)
        {
            var dataVersion = (byte) 10;
            writer.SetLength(writer.Position + minTickSize);
            byte[] buffer = writer.GetBuffer();
            fixed (byte* fptr = &buffer[writer.Position])
            {
                byte* ptr = fptr;
                ptr++; // Save space for size header.
                *(ptr) = dataVersion; ptr++;
                ptr++; // Save space for checksum.
                if (pricePrecision == 0L)
                {
                    SetPricePrecision(ref binary);
                    if (debug) log.Debug("Writing decimal places use in price compression.");
                    WriteField(BinaryField.Precision, &ptr, pricePrecision);
                }
                if (!isCompressStarted)
                {
                    if (debug) log.Debug("Writing Reset token during tick compression.");
                    WriteField(BinaryField.Reset, &ptr, 1);
                    isCompressStarted = true;
                }
                WriteField(BinaryField.ContentMask, &ptr, binary.contentMask - lastBinary.contentMask);
                var diff = (binary.UtcTime - lastBinary.UtcTime);
                WriteField(BinaryField.Time, &ptr, diff);
                if (binary.IsQuote)
                {
                    WriteField(BinaryField.Bid, &ptr, binary.Bid / pricePrecision - lastBinary.Bid);
                    WriteField(BinaryField.Ask, &ptr, binary.Ask / pricePrecision - lastBinary.Ask);
                    lastBinary.Bid = binary.Bid / pricePrecision;
                    lastBinary.Ask = binary.Ask / pricePrecision;
                }
                if (binary.IsTrade)
                {
                    WriteField(BinaryField.Price, &ptr, binary.Price / pricePrecision - lastBinary.Price);
                    WriteField(BinaryField.Size, &ptr, binary.Size - lastBinary.Size);
                    lastBinary.Price = binary.Price / pricePrecision;
                    lastBinary.Size = binary.Size;
                }
                if (binary.IsOption)
                {
                    WriteField(BinaryField.Strike, &ptr, binary.Strike / pricePrecision - lastBinary.Strike);
                    lastBinary.Strike = binary.Strike / pricePrecision;
                    diff = (binary.UtcOptionExpiration - lastBinary.UtcOptionExpiration);
                    WriteField(BinaryField.OptionExpiration, &ptr, diff);
                    lastBinary.UtcOptionExpiration = binary.UtcOptionExpiration;
                }
                if (binary.HasDepthOfMarket)
                {
                    var field = (byte)((byte)BinaryField.BidSize << 3);
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        WriteBidSize(ref binary, field, i, &ptr);
                    }
                    field = (byte)((byte)BinaryField.AskSize << 3);
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        WriteAskSize(ref binary, field, i, &ptr);
                    }
                }
                writer.Position += ptr - fptr;
                writer.SetLength(writer.Position);
                *fptr = (byte)(ptr - fptr);
                byte checksum = 0;
                for (var p = fptr + 3; p < ptr; p++)
                {
                    checksum ^= *p;
                }
                *(fptr + 2) = (byte)(checksum ^ lastChecksum);
                lastChecksum = checksum;
                lastBinary = binary;
            }
        }

        private unsafe void ToWriterVersion9(ref TickBinary binary, MemoryStream writer)
        {
            var dataVersion = (byte) 9;
            writer.SetLength(writer.Position + minTickSize);
            byte[] buffer = writer.GetBuffer();
            fixed (byte* fptr = &buffer[writer.Position])
            {
                byte* ptr = fptr;
                ptr++; // Save space for size header.
                *(ptr) = dataVersion; ptr++;
                ptr++; // Save space for checksum.
                if (pricePrecision == 0L)
                {
                    SetPricePrecision(ref binary);
                    if (debug) log.Debug("Writing decimal places use in price compression.");
                    WriteField(BinaryField.Precision, &ptr, pricePrecision);
                }
                if (!isCompressStarted)
                {
                    if (debug) log.Debug("Writing Reset token during tick compression.");
                    WriteField(BinaryField.Reset, &ptr, 1);
                    var ts = new TimeStamp(binary.UtcTime);
                    isCompressStarted = true;
                }
                WriteField(BinaryField.ContentMask, &ptr, binary.contentMask - lastBinary.contentMask);
                var diff = (binary.UtcTime / 1000 - lastBinary.UtcTime / 1000);
                WriteField(BinaryField.Time, &ptr, diff);
                if (binary.IsQuote)
                {
                    WriteField(BinaryField.Bid, &ptr, (binary.Bid - lastBinary.Bid) / pricePrecision);
                    WriteField(BinaryField.Ask, &ptr, (binary.Ask - lastBinary.Ask) / pricePrecision);
                }
                if (binary.IsTrade)
                {
                    WriteField(BinaryField.Price, &ptr, (binary.Price - lastBinary.Price) / pricePrecision);
                    WriteField(BinaryField.Size, &ptr, binary.Size - lastBinary.Size);
                }
                if (binary.HasDepthOfMarket)
                {
                    var field = (byte)((byte)BinaryField.BidSize << 3);
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        WriteBidSize(ref binary, field, i, &ptr);
                    }
                    field = (byte)((byte)BinaryField.AskSize << 3);
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        WriteAskSize(ref binary, field, i, &ptr);
                    }
                }
                writer.Position += ptr - fptr;
                writer.SetLength(writer.Position);
                *fptr = (byte)(ptr - fptr);
                byte checksum = 0;
                for (var p = fptr + 3; p < ptr; p++)
                {
                    checksum ^= *p;
                }
                *(fptr + 2) = (byte)(checksum ^ lastChecksum);
                lastChecksum = checksum;
                lastBinary = binary;
                //				log.Info("Length = " + (ptr - fptr));
            }
        }
        private byte lastChecksum;

        private unsafe byte CalcChecksum(ref TickBinary binary, string direction)
        {
            var runningChecksum = 0L;
            runningChecksum ^= binary.UtcTime;
            runningChecksum ^= binary.contentMask;
            if (binary.IsQuote)
            {
                runningChecksum ^= binary.Bid/pricePrecision;
                runningChecksum ^= binary.Ask/pricePrecision;
            }
            if (binary.IsTrade)
            {
                runningChecksum ^= binary.Price/pricePrecision;
                runningChecksum ^= binary.Size;
            }
            if (binary.IsOption)
            {
                runningChecksum ^= binary.Strike/pricePrecision;
                runningChecksum ^= binary.UtcOptionExpiration;
            }
            if (binary.HasDepthOfMarket)
            {
                fixed (ushort* usptr = binary.DepthBidLevels)
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        var size = *(usptr + i);
                        runningChecksum ^= size;
                    }
                fixed (ushort* usptr = binary.DepthAskLevels)
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        var size = *(usptr + i);
                        runningChecksum ^= size;
                    }
            }
            byte checksum = 0;
            for (var i = 0; i < 8; i++)
            {
                var next = (byte)(runningChecksum & 0xFF);
                checksum ^= next;
                runningChecksum >>= 8;
            }
            if (verbose) log.Verbose(direction + " " + binary + ", CheckSum " + checksum);
            return checksum;
        }

        public unsafe void ToWriter(ref TickBinary binary, MemoryStream writer)
        {
            ToWriterVersion11(ref binary, writer);
        }

        public unsafe void ToWriterVersion8(ref TickBinary binary, MemoryStream writer)
        {
            var dataVersion = (byte) 8;
            writer.SetLength(writer.Position + minTickSize);
            byte[] buffer = writer.GetBuffer();
            fixed (byte* fptr = &buffer[writer.Position])
            {
                byte* ptr = fptr;
                ptr++; // Save space for size header.
                *(ptr) = dataVersion; ptr++;
                *(long*)(ptr) = binary.UtcTime / 1000; ptr += sizeof(long);
                *(ptr) = binary.contentMask; ptr++;
                if (binary.IsQuote)
                {
                    *(long*)(ptr) = binary.Bid; ptr += sizeof(long);
                    *(long*)(ptr) = binary.Ask; ptr += sizeof(long);
                }
                if (binary.IsTrade)
                {
                    *ptr = binary.Side; ptr++;
                    *(long*)(ptr) = binary.Price; ptr += sizeof(long);
                    *(int*)(ptr) = binary.Size; ptr += sizeof(int);
                }
                if (binary.HasDepthOfMarket)
                {
                    fixed (ushort* p = binary.DepthBidLevels)
                    {
                        for (int i = 0; i < TickBinary.DomLevels; i++)
                        {
                            *(ushort*)(ptr) = *(p + i); ptr += sizeof(ushort);
                        }
                    }
                    fixed (ushort* p = binary.DepthAskLevels)
                    {
                        for (int i = 0; i < TickBinary.DomLevels; i++)
                        {
                            *(ushort*)(ptr) = *(p + i); ptr += sizeof(ushort);
                        }
                    }
                }
                writer.Position += ptr - fptr;
                writer.SetLength(writer.Position);
                *fptr = (byte)(ptr - fptr);
            }
        }

        private unsafe long ReadField2(byte** ptr)
        {
            long result = 0L;
            var size = (FieldSize)(**ptr & 0x07);
            (*ptr)++;
            if (size == FieldSize.Byte)
            {
                result = (*(sbyte*)*ptr); (*ptr) += sizeof(sbyte);
            }
            else if (size == FieldSize.Short)
            {
                result = (*(short*)(*ptr)); (*ptr) += sizeof(short);
            }
            else if (size == FieldSize.Int)
            {
                result = (*(int*)(*ptr)); (*ptr) += sizeof(int);
            }
            else if (size == FieldSize.Long)
            {
                result = (*(long*)(*ptr)); (*ptr) += sizeof(long);
            }
            return result;
        }

        private unsafe long ReadField(byte** ptr)
        {
            long result = 0L;
            var size = (FieldSize)(**ptr & 0x07);
            (*ptr)++;
            if (size == FieldSize.Byte)
            {
                result = (**ptr); (*ptr)++;
            }
            else if (size == FieldSize.Short)
            {
                result = (*(short*)(*ptr)); (*ptr) += sizeof(short);
            }
            else if (size == FieldSize.Int)
            {
                result = (*(int*)(*ptr)); (*ptr) += sizeof(int);
            }
            else if (size == FieldSize.Long)
            {
                result = (*(long*)(*ptr)); (*ptr) += sizeof(long);
            }
            return result;
        }

        private unsafe void ReadBidSize(ref TickBinary binary, byte** ptr)
        {
            fixed (ushort* p = binary.DepthBidLevels)
            {
                var index = **ptr & 0x07;
                (*ptr)++;
                *(p + index) = (ushort)(*(p + index) + *(short*)(*ptr));
                (*ptr) += sizeof(short);
            }
        }

        private unsafe void ReadAskSize(ref TickBinary binary, byte** ptr)
        {
            fixed (ushort* p = binary.DepthAskLevels)
            {
                var index = **ptr & 0x07;
                (*ptr)++;
                *(p + index) = (ushort)(*(p + index) + *(short*)(*ptr));
                (*ptr) += sizeof(short);
            }
        }

        private TickIO fromFileVerboseTickIO = Factory.TickUtil.TickIO();
        private unsafe int FromFileVersion11(ref TickBinary binary, byte* fptr, int length)
        {
            if( verbose) log.Verbose("Before Dx " + binary);
            if (pricePrecision == 0L)
            {
                SetPricePrecision(ref binary);
            }
            length--;
            byte* ptr = fptr;
            var checksum = *ptr; ptr++;
            while ((ptr - fptr) < length)
            {
                var field = (BinaryField)(*ptr >> 3);
                switch (field)
                {
                    case BinaryField.Precision:
                        if (debug) log.Debug("Processing decimal place precision during tick de-compression.");
                        pricePrecision = ReadField2(&ptr);
                        break;
                    case BinaryField.Reset:
                        if (debug) log.Debug("Processing Reset during tick de-compression.");
                        ReadField2(&ptr);
                        var symbol = binary.Symbol;
                        binary = default(TickBinary);
                        binary.Symbol = symbol;
                        if (verbose) log.Verbose("Reset Dx " + binary);
                        break;
                    case BinaryField.ContentMask:
                        binary.contentMask += (byte)ReadField2(&ptr);
                        break;
                    case BinaryField.Time:
                        binary.UtcTime += ReadField2(&ptr);
                        break;
                    case BinaryField.Strike:
                        binary.Strike += ReadField2(&ptr) * pricePrecision;
                        break;
                    case BinaryField.OptionExpiration:
                        var readField = ReadField2(&ptr);
                        var currentExpiration = binary.UtcOptionExpiration + readField;
                        binary.UtcOptionExpiration = currentExpiration;
                        break;
                    case BinaryField.Bid:
                        binary.Bid += ReadField2(&ptr) * pricePrecision;
                        break;
                    case BinaryField.Ask:
                        binary.Ask += ReadField2(&ptr) * pricePrecision;
                        break;
                    case BinaryField.Price:
                        binary.Price += ReadField2(&ptr) * pricePrecision;
                        break;
                    case BinaryField.Size:
                        binary.Size += (int)ReadField2(&ptr);
                        break;
                    case BinaryField.BidSize:
                        ReadBidSize(ref binary, &ptr);
                        //fixed (ushort* usptr = binary.DepthBidLevels)
                        //    for (int i = 0; i < TickBinary.DomLevels; i++)
                        //    {
                        //        var size = *(usptr + i);
                        //    }
                        break;
                    case BinaryField.AskSize:
                        ReadAskSize(ref binary, &ptr);
                        //fixed (ushort* usptr = binary.DepthAskLevels)
                        //    for (int i = 0; i < TickBinary.DomLevels; i++)
                        //    {
                        //        var size = *(usptr + i);
                        //    }
                        break;
                    default:
                        throw new ApplicationException("Unknown tick field type: " + field);
                }
            }
            if (verbose)
            {
                fromFileVerboseTickIO.Inject(binary);
                log.Verbose("Dx tick: " + fromFileVerboseTickIO);
            }
            var expectedChecksum = CalcChecksum(ref binary, "Dx");
            if (expectedChecksum != checksum)
            {
                fromFileVerboseTickIO.Inject(binary);
                throw new ApplicationException("Checksum mismatch " + checksum + " vs. " + expectedChecksum + ". This means integrity checking of tick compression failed. The tick which failed checksum: " + fromFileVerboseTickIO);
            }

            var len = (int)(ptr - fptr);
            return len;
        }

        private unsafe int FromFileVersion10(ref TickBinary binary, byte* fptr, int length)
        {
            // Backwards compatibility. The first iteration of version
            // 9 never stored the price precision in the file.
            if (pricePrecision == 0L)
            {
                SetPricePrecision(ref binary);
            }
            length--;
            byte* ptr = fptr;
            var checksum = *ptr; ptr++;
            //			var end = fptr + length;
            //			byte testchecksum = 0;
            //			for( var p = fptr+1; p<end; p++) {
            //				testchecksum ^= *p;	
            //			}

            while ((ptr - fptr) < length)
            {
                var field = (BinaryField)(*ptr >> 3);
                switch (field)
                {
                    case BinaryField.Precision:
                        if (debug) log.Debug("Processing decimal place precision during tick de-compression.");
                        pricePrecision = ReadField(&ptr);
                        break;
                    case BinaryField.Reset:
                        if (debug) log.Debug("Processing Reset during tick de-compression.");
                        ReadField(&ptr);
                        var symbol = binary.Symbol;
                        binary = default(TickBinary);
                        binary.Symbol = symbol;
                        lastChecksum = 0;
                        break;
                    case BinaryField.ContentMask:
                        binary.contentMask += (byte)ReadField(&ptr);
                        break;
                    case BinaryField.Time:
                        binary.UtcTime += ReadField(&ptr);
                        break;
                    case BinaryField.Strike:
                        binary.Strike += ReadField(&ptr) * pricePrecision;
                        break;
                    case BinaryField.OptionExpiration:
                        var readField = ReadField(&ptr);
                        var currentExpiration = binary.UtcOptionExpiration + readField;
                        var time = new TimeStamp(currentExpiration);
                        binary.UtcOptionExpiration = currentExpiration;
                        break;
                    case BinaryField.Bid:
                        binary.Bid += ReadField(&ptr) * pricePrecision;
                        break;
                    case BinaryField.Ask:
                        binary.Ask += ReadField(&ptr) * pricePrecision;
                        break;
                    case BinaryField.Price:
                        binary.Price += ReadField(&ptr) * pricePrecision;
                        break;
                    case BinaryField.Size:
                        binary.Size += (int)ReadField(&ptr);
                        break;
                    case BinaryField.BidSize:
                        ReadBidSize(ref binary, &ptr);
                        break;
                    case BinaryField.AskSize:
                        ReadAskSize(ref binary, &ptr);
                        break;
                    default:
                        throw new ApplicationException("Unknown tick field type: " + field);
                }
            }

            //			if( (byte) (testchecksum ^ lastChecksum) != checksum) {
            //				 System.Diagnostics.Debugger.Break();
            //				throw new ApplicationException("Checksum mismatch " + checksum + " vs. " + (byte) (testchecksum ^ lastChecksum) + ". This means integrity checking of tick compression failed.");
            //			}
            //			lastChecksum = testchecksum;

            int len = (int)(ptr - fptr);
            return len;
        }

        private unsafe int FromFileVersion9(ref TickBinary binary, byte* fptr, int length)
        {
            // Backwards compatibility. The first iteration of version
            // 9 never stored the price precision in the file.
            if (pricePrecision == 0L)
            {
                SetPricePrecision(ref binary);
            }
            length--;
            byte* ptr = fptr;
            var checksum = *ptr; ptr++;
            //			var end = fptr + length;
            //			byte testchecksum = 0;
            //			for( var p = fptr+1; p<end; p++) {
            //				testchecksum ^= *p;	
            //			}

            while ((ptr - fptr) < length)
            {
                var field = (BinaryField)(*ptr >> 3);
                switch (field)
                {
                    case BinaryField.Precision:
                        if (debug) log.Debug("Processing decimal place precision during tick de-compression.");
                        pricePrecision = ReadField(&ptr);
                        break;
                    case BinaryField.Reset:
                        if (debug) log.Debug("Processing Reset during tick de-compression.");
                        ReadField(&ptr);
                        var symbol = binary.Symbol;
                        binary = default(TickBinary);
                        binary.Symbol = symbol;
                        lastChecksum = 0;
                        break;
                    case BinaryField.ContentMask:
                        binary.contentMask += (byte)ReadField(&ptr);
                        break;
                    case BinaryField.Time:
                        var diff = ReadField(&ptr);
                        binary.UtcTime += (diff * 1000);
                        break;
                    case BinaryField.Bid:
                        binary.Bid += ReadField(&ptr) * pricePrecision;
                        break;
                    case BinaryField.Ask:
                        binary.Ask += ReadField(&ptr) * pricePrecision;
                        break;
                    case BinaryField.Price:
                        binary.Price += ReadField(&ptr) * pricePrecision;
                        break;
                    case BinaryField.Size:
                        binary.Size += (int)ReadField(&ptr);
                        break;
                    case BinaryField.BidSize:
                        ReadBidSize(ref binary, &ptr);
                        break;
                    case BinaryField.AskSize:
                        ReadAskSize(ref binary, &ptr);
                        break;
                    default:
                        throw new ApplicationException("Unknown tick field type: " + field);
                }
            }

            //			if( (byte) (testchecksum ^ lastChecksum) != checksum) {
            //				 System.Diagnostics.Debugger.Break();
            //				throw new ApplicationException("Checksum mismatch " + checksum + " vs. " + (byte) (testchecksum ^ lastChecksum) + ". This means integrity checking of tick compression failed.");
            //			}
            //			lastChecksum = testchecksum;

            int len = (int)(ptr - fptr);
            return len;
        }

        private unsafe int FromFileVersion8(ref TickBinary binary, byte* fptr)
        {
            if (pricePrecision == 0L)
            {
                SetPricePrecision(ref binary);
            }
            byte* ptr = fptr;
            binary.UtcTime = *(long*)ptr; ptr += sizeof(long);
            binary.UtcTime *= 1000;
            binary.contentMask = *ptr; ptr++;
            if (binary.IsQuote)
            {
                binary.Bid = *(long*)ptr; ptr += sizeof(long);
                binary.Bid = Math.Round(binary.Bid.ToDouble(), priceDecimals).ToLong();
                binary.Ask = *(long*)ptr; ptr += sizeof(long);
                binary.Ask = Math.Round(binary.Ask.ToDouble(), priceDecimals).ToLong();
            }
            if (binary.IsTrade)
            {
                binary.Side = *ptr; ptr++;
                binary.Price = *(long*)ptr; ptr += sizeof(long);
                binary.Price = Math.Round(binary.Price.ToDouble(), priceDecimals).ToLong();
                binary.Size = *(int*)ptr; ptr += sizeof(int);
            }
            if (binary.HasDepthOfMarket)
            {
                fixed (ushort* p = binary.DepthBidLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = *(ushort*)ptr; ptr += 2;
                    }
                }
                fixed (ushort* p = binary.DepthAskLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = *(ushort*)ptr; ptr += 2;
                    }
                }
            }
            int len = (int)(ptr - fptr);
            return len;
        }

        private int FromFileVersion7(ref TickBinary binary, BinaryReader reader)
        {
            int position = 0;
            double d = reader.ReadDouble(); position += 8;
            //			if( d < 0) {
            //				int x = 0;
            //			}
            binary.UtcTime = new TimeStamp(d).Internal;
            binary.contentMask = reader.ReadByte(); position += 1;
            if (binary.IsQuote)
            {
                binary.Bid = reader.ReadInt64(); position += 8;
                binary.Ask = reader.ReadInt64(); position += 8;
                if (!binary.IsTrade)
                {
                    binary.Price = (binary.Bid + binary.Ask) / 2;
                }
            }
            if (binary.IsTrade)
            {
                binary.Side = reader.ReadByte(); position += 1;
                binary.Price = reader.ReadInt64(); position += 8;
                binary.Size = reader.ReadInt32(); position += 4;
                if (binary.Price == 0)
                {
                    binary.Price = (binary.Bid + binary.Ask) / 2;
                }
                if (!binary.IsQuote)
                {
                    binary.Bid = binary.Ask = binary.Price;
                }
            }
            if (binary.HasDepthOfMarket)
            {
                fixed (ushort* p = binary.DepthBidLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = reader.ReadUInt16(); position += 2;
                    }
                }
                fixed (ushort* p = binary.DepthAskLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = reader.ReadUInt16(); position += 2;
                    }
                }
            }
            return position;
        }

        private int FromFileVersion6(ref TickBinary binary, BinaryReader reader)
        {
            int position = 0;
            binary.UtcTime = new TimeStamp(reader.ReadDouble()).Internal; position += 8;
            binary.Bid = reader.ReadInt64(); position += 8;
            binary.Ask = reader.ReadInt64(); position += 8;
            binary.contentMask = 0;
            binary.IsQuote = true;
            bool dom = reader.ReadBoolean(); position += 1;
            if (dom)
            {
                binary.IsTrade = true;
                binary.HasDepthOfMarket = true;
                binary.Side = reader.ReadByte(); position += 1;
                binary.Price = reader.ReadInt64(); position += 8;
                if (binary.Price == 0) { binary.Price = (binary.Bid + binary.Ask) / 2; }
                binary.Size = reader.ReadInt32(); position += 4;
                fixed (ushort* p = binary.DepthBidLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = reader.ReadUInt16(); position += 2;
                    }
                }
                fixed (ushort* p = binary.DepthAskLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = reader.ReadUInt16(); position += 2;
                    }
                }
            }
            return position;
        }

        private int FromFileVersion5(ref TickBinary binary, BinaryReader reader)
        {
            int position = 0;
            binary.UtcTime = new TimeStamp(reader.ReadDouble()).Internal; position += 8;
            binary.Bid = reader.ReadInt32(); position += 4;
            sbyte spread = reader.ReadSByte(); position += 1;
            binary.Ask = binary.Bid + spread;
            binary.Bid *= OlderFormatConvertToLong;
            binary.Ask *= OlderFormatConvertToLong;
            binary.contentMask = 0;
            binary.IsQuote = true;
            bool hasDOM = reader.ReadBoolean(); position += 1;
            if (hasDOM)
            {
                binary.IsTrade = true;
                binary.HasDepthOfMarket = true;
                binary.Price = reader.ReadInt32(); position += 4;
                binary.Price *= OlderFormatConvertToLong;
                if (binary.Price == 0) { binary.Price = (binary.Bid + binary.Ask) / 2; }
                binary.Size = reader.ReadUInt16(); position += 2;
                fixed (ushort* p = binary.DepthBidLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = reader.ReadUInt16(); position += 2;
                    }
                }
                fixed (ushort* p = binary.DepthAskLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = reader.ReadUInt16(); position += 2;
                    }
                }
            }
            return position;
        }

        private int FromFileVersion4(ref TickBinary binary, BinaryReader reader)
        {
            int position = 0;
            reader.ReadByte(); position += 1;
            // throw away symbol
            for (int i = 0; i < TickBinary.SymbolSize; i++)
            {
                reader.ReadChar(); position += 2;
            }
            binary.UtcTime = new TimeStamp(reader.ReadDouble()).Internal; position += 8;
            binary.Bid = reader.ReadInt32(); position += 4;
            sbyte spread = reader.ReadSByte(); position += 1;
            binary.Ask = binary.Bid + spread;
            binary.Bid *= OlderFormatConvertToLong;
            binary.Ask *= OlderFormatConvertToLong;
            binary.contentMask = 0;
            binary.IsQuote = true;
            bool hasDOM = reader.ReadBoolean(); position += 1;
            if (hasDOM)
            {
                binary.IsTrade = true;
                binary.HasDepthOfMarket = true;
                binary.Side = reader.ReadByte(); position += 1;
                binary.Price = reader.ReadInt32(); position += 4;
                binary.Price *= OlderFormatConvertToLong;
                if (binary.Price == 0) { binary.Price = (binary.Bid + binary.Ask) / 2; }
                binary.Size = reader.ReadUInt16(); position += 2;
                fixed (ushort* p = binary.DepthBidLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = reader.ReadUInt16(); position += 2;
                    }
                }
                fixed (ushort* p = binary.DepthAskLevels)
                {
                    for (int i = 0; i < TickBinary.DomLevels; i++)
                    {
                        *(p + i) = reader.ReadUInt16(); position += 2;
                    }
                }
            }
            return position;
        }

        private int FromFileVersion3(ref TickBinary binary, BinaryReader reader)
        {
            int position = 0;
            DateTime tickTime = DateTime.FromBinary(reader.ReadInt64()); position += 8;
            binary.UtcTime = new TimeStamp(tickTime.ToLocalTime()).Internal;
            binary.Bid = reader.ReadInt32(); position += 4;
            sbyte spread = reader.ReadSByte(); position += 1;
            binary.Ask = binary.Bid + spread;
            binary.Bid *= OlderFormatConvertToLong;
            binary.Ask *= OlderFormatConvertToLong;
            binary.Side = reader.ReadByte(); position += 1;
            binary.Price = reader.ReadInt32(); position += 4;
            binary.Price *= OlderFormatConvertToLong;
            if (binary.Price == 0) { binary.Price = (binary.Bid + binary.Ask) / 2; }
            binary.Size = reader.ReadUInt16(); position += 2;
            fixed (ushort* p = binary.DepthBidLevels)
            {
                for (int i = 0; i < TickBinary.DomLevels; i++)
                {
                    *(p + i) = reader.ReadUInt16(); position += 2;
                }
            }
            fixed (ushort* p = binary.DepthAskLevels)
            {
                for (int i = 0; i < TickBinary.DomLevels; i++)
                {
                    *(p + i) = reader.ReadUInt16(); position += 2;
                }
            }
            binary.contentMask = 0;
            binary.IsQuote = true;
            binary.IsTrade = true;
            binary.HasDepthOfMarket = true;
            return position;
        }

        private int FromFileVersion2(ref TickBinary binary, BinaryReader reader)
        {
            int position = 0;
            DateTime tickTime = DateTime.FromBinary(reader.ReadInt64()); position += 8;
            binary.UtcTime = new TimeStamp(tickTime.ToLocalTime()).Internal;
            binary.Bid = reader.ReadInt32(); position += 4;
            sbyte spread = reader.ReadSByte(); position += 1;
            binary.Ask = binary.Bid + spread;
            binary.Bid *= OlderFormatConvertToLong;
            binary.Ask *= OlderFormatConvertToLong;
            fixed (ushort* p = binary.DepthBidLevels)
            {
                *p = (ushort)reader.ReadInt32(); position += 4;
            }
            fixed (ushort* p = binary.DepthAskLevels)
            {
                *p = (ushort)reader.ReadInt32(); position += 4;
            }
            binary.contentMask = 0;
            binary.IsQuote = true;
            binary.HasDepthOfMarket = true;
            binary.Side = (byte)TradeSide.Unknown;
            binary.Price = (binary.Bid + binary.Ask) / 2;
            binary.Size = 0;
            return position;
        }

        private int FromFileVersion1(ref TickBinary binary, BinaryReader reader)
        {
            int position = 0;

            long int64 = reader.ReadInt64() ^ -9223372036854775808L;
            DateTime tickTime = DateTime.FromBinary(int64); position += 8;
            TimeStamp timeStamp = (TimeStamp)tickTime.AddHours(-4);
            binary.UtcTime = timeStamp.Internal;
            binary.Bid = reader.ReadInt32(); position += 4;
            sbyte spread = reader.ReadSByte(); position += 1;
            binary.Ask = binary.Bid + spread;
            binary.Bid *= OlderFormatConvertToLong;
            binary.Ask *= OlderFormatConvertToLong;
            binary.contentMask = 0;
            binary.IsQuote = true;
            binary.Price = (binary.Bid + binary.Ask) / 2;
            return position;
        }

        public int FromReader(ref TickBinary binary, MemoryStream reader)
        {
            fixed (byte* fptr = reader.GetBuffer())
            {
                byte* sptr = fptr + reader.Position;
                byte* ptr = sptr;
                byte size = *ptr; ptr++;
                var dataVersion = *ptr; ptr++;
                switch (dataVersion)
                {
                    case 8:
                        ptr += FromFileVersion8(ref binary, ptr);
                        break;
                    case 9:
                        ptr += FromFileVersion9(ref binary, ptr, (short)(size - 1));
                        break;
                    case 10:
                        ptr += FromFileVersion10(ref binary, ptr, (short)(size - 1));
                        break;
                    case 11:
                        ptr += FromFileVersion11(ref binary, ptr, (short)(size - 1));
                        break;
                    default:
                        throw new ApplicationException("Unknown Tick Version Number " + dataVersion);
                }
                reader.Position += (int)(ptr - sptr);
                return dataVersion;
            }
        }

        /// <summary>
        /// Old style FormatReader for legacy versions of TickZoom tck
        /// data files.
        /// </summary>
        public int FromReader(ref TickBinary binary, byte dataVersion, BinaryReader reader)
        {
            var symbol = binary.Symbol;
            binary = default(TickBinary);
            binary.Symbol = symbol;
            int position = 0;
            switch (dataVersion)
            {
                case 1:
                    position += FromFileVersion1(ref binary, reader);
                    break;
                case 2:
                    position += FromFileVersion2(ref binary, reader);
                    break;
                case 3:
                    position += FromFileVersion3(ref binary, reader);
                    break;
                case 4:
                    position += FromFileVersion4(ref binary, reader);
                    break;
                case 5:
                    position += FromFileVersion5(ref binary, reader);
                    break;
                case 6:
                    position += FromFileVersion6(ref binary, reader);
                    break;
                case 7:
                    position += FromFileVersion7(ref binary, reader);
                    break;
                default:
                    throw new ApplicationException("Unknown Tick Version Number " + dataVersion);
            }
            return dataVersion;
        }

        private bool memcmp(ushort* array1, ushort* array2)
        {
            for (int i = 0; i < TickBinary.DomLevels; i++)
            {
                if (*(array1 + i) != *(array2 + i)) return false;
            }
            return true;
        }

    }
}