using System.Collections.Generic;
using System.Threading;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewLevelLoader
{
    InterpPreviewLoadedLevel LoadLevel(string path, IActorEditorContext actorEditorContext, CancellationToken cancellationToken);
}

public sealed class InterpPreviewLoadedLevel
{
    public InterpPreviewLoadedLevel(string fullPath, IMEPackage package, IReadOnlyList<ActorProxy> actors)
    {
        FullPath = fullPath;
        Package = package;
        Actors = actors;
    }

    public string FullPath { get; }
    public IMEPackage Package { get; }
    public IReadOnlyList<ActorProxy> Actors { get; }
}
