using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Dialogue;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
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

    private static InterpPreviewRuntime CreateRuntime(IInterpPreviewSession session, IInterpPreviewLoadCoordinator loadCoordinator, IInterpPreviewDialogueResolver resolver)
    {
        return new InterpPreviewRuntime(session, new StubRenderCoordinator(), loadCoordinator, resolver);
    }

    private sealed class StubSession : IInterpPreviewSession
    {
        public IList<ActorProxy> Actors { get; } = new List<ActorProxy>();
        public int LoadedLevelCount { get; private set; }

        public bool ContainsLevelPath(string fullPath) => false;

        public void AddLevel(InterpPreviewLoadedLevel loadedLevel)
        {
            LoadedLevelCount++;
        }

        public void ClearLevels()
        {
            LoadedLevelCount = 0;
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
}
