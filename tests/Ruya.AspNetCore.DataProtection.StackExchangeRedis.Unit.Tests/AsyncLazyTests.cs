using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Ruya.AspNetCore.DataProtection.StackExchangeRedis.Unit.Tests;

[TestClass]
public class AsyncLazyTests
{
	#region Constructor Tests

	[TestMethod]
	public void Constructor_NullFactory_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new AsyncLazy<string>(null!));
	}

	[TestMethod]
	public void Constructor_ValidFactory_DoesNotThrow()
	{
		// Act & Assert - should not throw
		var lazy = new AsyncLazy<string>(() => Task.FromResult("test"));
		Assert.IsNotNull(lazy);
	}

	#endregion

	#region IsValueCreated Tests

	[TestMethod]
	public void IsValueCreated_BeforeAccess_ReturnsFalse()
	{
		// Arrange
		var lazy = new AsyncLazy<string>(() => Task.FromResult("test"));

		// Act & Assert
		Assert.IsFalse(lazy.IsValueCreated);
	}

	[TestMethod]
	public async Task IsValueCreated_AfterValueAccessed_ReturnsTrue()
	{
		// Arrange
		var lazy = new AsyncLazy<string>(() => Task.FromResult("test"));

		// Act
		_ = await lazy.Value;

		// Assert
		Assert.IsTrue(lazy.IsValueCreated);
	}

	[TestMethod]
	public async Task IsValueCreated_WhileTaskRunning_ReturnsFalse()
	{
		// Arrange
		var tcs = new TaskCompletionSource<string>();
		var lazy = new AsyncLazy<string>(() => tcs.Task);

		// Act - start the task but don't complete it
		var valueTask = lazy.Value;

		// Assert - should be false while task is running
		Assert.IsFalse(lazy.IsValueCreated);

		// Cleanup
		tcs.SetResult("test");
		await valueTask;
	}

	[TestMethod]
	public async Task IsValueCreated_AfterTaskFailed_ReturnsFalse()
	{
		// Arrange
		var lazy = new AsyncLazy<string>(() => Task.FromException<string>(new InvalidOperationException("test error")));

		// Act
		try
		{
			_ = await lazy.Value;
		}
		catch (InvalidOperationException)
		{
			// Expected
		}

		// Assert - failed tasks should not be considered "created"
		Assert.IsFalse(lazy.IsValueCreated);
	}

	#endregion

	#region Value Tests

	[TestMethod]
	public async Task Value_ReturnsFactoryResult()
	{
		// Arrange
		const string expected = "test value";
		var lazy = new AsyncLazy<string>(() => Task.FromResult(expected));

		// Act
		var result = await lazy.Value;

		// Assert
		Assert.AreEqual(expected, result);
	}

	[TestMethod]
	public async Task Value_CalledMultipleTimes_ReturnsSameInstance()
	{
		// Arrange
		var callCount = 0;
		var lazy = new AsyncLazy<object>(() =>
		{
			callCount++;
			return Task.FromResult(new object());
		});

		// Act
		var result1 = await lazy.Value;
		var result2 = await lazy.Value;
		var result3 = await lazy.Value;

		// Assert
		Assert.AreSame(result1, result2);
		Assert.AreSame(result2, result3);
		Assert.AreEqual(1, callCount, "Factory should only be called once");
	}

	[TestMethod]
	public async Task Value_FactoryThrows_PropagatesException()
	{
		// Arrange
		var expectedException = new InvalidOperationException("test error");
		var lazy = new AsyncLazy<string>(() => Task.FromException<string>(expectedException));

		// Act & Assert
		var actualException = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
			() => lazy.Value);

		Assert.AreSame(expectedException, actualException);
	}

	[TestMethod]
	public async Task Value_AsyncFactory_ExecutesAsynchronously()
	{
		// Arrange
		var executionOrder = new List<int>();
		var lazy = new AsyncLazy<string>(async () =>
		{
			executionOrder.Add(1);
			await Task.Delay(10);
			executionOrder.Add(2);
			return "test";
		});

		// Act
		executionOrder.Add(0);
		var valueTask = lazy.Value;
		executionOrder.Add(3);
		await valueTask;
		executionOrder.Add(4);

		// Assert - 0 should be before 1, 3 might be before or after 1 depending on timing
		Assert.AreEqual(0, executionOrder[0]);
		Assert.AreEqual(4, executionOrder[^1]);
	}

	#endregion

	#region ValueOrDefault Tests

	[TestMethod]
	public void ValueOrDefault_BeforeAccess_ReturnsDefault()
	{
		// Arrange
		var lazy = new AsyncLazy<string>(() => Task.FromResult("test"));

		// Act
		var result = lazy.ValueOrDefault;

		// Assert
		Assert.IsNull(result);
	}

	[TestMethod]
	public async Task ValueOrDefault_AfterValueAccessed_ReturnsValue()
	{
		// Arrange
		const string expected = "test value";
		var lazy = new AsyncLazy<string>(() => Task.FromResult(expected));

		// Act
		_ = await lazy.Value;
		var result = lazy.ValueOrDefault;

		// Assert
		Assert.AreEqual(expected, result);
	}

	[TestMethod]
	public void ValueOrDefault_WhileTaskRunning_ReturnsDefault()
	{
		// Arrange
		var tcs = new TaskCompletionSource<string>();
		var lazy = new AsyncLazy<string>(() => tcs.Task);

		// Act - start the task but don't complete it
		_ = lazy.Value;
		var result = lazy.ValueOrDefault;

		// Assert
		Assert.IsNull(result);

		// Cleanup
		tcs.SetResult("test");
	}

	[TestMethod]
	public void ValueOrDefault_ForValueType_ReturnsDefaultValue()
	{
		// Arrange
		var lazy = new AsyncLazy<int>(() => Task.FromResult(42));

		// Act
		var result = lazy.ValueOrDefault;

		// Assert
		Assert.AreEqual(0, result);
	}

	[TestMethod]
	public async Task ValueOrDefault_ForValueType_AfterAccess_ReturnsValue()
	{
		// Arrange
		const int expected = 42;
		var lazy = new AsyncLazy<int>(() => Task.FromResult(expected));

		// Act
		_ = await lazy.Value;
		var result = lazy.ValueOrDefault;

		// Assert
		Assert.AreEqual(expected, result);
	}

	#endregion

	#region Thread Safety Tests

	[TestMethod]
	public async Task Value_ConcurrentAccess_FactoryCalledOnce()
	{
		// Arrange
		var callCount = 0;
		var lazy = new AsyncLazy<string>(async () =>
		{
			Interlocked.Increment(ref callCount);
			await Task.Delay(50);
			return "test";
		});

		// Act - access value from multiple threads concurrently
		var tasks = Enumerable.Range(0, 10)
			.Select(_ => Task.Run(async () => await lazy.Value))
			.ToArray();

		await Task.WhenAll(tasks);

		// Assert
		Assert.AreEqual(1, callCount, "Factory should only be called once even with concurrent access");
	}

	[TestMethod]
	public async Task Value_ConcurrentAccess_AllGetSameResult()
	{
		// Arrange
		var lazy = new AsyncLazy<object>(() => Task.FromResult(new object()));

		// Act
		var tasks = Enumerable.Range(0, 10)
			.Select(_ => Task.Run(async () => await lazy.Value))
			.ToArray();

		var results = await Task.WhenAll(tasks);

		// Assert - all results should be the same instance
		var first = results[0];
		Assert.IsTrue(results.All(r => ReferenceEquals(r, first)));
	}

	#endregion
}
