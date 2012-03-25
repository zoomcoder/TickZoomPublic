using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TickZoom.LimeFIX
{
    public class LimeException : Exception
    {
        public LimeException()
        {
        }

        public LimeException(string message)
            : base(message)
        {
        }

        public LimeException(string message, Exception innnerException)
            : base(message, innnerException)
        {
        }
    }
}
