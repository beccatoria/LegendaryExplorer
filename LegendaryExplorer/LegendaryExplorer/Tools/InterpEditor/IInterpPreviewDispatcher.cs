using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewDispatcher
{
    bool CheckAccess();
    void VerifyAccess();
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
    Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);
    Task YieldAsync(CancellationToken cancellationToken = default);
}

public sealed class InterpPreviewWpfDispatcher : IInterpPreviewDispatcher
{
    private readonly Dispatcher _dispatcher;

    public InterpPreviewWpfDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void VerifyAccess()
    {
        _dispatcher.VerifyAccess();
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    public Task YieldAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _dispatcher.InvokeAsync(() => { }).Task;
    }
}

public sealed class InterpPreviewInlineDispatcher : IInterpPreviewDispatcher
{
    public bool CheckAccess() => true;

    public void VerifyAccess()
    {
    }

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(action());
    }

    public Task YieldAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
