using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Checks if the principal has all required scopes.
    /// </summary>
    /// <param name="principal">The claims principal to check.</param>
    /// <param name="requiredScopes">The scopes that must all be present.</param>
    /// <returns>True if all required scopes are present; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when requiredScopes is null.</exception>
    public static bool HasAllScopes(this ClaimsPrincipal principal, params string[] requiredScopes)
    {
        ArgumentNullException.ThrowIfNull(requiredScopes);
        var userScopes = GetScopes(principal);
        return requiredScopes.All(userScopes.Contains);
    }

    /// <summary>
    /// Checks if the principal has any of the specified scopes.
    /// </summary>
    /// <param name="principal">The claims principal to check.</param>
    /// <param name="scopes">The scopes to check for (any match is sufficient).</param>
    /// <returns>True if any of the specified scopes are present; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when scopes is null.</exception>
    public static bool HasAnyScope(this ClaimsPrincipal principal, params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var userScopes = GetScopes(principal);
        return scopes.Any(userScopes.Contains);
    }

    /// <summary>
    /// Gets all scopes from the principal.
    /// </summary>
    public static IReadOnlySet<string> GetScopes(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindAll(Constants.ScopeClaimType)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the subject (sub) claim.
    /// </summary>
    public static string? GetSubject(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// Gets the full actor chain if present (on-behalf-of flow).
    /// Returns null if the claim is missing or cannot be parsed.
    /// </summary>
    public static ActorChain? GetActorChain(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var actorClaim = principal.FindFirst(Constants.ActorClaimType)?.Value;
        return ActorChain.TryParse(actorClaim);
    }

    /// <summary>
    /// Gets the immediate actor (the service that last exchanged the token).
    /// </summary>
    public static string? GetImmediateActor(this ClaimsPrincipal principal)
    {
        return principal.GetActorChain()?.Subject;
    }

    /// <summary>
    /// Gets the full actor chain as a list from outermost to innermost.
    /// Example: ["service-n", "service-2", "service-1"] means service-1 called service-2 called service-n.
    /// </summary>
    public static IReadOnlyList<string> GetActorChainList(this ClaimsPrincipal principal)
    {
        return principal.GetActorChain()?.ToList() ?? [];
    }

    /// <summary>
    /// Gets the original actor (the first service in the chain, closest to the subject).
    /// </summary>
    public static string? GetOriginalActor(this ClaimsPrincipal principal)
    {
        var chain = principal.GetActorChainList();
        return chain.Count > 0 ? chain[^1] : null;
    }

    /// <summary>
    /// Checks if this token was obtained via token exchange (has an actor).
    /// </summary>
    public static bool IsOnBehalfOf(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirst(Constants.ActorClaimType) is not null;
    }

    /// <summary>
    /// Gets the original subject if this is an exchanged token.
    /// </summary>
    public static string? GetOriginalSubject(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirst(Constants.OriginalSubjectClaimType)?.Value
            ?? principal.GetSubject();
    }

    /// <summary>
    /// Checks if a specific service is in the actor chain.
    /// </summary>
    /// <param name="principal">The claims principal to check.</param>
    /// <param name="serviceName">The service name to search for in the actor chain.</param>
    /// <returns>True if the service is in the actor chain; otherwise, false.</returns>
    /// <exception cref="ArgumentException">Thrown when serviceName is null or whitespace.</exception>
    public static bool HasActorInChain(this ClaimsPrincipal principal, string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        var chain = principal.GetActorChainList();
        return chain.Contains(serviceName, StringComparer.OrdinalIgnoreCase);
    }
}
