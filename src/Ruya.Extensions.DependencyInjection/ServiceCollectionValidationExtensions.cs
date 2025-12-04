using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Ruya.Extensions.DependencyInjection;

public static class ServiceCollectionValidationExtensions
{
    public static IServiceCollection EnsureServicesRegistered(this IServiceCollection services, params Type[] requiredServices)
    {
        var missing = requiredServices
            .Where(t => !services.Any(sd => sd.ServiceType == t))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required services: {string.Join(", ", missing.Select(t => t.Name))}");
        }

        return services;
    }
}
