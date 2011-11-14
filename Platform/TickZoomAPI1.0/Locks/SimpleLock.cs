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
using System.Diagnostics;
using System.Threading;

namespace TickZoom.Api
{
	public class SimpleLock : IDisposable
	{
	    private static readonly Log log = Factory.Log.GetLogger(typeof (SimpleLock));
	    private int isLocked = 0;
	    
		public bool IsLocked {
			get { return isLocked != 0; }
		}
	    
		public bool TryLock()
		{
		    var thread = Thread.CurrentThread;
	    	var result = Interlocked.CompareExchange(ref isLocked,thread.ManagedThreadId,0) == 0;
		    return result;
		}
	    
		public void Lock()
		{
            if( TryLock()) return;
		    var logCount = 0;
		    var count = 0;
			while( !TryLock())
			{
			    ++count;
                if( count > 1000000)
                {
                    var thread = Thread.CurrentThread;
                    if( thread.ManagedThreadId == isLocked)
                    {
                        throw new ApplicationException("Looping lock on current thread.");
                    }
                    else
                    {
                        ++logCount;
                        if( logCount > 5)
                        {
                            throw new ApplicationException("Deadlock or thread sleeping during lock: " + Environment.StackTrace);
                        }
                        else
                        {
                            log.Error("Deadlock or thread sleeping during lock: " + Environment.StackTrace);
                            Thread.Sleep(1);
                            count = 0;
                        }
                    }
                }
			}
	    }
	    
	    public SimpleLock Using() {
	    	Lock();
	    	return this;
	    }
	    
	    public void Unlock() {
	    	Interlocked.Exchange(ref isLocked, 0);
	    }
		
		public void Dispose()
		{
			Unlock();
		}
		
	}
}
