using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewRuntime : IInterpPreviewRuntime
{
    private readonly IInterpPreviewSession _session;
    private readonly IInterpPreviewRenderCoordinator _renderCoordinator;
    private readonly IInterpPreviewLoadCoordinator _loadCoordinator;
    private readonly IInterpPreviewDialogueResolver _dialogueResolver;
    private readonly IInterpPreviewDispatcher _dispatcher;
    private readonly object _operationLock = new();
    private readonly object _diagnosticsLock = new();
    private readonly List<InterpPreviewDiagnostic> _diagnostics = [];
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private InterpPreviewLifecycleState _lifecycleState = InterpPreviewLifecycleState.Open;
    private TaskCompletionSource<bool> _operationsDrained;
    private Task _shutdownTask;
    private int _acceptedOperationCount;
    private long _latestOperationId;

    public InterpPreviewRuntime(IInterpPreviewSession session, IInterpPreviewRenderCoordinator renderCoordinator, IInterpPreviewLoadCoordinator loadCoordinator)
        : this(session, renderCoordinator, loadCoordinator, new InterpPreviewDialogueResolver(), new InterpPreviewInlineDispatcher())
    {
    }

    public InterpPreviewRuntime(
        IInterpPreviewSession session,
        IInterpPreviewRenderCoordinator renderCoordinator,
        IInterpPreviewLoadCoordinator loadCoordinator,
        IInterpPreviewDialogueResolver dialogueResolver)
        : this(session, renderCoordinator, loadCoordinator, dialogueResolver, new InterpPreviewInlineDispatcher())
    {
    }

    public InterpPreviewRuntime(
        IInterpPreviewSession session,
        IInterpPreviewRenderCoordinator renderCoordinator,
        IInterpPreviewLoadCoordinator loadCoordinator,
        IInterpPreviewDialogueResolver dialogueResolver,
        IInterpPreviewDispatcher dispatcher)
    {
        _session = session;
        _renderCoordinator = renderCoordinator;
        _loadCoordinator = loadCoordinator;
        _dialogueResolver = dialogueResolver;
        _dispatcher = dispatcher;
    }

    public event EventHandler DiagnosticsChanged;

    public int LoadedLevelCount => _session.LoadedLevelCount;
    public int TotalResourceCount => _session.TotalResourceCount;
    public IList<ActorProxy> Actors => _session.Actors;
    public int LookupKeyCount => _session.LookupKeyCount;
    public IReadOnlyList<string> LoadedLevelPaths => _session.LoadedLevelPaths;
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
        _dispatcher.VerifyAccess();
        lock (_operationLock)
        {
            ThrowIfNotOpen();
            _renderCoordinator.Attach(renderContext, _session);
        }
    }

    public void Detach(LevelEditorRenderContext renderContext)
    {
        _dispatcher.VerifyAccess();
        lock (_operationLock)
        {
            if (_lifecycleState != InterpPreviewLifecycleState.Disposed)
            {
                _renderCoordinator.Detach(renderContext);
            }
        }
    }

    public Task ShutdownAsync()
    {
        lock (_operationLock)
        {
            if (_lifecycleState == InterpPreviewLifecycleState.Disposed)
            {
                return Task.CompletedTask;
            }

            if (_shutdownTask is not null)
            {
                return _shutdownTask;
            }

            _lifecycleState = InterpPreviewLifecycleState.Closing;
            _latestOperationId++;
            _loadCoordinator.CancelPendingLoad();
            _shutdownTask = _acceptedOperationCount == 0
                ? Task.CompletedTask
                : _operationsDrained.Task;
            return _shutdownTask;
        }
    }

    public async Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace)
    {
        InterpPreviewLoadResult result = await _loadCoordinator.LoadLevelAsync(path, replace, actorEditorContext, onReplace).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() =>
        {
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
        }).ConfigureAwait(false);

        return result;
    }

    public Task<InterpPreviewOperationResult> ExecuteLoadOperationAsync(string path, bool replace, IActorEditorContext actorEditorContext)
    {
        return ExecuteOperationAsync(async (operationId) =>
        {
            InterpPreviewLoadedLevel loadedLevel = null;
            try
            {
                InterpPreviewLoadResult loadResult = await _loadCoordinator.LoadLevelAsync(
                    path,
                    replace: false,
                    actorEditorContext,
                    onReplace: null,
                    () => IsOperationCurrent(operationId)).ConfigureAwait(false);
                loadedLevel = loadResult.LoadedLevel;
                return await _dispatcher.InvokeAsync(() => HandleLoadResult(operationId, loadResult, replace)).ConfigureAwait(false);
            }
            finally
            {
                if (loadedLevel is not null)
                {
                    await _dispatcher.InvokeAsync(loadedLevel.Dispose).ConfigureAwait(false);
                }
            }
        });
    }

    public Task<InterpPreviewOperationResult> ExecuteCommitOperationAsync(InterpPreviewLoadedLevel loadedLevel, bool replace)
    {
        return ExecuteOperationAsync(
            operationId => _dispatcher.InvokeAsync(() => ExecuteCommitLoadedLevelCore(operationId, loadedLevel, replace)),
            () => _dispatcher.InvokeAsync(() => loadedLevel?.Dispose()));
    }

    public InterpPreviewDialogueResolution ResolveDialogue(InterpPreviewDialogueResolutionRequest request)
    {
        _dispatcher.VerifyAccess();
        lock (_operationLock)
        {
            if (_lifecycleState != InterpPreviewLifecycleState.Open)
            {
                return InterpPreviewDialogueResolution.Unresolved([]);
            }
        }

        if (request is not null)
        {
            request.SetLoadedSceneActorLookup(_session.FindActorsByLookup);
            request.SetLoadedLevelPaths(_session.LoadedLevelPaths);
        }

        InterpPreviewDialogueResolution resolution = _dialogueResolver.Resolve(request);
        ReplaceDiagnostics(resolution.Diagnostics);
        return resolution;
    }

    public void CancelPendingLoad()
    {
        _dispatcher.VerifyAccess();
        lock (_operationLock)
        {
            if (_lifecycleState != InterpPreviewLifecycleState.Open)
            {
                return;
            }

            _latestOperationId++;
            _loadCoordinator.CancelPendingLoad();
            AddDiagnostic(InterpPreviewDiagnosticSeverity.Info, "LOAD_CANCEL_REQUESTED", "Pending load cancellation requested.");
        }
    }

    public IReadOnlyList<ActorProxy> AddLoadedLevel(InterpPreviewLoadedLevel loadedLevel)
    {
        _dispatcher.VerifyAccess();
        lock (_operationLock)
        {
            ThrowIfNotOpen();
            InterpPreviewSessionCommitResult commitResult = _session.CommitLevel(loadedLevel, replace: false);
            AddDiagnostic(InterpPreviewDiagnosticSeverity.Info, "LEVEL_ADDED", $"Loaded {loadedLevel.FullPath}.");
            return commitResult.AddedActors;
        }
    }

    public bool ContainsResource(InterpPreviewResourceKey key)
    {
        _dispatcher.VerifyAccess();
        return _session.ContainsResource(key);
    }

    public void UnloadAllLevels(LevelEditorRenderContext renderContext)
    {
        _dispatcher.VerifyAccess();
        lock (_operationLock)
        {
            if (_lifecycleState == InterpPreviewLifecycleState.Disposed)
            {
                return;
            }

            renderContext.UnloadActors(_session.Actors);
            _session.ClearLevels();
            if (_lifecycleState == InterpPreviewLifecycleState.Open)
            {
                AddDiagnostic(InterpPreviewDiagnosticSeverity.Info, "LEVELS_UNLOADED", "All loaded levels were unloaded.");
            }
        }
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        lock (_operationLock)
        {
            if (_lifecycleState == InterpPreviewLifecycleState.Disposed)
            {
                return;
            }

            if (_acceptedOperationCount != 0)
            {
                throw new InvalidOperationException("ShutdownAsync must complete before disposing the Interp Preview runtime.");
            }

            _lifecycleState = InterpPreviewLifecycleState.Disposed;
            _latestOperationId++;
        }

        _loadCoordinator.Dispose();
        _session.Dispose();
        lock (_diagnosticsLock)
        {
            _diagnostics.Clear();
        }
        _operationGate.Dispose();
    }

    private Task<InterpPreviewOperationResult> ExecuteOperationAsync(
        Func<long, Task<InterpPreviewOperationResult>> operation,
        Func<Task> cleanup = null)
    {
        long operationId = 0;
        lock (_operationLock)
        {
            if (_lifecycleState == InterpPreviewLifecycleState.Open)
            {
                _latestOperationId++;
                operationId = _latestOperationId;
                _loadCoordinator.CancelPendingLoad();
                _acceptedOperationCount++;
                if (_acceptedOperationCount == 1)
                {
                    _operationsDrained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        if (operationId == 0)
        {
            return RejectOperationAsync(cleanup);
        }

        return ExecuteAcceptedOperationAsync(operationId, operation, cleanup);
    }

    private async Task<InterpPreviewOperationResult> ExecuteAcceptedOperationAsync(
        long operationId,
        Func<long, Task<InterpPreviewOperationResult>> operation,
        Func<Task> cleanup)
    {
        bool gateAcquired = false;
        try
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            gateAcquired = true;
            if (TryGetOperationRejection(operationId, out InterpPreviewOperationResult rejection))
            {
                return rejection;
            }

            return await operation(operationId).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (cleanup is not null)
                {
                    await cleanup().ConfigureAwait(false);
                }
            }
            finally
            {
                if (gateAcquired)
                {
                    _operationGate.Release();
                }

                CompleteAcceptedOperation();
            }
        }
    }

    private InterpPreviewOperationResult HandleLoadResult(long operationId, InterpPreviewLoadResult loadResult, bool replace)
    {
        _dispatcher.VerifyAccess();
        if (TryGetOperationRejection(operationId, out InterpPreviewOperationResult rejection))
        {
            return rejection;
        }

        switch (loadResult.Outcome)
        {
            case InterpPreviewLoadOutcome.Duplicate:
                AddOperationDiagnostic(operationId, InterpPreviewDiagnosticSeverity.Warning, "LOAD_DUPLICATE", "Selected level is already loaded.");
                return InterpPreviewOperationResult.NoChange("Level already loaded.");
            case InterpPreviewLoadOutcome.Cancelled:
                AddOperationDiagnostic(operationId, InterpPreviewDiagnosticSeverity.Info, "LOAD_CANCELLED", "Level load was cancelled.");
                return InterpPreviewOperationResult.Cancelled("Level load was cancelled.");
            case InterpPreviewLoadOutcome.Superseded:
                return InterpPreviewOperationResult.Superseded("Level load was superseded.");
            case InterpPreviewLoadOutcome.Failed:
                AddOperationDiagnostic(operationId, InterpPreviewDiagnosticSeverity.Error, "LOAD_FAILED", loadResult.Error?.Message ?? "Level load failed.");
                return InterpPreviewOperationResult.Failed(loadResult.Error, "Level load failed.");
            case InterpPreviewLoadOutcome.Loaded:
                return ExecuteCommitLoadedLevelCore(operationId, loadResult.LoadedLevel, replace);
            default:
                return InterpPreviewOperationResult.Failed(null, "Unknown load outcome.");
        }
    }

    private InterpPreviewOperationResult ExecuteCommitLoadedLevelCore(long operationId, InterpPreviewLoadedLevel loadedLevel, bool replace)
    {
        _dispatcher.VerifyAccess();
        if (loadedLevel is null)
        {
            return InterpPreviewOperationResult.Failed(new ArgumentNullException(nameof(loadedLevel)), "No loaded level to commit.");
        }

        lock (_operationLock)
        {
            if (TryGetOperationRejectionNoLock(operationId, out InterpPreviewOperationResult rejection))
            {
                return rejection;
            }

            try
            {
                InterpPreviewSessionCommitResult commitResult = _session.CommitLevel(loadedLevel, replace);
                AddDiagnostic(InterpPreviewDiagnosticSeverity.Info, "LEVEL_ADDED", $"Loaded {loadedLevel.FullPath}.");
                return InterpPreviewOperationResult.Committed(
                    commitResult.AddedActors,
                    commitResult.RemovedActors,
                    commitResult.RetiredResources,
                    $"Loaded {loadedLevel.FullPath}.");
            }
            catch (Exception ex)
            {
                AddDiagnostic(InterpPreviewDiagnosticSeverity.Error, "LEVEL_COMMIT_FAILED", ex.Message);
                return InterpPreviewOperationResult.Failed(ex, "Level commit failed.");
            }
        }
    }

    private async Task<InterpPreviewOperationResult> RejectOperationAsync(Func<Task> cleanup)
    {
        if (cleanup is not null)
        {
            await cleanup().ConfigureAwait(false);
        }

        return InterpPreviewOperationResult.RejectedClosing("Preview is closing.");
    }

    private bool IsOperationCurrent(long operationId)
    {
        lock (_operationLock)
        {
            return _lifecycleState == InterpPreviewLifecycleState.Open && _latestOperationId == operationId;
        }
    }

    private bool TryGetOperationRejection(long operationId, out InterpPreviewOperationResult rejection)
    {
        lock (_operationLock)
        {
            return TryGetOperationRejectionNoLock(operationId, out rejection);
        }
    }

    private bool TryGetOperationRejectionNoLock(long operationId, out InterpPreviewOperationResult rejection)
    {
        if (_lifecycleState != InterpPreviewLifecycleState.Open)
        {
            rejection = InterpPreviewOperationResult.RejectedClosing("Preview is closing.");
            return true;
        }

        if (_latestOperationId != operationId)
        {
            rejection = InterpPreviewOperationResult.Superseded("Operation superseded before commit.");
            return true;
        }

        rejection = null;
        return false;
    }

    private void CompleteAcceptedOperation()
    {
        TaskCompletionSource<bool> operationsDrained = null;
        lock (_operationLock)
        {
            _acceptedOperationCount--;
            if (_acceptedOperationCount == 0)
            {
                operationsDrained = _operationsDrained;
            }
        }

        operationsDrained?.TrySetResult(true);
    }

    private void AddOperationDiagnostic(long operationId, InterpPreviewDiagnosticSeverity severity, string code, string message)
    {
        _dispatcher.VerifyAccess();
        lock (_operationLock)
        {
            if (!TryGetOperationRejectionNoLock(operationId, out _))
            {
                AddDiagnostic(severity, code, message);
            }
        }
    }

    private void ThrowIfNotOpen()
    {
        if (_lifecycleState != InterpPreviewLifecycleState.Open)
        {
            throw new ObjectDisposedException(nameof(InterpPreviewRuntime));
        }
    }

    private void AddDiagnostic(InterpPreviewDiagnosticSeverity severity, string code, string message)
    {
        _dispatcher.VerifyAccess();
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
        _dispatcher.VerifyAccess();
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

    private enum InterpPreviewLifecycleState
    {
        Open,
        Closing,
        Disposed
    }
}
