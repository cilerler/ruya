using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using Ruya.Diagnostics;

namespace Ruya.Net
{
    [Serializable]
    public sealed class NetException : DiagnosticsException
    {
        public NetException()
        {
        }

        public NetException(string message) : base(message)
        {
        }

        public NetException(string message, Exception inner) : base(message, inner)
        {
        }

        private NetException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public NetException(TraceEventType traceEventType, string message) : base(traceEventType, message)
        {
        }
    }
}