using System;
using System.Collections.Generic;
using System.Threading;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewOwnedResource : IDisposable
{
    private readonly List<ActorProxy> _actors;
    private int _disposeSignaled;

    public InterpPreviewOwnedResource(InterpPreviewResourceKey key, string displayName, IMEPackage package, IReadOnlyList<ActorProxy> actors)
    {
        Key = key;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? key.SourcePath : displayName;
        Package = package;
        _actors = actors is null ? [] : [.. actors];
    }

    public InterpPreviewResourceKey Key { get; }
    public string DisplayName { get; }
    public IMEPackage Package { get; }
    public IReadOnlyList<ActorProxy> Actors => _actors;
    public bool IsDisposed => Volatile.Read(ref _disposeSignaled) != 0;
    public Exception DisposalError { get; private set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeSignaled, 1) != 0)
        {
            return;
        }

        var errors = new List<Exception>();
        foreach (ActorProxy actor in _actors)
        {
            try
            {
                actor?.Dispose();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        _actors.Clear();

        try
        {
            Package?.Dispose();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
        if (errors.Count > 0) DisposalError = new AggregateException("Preview resource cleanup failed.", errors);
    }
}
