using System;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace Ruya.Slack
{
    public class Client
    {
        private readonly Encoding _encoding = new UTF8Encoding();
        private readonly Uri _uri;

        public Client(string urlWithAccessToken)
        {
            _uri = new Uri(urlWithAccessToken);
        }

        public void PostMessage(string text, string iconEmoji = null, string username = null, string channel = null)
        {
            var payload = new Payload
                          {
                              Channel = channel,
                              Username = username,
                              Text = text
                          };
            if (iconEmoji != null)
            {
                payload.IconEmoji = iconEmoji;
            }

            PostMessage(payload);
        }

        public string PostMessage(Payload payload)
        {
            string output;
            string payloadJson = JsonConvert.SerializeObject(payload);
            using (var client = new WebClient())
            {
                var data = new NameValueCollection
                           {
                               ["payload"] = payloadJson
                           };

                byte[] response = client.UploadValues(_uri, "POST", data);

                //The response text is usually "ok"
                output = _encoding.GetString(response);
            }
            return output;
        }
    }
}