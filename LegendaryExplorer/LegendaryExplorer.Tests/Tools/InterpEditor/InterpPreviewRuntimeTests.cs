using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewRuntimeTests
{
    [TestMethod]
    public async Task LoadLevelAsync_AddsLoadReadyDiagnostic_WhenLevelLoads()
    {
        var session = new StubSession();
        var loadCoordinator = new StubLoadCoordinator
        {
            Result = InterpPreviewLoadResult.Loaded(new InterpPreviewLoadedLevel("C:\\Test\\Loaded.pcc", package: null, actors: new List<ActorProxy>()))
        };
        using var runtime = CreateRuntime(session, loadCoordinator, new StubDialogueResolver());

        InterpPreviewLoadResult result = await runtime.LoadLevelAsync("C:\\Test\\Loaded.pcc", replace: false, actorEditorContext: null, onReplace: null);

        Assert.AreEqual(InterpPreviewLoadOutcome.Loaded, result.Outcome);
        Assert.AreEqual(1, runtime.Diagnostics.Count);
        Assert.AreEqual("LOAD_READY", runtime.Diagnostics[0].Code);
    }

    [TestMethod]
    public void ResolveDialogue_ReplacesExistingDiagnostics()
    {
        var session = new StubSession();
        var loadCoordinator = new StubLoadCoordinator();
        var resolver = new StubDialogueResolver
        {
            Result = InterpPreviewDialogueResolution.Unresolved([
                new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_MISSING", "No preview data is linked to this line.")
            ])
        };
        using var runtime = CreateRuntime(session, loadCoordinator, resolver);

        runtime.CancelPendingLoad();
        Assert.AreEqual(1, runtime.Diagnostics.Count);
        Assert.AreEqual("LOAD_CANCEL_REQUESTED", runtime.Diagnostics[0].Code);

        runtime.ResolveDialogue(new InterpPreviewDialogueResolutionRequest(conversation: null, node: null));

        Assert.AreEqual(1, runtime.Diagnostics.Count);
        Assert.AreEqual("INTERP_MISSING", runtime.Diagnostics[0].Code);
    }

    [TestMethod]
    public void CancelPendingLoad_CallsCoordinatorAndAddsDiagnostic()
    {
        var session = new StubSession();
        var loadCoordinator = new StubLoadCoordinator();
        using var runtime = CreateRuntime(session, loadCoordinator, new StubDialogueResolver());

        runtime.CancelPendingLoad();

        Assert.AreEqual(1, loadCoordinator.CancelCalls);
        Assert.AreEqual(1, runtime.Diagnostics.Count);
        Assert.AreEqual("LOAD_CANCEL_REQUESTED", runtime.Diagnostics[0].Code);
    }

    [TestMethod]
    public async Task ExecuteCommitOperationAsync_DisposesCandidate_WhenRejectedClosing()
    {
        var session = new StubSession();
        var loadCoordinator = new StubLoadCoordinator();
        using var runtime = CreateRuntime(session, loadCoordinator, new StubDialogueResolver());

        await runtime.ShutdownAsync();
        var loaded = new InterpPreviewLoadedLevel("C:\\Test\\Player.pcc", package: null, actors: new List<ActorProxy>());

        InterpPreviewOperationResult result = await runtime.ExecuteCommitOperationAsync(loaded, replace: false);

        Assert.AreEqual(InterpPreviewOperationOutcome.RejectedClosing, result.Outcome);
        Assert.IsTrue(loaded.IsDisposed);
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task ShutdownAsync_WaitsForAcceptedOperationToDrain()
    {
        var session = new StubSession();
        var loadCoordinator = new BlockingLoadCoordinator();
        using var runtime = CreateRuntime(session, loadCoordinator, new StubDialogueResolver());

        Task<InterpPreviewOperationResult> operationTask = runtime.ExecuteLoadOperationAsync("C:\\Test\\Slow.pcc", replace: false, actorEditorContext: null);
        await loadCoordinator.Started.Task;

        Task shutdownTask = runtime.ShutdownAsync();
        Assert.IsFalse(shutdownTask.IsCompleted, "Shutdown should wait for accepted operation completion.");

        loadCoordinator.Release.TrySetResult(true);

        InterpPreviewOperationResult operationResult = await operationTask;
        Assert.AreEqual(InterpPreviewOperationOutcome.RejectedClosing, operationResult.Outcome);
        Assert.AreEqual(0, session.CommitCalls, "No commit should occur after shutdown begins.");
        Assert.IsFalse(runtime.Diagnostics.Any(d => d.Code == "LEVEL_ADDED"), "No post-shutdown level-added publication should occur.");

        await shutdownTask;
    }

    [TestMethod]
    [Timeout(15000)]
    public async Task NewerRequest_SupersedesOlderRequest_BeforeCommit()
    {
        var session = new StubSession();
        var loadCoordinator = new SupersedingLoadCoordinator();
        using var runtime = CreateRuntime(session, loadCoordinator, new StubDialogueResolver());

        Task<InterpPreviewOperationResult> first = runtime.ExecuteLoadOperationAsync("C:\\Test\\First.pcc", replace: false, actorEditorContext: null);
        await loadCoordinator.FirstCallEntered.Task;
        Task<InterpPreviewOperationResult> second = runtime.ExecuteLoadOperationAsync("C:\\Test\\Second.pcc", replace: false, actorEditorContext: null);

        InterpPreviewOperationResult firstResult = await first;
        InterpPreviewOperationResult secondResult = await second;

        Assert.AreEqual(InterpPreviewOperationOutcome.Cancelled, firstResult.Outcome);
        Assert.AreEqual(InterpPreviewOperationOutcome.Committed, secondResult.Outcome);
        Assert.AreEqual(1, session.CommitCalls);
    }

    [TestMethod]
    public async Task LoadLevelAsync_UsesDispatcherForDiagnosticPublication()
    {
        var session = new StubSession();
        var loadCoordinator = new StubLoadCoordinator
        {
            Result = InterpPreviewLoadResult.Loaded(new InterpPreviewLoadedLevel("C:\\Test\\Loaded.pcc", package: null, actors: new List<ActorProxy>()))
        };
        var dispatcher = new RecordingDispatcher();
        using var runtime = CreateRuntime(session, loadCoordinator, new StubDialogueResolver(), dispatcher);

        InterpPreviewLoadResult result = await runtime.LoadLevelAsync("C:\\Test\\Loaded.pcc", replace: false, actorEditorContext: null, onReplace: null);

        Assert.AreEqual(InterpPreviewLoadOutcome.Loaded, result.Outcome);
        Assert.IsTrue(dispatcher.InvokeCalls > 0);
        Assert.IsTrue(dispatcher.VerifyAccessCalls > 0);
    }

    [TestMethod]
    public void CancelPendingLoad_Throws_WhenDispatcherAccessDenied()
    {
        var session = new StubSession();
        var loadCoordinator = new StubLoadCoordinator();
        var dispatcher = new RecordingDispatcher { AllowAccess = false };
        var runtime = CreateRuntime(session, loadCoordinator, new StubDialogueResolver(), dispatcher);

        try
        {
            runtime.CancelPendingLoad();
            Assert.Fail("Expected InvalidOperationException.");
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            dispatcher.AllowAccess = true;
            runtime.Dispose();
        }
    }

    [TestMethod]
    public async Task ExecuteCommitOperationAsync_Replace_PropagatesRemovedActorsFromSessionCommit()
    {
        var retiredResource = new InterpPreviewOwnedResource(
            InterpPreviewResourceKey.ForLevel("C:\\Test\\OldLevel.pcc", MEGame.Unknown),
            "C:\\Test\\OldLevel.pcc",
            package: null,
            actors: []);
        var session = new StubSession
        {
            CommitResult = new InterpPreviewSessionCommitResult(
                addedActors: new List<ActorProxy> { CreateTestActor() },
                removedActors: new List<ActorProxy> { CreateTestActor(), CreateTestActor() },
                retiredResources: new List<InterpPreviewOwnedResource> { retiredResource })
        };
        var loadCoordinator = new StubLoadCoordinator();
        using var runtime = CreateRuntime(session, loadCoordinator, new StubDialogueResolver());

        InterpPreviewOperationResult result = await runtime.ExecuteCommitOperationAsync(
            new InterpPreviewLoadedLevel("C:\\Test\\Level.pcc", package: null, actors: []),
            replace: true);

        Assert.AreEqual(InterpPreviewOperationOutcome.Committed, result.Outcome);
        Assert.AreEqual(1, result.AddedActors.Count);
        Assert.AreEqual(2, result.RemovedActors.Count);
        Assert.AreEqual(1, result.RetiredResources.Count);
        Assert.AreSame(retiredResource, result.RetiredResources[0]);
        Assert.AreEqual(1, session.CommitCalls);
        Assert.AreEqual(1, session.LastCommitReplaceFlag);
    }

    [TestMethod]
    public async Task ExecuteCommitOperationAsync_FailedReplacement_PreservesPriorSessionState()
    {
        var session = new StubSession
        {
            LoadedLevelCount = 2,
            ThrowOnCommit = true
        };
        var loadCoordinator = new StubLoadCoordinator();
        using var runtime = CreateRuntime(session, loadCoordinator, new StubDialogueResolver());

        InterpPreviewOperationResult result = await runtime.ExecuteCommitOperationAsync(
            new InterpPreviewLoadedLevel("C:\\Test\\Replacement.pcc", package: null, actors: []),
            replace: true);

        Assert.AreEqual(InterpPreviewOperationOutcome.Failed, result.Outcome);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(2, session.LoadedLevelCount);
        Assert.AreEqual(1, session.CommitCalls);
        Assert.AreEqual(1, session.LastCommitReplaceFlag);
    }

    private static InterpPreviewRuntime CreateRuntime(IInterpPreviewSession session, IInterpPreviewLoadCoordinator loadCoordinator, IInterpPreviewDialogueResolver resolver)
    {
        return new InterpPreviewRuntime(session, new StubRenderCoordinator(), loadCoordinator, resolver);
    }

    private static InterpPreviewRuntime CreateRuntime(
        IInterpPreviewSession session,
        IInterpPreviewLoadCoordinator loadCoordinator,
        IInterpPreviewDialogueResolver resolver,
        IInterpPreviewDispatcher dispatcher)
    {
        return new InterpPreviewRuntime(session, new StubRenderCoordinator(), loadCoordinator, resolver, dispatcher);
    }

    private static ActorProxy CreateTestActor()
    {
        var actor = (ActorProxy)RuntimeHelpers.GetUninitializedObject(typeof(ActorProxy));
        actor.Components = [];
        return actor;
    }

    private sealed class StubSession : IInterpPreviewSession
    {
        public IList<ActorProxy> Actors { get; } = new List<ActorProxy>();
        public int LoadedLevelCount { get; set; }
        public int TotalResourceCount => LoadedLevelCount;
        public int LookupKeyCount { get; set; }
        public IReadOnlyList<string> LoadedLevelPaths { get; } = [];
        public int AddLevelCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public int LastCommitReplaceFlag { get; private set; } = -1;
        public InterpPreviewSessionCommitResult CommitResult { get; set; } = new([], [], []);
        public bool ThrowOnCommit { get; set; }

        public bool ContainsLevelPath(string fullPath) => false;
        public bool ContainsResource(InterpPreviewResourceKey key) => false;
        public IReadOnlyList<ActorProxy> FindActorsByLookup(string fullPath) => [];
        public InterpPreviewSessionCommitResult CommitLevel(InterpPreviewLoadedLevel loadedLevel, bool replace)
        {
            CommitCalls++;
            LastCommitReplaceFlag = replace ? 1 : 0;
            if (ThrowOnCommit)
            {
                throw new InvalidOperationException("Commit failed.");
            }

            return CommitResult;
        }

        public IReadOnlyList<ActorProxy> AddLevel(InterpPreviewLoadedLevel loadedLevel)
        {
            LoadedLevelCount++;
            AddLevelCalls++;
            return [];
        }

        public IReadOnlyList<ActorProxy> ReplaceAllWithLevel(InterpPreviewLoadedLevel loadedLevel)
        {
            LoadedLevelCount = 1;
            AddLevelCalls++;
            return [];
        }

        public void ClearLevels()
        {
            LoadedLevelCount = 0;
        }

        public void Dispose() { }
    }

    private sealed class BlockingLoadCoordinator : IInterpPreviewLoadCoordinator
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace)
        {
            Started.TrySetResult(true);
            await Release.Task;
            return InterpPreviewLoadResult.Loaded(new InterpPreviewLoadedLevel(path, package: null, actors: new List<ActorProxy>()));
        }

        public void CancelPendingLoad() { }

        public void Dispose() { }
    }

    private sealed class SupersedingLoadCoordinator : IInterpPreviewLoadCoordinator
    {
        private readonly TaskCompletionSource<InterpPreviewLoadResult> _firstResult = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public TaskCompletionSource<bool> FirstCallEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace)
        {
            int call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                FirstCallEntered.TrySetResult(true);
                return _firstResult.Task;
            }

            return Task.FromResult(InterpPreviewLoadResult.Loaded(new InterpPreviewLoadedLevel(path, package: null, actors: new List<ActorProxy>())));
        }

        public void CancelPendingLoad()
        {
            _firstResult.TrySetResult(InterpPreviewLoadResult.Cancelled());
        }

        public void Dispose() { }
    }

    private sealed class StubRenderCoordinator : IInterpPreviewRenderCoordinator
    {
        public void Attach(LevelEditorRenderContext renderContext, IInterpPreviewSession session) { }
        public void Detach(LevelEditorRenderContext renderContext) { }
    }

    private sealed class StubLoadCoordinator : IInterpPreviewLoadCoordinator
    {
        public int CancelCalls { get; private set; }
        public InterpPreviewLoadResult Result { get; set; } = InterpPreviewLoadResult.Duplicate();

        public Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace)
        {
            return Task.FromResult(Result);
        }

        public void CancelPendingLoad()
        {
            CancelCalls++;
        }

        public void Dispose() { }
    }

    private sealed class StubDialogueResolver : IInterpPreviewDialogueResolver
    {
        public InterpPreviewDialogueResolution Result { get; set; } = InterpPreviewDialogueResolution.Unresolved([]);

        public InterpPreviewDialogueResolution Resolve(InterpPreviewDialogueResolutionRequest request)
        {
            return Result;
        }
    }

    private sealed class RecordingDispatcher : IInterpPreviewDispatcher
    {
        public bool AllowAccess { get; set; } = true;
        public int VerifyAccessCalls { get; private set; }
        public int InvokeCalls { get; private set; }

        public bool CheckAccess() => AllowAccess;

        public void VerifyAccess()
        {
            VerifyAccessCalls++;
            if (!AllowAccess)
            {
                throw new InvalidOperationException("Dispatcher access denied.");
            }
        }

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            VerifyAccess();
            cancellationToken.ThrowIfCancellationRequested();
            InvokeCalls++;
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
        {
            VerifyAccess();
            cancellationToken.ThrowIfCancellationRequested();
            InvokeCalls++;
            return Task.FromResult(action());
        }

        public Task YieldAsync(CancellationToken cancellationToken = default)
        {
            VerifyAccess();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
