using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Permissions;
using System.Text;
using Ruya.Core;
using Ruya.Diagnostics.Properties;

namespace Ruya.Diagnostics.TraceListeners
{
    /// <summary>
    ///     An extended XmlWriterTraceListener that starts a new file after a configured trace file size. Trace files will be
    ///     numbered with a four character number suffix.
    ///     <example>
    ///         <code>
    ///             <sharedListeners>
    ///                 <add name="rollingXmlWriter"
    ///                     type="Ruya.Diagnostics.TraceListeners.RollingXmlWriterTraceListener, Ruya.Diagnostics"
    ///                     traceOutputOptions="DateTime, LogicalOperationStack, ProcessId, ThreadId, Callstack, Timestamp"
    ///                     initializeData="TraceFileName.svclog" rollSize="1048576" />
    ///             </sharedListeners>
    ///         </code>
    ///     </example>
    /// </summary>
    [HostProtection(Synchronization = true)]
    public class RollingXmlWriterTraceListener : XmlWriterTraceListener
    {

        private readonly object _lock = new object();

        /// <summary>
        ///     Initializes a new instance of the <see cref="RollingXmlWriterTraceListener" /> class by specifying the trace file
        ///     name.
        /// </summary>
        /// <param name="fileName">The trace file name.</param>
        public RollingXmlWriterTraceListener(string fileName) : base(fileName)
        {
            _fileName = fileName;
            DirectoryEnsurer();
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RollingXmlWriterTraceListener" /> class by specifying the trace file
        ///     name and the name of the new instance.
        /// </summary>
        /// <param name="fileName">The trace file name.</param>
        /// <param name="name">The name of the new instance.</param>
        public RollingXmlWriterTraceListener(string fileName, string name) : base(fileName, name)
        {
            _fileName = fileName;
            DirectoryEnsurer();
        }

        /// <summary>
        ///     Gets the custom attributes supported by the trace listener.
        /// </summary>
        /// <returns>
        ///     A string array naming the custom attributes supported by the trace listener, or null if there are no custom
        ///     attributes.
        /// </returns>
        protected override string[] GetSupportedAttributes()
        {
            return new[]
                   {
                       KeywordRollSize
                   };
        }

        /// <summary>
        ///     Emits an error message to the listener.
        /// </summary>
        /// <param name="message">A message to emit.</param>
        public override void Fail(string message)
        {
            RollingEnsurer();
            base.Fail(message);
        }

        /// <summary>
        ///     Emits an error message and a detailed message to the listener.
        /// </summary>
        /// <param name="message">The error message to write.</param>
        /// <param name="detailMessage">The detailed error message to append to the error message.</param>
        public override void Fail(string message, string detailMessage)
        {
            RollingEnsurer();
            base.Fail(message, detailMessage);
        }

        /// <summary>
        ///     Writes trace information, a data object, and event information to the file or stream.
        /// </summary>
        /// <param name="eventCache">
        ///     A <see cref="T:System.Diagnostics.TraceEventCache" /> that contains the current process ID,
        ///     thread ID, and stack trace information.
        /// </param>
        /// <param name="source">The source name.</param>
        /// <param name="eventType">One of the <see cref="T:System.Diagnostics.TraceEventType" /> values.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="data">A data object to emit.</param>
        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, object data)
        {
            RollingEnsurer();
            bool removeCallstack = CallStackAdder(eventType);
            base.TraceData(eventCache, source, eventType, id, data);
            CallStackRemover(removeCallstack);
        }

        /// <summary>
        ///     Writes trace information, data objects, and event information to the file or stream.
        /// </summary>
        /// <param name="eventCache">
        ///     A <see cref="T:System.Diagnostics.TraceEventCache" /> that contains the current process ID,
        ///     thread ID, and stack trace information.
        /// </param>
        /// <param name="source">The source name.</param>
        /// <param name="eventType">One of the <see cref="T:System.Diagnostics.TraceEventType" /> values.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="data">An array of data objects to emit.</param>
        public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, params object[] data)
        {
            RollingEnsurer();
            bool removeCallstack = CallStackAdder(eventType);
            base.TraceData(eventCache, source, eventType, id, data);
            CallStackRemover(removeCallstack);
        }

        /// <summary>
        ///     Writes trace and event information to the listener specific output.
        /// </summary>
        /// <param name="eventCache">
        ///     A <see cref="T:System.Diagnostics.TraceEventCache" /> object that contains the current process
        ///     ID, thread ID, and stack trace information.
        /// </param>
        /// <param name="source">
        ///     A name used to identify the output, typically the name of the application that generated the trace
        ///     event.
        /// </param>
        /// <param name="eventType">
        ///     One of the <see cref="T:System.Diagnostics.TraceEventType" /> values specifying the type of
        ///     event that has caused the trace.
        /// </param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <PermissionSet>
        ///     <IPermission
        ///         class="System.Security.Permissions.EnvironmentPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
        ///         version="1" Unrestricted="true" />
        ///     <IPermission
        ///         class="System.Security.Permissions.SecurityPermission, mscorlib, Version=2.0.3600.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
        ///         version="1" Flags="UnmanagedCode" />
        /// </PermissionSet>
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id)
        {
            RollingEnsurer();
            bool removeCallstack = CallStackAdder(eventType);
            base.TraceEvent(eventCache, source, eventType, id);
            CallStackRemover(removeCallstack);
        }

        /// <summary>
        ///     Writes trace information, a message, and event information to the file or stream.
        /// </summary>
        /// <param name="eventCache">
        ///     A <see cref="T:System.Diagnostics.TraceEventCache" /> that contains the current process ID,
        ///     thread ID, and stack trace information.
        /// </param>
        /// <param name="source">The source name.</param>
        /// <param name="eventType">One of the <see cref="T:System.Diagnostics.TraceEventType" /> values.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="message">The message to write.</param>
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            RollingEnsurer();
            bool removeCallstack = CallStackAdder(eventType);
            base.TraceEvent(eventCache, source, eventType, id, message);
            CallStackRemover(removeCallstack);
        }

        /// <summary>
        ///     Writes trace information, a formatted message, and event information to the file or stream.
        /// </summary>
        /// <param name="eventCache">
        ///     A <see cref="T:System.Diagnostics.TraceEventCache" /> that contains the current process ID,
        ///     thread ID, and stack trace information.
        /// </param>
        /// <param name="source">The source name.</param>
        /// <param name="eventType">One of the <see cref="T:System.Diagnostics.TraceEventType" /> values.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="format">
        ///     A format string that contains zero or more format items that correspond to objects in the
        ///     <paramref name="args" /> array.
        /// </param>
        /// <param name="args">An object array containing zero or more objects to format.</param>
        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            CutOffEnsurer(eventType, id, format);
            RollingEnsurer();
            bool removeCallstack = CallStackAdder(eventType);
            base.TraceEvent(eventCache, source, eventType, id, format, args);
            CallStackRemover(removeCallstack);
        }
        
        /// <summary>
        ///     Writes trace information including the identity of a related activity, a message, and event information to the file
        ///     or stream.
        /// </summary>
        /// <param name="eventCache">
        ///     A <see cref="T:System.Diagnostics.TraceEventCache" /> that contains the current process ID,
        ///     thread ID, and stack trace information.
        /// </param>
        /// <param name="source">The source name.</param>
        /// <param name="id">A numeric identifier for the event.</param>
        /// <param name="message">A trace message to write.</param>
        /// <param name="relatedActivityId">A <see cref="T:System.Guid" /> structure that identifies a related activity.</param>
        public override void TraceTransfer(TraceEventCache eventCache, string source, int id, string message, Guid relatedActivityId)
        {
            RollingEnsurer();
            base.TraceTransfer(eventCache, source, id, message, relatedActivityId);
        }

        /// <summary>
        ///     Writes the value of the object's <see cref="M:System.Object.Tostring" /> method to the listener.
        /// </summary>
        /// <param name="o">An <see cref="T:System.Object" /> whose fully qualified class name you want to write.</param>
        public override void Write(object o)
        {
            RollingEnsurer();
            base.Write(o);
        }

        /// <summary>
        ///     Writes a category name and the value of the object's <see cref="M:System.Object.Tostring" /> method to the
        ///     listener.
        /// </summary>
        /// <param name="o">An <see cref="T:System.Object" /> whose fully qualified class name you want to write.</param>
        /// <param name="category">A category name used to organize the output.</param>
        public override void Write(object o, string category)
        {
            RollingEnsurer();
            base.Write(o, category);
        }

        /// <summary>
        ///     Writes a verbatim message without any additional context information to the file or stream.
        /// </summary>
        /// <param name="message">The message to write.</param>
        public override void Write(string message)
        {
            RollingEnsurer();
            base.Write(message);
        }

        /// <summary>
        ///     Writes a category name and a message to the listener.
        /// </summary>
        /// <param name="message">A message to write.</param>
        /// <param name="category">A category name used to organize the output.</param>
        public override void Write(string message, string category)
        {
            RollingEnsurer();
            base.Write(message, category);
        }

        /// <summary>
        ///     Writes the value of the object's <see cref="M:System.Object.Tostring" /> method to the listener.
        /// </summary>
        /// <param name="o">An <see cref="T:System.Object" /> whose fully qualified class name you want to write.</param>
        public override void WriteLine(object o)
        {
            RollingEnsurer();
            base.WriteLine(o);
        }

        /// <summary>
        ///     Writes a category name and the value of the object's <see cref="M:System.Object.Tostring" /> method to the
        ///     listener.
        /// </summary>
        /// <param name="o">An <see cref="T:System.Object" /> whose fully qualified class name you want to write.</param>
        /// <param name="category">A category name used to organize the output.</param>
        public override void WriteLine(object o, string category)
        {
            RollingEnsurer();
            base.WriteLine(o, category);
        }

        /// <summary>
        ///     Writes a verbatim message without any additional context information followed by the current line terminator to the
        ///     file or stream.
        /// </summary>
        /// <param name="message">The message to write.</param>
        public override void WriteLine(string message)
        {
            RollingEnsurer();
            base.WriteLine(message);
        }

        /// <summary>
        ///     Writes a category name and a message to the listener, followed by a line terminator.
        /// </summary>
        /// <param name="message">A message to write.</param>
        /// <param name="category">A category name used to organize the output.</param>
        public override void WriteLine(string message, string category)
        {
            RollingEnsurer();
            base.WriteLine(message, category);
        }
        
        private void CutOffEnsurer(TraceEventType eventType, int id, string message)
        {
            _cutOff = eventType.Equals(TraceEventType.Information) && id.Equals(0) && !string.IsNullOrEmpty(message) && message.Equals(Resources.RollingXmlWriterTraceListener_CutOff);
        }

        private DateTime _lastCheck = DateTime.UtcNow;
        private void RollingEnsurer()
        {
            DateTime now = DateTime.UtcNow;
            bool newDate = now.Date != _lastCheck.Date;
            _lastCheck = now;

            bool changeFile = newDate || IsRollingConditionReached || _cutOff;
            if (!changeFile)
            {
                return;
            }
            ChangeFile();
        }

        private void DirectoryEnsurer()
        {
            var traceFileInfo = new FileInfo(_fileName);
            string directoryName = traceFileInfo.DirectoryName;
            if (directoryName != null)
            {
                if (!Directory.Exists(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }
            }
            else
            {
                // HARD-CODED constant
                throw new DiagnosticsException("Logging directory does not exist!");
            }
        }

        /// <summary>
        ///     Gets a value indicating whether the condition to roll over the trace file is reached.
        /// </summary>
        /// <value>
        ///     <c>true</c> if the condition to roll over the trace file is reached; otherwise, <c>false</c>.
        /// </value>
        protected bool IsRollingConditionReached
        {
            get
            {
                bool output;
                lock (_lock)
                {
                    var traceFileInfo = new FileInfo(_fileName);

                    // TODO calculate the next message size and make sure it will not exceed it
                    try
                    {
                        output = traceFileInfo.Length > RollSize;
                    }
                    catch (FileNotFoundException)
                    {
                        output = false;
                    }
                }
                return output;
            }
        }

        private bool _cutOff;

        private void ChangeFile()
        {
            lock (_lock)
            {
                string path = null;
                bool fileNameAvailable = false;
                while (!fileNameAvailable)
                {                    
                    path = GenerateFileName();
                    if (!File.Exists(path))
                        fileNameAvailable = true;
                }
                Close();
                File.Move(_fileName, path);

                //x ModifyForWebBasedTraceViewer(path);
                _cutOff = false;
            }
        }

        private string GenerateFileName()
        {
            string directoryName = Path.GetDirectoryName(_fileName) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_fileName);
            string extension = Path.GetExtension(_fileName);
            string fileName = string.Format(CultureInfo.InvariantCulture, Resources.RollingXmlWriterTraceListener_GenerateFileName_FileNameFormat, fileNameWithoutExtension, DateTimeHelper.GetDateTimeUtcNowWithUtcOffset(), extension);
            string path = Path.Combine(directoryName, fileName);
            return path;
        }
        
        /// <summary>
        ///     The basic trace file name as it is configured in configuration file's system.diagnostics section. However, this
        ///     class will append a numeric suffix to the file name (respecting the original file extension).
        /// </summary>
        private readonly string _fileName;

        /// <summary>
        ///     Reads the custom attributes' values from the configuration file. We call this method the first time the attributes
        ///     are accessed.
        ///     <remarks>
        ///         We do not do this when the listener is constructed becausethe attributes will not yet have been read
        ///         from the configuration file.
        ///     </remarks>
        /// </summary>
        private bool _initializedRollSize;
        
        private const string KeywordRollSize = "rollSize";

        /// <summary>
        ///     The size in bytes of a trace file before a new file is started.
        /// </summary>
        private long _rollSize;

        /// <summary>
        ///     Gets or sets the maximum size of the trace file.
        /// </summary>
        /// <value>The maximum size of the trace file.</value>
        public long RollSize // ReSharper restore MemberCanBePrivate.Global
        {
            get
            {
                lock (_lock)
                {
                    if (_initializedRollSize)
                    {
                        return _rollSize;
                    }
                    if (Attributes.ContainsKey(KeywordRollSize))
                    {
                        long output;
                        string attributeValue = Attributes[KeywordRollSize];
                        if (long.TryParse(attributeValue, out output))
                        {
                            _rollSize = output;
                            _initializedRollSize = true;
                        }
                        else
                        {
                            DiagnosticsExceptionThrower(attributeValue);
                        }
                    }
                    else
                    {
                        DiagnosticsExceptionThrower(null);
                    }
                }
                return _rollSize;
            }
            set
            {
                if (value.ToString(CultureInfo.InvariantCulture).Length == 0)
                {
                    throw new ArgumentException(KeywordRollSize);
                }
                lock (_lock)
                {
                    _rollSize = value;
                    _initializedRollSize = true;
                }
            }
        }

        private void DiagnosticsExceptionThrower(string attributeValue)
        {
            throw new DiagnosticsException(string.Format(CultureInfo.InvariantCulture, Resources.RollingXmlWriterTraceListener_RollSize, Name, attributeValue));
        }

        private bool CallStackAdder(TraceEventType eventType)
        {
            if (TraceOutputOptions.HasFlag(TraceOptions.Callstack))
            {
                return false;
            }

            bool importantEvent = eventType.HasFlag(TraceEventType.Error) || eventType.HasFlag(TraceEventType.Critical);
            if (!importantEvent)
            {
                return false;
            }
            TraceOutputOptions |= TraceOptions.Callstack;
            return true;
        }

        private void CallStackRemover(bool removeCallStack)
        {
            if (removeCallStack)
                TraceOutputOptions -= TraceOptions.Callstack;
        }
    }
}