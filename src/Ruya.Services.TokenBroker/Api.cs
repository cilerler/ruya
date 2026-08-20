using System;
using System.Linq;
using System.Threading;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ruya.Services.TokenBroker.Contracts;
using Ruya.Services.TokenBroker.Exceptions;
using Ruya.Services.TokenBroker.Models;

namespace Ruya.Services.TokenBroker;

public static class Api
{
    public static WebApplication MapTokenBrokerApi(this WebApplication app)
    {
        app.MapGroup("/api/v1/token")
           .WithTags("TokenBroker")
           .MapCreateToken(string.Empty)
           .MapExchangeToken(string.Empty)
           .MapValidateToken(string.Empty);

        var legacyGroup = app.MapGroup("/api/token")
            .WithTags("TokenBroker (legacy)")
            .ExcludeFromDescription();
        legacyGroup.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers["Deprecation"] = "true";
            return await next(context);
        });
        legacyGroup
            .MapCreateToken("Legacy")
            .MapExchangeToken("Legacy")
            .MapValidateToken("Legacy");

        return app;
    }

    private static RouteGroupBuilder MapCreateToken(this RouteGroupBuilder group, string nameSuffix)
    {
        group.MapPost("/", async (
            [FromHeader(Name = Constants.ApiKeyHeader)] string? apiKey,
            [FromHeader(Name = Constants.ServiceNameHeader)] string? serviceName,
            [FromBody] CreateTokenApiRequest request,
            [FromServices] ITokenBroker tokenBroker,
            [FromServices] IApiKeyValidator apiKeyValidator,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger(LoggerCategories.Api);
            try
            {
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(serviceName))
                {
                    return Results.Unauthorized();
                }

                var registration = await apiKeyValidator.ValidateApiKeyAsync(apiKey, cancellationToken);
                if (registration is null)
                {
                    logger.TokenCreationRejected(serviceName, "invalid API key");
                    return Results.Unauthorized();
                }

                // Verify service name matches registration
                if (!string.Equals(registration.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                {
                    logger.ServiceNameMismatch(serviceName, registration.ServiceName);
                    return Results.Unauthorized();
                }

                // Validate token lifetime if specified
                if (request.LifetimeMinutes.HasValue)
                {
                    if (request.LifetimeMinutes.Value < 1 || request.LifetimeMinutes.Value > TokenBrokerSettings.MaxAllowedLifetimeMinutes)
                    {
                        logger.TokenCreationRejected(serviceName, "invalid lifetime");
                        return Results.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid lifetime",
                            Detail = $"Token lifetime must be between 1 and {TokenBrokerSettings.MaxAllowedLifetimeMinutes} minutes."
                        });
                    }
                }

                // Verify requested scopes are allowed
                if (request.Scopes is not null)
                {
                    var disallowedScopes = request.Scopes.Except(registration.AllowedScopes ?? [], StringComparer.Ordinal).ToList();
                    if (disallowedScopes.Count > 0)
                    {
                        logger.DisallowedScopes(serviceName, disallowedScopes.Count);
                        return Results.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid scopes",
                            Detail = $"Service not allowed to request scopes: {string.Join(", ", disallowedScopes)}"
                        });
                    }
                }

                if (request.Roles is not null)
                {
                    var disallowedRoles = request.Roles.Except(registration.AllowedRoles ?? [], StringComparer.Ordinal).ToList();
                    if (disallowedRoles.Count > 0)
                    {
                        logger.TokenCreationRejected(serviceName, "roles outside registration");
                        return Results.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid roles",
                            Detail = "The request contains roles that are not authorized for this service."
                        });
                    }
                }

                var tokenRequest = new TokenRequest
                {
                    Subject = serviceName,
                    Name = request.Name,
                    Roles = request.Roles,
                    AllowedRoles = registration.AllowedRoles,
                    Scopes = request.Scopes,
                    CustomLifetime = request.LifetimeMinutes.HasValue
                        ? TimeSpan.FromMinutes(request.LifetimeMinutes.Value)
                        : null
                };

                var response = await tokenBroker.CreateTokenAsync(tokenRequest, cancellationToken);

                logger.TokenCreatedForService(serviceName, request.Scopes?.Count ?? 0, response.ExpiresAtUtc);

                return Results.Ok(response);
            }
            catch (TokenBrokerException ex)
            {
                logger.TokenCreationFailed(serviceName ?? "(missing)", ex.ErrorCode);
                return Results.Problem("Token creation was rejected.", statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName($"CreateToken{nameSuffix}")
        .WithMetadata(new RequestSizeLimitAttribute(64 * 1024))
        .Produces<TokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static RouteGroupBuilder MapExchangeToken(this RouteGroupBuilder group, string nameSuffix)
    {
        group.MapPost("/exchange", async (
            [FromHeader(Name = Constants.ApiKeyHeader)] string? apiKey,
            [FromHeader(Name = Constants.ServiceNameHeader)] string? serviceName,
            [FromBody] ExchangeTokenApiRequest request,
            [FromServices] ITokenBroker tokenBroker,
            [FromServices] IApiKeyValidator apiKeyValidator,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger(LoggerCategories.Api);
            try
            {
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(serviceName))
                {
                    return Results.Unauthorized();
                }

                var registration = await apiKeyValidator.ValidateApiKeyAsync(apiKey, cancellationToken);
                if (registration is null)
                {
                    logger.TokenExchangeRejected(serviceName, "invalid API key");
                    return Results.Unauthorized();
                }

                if (!string.Equals(registration.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                {
                    logger.ServiceNameMismatch(serviceName, registration.ServiceName);
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request.Token))
                {
                    return Results.BadRequest(new ProblemDetails
                    {
                        Title = "Invalid token",
                        Detail = "A token is required for exchange."
                    });
                }

                if (!registration.CanExchangeTokens)
                {
                    logger.TokenExchangeRejected(serviceName, "exchange not allowed");
                    return Results.Problem(
                        $"Service '{serviceName}' is not allowed to exchange tokens",
                        statusCode: StatusCodes.Status403Forbidden);
                }

                // Validate token lifetime if specified
                if (request.LifetimeMinutes.HasValue)
                {
                    if (request.LifetimeMinutes.Value < 1 || request.LifetimeMinutes.Value > TokenBrokerSettings.MaxAllowedLifetimeMinutes)
                    {
                        logger.TokenExchangeRejected(serviceName, "invalid lifetime");
                        return Results.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid lifetime",
                            Detail = $"Token lifetime must be between 1 and {TokenBrokerSettings.MaxAllowedLifetimeMinutes} minutes."
                        });
                    }
                }

                var exchangeRequest = new TokenExchangeRequest
                {
                    OriginalToken = request.Token,
                    ActorService = serviceName,
                    NarrowedScopes = request.NarrowedScopes,
                    ActorAllowedScopes = registration.AllowedScopes,
                    CustomLifetime = request.LifetimeMinutes.HasValue
                        ? TimeSpan.FromMinutes(request.LifetimeMinutes.Value)
                        : null
                };

                var response = await tokenBroker.ExchangeTokenAsync(exchangeRequest, cancellationToken);

                logger.TokenExchangedForService(
                    serviceName,
                    response.ExpiresAtUtc);

                return Results.Ok(response);
            }
            catch (InvalidTokenException)
            {
                logger.TokenExchangeFailed(serviceName ?? "(missing)", "invalid token");
                return Results.Problem("Token exchange was rejected.", statusCode: StatusCodes.Status400BadRequest);
            }
            catch (TokenExchangeNotAllowedException)
            {
                logger.TokenExchangeFailed(serviceName ?? "(missing)", "exchange not allowed");
                return Results.Problem("Token exchange is not allowed for this service.", statusCode: StatusCodes.Status403Forbidden);
            }
        })
        .WithName($"ExchangeToken{nameSuffix}")
        .WithMetadata(new RequestSizeLimitAttribute(64 * 1024))
        .Produces<TokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static RouteGroupBuilder MapValidateToken(this RouteGroupBuilder group, string nameSuffix)
    {
        group.MapPost("/validate", async (
            [FromHeader(Name = Constants.ApiKeyHeader)] string? apiKey,
            [FromHeader(Name = Constants.ServiceNameHeader)] string? serviceName,
            [FromBody] ValidateTokenApiRequest request,
            [FromServices] ITokenBroker tokenBroker,
            [FromServices] IApiKeyValidator apiKeyValidator,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(serviceName))
            {
                return Results.Unauthorized();
            }

            var registration = await apiKeyValidator.ValidateApiKeyAsync(apiKey, cancellationToken);
            if (registration is null
                || !string.Equals(registration.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return Results.BadRequest(new TokenValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Token validation failed."
                });
            }

            var result = await tokenBroker.ValidateTokenAsync(request.Token, cancellationToken);
            return result.IsValid
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .WithName($"ValidateToken{nameSuffix}")
        .WithMetadata(new RequestSizeLimitAttribute(64 * 1024))
        .Produces<TokenValidationResult>(StatusCodes.Status200OK)
        .Produces<TokenValidationResult>(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        return group;
    }
}
