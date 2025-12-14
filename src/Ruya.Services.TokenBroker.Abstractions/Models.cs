using System;
using System.Collections.Generic;
using System.Linq;

namespace Ruya.Services.TokenBroker.Models;

/// <summary>
/// Request to create a new token.
/// </summary>
public sealed record TokenRequest
{
    public required string Subject { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<string>? Roles { get; init; }
    public IReadOnlyList<string>? Scopes { get; init; }
    public TimeSpan? CustomLifetime { get; init; }
    public IDictionary<string, string>? AdditionalClaims { get; init; }
}

/// <summary>
/// Request to exchange an existing token for a new one (on-behalf-of flow).
/// </summary>
public sealed record TokenExchangeRequest
{
    /// <summary>
    /// The original token to exchange.
    /// </summary>
    public required string OriginalToken { get; init; }

    /// <summary>
    /// The service performing the exchange (acting party).
    /// </summary>
    public required string ActorService { get; init; }

    /// <summary>
    /// Optionally narrow the scopes. If null, inherits original scopes.
    /// </summary>
    public IReadOnlyList<string>? NarrowedScopes { get; init; }

    /// <summary>
    /// Custom lifetime for the exchanged token. If null, uses default.
    /// </summary>
    public TimeSpan? CustomLifetime { get; init; }
}

/// <summary>
/// Response containing the generated token.
/// </summary>
public sealed record TokenResponse
{
    public required string AccessToken { get; init; }
    public required string TokenType { get; init; }
    public required int ExpiresIn { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required string Subject { get; init; }
    public ActorChain? Actor { get; init; }
    public IReadOnlyList<string>? Scopes { get; init; }
}

/// <summary>
/// Represents a nested actor chain per RFC 8693.
/// </summary>
public sealed record ActorChain
{
    public required string Subject { get; init; }
    public ActorChain? Actor { get; init; }

    /// <summary>
    /// Returns the full chain as a list from outermost to innermost actor.
    /// </summary>
    public IReadOnlyList<string> ToList()
    {
        var result = new List<string> { Subject };
        var current = Actor;
        while (current is not null)
        {
            result.Add(current.Subject);
            current = current.Actor;
        }
        return result;
    }

    /// <summary>
    /// Creates an ActorChain from a list of subjects (outermost first).
    /// </summary>
    /// <param name="subjects">The subjects in the chain, from outermost to innermost.</param>
    /// <returns>An ActorChain representing the provided subjects.</returns>
    /// <exception cref="ArgumentNullException">Thrown when subjects is null.</exception>
    /// <exception cref="ArgumentException">Thrown when subjects is empty or contains null/whitespace values.</exception>
    public static ActorChain FromList(IEnumerable<string> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        var list = subjects.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("At least one subject required.", nameof(subjects));
        }

        if (list.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Subjects cannot contain null or whitespace values.", nameof(subjects));
        }

        // Build the chain from innermost (last element) to outermost (first element)
        var chain = new ActorChain { Subject = list[^1], Actor = null };
        for (var i = list.Count - 2; i >= 0; i--)
        {
            chain = new ActorChain { Subject = list[i], Actor = chain };
        }
        return chain;
    }

    /// <summary>
    /// Attempts to parse an ActorChain from JSON, supporting both current and legacy formats.
    /// </summary>
    /// <param name="json">JSON string representing the actor chain.</param>
    /// <returns>The parsed ActorChain, or null if parsing fails.</returns>
    public static ActorChain? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        // Try current ActorChain format first: { "subject": "...", "actor": { ... } }
        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<ActorChain>(json, Constants.JsonSerializerOptions);
            if (result?.Subject is not null)
            {
                return result;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall through to legacy format
        }

        // Try legacy single-actor format: { "sub": "service-name" }
        try
        {
            var legacy = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json, Constants.JsonSerializerOptions);
            if (legacy.TryGetProperty("sub", out var sub) && sub.GetString() is { } subject)
            {
                return new ActorChain { Subject = subject };
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Unable to parse in any supported format
        }

        return null;
    }

    /// <summary>
    /// Serializes the ActorChain to JSON.
    /// </summary>
    public string ToJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(this, Constants.JsonSerializerOptions);
    }
}

/// <summary>
/// Represents a registered service with its API key.
/// </summary>
public sealed record ServiceRegistration
{
    public required string ServiceName { get; init; }
    public required string ApiKeyHash { get; init; }
    public IReadOnlyList<string>? AllowedScopes { get; init; }
    public bool CanExchangeTokens { get; init; } = true;
}

/// <summary>
/// API request model for creating a new token.
/// </summary>
public sealed record CreateTokenApiRequest
{
    public string? Name { get; init; }
    public List<string>? Roles { get; init; }
    public List<string>? Scopes { get; init; }
    public int? LifetimeMinutes { get; init; }
}

/// <summary>
/// API request model for exchanging a token.
/// </summary>
public sealed record ExchangeTokenApiRequest
{
    public required string Token { get; init; }
    public List<string>? NarrowedScopes { get; init; }
    public int? LifetimeMinutes { get; init; }
}

/// <summary>
/// API request model for validating a token.
/// </summary>
public sealed record ValidateTokenApiRequest
{
    public required string Token { get; init; }
}

/// <summary>
/// Result of token validation.
/// </summary>
public sealed record TokenValidationResult
{
    public bool IsValid { get; init; }
    public string? Subject { get; init; }
    public ActorChain? ActorChain { get; init; }
    public IReadOnlyList<string>? Scopes { get; init; }
    public IReadOnlyList<string>? Roles { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the immediate actor (the service that last exchanged the token).
    /// </summary>
    public string? ImmediateActor => ActorChain?.Subject;

    /// <summary>
    /// Gets the full actor chain as a list from outermost to innermost.
    /// </summary>
    public IReadOnlyList<string>? ActorChainList => ActorChain?.ToList();
}
