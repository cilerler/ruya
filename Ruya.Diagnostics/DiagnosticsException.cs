using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;

namespace Ruya.Diagnostics
{
    [Serializable]
    public class DiagnosticsException : Exception
    {
        public DiagnosticsException()
        {
        }

        public DiagnosticsException(string message) : base(message)
        {
            Tracer.Instance.TraceEvent(TraceEventType.Error, 0, message);
        }

        public DiagnosticsException(string message, Exception inner) : base(message, inner)
        {
            if (inner == null)
            {
                throw new ArgumentNullException(nameof(inner));
            }
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(message);
            stringBuilder.AppendLine(inner.Message);
            Tracer.Instance.TraceEvent(TraceEventType.Error, 0, stringBuilder.ToString());
        }

        protected DiagnosticsException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        public DiagnosticsException(TraceEventType traceEventType, string message) : base(message)
        {
            Tracer.Instance.TraceEvent(traceEventType, 0, message);
        }
    }
}