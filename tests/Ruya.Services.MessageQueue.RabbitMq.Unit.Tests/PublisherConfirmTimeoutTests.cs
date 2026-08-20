using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ruya.Services.MessageQueue.Abstractions;

namespace Ruya.Services.MessageQueue.RabbitMq.Unit.Tests;

[TestClass]
public sealed class PublisherConfirmTimeoutTests
{
    [TestMethod]
    public async Task WithConfirmsAsync_ConfirmationExceedsDeadline_ThrowsTimeout()
    {
        // Act
        var exception = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            RabbitMQMessageQueue.WithConfirmsAsync(
                cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
                TimeSpan.FromMilliseconds(50),
                CancellationToken.None));

        // Assert
        StringAssert.Contains(exception.Message, "00:00:00.0500000", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task WithConfirmsAsync_CallerCancels_PreservesCancellation()
    {
        // Arrange
        using var callerCancellation = new CancellationTokenSource();
        await callerCancellation.CancelAsync();

        // Act
        var exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            RabbitMQMessageQueue.WithConfirmsAsync(
                cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
                TimeSpan.FromSeconds(5),
                callerCancellation.Token));

        // Assert
        Assert.AreEqual(callerCancellation.Token, exception.CancellationToken);
    }

    [TestMethod]
    public void ShouldWaitForConfirmation_ProviderAndPublishEnableConfirms_ReturnsTrue()
    {
        // Arrange
        var providerOptions = new RabbitMQOptions { UsePublisherConfirms = true };
        var publishOptions = new PublishOptions { WaitForConfirmation = true };

        // Act
        var result = RabbitMQMessageQueue.ShouldWaitForConfirmation(providerOptions, publishOptions);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ShouldWaitForConfirmation_PublishOptsOut_ReturnsFalse()
    {
        // Arrange
        var providerOptions = new RabbitMQOptions { UsePublisherConfirms = true };
        var publishOptions = new PublishOptions { WaitForConfirmation = false };

        // Act
        var result = RabbitMQMessageQueue.ShouldWaitForConfirmation(providerOptions, publishOptions);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ShouldWaitForConfirmation_ProviderDisablesConfirms_ReturnsFalse()
    {
        // Arrange
        var providerOptions = new RabbitMQOptions { UsePublisherConfirms = false };

        // Act
        var result = RabbitMQMessageQueue.ShouldWaitForConfirmation(providerOptions, publishOptions: null);

        // Assert
        Assert.IsFalse(result);
    }
}
