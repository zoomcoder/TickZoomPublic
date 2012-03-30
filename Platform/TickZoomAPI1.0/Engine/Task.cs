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
	[Flags]
	public enum Scheduler {
		None = 0,
		RoundRobin = 1,
		EarliestTime = 2,
	}

    public enum QueueDirection
    {
        Inbound,
        Outbound
    }

    public interface QueueFilter
    {
        bool Receive(out EventItem eventItem);
        void IncludeTypes(params EventType[] includes);
        void ExcludeTypes(params EventType[] excludes);
        bool CanReceive { get; }
        void Pop();
    }

    public interface Task
    {
		void Start();
		void Stop();
		void Join();
		void Pause();
		void Resume();
        void IncreaseInbound(int id, long earliestUtcTime);
        void DecreaseInbound(int id, long earliestUtcTime);
		void ConnectInbound(Queue queue, out int inboundId);
		bool HasActivity {
			get;
		}
		bool IsAlive {
			get;
		}
		bool IsActive {
			get;
		}
		bool IsLogging {
			get;
			set;
		}
		object Tag {
			get;
			set;
		}
		Scheduler Scheduler {
			get;
			set;
		}

        bool IsPaused { get; }
        string Name { get; set; }
        QueueFilter Filter { get; }
        bool IsStopped { get; }

        void IncreaseOutbound(int id);
        void DecreaseOutbound(int id);
	    unsafe void ConnectOutbound(Queue queue, out int outboundId);
        QueueFilter GetFilter();
    }
}
