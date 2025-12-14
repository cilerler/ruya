using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Ruya.Services.TokenBroker.Contracts;
using Ruya.Services.TokenBroker.Exceptions;
using Ruya.Services.TokenBroker.Models;

using TokenValidationResult = Ruya.Services.TokenBroker.Models.TokenValidationResult;

namespace Ruya.Services.TokenBroker;

public sealed class TokenBroker : ITokenBroker
{
    private static readonly ActivitySource ActivitySource = new(MetricConstants.MeterName, "1.0.0");

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
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(meterFactory);

        _logger = logger;
        _settings = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _tokenHandler = new JwtSecurityTokenHandler();

        var securityKey = new SymmetricSecurityKey(Convert.FromBase64String(_settings.SigningKeyBase64));
        _signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudiences = _settings.Audiences,
            ValidateLifetime = true,
            ClockSkew = _settings.ClockSkew,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey
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

        using var activity = ActivitySource.StartActivity("CreateToken", ActivityKind.Internal);
        activity?.SetTag("token.subject", request.Subject);
        activity?.SetTag("token.scopes_count", request.Scopes?.Count ?? 0);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var lifetime = request.CustomLifetime ?? _settings.TokenLifetime;
            var expires = now.Add(lifetime);
            var notBefore = now.Subtract(_settings.ClockSkew);
            var jti = Guid.NewGuid().ToString("N");

            var claims = BuildClaims(request, jti);
            var token = CreateJwtToken(claims, notBefore, expires);

            _tokensCreatedCounter.Add(1);
            _logger.TokenCreated(jti, request.Subject, expires);

            activity?.SetTag("token.jti", jti);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Task.FromResult(new TokenResponse
            {
                AccessToken = token,
                TokenType = "Bearer",
                ExpiresIn = (int)lifetime.TotalSeconds,
                ExpiresAt = expires,
                Subject = request.Subject,
                Scopes = request.Scopes?.ToList()
            });
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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

        using var activity = ActivitySource.StartActivity("ExchangeToken", ActivityKind.Internal);
        activity?.SetTag("token.actor_service", request.ActorService);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate and parse original token
            ClaimsPrincipal principal;
            try
            {
                principal = _tokenHandler.ValidateToken(
                    request.OriginalToken,
                    _validationParameters,
                    out _);
            }
            catch (SecurityTokenException ex)
            {
                _tokenValidationFailuresCounter.Add(1);
                activity?.SetStatus(ActivityStatusCode.Error, "Token validation failed");
                throw new InvalidTokenException("Token validation failed", ex);
            }

            var originalSubject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(originalSubject))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Missing subject claim");
                throw new InvalidTokenException("Original token missing subject claim");
            }

            activity?.SetTag("token.original_subject", originalSubject);

            // Get original scopes and narrow if requested
            var originalScopes = principal.FindAll(Constants.ScopeClaimType)
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToList();

            var finalScopes = request.NarrowedScopes?.ToList() ?? originalScopes;

            // Verify narrowed scopes are subset of original
            if (request.NarrowedScopes is not null)
            {
                var invalidScopes = finalScopes.Except(originalScopes).ToList();
                if (invalidScopes.Count > 0)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Scope elevation attempted");
                    throw new InvalidTokenException(
                        $"Cannot elevate scopes. Invalid: {string.Join(", ", invalidScopes)}");
                }
            }

            // Build nested actor chain
            ActorChain newActorChain;
            var existingActorClaim = principal.FindFirst(Constants.ActorClaimType)?.Value;

            if (existingActorClaim is not null)
            {
                // Parse existing chain and prepend new actor
                var existingChain = ActorChain.TryParse(existingActorClaim);
                newActorChain = new ActorChain
                {
                    Subject = request.ActorService,
                    Actor = existingChain
                };
            }
            else
            {
                // First exchange - create new chain
                newActorChain = new ActorChain { Subject = request.ActorService };
            }

            activity?.SetTag("token.actor_chain_depth", newActorChain.ToList().Count);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var lifetime = request.CustomLifetime ?? _settings.TokenLifetime;
            var expires = now.Add(lifetime);
            var notBefore = now.Subtract(_settings.ClockSkew);
            var jti = Guid.NewGuid().ToString("N");

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, originalSubject),
                new(JwtRegisteredClaimNames.Jti, jti),
                new(Constants.OriginalSubjectClaimType, originalSubject)
            };

            // Add nested actor claim as JSON object per RFC 8693
            claims.Add(new Claim(Constants.ActorClaimType, newActorChain.ToJson(), JsonClaimValueTypes.Json));

            if (finalScopes.Count > 0)
            {
                claims.Add(new Claim(Constants.ScopeClaimType, string.Join(' ', finalScopes)));
            }

            foreach (var audience in _settings.Audiences)
            {
                claims.Add(new Claim(JwtRegisteredClaimNames.Aud, audience));
            }

            var token = CreateJwtToken(claims, notBefore, expires);

            _tokensExchangedCounter.Add(1);
            _logger.TokenExchanged(jti, originalSubject, request.ActorService, expires);

            activity?.SetTag("token.jti", jti);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Task.FromResult(new TokenResponse
            {
                AccessToken = token,
                TokenType = "Bearer",
                ExpiresIn = (int)lifetime.TotalSeconds,
                ExpiresAt = expires,
                Subject = originalSubject,
                Actor = newActorChain,
                Scopes = finalScopes
            });
        }
        catch (Exception ex) when (ex is not InvalidTokenException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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

        using var activity = ActivitySource.StartActivity("ValidateToken", ActivityKind.Internal);
        _tokenValidationsCounter.Add(1);

        try
        {
            var principal = _tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken);

            var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var actorClaim = principal.FindFirst(Constants.ActorClaimType)?.Value;
            var actorChain = ActorChain.TryParse(actorClaim);

            var scopes = principal.FindAll(Constants.ScopeClaimType)
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToList();

            var roles = principal.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            activity?.SetTag("token.subject", subject);
            activity?.SetTag("token.is_exchanged", actorChain is not null);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return Task.FromResult(new TokenValidationResult
            {
                IsValid = true,
                Subject = subject,
                ActorChain = actorChain,
                Scopes = scopes,
                Roles = roles,
                ExpiresAt = validatedToken.ValidTo
            });
        }
        catch (Exception ex)
        {
            _tokenValidationFailuresCounter.Add(1);
            _logger.TokenValidationFailed(ex);
            activity?.SetStatus(ActivityStatusCode.Error, "Token validation failed");

            return Task.FromResult(new TokenValidationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message
            });
        }
    }

    private List<Claim> BuildClaims(TokenRequest request, string jti)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.Subject),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        if (!string.IsNullOrEmpty(request.Name))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Name, request.Name));
        }

        if (request.Roles is { Count: > 0 })
        {
            claims.AddRange(request.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        }

        if (request.Scopes is { Count: > 0 })
        {
            claims.Add(new Claim(Constants.ScopeClaimType, string.Join(' ', request.Scopes)));
        }

        if (request.AdditionalClaims is { Count: > 0 })
        {
            claims.AddRange(request.AdditionalClaims.Select(kv => new Claim(kv.Key, kv.Value)));
        }

        foreach (var audience in _settings.Audiences)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Aud, audience));
        }

        return claims;
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
