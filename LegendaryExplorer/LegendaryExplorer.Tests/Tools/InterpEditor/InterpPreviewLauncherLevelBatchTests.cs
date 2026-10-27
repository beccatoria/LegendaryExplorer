using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorer.Tools.LevelEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewLauncherLevelBatchTests
{
    [TestMethod]
    public async Task ExecuteTransactionalReplaceAsync_FailedLaterLoad_DoesNotCommitAndDisposesStaged()
    {
        var firstLoaded = CreateLoadedLevel("C:\\Test\\First.pcc");
        var runtime = new StubRuntime(
            loadResults:
            [
                InterpPreviewLoadResult.Loaded(firstLoaded),
                InterpPreviewLoadResult.Failed(new InvalidOperationException("second failed"))
            ],
            commitResults: []);

        InterpPreviewLauncherLevelBatchResult result = await InterpPreviewLauncherLevelBatch.ExecuteTransactionalReplaceAsync(
            runtime,
            ["C:\\Test\\First.pcc", "C:\\Test\\Second.pcc"],
            actorEditorContext: null,
            isRequestCurrent: () => true);

        Assert.AreEqual(InterpPreviewLauncherBatchTerminalOutcome.Failed, result.TerminalOutcome);
        Assert.AreEqual(0, runtime.CommitCalls.Count);
        Assert.IsTrue(firstLoaded.IsDisposed);
    }

    [TestMethod]
    public async Task ExecuteTransactionalReplaceAsync_AllLoadsSucceed_CommitsInOrderWithSingleReplace()
    {
        var firstLoaded = CreateLoadedLevel("C:\\Test\\First.pcc");
        var secondLoaded = CreateLoadedLevel("C:\\Test\\Second.pcc");
        var runtime = new StubRuntime(
            loadResults:
            [
                InterpPreviewLoadResult.Loaded(firstLoaded),
                InterpPreviewLoadResult.Loaded(secondLoaded)
            ],
            commitResults:
            [
                InterpPreviewOperationResult.Committed([]),
                InterpPreviewOperationResult.Committed([])
            ]);

        InterpPreviewLauncherLevelBatchResult result = await InterpPreviewLauncherLevelBatch.ExecuteTransactionalReplaceAsync(
            runtime,
            ["C:\\Test\\First.pcc", "C:\\Test\\Second.pcc"],
            actorEditorContext: null,
            isRequestCurrent: () => true);

        Assert.AreEqual(InterpPreviewLauncherBatchTerminalOutcome.None, result.TerminalOutcome);
        Assert.AreEqual(2, result.LoadedCount);
        Assert.IsFalse(result.HadFailure);
        Assert.AreEqual(2, result.CommittedLevels.Count);
        Assert.AreEqual(2, runtime.CommitCalls.Count);
        Assert.AreEqual("C:\\Test\\First.pcc", runtime.CommitCalls[0].loadedLevel.FullPath);
        Assert.IsTrue(runtime.CommitCalls[0].replace);
        Assert.AreEqual("C:\\Test\\Second.pcc", runtime.CommitCalls[1].loadedLevel.FullPath);
        Assert.IsFalse(runtime.CommitCalls[1].replace);
    }

    [TestMethod]
    public async Task ExecuteTransactionalReplaceAsync_SupersededBeforeCommit_DoesNotCommitAndDisposesStaged()
    {
        var firstLoaded = CreateLoadedLevel("C:\\Test\\First.pcc");
        var secondLoaded = CreateLoadedLevel("C:\\Test\\Second.pcc");
        var runtime = new StubRuntime(
            loadResults:
            [
                InterpPreviewLoadResult.Loaded(firstLoaded),
                InterpPreviewLoadResult.Loaded(secondLoaded)
            ],
            commitResults: []);

        int isCurrentCalls = 0;
        InterpPreviewLauncherLevelBatchResult result = await InterpPreviewLauncherLevelBatch.ExecuteTransactionalReplaceAsync(
            runtime,
            ["C:\\Test\\First.pcc", "C:\\Test\\Second.pcc"],
            actorEditorContext: null,
            isRequestCurrent: () => ++isCurrentCalls <= 2);

        Assert.AreEqual(InterpPreviewLauncherBatchTerminalOutcome.Superseded, result.TerminalOutcome);
        Assert.AreEqual(0, runtime.CommitCalls.Count);
        Assert.IsTrue(firstLoaded.IsDisposed);
        Assert.IsTrue(secondLoaded.IsDisposed);
    }

    [TestMethod]
    public async Task ExecuteTransactionalReplaceAsync_FailedDuringCommit_PreservesCommittedLevelsForCaller()
    {
        var firstLoaded = CreateLoadedLevel("C:\\Test\\First.pcc");
        var secondLoaded = CreateLoadedLevel("C:\\Test\\Second.pcc");
        var runtime = new StubRuntime(
            loadResults:
            [
                InterpPreviewLoadResult.Loaded(firstLoaded),
                InterpPreviewLoadResult.Loaded(secondLoaded)
            ],
            commitResults:
            [
                InterpPreviewOperationResult.Committed([]),
                InterpPreviewOperationResult.Failed(new InvalidOperationException("commit failed"))
            ]);

        InterpPreviewLauncherLevelBatchResult result = await InterpPreviewLauncherLevelBatch.ExecuteTransactionalReplaceAsync(
            runtime,
            ["C:\\Test\\First.pcc", "C:\\Test\\Second.pcc"],
            actorEditorContext: null,
            isRequestCurrent: () => true);

        Assert.AreEqual(InterpPreviewLauncherBatchTerminalOutcome.Failed, result.TerminalOutcome);
        Assert.AreEqual(1, result.LoadedCount);
        Assert.AreEqual(1, result.CommittedLevels.Count);
        Assert.AreEqual("C:\\Test\\First.pcc", result.CommittedLevels[0].Path);
        Assert.IsTrue(secondLoaded.IsDisposed);
    }

    [TestMethod]
    public async Task ExecuteTransactionalReplaceAsync_DeduplicatesCanonicalPaths_BeforeLoading()
    {
        var firstLoaded = CreateLoadedLevel("C:\\Test\\First.pcc");
        var runtime = new StubRuntime(
            loadResults:
            [
                InterpPreviewLoadResult.Loaded(firstLoaded)
            ],
            commitResults:
            [
                InterpPreviewOperationResult.Committed([])
            ]);

        InterpPreviewLauncherLevelBatchResult result = await InterpPreviewLauncherLevelBatch.ExecuteTransactionalReplaceAsync(
            runtime,
            ["C:\\Test\\First.pcc", "C:\\Test\\..\\Test\\FIRST.pcc"],
            actorEditorContext: null,
            isRequestCurrent: () => true);

        Assert.AreEqual(InterpPreviewLauncherBatchTerminalOutcome.None, result.TerminalOutcome);
        Assert.AreEqual(1, runtime.LoadCalls.Count);
        Assert.AreEqual("C:\\Test\\First.pcc", runtime.LoadCalls[0]);
        Assert.AreEqual(1, runtime.CommitCalls.Count);
        Assert.AreEqual(1, result.LoadedCount);
    }

    [TestMethod]
    public async Task ExecuteTransactionalReplaceAsync_InvalidPath_FailsWithoutLoad()
    {
        var runtime = new StubRuntime(loadResults: [], commitResults: []);

        InterpPreviewLauncherLevelBatchResult result = await InterpPreviewLauncherLevelBatch.ExecuteTransactionalReplaceAsync(
            runtime,
            ["   "],
            actorEditorContext: null,
            isRequestCurrent: () => true);

        Assert.AreEqual(InterpPreviewLauncherBatchTerminalOutcome.Failed, result.TerminalOutcome);
        Assert.AreEqual(0, runtime.LoadCalls.Count);
        Assert.AreEqual(0, runtime.CommitCalls.Count);
        Assert.IsTrue(result.HadFailure);
    }

    [TestMethod]
    public async Task ExecuteTransactionalReplaceAsync_SupersededDuringCommit_PreservesCommittedLevelsForCaller()
    {
        var firstLoaded = CreateLoadedLevel("C:\\Test\\First.pcc");
        var secondLoaded = CreateLoadedLevel("C:\\Test\\Second.pcc");
        var runtime = new StubRuntime(
            loadResults:
            [
                InterpPreviewLoadResult.Loaded(firstLoaded),
                InterpPreviewLoadResult.Loaded(secondLoaded)
            ],
            commitResults:
            [
                InterpPreviewOperationResult.Committed([])
            ]);

        int isCurrentCalls = 0;
        InterpPreviewLauncherLevelBatchResult result = await InterpPreviewLauncherLevelBatch.ExecuteTransactionalReplaceAsync(
            runtime,
            ["C:\\Test\\First.pcc", "C:\\Test\\Second.pcc"],
            actorEditorContext: null,
            isRequestCurrent: () => ++isCurrentCalls <= 3);

        Assert.AreEqual(InterpPreviewLauncherBatchTerminalOutcome.Superseded, result.TerminalOutcome);
        Assert.AreEqual(1, result.LoadedCount);
        Assert.AreEqual(1, result.CommittedLevels.Count);
        Assert.AreEqual("C:\\Test\\First.pcc", result.CommittedLevels[0].Path);
        Assert.IsTrue(secondLoaded.IsDisposed);
    }

    [TestMethod]
    public async Task ExecuteTransactionalReplaceAsync_RejectedClosingDuringCommit_PreservesCommittedLevelsForCaller()
    {
        var firstLoaded = CreateLoadedLevel("C:\\Test\\First.pcc");
        var secondLoaded = CreateLoadedLevel("C:\\Test\\Second.pcc");
        var runtime = new StubRuntime(
            loadResults:
            [
                InterpPreviewLoadResult.Loaded(firstLoaded),
                InterpPreviewLoadResult.Loaded(secondLoaded)
            ],
            commitResults:
            [
                InterpPreviewOperationResult.Committed([]),
                InterpPreviewOperationResult.RejectedClosing("closing")
            ]);

        InterpPreviewLauncherLevelBatchResult result = await InterpPreviewLauncherLevelBatch.ExecuteTransactionalReplaceAsync(
            runtime,
            ["C:\\Test\\First.pcc", "C:\\Test\\Second.pcc"],
            actorEditorContext: null,
            isRequestCurrent: () => true);

        Assert.AreEqual(InterpPreviewLauncherBatchTerminalOutcome.RejectedClosing, result.TerminalOutcome);
        Assert.AreEqual(1, result.LoadedCount);
        Assert.AreEqual(1, result.CommittedLevels.Count);
        Assert.AreEqual("C:\\Test\\First.pcc", result.CommittedLevels[0].Path);
        Assert.IsTrue(secondLoaded.IsDisposed);
    }

    private static InterpPreviewLoadedLevel CreateLoadedLevel(string path)
    {
        return new InterpPreviewLoadedLevel(path, package: null, actors: new List<ActorProxy>());
    }

    private sealed class StubRuntime : IInterpPreviewRuntime
    {
        private readonly Queue<InterpPreviewLoadResult> _loadResults;
        private readonly Queue<InterpPreviewOperationResult> _commitResults;

        public StubRuntime(IEnumerable<InterpPreviewLoadResult> loadResults, IEnumerable<InterpPreviewOperationResult> commitResults)
        {
            _loadResults = new Queue<InterpPreviewLoadResult>(loadResults ?? []);
            _commitResults = new Queue<InterpPreviewOperationResult>(commitResults ?? []);
        }

        public List<(InterpPreviewLoadedLevel loadedLevel, bool replace)> CommitCalls { get; } = [];
        public List<string> LoadCalls { get; } = [];

        public event EventHandler DiagnosticsChanged
        {
            add { }
            remove { }
        }

        public int LoadedLevelCount => 0;
        public int TotalResourceCount => 0;
        public int LookupKeyCount => 0;
        public IReadOnlyList<InterpPreviewDiagnostic> Diagnostics => [];
        public IReadOnlyList<string> LoadedLevelPaths => [];
        public IList<ActorProxy> Actors => new List<ActorProxy>();
        public bool IsOpen => true;
        public bool IsRequestCurrent(long requestId) => true;
        public bool TryPublishFeedback(InterpPreviewLauncherResult result, Action publish) => throw new NotSupportedException();
        public Task<InterpPreviewLauncherResult> ExecuteLauncherRequestAsync(InterpPreviewLauncherRequest request,
            IActorEditorContext context, IInterpPreviewLauncherHost host) => throw new NotSupportedException();

        public bool ContainsResource(InterpPreviewResourceKey key) => false;
        public void Attach(LevelEditorRenderContext renderContext) { }
        public void Detach(LevelEditorRenderContext renderContext) { }
        public Task ShutdownAsync() => Task.CompletedTask;
        public void CancelPendingLoad() { }
        public IReadOnlyList<ActorProxy> AddLoadedLevel(InterpPreviewLoadedLevel loadedLevel) => [];
        public void UnloadAllLevels(LevelEditorRenderContext renderContext) { }
        public InterpPreviewDialogueResolution ResolveDialogue(InterpPreviewDialogueResolutionRequest request) => InterpPreviewDialogueResolution.Unresolved([]);

        public Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace)
        {
            LoadCalls.Add(path);
            if (_loadResults.Count == 0)
            {
                Assert.Fail("No load result prepared for test runtime.");
            }

            return Task.FromResult(_loadResults.Dequeue());
        }

        public Task<InterpPreviewOperationResult> ExecuteLoadOperationAsync(string path, bool replace, IActorEditorContext actorEditorContext)
            => Task.FromResult(InterpPreviewOperationResult.NoChange());

        public Task<InterpPreviewOperationResult> ExecuteCommitOperationAsync(InterpPreviewLoadedLevel loadedLevel, bool replace)
        {
            CommitCalls.Add((loadedLevel, replace));
            if (_commitResults.Count == 0)
            {
                loadedLevel.TryTransferOwnership(out _, out _);
                return Task.FromResult(InterpPreviewOperationResult.Committed([]));
            }

            InterpPreviewOperationResult result = _commitResults.Dequeue();
            if (result.Outcome == InterpPreviewOperationOutcome.Committed)
            {
                loadedLevel.TryTransferOwnership(out _, out _);
            }

            return Task.FromResult(result);
        }

        public void Dispose() { }
    }
}
