using System.Collections.Generic;
using System.Diagnostics;
using Ruya.Diagnostics.DistributedTracing;

namespace Ruya.Services.CloudStorage.Tests.Common;

public class StubDistributedTracing : IDistributedTracing
{
    public ActivityScope StartActivity(string activityName, ActivityKind activityKind = ActivityKind.Internal, string? parentId = null, string? cacheKey = null, IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        return new ActivityScope(null);
    }

    public ActivityScope ContinueActivity(string activityName, string cacheKey, ActivityKind activityKind = ActivityKind.Internal, string? fallbackParentId = null, IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        return new ActivityScope(null);
    }

    public ActivityScope CreateLinkedActivity(string activityName, ActivityContext linkedContext, ActivityKind activityKind = ActivityKind.Internal, IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        return new ActivityScope(null);
    }
}
