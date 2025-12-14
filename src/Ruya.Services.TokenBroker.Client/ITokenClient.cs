using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.TokenBroker.Client;

public interface ITokenClient
{
    /// <summary>
    /// Gets a token for the configured service, using cached token if available and valid.
    /// </summary>
    /// <param name="scopes">Optional scopes to request.</param>
    /// <param name="forceRefresh">If true, bypasses cache and requests a new token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The access token string.</returns>
    Task<string> GetTokenAsync(string[]? scopes = null, bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges an existing token for a new one with actor information (on-behalf-of flow).
    /// Exchanged tokens are not cached since they are specific to each incoming request.
    /// </summary>
    /// <param name="originalToken">The token to exchange.</param>
    /// <param name="narrowedScopes">Optional scopes to narrow to (must be subset of original).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new access token string.</returns>
    Task<string> ExchangeTokenAsync(string originalToken, string[]? narrowedScopes = null, CancellationToken cancellationToken = default);
}
