using System;
using Microsoft.Extensions.Options;
using Ruya.Services.MessageQueue.Configuration;

namespace Ruya.Services.ReliableMessaging.MessageQueue;

/// <summary>Validates the fallback queue/provider used when an Outbox envelope has no dispatcher name.</summary>
public sealed class MessageQueueDispatcherOptionsValidator : IValidateOptions<MessageQueueDispatcherOptions>
{
	private readonly IOptions<MessageQueueOptions> _messageQueueOptions;

	public MessageQueueDispatcherOptionsValidator(IOptions<MessageQueueOptions> messageQueueOptions)
	{
		ArgumentNullException.ThrowIfNull(messageQueueOptions);
		_messageQueueOptions = messageQueueOptions;
	}

	public ValidateOptionsResult Validate(string? name, MessageQueueDispatcherOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (string.IsNullOrWhiteSpace(options.QueueName))
		{
			return ValidateOptionsResult.Fail("QueueName must identify a configured MessageQueue provider.");
		}

		if (!_messageQueueOptions.Value.Providers.TryGetValue(options.QueueName, out var provider))
		{
			return ValidateOptionsResult.Fail(
				$"QueueName '{options.QueueName}' is not configured in MessageQueue:Providers.");
		}

		return provider.Enabled
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(
				$"QueueName '{options.QueueName}' identifies a disabled MessageQueue provider.");
	}
}
