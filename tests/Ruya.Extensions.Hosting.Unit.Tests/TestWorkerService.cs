using System;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ruya.Diagnostics.DistributedTracing;
using Ruya.Extensions.Hosting;

namespace Ruya.Extensions.Hosting.Unit.Tests;

internal static class TestMeters
{
    public static Meter Create() => new("TestMeter");
}

public sealed class TestWorkerSettings : WorkerBackgroundServiceSettings
{
    public new const string ConfigurationSectionName = "TestWorker";
}

public sealed class TestWorkerService : WorkerBackgroundService<TestWorkerSettings>, IAsyncDisposable
{
    private int _executionCount;

    public int ExecutionCount => Volatile.Read(ref _executionCount);
    public Func<CancellationToken, Task>? DoWorkAction { get; set; }
    public Func<Exception, bool> TransientExceptionPredicate { get; set; } = _ => false;

    public TestWorkerService(
        ILogger<TestWorkerService> logger,
        IDistributedTracing distributedTracing,
        IMeterFactory meterFactory,
        IOptions<TestWorkerSettings> options,
        HealthCheckService healthCheckService,
        IHostApplicationLifetime hostApplicationLifetime)
        : base(
            logger,
            distributedTracing,
            meterFactory,
            options,
            healthCheckService,
            hostApplicationLifetime)
    {
    }

    public void SetIdleCycle(bool value) => IdleCycle = value;

    public bool CurrentIdleCycle => IdleCycle;

    public override Task DoWorkAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _executionCount);
        if (DoWorkAction != null)
        {
            return DoWorkAction(cancellationToken);
        }
        return Task.CompletedTask;
    }

    protected override bool IsTransient(Exception exception) => TransientExceptionPredicate(exception);

    public async ValueTask DisposeAsync()
    {
        await StoppingAsync(CancellationToken.None);
        Dispose();
        GC.SuppressFinalize(this);
    }
}
