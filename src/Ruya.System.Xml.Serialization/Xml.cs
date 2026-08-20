using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace Ruya.System.Xml.Serialization;

[SuppressMessage("Naming", "CA1724", Justification = "The released Xml type name is retained for 8.x binary compatibility.")]
public static class Xml
{
    private static readonly ConcurrentDictionary<(Type, string?), Lazy<XmlSerializer>> SerializerCache = new();

    public static T Deserialize<T>(string xml, string? rootElementName = null)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var serializer = GetSerializer<T>(rootElementName);
        using var textReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(textReader, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });

        return (T)serializer.Deserialize(xmlReader)!;
    }

    public static string Serialize<T>(T source, string? rootElementName = null, bool omitXmlDeclaration = true)
    {
        ArgumentNullException.ThrowIfNull(source);

        var serializer = GetSerializer<T>(rootElementName);
        var namespaces = new XmlSerializerNamespaces([XmlQualifiedName.Empty]);

        using var stringWriter = new StringWriter();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = omitXmlDeclaration,
            Indent = false
        };

        using var xmlWriter = XmlWriter.Create(stringWriter, settings);
        serializer.Serialize(xmlWriter, source, namespaces);

        return stringWriter.ToString();
    }

    private static XmlSerializer GetSerializer<T>(string? rootElementName)
    {
        var key = (typeof(T), rootElementName);
        return SerializerCache.GetOrAdd(key, static k => new Lazy<XmlSerializer>(() =>
        {
            if (string.IsNullOrWhiteSpace(k.Item2))
                return new XmlSerializer(k.Item1);

            var root = new XmlRootAttribute { ElementName = k.Item2, IsNullable = true };
            return new XmlSerializer(k.Item1, root);
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }
}
