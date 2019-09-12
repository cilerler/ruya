using System;
using System.Net;

namespace Ruya.Net
{
    public class WebClientExtended : System.Net.WebClient
    {
        public bool IsHeadOnly { get; set; }
        public int Timeout { get; set; }

        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            if (request != null)
            {
                bool timeoutAssigned = Timeout != 0;
                if (timeoutAssigned)
                {
                    request.Timeout = Timeout;
                }
                if (IsHeadOnly)
                {
                    request.Method = WebRequestMethods.Http.Head;
                }
            }
            return request;
        }
    }
}