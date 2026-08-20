using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ruya.System.Xml.Serialization.Unit.Tests;

[TestClass]
public sealed class XmlTests
{
    [TestMethod]
    public void Deserialize_ExternalEntityDocument_RejectsDtdProcessing()
    {
        const string payload = """
            <!DOCTYPE sample [<!ENTITY secret SYSTEM "file:///does-not-matter">]>
            <Sample><Value>&secret;</Value></Sample>
            """;

        Assert.ThrowsExactly<InvalidOperationException>(() => Xml.Deserialize<XmlTestSample>(payload));
    }

    [TestMethod]
    public void SerializeThenDeserialize_ValidObject_RoundTripsValues()
    {
        var source = new XmlTestSample { Value = "safe" };

        var serialized = Xml.Serialize(source);
        var result = Xml.Deserialize<XmlTestSample>(serialized);

        Assert.AreEqual(source.Value, result.Value);
    }

    [TestMethod]
    public void Deserialize_NullXml_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Xml.Deserialize<XmlTestSample>(null!));
    }

    [TestMethod]
    public void Serialize_NullSource_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => Xml.Serialize<XmlTestSample>(null!));
    }
}

public sealed class XmlTestSample
{
    public string? Value { get; set; }
}
