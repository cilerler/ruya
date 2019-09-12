using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using Ruya.Diagnostics;

namespace Ruya.Xml
{
    [Serializable]
    public class XmlException : DiagnosticsException
    {
        public XmlException()
        {
        }

        public XmlException(string message) : base(message)
        {
        }

        public XmlException(string message, Exception inner) : base(message, inner)
        {
        }

        protected XmlException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public XmlException(TraceEventType traceEventType, string message) : base(traceEventType, message)
        {
        }
    }
}