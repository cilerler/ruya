using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security;
using System.Threading.Tasks;
using Ruya.Diagnostics;

namespace Ruya.Net
{
    // TEST class WebClient
    // COMMENT class WebClient
    public class WebClient
    {
        // TODO Implement overwrite control feature for remote addresses

        private bool _timeoutModified;
        private bool Proxy { get; set; }
        private int Timeout { get; set; }
        private string WebHeaderAccept { get; set; }
        private string RemoteAddress { get; set; }
        private string LocalAddress { get; set; }
        private bool Overwrite { get; set; }
        private bool FileExist { get; set; }
        private NetworkCredential Credentials { get; set; }

        public WebClient ChangeAcceptHeader(string header)
        {
            WebHeaderAccept = header;
            return this;
        }

        public WebClient Local(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                }
                FileExist = File.Exists(path);
                if (FileExist)
                {
                    // HARD-CODED constant
                    Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "File already exist {0}", path));
                }
                LocalAddress = path;
            }
            catch (IOException ioe)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, string.Format(CultureInfo.InvariantCulture, "{0} {1}", path, ioe.Message));
            }
            catch (UnauthorizedAccessException uae)
            {
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, string.Format(CultureInfo.InvariantCulture, "{0} {1}", path, uae.Message));
            }
            catch (Exception e)
            {
                throw new NetException(string.Empty, e);
            }
            return this;
        }

        /// <summary>
        ///     Only available for download operations
        /// </summary>
        /// <returns></returns>
        public WebClient EnableOverwrite()
        {
            Overwrite = true;
            return this;
        }

        /// <summary>
        ///     Must be complete address e.g. ftp://www.microsoft.com/_DEV0221300/Test.pdf
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public WebClient Remote(string path)
        {
            RemoteAddress = path;
            return this;
        }

        public WebClient UseProxy()
        {
            Proxy = true;
            return this;
        }

        public WebClient SetTimeout(TimeSpan interval)
        {
            int output;
            if (int.TryParse(interval.TotalMilliseconds.ToString(CultureInfo.InvariantCulture), out output))
            {
                Timeout = output;
                _timeoutModified = true;
            }
            return this;
        }

        public WebClient SetCredentials(string userName, string password)
        {
            Credentials = new NetworkCredential(userName, password);
            return this;
        }

        public WebClient SetCredentials(string userName, SecureString password)
        {
            Credentials = new NetworkCredential(userName, password);
            return this;
        }

        #region Helper methods

        private static void ProcessTask(Task operation, string diagnosticMessage)
        {
            if (operation.IsCompleted)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Operation completed {0}", diagnosticMessage));
            }
            else if (operation.IsCanceled)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Information, 0, string.Format(CultureInfo.InvariantCulture, "Operation canceled {0}", diagnosticMessage));
            }
            else if (operation.IsFaulted)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, string.Format(CultureInfo.InvariantCulture, "Operation failed {0}", diagnosticMessage));
            }
            else
            {
                // HARD-CODED constant
                throw new NotImplementedException(string.Format(CultureInfo.InvariantCulture, "Operation completed without information {0}", operation.Status));
            }
        }

        public async Task<string> DownloadStringAsync()
        {
            // HARD-CODED constant
            string diagnosticMessage = string.Format(CultureInfo.InvariantCulture, "from {0} to {1}", RemoteAddress, LocalAddress);
            string result = string.Empty;

            bool isDownloadRestricted = string.IsNullOrEmpty(RemoteAddress);
            if (isDownloadRestricted)
            {
                // TODO provide more details about the issue
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Download arguments are not correct {0}", diagnosticMessage));
                return result;
            }

            try
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Downloading {0}", diagnosticMessage));
                Task<string> operation = DownloadStringTaskAsync();
                result = await operation;
                ProcessTask(operation, diagnosticMessage);

                if (operation.IsCompleted &&
                    !string.IsNullOrEmpty(LocalAddress))
                {
                    IO.FileHelper.WriteFileAllText(LocalAddress, result);
                }
            }
            catch (Exception ex)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Critical, 0, string.Format(CultureInfo.InvariantCulture, "{0} occured while downloading {1}", ex.Message, LocalAddress));
            }
            return result;
        }

        public async Task DownloadFileAsync()
        {
                // HARD-CODED constant
            string diagnosticMessage = string.Format(CultureInfo.InvariantCulture, "from {0} to {1}", RemoteAddress, LocalAddress);

            bool isDownloadRestricted = (!Overwrite && FileExist) || string.IsNullOrEmpty(LocalAddress) || string.IsNullOrEmpty(RemoteAddress);
            if (isDownloadRestricted)
            {
                // HARD-CODED constant
                // TODO provide more details about the issue
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, string.Format(CultureInfo.InvariantCulture, "Arguments are not correct {0}", diagnosticMessage));
                return;
            }

            try
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Starting to download {0}", diagnosticMessage));
                Task operation = DownloadFileTaskAsync();
                await operation;
                ProcessTask(operation, diagnosticMessage);
            }
            catch (Exception ex)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Critical, 0, string.Format(CultureInfo.InvariantCulture, "{0} occured while downloading {1}", ex.Message, diagnosticMessage));
            }
        }

        public async Task<byte[]> UploadFileAsync()
        {
                // HARD-CODED constant
            string diagnosticMessage = string.Format(CultureInfo.InvariantCulture, "from {0} to {1}", LocalAddress, RemoteAddress);
            byte[] result =
            {
            };

            bool isUploadRestricted = !FileExist || string.IsNullOrEmpty(LocalAddress) || string.IsNullOrEmpty(RemoteAddress);
            if (isUploadRestricted)
            {
                // HARD-CODED constant
                // TODO provide more details about the issue
                Tracer.Instance.TraceEvent(TraceEventType.Error, 0, string.Format(CultureInfo.InvariantCulture, "Arguments are not correct {0}", diagnosticMessage));
                return result;
            }

            try
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Starting to upload {0}", diagnosticMessage));
                Task<byte[]> operation = UploadFileTaskAsync();
                result = await operation;
                ProcessTask(operation, diagnosticMessage);
            }
            catch (Exception ex)
            {
                // HARD-CODED constant
                Tracer.Instance.TraceEvent(TraceEventType.Critical, 0, string.Format(CultureInfo.InvariantCulture, "{0} occured while uploading {1}", ex.Message, diagnosticMessage));
            }
            return result;
        }

        #endregion

        #region Action methods

        #region Common methods

        private static void ProgressUpdate(int progressPercentage, long bytesReceived, long totalBytesToReceive, long bytesSent, long totalBytesToSent)
        {
                // HARD-CODED constant
            Tracer.Instance.TraceEvent(TraceEventType.Verbose, 0, string.Format(CultureInfo.InvariantCulture, "Progress {0} % complete.  Bytes received {1} of {2}.  Bytes sent {3} of {4}", progressPercentage, bytesReceived, totalBytesToReceive, bytesSent, totalBytesToSent));
        }

        private void CommonOperation(WebClientExtended client)
        {
            if (Proxy)
            {
                client.Proxy = new WebProxy();
            }

            if (Credentials != null)
            {
                client.Credentials = Credentials;
            }

            if (_timeoutModified)
            {
                client.Timeout = Timeout;
            }

            if (!string.IsNullOrWhiteSpace(WebHeaderAccept))
            {
                client.Headers["Accept"] = WebHeaderAccept;
            }
        }

        private void CommonOperationDownload(WebClientExtended client)
        {
            CommonOperation(client);
            client.DownloadProgressChanged += (sender, args) =>
                                              {
                                                  ProgressUpdate(args.ProgressPercentage, args.BytesReceived, args.TotalBytesToReceive, 0, 0);
                                              };
        }

        private void CommonOperationUpload(WebClientExtended client)
        {
            CommonOperation(client);
            client.UploadProgressChanged += (sender, args) =>
                                            {
                                                ProgressUpdate(args.ProgressPercentage, args.BytesReceived, args.TotalBytesToReceive, args.BytesSent, args.TotalBytesToSend);
                                            };
        }

        #endregion

        private Task<byte[]> UploadFileTaskAsync()
        {
            Task<byte[]> task;
            using (var client = new WebClientExtended())
            {
                CommonOperationUpload(client);
                task = client.UploadFileTaskAsync(RemoteAddress, LocalAddress);
            }
            return task;
        }

        private Task DownloadFileTaskAsync()
        {
            Task task;
            using (var client = new WebClientExtended())
            {
                CommonOperationDownload(client);
                task = client.DownloadFileTaskAsync(RemoteAddress, LocalAddress);
            }
            return task;
        }

        private Task<string> DownloadStringTaskAsync()
        {
            Task<string> task;
            using (var client = new WebClientExtended())
            {
                CommonOperationDownload(client);
                task = client.DownloadStringTaskAsync(RemoteAddress);
            }
            return task;
        }

        #endregion
    }
}