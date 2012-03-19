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
using System.Threading;

namespace TickZoom.Api
{
	[CLSCompliant(false)]
	public class TickBinaryBox
	{
	    private int referenceCount;
	    private Pool<TickBinaryBox> pool;
		public TickBinary TickBinary;
	    private long id;
	    private int callerId;
	    private static long nextId;

        public TickBinaryBox( Pool<TickBinaryBox> pool)
        {
            this.pool = pool;
            referenceCount = 1;
            id = ++nextId;
        }

	    public long Id
	    {
	        get { return id; }
	    }

	    public int CallerId
	    {
	        get { return callerId; }
	    }

	    public void ResetReference(int callerId)
	    {
	        this.callerId = callerId;
            referenceCount = 1;
        }

        public void AddReference()
        {
            if( referenceCount < 1)
            {
                throw new ApplicationException("This item was already freed because reference count is " + referenceCount);
            }
            ++referenceCount;
        }

        public void Free()
        {
            if (referenceCount < 1)
            {
                throw new ApplicationException("This item was already freed because reference count is " + referenceCount);
            }
            var value = --referenceCount;
            if( value == 0)
            {
                pool.Free(this);
            }
        }

        public override string ToString()
        {
            return "Reference " + referenceCount + " " + TickBinary;
        }
	}
}
