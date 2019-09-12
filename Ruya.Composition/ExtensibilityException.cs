using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using Ruya.Diagnostics;

namespace Ruya.Composition
{
    [Serializable]
    public class CompositionException : DiagnosticsException
    {
        public CompositionException()
        {
        }

        public CompositionException(string message) : base(message)
        {
        }

        public CompositionException(string message, Exception inner) : base(message, inner)
        {
        }

        protected CompositionException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public CompositionException(TraceEventType traceEventType, string message) : base(traceEventType, message)
        {
        }
    }
}