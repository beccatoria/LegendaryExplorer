using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewRuntime : IInterpPreviewRuntime
{
    private readonly IInterpPreviewSession _session;
    private readonly IInterpPreviewRenderCoordinator _renderCoordinator;
    private readonly IInterpPreviewLoadCoordinator _loadCoordinator;
    private readonly IInterpPreviewDialogueResolver _dialogueResolver;
    private readonly object _diagnosticsLock = new();
    private readonly List<InterpPreviewDiagnostic> _diagnostics = [];

    public InterpPreviewRuntime(IInterpPreviewSession session, IInterpPreviewRenderCoordinator renderCoordinator, IInterpPreviewLoadCoordinator loadCoordinator)
        : this(session, renderCoordinator, loadCoordinator, new InterpPreviewDialogueResolver())
    {
    }

    public InterpPreviewRuntime(
        IInterpPreviewSession session,
        IInterpPreviewRenderCoordinator renderCoordinator,
        IInterpPreviewLoadCoordinator loadCoordinator,
        IInterpPreviewDialogueResolver dialogueResolver)
    {
        _session = session;
        _renderCoordinator = renderCoordinator;
        _loadCoordinator = loadCoordinator;
        _dialogueResolver = dialogueResolver;
    }

    public event EventHandler DiagnosticsChanged;

    public int LoadedLevelCount => _session.LoadedLevelCount;
    public IList<ActorProxy> Actors => _session.Actors;
    public IReadOnlyList<InterpPreviewDiagnostic> Diagnostics
    {
        get
        {
            lock (_diagnosticsLock)
            {
                return _diagnostics.ToArray();
            }
        }
    }

    public void Attach(LevelEditorRenderContext renderContext)
    {
        _renderCoordinator.Attach(renderContext, _session);
    }

    public void Detach(LevelEditorRenderContext renderContext)
    {
        _renderCoordinator.Detach(renderContext);
    }

    public async Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace)
    {
        InterpPreviewLoadResult result = await _loadCoordinator.LoadLevelAsync(path, replace, actorEditorContext, onReplace).ConfigureAwait(true);
        switch (result.Outcome)
        {
            case InterpPreviewLoadOutcome.Cancelled:
                AddDiagnostic(InterpPreviewDiagnosticSeverity.Info, "LOAD_CANCELLED", "Level load was cancelled.");
                break;
            case InterpPreviewLoadOutcome.Duplicate:
                AddDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "LOAD_DUPLICATE", "Selected level is already loaded.");
                break;
            case InterpPreviewLoadOutcome.Failed:
                AddDiagnostic(InterpPreviewDiagnosticSeverity.Error, "LOAD_FAILED", result.Error?.Message ?? "Level load failed.");
                break;
            case InterpPreviewLoadOutcome.Loaded:
                AddDiagnostic(InterpPreviewDiagnosticSeverity.Info, "LOAD_READY", "Level load completed and is ready for scene integration.");
                break;
        }

        return result;
    }

    public InterpPreviewDialogueResolution ResolveDialogue(InterpPreviewDialogueResolutionRequest request)
    {
        InterpPreviewDialogueResolution resolution = _dialogueResolver.Resolve(request);
        ReplaceDiagnostics(resolution.Diagnostics);
        return resolution;
    }

    public void CancelPendingLoad()
    {
        _loadCoordinator.CancelPendingLoad();
        AddDiagnostic(InterpPreviewDiagnosticSeverity.Info, "LOAD_CANCEL_REQUESTED", "Pending load cancellation requested.");
    }

    public void AddLoadedLevel(InterpPreviewLoadedLevel loadedLevel)
    {
        _session.AddLevel(loadedLevel);
        AddDiagnostic(InterpPreviewDiagnosticSeverity.Info, "LEVEL_ADDED", $"Loaded {loadedLevel.FullPath}.");
    }

    public void UnloadAllLevels(LevelEditorRenderContext renderContext)
    {
        renderContext.UnloadLevel();
        _session.ClearLevels();
        AddDiagnostic(InterpPreviewDiagnosticSeverity.Info, "LEVELS_UNLOADED", "All loaded levels were unloaded.");
    }

    public void Dispose()
    {
        _loadCoordinator.Dispose();
        _session.Dispose();
        ReplaceDiagnostics([]);
    }

    private void AddDiagnostic(InterpPreviewDiagnosticSeverity severity, string code, string message)
    {
        lock (_diagnosticsLock)
        {
            _diagnostics.Add(new InterpPreviewDiagnostic(severity, code, message));
            if (_diagnostics.Count > 200)
            {
                _diagnostics.RemoveRange(0, _diagnostics.Count - 200);
            }
        }

        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceDiagnostics(IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
    {
        lock (_diagnosticsLock)
        {
            _diagnostics.Clear();
            if (diagnostics is not null)
            {
                _diagnostics.AddRange(diagnostics);
            }
        }

        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }
}
