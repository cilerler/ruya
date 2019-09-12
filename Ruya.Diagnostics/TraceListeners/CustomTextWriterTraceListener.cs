using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime;
using System.Security.Permissions;
using System.Text;

namespace Ruya.Diagnostics.TraceListeners
{
#warning Refactor
    [HostProtection(SecurityAction.LinkDemand, Synchronization = true)]
    public class CustomTextWriterTraceListener : TraceListener
    {
        public virtual int RollSize { set; get; }
        private TextWriter _internalWriter;
        private string _fileName;
        private string _fileNameOriginal;


        protected CustomTextWriterTraceListener()
        {
        }

        protected CustomTextWriterTraceListener(Stream stream) : this(stream, string.Empty)
        {
        }

        protected CustomTextWriterTraceListener(Stream stream, string name) : base(name)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }
            _internalWriter = new StreamWriter(stream);
        }

        protected CustomTextWriterTraceListener(TextWriter writer) : this(writer, string.Empty)
        {
        }

        protected CustomTextWriterTraceListener(TextWriter writer, string name) : base(name)
        {
            if (writer == null)
            {
                throw new ArgumentNullException("writer");
            }
            _internalWriter = writer;
        }

        [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")]
        protected CustomTextWriterTraceListener(string fileName)
        {
            _fileName = fileName;
        }

        [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")]
        protected CustomTextWriterTraceListener(string fileName, string name) : base(name)
        {
            _fileName = fileName;
        }

        public TextWriter Writer
        {
            get
            {
                EnsureWriter();
                return _internalWriter;
            }
            [TargetedPatchingOptOut("Performance critical to inline this type of method across NGen image boundaries")]
            set
            {
                _internalWriter = value;
            }
        }

        public override void Close()
        {
            if (_internalWriter != null)
            {
                try
                {
                    _internalWriter.Close();
                }
                catch (ObjectDisposedException)
                {
                }
            }
            _internalWriter = null;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    Close();
                }
                else
                {
                    if (_internalWriter != null)
                    {
                        try
                        {
                            _internalWriter.Close();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }
                    _internalWriter = null;
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        public override void Flush()
        {
            if (!EnsureWriter())
            {
                return;
            }
            try
            {
                _internalWriter.Flush();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public override void Write(string message)
        {
            if (!EnsureWriter())
            {
                return;
            }
            if (NeedIndent)
            {
                WriteIndent();
            }
            try
            {
                _internalWriter.Write(message);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public override void WriteLine(string message)
        {
            if (!EnsureWriter())
            {
                return;
            }
            if (NeedIndent)
            {
                WriteIndent();
            }
            try
            {
                _internalWriter.WriteLine(message);
                NeedIndent = true;
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private bool EnsureWriter()
        {
            if (_fileName != null)
            {
                if (_fileName != GenerateFileName())
                {
                    _fileName = GenerateFileName();
                    Close();
                }
            }

            bool flag = true;
            if (_internalWriter != null)
            {
                // ReSharper disable ConditionIsAlwaysTrueOrFalse
                return flag;
                // ReSharper restore ConditionIsAlwaysTrueOrFalse
            }

            flag = false;
            if (_fileName == null)
            {
                // ReSharper disable ConditionIsAlwaysTrueOrFalse
                return flag;
                // ReSharper restore ConditionIsAlwaysTrueOrFalse
            }

            Encoding encodingWithFallback = GetEncodingWithFallback(new UTF8Encoding(false));
            string path = Path.GetFullPath(_fileName);
            string directoryName = Path.GetDirectoryName(path);
            if (directoryName == null)
            {
                // ReSharper disable ConditionIsAlwaysTrueOrFalse
                return flag;
                // ReSharper restore ConditionIsAlwaysTrueOrFalse
            }

            string path2 = Path.GetFileName(path);
            for (int index = 0; index < 2; ++index)
            {
                try
                {
                    _internalWriter = new StreamWriter(path, true, encodingWithFallback, 4096);
                    flag = true;
                    break;
                }
                catch (IOException)
                {
                    path2 = Guid.NewGuid() + path2;
                    path = Path.Combine(directoryName, path2);
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }
            }
            if (!flag)
            {
                _fileName = null;
            }
            return flag;
        }

        private string GenerateFileName()
        {
            if (string.IsNullOrWhiteSpace(_fileNameOriginal))
            {
                _fileNameOriginal = _fileName;
            }
            string directoryName = Path.GetDirectoryName(_fileNameOriginal) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_fileNameOriginal);
            string extension = Path.GetExtension(_fileNameOriginal);
            string date = "_" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string fileName = fileNameWithoutExtension + date + extension;
            string path = Path.Combine(directoryName, fileName);

            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > RollSize * 0.9)
                {
                    //TODO calculate the next message size and make sure it will not exceed it
                    string time = "." + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);
                    string updatedPath = Path.ChangeExtension(path, time);
                    Close();
                    File.Move(path, updatedPath);
                }
            }

            return path;
        }

        private static Encoding GetEncodingWithFallback(Encoding encoding)
        {
            var encoding1 = (Encoding)encoding.Clone();
            encoding1.EncoderFallback = EncoderFallback.ReplacementFallback;
            encoding1.DecoderFallback = DecoderFallback.ReplacementFallback;
            return encoding1;
        }
    }

}
