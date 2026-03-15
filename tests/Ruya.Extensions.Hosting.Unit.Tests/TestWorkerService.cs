using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Extensions.Hosting;

namespace Ruya.Extensions.Hosting.Unit.Tests;

public class TestWorkerSettings : WorkerBackgroundServiceSettings
{
}

public class TestWorkerService : WorkerBackgroundService<TestWorkerSettings>
{
    public int ExecutionCount { get; private set; }
    public Func<CancellationToken, Task>? DoWorkAction { get; set; }

    public TestWorkerService(
        ILogger<TestWorkerService> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<TestWorkerSettings> options,
        IEnumerable<IHealthCheck> healthChecks)
        : base(logger, distributedTracing, meterFactory, options, healthChecks)
    {
    }

    public void SetIdleCycle(bool value) => IdleCycle = value;

    public override Task DoWorkAsync(CancellationToken cancellationToken)
    {
        ExecutionCount++;
        if (DoWorkAction != null)
        {
            return DoWorkAction(cancellationToken);
        }
        return Task.CompletedTask;
    }
}
