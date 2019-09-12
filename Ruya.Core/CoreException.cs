using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;
using Ruya.Core.Properties;

namespace Ruya.Core
{
    // TEST class CoreException //! implemented in CONSOLE
    /// <summary>
    /// Base exception class for the library    
    /// </summary>
    [Serializable]
    public sealed class CoreException : Exception
    {
        public CoreException()
        {
#if DEBUG
            Debug.WriteLine(TraceEventType.Critical, Resources.CoreException_Message);
#endif
#if TRACE
            Trace.WriteLine(TraceEventType.Critical, Resources.CoreException_Message);
#endif
        }

        public CoreException(string message) : base(message)
        {
#if DEBUG
            Debug.WriteLine(TraceEventType.Critical, message);
#endif
#if TRACE
            Trace.WriteLine(TraceEventType.Critical, message);
#endif
        }

        public CoreException(string message, Exception inner) : base(message, inner)
        {
            if (inner == null)
            {
                throw new ArgumentNullException(nameof(inner));
            }
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(message);
            stringBuilder.AppendLine(inner.Message);
            string output = stringBuilder.ToString();
#if DEBUG
            Debug.WriteLine(TraceEventType.Critical, output);
#endif
#if TRACE
            Trace.WriteLine(TraceEventType.Critical, output);
#endif
        }

        private CoreException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
