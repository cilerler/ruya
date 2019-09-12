using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using Ruya.Diagnostics;

namespace Ruya.IO
{
    public static class DirectoryHelper
    {
        /// <summary>
        ///     Deletes the given directory, including the files in it
        /// </summary>
        /// <param name="path"></param>
        public static bool DeleteDirectory(string path)
        {
            var output = false;

            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This).Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            if (Directory.Exists(path))
            {
                try
                {
                    var proceed = false;
                    var directoryInfo = new DirectoryInfo(path);
                    foreach (FileInfo file in directoryInfo.GetFiles())
                    {
                        try
                        {
                            FileHelper.DeleteFile(file.FullName);
                            proceed = true;
                        }
                        catch (SecurityException securityException)
                        {
                            Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
                            break;
                        }
                        catch (PathTooLongException pathTooLongException)
                        {
                            Tracer.Instance.TraceEvent(TraceEventType.Error, 0, pathTooLongException.Message);
                            break;
                        }
                    }
                    if (proceed)
                    {
                        proceed = false;
                        try
                        {
                            DirectoryInfo[] directoriesInfo = directoryInfo.GetDirectories();
                            foreach (DirectoryInfo directory in directoriesInfo)
                            {
                                directory.Delete(true);
                            }
                            proceed = true;
                        }
                        catch (UnauthorizedAccessException unauthorizedAccessException)
                        {
                            Tracer.Instance.TraceEvent(TraceEventType.Error, 0, unauthorizedAccessException.Message);
                        }
                        catch (SecurityException securityException)
                        {
                            Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
                        }
                        catch (DirectoryNotFoundException directoryNotFoundException)
                        {
                            Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
                        }
                        catch (IOException ioException)
                        {
                            Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
                        }
                        if (proceed)
                        {
                            try
                            {
                                Directory.Delete(path);
                            }
                            catch (ArgumentNullException argumentNullException)
                            {
                                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentNullException.Message);
                            }
                            catch (ArgumentException argumentException)
                            {
                                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentException.Message);
                            }
                            catch (PathTooLongException pathTooLongException)
                            {
                                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, pathTooLongException.Message);
                            }
                            catch (UnauthorizedAccessException unauthorizedAccessException)
                            {
                                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, unauthorizedAccessException.Message);
                            }
                            catch (DirectoryNotFoundException directoryNotFoundException)
                            {
                                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
                            }
                            catch (SecurityException securityException)
                            {
                                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
                            }
                            catch (IOException ioException)
                            {
                                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
                            }
                            output = true;

                            // HARD-CODED constant
                            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Deleted {0}", path));
                        }
                    }
                }
                catch (ArgumentNullException argumentNullException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentNullException.Message);
                }
                catch (ArgumentException argumentException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentException.Message);
                }
                catch (PathTooLongException pathTooLongException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, pathTooLongException.Message);
                }
                catch (SecurityException securityException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
                }
                catch (DirectoryNotFoundException directoryNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
                }
            }

            if (logicalOperation)
            {
                TraceHelper.StopLogicalOperation();
            }

            return output;
        }

        public static DirectoryInfo CreateDirectory(string path)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This).Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            DirectoryInfo output = null;
            if (!Directory.Exists(path))
            {
                try
                {
                    output = Directory.CreateDirectory(path);

                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Created {0}", path));
                }
                catch (ArgumentNullException argumentNullException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentNullException.Message);
                }
                catch (ArgumentException argumentException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentException.Message);
                }
                catch (UnauthorizedAccessException unauthorizedAccessException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, unauthorizedAccessException.Message);
                }
                catch (DirectoryNotFoundException directoryNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
                }
                catch (IOException ioException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
                }
                catch (NotSupportedException notSupportedException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, notSupportedException.Message);
                }                
            }

            if (logicalOperation)
            {
                TraceHelper.StopLogicalOperation();
            }

            return output;
        }

        #region EnumerateDirectories

        public static IEnumerable<string> EnumerateDirectories(string path)
        {
            return EnumerateDirectories(path, string.Empty, false, SearchOption.AllDirectories);
        }

        public static IEnumerable<string> EnumerateDirectories(string path, string searchPattern)
        {
            return EnumerateDirectories(path, searchPattern, false, SearchOption.AllDirectories);
        }

        public static IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateDirectories(path, searchPattern, true, searchOption);
        }

        private static IEnumerable<string> EnumerateDirectories(string path, string searchPattern, bool useSearchOption, SearchOption searchOption)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This).Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            IEnumerable<string> output = new List<string>();
            if (Directory.Exists(path))
            {
                try
                {
                    if (!string.IsNullOrEmpty(searchPattern))
                    {
                        output = useSearchOption
                                     ? Directory.EnumerateDirectories(path, searchPattern, searchOption)
                                     : Directory.EnumerateDirectories(path, searchPattern);
                    }
                    else
                    {
                        output = Directory.EnumerateDirectories(path);
                    }

                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, "List retrieved succesfully.");
                }
                catch (ArgumentNullException argumentNullException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentNullException.Message);
                }
                catch (ArgumentException argumentException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentException.Message);
                }
                catch (UnauthorizedAccessException unauthorizedAccessException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, unauthorizedAccessException.Message);
                }
                catch (DirectoryNotFoundException directoryNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
                }
                catch (IOException ioException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
                }
                catch (SecurityException securityException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
                }
            }

            if (logicalOperation)
            {
                TraceHelper.StopLogicalOperation();
            }

            return output;
        }

        #endregion

        #region EnumerateFiles
        public static IEnumerable<string> EnumerateFiles(string path)
        {
            return EnumerateFiles(path, string.Empty, false, SearchOption.AllDirectories);
        }

        public static IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        {
            return EnumerateFiles(path, searchPattern, false, SearchOption.AllDirectories);
        }

        public static IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateFiles(path, searchPattern, true, searchOption);
        }
        private static IEnumerable<string> EnumerateFiles(string path, string searchPattern, bool useSearchOption, SearchOption searchOption)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This).Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            IEnumerable<string> output = new List<string>();
            if (Directory.Exists(path))
            {
                try
                {
                    if (!string.IsNullOrEmpty(searchPattern))
                    {
                        output = useSearchOption
                                     ? Directory.EnumerateFiles(path, searchPattern, searchOption)
                                     : Directory.EnumerateFiles(path, searchPattern);
                    }
                    else
                    {
                        output = Directory.EnumerateFiles(path);
                    }

                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, "List retrieved succesfully.");
                }
                catch (ArgumentNullException argumentNullException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentNullException.Message);
                }
                catch (ArgumentException argumentException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, argumentException.Message);
                }
                catch (UnauthorizedAccessException unauthorizedAccessException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, unauthorizedAccessException.Message);
                }
                catch (DirectoryNotFoundException directoryNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
                }
                catch (IOException ioException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
                }
                catch (SecurityException securityException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
                }
            }

            if (logicalOperation)
            {
                TraceHelper.StopLogicalOperation();
            }

            return output;
        }
        
        #endregion
    }

}