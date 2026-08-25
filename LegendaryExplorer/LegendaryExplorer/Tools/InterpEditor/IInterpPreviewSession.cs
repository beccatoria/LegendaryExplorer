using System;
using System.Collections.Generic;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewSession : IDisposable
{
    IList<ActorProxy> Actors { get; }
    int LoadedLevelCount { get; }
    bool ContainsLevelPath(string fullPath);
    void AddLevel(InterpPreviewLoadedLevel loadedLevel);
    void ClearLevels();
}
