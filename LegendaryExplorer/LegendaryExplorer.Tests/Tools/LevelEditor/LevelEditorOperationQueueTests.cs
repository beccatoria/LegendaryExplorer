using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class LevelEditorOperationQueueTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestMethod]
    public async Task Appends_AreSerializedUntilWorkerCleanupCompletes()
    {
        var queue = new LevelEditorOperationQueue();
        var entered = Signal();
        var release = Signal();
        var calls = new List<string>();
        var first = queue.RunAsync(false, async _ =>
        {
            calls.Add("first entered");
            entered.SetResult();
            await release.Task;
            calls.Add("first cleanup");
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = queue.RunAsync(false, _ => { calls.Add("second entered"); return Task.CompletedTask; });
        Assert.IsFalse(second.IsCompleted);
        Assert.IsTrue(queue.IsBusy);
        release.SetResult();
        Assert.IsTrue(await first);
        Assert.IsTrue(await second);
        CollectionAssert.AreEqual(new[] { "first entered", "first cleanup", "second entered" }, calls);
        Assert.IsFalse(queue.IsBusy);
    }

    [TestMethod]
    public async Task Replacement_CancelsOldLoadButWaitsForItsNonInterruptibleWork()
    {
        var queue = new LevelEditorOperationQueue();
        var entered = Signal();
        var release = Signal();
        CancellationToken oldToken = default;
        bool cleaned = false;
        bool stalePublished = false;
        var first = queue.RunAsync(false, async token =>
        {
            oldToken = token;
            entered.SetResult();
            try { await release.Task; stalePublished = !token.IsCancellationRequested; }
            finally { cleaned = true; }
        });
        await entered.Task;
        var replacement = queue.RunAsync(true, _ =>
        {
            Assert.IsTrue(cleaned, "Shared resources must not be unloaded before worker cleanup.");
            return Task.CompletedTask;
        });
        Assert.IsTrue(oldToken.IsCancellationRequested);
        Assert.IsFalse(replacement.IsCompleted);
        release.SetResult();
        Assert.IsFalse(await first);
        Assert.IsTrue(await replacement);
        Assert.IsFalse(stalePublished);
    }

    [TestMethod]
    public async Task LatestReplacement_DiscardsQueuedOlderRequests()
    {
        var queue = new LevelEditorOperationQueue();
        var entered = Signal();
        var release = Signal();
        var first = queue.RunAsync(false, async _ => { entered.SetResult(); await release.Task; });
        await entered.Task;
        bool appendRan = false;
        bool supersededRan = false;
        bool latestRan = false;
        var append = queue.RunAsync(false, _ => { appendRan = true; return Task.CompletedTask; });
        var superseded = queue.RunAsync(true, _ => { supersededRan = true; return Task.CompletedTask; });
        var latest = queue.RunAsync(true, _ => { latestRan = true; return Task.CompletedTask; });
        release.SetResult();
        Assert.IsFalse(await first);
        Assert.IsFalse(await append);
        Assert.IsFalse(await superseded);
        Assert.IsTrue(await latest);
        Assert.IsFalse(appendRan);
        Assert.IsFalse(supersededRan);
        Assert.IsTrue(latestRan);
    }

    [TestMethod]
    public async Task Stop_DrainsWorkersAndRejectsNewRequests()
    {
        var queue = new LevelEditorOperationQueue();
        var entered = Signal();
        var release = Signal();
        bool cleaned = false;
        var first = queue.RunAsync(false, async _ =>
        {
            entered.SetResult();
            await release.Task;
            cleaned = true;
        });
        await entered.Task;
        var stop = queue.StopAsync();
        Assert.IsFalse(stop.IsCompleted);
        Assert.IsFalse(await queue.RunAsync(false, _ => throw new AssertFailedException("A closed queue ran a callback.")));
        release.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(cleaned);
        Assert.IsFalse(await first);
        await queue.StopAsync();
        Assert.IsFalse(queue.IsBusy);
    }

    [TestMethod]
    public async Task Failure_ReleasesQueueWithoutSuppressingTheError()
    {
        var queue = new LevelEditorOperationQueue();
        var exception = new InvalidOperationException("Expected failure");
        try
        {
            await queue.RunAsync(false, _ => throw exception);
            Assert.Fail("The failure was swallowed.");
        }
        catch (InvalidOperationException actual) { Assert.AreSame(exception, actual); }
        Assert.IsFalse(queue.IsBusy);
        Assert.IsTrue(await queue.RunAsync(false, _ => Task.CompletedTask));
    }

    [TestMethod]
    public async Task Replacement_DiscardsAnObsoleteWorkersLateFailure()
    {
        var queue = new LevelEditorOperationQueue();
        var release = Signal();
        var first = queue.RunAsync(false, async _ => { await release.Task; throw new InvalidOperationException("Obsolete worker"); });
        var next = queue.RunAsync(true, _ => Task.CompletedTask);
        release.SetResult();
        Assert.IsFalse(await first);
        Assert.IsTrue(await next);
        Assert.IsFalse(queue.IsBusy);
    }
}
