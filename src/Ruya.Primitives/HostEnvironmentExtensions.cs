using Microsoft.Extensions.Hosting;
using System;

namespace Ruya.Primitives;

public static class HostEnvironmentExtensions
{
    public static bool IsIntegration(this IHostEnvironment env) => env.IsEnvironment(Constants.Environments.Integration);

    public static bool IsTesting(this IHostEnvironment env) => env.IsEnvironment(Constants.Environments.Testing);
}
