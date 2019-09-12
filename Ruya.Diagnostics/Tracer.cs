using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using Ruya.Core;
using Ruya.Core.ThirdParty;
using Ruya.Diagnostics.Properties;

namespace Ruya.Diagnostics
{
    // TEST class Tracer
    // COMMENT class Tracer
    public sealed class Tracer
    {
        private static List<TraceSource> _traceSourceCollection;

        private static string GetCallerAssemblySpecificInformation()
        {
            MethodBase methodBase = StackTraceHelper.GetMethod(StackTraceCaller.CallersCallersCaller);
            Assembly assembly = methodBase.Module.Assembly;
            string output = assembly.GetTitleAttribute();
            return output;
        }

        private TraceSource GetTraceSource(string traceSourceName)
        {
            if (string.IsNullOrEmpty(traceSourceName))
            {
                traceSourceName = GetCallerAssemblySpecificInformation();
            }
            if (_traceSourceCollection == null)
            {
                Create(traceSourceName);
            }

            TraceSource traceSource = null;
            while (traceSource == null)
            {
                traceSource = _traceSourceCollection?.Find(ts => ts.Name.Equals(traceSourceName));
                if (traceSource == null)
                {
                    Create(traceSourceName);
                }
            }
            return traceSource;
        }

        /// <summary>
        /// Creates the tracesource
        /// </summary>
        /// <param name="traceSourceName"></param>
        [Conditional(Constants.Trace)]
        private void Create(string traceSourceName)
        {
            Type callerBase = StackTraceHelper.GetMethod(StackTraceCaller.Caller)
                                              .ReflectedType;
            Type myBase = MethodBase.GetCurrentMethod()
                                    .ReflectedType;

            bool isInternalCall = callerBase != null && myBase != null && myBase.FullName.Equals(callerBase.FullName);

            var shouldCreateTraceSource = false;
            if (!isInternalCall)
            {
                TraceSource traceSource = GetTraceSource(traceSourceName);
                if (traceSource == null)
                {
                    shouldCreateTraceSource = true;
                }
            }
            if (isInternalCall || shouldCreateTraceSource)
            {
                if (_traceSourceCollection == null)
                {
                    _traceSourceCollection = new List<TraceSource>();
                }
                _traceSourceCollection.Add(new TraceSource(traceSourceName));
                TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, Resources.Tracer_TraceDataToFile_Created, traceSourceName));
            }
        }
        
        private static IEnumerable<string> GetEnsuredList(IEnumerable<string> input)
        {
            List<string> traceSourceNames = input.ToList();
            Assembly assembly = Assembly.GetExecutingAssembly();
            string output = assembly.GetTitleAttribute();
            traceSourceNames.Remove(output);
            traceSourceNames.Add(output);
            return traceSourceNames;
        }

        /// <summary>
        /// Closes all the trace sources in the trace sources collection
        /// </summary>
        [Conditional(Constants.Trace)]
        public void CloseAll()
        {
            IEnumerable<string> traceSourceNames = GetEnsuredList(_traceSourceCollection.Select(tsc => tsc.Name));
            
            foreach (string traceSourceName in traceSourceNames)
            {
                Close(traceSourceName);
            }
        }
        
        /// <summary>
        /// Closes the given trace source
        /// </summary>
        /// <param name="traceSourceName"></param>
        [Conditional(Constants.Trace)]
        public void Close(string traceSourceName)
        {
            if (_traceSourceCollection == null)
            {
                return;
            }

            TraceSource traceSource = GetTraceSource(traceSourceName);
            if (traceSource == null)
            {
                throw new ArgumentOutOfRangeException(nameof(traceSourceName));
            }

            TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, Resources.Tracer_TraceDataToFile_Destroyed, traceSourceName));

            traceSource.Flush();
            traceSource.Close();
            _traceSourceCollection.Remove(traceSource);
        }

        /// <summary>
        /// 
        ///     <example>
        ///         <code>
        ///             string info = GetAttributeValue(null, "rollingXmlWriter", "initializeData");
        ///         </code>
        ///     </example>
        /// </summary>
        /// <param name="traceSourceName"></param>
        /// <param name="listenerName"></param>
        /// <param name="fieldName"></param>
        /// <returns></returns>
        public string GetAttributeValue(string traceSourceName, string listenerName, string fieldName)
        {
            
            if (string.IsNullOrEmpty(fieldName))
            {
                throw new ArgumentNullException(nameof(fieldName));
            }
            if (string.IsNullOrEmpty(listenerName))
            {
                throw new ArgumentNullException(nameof(listenerName));
            }
            TraceSource traceSource = GetTraceSource(traceSourceName);
            TraceListenerCollection traceListenerCollection = traceSource.Listeners;
            TraceListener traceListener = traceListenerCollection[listenerName];
            FieldInfo fieldInfo = traceListener?.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            var fieldValue = (string) fieldInfo?.GetValue(traceListener);
            return fieldValue;
        }


        [Conditional(Constants.Trace)]
        public void TraceTransfer(int id, string message, Guid relatedActivityId)
        {
            TraceSource traceSource = GetTraceSource(null);
            traceSource?.TraceTransfer(id, message, relatedActivityId);
        }

        [Conditional(Constants.Trace)]
        public void TraceInformation(string message)
        {
            TraceSource traceSource = GetTraceSource(null);
            traceSource?.TraceInformation(message);
        }

        [Conditional(Constants.Trace)]
        public void TraceEvent(TraceEventType eventType, int id)
        {
            TraceSource traceSource = GetTraceSource(null);
            traceSource?.TraceEvent(eventType, id);
        }

        [Conditional(Constants.Trace)]
        public void TraceEvent(TraceEventType eventType, int id, string message)
        {
            TraceSource traceSource = GetTraceSource(null);
            traceSource?.TraceEvent(eventType, id, message);
        }

        [Conditional(Constants.Trace)]
        public void TraceEvent(TraceEventType eventType, int id, string format, params object[] args)
        {
            TraceSource traceSource = GetTraceSource(null);
            traceSource?.TraceEvent(eventType, id, format, args);
        }

        [Conditional(Constants.Trace)]
        public void TraceData(TraceEventType eventType, int id, object data)
        {
            TraceSource traceSource = GetTraceSource(null);
            traceSource?.TraceData(eventType, id, data);
        }

        [Conditional(Constants.Trace)]
        public void TraceData(TraceEventType eventType, int id, params object[] data)
        {
            TraceSource traceSource = GetTraceSource(null);
            traceSource?.TraceData(eventType, id, data);
        }

        [Conditional(Constants.Trace)]
        public void TraceDataToFile(TraceEventType traceEventType, string path, object content, bool useObjectDump = false)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            TraceSource traceSource = GetTraceSource(null);

            if (traceSource == null ||
                !traceSource.Switch.ShouldTrace(traceEventType) ||
                traceSource.Listeners == null)
            {
                return;
            }

            try
            {
                string output = content.ToString();
                if (useObjectDump)
                {
                    output = content.Dump(5);
                }
                File.WriteAllText(path, output);
                traceSource.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, Resources.Tracer_TraceDataToFile_Written, path));
            }
            catch (ArgumentNullException ane)
            {
                TraceEvent(TraceEventType.Error, 0, ane.Message);
            }
            catch (ArgumentException ae)
            {
                TraceEvent(TraceEventType.Error, 0, ae.Message);
            }
            catch (PathTooLongException ptle)
            {
                TraceEvent(TraceEventType.Error, 0, ptle.Message);
            }
            catch (DirectoryNotFoundException dnfe)
            {
                TraceEvent(TraceEventType.Error, 0, dnfe.Message);
            }
            catch (IOException ie)
            {
                TraceEvent(TraceEventType.Error, 0, ie.Message);
            }
            catch (UnauthorizedAccessException uae)
            {
                TraceEvent(TraceEventType.Error, 0, uae.Message);
            }
            catch (NotSupportedException nse)
            {
                TraceEvent(TraceEventType.Error, 0, nse.Message);
            }
            catch (SecurityException se)
            {
                TraceEvent(TraceEventType.Error, 0, se.Message);
            }
        }

        #region Singleton

        private static readonly Lazy<Tracer> Lazy = new Lazy<Tracer>(() => new Tracer());

        public static Tracer Instance => Lazy.Value;

        private Tracer()
        {
        }

        #endregion
    }
}