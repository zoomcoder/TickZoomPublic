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
	    private static readonly bool debug = log.IsDebugEnabled;
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
            if( isLocked == Thread.CurrentThread.ManagedThreadId)
            {
                throw new ApplicationException("Already locked by this same thread.");
            }
		    var count = 0L;
		    var loggedFlag = false;
			while( !TryLock())
			{
                if (count > 1000000)
                {
                    if (!loggedFlag)
                    {
                        try
                        {
                            throw new ApplicationException("Either deadlocked thread or else sleeping thread while locked.");
                        }
                        catch( Exception ex)
                        {
                            if (debug) log.Warn(ex.Message, ex);
                        }
                        loggedFlag = true;
                    }
                    Thread.Sleep(1);
                }
                else
                {
                    ++count;
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
