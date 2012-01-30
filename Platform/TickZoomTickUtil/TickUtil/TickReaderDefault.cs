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
using TickZoom.Api;

//using TickZoom.Api;

namespace TickZoom.TickUtil
{

	/// <summary>
	/// Description of TickArray.
	/// </summary>
	public class TickReaderDefault : Reader, TickReader
	{
		static readonly Log log = Factory.SysLog.GetLogger(typeof(TickReader));
		readonly bool debug = log.IsDebugEnabled;
		double startDouble = double.MinValue;
		double endDouble = double.MaxValue;
	    private Agent agent;
        public Agent Agent
        {
            get { return agent; }
            set { agent = value; }
        }

		public TickQueue ReadQueue {
			get {
                throw new NotImplementedException();
			}
		}

        public Agent GetReceiver()
        {
            throw new NotImplementedException();
        }


	    public void StartSymbol(EventItem eventItem)
		{
			var detail = (StartSymbolDetail)eventItem.EventDetail;

			if (!eventItem.Symbol.Equals(Symbol)) {
				throw new ApplicationException("Mismatching symbol.");
			}
			if (detail.LastTime != StartTime) {
				throw new ApplicationException("Mismatching start time. Expected: " + StartTime + " but was " + detail.LastTime);
			}
		}

        public void Shutdown()
        {
            Dispose();
        }

        public override Yield Invoke()
		{
		    EventItem eventItem;
		    if (fileReaderTask.Filter.Receive(out eventItem))
		    {
		        switch (eventItem.EventType)
		        {
                    case EventType.StartSymbol:
                        StartSymbol(eventItem);
                        fileReaderTask.Filter.Pop();
		                break;
                    case EventType.Connect:
                        Start(eventItem);
                        fileReaderTask.Filter.Pop();
                        break;
                    case EventType.Disconnect:
                        Stop(eventItem);
                        fileReaderTask.Filter.Pop();
                        break;
                    case EventType.StopSymbol:
                        StopSymbol(eventItem);
                        fileReaderTask.Filter.Pop();
                        break;
                    case EventType.PositionChange:
                        PositionChange(eventItem);
                        fileReaderTask.Filter.Pop();
                        break;
                    case EventType.RemoteShutdown:
                    case EventType.Terminate:
                        Dispose();
                        fileReaderTask.Filter.Pop();
                        break;
                    default:
            		    return base.Invoke();
		        }
		    }
            else
		    {
		        return base.Invoke();
		    }
		    return Yield.NoWork.Repeat;
		}

	    public void StopSymbol(EventItem eventItem)
		{

		}

		public void PositionChange(EventItem eventItem)
		{
			throw new NotImplementedException();
		}

        public bool SendEvent(EventItem eventItem)
        {
            var result = true;
            var receiver = eventItem.Agent;
            var symbol = eventItem.Symbol;
            var eventType = eventItem.EventType;
            var eventDetail = eventItem.EventDetail;
            switch ((EventType)eventType)
            {
				default:
					throw new ApplicationException("Unexpected event type: " + (EventType)eventType);
            }
            return result;
        }
	}
}
