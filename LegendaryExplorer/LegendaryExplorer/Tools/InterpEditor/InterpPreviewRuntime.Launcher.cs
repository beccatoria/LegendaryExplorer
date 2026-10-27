using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed partial class InterpPreviewRuntime
{
    public async Task<InterpPreviewLauncherResult> ExecuteLauncherRequestAsync(
        InterpPreviewLauncherRequest request, IActorEditorContext context, IInterpPreviewLauncherHost host)
    {
        long requestId = 0;
        InterpPreviewLauncherResult result = null;
        InterpPreviewOperationResult operation = await ExecuteOperationAsync(async id =>
        {
            requestId = id;
            try
            {
                result = await ExecuteLauncherCoreAsync(id, request, context, host).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                result = ex is OperationCanceledException
                    ? InterpPreviewLauncherResult.Cancelled("Preview request was cancelled.")
                    : InterpPreviewLauncherResult.Failed($"Unable to complete preview request: {ex.Message}");
                await _dispatcher.InvokeAsync(() => AddOperationDiagnostic(id,
                    ex is OperationCanceledException ? InterpPreviewDiagnosticSeverity.Info : InterpPreviewDiagnosticSeverity.Error,
                    ex is OperationCanceledException ? "LAUNCH_CANCELLED" : "LAUNCH_FAILED", ex.Message)).ConfigureAwait(false);
            }
            return InterpPreviewOperationResult.NoChange();
        }).ConfigureAwait(false);

        if (operation.Outcome is InterpPreviewOperationOutcome.Superseded or InterpPreviewOperationOutcome.RejectedClosing)
        {
            return LauncherRejection(operation).WithRequestId(requestId);
        }

        if (TryGetOperationRejection(requestId, out InterpPreviewOperationResult rejection))
        {
            return LauncherRejection(rejection).WithRequestId(requestId);
        }

        return (result ?? InterpPreviewLauncherResult.Failed("Preview request did not complete.")).WithRequestId(requestId);
    }

    private async Task<InterpPreviewLauncherResult> ExecuteLauncherCoreAsync(
        long requestId, InterpPreviewLauncherRequest request, IActorEditorContext context, IInterpPreviewLauncherHost host)
    {
        if (request is null || host is null
            || !InterpPreviewLauncherLevelBatch.TryNormalizeDistinctLevelPaths(request.LevelPaths, out IReadOnlyList<string> paths))
        {
            return InterpPreviewLauncherResult.Failed("Preview request contained invalid level paths or missing context.");
        }

        var candidates = new List<InterpPreviewLoadedLevel>();
        int loadedCount = 0;
        bool sceneCommitted = false;
        try
        {
            if (!request.ClearScene)
            {
                // Retained paths are acknowledged by the coordinator as duplicates; every
                // new candidate is staged before any part of the committed scene changes.
                foreach (string path in paths)
                {
                    if (TryGetOperationRejection(requestId, out var rejection)) return LauncherRejection(rejection);
                    InterpPreviewLoadResult load = await _loadCoordinator.LoadLevelAsync(path, false, context, null,
                        () => IsOperationCurrent(requestId)).ConfigureAwait(false);
                    if (load.LoadedLevel is not null) candidates.Add(load.LoadedLevel);
                    if (TryGetOperationRejection(requestId, out rejection)) return LauncherRejection(rejection);
                    switch (load.Outcome)
                    {
                        case InterpPreviewLoadOutcome.Duplicate:
                        case InterpPreviewLoadOutcome.Loaded when load.LoadedLevel is not null:
                            loadedCount++;
                            break;
                        case InterpPreviewLoadOutcome.Cancelled:
                            return InterpPreviewLauncherResult.Cancelled("Preview request was cancelled.");
                        case InterpPreviewLoadOutcome.Superseded:
                            return InterpPreviewLauncherResult.Superseded("Preview request was superseded.");
                        default:
                            throw load.Error ?? new InvalidOperationException("Unable to stage all requested levels.");
                    }
                }
            }

            if (request.EnsurePlayerContext && !request.ClearScene)
            {
                Task<InterpPreviewLoadedLevel> preparation = await _dispatcher.InvokeAsync(() =>
                    IsOperationCurrent(requestId)
                        ? host.PreparePlayerAsync(request, () => IsOperationCurrent(requestId))
                        : Task.FromResult<InterpPreviewLoadedLevel>(null)).ConfigureAwait(false);
                InterpPreviewLoadedLevel player = await preparation.ConfigureAwait(false);
                if (player is not null) candidates.Add(player);
                if (TryGetOperationRejection(requestId, out var rejection)) return LauncherRejection(rejection);
            }

            IReadOnlyList<string> desiredPaths = request.ClearScene ? Array.Empty<string>()
                : request.ReplaceOnFirstLevel && paths.Count > 0 ? paths : null;
            InterpPreviewOperationResult commit = await CommitCollectionAndPublishAsync(requestId, candidates,
                desiredPaths, host, updateCamera: paths.Count > 0).ConfigureAwait(false);
            if (commit.Outcome is not (InterpPreviewOperationOutcome.Committed or InterpPreviewOperationOutcome.NoChange))
                return LauncherRejection(commit);
            sceneCommitted = commit.Outcome == InterpPreviewOperationOutcome.Committed;

            return await _dispatcher.InvokeAsync(() =>
            {
                lock (_operationLock)
                {
                    if (TryGetOperationRejectionNoLock(requestId, out var rejection)) return LauncherRejection(rejection);
                    InterpPreviewDialogueResolution resolution = null;
                    if (request.ResolveDialogue && !request.ClearScene)
                    {
                        var resolutionRequest = new InterpPreviewDialogueResolutionRequest(request.Conversation, request.Node)
                        {
                            PreferFemalePlayer = request.PreferFemalePlayer ?? true,
                            RequestId = requestId,
                            CanContinue = () => IsOperationCurrent(requestId)
                        };
                        resolution = ResolveDialogue(resolutionRequest);
                        // A diagnostics subscriber may synchronously accept another request.
                        if (TryGetOperationRejectionNoLock(requestId, out rejection)) return LauncherRejection(rejection);
                        host.PublishDialogue(resolution, request);
                    }
                    return InterpPreviewLauncherResult.Completed(loadedCount, paths.Count, false, resolution,
                        request.ClearScene ? "Unloaded all levels." : $"Loaded {loadedCount}/{paths.Count} level files.", sceneChanged: commit.Outcome == InterpPreviewOperationOutcome.Committed);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() =>
            {
                _dispatcher.VerifyAccess();
                var errors = new List<Exception>();
                foreach (InterpPreviewLoadedLevel candidate in candidates)
                {
                    try { candidate.Dispose(); }
                    catch (Exception ex) { errors.Add(ex); }
                }
                if (errors.Count > 0)
                {
                    var failure = new AggregateException("Preview candidate cleanup failed.", errors);
                    if (!sceneCommitted) throw failure;
                    AddOperationDiagnostic(requestId, InterpPreviewDiagnosticSeverity.Warning, "RESOURCE_CLEANUP_FAILED", failure.ToString());
                }
            }).ConfigureAwait(false);
        }
    }

    private Task<InterpPreviewOperationResult> CommitCollectionAndPublishAsync(long requestId,
        IReadOnlyList<InterpPreviewLoadedLevel> candidates, IReadOnlyList<string> desiredPaths,
        IInterpPreviewLauncherHost host, bool updateCamera)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            _dispatcher.VerifyAccess();
            lock (_operationLock)
            {
                if (TryGetOperationRejectionNoLock(requestId, out var rejection)) return rejection;
                InterpPreviewSessionCommitResult change;
                try
                {
                    change = _session.CommitCollection(candidates, desiredPaths, (delta, commitScene) =>
                        host.ApplySceneChange(InterpPreviewOperationResult.Committed(delta.AddedActors,
                            delta.RemovedActors, delta.RetiredResources), () =>
                        {
                            // Includes synchronous reentrancy from a host/registration callback.
                            if (!IsOperationCurrent(requestId)) throw new OperationCanceledException("Scene request is no longer current.");
                            commitScene();
                        }));
                }
                catch (Exception ex)
                {
                    if (TryGetOperationRejectionNoLock(requestId, out rejection)) return rejection;
                    AddOperationDiagnostic(requestId, InterpPreviewDiagnosticSeverity.Error, "SCENE_COMMIT_FAILED", ex.ToString());
                    return InterpPreviewOperationResult.Failed(ex, "Scene replacement failed; the previous scene was retained.");
                }
                if (!change.Changed) return InterpPreviewOperationResult.NoChange("Requested scene is already loaded.");
                var result = InterpPreviewOperationResult.Committed(change.AddedActors, change.RemovedActors,
                    change.RetiredResources, "Preview scene updated.");
                // Retirement is after unregister/publication and stays within the accepted operation.
                // A cleanup failure must not undo, or falsely label as failed, a valid new scene.
                foreach (InterpPreviewOwnedResource retired in change.RetiredResources)
                {
                    retired.Dispose();
                    if (retired.DisposalError is not null)
                        AddOperationDiagnostic(requestId, InterpPreviewDiagnosticSeverity.Warning, "RESOURCE_CLEANUP_FAILED",
                            retired.DisposalError.ToString());
                }
                if (IsOperationCurrent(requestId))
                {
                    try { host.PublishSceneChange(result, updateCamera); }
                    catch (Exception ex)
                    {
                        AddOperationDiagnostic(requestId, InterpPreviewDiagnosticSeverity.Warning, "SCENE_FEEDBACK_FAILED", ex.Message);
                    }
                }
                AddOperationDiagnostic(requestId, InterpPreviewDiagnosticSeverity.Info, "SCENE_COMMITTED", result.Message);
                return result;
            }
        });
    }

    private static InterpPreviewLauncherResult LauncherRejection(InterpPreviewOperationResult result) => result.Outcome switch
    {
        InterpPreviewOperationOutcome.RejectedClosing => InterpPreviewLauncherResult.RejectedClosing(result.Message),
        InterpPreviewOperationOutcome.Superseded => InterpPreviewLauncherResult.Superseded(result.Message),
        InterpPreviewOperationOutcome.Cancelled => InterpPreviewLauncherResult.Cancelled(result.Message),
        _ => InterpPreviewLauncherResult.Failed(result.Error?.Message ?? result.Message)
    };
}
