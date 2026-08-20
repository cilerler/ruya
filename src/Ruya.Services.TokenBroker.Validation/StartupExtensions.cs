using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Ruya.Services.TokenBroker.Validation;

namespace Ruya.Services.TokenBroker;

public static class ValidationStartupExtensions
{
    /// <summary>
    /// Adds JWT Bearer authentication for validating tokens with public RSA keys only.
    /// </summary>
    public static IServiceCollection AddTokenValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddTokenValidationCore(services, configureSettings: null);
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
        return AddTokenValidationCore(services, configureSettings);
    }

    private static IServiceCollection AddTokenValidationCore(
        IServiceCollection services,
        Action<TokenValidationSettings>? configureSettings)
    {
        var settingsBuilder = services.AddOptions<TokenValidationSettings>()
            .BindConfiguration(TokenValidationSettings.ConfigurationSectionName);
        if (configureSettings is not null)
        {
            settingsBuilder.Configure(configureSettings);
        }
        settingsBuilder.ValidateDataAnnotations().ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<TokenValidationSettings>, ILoggerFactory>((options, settingsOptions, loggerFactory) =>
            {
                var settings = settingsOptions.Value;
                var keys = RsaPublicKeyFactory.Create(settings.SigningPublicKeys);

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudiences = settings.Audiences,
                    ValidateLifetime = true,
                    ClockSkew = settings.ClockSkew,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    TryAllIssuerSigningKeys = false,
                    IssuerSigningKeyResolver = (_, _, keyId, _) =>
                        keyId is not null && keys.TryGetValue(keyId, out var key)
                            ? [key]
                            : []
                };

                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        loggerFactory.CreateLogger<JwtBearerHandler>().JwtAuthenticationFailed();
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }
}
