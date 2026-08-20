using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Ruya.Diagnostics.DistributedTracing;

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis.Unit.Tests;

[TestClass]
public sealed class DataProtectionServiceTests
{
	private static readonly string[] ExpectedPurposeChains = ["a;b|c", "a|b;c"];

	[TestMethod]
	public void Protect_DifferentPurposeChainsWithSameDelimitedText_UsesDistinctProtectors()
	{
		var provider = new RecordingDataProtectionProvider();
		using var harness = CreateService(provider);

		_ = harness.Service.Protect("first", ["a;b", "c"]);
		_ = harness.Service.Protect("second", ["a", "b;c"]);

		CollectionAssert.AreEquivalent(
			ExpectedPurposeChains,
			provider.ProtectedPurposeChains.ToArray());
	}

	[TestMethod]
	public void Protect_BlankPurpose_ThrowsArgumentException()
	{
		using var harness = CreateService(new RecordingDataProtectionProvider());

		Assert.ThrowsExactly<ArgumentException>(() => harness.Service.Protect("content", [" "]));
	}

	private static DataProtectionServiceHarness CreateService(IDataProtectionProvider provider)
	{
		var tracing = new Mock<IDistributedTracing>();
		var meterFactory = new TestMeterFactory();
		var service = new DataProtectionService(
			NullLogger<DataProtectionService>.Instance,
			tracing.Object,
			meterFactory,
			Options.Create(new DataProtectionSettings
			{
				ApplicationName = "tests",
				ConnectionStringKey = "Redis",
				CacheKey = "data-protection"
			}), provider);
		return new DataProtectionServiceHarness(service, meterFactory);
	}
}

public sealed class DataProtectionServiceHarness(
	DataProtectionService service,
	TestMeterFactory meterFactory) : IDisposable
{
	public DataProtectionService Service { get; } = service;

	public void Dispose() => meterFactory.Dispose();
}

public sealed class RecordingDataProtectionProvider : IDataProtectionProvider
{
	public ConcurrentQueue<string> ProtectedPurposeChains { get; } = new();

	public IDataProtector CreateProtector(string purpose)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
		return new RecordingDataProtector(this, [purpose]);
	}

	private sealed class RecordingDataProtector(
		RecordingDataProtectionProvider owner,
		IReadOnlyList<string> purposes) : IDataProtector
	{
		public IDataProtector CreateProtector(string purpose)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
			return new RecordingDataProtector(owner, [.. purposes, purpose]);
		}

		public byte[] Protect(byte[] plaintext)
		{
			ArgumentNullException.ThrowIfNull(plaintext);
			owner.ProtectedPurposeChains.Enqueue(string.Join('|', purposes));
			return plaintext.ToArray();
		}

		public byte[] Unprotect(byte[] protectedData)
		{
			ArgumentNullException.ThrowIfNull(protectedData);
			return protectedData.ToArray();
		}
	}
}

public sealed class TestMeterFactory : IMeterFactory
{
	private readonly ConcurrentBag<Meter> _meters = [];

	public Meter Create(MeterOptions options)
	{
		var meter = new Meter(options);
		_meters.Add(meter);
		return meter;
	}

	public void Dispose()
	{
		foreach (var meter in _meters)
		{
			meter.Dispose();
		}
	}
}
