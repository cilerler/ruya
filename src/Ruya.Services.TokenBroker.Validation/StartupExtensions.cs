using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ruya.Services.TokenBroker;

public static class ValidationStartupExtensions
{
    /// <summary>
    /// Adds JWT Bearer authentication for validating tokens.
    /// Use this in services that receive and validate tokens from other services.
    /// This is a lightweight configuration that doesn't require Redis or token issuance capabilities.
    /// </summary>
    public static IServiceCollection AddTokenValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<TokenValidationSettings>()
            .BindConfiguration(TokenValidationSettings.ConfigurationSectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<TokenValidationSettings>, ILoggerFactory>((options, settingsOptions, loggerFactory) =>
            {
                var settings = settingsOptions.Value;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudiences = settings.Audiences,
                    ValidateLifetime = true,
                    ClockSkew = settings.ClockSkew,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Convert.FromBase64String(settings.SigningKeyBase64))
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = loggerFactory.CreateLogger<JwtBearerHandler>();
                        logger.LogWarning(context.Exception, "JWT authentication failed");
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Adds JWT Bearer authentication with custom settings configuration.
    /// </summary>
    public static IServiceCollection AddTokenValidation(
        this IServiceCollection services,
        Action<TokenValidationSettings> configureSettings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureSettings);

        services.AddOptions<TokenValidationSettings>()
            .BindConfiguration(TokenValidationSettings.ConfigurationSectionName)
            .Configure(configureSettings)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<TokenValidationSettings>, ILoggerFactory>((options, settingsOptions, loggerFactory) =>
            {
                var settings = settingsOptions.Value;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudiences = settings.Audiences,
                    ValidateLifetime = true,
                    ClockSkew = settings.ClockSkew,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Convert.FromBase64String(settings.SigningKeyBase64))
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = loggerFactory.CreateLogger<JwtBearerHandler>();
                        logger.LogWarning(context.Exception, "JWT authentication failed");
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
