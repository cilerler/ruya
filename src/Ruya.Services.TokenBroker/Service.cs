using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Ruya.Services.TokenBroker.Contracts;
using Ruya.Services.TokenBroker.Exceptions;
using Ruya.Services.TokenBroker.Models;
using Ruya.Services.TokenBroker.Validation;

using TokenValidationResult = Ruya.Services.TokenBroker.Models.TokenValidationResult;

namespace Ruya.Services.TokenBroker;

public sealed class TokenBroker : ITokenBroker
{
    private static readonly ActivitySource ActivitySource = new(MetricConstants.MeterName, "1.0.0");
    private static readonly HashSet<string> ReservedClaimTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        JwtRegisteredClaimNames.Sub,
        JwtRegisteredClaimNames.Jti,
        JwtRegisteredClaimNames.Iss,
        JwtRegisteredClaimNames.Aud,
        JwtRegisteredClaimNames.Exp,
        JwtRegisteredClaimNames.Nbf,
        JwtRegisteredClaimNames.Iat,
        Constants.ActorClaimType,
        Constants.OriginalSubjectClaimType,
        Constants.ScopeClaimType,
        ClaimTypes.Role,
        "role",
        "roles"
    };

    private readonly ILogger<TokenBroker> _logger;
    private readonly TokenBrokerSettings _settings;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _validationParameters;
    private readonly TimeProvider _timeProvider;
    private readonly JwtSecurityTokenHandler _tokenHandler;

    private readonly Counter<long> _tokensCreatedCounter;
    private readonly Counter<long> _tokensExchangedCounter;
    private readonly Counter<long> _tokenValidationsCounter;
    private readonly Counter<long> _tokenValidationFailuresCounter;
    private readonly Histogram<double> _tokenCreationDuration;

    public TokenBroker(
        ILogger<TokenBroker> logger,
        IMeterFactory meterFactory,
        IOptions<TokenBrokerSettings> options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meterFactory);

        _logger = logger;
        _settings = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _tokenHandler = new JwtSecurityTokenHandler
        {
            MaximumTokenSizeInBytes = Constants.Defaults.MaximumTokenSizeInBytes
        };

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_settings.SigningPrivateKeyPem);
        var privateKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: true))
        {
            KeyId = _settings.SigningKeyId
        };
        var publicKeys = RsaPublicKeyFactory.Create(_settings.SigningPublicKeys);

        _signingCredentials = new SigningCredentials(privateKey, SecurityAlgorithms.RsaSha256);
        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudiences = _settings.Audiences,
            ValidateLifetime = true,
            ClockSkew = _settings.ClockSkew,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            TryAllIssuerSigningKeys = false,
            IssuerSigningKeyResolver = (_, _, keyId, _) =>
                keyId is not null && publicKeys.TryGetValue(keyId, out var key)
                    ? [key]
                    : []
        };

        var meter = meterFactory.Create(MetricConstants.MeterName);
        _tokensCreatedCounter = meter.CreateCounter<long>(
            MetricConstants.TokensCreated, "tokens", "Total tokens created");
        _tokensExchangedCounter = meter.CreateCounter<long>(
            MetricConstants.TokensExchanged, "tokens", "Total tokens exchanged");
        _tokenValidationsCounter = meter.CreateCounter<long>(
            MetricConstants.TokenValidations, "validations", "Total token validation attempts");
        _tokenValidationFailuresCounter = meter.CreateCounter<long>(
            MetricConstants.TokenValidationFailures, "failures", "Total token validation failures");
        _tokenCreationDuration = meter.CreateHistogram<double>(
            MetricConstants.TokenCreationDuration, "s", "Token creation duration");
    }

    public Task<TokenResponse> CreateTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject);
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = ActivitySource.StartActivity("CreateToken", ActivityKind.Internal);
        activity?.SetTag("token.scopes_count", request.Scopes?.Count ?? 0);
        activity?.SetTag("token.roles_count", request.Roles?.Count ?? 0);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            EnsureAuthorizedRoles(request.Roles, request.AllowedRoles);
            EnsureNoReservedClaims(request.AdditionalClaims);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var lifetime = GetValidatedLifetime(request.CustomLifetime);
            var expires = now.Add(lifetime);
            var notBefore = now.Subtract(_settings.ClockSkew);
            var claims = BuildClaims(request, Guid.NewGuid().ToString("N"));
            var token = CreateJwtToken(claims, notBefore, expires);

            cancellationToken.ThrowIfCancellationRequested();
            _tokensCreatedCounter.Add(1);
            _logger.TokenCreated(expires);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Task.FromResult(new TokenResponse
            {
                AccessToken = token,
                TokenType = "Bearer",
                ExpiresIn = checked((int)lifetime.TotalSeconds),
                ExpiresAt = expires,
                Subject = request.Subject,
                Scopes = request.Scopes?.Distinct(StringComparer.Ordinal).ToList()
            });
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Token creation failed");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _tokenCreationDuration.Record(stopwatch.Elapsed.TotalSeconds);
        }
    }

    public Task<TokenResponse> ExchangeTokenAsync(TokenExchangeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OriginalToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorService);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTokenSize(request.OriginalToken);

        using var activity = ActivitySource.StartActivity("ExchangeToken", ActivityKind.Internal);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ClaimsPrincipal principal;
            SecurityToken validatedToken;
            try
            {
                principal = _tokenHandler.ValidateToken(request.OriginalToken, _validationParameters, out validatedToken);
            }
            catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
            {
                _tokenValidationFailuresCounter.Add(1);
                activity?.SetStatus(ActivityStatusCode.Error, "Token validation failed");
                throw new InvalidTokenException("Token validation failed", ex);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var originalSubject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(originalSubject))
            {
                throw new InvalidTokenException("Original token missing subject claim");
            }

            var originalScopes = principal.FindAll(Constants.ScopeClaimType)
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var finalScopes = (request.NarrowedScopes ?? originalScopes)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var actorAllowedScopes = request.ActorAllowedScopes ?? [];

            if (finalScopes.Except(originalScopes, StringComparer.Ordinal).Any())
            {
                throw new InvalidTokenException("The exchange request attempted to elevate scopes.");
            }
            if (finalScopes.Except(actorAllowedScopes, StringComparer.Ordinal).Any())
            {
                throw new InvalidTokenException("The actor service is not authorized for the requested scopes.");
            }

            var existingActorClaim = principal.FindFirst(Constants.ActorClaimType)?.Value;
            var existingChain = ActorChain.TryParse(existingActorClaim);
            if (existingActorClaim is not null && existingChain is null)
            {
                throw new InvalidTokenException("The actor chain is malformed.");
            }

            var newActorChain = new ActorChain
            {
                Subject = request.ActorService,
                Actor = existingChain
            };
            var actorDepth = newActorChain.ToList().Count;
            if (actorDepth > Constants.Defaults.MaximumActorChainDepth)
            {
                throw new InvalidTokenException("The actor chain exceeds the supported depth.");
            }
            activity?.SetTag("token.actor_chain_depth", actorDepth);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var lifetime = GetValidatedLifetime(request.CustomLifetime);
            var originalExpiry = DateTime.SpecifyKind(validatedToken.ValidTo, DateTimeKind.Utc);
            var requestedExpiry = now.Add(lifetime);
            var expires = requestedExpiry < originalExpiry ? requestedExpiry : originalExpiry;
            if (expires <= now)
            {
                throw new InvalidTokenException("The original token has no remaining lifetime.");
            }

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, originalSubject),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new(Constants.OriginalSubjectClaimType, originalSubject),
                new(Constants.ActorClaimType, newActorChain.ToJson(), JsonClaimValueTypes.Json)
            };
            if (finalScopes.Count > 0)
            {
                claims.Add(new Claim(Constants.ScopeClaimType, string.Join(' ', finalScopes)));
            }
            AddAudienceClaims(claims);

            var token = CreateJwtToken(claims, now.Subtract(_settings.ClockSkew), expires);
            cancellationToken.ThrowIfCancellationRequested();
            _tokensExchangedCounter.Add(1);
            _logger.TokenExchanged(request.ActorService, expires);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Task.FromResult(new TokenResponse
            {
                AccessToken = token,
                TokenType = "Bearer",
                ExpiresIn = checked((int)(expires - now).TotalSeconds),
                ExpiresAt = expires,
                Subject = originalSubject,
                Actor = newActorChain,
                Scopes = finalScopes
            });
        }
        catch (InvalidTokenException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Token exchange rejected");
            throw;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Token exchange failed");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _tokenCreationDuration.Record(stopwatch.Elapsed.TotalSeconds);
        }
    }

    public Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = ActivitySource.StartActivity("ValidateToken", ActivityKind.Internal);
        _tokenValidationsCounter.Add(1);
        try
        {
            EnsureTokenSize(token);
            var principal = _tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken);
            cancellationToken.ThrowIfCancellationRequested();

            var actorChain = ActorChain.TryParse(principal.FindFirst(Constants.ActorClaimType)?.Value);
            var scopes = principal.FindAll(Constants.ScopeClaimType)
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var roles = principal.FindAll(ClaimTypes.Role)
                .Select(claim => claim.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            activity?.SetTag("token.is_exchanged", actorChain is not null);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return Task.FromResult(new TokenValidationResult
            {
                IsValid = true,
                Subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                ActorChain = actorChain,
                Scopes = scopes,
                Roles = roles,
                ExpiresAt = DateTime.SpecifyKind(validatedToken.ValidTo, DateTimeKind.Utc)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or InvalidTokenException)
        {
            _tokenValidationFailuresCounter.Add(1);
            _logger.TokenValidationFailed();
            activity?.SetStatus(ActivityStatusCode.Error, "Token validation failed");
            return Task.FromResult(new TokenValidationResult
            {
                IsValid = false,
                ErrorMessage = "Token validation failed."
            });
        }
    }

    private static void EnsureAuthorizedRoles(IReadOnlyList<string>? requestedRoles, IReadOnlyList<string>? allowedRoles)
    {
        if (requestedRoles is { Count: > 0 }
            && requestedRoles.Except(allowedRoles ?? [], StringComparer.Ordinal).Any())
        {
            throw new InvalidTokenException("The request contains roles that were not authorized by the issuer.");
        }
    }

    private static void EnsureNoReservedClaims(IDictionary<string, string>? additionalClaims)
    {
        if (additionalClaims?.Keys.Any(ReservedClaimTypes.Contains) == true)
        {
            throw new InvalidTokenException("Additional claims cannot replace issuer-owned claims.");
        }
    }

    private static void EnsureTokenSize(string token)
    {
        if (Encoding.UTF8.GetByteCount(token) > Constants.Defaults.MaximumTokenSizeInBytes)
        {
            throw new InvalidTokenException("The token exceeds the supported size.");
        }
    }

    private TimeSpan GetValidatedLifetime(TimeSpan? requestedLifetime)
    {
        var lifetime = requestedLifetime ?? _settings.TokenLifetime;
        if (lifetime < TimeSpan.FromMinutes(1)
            || lifetime > TimeSpan.FromMinutes(TokenBrokerSettings.MaxAllowedLifetimeMinutes))
        {
            throw new InvalidTokenException("The requested token lifetime is outside the supported range.");
        }

        return lifetime;
    }

    private List<Claim> BuildClaims(TokenRequest request, string jti)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.Subject),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Name, request.Name));
        }
        if (request.Roles is { Count: > 0 })
        {
            claims.AddRange(request.Roles.Distinct(StringComparer.Ordinal).Select(role => new Claim(ClaimTypes.Role, role)));
        }
        if (request.Scopes is { Count: > 0 })
        {
            claims.Add(new Claim(Constants.ScopeClaimType, string.Join(' ', request.Scopes.Distinct(StringComparer.Ordinal))));
        }
        if (request.AdditionalClaims is { Count: > 0 })
        {
            claims.AddRange(request.AdditionalClaims.Select(claim => new Claim(claim.Key, claim.Value)));
        }

        AddAudienceClaims(claims);
        return claims;
    }

    private void AddAudienceClaims(List<Claim> claims)
    {
        foreach (var audience in _settings.Audiences)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Aud, audience));
        }
    }

    private string CreateJwtToken(List<Claim> claims, DateTime notBefore, DateTime expires)
    {
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: _signingCredentials);

        return _tokenHandler.WriteToken(token);
    }
}
