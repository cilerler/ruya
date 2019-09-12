using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using Ruya.Diagnostics;

namespace Ruya.Configuration
{
    [Serializable]
    public class ConfigurationException : DiagnosticsException
    {
        public ConfigurationException()
        {
        }

        public ConfigurationException(string message) : base(message)
        {
        }

        public ConfigurationException(string message, Exception inner) : base(message, inner)
        {
        }

        protected ConfigurationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public ConfigurationException(TraceEventType traceEventType, string message) : base(traceEventType, message)
        {
        }
    }
}