using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;
using System.Xml;
using Ruya.Configuration.Properties;
using Ruya.Core;
using Ruya.Security;

namespace Ruya.Configuration
{
    public class TripleDesProtectedConfigurationProvider : ProtectedConfigurationProvider
    {
        private string _name;
        public override string Name => _name;

        private KeyValuePair<byte[], byte[]> _secret;
        public string Secret
        {
            get
            {
                using (var tripleDes = new TripleDes())
                {
                    _secret = tripleDes.Secret;
                }
                string output = _secret.Key.ToHex() + Resources.TripleDesProtectedConfigurationProvider_Separator + _secret.Value.ToHex();
                return output;
            }
            private set
            {
                string[] secret = value.Split(Resources.TripleDesProtectedConfigurationProvider_Separator[0]);
                string key = secret[0];
                string iv = secret[1];
                _secret = new KeyValuePair<byte[], byte[]>(HexHelper.HexToByte(key), HexHelper.HexToByte(iv));
            }
        }

        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            _name = name;
            string token = Resources.TripleDesProtectedConfigurationProvider_Token;
            Secret = config[token];
        }

        public override XmlNode Encrypt(XmlNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            XmlNode output = EncryptDecrypt(true, node);
            return output;
        }

        public override XmlNode Decrypt(XmlNode encryptedNode)
        {
            if (encryptedNode == null)
            {
                throw new ArgumentNullException(nameof(encryptedNode));
            }
            XmlNode output = EncryptDecrypt(false, encryptedNode);
            return output;
        }

        private XmlNode EncryptDecrypt(bool encrypt, XmlNode node)
        {
            string data;
            using (var tripleDes = new TripleDes(_secret))
            {
                data = encrypt
                           ? tripleDes.Encrypt(node.OuterXml)
                           : tripleDes.Decrypt(node.InnerText);
            }
            var xmlDoc = new XmlDocument
                         {
                             PreserveWhitespace = true
                         };

            string value = data;
            if (encrypt)
            {
                const string encryptedDataTag = "EncryptedData";
                value = string.Format(CultureInfo.InvariantCulture, Constants.XmlTag, encryptedDataTag, data);
            }

            xmlDoc.LoadXml(value);
            // ReSharper disable once AssignNullToNotNullAttribute
            return xmlDoc.DocumentElement;
        }
    }
}