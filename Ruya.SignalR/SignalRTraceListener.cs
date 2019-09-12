using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Security.Permissions;
using System.Text;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;

namespace Ruya.SignalR
{
    [HubName("traceHub")]
    public class TraceHub : Hub
    {
        
    }

    public class DynamicTrace : HubBase<TraceHub>
    {
        private readonly string _methodName;

        public DynamicTrace(string data)
        {
            _methodName = data;
        }

        private void ClientsAll(string message)
        {
            IClientProxy clients = Hub.Clients.All;
            clients.Invoke(_methodName, message);
        }
        
        public void Write(string message)
        {
            ClientsAll(message);
        }

        public void WriteLine(string message)
        {
            ClientsAll(message);
        }
    }

    [HostProtection(SecurityAction.LinkDemand, Synchronization = true)]
    public class SignalRTraceListener : TraceListener
    {
        private const string Delimiter = ",";
        private const bool Limit = false;
        private readonly DynamicTrace _dynamicTrace;
        private string _secondaryDelim = ",";

        public SignalRTraceListener(string methodName)
        {
            _dynamicTrace = new DynamicTrace(methodName);
        }

        public override void Write(string message)
        {
            _dynamicTrace.Write(message);
        }

        public override void WriteLine(string message)
        {
            _dynamicTrace.WriteLine(message);
        }

        public override void Flush()
        {
            string internalWriter;
            lock (_messageHolder)
            {
                internalWriter = _messageHolder.ToString();
                _messageHolder = new StringBuilder();
            }
            Write(internalWriter);
        }

        private StringBuilder _messageHolder = new StringBuilder();
        private void Append(string message)
        {
            _messageHolder.Append(message);
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            if (Filter != null &&
                !Filter.ShouldTrace(eventCache, source, eventType, id, format, args, null, null))
            {
                return;
            }
            WriteHeader(source, eventType, id);
            // ReSharper disable ConditionIsAlwaysTrueOrFalse
            WriteEscaped(args != null
                             // ReSharper restore ConditionIsAlwaysTrueOrFalse
                             ? string.Format(CultureInfo.InvariantCulture, format, args)
                             : format);
            Append(Delimiter);
            if (!Limit)
            {
                Append(Delimiter);
            }
            WriteFooter(eventCache);
        }


        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            if (Filter != null &&
                !Filter.ShouldTrace(eventCache, source, eventType, id, message, null, null, null))
            {
                return;
            }
            WriteHeader(source, eventType, id);
            WriteEscaped(message);
            Append(Delimiter);
            if (!Limit)
            {
                Append(Delimiter);
            }
            WriteFooter(eventCache);
        }

        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, object data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (Filter != null &&
                !Filter.ShouldTrace(eventCache, source, eventType, id, null, null, data, null))
            {
                return;
            }
            WriteHeader(source, eventType, id);
            Append(Delimiter);
            WriteEscaped(data.ToString());
            Append(Delimiter);
            WriteFooter(eventCache);
        }

        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, params object[] data)
        {
            if (Filter != null &&
                !Filter.ShouldTrace(eventCache, source, eventType, id, null, null, null, data))
            {
                return;
            }
            WriteHeader(source, eventType, id);
            Append(Delimiter);
            if (data != null)
            {
                for (var index = 0; index < data.Length; ++index)
                {
                    if (index != 0)
                    {
                        Append(_secondaryDelim);
                    }
                    WriteEscaped(data[index].ToString());
                }
            }
            Append(Delimiter);
            WriteFooter(eventCache);
        }

        private void WriteHeader(string source, TraceEventType eventType, int id)
        {
            if (Limit)
            {
                return;
            }
            WriteEscaped(source);
            Append(Delimiter);
            Append(((object)eventType).ToString());
            Append(Delimiter);
            Append(id.ToString(CultureInfo.InvariantCulture));
            Append(Delimiter);
        }

        private void WriteDelimiter(bool isTraceOptionEnabled)
        {
            if (!Limit ||
                (Limit && isTraceOptionEnabled))
            {
                Append(Delimiter);
            }
        }

        private void WriteFooter(TraceEventCache eventCache)
        {
            if (eventCache != null)
            {
                bool isTraceOptionEnabled = IsEnabled(TraceOptions.ProcessId);
                if (isTraceOptionEnabled)
                {
                    Append(eventCache.ProcessId.ToString(CultureInfo.InvariantCulture));
                }
                // ReSharper disable ConditionIsAlwaysTrueOrFalse
                WriteDelimiter(isTraceOptionEnabled);
                // ReSharper restore ConditionIsAlwaysTrueOrFalse


                isTraceOptionEnabled = IsEnabled(TraceOptions.LogicalOperationStack);
                if (isTraceOptionEnabled)
                {
                    WriteStackEscaped(eventCache.LogicalOperationStack);
                }
                WriteDelimiter(isTraceOptionEnabled);


                isTraceOptionEnabled = IsEnabled(TraceOptions.ThreadId);
                if (isTraceOptionEnabled)
                {
                    WriteEscaped(eventCache.ThreadId.ToString(CultureInfo.InvariantCulture));
                }
                WriteDelimiter(isTraceOptionEnabled);


                isTraceOptionEnabled = IsEnabled(TraceOptions.DateTime);
                if (isTraceOptionEnabled)
                {
                    WriteEscaped(eventCache.DateTime.ToString("o", CultureInfo.InvariantCulture));
                }
                WriteDelimiter(isTraceOptionEnabled);


                isTraceOptionEnabled = IsEnabled(TraceOptions.Timestamp);
                if (isTraceOptionEnabled)
                {
                    Append(eventCache.Timestamp.ToString(CultureInfo.InvariantCulture));
                }
                // ReSharper disable ConditionIsAlwaysTrueOrFalse
                WriteDelimiter(isTraceOptionEnabled);
                // ReSharper restore ConditionIsAlwaysTrueOrFalse


                isTraceOptionEnabled = IsEnabled(TraceOptions.Callstack);
                if (isTraceOptionEnabled)
                {
                    WriteEscaped(eventCache.Callstack);
                }
            }
            else
            {
                for (var index = 0; index < 5; ++index)
                {
                    Append(Delimiter);
                }
            }
        }

        private bool WriteEscaped(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            var stringBuilder = new StringBuilder("\"");
            int startIndex;
            int num;
            for (startIndex = 0; (num = message.IndexOf('"', startIndex)) != -1; startIndex = num + 1)
            {
                stringBuilder.Append(message, startIndex, num - startIndex);
                stringBuilder.Append("\"\"");
            }
            stringBuilder.Append(message, startIndex, message.Length - startIndex);
            stringBuilder.Append("\"");
            string output = stringBuilder.ToString();
            bool result = !(Limit & string.IsNullOrWhiteSpace(output.Trim('"')));
            if (result)
            {
                Append(output);
            }
            return result;
        }

        private bool WriteStackEscaped(Stack stack)
        {
            var stringBuilder = new StringBuilder("\"");
            var flag = true;
            foreach (object obj in stack)
            {
                if (!flag)
                {
                    stringBuilder.Append(", ");
                }
                else
                {
                    flag = false;
                }
                string str = obj.ToString();
                int startIndex;
                int num;
                for (startIndex = 0; (num = str.IndexOf('"', startIndex)) != -1; startIndex = num + 1)
                {
                    stringBuilder.Append(str, startIndex, num - startIndex);
                    stringBuilder.Append("\"\"");
                }
                stringBuilder.Append(str, startIndex, str.Length - startIndex);
            }
            stringBuilder.Append("\"");
            string output = stringBuilder.ToString();
            bool result = !(Limit & string.IsNullOrWhiteSpace(output.Trim('"')));
            if (result)
            {
                Append(output);
            }
            return result;
        }

        private bool IsEnabled(TraceOptions opts)
        {
            return (opts & TraceOutputOptions) != TraceOptions.None;
        }


    }


}