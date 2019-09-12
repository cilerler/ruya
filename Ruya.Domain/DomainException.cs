using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using Ruya.Diagnostics;

namespace Ruya.Domain
{
    [Serializable]
    public sealed class DomainException : DiagnosticsException
    {
        public DomainException()
        {
        }

        public DomainException(string message) : base(message)
        {
        }

        public DomainException(string message, Exception inner) : base(message, inner)
        {
        }

        private DomainException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public DomainException(TraceEventType traceEventType, string message) : base(traceEventType, message)
        {
        }
    }
}
