using System;
using System.Threading;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tools.LevelEditor;

/// <summary>
/// Serializes scene mutations. A replacement cancels earlier requests, but waits for their
/// workers to finish before it may unload shared GPU resources. Callbacks keep the caller's context.
/// </summary>
public sealed class LevelEditorOperationQueue
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _generation = new();
    private bool _stopped;
    private int _pending;

    public bool IsBusy { get { lock (_sync) return _pending > 0; } }

    public async Task<bool> RunAsync(bool replace, Func<CancellationToken, Task> operation)
    {
        CancellationTokenSource previous = null;
        CancellationTokenSource request;
        lock (_sync)
        {
            if (_stopped) return false;
            if (replace)
            {
                previous = _generation;
                _generation = new CancellationTokenSource();
            }
            request = CancellationTokenSource.CreateLinkedTokenSource(_generation.Token);
            _pending++;
        }
        CancelAndDispose(previous);
        bool acquired = false;
        try
        {
            await _gate.WaitAsync(request.Token);
            acquired = true;
            request.Token.ThrowIfCancellationRequested();
            await operation(request.Token);
            request.Token.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (request.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine($"Discarded failure from a cancelled scene operation: {exception}");
            return false;
        }
        finally
        {
            if (acquired) _gate.Release();
            request.Dispose();
            lock (_sync) _pending--;
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource generation;
        lock (_sync)
        {
            _stopped = true;
            generation = _generation;
            _generation = null;
        }
        CancelAndDispose(generation);
        // Cancellation alone does not mean a worker has stopped touching the renderer.
        await _gate.WaitAsync();
        _gate.Release();
    }

    private static void CancelAndDispose(CancellationTokenSource source)
    {
        if (source is null) return;
        try { source.Cancel(); }
        catch (AggregateException exception) { System.Diagnostics.Debug.WriteLine(exception); }
        finally { source.Dispose(); }
    }
}
