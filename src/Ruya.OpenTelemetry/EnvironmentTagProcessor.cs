using System;
using System.Diagnostics;
using OpenTelemetry;

namespace Ruya.OpenTelemetry;

/// <summary>
/// Adds deployment.environment tag to all activities.
/// </summary>
internal sealed class EnvironmentTagProcessor : BaseProcessor<Activity>
{
    private readonly string _environmentName;

    public EnvironmentTagProcessor(string environmentName)
    {
        _environmentName = environmentName ?? throw new ArgumentNullException(nameof(environmentName));
    }

    public override void OnStart(Activity data)
    {
        data.SetTag("deployment.environment", _environmentName);
    }
}
