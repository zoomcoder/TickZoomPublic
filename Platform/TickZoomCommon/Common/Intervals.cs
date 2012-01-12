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
using TickZoom.Api;

namespace TickZoom.Common
{
	/// <summary>
	/// Description of Intervals.
	/// </summary>
    public class Intervals
    {
        public readonly static Interval Default = new IntervalImpl(BarUnit.Default, 0);
        public readonly static Interval Tick1 = new IntervalImpl(BarUnit.Tick, 1);
        public readonly static Interval Tick10 = new IntervalImpl(BarUnit.Tick, 10);
        public readonly static Interval Tick20 = new IntervalImpl(BarUnit.Tick, 20);
        public readonly static Interval Tick50 = new IntervalImpl(BarUnit.Tick, 50);
        public readonly static Interval Tick100 = new IntervalImpl(BarUnit.Tick, 100);
        public readonly static Interval Tick200 = new IntervalImpl(BarUnit.Tick, 200);
        public readonly static Interval Week1 = new IntervalImpl(BarUnit.Week, 1);
        public readonly static Interval Session1 = new IntervalImpl(BarUnit.Session, 1);
        public readonly static Interval Year1 = new IntervalImpl(BarUnit.Year, 1);
        public readonly static Interval Month1 = new IntervalImpl(BarUnit.Month, 1);
        public readonly static Interval Quarter1 = new IntervalImpl(BarUnit.Month, 3);
        public readonly static Interval Second1 = new IntervalImpl(BarUnit.Second, 1);
        public readonly static Interval Second10 = new IntervalImpl(BarUnit.Second, 10);
        public readonly static Interval Second30 = new IntervalImpl(BarUnit.Second, 30);
        public readonly static Interval Second60 = new IntervalImpl(BarUnit.Second, 60);
        public readonly static Interval Second75 = new IntervalImpl(BarUnit.Second, 75);
        public readonly static Interval Second150 = new IntervalImpl(BarUnit.Second, 150);
        public readonly static Interval Minute1 = new IntervalImpl(BarUnit.Minute, 1);
        public readonly static Interval Minute2 = new IntervalImpl(BarUnit.Minute, 2);
        public readonly static Interval Minute5 = new IntervalImpl(BarUnit.Minute, 5);
        public readonly static Interval Minute10 = new IntervalImpl(BarUnit.Minute, 10);
        public readonly static Interval Minute30 = new IntervalImpl(BarUnit.Minute, 30);
        public readonly static Interval Day1 = new IntervalImpl(BarUnit.Day, 1);
        public readonly static Interval Hour1 = new IntervalImpl(BarUnit.Hour, 1);
        public readonly static Interval Hour4 = new IntervalImpl(BarUnit.Hour, 4);
        public readonly static Interval Range40 = new IntervalImpl(BarUnit.Range, 40);
        public readonly static Interval Range30 = new IntervalImpl(BarUnit.Range, 30);
        public readonly static Interval Range20 = new IntervalImpl(BarUnit.Range, 20);
        public readonly static Interval Range10 = new IntervalImpl(BarUnit.Range, 10);
        public readonly static Interval Range5 = new IntervalImpl(BarUnit.Range, 5);
        public static Interval Define(BarUnit unit, int period)
        {
            return new IntervalImpl(unit, period);
        }
    }
}
