using System.Threading;
using System.Threading.Tasks;

using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Contracts;

public interface IApiKeyValidator
{
    /// <summary>
    /// Validates an API key and returns the associated service registration.
    /// </summary>
    Task<ServiceRegistration?> ValidateApiKeyAsync(string apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers or updates a service's API key.
    /// </summary>
    Task RegisterServiceAsync(ServiceRegistration registration, string apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a service's API key.
    /// </summary>
    Task RemoveServiceAsync(string serviceName, CancellationToken cancellationToken = default);
}
