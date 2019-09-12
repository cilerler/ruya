using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Permissions;
using System.Text;

namespace Ruya.Diagnostics.TraceListeners
{
    [HostProtection(SecurityAction.LinkDemand, Synchronization = true)]
    public class CustomDelimitedListTraceListener : CustomTextWriterTraceListener
    {
        private const string KeywordRollSize = "rollSize";
        private const string KeywordDelimiter = "delimiter";
        private const string KeywordLimit = "limit";
        private readonly object _lock = new object();
        private readonly bool _isConsole;
        private string _delimiter = ";";
        private bool _initializedDelim;
        private bool _initializedLimit;
        private bool _initializedRollSize;
        private bool _limit;
        private string _secondaryDelim = ",";

        public CustomDelimitedListTraceListener() : base(Console.Out)
        {
            _isConsole = true;
        }

        public CustomDelimitedListTraceListener(bool useErrorStream) : base(useErrorStream
                                                                                ? Console.Error
                                                                                : Console.Out)
        {
            _isConsole = true;
        }

        public CustomDelimitedListTraceListener(Stream stream) : base(stream)
        {
        }

        public CustomDelimitedListTraceListener(Stream stream, string name) : base(stream, name)
        {
        }

        public CustomDelimitedListTraceListener(TextWriter writer) : base(writer)
        {
        }

        public CustomDelimitedListTraceListener(TextWriter writer, string name) : base(writer, name)
        {
        }

        public CustomDelimitedListTraceListener(string fileName) : base(fileName)
        {
        }

        public CustomDelimitedListTraceListener(string fileName, string name) : base(fileName, name)
        {
        }

        // ReSharper disable MemberCanBePrivate.Global
        public string Delimiter
        {
            get
            {
                lock (_lock)
                {
                    if (_initializedDelim)
                    {
                        return _delimiter;
                    }
                    if (Attributes.ContainsKey(KeywordDelimiter))
                    {
                        _delimiter = Attributes[KeywordDelimiter];
                    }
                    _initializedDelim = true;
                }
                return _delimiter;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(Name);
                }
                if (value.Length == 0)
                {
                    throw new ArgumentException(KeywordDelimiter);
                }
                lock (_lock)
                {
                    _delimiter = value;
                    _initializedDelim = true;
                }
                _secondaryDelim = _delimiter == ","
                                      ? ";"
                                      : ",";
            }
        }

        // ReSharper disable MemberCanBePrivate.Global
        public bool Limit
        {
            get
            {
                lock (_lock)
                {
                    if (_initializedLimit)
                    {
                        return _limit;
                    }
                    if (Attributes.ContainsKey(KeywordLimit))
                    {
                        _limit = Convert.ToBoolean(Attributes[KeywordLimit], CultureInfo.InvariantCulture);
                    }
                    _initializedLimit = true;
                }
                return _limit;
            }
            set
            {
                if (value.ToString()
                         .Length == 0)
                {
                    throw new ArgumentException(KeywordLimit);
                }
                lock (_lock)
                {
                    _limit = value;
                    _initializedLimit = true;
                }
            }
        }

        private static void ArgumentNull()
        {
            // HARD-CODED constant
            throw new DiagnosticsException(TraceEventType.Error, "Roll Size is not available");
        }

        public override int RollSize
        {
            get
            {
                lock (_lock)
                {
                    if (_initializedRollSize)
                    {
                        return base.RollSize;
                    }
                    if (Attributes.ContainsKey(KeywordRollSize))
                    {
                        base.RollSize = Convert.ToInt32(Attributes[KeywordRollSize], CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        ArgumentNull();
                    }
                    _initializedRollSize = true;
                }
                return base.RollSize;
            }
            set
            {
                if (value == 0)
                {
                    throw new ArgumentException(KeywordRollSize);
                }
                lock (_lock)
                {
                    base.RollSize = value;
                    _initializedRollSize = true;
                }
            }
        }


        protected override string[] GetSupportedAttributes()
        {
            return new[]
                   {
                       KeywordDelimiter,
                       KeywordLimit,
                       KeywordRollSize
                   };
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
            Write(Delimiter);
            if (!Limit)
            {
                Write(Delimiter);
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
            Write(Delimiter);
            if (!Limit)
            {
                Write(Delimiter);
            }
            WriteFooter(eventCache);
        }

        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, object data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            if (Filter != null &&
                !Filter.ShouldTrace(eventCache, source, eventType, id, null, null, data, null))
            {
                return;
            }
            WriteHeader(source, eventType, id);
            Write(Delimiter);
            WriteEscaped(data.ToString());
            Write(Delimiter);
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
            Write(Delimiter);
            if (data != null)
            {
                for (var index = 0; index < data.Length; ++index)
                {
                    if (index != 0)
                    {
                        Write(_secondaryDelim);
                    }
                    WriteEscaped(data[index].ToString());
                }
            }
            Write(Delimiter);
            WriteFooter(eventCache);
        }

        private void WriteHeader(string source, TraceEventType eventType, int id)
        {
            if (Limit)
            {
                return;
            }
            WriteEscaped(source);
            Write(Delimiter);
            Write(((object) eventType).ToString());
            Write(Delimiter);
            Write(id.ToString(CultureInfo.InvariantCulture));
            Write(Delimiter);
        }

        private void WriteDelimiter(bool isTraceOptionEnabled)
        {
            if (!_isConsole ||
                !Limit ||
                (Limit && isTraceOptionEnabled))
            {
                Write(Delimiter);
            }
        }

        private void WriteFooter(TraceEventCache eventCache)
        {
            if (eventCache != null)
            {
                bool isTraceOptionEnabled = IsEnabled(TraceOptions.ProcessId);
                if (isTraceOptionEnabled)
                {
                    Write(eventCache.ProcessId.ToString(CultureInfo.InvariantCulture));
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
                    Write(eventCache.Timestamp.ToString(CultureInfo.InvariantCulture));
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
                    Write(Delimiter);
                }
            }
            WriteLine(string.Empty);
            if (_isConsole)
            {
                // HARD-CODED constant
                WriteLine(new string('=', 10));
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
                Write(output);
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
                Write(output);
            }
            return result;
        }

        private bool IsEnabled(TraceOptions opts)
        {
            return (opts & TraceOutputOptions) != TraceOptions.None;
        }
    }
}