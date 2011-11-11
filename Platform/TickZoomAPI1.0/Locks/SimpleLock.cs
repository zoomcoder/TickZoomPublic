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
	    private Thread lockingThread;
	    
		public bool IsLocked {
			get { return isLocked == 1; }
		}
	    
		public bool TryLock() {
	    	var result = Interlocked.CompareExchange(ref isLocked,1,0) == 0;
            if( result)
            {
                lockingThread = Thread.CurrentThread;
            }
		    return result;
		}
	    
		public void Lock()
		{
		    var checkDeadlock = true;
		    var count = 0L;
			while( !TryLock())
			{
			    ++count;
                if( checkDeadlock && count > int.MaxValue)
                {
                    var stackTrace = new StackTrace(lockingThread, true);
                    lockingThread.Abort();
                    throw new ApplicationException("Deadlock. Lock held by thread named " + lockingThread.Name + " from:\n" + stackTrace);
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
