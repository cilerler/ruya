using System;
using Microsoft.Extensions.DependencyInjection;

namespace Ruya.Services.ReliableMessaging.Extensions;

internal sealed class ReliableMessagingBuilder : IReliableMessagingBuilder
{
	public ReliableMessagingBuilder(IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		Services = services;
	}

	public IServiceCollection Services { get; }
}
