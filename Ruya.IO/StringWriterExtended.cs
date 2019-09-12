using System;
using System.IO;
using System.Text;

namespace Ruya.IO
{
    public sealed class StringWriterExtended : StringWriter
    {
        public StringWriterExtended(Encoding encoding, IFormatProvider formatProvider) : base(formatProvider)
        {
            Encoding = encoding;
        }

        public override Encoding Encoding { get; }
    }
}