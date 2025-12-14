using System.Threading;
using System.Threading.Tasks;

using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Contracts;

/// <summary>
/// Service for creating, exchanging, and validating JWT tokens.
/// </summary>
/// <remarks>
/// All methods are async to maintain a consistent interface contract, even though
/// the current implementations are CPU-bound. This design allows for future extensibility
/// such as remote token validation (introspection), distributed signing, or OIDC discovery
/// without breaking changes to the interface.
/// </remarks>
public interface ITokenBroker
{
    /// <summary>
    /// Creates a new JWT token for the specified request.
    /// </summary>
    Task<TokenResponse> CreateTokenAsync(TokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges an existing token for a new one with actor information (on-behalf-of flow).
    /// </summary>
    Task<TokenResponse> ExchangeTokenAsync(TokenExchangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a token and returns the claims if valid.
    /// </summary>
    Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
}
