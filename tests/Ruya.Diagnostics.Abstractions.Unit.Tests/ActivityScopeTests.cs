using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Ruya.Diagnostics.DistributedTracing;

namespace Ruya.Diagnostics.Abstractions.Unit.Tests;

[TestClass]
public sealed class ActivityScopeTests
{
    [TestMethod]
    public void Dispose_ConcurrentCopies_StopAndInvokeCallbackExactlyOnce()
    {
        using var activity = new Activity("copy-safe");
        activity.Start();
        var callbackCount = 0;
        var scope = new ActivityScope(activity, _ => Interlocked.Increment(ref callbackCount));
        var copies = Enumerable.Repeat(scope, 32).ToArray();

        Parallel.ForEach(copies, copy => copy.Dispose());

        Assert.AreEqual(1, callbackCount);
        Assert.IsNull(scope.Activity);
        Assert.AreNotEqual(default, activity.Duration);
    }
}
