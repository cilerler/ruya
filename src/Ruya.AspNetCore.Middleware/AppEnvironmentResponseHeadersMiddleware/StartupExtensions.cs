using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ruya.Extensions.Configuration;

namespace Ruya.AspNetCore.Middleware.AppEnvironmentResponseHeaders;

/// <summary>
/// Extension methods for configuring the AppEnvironmentResponseHeaders middleware.
/// </summary>
public static class StartupExtensions
{
	/// <summary>
	/// Adds the AppEnvironmentResponseHeaders services to the service collection.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="setupAction">Optional action to configure the settings.</param>
	/// <returns>The service collection for chaining.</returns>
	public static IServiceCollection AddAppEnvironmentResponseHeaders(
		this IServiceCollection services,
		Action<AppEnvironmentResponseHeadersSettings>? setupAction = null)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddOptions<AppEnvironmentResponseHeadersSettings>()
			.BindConfiguration(AppEnvironmentResponseHeadersSettings.ConfigurationSectionName)
			.Configure<IConfiguration>((settings, config) =>
			{
				settings.Enabled = config.GetFeatureFlag<AppEnvironmentResponseHeadersSettings>();
			})
			.ValidateDataAnnotations()
			.ValidateOnStart();

		if (setupAction is not null)
		{
			services.Configure(setupAction);
		}

		return services;
	}

	/// <summary>
	/// Adds the AppEnvironmentResponseHeaders middleware to the application pipeline.
	/// </summary>
	/// <param name="builder">The application builder.</param>
	/// <returns>The application builder for chaining.</returns>
	public static IApplicationBuilder UseAppEnvironmentResponseHeaders(this IApplicationBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		return builder.UseMiddleware<AppEnvironmentResponseHeadersMiddleware>();
	}
}
