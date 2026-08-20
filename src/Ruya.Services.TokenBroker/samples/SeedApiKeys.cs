// Development/bootstrap example. API-key values must come from an environment,
// user-secrets, or external secret provider; do not place them in appsettings.json.
// The provisioning owner must invoke this before ApiKeyCacheDuration expires when
// registrations are expected to remain active continuously.

using Microsoft.Extensions.Configuration;

using Ruya.Services.TokenBroker.Contracts;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Samples;

public static class SeedApiKeys
{
    public static async Task RegisterConfiguredServicesAsync(
        IApiKeyValidator validator,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var registrations = configuration
            .GetSection("TokenBrokerBootstrap:Services")
            .Get<List<ServiceBootstrap>>() ?? [];

        foreach (var service in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apiKey = configuration[$"TokenBrokerBootstrap:ApiKeys:{service.ServiceName}"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    $"The secret API key for bootstrap service '{service.ServiceName}' is not configured.");
            }

            await validator.RegisterServiceAsync(
                new ServiceRegistration
                {
                    ServiceName = service.ServiceName,
                    ApiKeyHash = string.Empty, // RegisterServiceAsync computes and stores the hash.
                    AllowedScopes = service.AllowedScopes,
                    AllowedRoles = service.AllowedRoles,
                    CanExchangeTokens = service.CanExchangeTokens
                },
                apiKey,
                cancellationToken);
        }
    }

    private sealed class ServiceBootstrap
    {
        public required string ServiceName { get; init; }
        public IReadOnlyList<string> AllowedScopes { get; init; } = [];
        public IReadOnlyList<string> AllowedRoles { get; init; } = [];
        public bool CanExchangeTokens { get; init; }
    }
}
