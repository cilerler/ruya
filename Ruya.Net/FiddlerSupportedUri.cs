using System;

namespace Ruya.Net
{
    public class FiddlerSupportedUri
    {
        private const string FiddlerSuffix = ".fiddler";

        public FiddlerSupportedUri()
        {
            Address = new UriBuilder().Uri;
        }

        public FiddlerSupportedUri(int port) : this()
        {
            var leanAddress = new UriBuilder(Address)
                              {
                                  Port = port
                              };
            Address = leanAddress.Uri;
        }

        public FiddlerSupportedUri(Uri address)
        {
            Address = address;
        }

        public FiddlerSupportedUri(string address)
        {
            Address = new Uri(address);
        }

        public Uri Address { get; set; }
        public bool IsFiddlerSafe => Address.Host.Contains(FiddlerSuffix);

        public FiddlerSupportedUri AddFiddlerSupport()
        {
            // ReSharper disable once InvertIf
            if (!IsFiddlerSafe &&
                Address.IsLoopback)
            {
                var leanAddress = new UriBuilder(Address);
                leanAddress.Host = leanAddress.Host + FiddlerSuffix;
                Address = leanAddress.Uri;
            }
            return this;
        }

        public FiddlerSupportedUri SetPort(int port)
        {
            var leanAddress = new UriBuilder(Address)
                              {
                                  Port = port
                              };
            Address = leanAddress.Uri;
            return this;
        }

        public override string ToString()
        {
            return Address.ToString();
        }
    }
}