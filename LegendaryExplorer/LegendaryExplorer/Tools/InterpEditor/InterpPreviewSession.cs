using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewSession : IInterpPreviewSession
{
    private readonly List<IMEPackage> _levelPackages = [];
    private readonly List<string> _levelPaths = [];
    private readonly List<ActorProxy> _levelActors = [];

    public IList<ActorProxy> Actors => _levelActors;
    public int LoadedLevelCount => _levelPaths.Count;

    public bool ContainsLevelPath(string fullPath)
    {
        return _levelPaths.Any(existingPath => string.Equals(existingPath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public void AddLevel(InterpPreviewLoadedLevel loadedLevel)
    {
        _levelPackages.Add(loadedLevel.Package);
        _levelPaths.Add(loadedLevel.FullPath);
        _levelActors.AddRange(loadedLevel.Actors);
    }

    public void ClearLevels()
    {
        _levelActors.Clear();
        foreach (IMEPackage package in _levelPackages)
        {
            package.Dispose();
        }

        _levelPackages.Clear();
        _levelPaths.Clear();
    }

    public void Dispose()
    {
        ClearLevels();
    }
}
