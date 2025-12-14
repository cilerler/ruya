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
        app.MapGroup("/api/token")
           .WithTags("TokenBroker")
           .MapCreateToken()
           .MapExchangeToken()
           .MapValidateToken();

        return app;
    }

    private static RouteGroupBuilder MapCreateToken(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            [FromHeader(Name = Constants.ApiKeyHeader)] string apiKey,
            [FromHeader(Name = Constants.ServiceNameHeader)] string serviceName,
            [FromBody] CreateTokenApiRequest request,
            [FromServices] ITokenBroker tokenBroker,
            [FromServices] IApiKeyValidator apiKeyValidator,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger(LoggerCategories.Api);
            try
            {
                var registration = await apiKeyValidator.ValidateApiKeyAsync(apiKey, cancellationToken);
                if (registration is null)
                {
                    logger.LogWarning(LogEvents.TokenCreationRejected, "Token creation rejected: invalid API key for claimed service {ServiceName}", serviceName);
                    return Results.Unauthorized();
                }

                // Verify service name matches registration
                if (!string.Equals(registration.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(LogEvents.ServiceNameMismatch, "Token creation rejected: service name mismatch. Claimed: {ClaimedService}, Registered: {RegisteredService}",
                        serviceName, registration.ServiceName);
                    return Results.Unauthorized();
                }

                // Validate token lifetime if specified
                if (request.LifetimeMinutes.HasValue)
                {
                    if (request.LifetimeMinutes.Value < 1 || request.LifetimeMinutes.Value > TokenBrokerSettings.MaxAllowedLifetimeMinutes)
                    {
                        logger.LogWarning(LogEvents.TokenCreationRejected,
                            "Token creation rejected: service {ServiceName} requested invalid lifetime {LifetimeMinutes} minutes",
                            serviceName, request.LifetimeMinutes.Value);
                        return Results.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid lifetime",
                            Detail = $"Token lifetime must be between 1 and {TokenBrokerSettings.MaxAllowedLifetimeMinutes} minutes."
                        });
                    }
                }

                // Verify requested scopes are allowed
                if (request.Scopes is not null && registration.AllowedScopes is not null)
                {
                    var disallowedScopes = request.Scopes.Except(registration.AllowedScopes).ToList();
                    if (disallowedScopes.Count > 0)
                    {
                        logger.LogWarning(LogEvents.DisallowedScopes, "Token creation rejected: service {ServiceName} requested disallowed scopes: {DisallowedScopes}",
                            serviceName, string.Join(", ", disallowedScopes));
                        return Results.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid scopes",
                            Detail = $"Service not allowed to request scopes: {string.Join(", ", disallowedScopes)}"
                        });
                    }
                }

                var tokenRequest = new TokenRequest
                {
                    Subject = serviceName,
                    Name = request.Name,
                    Roles = request.Roles,
                    Scopes = request.Scopes,
                    CustomLifetime = request.LifetimeMinutes.HasValue
                        ? TimeSpan.FromMinutes(request.LifetimeMinutes.Value)
                        : null
                };

                var response = await tokenBroker.CreateTokenAsync(tokenRequest, cancellationToken);

                logger.LogInformation(LogEvents.TokenCreated, "Token created for service {ServiceName} with scopes [{Scopes}], expires {ExpiresAt}",
                    serviceName, string.Join(", ", request.Scopes ?? []), response.ExpiresAt);

                return Results.Ok(response);
            }
            catch (TokenBrokerException ex)
            {
                logger.LogWarning(LogEvents.TokenCreationFailed, ex, "Token creation failed for service {ServiceName}: {ErrorCode}",
                    serviceName, ex.ErrorCode);
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("CreateToken")
        .Produces<TokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static RouteGroupBuilder MapExchangeToken(this RouteGroupBuilder group)
    {
        group.MapPost("/exchange", async (
            [FromHeader(Name = Constants.ApiKeyHeader)] string apiKey,
            [FromHeader(Name = Constants.ServiceNameHeader)] string serviceName,
            [FromBody] ExchangeTokenApiRequest request,
            [FromServices] ITokenBroker tokenBroker,
            [FromServices] IApiKeyValidator apiKeyValidator,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger(LoggerCategories.Api);
            try
            {
                var registration = await apiKeyValidator.ValidateApiKeyAsync(apiKey, cancellationToken);
                if (registration is null)
                {
                    logger.LogWarning(LogEvents.TokenExchangeRejected, "Token exchange rejected: invalid API key for claimed service {ServiceName}", serviceName);
                    return Results.Unauthorized();
                }

                if (!string.Equals(registration.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(LogEvents.ServiceNameMismatch, "Token exchange rejected: service name mismatch. Claimed: {ClaimedService}, Registered: {RegisteredService}",
                        serviceName, registration.ServiceName);
                    return Results.Unauthorized();
                }

                if (!registration.CanExchangeTokens)
                {
                    logger.LogWarning(LogEvents.TokenExchangeRejected, "Token exchange rejected: service {ServiceName} is not allowed to exchange tokens", serviceName);
                    return Results.Problem(
                        $"Service '{serviceName}' is not allowed to exchange tokens",
                        statusCode: StatusCodes.Status403Forbidden);
                }

                // Validate token lifetime if specified
                if (request.LifetimeMinutes.HasValue)
                {
                    if (request.LifetimeMinutes.Value < 1 || request.LifetimeMinutes.Value > TokenBrokerSettings.MaxAllowedLifetimeMinutes)
                    {
                        logger.LogWarning(LogEvents.TokenExchangeRejected,
                            "Token exchange rejected: service {ServiceName} requested invalid lifetime {LifetimeMinutes} minutes",
                            serviceName, request.LifetimeMinutes.Value);
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
                    CustomLifetime = request.LifetimeMinutes.HasValue
                        ? TimeSpan.FromMinutes(request.LifetimeMinutes.Value)
                        : null
                };

                var response = await tokenBroker.ExchangeTokenAsync(exchangeRequest, cancellationToken);

                logger.LogInformation(LogEvents.TokenExchanged, "Token exchanged by service {ServiceName} for subject {Subject}, actor chain: [{ActorChain}]",
                    serviceName, response.Subject, string.Join(" -> ", response.Actor?.ToList() ?? []));

                return Results.Ok(response);
            }
            catch (InvalidTokenException ex)
            {
                logger.LogWarning(LogEvents.TokenExchangeFailed, ex, "Token exchange failed for service {ServiceName}: invalid token", serviceName);
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (TokenExchangeNotAllowedException ex)
            {
                logger.LogWarning(LogEvents.TokenExchangeFailed, ex, "Token exchange failed for service {ServiceName}: exchange not allowed", serviceName);
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
        })
        .WithName("ExchangeToken")
        .Produces<TokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    private static RouteGroupBuilder MapValidateToken(this RouteGroupBuilder group)
    {
        group.MapPost("/validate", async (
            [FromBody] ValidateTokenApiRequest request,
            [FromServices] ITokenBroker tokenBroker,
            CancellationToken cancellationToken) =>
        {
            var result = await tokenBroker.ValidateTokenAsync(request.Token, cancellationToken);
            return result.IsValid
                ? Results.Ok(result)
                : Results.BadRequest(result);
        })
        .WithName("ValidateToken")
        .Produces<TokenValidationResult>(StatusCodes.Status200OK)
        .Produces<TokenValidationResult>(StatusCodes.Status400BadRequest);

        return group;
    }
}
