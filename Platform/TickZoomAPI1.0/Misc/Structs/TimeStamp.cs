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
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace TickZoom.Api
{
	public struct TimeStamp : IComparable<TimeStamp>
	{
		private long _timeStamp;
		private static readonly TimeStamp maxValue = new TimeStamp(lDoubleDayMax);
		private static readonly TimeStamp minValue = new TimeStamp(lDoubleDayMin);
		
		public static TimeStamp MaxValue { 
			get { return maxValue; }
		}
		public static TimeStamp MinValue { 
			get { return minValue; }
		}
		public const double XLDay1 = 2415018.5;
		public const long lXLDay1 = (long) (XLDay1 * MicrosecondsPerDay);

		public const long lJulianDayMin = 0L;
		public const long lJulianDayMax = 464268974400000000L;
		
		public const long lDoubleDayMin = lJulianDayMin - lXLDay1;
		public const long lDoubleDayMax = lJulianDayMax - lXLDay1;
		public const long MonthsPerYear = 12L;
		public const long HoursPerDay = 24L;
		public const long MinutesPerHour = 60L;
		public const long SecondsPerMinute = 60L;
		public const long MinutesPerDay = 1440L;
		public const long SecondsPerDay = 86400L;
		public const long MicrosecondsPerMillisecond =  1000L;
		public const long MicrosecondsPerSecond =  1000000L;
		public const long MillisecondsPerSecond =  1000L;
		public const long MicrosecondsPerMinute =  60000000L;
		public const long MicrosecondsPerHour = 3600000000L;
		public const long MicrosecondsPerDay = 86400000000L;
		public const long MillisecondsPerDay = 86400000L;
		public const long DateTimeToTimeStampAdjust = 59926435200000750L;
		public const string DefaultFormatStr = "yyyy-MM-dd HH:mm:ss.fffuuu";
		
		public void Assign( int year, int month, int day, int hour, int minute, int second, int millis) {
			Interlocked.Exchange(ref _timeStamp, CalendarDateTotimeStamp( year, month, day, hour, minute, second, millis, 0));
		}
		
		public static TimeStamp FromOADate(double value) {
			value *= MicrosecondsPerDay;
			return new TimeStamp( (long) value);
		}
		
		private static long DoubleToLong( double value) {
			value *= MillisecondsPerDay;
			value += 0.5;
			long result = (long) value;
			return result * 1000;
		}
		
		private static double LongToDouble( long value) {
			return value / (double) MicrosecondsPerDay;
		}
		
//		#if FIXED
		public Elapsed TimeOfDay {
			get { int other;
				  int hour;
				  int minute;
				  int second;
				  int millis;
				  int micros;
				  GetDate(out other,out other,out other,out hour,out minute,out second,out millis, out micros);
				  return new Elapsed(micros+
				        millis*MicrosecondsPerMillisecond+
				  		second*MicrosecondsPerSecond+
				  		minute*MicrosecondsPerMinute+
				  		hour*MicrosecondsPerHour);
			}
		}
		
		public int Year {
			get { int other;
				  int year;
				  GetDate(out year,out other,out other,out other,out other,out other,out other, out other);
				  return year;
			}
		}
		
		public WeekDay WeekDay {
			get { return (WeekDay) timeStampToWeekDay(_timeStamp); }
		}
		public int Month {
			get { int other;
				  int month;
				  GetDate(out other,out month,out other,out other,out other,out other,out other, out other);
				  return month;
			}
		}
		public int Day {
			get { int other;
				  int day;
				  GetDate(out other,out other,out day,out other,out other,out other,out other, out other);
				  return day;
			}
		}
		public int Hour {
			get { int other;
				  int hour;
				  GetDate(out other,out other,out other,out hour,out other,out other,out other, out other);
				  return hour;
			}
		}
		
		public int Minute {
			get { int other;
				  int minute;
				  GetDate(out other,out other,out other,out other,out minute,out other,out other, out other);
				  return minute;
			}
		}
		
		public int Second {
			get { int other;
				  int second;
				  GetDate(out other,out other,out other,out other,out other,out second,out other, out other);
				  return second;
			}
		}
		
		public int Millisecond {
			get { int other;
				  int millisecond;
				  GetDate(out other,out other,out other,out other,out other,out other,out millisecond, out other);
				  return millisecond;
			}
		}
		
		public int Microsecond {
			get { int other;
				  int microsecond;
				  GetDate(out other,out other,out other,out other,out other,out other,out other, out microsecond);
				  return microsecond;
			}
		}
		
//		#endif
		private static void Synchronize() {
			lock(locker) {
				Thread.CurrentThread.Priority = ThreadPriority.Highest;
	        	lastDateTime = DateTime.UtcNow.Ticks;
	        	long currentDateTime;
        		long frequency = System.Diagnostics.Stopwatch.Frequency;
        		stopWatchFrequency = frequency;
	        	do {
	        		lastStopWatch = System.Diagnostics.Stopwatch.GetTimestamp();
	        		currentDateTime = DateTime.UtcNow.Ticks;
	        	} while( lastDateTime == currentDateTime);
	        	lastDateTime = currentDateTime;
	        	Thread.CurrentThread.Priority = ThreadPriority.Normal;
			}
		}
		private static object locker = new object();
		private static long lastStopWatch;
		private static long stopWatchFrequency = 1L;
		private static long lastDateTime;
		
		public static TimeStamp Parse(string value) {
			return new TimeStamp(value);
		}

        private struct HardwareTimeStamp
        {
            public long OriginalSystemClock;
            public TimeStamp OriginalTimeStamp;
            public long LastSystemClock;
            public long LastTimeStamp;
            public long SynchronizeOffset;
            public override string ToString()
            {
                return LastTimeStamp.ToString();
            }
        }

	    private static int hardwareTimestampsCount;
	    private static HardwareTimeStamp[] hardwareTimeStamps = new HardwareTimeStamp[64];
	    private static long adjustedFrequency;

        private static TimeStamp ResetUtcNow(int index)
        {
            var process = Process.GetCurrentProcess();
            var thread = Thread.CurrentThread;
            var priorityClass = process.PriorityClass;
            var threadPriority = thread.Priority;
            thread.Priority = ThreadPriority.Highest;
            process.PriorityClass = ProcessPriorityClass.RealTime;
            var dtUtcNow = DateTime.UtcNow;
            var timeStamp = new TimeStamp(dtUtcNow);
            hardwareTimeStamps[index].OriginalTimeStamp = timeStamp;
            stopWatchFrequency = Stopwatch.Frequency;
            adjustedFrequency = (stopWatchFrequency << 20) / 1000000L;
            hardwareTimeStamps[index].OriginalSystemClock = Stopwatch.GetTimestamp();
            thread.Priority = threadPriority;
            process.PriorityClass = priorityClass;
            return timeStamp;
        }
		
		public static TimeStamp UtcNow {
			get
			{
                var index = Thread.CurrentThread.ManagedThreadId;
                if( index > hardwareTimestampsCount)
                {
                    hardwareTimestampsCount = index;
                }
                if( index >= hardwareTimeStamps.Length)
                {
                    Array.Resize(ref hardwareTimeStamps, hardwareTimeStamps.Length * 2);
                }
                if (hardwareTimeStamps[index].OriginalTimeStamp == default(TimeStamp))
                {
                    var timeStamp = ResetUtcNow(index);
                    hardwareTimeStamps[index].LastTimeStamp = timeStamp.Internal;
                    hardwareTimeStamps[index].LastSystemClock = Stopwatch.GetTimestamp();
                }
			    TimeStamp result;
			    var count = 0;
                while (!CalculateTimeStamp(index, out result))
                {
                    ResetUtcNow(index);
                    ++count;
                }
                if( count > 0)
                {
                    Interlocked.Increment(ref adjustedClockCounter);
                }
                return result;
            }
		}

	    private static long adjustedClockCounter;
	    private static Log log;

        private static bool CalculateTimeStamp(int index, out TimeStamp timeStamp)
        {
            var result = true;
            var systemClock = Stopwatch.GetTimestamp();
            var systemClockChange = systemClock - hardwareTimeStamps[index].OriginalSystemClock + hardwareTimeStamps[index].SynchronizeOffset;
            var adjustedClockChange = (systemClockChange << 20) / adjustedFrequency;
            if (adjustedClockChange < 0)
            {
                result = false;
            }
            timeStamp = hardwareTimeStamps[index].OriginalTimeStamp;
            timeStamp.AddMicroseconds(adjustedClockChange);
            var timeDiff = timeStamp.Internal - hardwareTimeStamps[index].LastTimeStamp;
            if (timeDiff < 0)
            {
                result = false;
            }
            for (var i = 0; i < hardwareTimestampsCount; i++ )
            {
                var diff = timeStamp.Internal - hardwareTimeStamps[i].LastTimeStamp;
                if( diff < 0)
                {
                    hardwareTimeStamps[i].SynchronizeOffset = -diff;
                    result = false;
                    break;
                }
            }
            if (result)
            {
                hardwareTimeStamps[index].LastSystemClock = systemClock;
                hardwareTimeStamps[index].LastTimeStamp = timeStamp.Internal;
            }
            return result;
        }
		

        private static void Error(string timeString, int pos)
        {
            throw new ApplicationException("Unexpected date time char '" + timeString[pos] + "' in " + timeString + " at position " + pos + ".");
        }

		public TimeStamp( string timeString)
		{
            try
            {
                if (timeString.Length < 6) throw new ApplicationException("String too short to be any valid date and time.");
                int spaceCount = 0,
                    hyphenCount = 0,
                    slashCount = 0,
                    hyphenPos = 0,
                    spacePos = 0,
                    startPos = 0,
                    endPos = timeString.Length;
                var start = true;
                // Trim whitespace from the end.
                for (var i = timeString.Length - 1; i >= 0; i--)
                {
                    var chr = timeString[i];
                    if (char.IsWhiteSpace(chr))
                    {
                        endPos--;
                    }
                    else
                    {
                        break;
                    }
                }
                // Analyze the string.
                for (var i = 0; i < endPos; i++)
                {
                    var chr = timeString[i];
                    if (start && char.IsWhiteSpace(chr))
                    {
                        startPos++;
                        continue;
                    }
                    start = false;
                    switch (timeString[i])
                    {
                        case ' ':
                            spaceCount++;
                            if (spacePos == 0)
                            {
                                spacePos = i;
                            }
                            break;
                        case '-':
                            hyphenCount++;
                            if (hyphenPos == 0)
                            {
                                hyphenPos = i;
                            }
                            break;
                        case '/':
                            slashCount++;
                            break;
                    }
                }
                int dateEndPos;
                char dateSeparator;
                if (hyphenCount == 2)
                {
                    dateSeparator = '-';
                }
                else if (slashCount == 2)
                {
                    dateSeparator = '/';
                }
                else
                {
                    dateSeparator = (char)0;
                }
                if (spaceCount == 1)
                {
                    dateEndPos = spacePos;
                }
                else if (hyphenCount == 1)
                {
                    dateEndPos = hyphenPos;
                }
                else
                {
                    dateEndPos = 0;
                }
                int hour = 0, minute = 0, second = 0, millis = 0, micros = 0;
                if (dateEndPos > 0)
                {
                    var timePos = dateEndPos + 1;
                    hour = (timeString[timePos++] - '0') * 10 + (timeString[timePos++] - '0');
                    if (timeString[timePos] != ':') Error(timeString, timePos);
                    timePos++; // skip the :
                    minute = (timeString[timePos++] - '0') * 10 + (timeString[timePos++] - '0');
                    if (timePos + 3 <= endPos)
                    {
                        if (timeString[timePos] != ':') Error(timeString, timePos);
                        timePos++;
                        second = (timeString[timePos++] - '0') * 10 + (timeString[timePos++] - '0');
                        if (timePos + 4 <= endPos)
                        {
                            if (timeString[timePos] != '.') Error(timeString, timePos);
                            timePos++;
                            millis = (timeString[timePos++] - '0') * 100 + (timeString[timePos++] - '0') * 10 + (timeString[timePos++] - '0');
                            if (timePos + 3 <= endPos)
                            {
                                if (timeString[timePos] == '.') timePos++;
                                micros = (timeString[timePos++] - '0') * 100 + (timeString[timePos++] - '0') * 10 + (timeString[timePos++] - '0');
                            }
                        }
                    }
                }
                else
                {
                    dateEndPos = endPos;
                }
                int year = 1970;
                int month = 5;
                int day = 14;
                var datePos = startPos;
                if (timeString[startPos + 4] == dateSeparator)
                {
                    // Starts wth 4 digit year.
                    year = (timeString[datePos++] - '0') * 1000 + (timeString[datePos++] - '0') * 100 +
                           (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                    if (timeString[datePos] != dateSeparator) Error(timeString, datePos);
                    datePos++;
                    month = (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                    if (timeString[datePos] != dateSeparator) Error(timeString, datePos);
                    datePos++;
                    day = (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                }
                else if (timeString[startPos + 2] == dateSeparator)
                {
                    // Starts with either 2 digit year or 2 digit month
                    year = (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                    if (timeString[datePos] != dateSeparator) Error(timeString, datePos);
                    datePos++;
                    month = (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                    if (timeString[datePos] != dateSeparator) Error(timeString, datePos);
                    datePos++;
                    if (datePos + 4 <= dateEndPos)
                    {
                        // Four digits left so must be the year last.
                        day = month;
                        month = year;
                        year = (timeString[datePos++] - '0') * 1000 + (timeString[datePos++] - '0') * 100 +
                               (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                    }
                }
                else if (datePos + 8 == dateEndPos)
                {
                    // Starts wth 4 digit year.
                    year = (timeString[datePos++] - '0') * 1000 + (timeString[datePos++] - '0') * 100 +
                           (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                    month = (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                    day = (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                    if (month > 12 || day > 31 || month <= 0 || day <= 0)
                    {
                        datePos -= 8;
                        month = (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                        day = (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                        year = (timeString[datePos++] - '0') * 1000 + (timeString[datePos++] - '0') * 100 +
                               (timeString[datePos++] - '0') * 10 + (timeString[datePos++] - '0');
                    }
                }
                _timeStamp = CalendarDateTotimeStamp(year, month, day, hour, minute, second, millis, micros);
                if (Month != month || Year != year || Day != day || Hour != hour || Minute != minute || Second != second || Millisecond != millis)
                {
                    throw new ApplicationException("Invalid date for " + timeString);
                }
            }
            catch( Exception ex)
            {
                throw new ApplicationException("Error while parsing dat time string: " + timeString + ": " + ex.Message,ex);
            }
		}
	    
	    private static int ToInt32(string value) {
	    	if( string.IsNullOrEmpty(value)){
	    		return 0;
	    	}
	    	int result = 0;
	    	for(int i=0; i<value.Length; i++) {
	    		var digit = (byte) value[i];
	    		if( digit < 48 || digit > 57) {
	    			throw new ApplicationException("Format Error. Digit " + i + " in '" + value + "' is not a numerical digit.");
	    		}
	    		result *= 10;
	    		result += digit - 48;
	    	}
	    	return result;
	    }
		
		public TimeStamp( long timeStamp )
		{
			_timeStamp = timeStamp;
		}
		
		public TimeStamp( double timeStamp )
		{
			_timeStamp = DoubleToLong(timeStamp);
		}
		public TimeStamp( DateTime dateTime )
		{
            _timeStamp = dateTime.Ticks/10 - DateTimeToTimeStampAdjust;
        }
		public TimeStamp( int year, int month, int day )
		{
			_timeStamp = CalendarDateTotimeStamp( year, month, day, 0, 0, 0 );
		}
		public TimeStamp( int year, int month, int day, int hour, int minute, int second )
		{
			_timeStamp = CalendarDateTotimeStamp( year, month, day, hour, minute, second );
		}
//		public TimeStamp( int year, int month, int day, int hour, int minute, int second )
//		{
//			_timeStamp = CalendarDateTotimeStamp( year, month, day, hour, minute, second );
//		}
		public TimeStamp( int year, int month, int day, int hour, int minute, int second, int millisecond )
		{
			_timeStamp = CalendarDateTotimeStamp( year, month, day, hour, minute, second, millisecond, 0);
		}
		public TimeStamp( TimeStamp rhs )
		{
			_timeStamp = rhs._timeStamp;
		}
	
		public long Internal
		{
			get { return _timeStamp; }
			set { _timeStamp = value; }
		}
		
		public double ToOADate() {
			return _timeStamp / (double) MicrosecondsPerDay;
		}

		public bool IsValidDate
		{
			get { return _timeStamp >= lDoubleDayMin && _timeStamp <= lDoubleDayMax; }
		}
		
		public DateTime DateTime
		{
			get { return timeStampToDateTime( _timeStamp ); }
			set { _timeStamp = DateTimeToTimeStamp( value ); }
		}
		
		public long JulianDay
		{
			get { return timeStampToJulianDay( _timeStamp ); }
			set { _timeStamp = JulianDayTotimeStamp( value ); }
		}
		
		public double DecimalYear
		{
			get { return timeStampToDecimalYear( _timeStamp ); }
			set { _timeStamp = DecimalYearTotimeStamp( value ); }
		}
	
		private static bool CheckValidDate( long timeStamp )
		{
			return timeStamp >= lDoubleDayMin && timeStamp <= lDoubleDayMax;
		}

		public static long MakeValidDate( long timeStamp )
		{
			if ( timeStamp < lDoubleDayMin )
				timeStamp = lDoubleDayMin;
			if ( timeStamp > lDoubleDayMax )
				timeStamp = lDoubleDayMax;
			return timeStamp;
		}
		
		public void GetDate( out int year, out int month, out int day )
		{
			int hour, minute, second;
			
			timeStampToCalendarDate( _timeStamp, out year, out month, out day, out hour, out minute, out second );
		}
		
		public void GetDate( out int year, out int month, out int day,
						out int hour, out int minute, out int second, out int millis, out int micros)
		{
			timeStampToCalendarDate( _timeStamp, out year, out month, out day, out hour, out minute, out second, out millis, out micros);
		}
		
		public void SetDate( int year, int month, int day )
		{
			_timeStamp = CalendarDateTotimeStamp( year, month, day, 0, 0, 0 );
		}
		
		public void GetDate( out int year, out int month, out int day,
						out int hour, out int minute, out int second )
		{
			timeStampToCalendarDate( _timeStamp, out year, out month, out day, out hour, out minute, out second );
		}

		public void SetDate( int year, int month, int day, int hour, int minute, int second )
		{
			_timeStamp = CalendarDateTotimeStamp( year, month, day, hour, minute, second );
		}
		
		public double GetDayOfYear()
		{
			return timeStampToDayOfYear( _timeStamp );
		}

		public int GetDayOfWeek()
		{
			return timeStampToWeekDay( _timeStamp );
		}
		
		public static long CalendarDateTotimeStamp( int year, int month, int day,
			int hour, int minute, int second, int millisecond, int microsecond )
		{
			// Normalize the data to allow for negative and out of range values
			// In this way, setting month to zero would be December of the previous year,
			// setting hour to 24 would be the first hour of the next day, etc.
			//double dsec = second + (double) millisecond / MillisecondsPerSecond;
			NormalizeCalendarDate( ref year, ref month, ref day, ref hour, ref minute, ref second, ref millisecond, ref microsecond);
		
			return _CalendarDateTotimeStamp( year, month, day, hour, minute, second, millisecond, microsecond);
		}
		
		public static long CalendarDateTotimeStamp( int year, int month, int day,
			int hour, int minute, int second )
		{
			// Normalize the data to allow for negative and out of range values
			// In this way, setting month to zero would be December of the previous year,
			// setting hour to 24 would be the first hour of the next day, etc.
			int ms = 0;
			int micros = 0;
			NormalizeCalendarDate( ref year, ref month, ref day, ref hour, ref minute,
					ref second, ref ms, ref micros );
		
			return _CalendarDateTotimeStamp( year, month, day, hour, minute, second, ms, micros );
		}
		
//		public static long CalendarDateTotimeStamp( int year, int month, int day,
//			int hour, int minute, int second )
//		{
//			// Normalize the data to allow for negative and out of range values
//			// In this way, setting month to zero would be December of the previous year,
//			// setting hour to 24 would be the first hour of the next day, etc.
//			int sec = (int)second;
//			int ms = ( second - sec ) * MillisecondsPerSecond;
//			NormalizeCalendarDate( ref year, ref month, ref day, ref hour, ref minute, ref sec,
//					ref ms );
//		
//			return _CalendarDateTotimeStamp( year, month, day, hour, minute, sec, ms );
//		}
		
		public static long CalendarDateToJulianDay( int year, int month, int day,
			int hour, int minute, int second )
		{
			// Normalize the data to allow for negative and out of range values
			// In this way, setting month to zero would be December of the previous year,
			// setting hour to 24 would be the first hour of the next day, etc.
			int ms = 0;
			int micros = 0;
			NormalizeCalendarDate( ref year, ref month, ref day, ref hour, ref minute,
				ref second, ref ms, ref micros );
		
			double value = _CalendarDateToJulianDay( year, month, day, hour, minute, second, ms, micros);
			return DoubleToLong(value);
		}
		
		public static double CalendarDateToJulianDay( int year, int month, int day,
			int hour, int minute, int second, int millisecond )
		{
			// Normalize the data to allow for negative and out of range values
			// In this way, setting month to zero would be December of the previous year,
			// setting hour to 24 would be the first hour of the next day, etc.
			int ms = millisecond;
			int micros = 0;
			NormalizeCalendarDate( ref year, ref month, ref day, ref hour, ref minute,
						ref second, ref ms, ref micros );
		
			return _CalendarDateToJulianDay( year, month, day, hour, minute, second, ms, micros );
		}

//		public void RoundTime()
//        {
//	    	long numberOfTicks = (long)( (_timeStamp * MillisecondsPerDay) + 0.5);
//            _timeStamp = numberOfTicks * MinimumTick;
//		}
	    
	    private static void NormalizeCalendarDate( ref int year, ref int month, ref int day,
											ref int hour, ref int minute, ref int second,
											ref int millisecond, ref int microsecond )
		{
			// Normalize the data to allow for negative and out of range values
			// In this way, setting month to zero would be December of the previous year,
			// setting hour to 24 would be the first hour of the next day, etc.

			// Normalize the milliseconds and carry over to seconds
			long carry = microsecond / MicrosecondsPerMillisecond;
			microsecond -= (int) (carry * MicrosecondsPerMillisecond);
			millisecond += (int) carry;
			
			// Normalize the milliseconds and carry over to seconds
			carry = millisecond / MillisecondsPerSecond;
			millisecond -= (int) (carry * MillisecondsPerSecond);
			second += (int) carry;

			// Normalize the seconds and carry over to minutes
			carry = second / SecondsPerMinute;
			second -= (int) (carry * SecondsPerMinute);
			minute += (int) carry;
		
			// Normalize the minutes and carry over to hours
			carry = minute / MinutesPerHour;
			minute -= (int) (carry * MinutesPerHour);
			hour += (int) carry;
		
			// Normalize the hours and carry over to days
			carry = hour / HoursPerDay;
			hour -= (int) (carry * HoursPerDay);
			day += (int) carry;
		
			// Normalize the months and carry over to years
			carry = month / MonthsPerYear;
			month -= (int) (carry * MonthsPerYear);
			year += (int) carry;
		}
		
		private static long _CalendarDateTotimeStamp( int year, int month, int day, int hour,
					int minute, int second, int millisecond, int microsecond)
		{
			var timeStamp = _CalendarDateToJulianDay( year, month, day, hour, minute,
	                                          second, millisecond, microsecond );
			var value = JulianDayTotimeStamp( timeStamp  );
			return value;
		}
		
		private static long _CalendarDateToJulianDay( int year, int month, int day, int hour,
					int minute, int second, int millisecond, int microsecond )
		{
			// Taken from http://www.srrb.noaa.gov/highlights/sunrise/program.txt
			// routine calcJD()
		
			if ( month <= 2 )
			{
				year -= 1;
				month += 12;
			}
			double A = Math.Floor( (double) year / 100.0 );
			double B = 2 - A + Math.Floor( A / 4.0 );
		
			double value = Math.Floor( 365.25 * ( (double) year + 4716.0 ) ) +
					Math.Floor( 30.6001 * (double) ( month + 1 ) ) +
					(double) day + B - 1524.5;
			var lvalue = (long) (value * MicrosecondsPerDay);
			var lfday = hour * MicrosecondsPerHour + minute * MicrosecondsPerMinute +
				second * MicrosecondsPerSecond + millisecond * MicrosecondsPerMillisecond +
				microsecond;
			var lresult = lvalue + lfday;
			return lresult;
		
		}

		public static void timeStampToCalendarDate( long timeStamp, out int year, out int month,
			out int day, out int hour, out int minute, out int second )
		{
			var jDay = timeStampToJulianDay( timeStamp );
			
			JulianDayToCalendarDate( jDay, out year, out month, out day, out hour,
				out minute, out second );
		}
		
		public static void timeStampToCalendarDate( long timeStamp, out int year, out int month,
			out int day, out int hour, out int minute, out int second, out int millisecond, out int microsecond )
		{
			var jDay = timeStampToJulianDay( timeStamp );
			JulianDayToCalendarDate( jDay, out year, out month, out day, out hour,
				out minute, out second, out millisecond, out microsecond );
		}
		
		public static void timeStampToCalendarDate( long timeStamp, out int year, out int month,
			out int day, out int hour, out int minute, out double second )
		{
			var jDay = timeStampToJulianDay( timeStamp );
			
			JulianDayToCalendarDate( jDay, out year, out month, out day, out hour,
				out minute, out second );
		}
		
		public static void JulianDayToCalendarDate( long jDay, out int year, out int month,
			out int day, out int hour, out int minute, out int second )
		{
			int ms = 0;
			int micros = 0;

			JulianDayToCalendarDate( jDay, out year, out month,
					out day, out hour, out minute, out second, out ms, out micros );
		}

		public static void JulianDayToCalendarDate( long jDay, out int year, out int month,
			out int day, out int hour, out int minute, out double second )
		{
			int sec;
			int ms;
			int micros;

			JulianDayToCalendarDate( jDay, out year, out month,
					out day, out hour, out minute, out sec, out ms, out micros );

			second = sec + ms / MicrosecondsPerSecond;
		}

		public static void JulianDayToCalendarDate( long timeStamp, out int year, out int month,
			out int day, out int hour, out int minute, out int second, out int millisecond, out int microsecond )
		{
			double jDay = timeStamp / (double) MicrosecondsPerDay;
			double z = Math.Floor( jDay + 0.5);
			double f = jDay + 0.5 - z;
			
			double alpha = Math.Floor( ( z - 1867216.25 ) / 36524.25 );
			double A = z + 1.0 + alpha - Math.Floor( alpha / 4 );
			double B = A + 1524.0;
			double C = Math.Floor( ( B - 122.1 ) / 365.25 );
			double D = Math.Floor( 365.25 * C );
			double E = Math.Floor( ( B - D ) / 30.6001 );
		
			day = (int) Math.Floor( B - D - Math.Floor( 30.6001 * E ) + f );
			month = (int) ( ( E < 14.0 ) ? E - 1.0 : E - 13.0 );
			year = (int) ( ( month > 2 ) ? C - 4716 : C - 4715 );
		
			var halfDay = MicrosecondsPerDay / 2;
			var lfday1 = timeStamp - halfDay;
			var lfday2 = (lfday1 / MicrosecondsPerDay) * MicrosecondsPerDay;
			var lfday = lfday1 - lfday2;
		
			hour = (int) (lfday / MicrosecondsPerHour);
			lfday -= hour * MicrosecondsPerHour;
			minute = (int) (lfday / MicrosecondsPerMinute);
			lfday -= minute * MicrosecondsPerMinute;
			second = (int) (lfday / MicrosecondsPerSecond);
			lfday -= second * MicrosecondsPerSecond;
			millisecond = (int) (lfday / MicrosecondsPerMillisecond);
			lfday -= millisecond * MicrosecondsPerMillisecond;
			microsecond = (int) lfday;
		}
		
		public static long timeStampToJulianDay( long timeStamp )
		{
			return timeStamp + lXLDay1;
		}
		
		public static long JulianDayTotimeStamp( long jDay )
		{
			return jDay - lXLDay1;
		}
		
		public static double timeStampToDecimalYear( long timeStamp )
		{
			int year, month, day, hour, minute, second;
			
			timeStampToCalendarDate( timeStamp, out year, out month, out day, out hour, out minute, out second );
			
			double jDay1 = CalendarDateToJulianDay( year, 1, 1, 0, 0, 0 );
			double jDay2 = CalendarDateToJulianDay( year + 1, 1, 1, 0, 0, 0 );
			double jDayMid = CalendarDateToJulianDay( year, month, day, hour, minute, second );
			
			
			return (double) year + ( jDayMid - jDay1 ) / ( jDay2 - jDay1 );
		}
		
		public static long DecimalYearTotimeStamp( double yearDec )
		{
			int year = (int) yearDec;
			
			long jDay1 = CalendarDateToJulianDay( year, 1, 1, 0, 0, 0 );
			long jDay2 = CalendarDateToJulianDay( year + 1, 1, 1, 0, 0, 0 );
			
			long jDay = (long) (( yearDec - (double) year ) * ( jDay2 - jDay1 ) + jDay1);
			double value = JulianDayTotimeStamp( jDay );
			return DoubleToLong(value);
		}
		
		public static double timeStampToDayOfYear( long timeStamp )
		{
			int year, month, day, hour, minute, second;
			timeStampToCalendarDate( timeStamp, out year, out month, out day,
									out hour, out minute, out second );
			var longDayOfYear = timeStampToJulianDay( timeStamp ) - CalendarDateToJulianDay( year, 1, 1, 0, 0, 0 );
			return longDayOfYear / (double) MicrosecondsPerDay + 1.0;
		}
		
		public static int timeStampToWeekDay( long timeStamp )
		{
			var jDay = timeStampToJulianDay( timeStamp ) / (double) MicrosecondsPerDay;
			return (int) ( jDay + 1.5 ) % 7;
		}
		
		public static DateTime timeStampToDateTime( long timeStamp )
		{
			int year, month, day, hour, minute, second, millisecond, microsecond;
			timeStampToCalendarDate( timeStamp, out year, out month, out day,
									out hour, out minute, out second, out millisecond, out microsecond );
			return new DateTime( year, month, day, hour, minute, second, millisecond );
		}
		
		public static long DateTimeToTimeStamp( DateTime dt )
		{
			return CalendarDateTotimeStamp( dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second,
										dt.Millisecond, 0 );
		}

		public void Sync() {
			int year, month, day, hour, minute, second, millisecond, microsecond;
 		    GetDate(out year,out month,out day,out hour,out minute,out second,out millisecond,out microsecond);
			
			Assign(year,month,day,hour,minute,second,millisecond);
		}
		
		public void AddMilliseconds( long dMilliseconds )
		{
			_timeStamp += dMilliseconds * MicrosecondsPerMillisecond;
		}

		public void AddMicroseconds( long dMicroseconds )
		{
			_timeStamp += dMicroseconds;
		}

		public void AddSeconds( long dSeconds )
		{
			_timeStamp += dSeconds * MicrosecondsPerSecond;
		}

		public void AddMinutes( long dMinutes )
		{
			_timeStamp += dMinutes * MicrosecondsPerMinute;
		}
		
		public void AddHours( long dHours )
		{
			_timeStamp += dHours * MicrosecondsPerHour;
		}
		
		public void AddDays( long dDays )
		{
			_timeStamp += dDays * MicrosecondsPerDay;
		}
		
		public void AddMonths( int dMonths )
		{
			int iMon = (int) dMonths;
			double monFrac = Math.Abs( dMonths - (double) iMon );
			int sMon = Math.Sign( dMonths );
			
			int year, month, day, hour, minute, second;
			
			timeStampToCalendarDate( _timeStamp, out year, out month, out day, out hour, out minute, out second );
			if ( iMon != 0 )
			{
				month += iMon;
				_timeStamp = CalendarDateTotimeStamp( year, month, day, hour, minute, second );
			}
			
			if ( sMon != 0 )
			{
				long timeStamp2 = CalendarDateTotimeStamp( year, month+sMon, day, hour, minute, second );
				_timeStamp += (long) ((timeStamp2 - _timeStamp) * monFrac);
			}
		}
		
		public void AddYears( double dYears )
		{
			int iYear = (int) dYears;
			double yearFrac = Math.Abs( dYears - (double) iYear );
			int sYear = Math.Sign( dYears );
			
			int year, month, day, hour, minute, second;
			
			timeStampToCalendarDate( _timeStamp, out year, out month, out day, out hour, out minute, out second );
			if ( iYear != 0 )
			{
				year += iYear;
				_timeStamp = CalendarDateTotimeStamp( year, month, day, hour, minute, second );
			}
			
			if ( sYear != 0 )
			{
				long timeStamp2 = CalendarDateTotimeStamp( year+sYear, month, day, hour, minute, second );
				_timeStamp += (long) ((timeStamp2 - _timeStamp) * yearFrac);
			}
		}

		public static Elapsed operator -( TimeStamp lhs, TimeStamp rhs )
		{
			return new Elapsed( lhs._timeStamp - rhs._timeStamp);
		}
		
		public static TimeStamp operator -( TimeStamp lhs, Elapsed rhs )
		{
			lhs._timeStamp -= rhs.Internal;
			return lhs;
		}
		
		public static TimeStamp operator +( TimeStamp lhs, Elapsed rhs )
		{
			lhs._timeStamp += rhs.Internal;
			return lhs;
		}
		
//		public static explicit operator TimeStamp( long timeStamp)
//		{
//			return new TimeStamp(timeStamp);
//		}
		
//		public static explicit operator long( TimeStamp TimeStamp )
//		{
//			return TimeStamp._timeStamp;
//		}
//		
//		public static explicit operator double( TimeStamp TimeStamp )
//		{
//			return (double)TimeStamp._timeStamp / MillisecondsPerDay;
//		}
		
		public static explicit operator DateTime( TimeStamp TimeStamp )
		{
			
			return timeStampToDateTime( TimeStamp.Internal);
		}
		
		public static explicit operator TimeStamp( DateTime dt )
		{
			
			return new TimeStamp( DateTimeToTimeStamp( dt ) / (double) MicrosecondsPerDay );
		}
		
		public static bool operator >=( TimeStamp lhs, TimeStamp rhs)
		{
			return lhs.CompareTo(rhs) >= 0;
		}
		
		public static bool operator ==( TimeStamp lhs, TimeStamp rhs)
		{
			return lhs.CompareTo(rhs) == 0;
		}
		
		public static bool operator !=( TimeStamp lhs, TimeStamp rhs)
		{
			return lhs.CompareTo(rhs) != 0;
		}
		
		public static bool operator <=( TimeStamp lhs, TimeStamp rhs)
		{
			return lhs.CompareTo(rhs) <= 0;
		}
		
		public static bool operator >( TimeStamp lhs, TimeStamp rhs)
		{
			return lhs.CompareTo(rhs) > 0;
		}
		
		public static bool operator <( TimeStamp lhs, TimeStamp rhs)
		{
			return lhs.CompareTo(rhs) < 0;
		}
		
		public override bool Equals( object obj )
		{
			if ( obj is TimeStamp )
			{
				return CompareTo((TimeStamp) obj)==0;
			}
			else if ( obj is long )
			{
				return ((long) obj) == _timeStamp;
			}
			else
				return false;
		}
		
		public override int GetHashCode()
		{
			return _timeStamp.GetHashCode();
		}

		public int CompareTo( TimeStamp target )
		{
			long value = _timeStamp - target._timeStamp;
			return value == 0 ? 0 : value > 0 ? 1 : -1;
		}

		public string ToString( long timeStamp )
		{
			return ToString( timeStamp, DefaultFormatStr );
		}
		
		public override string ToString()
		{
			return ToString( _timeStamp, DefaultFormatStr );
		}
		
		public string ToString( string fmtStr )
		{
			return ToString( this._timeStamp, fmtStr );
		}
		
		public void Add( Elapsed elapsed) {
			_timeStamp += elapsed.Internal;
		}

		public static string ToString( long timeStamp, string _fmtStr )
		{
			int	year, month, day, hour, minute, second, millisecond, microsecond;

			StringBuilder fmtStr = new StringBuilder(_fmtStr);
			if ( !CheckValidDate( timeStamp ) )
				return "Date Error";

			timeStampToCalendarDate( timeStamp, out year, out month, out day, out hour, out minute,
											out second, out millisecond, out microsecond );
			fmtStr.Replace( "yyyy", year.ToString("d4") );
			fmtStr.Replace( "MM", month.ToString("d2") );
			fmtStr.Replace( "dd", day.ToString("d2") );
			if ( year <= 0 )
			{
				year = 1 - year;
				fmtStr.Append(" (BC)");
			}

			fmtStr.Replace( "HH", hour.ToString("d2") );
			fmtStr.Replace( "mm", minute.ToString("d2") );
			fmtStr.Replace( "ss", second.ToString("d2") );
			fmtStr.Replace( "fff", ((int)millisecond).ToString("d3") );
			fmtStr.Replace( "uuu", ((int)microsecond).ToString("d3") );
			
//			if ( _fmtStr.IndexOf("d") >= 0 )
//			{
//				fmtStr = fmtStr.Replace( "dd", ((int) timeStamp).ToString("d2") );
//				fmtStr = fmtStr.Replace( "d", ((int) timeStamp).ToString("d") );
//				timeStamp -= (int) timeStamp;
//			}
//			if ( _fmtStr.IndexOf("h") >= 0 )
//			{
//				fmtStr = fmtStr.Replace( "hh", ((int) (timeStamp * 24)).ToString("d2") );
//				fmtStr = fmtStr.Replace( "h", ((int) (timeStamp * 24)).ToString("d") );
//				timeStamp = ( timeStamp * 24 - (int) (timeStamp * 24) ) / 24.0;
//			}
//			if ( _fmtStr.IndexOf("m") >= 0 )
//			{
//				fmtStr = fmtStr.Replace( "mm", ((int) (timeStamp * 1440)).ToString("d2") );
//				fmtStr = fmtStr.Replace( "m", ((int) (timeStamp * 1440)).ToString("d") );
//				timeStamp = ( timeStamp * 1440 - (int) (timeStamp * 1440) ) / 1440.0;
//			}
//			if ( _fmtStr.IndexOf("s") >= 0 )
//			{
//				fmtStr = fmtStr.Replace( "ss", ((int) (timeStamp * 86400)).ToString("d2") );
//				fmtStr = fmtStr.Replace( "s", ((int) (timeStamp * 86400)).ToString("d") );
//				timeStamp = ( timeStamp * 86400 - (int) (timeStamp * 86400) ) / 86400.0;
//			}
//			if ( _fmtStr.IndexOf("f") >= 0 ) {
//				fmtStr = fmtStr.Replace( "fffff", ((int) (timeStamp * 8640000000)).ToString("d") );
//				fmtStr = fmtStr.Replace( "ffff", ((int) (timeStamp * 864000000)).ToString("d") );
//				fmtStr = fmtStr.Replace( "fff", ((int) (timeStamp * 86400000)).ToString("d") );
//				fmtStr = fmtStr.Replace( "ff", ((int) (timeStamp * 8640000)).ToString("d") );
//				fmtStr = fmtStr.Replace( "f", ((int) (timeStamp * 864000)).ToString("d") );
//			}

			return fmtStr.ToString();
		}
	}
}
