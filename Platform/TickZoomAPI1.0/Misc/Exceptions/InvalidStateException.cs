using System;

namespace TickZoom.Api
{
    /// <summary>
    /// Description of Class1.
    /// </summary>
    [Serializable]
    public class InvalidStateException : System.ApplicationException
    {
        public InvalidStateException() { }
        public InvalidStateException(string message) : base(message) { }
        public InvalidStateException(string message, System.Exception inner) : base(message, inner) { }

        // Constructor needed for serialization 
        // when exception propagates from a remoting server to the client.
        protected InvalidStateException(System.Runtime.Serialization.SerializationInfo info,
                                        System.Runtime.Serialization.StreamingContext context) { }

    }
}