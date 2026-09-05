using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewRuntime : IDisposable
{
    event EventHandler DiagnosticsChanged;

    int LoadedLevelCount { get; }
    int TotalResourceCount { get; }
    int LookupKeyCount { get; }
    IReadOnlyList<InterpPreviewDiagnostic> Diagnostics { get; }
    IReadOnlyList<string> LoadedLevelPaths { get; }
    bool ContainsResource(InterpPreviewResourceKey key);

    void Attach(LevelEditorRenderContext renderContext);
    void Detach(LevelEditorRenderContext renderContext);
    Task ShutdownAsync();
    Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace);
    Task<InterpPreviewOperationResult> ExecuteLoadOperationAsync(string path, bool replace, IActorEditorContext actorEditorContext);
    Task<InterpPreviewOperationResult> ExecuteCommitOperationAsync(InterpPreviewLoadedLevel loadedLevel, bool replace);
    InterpPreviewDialogueResolution ResolveDialogue(InterpPreviewDialogueResolutionRequest request);
    void CancelPendingLoad();
    IReadOnlyList<ActorProxy> AddLoadedLevel(InterpPreviewLoadedLevel loadedLevel);
    IList<ActorProxy> Actors { get; }
    void UnloadAllLevels(LevelEditorRenderContext renderContext);
}
