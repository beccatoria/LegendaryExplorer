using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewRuntime : IDisposable
{
    event EventHandler DiagnosticsChanged;

    int LoadedLevelCount { get; }
    IReadOnlyList<InterpPreviewDiagnostic> Diagnostics { get; }

    void Attach(LevelEditorRenderContext renderContext);
    void Detach(LevelEditorRenderContext renderContext);
    Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace);
    InterpPreviewDialogueResolution ResolveDialogue(InterpPreviewDialogueResolutionRequest request);
    void CancelPendingLoad();
    void AddLoadedLevel(InterpPreviewLoadedLevel loadedLevel);
    IList<ActorProxy> Actors { get; }
    void UnloadAllLevels(LevelEditorRenderContext renderContext);
}
