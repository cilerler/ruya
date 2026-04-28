using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.ReliableMessaging.Inbox;

namespace Ruya.Services.ReliableMessaging.Unit.Tests.Inbox;

[TestClass]
[TestCategory("Unit")]
public sealed class TypeNameInboxConsumerNameProviderTests
{
	private sealed class UnannotatedHandler;

	[InboxConsumerName("custom.consumer.name")]
	private sealed class AnnotatedHandler;

	[TestMethod]
	public void GetConsumerName_WithoutAttribute_ReturnsFullTypeName()
	{
		var provider = new TypeNameInboxConsumerNameProvider();

		var name = provider.GetConsumerName(typeof(UnannotatedHandler));

		Assert.AreEqual(typeof(UnannotatedHandler).FullName, name);
	}

	[TestMethod]
	public void GetConsumerName_WithAttribute_ReturnsAttributeValue()
	{
		var provider = new TypeNameInboxConsumerNameProvider();

		var name = provider.GetConsumerName(typeof(AnnotatedHandler));

		Assert.AreEqual("custom.consumer.name", name);
	}

	[TestMethod]
	public void GetConsumerName_WithNullType_ThrowsArgumentNullException()
	{
		var provider = new TypeNameInboxConsumerNameProvider();

		Assert.ThrowsExactly<ArgumentNullException>(() => provider.GetConsumerName(null!));
	}

	[TestMethod]
	public void InboxConsumerNameAttribute_WithEmptyValue_ThrowsArgumentException()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new InboxConsumerNameAttribute(string.Empty));
	}
}
