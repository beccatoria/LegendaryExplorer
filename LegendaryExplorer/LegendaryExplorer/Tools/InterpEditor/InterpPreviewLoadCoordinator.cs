using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewLoadCoordinator : IInterpPreviewLoadCoordinator
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly IInterpPreviewLevelLoader _levelLoader;
    private readonly IInterpPreviewPackagePreparer _packagePreparer;
    private readonly IInterpPreviewActorRealizer _actorRealizer;
    private readonly IInterpPreviewDispatcher _dispatcher;
    private readonly IInterpPreviewSession _session;
    private CancellationTokenSource _loadCancellationSource;

    public InterpPreviewLoadCoordinator(IInterpPreviewLevelLoader levelLoader, IInterpPreviewSession session)
    {
        _levelLoader = levelLoader;
        _session = session;
    }

    public InterpPreviewLoadCoordinator(
        IInterpPreviewPackagePreparer packagePreparer,
        IInterpPreviewActorRealizer actorRealizer,
        IInterpPreviewDispatcher dispatcher,
        IInterpPreviewSession session)
    {
        _packagePreparer = packagePreparer;
        _actorRealizer = actorRealizer;
        _dispatcher = dispatcher;
        _session = session;
    }

    public Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace)
    {
        return LoadLevelAsync(path, replace, actorEditorContext, onReplace, () => true);
    }

    public async Task<InterpPreviewLoadResult> LoadLevelAsync(
        string path,
        bool replace,
        IActorEditorContext actorEditorContext,
        Action onReplace,
        Func<bool> canContinue)
    {
        CancelPendingLoad();
        CancellationTokenSource cts = new();
        CancellationTokenSource previous = Interlocked.Exchange(ref _loadCancellationSource, cts);
        TryCancel(previous);

        CancellationToken token = cts.Token;
        bool lockAcquired = false;
        InterpPreviewPreparedResource preparedResource = null;
        try
        {
            await _loadGate.WaitAsync(token).ConfigureAwait(false);
            lockAcquired = true;

            if (token.IsCancellationRequested)
            {
                return InterpPreviewLoadResult.Cancelled();
            }

            path = Path.GetFullPath(path);
            if (!canContinue())
            {
                return InterpPreviewLoadResult.Superseded();
            }

            bool isDuplicate = _dispatcher is null
                ? _session.ContainsLevelPath(path)
                : await _dispatcher.InvokeAsync(() => _session.ContainsLevelPath(path), token).ConfigureAwait(false);
            if (isDuplicate)
            {
                return InterpPreviewLoadResult.Duplicate();
            }

            if (_packagePreparer is null)
            {
                if (replace)
                {
                    onReplace?.Invoke();
                }

                InterpPreviewLoadedLevel legacyLoadedLevel = await Task.Run(
                    () => _levelLoader.LoadLevel(path, actorEditorContext, token),
                    token).ConfigureAwait(false);
                return InterpPreviewLoadResult.Loaded(legacyLoadedLevel);
            }

            InterpPreviewPrepareResult prepareResult = await _packagePreparer.PrepareAsync(path, token).ConfigureAwait(false);
            preparedResource = prepareResult.PreparedResource;
            switch (prepareResult.Outcome)
            {
                case InterpPreviewPrepareOutcome.Cancelled:
                    return InterpPreviewLoadResult.Cancelled();
                case InterpPreviewPrepareOutcome.Failed:
                    return InterpPreviewLoadResult.Failed(prepareResult.Error);
                case InterpPreviewPrepareOutcome.Prepared:
                    break;
                default:
                    return InterpPreviewLoadResult.Failed(new InvalidOperationException("Unknown package preparation outcome."));
            }

            if (!canContinue())
            {
                return InterpPreviewLoadResult.Superseded();
            }

            InterpPreviewRealizeResult realizeResult = await _actorRealizer
                .RealizeAsync(preparedResource, actorEditorContext, canContinue, token)
                .ConfigureAwait(false);
            return realizeResult.Outcome switch
            {
                InterpPreviewRealizeOutcome.Loaded => InterpPreviewLoadResult.Loaded(realizeResult.LoadedLevel),
                InterpPreviewRealizeOutcome.Cancelled => InterpPreviewLoadResult.Cancelled(),
                InterpPreviewRealizeOutcome.Superseded => InterpPreviewLoadResult.Superseded(),
                InterpPreviewRealizeOutcome.Failed => InterpPreviewLoadResult.Failed(realizeResult.Error),
                _ => InterpPreviewLoadResult.Failed(new InvalidOperationException("Unknown actor realization outcome."))
            };
        }
        catch (OperationCanceledException)
        {
            return InterpPreviewLoadResult.Cancelled();
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                return InterpPreviewLoadResult.Cancelled();
            }

            return InterpPreviewLoadResult.Failed(ex);
        }
        finally
        {
            preparedResource?.Dispose();
            if (lockAcquired)
            {
                _loadGate.Release();
            }

            Interlocked.CompareExchange(ref _loadCancellationSource, null, cts);
            cts.Dispose();
        }
    }

    public void CancelPendingLoad()
    {
        CancellationTokenSource source = Volatile.Read(ref _loadCancellationSource);
        TryCancel(source);
    }

    private static void TryCancel(CancellationTokenSource source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        CancelPendingLoad();
        _loadGate.Dispose();
    }
}
