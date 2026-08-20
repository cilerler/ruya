using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

namespace Ruya.Services.CloudStorage.UnitTests;

internal sealed class MetricCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private long _sum;

    internal MetricCollector(string meterName, string instrumentName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref _sum, measurement));
        _listener.Start();
    }

    internal long Sum => Interlocked.Read(ref _sum);

    public void Dispose() => _listener.Dispose();
}

internal sealed class FixedMeterFactory(Meter meter) : IMeterFactory
{
    public Meter Create(MeterOptions options) => meter;

    public void Dispose()
    {
    }
}

internal sealed class ThrowingAsyncEnumerator<T>(Exception exception) : IAsyncEnumerator<T>
{
    public T Current => default!;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<bool> MoveNextAsync() => ValueTask.FromException<bool>(exception);
}

internal sealed class ThrowingEnumerable<T>(Exception exception) : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => new ThrowingEnumerator(exception);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private sealed class ThrowingEnumerator(Exception exception) : IEnumerator<T>
    {
        public T Current => default!;

        object IEnumerator.Current => Current!;

        public void Dispose()
        {
        }

        public bool MoveNext() => throw exception;

        public void Reset() => throw new NotSupportedException();
    }
}
