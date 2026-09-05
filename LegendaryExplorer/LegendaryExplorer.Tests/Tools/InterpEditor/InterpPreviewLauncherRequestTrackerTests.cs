using LegendaryExplorer.Tools.InterpEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewLauncherRequestTrackerTests
{
    [TestMethod]
    public void Request_DefaultsToNoExplicitPlayerVariantOverride()
    {
        var request = new InterpPreviewLauncherRequest(levelPaths: []);

        Assert.IsNull(request.PreferFemalePlayer);
    }

    [TestMethod]
    public void BeginRequest_AdvancesIdentity_AndInvalidatesPrevious()
    {
        int cancelCalls = 0;
        var tracker = new InterpPreviewLauncherRequestTracker(() => Interlocked.Increment(ref cancelCalls));

        long firstRequestId = tracker.BeginRequest();
        long secondRequestId = tracker.BeginRequest();

        Assert.AreEqual(2, cancelCalls);
        Assert.IsTrue(secondRequestId > firstRequestId);
        Assert.IsFalse(tracker.IsCurrent(firstRequestId));
        Assert.IsTrue(tracker.IsCurrent(secondRequestId));
    }

    [TestMethod]
    public async Task IsCurrent_ReturnsFalseForOlderRequest_AfterConcurrentBegin()
    {
        var tracker = new InterpPreviewLauncherRequestTracker(onRequestStarted: null);
        long requestId = tracker.BeginRequest();

        await Task.Run(() => tracker.BeginRequest());

        Assert.IsFalse(tracker.IsCurrent(requestId));
    }
}
