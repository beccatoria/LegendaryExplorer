using System;
using System.Collections.Generic;
using System.Threading;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewLevelLoader
{
    InterpPreviewLoadedLevel LoadLevel(string path, IActorEditorContext actorEditorContext, CancellationToken cancellationToken);
}

public sealed class InterpPreviewLoadedLevel : IDisposable
{
    private IMEPackage _package;
    private IReadOnlyList<ActorProxy> _actors;
    private int _ownershipState;

    public InterpPreviewLoadedLevel(
        string fullPath,
        IMEPackage package,
        IReadOnlyList<ActorProxy> actors,
        InterpPreviewResourceKind resourceKind = InterpPreviewResourceKind.Level,
        InterpPreviewPlayerVariant playerVariant = InterpPreviewPlayerVariant.None)
    {
        FullPath = fullPath;
        _package = package;
        _actors = actors ?? [];
        ResourceKind = resourceKind;
        PlayerVariant = resourceKind == InterpPreviewResourceKind.PlayerContext
            ? playerVariant
            : InterpPreviewPlayerVariant.None;
    }

    public string FullPath { get; }
    public InterpPreviewResourceKind ResourceKind { get; }
    public InterpPreviewPlayerVariant PlayerVariant { get; }
    public IMEPackage Package => _package;
    public IReadOnlyList<ActorProxy> Actors => _actors;
    public bool IsTransferred => Volatile.Read(ref _ownershipState) == 1;
    public bool IsDisposed => Volatile.Read(ref _ownershipState) == 2;

    public bool TryTransferOwnership(out IMEPackage package, out IReadOnlyList<ActorProxy> actors)
    {
        if (Interlocked.CompareExchange(ref _ownershipState, 1, 0) != 0)
        {
            package = null;
            actors = [];
            return false;
        }

        package = _package;
        actors = _actors;
        _package = null;
        _actors = [];
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _ownershipState, 2, 0) != 0)
        {
            return;
        }

        foreach (ActorProxy actor in _actors)
        {
            actor?.Dispose();
        }

        _actors = [];
        _package?.Dispose();
        _package = null;
    }

    public InterpPreviewOwnedResource ToOwnedResource()
    {
        MEGame game = _package?.Game ?? MEGame.Unknown;
        InterpPreviewResourceKey key = ResourceKind == InterpPreviewResourceKind.PlayerContext
            ? InterpPreviewResourceKey.ForPlayerContext(FullPath, game, PlayerVariant)
            : InterpPreviewResourceKey.ForLevel(FullPath, game);

        if (!TryTransferOwnership(out IMEPackage package, out IReadOnlyList<ActorProxy> actors))
        {
            throw new InvalidOperationException("Loaded level candidate ownership was already transferred or disposed.");
        }

        return new InterpPreviewOwnedResource(key, FullPath, package, actors);
    }
}
