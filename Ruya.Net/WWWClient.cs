using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using System.Text;

namespace Ruya.Net
{
#warning Refactor
    public class WwwClient
    {
        public enum WwwClientDownloadType
        {
            Head,
            Data,
            String,
            File
        }

        public WwwClient(string address, Encoding encoding, NameValueCollection queryString, bool callDownload, WwwClientDownloadType downloadType, int timeout)
        {
            BaseAddress = address;
            QueryString = queryString ?? new NameValueCollection();
            Encoding = encoding ?? Encoding.Default;
            DownloadType = downloadType;
            CheckHead = false;
            GZip = false;
            Timeout = timeout;
            if (callDownload)
            {
                Download();
            }
        }

        public bool CheckHead { get; set; }
        public WwwClientDownloadType DownloadType { get; set; }
        public bool GZip { get; set; }
        public string UserAgent { get; set; }
        public string BaseAddress { get; set; }
        public Encoding Encoding { get; set; }
        public string ContentEncoding { get; set; }
        public string ContentType { get; set; }
        public string ContentPath { get; set; }
        public string ContentString { get; private set; }
        public Collection<byte> ContentByte { get; private set; }
        public NameValueCollection QueryString { get; }
        public int Timeout { get; set; }

        public void Download()
        {
            using (var client = new WebClientExtended())
            {
                client.BaseAddress = BaseAddress;
                client.QueryString.Clear();
                client.QueryString = QueryString;
                client.Encoding = Encoding;

                // set user agent
                if (!string.IsNullOrWhiteSpace(UserAgent))
                {
                    client.Headers["User-Agent"] = UserAgent;

                    // Create web client simulating IE6.
                    //x client.Headers["User-Agent"] = "Mozilla/4.0 (Compatible; Windows NT 5.1; MSIE 6.0)" + " (compatible; MSIE 6.0; Windows NT 5.1; .NET CLR 1.1.4322; .NET CLR 2.0.50727)";
                    //x client.Headers["User-Agent"] = "Googlebot/2.1 (+http://www.googlebot.com/bot.html)";
                }

                // accept-encoding headers
                if (GZip)
                {
                    client.Headers["Accept-Encoding"] = "gzip";
                }

                client.Timeout = Timeout;

                try
                {
                    // header 
                    if (CheckHead || DownloadType == WwwClientDownloadType.Head)
                    {
                        client.IsHeadOnly = true;

                        ContentByte = new Collection<byte>(new Collection<byte>(client.DownloadData(client.BaseAddress)));

                        client.IsHeadOnly = false;

                        if (DownloadType != WwwClientDownloadType.Head &&
                            client.ResponseHeaders["content-type"].StartsWith(@"text/", StringComparison.Ordinal))
                        {
                            DownloadType = WwwClientDownloadType.String;
                        }
                    }


                    // check for text/html
                    switch (DownloadType)
                    {
                        case WwwClientDownloadType.Data:
                            ContentByte = new Collection<byte>(new Collection<byte>(client.DownloadData(client.BaseAddress)));
                            break;
                        case WwwClientDownloadType.String:
                            ContentString = client.DownloadString(client.BaseAddress);
                            break;
                        case WwwClientDownloadType.File:
                            client.DownloadFile(client.BaseAddress, ContentPath);
                            break;
                        case WwwClientDownloadType.Head:
                            // already received the data as header
                            break;
                    }

                    // Get response header.
                    ContentEncoding = client.ResponseHeaders["Content-Encoding"];

                    // Get content type
                    ContentType = client.ResponseHeaders["content-type"];
                }
                catch (WebException ex)
                {
                    if (ex.Response != null)
                    {
                        var response = (HttpWebResponse) ex.Response;
                        HttpStatusCode statusCode = response.StatusCode;
                        switch (statusCode)
                        {
                            case HttpStatusCode.OK:
                            case HttpStatusCode.Accepted:
                            case HttpStatusCode.Created:
                            case HttpStatusCode.NoContent:
                            case HttpStatusCode.NotFound:
                            case HttpStatusCode.Unauthorized:
                            case HttpStatusCode.Forbidden:
                            case HttpStatusCode.PreconditionFailed:
                            case HttpStatusCode.ServiceUnavailable:
                            case HttpStatusCode.InternalServerError:
                                throw new WebException();
                        }
                    }
                    throw new WebException();
                }
            }
        }
    }
}