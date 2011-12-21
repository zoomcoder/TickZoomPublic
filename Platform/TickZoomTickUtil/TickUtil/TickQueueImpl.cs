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
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using TickZoom.Api;

namespace TickZoom.TickUtil
{
	public class TickQueueImpl : FastQueueImpl<QueueItem>, TickQueue
	{
	    public TickQueueImpl(string name) : base(name) {
	    	
	    }
	    
	    public TickQueueImpl(string name, int size) : base(name, size) {
	    	
	    }
		
	    public void Enqueue(ref TickBinary o)
	    {
            while( !TryEnqueue(ref o)) {
                if( IsFull)
                {
                    throw new ApplicationException("Enqueue failed for " + Name + " with " + Count + " items.");
                }
	    	}
	    }
	    
	    public bool TryEnqueue(ref TickBinary o)
	    {
        	QueueItem item = new QueueItem();
        	item.Symbol = o.Symbol;
        	item.EventType = (int) EventType.Tick;
    		item.Tick = o;
    		return TryEnqueue(item, item.Tick.UtcTime);
	    }
	    
	    public void Enqueue(EventType entryType, SymbolInfo symbol)
	    {
	    	if( !TryEnqueue(entryType, symbol)) {
	    		throw new ApplicationException("Enqueue failed.");
	    	}
	    }
	    
	    public bool TryEnqueue(EventType entryType, SymbolInfo symbol)
	    {
        	QueueItem item = new QueueItem();
	    	item.EventType = (int) entryType;
	    	if( symbol != null) {
	    		item.Symbol = symbol.BinaryIdentifier;
	    	}
	    	return TryEnqueue(item, TimeStamp.UtcNow.Internal);
	    }
	    
	    public void Enqueue(EventType entryType, string error)
	    {
	    	if( !TryEnqueue(entryType, error)) {
	    		throw new ApplicationException("queue is full");
	    	}
	    }
	    
	    public bool TryEnqueue(EventType entryType, string error)
	    {
        	var item = new QueueItem();
	    	item.EventType = (int) entryType;
	    	return TryEnqueue(item,TimeStamp.UtcNow.Internal);
	    }
	    
	    public void Dequeue(ref TickBinary tick)
	    {
	    	while( !TryDequeue(ref tick)) {
	    		Thread.Sleep(1);
	    	}
	    }
	    
	    public bool TryDequeue(ref TickBinary tick)
	    {
        	QueueItem item = new QueueItem();
	    	bool result = TryDequeue(out item);
	    	if( result) {
	    		if( item.EventType != (int) EventType.Tick) {
		    		string symbol;
		    		if( item.Symbol != 0) {
		    			symbol = item.Symbol.ToSymbol();
		    		} else {
		    			symbol = "";
		    		}
		    		throw new QueueException( (EventType) item.EventType, symbol);
		    	} else {
	    			tick = item.Tick;
	    		}
		    }
	    	return result;
	    }

        public void Peek(ref TickBinary tick)
        {
            if (!TryPeek(ref tick))
            {
                throw new ApplicationException("Queue is empy");
            }
        }

	    public bool TryPeek(ref TickBinary tick)
        {
            var item = new QueueItem();
            bool result = TryPeek(out item);
            if (result)
            {
                if (item.EventType != (int)EventType.Tick)
                {
                    string symbol;
                    if (item.Symbol != 0)
                    {
                        symbol = item.Symbol.ToSymbol();
                    }
                    else
                    {
                        symbol = "";
                    }
                    throw new QueueException((EventType)item.EventType, symbol);
                }
                else
                {
                    tick = item.Tick;
                }
            }
            return result;
        }
    }
	
}
