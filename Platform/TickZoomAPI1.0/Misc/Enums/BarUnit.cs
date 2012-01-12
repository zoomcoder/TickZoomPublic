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
using System.Globalization;
using System.Runtime.InteropServices.ComTypes;
using System.Diagnostics;

namespace TickZoom.Api
{
    /// <summary>
    /// Description of Struct1.
    /// </summary>
    [Diagram(AttributeExclude = true)]
    public class TimeFrame
    {
        BarUnit unit;
        int period;
        int seconds;

        private int CalcSeconds(BarUnit unit, int interval)
        {
            int seconds = 0;
            switch (unit)
            {
                case BarUnit.Minute:
                    seconds = interval * 60;
                    break;
                case BarUnit.Hour:
                    seconds = interval * 60 * 60;
                    break;
                case BarUnit.Day:
                    seconds = interval * 60 * 60 * 24;
                    break;
                case BarUnit.Month:
                    seconds = interval * 60 * 60 * 24 * 20;
                    break;
                case BarUnit.Second:
                    seconds = interval;
                    break;
                case BarUnit.Week:
                    seconds = interval * 60 * 60 * 24 * 5;
                    break;
                case BarUnit.Year:
                    seconds = interval * 60 * 60 * 24 * 20 * 12;
                    break;
                case BarUnit.Session:
                    seconds = int.MaxValue;
                    break;
                default:
                    seconds = 0;
                    break;
            }
            return seconds;
        }

        public TimeFrame(BarUnit unit, int interval)
        {
            this.unit = unit;
            this.period = interval;
            this.seconds = 0;
            this.seconds = CalcSeconds(unit, interval);
        }

        public override string ToString()
        {
            return period + " " + unit + " Bars";
        }

        public bool IsOrdinal
        {
            get
            {
                switch (unit)
                {
                    case BarUnit.Volume:
                    case BarUnit.Tick:
                    case BarUnit.Change:
                    case BarUnit.Range:
                        return true;
                    default:
                        return false;
                }
            }
        }

        #region Equals and GetHashCode implementation
        // The code in this region is useful if you want to use this structure in collections.
        // If you don't need it, you can just remove the region and the ": IEquatable<Struct1>" declaration.

        public override bool Equals(object obj)
        {
            if (obj is TimeFrame)
                return Equals((TimeFrame)obj); // use Equals method below
            else
                return false;
        }

        public  bool Equals(TimeFrame other)
        {
            // add comparisions for all members here
            return this.unit == other.unit && this.period == other.period;
        }

        public bool Greater(TimeFrame other)
        {
            // add comparisions for all members here
            return this.unit > other.unit || (this.unit == other.unit && this.period > other.period);
        }

        public bool Lesser(TimeFrame other)
        {
            // add comparisions for all members here
            return this.unit < other.unit || (this.unit == other.unit && this.period < other.period);
        }

        public override int GetHashCode()
        {
            // combine the hash codes of all members here (e.g. with XOR operator ^)
            return unit.GetHashCode() * 1000000 + (int)(period * 1000);
        }

        public static bool operator ==(TimeFrame lhs, TimeFrame rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator >(TimeFrame lhs, TimeFrame rhs)
        {
            return lhs.Greater(rhs);
        }

        public static bool operator <(TimeFrame lhs, TimeFrame rhs)
        {
            return lhs.Lesser(rhs);
        }

        public static bool operator !=(TimeFrame lhs, TimeFrame rhs)
        {
            return !(lhs.Equals(rhs)); // use operator == and negate result
        }

        public int Seconds1
        {
            get { return seconds; }
        }
        #endregion

        public BarUnit Unit
        {
            get { return unit; }
        }
        public int Period1
        {
            get { return period; }
        }
    }
    /// <summary>
    /// Description of Period.
    /// </summary>
    [Diagram(AttributeExclude = true)]
    public class IntervalImpl : Interval
    {
        TimeFrame timeFrame;
        TimeFrame secondaryTimeFrame;
        SecondaryType secondaryType;
        bool isTimeBased = false;

        public IntervalImpl(Interval interval)
            : this(interval.BarUnit, interval.Period)
        {

        }

        public IntervalImpl(BarUnit unit, int interval, BarUnit secondaryUnit, int secondaryInterval)
            : this(unit, interval)
        {
            this.secondaryTimeFrame = new TimeFrame(secondaryUnit, secondaryInterval);
            CalcSecondaryType();
        }

        [Diagram(AttributeExclude = true)]
        public IntervalImpl(BarUnit unit, int interval)
        {
            this.timeFrame = this.secondaryTimeFrame = new TimeFrame(unit, interval);
            this.secondaryType = SecondaryType.None;
            // Set up default secondary time frames.
            switch (unit)
            {
                case BarUnit.Day:
                case BarUnit.Hour:
                case BarUnit.Minute:
                case BarUnit.Month:
                case BarUnit.Second:
                case BarUnit.Week:
                case BarUnit.Year:
                    this.secondaryType = SecondaryType.None;
                    isTimeBased = true;
                    break;
                case BarUnit.Range:
                    this.secondaryTimeFrame = new TimeFrame(BarUnit.Day, 1);
                    CalcSecondaryType();
                    break;
                default:
                    this.secondaryType = SecondaryType.None;
                    break;
            }
        }

        private enum SecondaryType
        {
            Rolling,
            Reset,
            None
        }

        public bool HasSecondary
        {
            get { return timeFrame != secondaryTimeFrame; }
        }

        public bool IsRolling
        {
            get { return secondaryType == SecondaryType.Rolling; }
        }

        public bool HasReset
        {
            get { return secondaryType == SecondaryType.Reset; }
        }

        // Convert secondary timeframe to single time frame Bar Period.
        public Interval Secondary
        {
            get { return new IntervalImpl(secondaryTimeFrame.Unit, secondaryTimeFrame.Period1); }
        }

        private void CalcSecondaryType()
        {
            if (HasSecondary)
            {
                switch (timeFrame.Unit)
                {
                    case BarUnit.Change:
                    case BarUnit.Volume:
                    case BarUnit.Range:
                    case BarUnit.Tick:
                        secondaryType = SecondaryType.Reset;
                        break;
                    case BarUnit.Second:
                    case BarUnit.Minute:
                    case BarUnit.Hour:
                    case BarUnit.Day:
                    case BarUnit.Week:
                    case BarUnit.Month:
                    case BarUnit.Year:
                        secondaryType = SecondaryType.Rolling;
                        break;
                    default:
                        secondaryType = SecondaryType.None;
                        break;
                }
            }
            else
            {
                secondaryType = SecondaryType.None;
            }
        }

        public override string ToString()
        {
            if (timeFrame.Unit == BarUnit.Default)
            {
                return "Default";
            }
            if (timeFrame.Unit == BarUnit.Custom)
            {
                object obj = Get("Instance", (BarLogicInterface)null);
                if (obj != null)
                {
                    return obj.ToString();
                }
            }
            if (secondaryTimeFrame != timeFrame)
            {
                string secondaryDesc = "Secondary";
                if (IsRolling)
                {
                    secondaryDesc = "Rolling";
                }
                if (HasReset)
                {
                    secondaryDesc = "Reset";
                }
                return timeFrame + ": " + secondaryDesc + " " + secondaryTimeFrame;
            }
            else
            {
                return timeFrame.ToString();
            }
        }

        public bool IsOrdinal
        {
            get { return timeFrame.IsOrdinal; }
        }

        #region Equals and GetHashCode implementation
        // The code in this region is useful if you want to use this structure in collections.
        // If you don't need it, you can just remove the region and the ": IEquatable<Struct1>" declaration.

        public override bool Equals(object obj)
        {
            if (obj == null) return false;

            Interval interval = obj as Interval;
            if (!Object.Equals(interval, null))
                return Equals(interval); // use Equals method below
            else
                return false;
        }

        public bool Equals(Interval _other)
        {
            // add comparisions for all members here
            IntervalImpl other = _other as IntervalImpl;
            if (Object.Equals(other, null))
            {
                return this.BarUnit == _other.BarUnit &&
                    this.Period == _other.Period;
                //				&&
                //					this.Secondary.BarUnit == _other.Secondary.BarUnit &&
                //					this.Secondary.Period == _other.Secondary.Period;
            }
            else
            {
                return this.timeFrame == other.timeFrame && this.secondaryTimeFrame == other.secondaryTimeFrame;
            }
        }

        public bool Greater(Interval _other)
        {
            IntervalImpl other = _other as IntervalImpl;
            if (Object.Equals(other, null))
            {
                return this.BarUnit > _other.BarUnit ||
                    this.Period > _other.Period;
                //				||
                //					this.Secondary.BarUnit > _other.Secondary.BarUnit ||
                //					this.Secondary.Period  > _other.Secondary.Period;
            }
            else
            {
                return this.timeFrame > other.timeFrame || this.secondaryTimeFrame > other.secondaryTimeFrame;
            }
        }

        public bool Lesser(Interval _other)
        {
            IntervalImpl other = _other as IntervalImpl;
            if (Object.Equals(other, null))
            {
                return this.BarUnit < _other.BarUnit ||
                    this.Period < _other.Period;
                //				||
                //					this.Secondary.BarUnit < _other.Secondary.BarUnit ||
                //					this.Secondary.Period  < _other.Secondary.Period;
            }
            else
            {
                return this.timeFrame < other.timeFrame || this.secondaryTimeFrame < other.secondaryTimeFrame;
            }
        }

        public override int GetHashCode()
        {
            // combine the hash codes of all members here (e.g. with XOR operator ^)
            return timeFrame.GetHashCode() ^ timeFrame.GetHashCode();
        }

        public static bool operator ==(IntervalImpl lhs, IntervalImpl rhs)
        {
            if (Object.Equals(lhs, null))
            {
                return Object.Equals(rhs, null);
            }
            else if (Object.Equals(rhs, null))
            {
                return false;
            }
            return lhs.Equals(rhs);
        }

        public static bool operator ==(Interval lhs, IntervalImpl rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(Interval lhs, IntervalImpl rhs)
        {
            return !lhs.Equals(rhs);
        }

        public static bool operator >(IntervalImpl lhs, IntervalImpl rhs)
        {
            return lhs.Greater(rhs);
        }

        public static bool operator <(IntervalImpl lhs, IntervalImpl rhs)
        {
            return lhs.Lesser(rhs);
        }

        public static bool operator !=(IntervalImpl lhs, IntervalImpl rhs)
        {
            return !(lhs.Equals(rhs)); // use operator == and negate result
        }

        #endregion

        public int Seconds
        {
            get { return timeFrame.Seconds1; }
        }

        public int SecondarySeconds
        {
            get { return secondaryTimeFrame.Seconds1; }
        }

        public bool IsTimeBased
        {
            get
            {
                return isTimeBased;
            }
        }

        public BarUnit BarUnit
        {
            get { return timeFrame.Unit; }
        }
        public int Period
        {
            get { return timeFrame.Period1; }
        }

        private Dictionary<string, object> properties;
        public void Set<T>(string name, T value)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ApplicationException("Name argument was null.");
            }
            if (value == null)
            {
                throw new ApplicationException("Name argument was null.");
            }
            if (properties == null)
            {
                properties = new Dictionary<string, object>();
            }
            properties[name] = value;
        }

        public T Get<T>(string name, T value)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ApplicationException("Name argument was null.");
            }
            if (properties == null)
            {
                return value;
            }
            else
            {
                object result;
                if (properties.TryGetValue(name, out result))
                {
                    if (result is T)
                    {
                        return (T)result;
                    }
                    else
                    {
                        return value;
                    }
                }
                else
                {
                    return value;
                }
            }
        }
    }
	public enum BarUnit
	{
	   Default,
	   Volume,
	   /// <summary>
	   /// Constant range bars. These reset every day by default.
	   /// </summary>
	   Range,
	   Change,
	   Tick,
	   Second,
	   Minute,
	   Hour,
	   Day,
	   Session,
	   Week,
	   Month,
	   Year,
	   Custom,
	}
}
