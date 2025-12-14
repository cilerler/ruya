using System;
using Microsoft.Extensions.DependencyInjection;

namespace Ruya.AspNetCore.Diagnostics.GlobalExceptionHandler;

public static class StartupExtensions
{
	public static IServiceCollection AddGlobalExceptionHandlerService(this IServiceCollection serviceCollection)
	{
		ArgumentNullException.ThrowIfNull(serviceCollection);
		serviceCollection.AddProblemDetails().AddExceptionHandler<GlobalExceptionHandlerService>();
		return serviceCollection;
	}
}
