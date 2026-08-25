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
    private readonly IInterpPreviewSession _session;
    private CancellationTokenSource _loadCancellationSource;

    public InterpPreviewLoadCoordinator(IInterpPreviewLevelLoader levelLoader, IInterpPreviewSession session)
    {
        _levelLoader = levelLoader;
        _session = session;
    }

    public async Task<InterpPreviewLoadResult> LoadLevelAsync(string path, bool replace, IActorEditorContext actorEditorContext, Action onReplace)
    {
        CancelPendingLoad();
        CancellationTokenSource cts = new();
        CancellationTokenSource previous = Interlocked.Exchange(ref _loadCancellationSource, cts);
        previous?.Dispose();

        CancellationToken token = cts.Token;
        bool lockAcquired = false;
        try
        {
            await _loadGate.WaitAsync(token).ConfigureAwait(true);
            lockAcquired = true;

            if (token.IsCancellationRequested)
            {
                return InterpPreviewLoadResult.Cancelled();
            }

            if (replace)
            {
                onReplace?.Invoke();
            }

            path = Path.GetFullPath(path);
            if (_session.ContainsLevelPath(path))
            {
                return InterpPreviewLoadResult.Duplicate();
            }

            InterpPreviewLoadedLevel loadedLevel = _levelLoader.LoadLevel(path, actorEditorContext, token);
            return InterpPreviewLoadResult.Loaded(loadedLevel);
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
            if (lockAcquired)
            {
                _loadGate.Release();
            }

            cts.Dispose();
        }
    }

    public void CancelPendingLoad()
    {
        CancellationTokenSource source = Interlocked.Exchange(ref _loadCancellationSource, null);
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

        source.Dispose();
    }

    public void Dispose()
    {
        CancelPendingLoad();
        _loadGate.Dispose();
    }
}
