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

namespace TickZoom.Api
{
	[CLSCompliant(false)]
	public interface FastEventQueue : FastQueue<QueueItem> {
		
	}
	
	public interface FastFillQueue : FastQueue<LogicalFillBinary> {
		
	}
	
	public interface Queue : IDisposable
	{
		string Name { get; }
		string GetStats();
		int Count { get; }
	    void SetException(Exception ex);
		bool IsStarted { get; }
        int Capacity { get; set;  }
	    bool IsFull { get; }
        bool IsEmpty { get; }
        void ConnectInbound(Task task);
	    void ConnectOutbound(Task task);
        bool DisableRelease { get; set; }
    }

    public interface ReceiveQueue<T> : Queue
    {
		void Enqueue(T item, long utcTime);
        bool TryEnqueue(T item, long utcTime);
    }

	public interface FastQueue<T> : ReceiveQueue<T>
	{
        StartEnqueue StartEnqueue { get; set; }
	    void Clear();
        void Dequeue(out T tick);
	    bool TryDequeue(out T item);
		void Peek(out T tick);
	    bool TryPeek( out T tick);
	}

    public interface ReceiveEventQueue : ReceiveQueue<EventItem>
    {
        SymbolInfo Symbol { get; }
    }

    public interface EventQueue : FastQueue<EventItem>, ReceiveEventQueue
    {
    }

}


