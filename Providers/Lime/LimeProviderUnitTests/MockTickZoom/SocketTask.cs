using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using TickZoom.Api;

namespace LimeProviderUnitTests.MockTickZoom
{
    class SocketTask : Task
    {
        #region Task Members

        public void Start()
        {
            throw new NotImplementedException();
        }

        public void Stop()
        {
            throw new NotImplementedException();
        }

        public void Join()
        {
            throw new NotImplementedException();
        }

        public void Pause()
        {
            throw new NotImplementedException();
        }

        public void Resume()
        {
            throw new NotImplementedException();
        }

        public void IncreaseInbound(int id, long earliestUtcTime)
        {
            throw new NotImplementedException();
        }

        public void DecreaseInbound(int id, long earliestUtcTime)
        {
            throw new NotImplementedException();
        }

        public void ConnectInbound(Queue queue, out int inboundId)
        {
            throw new NotImplementedException();
        }

        public bool HasActivity
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsAlive
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsActive
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsLogging
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public object Tag
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public Action<Exception> OnException
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public Scheduler Scheduler { get; set; }

        public bool IsPaused
        {
            get { throw new NotImplementedException(); }
        }

        public string Name
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public QueueFilter Filter
        {
            get { throw new NotImplementedException(); }
        }

        public bool IsStopped
        {
            get { throw new NotImplementedException(); }
        }

        public void IncreaseOutbound(int id)
        {
            throw new NotImplementedException();
        }

        public void DecreaseOutbound(int id)
        {
            throw new NotImplementedException();
        }

        public unsafe void ConnectOutbound(Queue queue, out int outboundId)
        {
            throw new NotImplementedException();
        }

        public QueueFilter GetFilter()
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
