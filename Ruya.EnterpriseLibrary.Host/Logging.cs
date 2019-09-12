using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Practices.EnterpriseLibrary.Common.Configuration;
using Microsoft.Practices.EnterpriseLibrary.Logging;
using Microsoft.Practices.EnterpriseLibrary.Logging.Configuration;
using Microsoft.Practices.EnterpriseLibrary.Logging.ExtraInformation;
using Microsoft.Practices.EnterpriseLibrary.Logging.Filters;
using Microsoft.Practices.EnterpriseLibrary.Logging.Formatters;
using Microsoft.Practices.EnterpriseLibrary.Logging.TraceListeners;

namespace Ruya.EL.Host
{
    internal class Logging
    {
        /*
            //+ custom trace listeners & formatters
            //var consoleFormatter = new ConsoleFormatter();
            //var consoleTraceListenerWithXmlFormat = new ConsoleTraceListener(delimiter)
            //                                        {
            //                                            Formatter = xmlFormatter
            //                                        };
            //var consoleTraceListenerWithCsvFormat = new ConsoleTraceListener(delimiter)
            //                                        {
            //                                            Formatter = csvFormatter
            //                                        };
            //var consoleTraceListener = new ConsoleTraceListener(delimiter)
            //                           {
            //                               Formatter = formatter
            //                           };
#region Nested type: ConsoleFormatter

[ConfigurationElementType(typeof (CustomFormatterData))]
public class ConsoleFormatter : ILogFormatter
{
    private readonly NameValueCollection _attributes;

    public ConsoleFormatter(NameValueCollection attributes)
    {
        _attributes = attributes;
    }

    public ConsoleFormatter() : this(new NameValueCollection())
    {
    }

    #region ILogFormatter Members

    public string Format(LogEntry log)
    {
        var sb = new StringBuilder();
        sb.AppendFormat("Priority {0}", log.Priority.ToString(CultureInfo.InvariantCulture));
        sb.AppendFormat("Timestamp {0}", log.TimeStampString);
        sb.AppendFormat("Message", log.Message);
        sb.AppendFormat("EventId", log.EventId.ToString(CultureInfo.InvariantCulture));
        sb.AppendFormat("Severity", log.Severity.ToString());
        sb.AppendFormat("Title", log.Title);
        sb.AppendFormat("Machine", log.MachineName);
        sb.AppendFormat("AppDomain", log.AppDomainName);
        sb.AppendFormat("ProcessId", log.ProcessId);
        sb.AppendFormat("ProcessName", log.ProcessName);
        sb.AppendFormat("Win32ThreadId", log.Win32ThreadId);
        sb.AppendFormat("ThreadName", log.ManagedThreadName);
        return sb.ToString();
    }

    #endregion
}

#endregion

#region Nested type: ConsoleTraceListener

[ConfigurationElementType(typeof (CustomTraceListenerData))]
public class ConsoleTraceListener : CustomTraceListener
{
    public ConsoleTraceListener()
    {
    }

    public ConsoleTraceListener(string delimiter)
    {
        Attributes["delimiter"] = delimiter;
    }

    public override void TraceData(TraceEventCache eventCache, string source, TraceEventType eventType, int id, object data)
    {
        if (data is LogEntry
            && Formatter != null)
        {
            WriteLine(Formatter.Format(data as LogEntry));
        }
        else
        {
            WriteLine(data.ToString());
        }
    }

    public override void Write(string message)
    {
        Console.Write(Attributes["delimiter"]);

        Console.Write(message);
    }

    public override void WriteLine(string message)
    {
        // Delimit each message
        Console.WriteLine(Attributes["delimiter"]);

        // Write formatted message
        Console.WriteLine(message);
    }
}

#endregion
*/

        #region Nested type: CsvFormatter

        [ConfigurationElementType(typeof (CustomFormatterData))]
        public class CsvFormatter : ILogFormatter
        {
            private readonly TextFormatter _formatter;

            public CsvFormatter(string template)
            {
                _formatter = new TextFormatter(template);
            }

            public string Template => _formatter.Template;

            #region ILogFormatter Members

            public string Format(LogEntry log)
            {
                var logEntry = (LogEntry) log.Clone();
                logEntry.Message = NormalizeToCsvToken(logEntry.Message);
                List<KeyValuePair<string, object>> normalizableKeys = logEntry.ExtendedProperties.Where(l => l.Value == null || l.Value is string)
                                                                              .ToList();
                foreach (KeyValuePair<string, object> pair in normalizableKeys)
                {
                    logEntry.ExtendedProperties[pair.Key] = NormalizeToCsvToken((string) pair.Value);
                }

                return _formatter.Format(logEntry);
            }

            #endregion

            private static string NormalizeToCsvToken(string text)
            {
                var wrapLogText = false;

                const string qualifier = "\"";
                if (text.Contains(qualifier))
                {
                    text = text.Replace(qualifier, qualifier + qualifier);
                    wrapLogText = true;
                }

                var delimiters = new[]
                                 {
                                     ";",
                                     ",",
                                     "\n",
                                     "\r",
                                     "\r\n"
                                 };
                foreach (string delimiter in delimiters)
                {
                    if (text.Contains(delimiter))
                    {
                        wrapLogText = true;
                    }
                }

                if (wrapLogText)
                {
                    text = string.Format("\"{0}\"", text);
                }
                return text;
            }
        }

        #endregion

        #region Nested type: XmlFormatter

        [ConfigurationElementType(typeof (CustomFormatterData))]
        public class XmlFormatter : ILogFormatter
        {
            private readonly NameValueCollection _attributes;

            public XmlFormatter(NameValueCollection attributes)
            {
                _attributes = attributes;
            }

            public XmlFormatter(string prefix, string ns)
            {
                _attributes = new NameValueCollection
                              {
                                  {
                                      "prefix", prefix
                                  },
                                  {
                                      "namespace", ns
                                  }
                              };
            }

            #region ILogFormatter Members

            public string Format(LogEntry log)
            {
                string prefix = _attributes["prefix"];
                string ns = _attributes["namespace"];

                using (var s = new StringWriter())
                {
                    var w = new XmlTextWriter(s)
                            {
                                Formatting = Formatting.Indented,
                                Indentation = 2
                            };
                    w.WriteStartDocument(true);
                    w.WriteStartElement(prefix, "logEntry", ns);
                    w.WriteAttributeString("Priority", ns, log.Priority.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("Timestamp", ns, log.TimeStampString);
                    w.WriteElementString("Message", ns, log.Message);
                    w.WriteElementString("EventId", ns, log.EventId.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("Severity", ns, log.Severity.ToString());
                    w.WriteElementString("Title", ns, log.Title);
                    w.WriteElementString("Machine", ns, log.MachineName);
                    w.WriteElementString("AppDomain", ns, log.AppDomainName);
                    w.WriteElementString("ProcessId", ns, log.ProcessId);
                    w.WriteElementString("ProcessName", ns, log.ProcessName);
                    w.WriteElementString("Win32ThreadId", ns, log.Win32ThreadId);
                    w.WriteElementString("ThreadName", ns, log.ManagedThreadName);
                    w.WriteEndElement();
                    w.WriteEndDocument();

                    return s.ToString();
                }
            }

            #endregion
        }

        #endregion

        #region Menu Items

        [Description("Simple logging")]
        internal static void SimpleLogWrite()
        {
            LogMe(Logger.Writer);
        }


        [Description("Simple logging using declarative configuration")]
        internal static void SimpleLogWriteDecalarative()
        {
            // Create the default LogWriter object from the configuration.
            // The actual concrete type is determined by the configuration settings.
            var logWriterFactory = new LogWriterFactory();
            LogWriter logWriter = logWriterFactory.Create();

            LogMe(logWriter);
        }

        #endregion

        #region Auxiliary routines

        private const int MaximumPriority = 99;
        private const string DefaultCategory = "General";
        private const string BlockedCategory = "BlockedByFilter";

        internal static void LogMe(LogWriter logWriter)
        {
            Console.WriteLine("IsLoggingEnabled: {0} IsTracingEnabled: {1}{2}", logWriter.IsLoggingEnabled(), logWriter.IsTracingEnabled(), Environment.NewLine);

            // Check if logging is enabled before creating log entries.
            if (logWriter.IsLoggingEnabled())
            {
                string[] logCategories =
                {
                    DefaultCategory,
                    "Important"
                };

                // Create a Dictionary of extended properties
                var exProperties = new Dictionary<string, object>
                                   {
                                       {
                                           "Extra Information", "Some Special Value"
                                       },
                                       {
                                           "Extra Information 2", "Some Special Value 2"
                                       }
                                   };

                logWriter.Write("Log entry created using the simplest overload.");
                logWriter.Write("Log entry with a single category.", DefaultCategory);
                logWriter.Write("Log entry with multiple caregories and ExtraProperties", logCategories, exProperties);
                logWriter.Write("Log entry with a category, priority, and event ID.", DefaultCategory, 6, 9001);
                logWriter.Write("Log entry with a category, priority, event ID, " + "and severity.", DefaultCategory, 5, 9002, TraceEventType.Warning);
                logWriter.Write("Log entry with a category, priority, event ID, " + "severity, and title.", DefaultCategory, 8, 9003, TraceEventType.Critical, "Logging");
                logWriter.Write("Log entry with a category, priority, event ID, " + "severity, and title.", BlockedCategory, 8, 9004, TraceEventType.Critical, "Logging");
                logWriter.Write("Log entry with a category, priority, event ID, " + "severity, and title.", new[]
                                                                                                            {
                                                                                                                DefaultCategory,
                                                                                                                BlockedCategory
                                                                                                            }, 8, 9005, TraceEventType.Critical, "Logging");
                logWriter.Write("Log entry with a category, priority, event ID, " + "severity, title and extra properties.", DefaultCategory, 3, 9006, TraceEventType.Verbose, "Logging", exProperties);

                logWriter.Write("Entry which will not be logged due to the priority level", DefaultCategory, MaximumPriority + 1, 9007);
                ReplacePriorityFilter(logWriter, MaximumPriority + 1);
                logWriter.Write("Entry which will be logged due to the priority level", DefaultCategory, MaximumPriority + 1, 9008);
                ReplacePriorityFilter(logWriter, MaximumPriority);

                var entry1 = new LogEntry
                             {
                                 Categories = new[]
                                              {
                                                  DefaultCategory
                                              },
                                 EventId = 9009,
                                 Message = "LogEntry with individual properties specified.",
                                 Priority = 9,
                                 Severity = TraceEventType.Warning,
                                 Title = "Logging"
                             };
                ShowDetailsAndAddExtraInfo(logWriter, entry1);

                // Create a log entry that will be processed by the "Unprocessed" special source.
                logWriter.Write("Entry with category not defined in configuration.", "InvalidCategory");

                // Create a log entry that will be processed by the "Errors & Warnings" special source.
                logWriter.Write("Entry that causes a logging error.", "CauseLoggingError");

                using (new Tracer("Download"))
                {
                    using (new Tracer("Internet", new Guid("{12345678-1234-1234-1234-123456789ABC}")))
                    {
                        logWriter.Write("Hello World inside");
                    }

                    using (new Tracer("File"))
                    {
                        logWriter.Write("Hello World inside again");
                    }
                }
            }
            else
            {
                Console.WriteLine("Logging is disabled in the configuration.");
            }
        }

        internal static LoggingConfiguration BuildProgrammaticConfig()
        {
            // The default values if not specified in call to Write method are:
            // - Category: "General"
            // - Priority: -1
            // - Event ID: 1
            // - Severity: Information
            // - Title: [none]

            //++ Filters
            ICollection<string> categories = new List<string>
                                             {
                                                 BlockedCategory
                                             };
            var categoryFilter = new CategoryFilter("Category Filter", categories, CategoryFilterMode.AllowAllExceptDenied);
            var priorityFilter = new PriorityFilter("Priority Filter", 2, MaximumPriority);
            var logEnabledFilter = new LogEnabledFilter("LogEnabled Filter", true);

            //++ Formatters
            var delimiter = new string('-', 79);
            const string newLine = "{newline}";

            Func<string, string> wrap = x => string.Format("{0}{1}{2}{0}{1}", delimiter, newLine, x);

            const string briefFormat = "Timestamp: {timestamp(local)}" + newLine + "Message: {message}" + newLine + "Category: {category}" + newLine + "Priority: {priority}" + newLine + "Event Id: {eventid}" + newLine + "Activity Id: {property(ActivityId)}" + newLine + "Severity: {severity}" + newLine + "Title: {title}" + newLine;
            const string extendedFormat = briefFormat + "Machine: {localMachine}" + newLine + "App Domain: {localAppDomain}" + newLine + "Process Id: {localProcessId}" + newLine + "Process Name: {localProcessName}" + newLine + "Thread Name: {threadName}" + newLine + "Win32 Thread Id: {win32ThreadId}" + newLine + "Extended Properties: {dictionary({key} - {value}{newline})}" + newLine;
            const string extendedCsvFormat = "{timestamp(local)},{message},{priority},{eventid},{property(ActivityId)},{severity},{title},{localMachine},{localAppDomain},{localProcessId},{localProcessName},{threadName},{win32ThreadId},{dictionary({key} - {value};)},{category}";

            var formatter = new TextFormatter(briefFormat);
            var wrappedFormatter = new TextFormatter(wrap(extendedFormat));
            var extendedFormatter = new TextFormatter(extendedFormat);
            var csvFormatter = new CsvFormatter(extendedCsvFormat);
            var xmlFormatter = new XmlFormatter("myPrefix", "myNamespace");

            //++ Trace listeners            
            string saveLocation = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + Path.DirectorySeparatorChar;

            var unprocessedFlatFileTraceListener = new FlatFileTraceListener(saveLocation + "unprocessed.txt", delimiter, delimiter, extendedFormatter);
            var loggingErrorsAndWarningsFlatFileTraceListener = new FlatFileTraceListener(saveLocation + "loggingErrorsAndWarnings.txt", delimiter, delimiter, extendedFormatter);

            var exceptionRollingFlatFileTraceListener = new RollingFlatFileTraceListener(saveLocation + "exceptions.csv", delimiter, delimiter, extendedFormatter, 4096, "yyyyMMdd", RollFileExistsBehavior.Increment, RollInterval.Day, 31);
            //var exceptionRollingFlatFileTraceListener = new FlatFileTraceListener(saveLocation + "exceptions.csv", delimiter, delimiter, extendedFormatter);

            var consoleTraceListener = new FormattedTextWriterTraceListener(Console.Out, wrappedFormatter);
            var emailTraceListener = new EmailTraceListener("cengiz@ilerler.com", "cilerler@hotmail.com", "Start[", "]End", "127.0.0.1");

            var eventLog = new EventLog("Application", ".", "Logging Application");
            var eventLogTraceListener = new FormattedEventLogTraceListener(eventLog);

            var generalFlatFileTraceListener = new FlatFileTraceListener(saveLocation + "default.csv", delimiter, delimiter, csvFormatter);
            var generalXmlFlatFileTraceListener = new FlatFileTraceListener(saveLocation + "general_XmlFlatFileTraceListener.xml", string.Empty, string.Empty, xmlFormatter)
                                                  {
                                                      Filter = new EventTypeFilter(SourceLevels.Error)
                                                  };
            var generalXmlTraceListener = new XmlTraceListener(saveLocation + "general_XmlTraceListener.xml")
                                          {
                                              Filter = new EventTypeFilter(SourceLevels.Error)
                                          };

            var importantFlatFileTraceListener = new FlatFileTraceListener(saveLocation + "important.txt", delimiter, delimiter, formatter);
            var importantAsyncTraceListener = new AsynchronousTraceListenerWrapper(importantFlatFileTraceListener, false);

            var internetFlatFileTraceListener = new FlatFileTraceListener(saveLocation + "internet.txt", delimiter, delimiter, formatter);
            var fileFlatFileTraceListener = new FlatFileTraceListener(saveLocation + "file.txt", delimiter, delimiter, formatter);
            var downloadFlatFileTraceListener = new FlatFileTraceListener(saveLocation + "download.txt", delimiter, delimiter, formatter);

            //++ Build Configuration
            var config = new LoggingConfiguration
                         {
                             IsTracingEnabled = true
                         };

            config.Filters.Add(priorityFilter);
            config.Filters.Add(logEnabledFilter);
            config.Filters.Add(categoryFilter);

            // Special Sources Configuration
            config.SpecialSources.Unprocessed.AddTraceListener(unprocessedFlatFileTraceListener);
            //x config.SpecialSources.Unprocessed.AddTraceListener(consoleTraceListener);
            // The defaults for the asynchronous wrapper are:
            //   bufferSize: 30000
            //   disposeTimeout: infinite
            // config.SpecialSources.Unprocessed.AddAsynchronousTraceListener(asyncTraceListener); // sample1
            // config.SpecialSources.Unprocessed.AddAsynchronousTraceListener(consoleTraceListener); // sample2


            config.SpecialSources.LoggingErrorsAndWarnings.AddTraceListener(loggingErrorsAndWarningsFlatFileTraceListener);

            config.AddLogSource(DefaultCategory, SourceLevels.All, true)
                  .AddTraceListener(generalFlatFileTraceListener);
            config.LogSources[DefaultCategory].AddTraceListener(generalXmlTraceListener);
            config.LogSources[DefaultCategory].AddTraceListener(generalXmlFlatFileTraceListener);

            config.AddLogSource("Exception", SourceLevels.All, true)
                  .AddTraceListener(exceptionRollingFlatFileTraceListener);

            //! make sure you do not have eventlog access (not ran VS with admin rights)
            config.AddLogSource("CauseLoggingError", SourceLevels.All, true)
                  .AddTraceListener(eventLogTraceListener);

            config.AddLogSource("Important", SourceLevels.All, true)
                  .AddTraceListener(importantAsyncTraceListener);

            config.AddLogSource("Download", SourceLevels.All, true)
                  .AddTraceListener(downloadFlatFileTraceListener);

            config.AddLogSource("Internet", SourceLevels.All, true)
                  .AddTraceListener(internetFlatFileTraceListener);

            config.AddLogSource("File", SourceLevels.All, true)
                  .AddTraceListener(fileFlatFileTraceListener);

            return config;
        }

        private static void ReplacePriorityFilter(LogWriter logWriter, int maximumPriority)
        {
            logWriter.Configure(cfg =>
                                {
                                    cfg.Filters.Clear();

                                    ICollection<string> categories = new List<string>();
                                    categories.Add(BlockedCategory);

                                    var priorityFilter = new PriorityFilter("Priority Filter", 2, maximumPriority);
                                    var logEnabledFilter = new LogEnabledFilter("LogEnabled Filter", true);
                                    var categoryFilter = new CategoryFilter("Category Filter", categories, CategoryFilterMode.AllowAllExceptDenied);
                                    cfg.Filters.Add(priorityFilter);
                                    cfg.Filters.Add(logEnabledFilter);
                                    cfg.Filters.Add(categoryFilter);
                                });
        }

        private static void ShowDetailsAndAddExtraInfo(LogWriter logWriter, LogEntry entry)
        {
            // Display information about the Trace Sources and Listeners for this LogEntry. 
            IEnumerable<LogSource> sources = logWriter.GetMatchingTraceSources(entry);
            foreach (LogSource source in sources)
            {
                Console.WriteLine("Log Source name: '{0}'", source.Name);
                foreach (TraceListener listener in source.Listeners)
                {
                    Console.WriteLine(" - Listener name: '{0}'", listener.Name);
                }
            }
            // Check if any filters will block this LogEntry.
            // This approach allows you to check for specific types of filter.
            // If there are no filters of the specified type configured, the GetFilter 
            // method returns null, so check this before calling the ShouldLog method.
            var catFilter = logWriter.GetFilter<CategoryFilter>();
            if (null == catFilter ||
                catFilter.ShouldLog(entry.Categories))
            {
                Console.WriteLine("Category Filter(s) will not block this LogEntry.");
            }
            else
            {
                Console.WriteLine("A Category Filter will block this LogEntry.");
            }
            var priFilter = logWriter.GetFilter<PriorityFilter>();
            if (null == priFilter ||
                priFilter.ShouldLog(entry.Priority))
            {
                Console.WriteLine("Priority Filter(s) will not block this LogEntry.");
            }
            else
            {
                Console.WriteLine("A Priority Filter will block this LogEntry.");
            }
            // Alternatively, a simple approach can be used to check for any type of filter
            if (logWriter.ShouldLog(entry))
            {
                Console.WriteLine("This LogEntry will not be blocked due to configuration settings.");
                // Create the additional context information to add to the LogEntry. Checking that 
                // the LogEntry will not be blocked first minimizes the performance impact.
                var dict = new Dictionary<string, object>();
                // Use the information helper classes to get information about the environment and add it to the dictionary.
                var debugHelper = new DebugInformationProvider();
                debugHelper.PopulateDictionary(dict);
                Console.WriteLine("Added the current stack trace to the Log Entry.");
                var infoHelper = new ManagedSecurityContextInformationProvider();
                infoHelper.PopulateDictionary(dict);
                Console.WriteLine("Added current identity name, authentication type, and status to the Log Entry.");
                var secHelper = new UnmanagedSecurityContextInformationProvider();
                secHelper.PopulateDictionary(dict);
                Console.WriteLine("Added the current user name and process account name to the Log Entry.");
                var comHelper = new ComPlusInformationProvider();
                comHelper.PopulateDictionary(dict);
                Console.WriteLine("Added COM+ IDs and caller account information to the Log Entry.");
                // Get any other information you require and add it to the dictionary.
                string configInfo = File.ReadAllText(@"..\..\App.config");
                dict.Add("Config information", configInfo);
                Console.WriteLine("Added information about the configuration of the application to the Log Entry.");
                // Set the dictionary in the LogEntry and write it using the default LogWriter.
                entry.ExtendedProperties = dict;

                logWriter.Write(entry);

                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("This LogEntry will be blocked due to configuration settings.");
            }
        }

        #endregion
    }
}