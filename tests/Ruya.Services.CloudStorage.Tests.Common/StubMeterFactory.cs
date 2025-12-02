using System.Diagnostics.Metrics;

namespace Ruya.Services.CloudStorage.Tests.Common;

public class StubMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options)
    {
        return new Meter(options);
    }

    public void Dispose()
    {
    }
}
