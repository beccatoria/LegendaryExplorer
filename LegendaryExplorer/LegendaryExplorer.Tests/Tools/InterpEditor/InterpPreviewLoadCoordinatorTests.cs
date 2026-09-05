using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewLoadCoordinatorTests
{
    [TestMethod]
    public async Task LoadLevelAsync_ReturnsDuplicate_WhenPathAlreadyLoaded()
    {
        var levelLoader = new StubLevelLoader();
        var session = new StubSession { ContainsLevelPathResult = true };
        using var coordinator = new InterpPreviewLoadCoordinator(levelLoader, session);

        InterpPreviewLoadResult result = await coordinator.LoadLevelAsync("C:\\Test\\Same.pcc", replace: false, actorEditorContext: null, onReplace: null);

        Assert.AreEqual(InterpPreviewLoadOutcome.Duplicate, result.Outcome);
        Assert.AreEqual(0, levelLoader.Calls);
    }

    [TestMethod]
    public async Task LoadLevelAsync_InvokesReplaceCallback_WhenReplaceRequested()
    {
        var levelLoader = new StubLevelLoader();
        var session = new StubSession();
        using var coordinator = new InterpPreviewLoadCoordinator(levelLoader, session);
        bool replaceInvoked = false;

        InterpPreviewLoadResult result = await coordinator.LoadLevelAsync("C:\\Test\\Load.pcc", replace: true, actorEditorContext: null, onReplace: () => replaceInvoked = true);

        Assert.AreEqual(InterpPreviewLoadOutcome.Loaded, result.Outcome);
        Assert.IsTrue(replaceInvoked);
        Assert.AreEqual(1, levelLoader.Calls);
    }

    [TestMethod]
    public async Task LoadLevelAsync_ReturnsFailed_WhenLoaderThrows()
    {
        var levelLoader = new StubLevelLoader { ExceptionToThrow = new InvalidOperationException("boom") };
        var session = new StubSession();
        using var coordinator = new InterpPreviewLoadCoordinator(levelLoader, session);

        InterpPreviewLoadResult result = await coordinator.LoadLevelAsync("C:\\Test\\Broken.pcc", replace: false, actorEditorContext: null, onReplace: null);

        Assert.AreEqual(InterpPreviewLoadOutcome.Failed, result.Outcome);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task LoadLevelAsync_ReturnsCancelled_WhenCancelPendingLoadCalledDuringLoad()
    {
        using var loadStarted = new ManualResetEventSlim(false);
        var levelLoader = new BlockingUntilCancelledLoader(loadStarted);
        var session = new StubSession();
        using var coordinator = new InterpPreviewLoadCoordinator(levelLoader, session);

        Task<InterpPreviewLoadResult> loadTask = coordinator.LoadLevelAsync("C:\\Test\\CancelMe.pcc", replace: false, actorEditorContext: null, onReplace: null);
        Assert.IsTrue(loadStarted.Wait(TimeSpan.FromSeconds(2)), "Loader did not start in time.");

        coordinator.CancelPendingLoad();
        InterpPreviewLoadResult result = await AwaitWithTimeout(loadTask, TimeSpan.FromSeconds(5), "Load task did not complete after cancellation.");

        Assert.AreEqual(InterpPreviewLoadOutcome.Cancelled, result.Outcome);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task LoadLevelAsync_SecondLoadCancelsFirstInFlightLoad()
    {
        using var firstLoadStarted = new ManualResetEventSlim(false);
        var levelLoader = new FirstCallBlocksUntilCancelledThenSucceedsLoader(firstLoadStarted);
        var session = new StubSession();
        using var coordinator = new InterpPreviewLoadCoordinator(levelLoader, session);

        Task<InterpPreviewLoadResult> firstLoadTask = coordinator.LoadLevelAsync("C:\\Test\\First.pcc", replace: false, actorEditorContext: null, onReplace: null);
        Assert.IsTrue(firstLoadStarted.Wait(TimeSpan.FromSeconds(2)), "First load did not start in time.");

        Task<InterpPreviewLoadResult> secondLoadTask = coordinator.LoadLevelAsync("C:\\Test\\Second.pcc", replace: false, actorEditorContext: null, onReplace: null);

        InterpPreviewLoadResult firstResult = await AwaitWithTimeout(firstLoadTask, TimeSpan.FromSeconds(5), "First load task did not complete after being superseded.");
        InterpPreviewLoadResult secondResult = await AwaitWithTimeout(secondLoadTask, TimeSpan.FromSeconds(5), "Second load task did not complete.");

        Assert.AreEqual(InterpPreviewLoadOutcome.Cancelled, firstResult.Outcome);
        Assert.AreEqual(InterpPreviewLoadOutcome.Loaded, secondResult.Outcome);
        Assert.AreEqual(2, levelLoader.Calls);
    }

    [TestMethod]
    public async Task LoadLevelAsync_ReturnsSuperseded_AndDisposesPreparedResource_WhenCanContinueTurnsFalseAfterPrepare()
    {
        var session = new StubSession();
        var dispatcher = new InterpPreviewInlineDispatcher();
        var prepared = new InterpPreviewPreparedResource("C:\\Test\\Prepared.pcc", package: null, descriptors: Array.Empty<InterpPreviewActorBuildDescriptor>());
        var preparer = new StubPackagePreparer { Result = InterpPreviewPrepareResult.Prepared(prepared) };
        var realizer = new StubActorRealizer();
        using var coordinator = new InterpPreviewLoadCoordinator(preparer, realizer, dispatcher, session);

        int canContinueCalls = 0;
        InterpPreviewLoadResult result = await coordinator.LoadLevelAsync(
            "C:\\Test\\Prepared.pcc",
            replace: false,
            actorEditorContext: null,
            onReplace: null,
            canContinue: () => Interlocked.Increment(ref canContinueCalls) == 1);

        Assert.AreEqual(InterpPreviewLoadOutcome.Superseded, result.Outcome);
        Assert.IsTrue(prepared.IsDisposed);
        Assert.AreEqual(0, realizer.Calls);
    }

    [TestMethod]
    public async Task LoadLevelAsync_ReturnsFailed_AndDisposesPreparedResource_WhenRealizationFails()
    {
        var session = new StubSession();
        var dispatcher = new InterpPreviewInlineDispatcher();
        var prepared = new InterpPreviewPreparedResource("C:\\Test\\Prepared.pcc", package: null, descriptors: Array.Empty<InterpPreviewActorBuildDescriptor>());
        var preparer = new StubPackagePreparer { Result = InterpPreviewPrepareResult.Prepared(prepared) };
        var realizer = new StubActorRealizer
        {
            Result = InterpPreviewRealizeResult.Failed(new InvalidOperationException("realize failed"))
        };
        using var coordinator = new InterpPreviewLoadCoordinator(preparer, realizer, dispatcher, session);

        InterpPreviewLoadResult result = await coordinator.LoadLevelAsync(
            "C:\\Test\\Prepared.pcc",
            replace: false,
            actorEditorContext: null,
            onReplace: null,
            canContinue: () => true);

        Assert.AreEqual(InterpPreviewLoadOutcome.Failed, result.Outcome);
        Assert.IsNotNull(result.Error);
        Assert.IsTrue(prepared.IsDisposed);
        Assert.AreEqual(1, realizer.Calls);
    }

    [TestMethod]
    public async Task LoadLevelAsync_ReturnsFailed_AndDisposesPreparedResource_WhenRealizerThrowsDuringConstruction()
    {
        var session = new StubSession();
        var dispatcher = new InterpPreviewInlineDispatcher();
        var prepared = new InterpPreviewPreparedResource("C:\\Test\\Prepared.pcc", package: null, descriptors: Array.Empty<InterpPreviewActorBuildDescriptor>());
        var preparer = new StubPackagePreparer { Result = InterpPreviewPrepareResult.Prepared(prepared) };
        var realizer = new StubActorRealizer
        {
            ExceptionToThrow = new InvalidOperationException("partial construction failed")
        };
        using var coordinator = new InterpPreviewLoadCoordinator(preparer, realizer, dispatcher, session);

        InterpPreviewLoadResult result = await coordinator.LoadLevelAsync(
            "C:\\Test\\Prepared.pcc",
            replace: false,
            actorEditorContext: null,
            onReplace: null,
            canContinue: () => true);

        Assert.AreEqual(InterpPreviewLoadOutcome.Failed, result.Outcome);
        Assert.IsNotNull(result.Error);
        Assert.IsTrue(prepared.IsDisposed);
        Assert.AreEqual(1, realizer.Calls);
    }

    [TestMethod]
    public async Task LoadLevelAsync_ReturnsSuperseded_WithoutPreparing_WhenCanContinueFalseBeforeWork()
    {
        var session = new StubSession();
        var dispatcher = new InterpPreviewInlineDispatcher();
        var preparer = new StubPackagePreparer
        {
            Result = InterpPreviewPrepareResult.Prepared(new InterpPreviewPreparedResource(
                "C:\\Test\\Prepared.pcc",
                package: null,
                descriptors: Array.Empty<InterpPreviewActorBuildDescriptor>()))
        };
        var realizer = new StubActorRealizer();
        using var coordinator = new InterpPreviewLoadCoordinator(preparer, realizer, dispatcher, session);

        InterpPreviewLoadResult result = await coordinator.LoadLevelAsync(
            "C:\\Test\\Prepared.pcc",
            replace: false,
            actorEditorContext: null,
            onReplace: null,
            canContinue: () => false);

        Assert.AreEqual(InterpPreviewLoadOutcome.Superseded, result.Outcome);
        Assert.AreEqual(0, realizer.Calls);
    }

    [TestMethod]
    public async Task LoadLevelAsync_ReturnsCancelled_WhenPrepareCancels_AndSkipsRealization()
    {
        var session = new StubSession();
        var dispatcher = new InterpPreviewInlineDispatcher();
        var preparer = new StubPackagePreparer { Result = InterpPreviewPrepareResult.Cancelled() };
        var realizer = new StubActorRealizer();
        using var coordinator = new InterpPreviewLoadCoordinator(preparer, realizer, dispatcher, session);

        InterpPreviewLoadResult result = await coordinator.LoadLevelAsync(
            "C:\\Test\\Prepared.pcc",
            replace: false,
            actorEditorContext: null,
            onReplace: null,
            canContinue: () => true);

        Assert.AreEqual(InterpPreviewLoadOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, realizer.Calls);
    }

    [TestMethod]
    public async Task LoadLevelAsync_ReturnsSuperseded_AndDisposesPreparedResource_WhenRealizerSupersedes()
    {
        var session = new StubSession();
        var dispatcher = new InterpPreviewInlineDispatcher();
        var prepared = new InterpPreviewPreparedResource("C:\\Test\\Prepared.pcc", package: null, descriptors: Array.Empty<InterpPreviewActorBuildDescriptor>());
        var preparer = new StubPackagePreparer { Result = InterpPreviewPrepareResult.Prepared(prepared) };
        var realizer = new StubActorRealizer { Result = InterpPreviewRealizeResult.Superseded() };
        using var coordinator = new InterpPreviewLoadCoordinator(preparer, realizer, dispatcher, session);

        InterpPreviewLoadResult result = await coordinator.LoadLevelAsync(
            "C:\\Test\\Prepared.pcc",
            replace: false,
            actorEditorContext: null,
            onReplace: null,
            canContinue: () => true);

        Assert.AreEqual(InterpPreviewLoadOutcome.Superseded, result.Outcome);
        Assert.AreEqual(1, realizer.Calls);
        Assert.IsTrue(prepared.IsDisposed);
    }

    private sealed class StubLevelLoader : IInterpPreviewLevelLoader
    {
        public int Calls { get; private set; }
        public Exception ExceptionToThrow { get; set; }

        public InterpPreviewLoadedLevel LoadLevel(string path, IActorEditorContext actorEditorContext, CancellationToken cancellationToken)
        {
            Calls++;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return new InterpPreviewLoadedLevel(path, package: null, actors: new List<ActorProxy>());
        }
    }

    private sealed class StubSession : IInterpPreviewSession
    {
        public bool ContainsLevelPathResult { get; set; }
        public IList<ActorProxy> Actors { get; } = new List<ActorProxy>();
        public int LoadedLevelCount => 0;
        public int TotalResourceCount => 0;
        public int LookupKeyCount => 0;
        public IReadOnlyList<string> LoadedLevelPaths => [];

        public bool ContainsLevelPath(string fullPath) => ContainsLevelPathResult;
        public bool ContainsResource(InterpPreviewResourceKey key) => false;
        public IReadOnlyList<ActorProxy> FindActorsByLookup(string lookup) => [];
        public InterpPreviewSessionCommitResult CommitLevel(InterpPreviewLoadedLevel loadedLevel, bool replace)
        {
            IReadOnlyList<ActorProxy> added = replace
                ? ReplaceAllWithLevel(loadedLevel)
                : AddLevel(loadedLevel);
            return new InterpPreviewSessionCommitResult(added, [], []);
        }

        public IReadOnlyList<ActorProxy> AddLevel(InterpPreviewLoadedLevel loadedLevel) => [];
        public IReadOnlyList<ActorProxy> ReplaceAllWithLevel(InterpPreviewLoadedLevel loadedLevel) => [];
        public void ClearLevels() { }
        public void Dispose() { }
    }

    private sealed class StubPackagePreparer : IInterpPreviewPackagePreparer
    {
        public InterpPreviewPrepareResult Result { get; set; } = InterpPreviewPrepareResult.Cancelled();

        public Task<InterpPreviewPrepareResult> PrepareAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }

    private sealed class StubActorRealizer : IInterpPreviewActorRealizer
    {
        public int Calls { get; private set; }
        public InterpPreviewRealizeResult Result { get; set; } = InterpPreviewRealizeResult.Cancelled();
        public Exception ExceptionToThrow { get; set; }

        public Task<InterpPreviewRealizeResult> RealizeAsync(
            InterpPreviewPreparedResource preparedResource,
            IActorEditorContext actorEditorContext,
            Func<bool> canContinue,
            CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class BlockingUntilCancelledLoader : IInterpPreviewLevelLoader
    {
        private readonly ManualResetEventSlim _started;

        public BlockingUntilCancelledLoader(ManualResetEventSlim started)
        {
            _started = started;
        }

        public InterpPreviewLoadedLevel LoadLevel(string path, IActorEditorContext actorEditorContext, CancellationToken cancellationToken)
        {
            _started.Set();
            Stopwatch sw = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(5))
                {
                    throw new TimeoutException("Cancellation token was not signaled for blocking load within timeout.");
                }

                Thread.Sleep(5);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new InterpPreviewLoadedLevel(path, package: null, actors: new List<ActorProxy>());
        }
    }

    private sealed class FirstCallBlocksUntilCancelledThenSucceedsLoader : IInterpPreviewLevelLoader
    {
        private readonly ManualResetEventSlim _firstCallStarted;
        public int Calls;

        public FirstCallBlocksUntilCancelledThenSucceedsLoader(ManualResetEventSlim firstCallStarted)
        {
            _firstCallStarted = firstCallStarted;
        }

        public InterpPreviewLoadedLevel LoadLevel(string path, IActorEditorContext actorEditorContext, CancellationToken cancellationToken)
        {
            int callIndex = Interlocked.Increment(ref Calls);
            if (callIndex == 1)
            {
                _firstCallStarted.Set();
                Stopwatch sw = Stopwatch.StartNew();
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (sw.Elapsed > TimeSpan.FromSeconds(5))
                    {
                        throw new TimeoutException("First in-flight load was not cancelled within timeout.");
                    }

                    Thread.Sleep(5);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return new InterpPreviewLoadedLevel(path, package: null, actors: new List<ActorProxy>());
        }
    }

    private static async Task<InterpPreviewLoadResult> AwaitWithTimeout(Task<InterpPreviewLoadResult> task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            Assert.Fail(timeoutMessage);
        }

        return await task;
    }
}
