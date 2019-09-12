using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using Ruya.Diagnostics;

namespace Ruya.Data.Entity
{
    [Serializable]
    public class DataEntityException : DiagnosticsException
    {
        public DataEntityException()
        {
        }

        public DataEntityException(string message) : base(message)
        {
        }

        public DataEntityException(string message, Exception inner) : base(message, inner)
        {
        }

        protected DataEntityException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public DataEntityException(TraceEventType traceEventType, string message) : base(traceEventType, message)
        {
        }
    }
}