using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using Ruya.Diagnostics;

namespace Ruya.IO
{
    public static class FileHelper
    {
        public static bool DeleteFile(string path)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            var output = false;
            bool isExist = File.Exists(path);
            if (isExist)
            {
                try
                {
                    File.Delete(path);
                    output = true;
                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Deleted {0}", path));
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

        public static bool CopyFile(string sourceFile, string destinationFile, bool overwrite)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            var output = false;
            bool isExist = File.Exists(sourceFile);
            if (isExist)
            {
                try
                {
                    File.Copy(sourceFile, destinationFile, overwrite);
                    output = true;

                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Copied from {0} to {1}, overwrite {2}", sourceFile, destinationFile, overwrite));
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

        public static bool MoveFile(string sourceFile, string destinationFile, bool overwrite)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            var output = false;
            bool isExist = File.Exists(destinationFile);
            if (isExist)
            {
                bool copyFileSuccess = CopyFile(sourceFile, destinationFile, overwrite);
                if (copyFileSuccess)
                {
                    output = DeleteFile(sourceFile);
                }
            }
            else
            {
                try
                {
                    File.Move(sourceFile, destinationFile);
                    output = true;
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

            if (output)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Moved from {0} to {1}, overwrite {2}, exist {3}", sourceFile, destinationFile, overwrite, isExist));
            }

            if (logicalOperation)
            {
                TraceHelper.StopLogicalOperation();
            }

            return output;
        }
        
        #region ReadFileAllText

        public static string ReadFileAllText(string path)
        {
            return ReadFileAllText(path, null);
        }
        
        public static string ReadFileAllText(string path, Encoding encoding)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            string output = string.Empty;
            bool isExist = File.Exists(path);
            if (isExist)
            {
                try
                {
                    output = encoding == null
                                 ? File.ReadAllText(path)
                                 : File.ReadAllText(path, encoding);
                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Read complete {0}", path));
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
                catch (SecurityException securityException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
                }
                catch (DirectoryNotFoundException directoryNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
                }
                catch (FileNotFoundException fileNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, fileNotFoundException.Message);
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

        #endregion
        #region WriteFileAllText

        public static bool WriteFileAllText(string path, string contents)
        {
            return WriteFileAllText(path, contents, null);
        }

        public static bool WriteFileAllText(string path, string contents, Encoding encoding)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            var output = false;

            try
            {
                if (encoding == null)
                {
                    File.WriteAllText(path, contents);
                }
                else
                {
                    File.WriteAllText(path, contents, encoding);
                }
                output = true;

                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Write complete {0}", path));
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
            catch (SecurityException securityException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
            }
            catch (DirectoryNotFoundException directoryNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
            }
            catch (FileNotFoundException fileNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, fileNotFoundException.Message);
            }
            catch (IOException ioException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, notSupportedException.Message);
            }


            if (logicalOperation)
            {
                TraceHelper.StopLogicalOperation();
            }

            return output;
        }

        #endregion
        #region AppendFileAllText

        public static bool AppendFileAllText(string path, string contents)
        {
            return AppendFileAllText(path, contents, null);
        }

        public static bool AppendFileAllText(string path, string contents, Encoding encoding)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            var output = false;

            try
            {
                if (encoding == null)
                {
                    File.AppendAllText(path, contents);
                }
                else
                {
                    File.AppendAllText(path, contents, encoding);
                }
                output = true;

                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Append complete {0}", path));
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
            catch (SecurityException securityException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
            }
            catch (DirectoryNotFoundException directoryNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
            }
            catch (FileNotFoundException fileNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, fileNotFoundException.Message);
            }
            catch (IOException ioException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, notSupportedException.Message);
            }


            if (logicalOperation)
            {
                TraceHelper.StopLogicalOperation();
            }

            return output;
        }

        #endregion

        #region ReadFileAllLines

        public static string[] ReadFileAllLines(string path)
        {
            return ReadFileAllLines(path, null);
        }

        public static string[] ReadFileAllLines(string path, Encoding encoding)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            string[] output = null;
            bool isExist = File.Exists(path);
            if (isExist)
            {
                try
                {
                    output = encoding == null
                                 ? File.ReadAllLines(path)
                                 : File.ReadAllLines(path, encoding);
                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Read complete {0}", path));
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
                catch (SecurityException securityException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
                }
                catch (DirectoryNotFoundException directoryNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
                }
                catch (FileNotFoundException fileNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, fileNotFoundException.Message);
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

        #endregion
        #region WriteFileAllLines

        public static bool WriteFileAllLines(string path, string[] contents)
        {
            return WriteFileAllLines(path, contents, null);
        }

        public static bool WriteAllLines(string path, IEnumerable<string> contents)
        {
            return WriteFileAllLines(path, contents.ToArray(), null);
        }

        public static bool WriteFileAllLines(string path, IEnumerable<string> contents, Encoding encoding)
        {
            return WriteFileAllLines(path, contents.ToArray(), encoding);
        }

        public static bool WriteFileAllLines(string path, string[] contents, Encoding encoding)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            var output = false;

            try
            {
                if (encoding == null)
                {
                    File.WriteAllLines(path, contents);
                }
                else
                {
                    File.WriteAllLines(path, contents, encoding);
                }
                output = true;

                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Write complete {0}", path));
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
            catch (SecurityException securityException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
            }
            catch (DirectoryNotFoundException directoryNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
            }
            catch (FileNotFoundException fileNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, fileNotFoundException.Message);
            }
            catch (IOException ioException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, notSupportedException.Message);
            }


            if (logicalOperation)
            {
                TraceHelper.StopLogicalOperation();
            }

            return output;
        }

        #endregion
        #region AppendFileAllLines

        public static bool AppendFileAllLines(string path, string[] contents)
        {
            return AppendFileAllLines(path, contents, null);
        }

        public static bool AppendAllLines(string path, IEnumerable<string> contents)
        {
            return AppendFileAllLines(path, contents.ToArray(), null);
        }

        public static bool AppendFileAllLines(string path, IEnumerable<string> contents, Encoding encoding)
        {
            return AppendFileAllLines(path, contents.ToArray(), encoding);
        }

        public static bool AppendFileAllLines(string path, string[] contents, Encoding encoding)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            var output = false;

            try
            {
                if (encoding == null)
                {
                    File.AppendAllLines(path, contents);
                }
                else
                {
                    File.AppendAllLines(path, contents, encoding);
                }
                output = true;

                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Append complete {0}", path));
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
            catch (SecurityException securityException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
            }
            catch (DirectoryNotFoundException directoryNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
            }
            catch (FileNotFoundException fileNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, fileNotFoundException.Message);
            }
            catch (IOException ioException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, notSupportedException.Message);
            }


            if (logicalOperation)
            {
                TraceHelper.StopLogicalOperation();
            }

            return output;
        }

        #endregion

        #region ReadFileAllBytes

        public static byte[] ReadFileAllBytes(string path, Encoding encoding)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            byte[] output = null;
            bool isExist = File.Exists(path);
            if (isExist)
            {
                try
                {
                    output = File.ReadAllBytes(path);
                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Read complete {0}", path));
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
                catch (SecurityException securityException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
                }
                catch (DirectoryNotFoundException directoryNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
                }
                catch (FileNotFoundException fileNotFoundException)
                {
                    Tracer.Instance.TraceEvent(TraceEventType.Error, 0, fileNotFoundException.Message);
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

        #endregion
        #region WriteFileAllBytes
        
        public static bool WriteFileAllBytes(string path, byte[] bytes)
        {
            string methodName = StackTraceHelper.GetMethod(StackTraceCaller.This)
                                                .Name;
            bool logicalOperation = TraceHelper.StartLogicalOperation(methodName);

            var output = false;

            try
            {
                File.WriteAllBytes(path, bytes);
                output = true;

                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Write complete {0}", path));
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
            catch (SecurityException securityException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, securityException.Message);
            }
            catch (DirectoryNotFoundException directoryNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, directoryNotFoundException.Message);
            }
            catch (FileNotFoundException fileNotFoundException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, fileNotFoundException.Message);
            }
            catch (IOException ioException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, ioException.Message);
            }
            catch (NotSupportedException notSupportedException)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, notSupportedException.Message);
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