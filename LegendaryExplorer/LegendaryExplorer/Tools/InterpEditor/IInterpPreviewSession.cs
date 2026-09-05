using System;
using System.Collections.Generic;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewSessionCommitResult
{
    public InterpPreviewSessionCommitResult(
        IReadOnlyList<ActorProxy> addedActors,
        IReadOnlyList<ActorProxy> removedActors,
        IReadOnlyList<InterpPreviewOwnedResource> retiredResources = null)
    {
        AddedActors = Snapshot(addedActors);
        RemovedActors = Snapshot(removedActors);
        RetiredResources = Snapshot(retiredResources);
    }

    public IReadOnlyList<ActorProxy> AddedActors { get; }
    public IReadOnlyList<ActorProxy> RemovedActors { get; }
    public IReadOnlyList<InterpPreviewOwnedResource> RetiredResources { get; }

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> source)
    {
        if (source is null || source.Count == 0)
        {
            return [];
        }

        T[] snapshot = new T[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            snapshot[i] = source[i];
        }

        return snapshot;
    }
}

public interface IInterpPreviewSession : IDisposable
{
    IList<ActorProxy> Actors { get; }
    int LoadedLevelCount { get; }
    int TotalResourceCount { get; }
    int LookupKeyCount { get; }
    IReadOnlyList<string> LoadedLevelPaths { get; }
    bool ContainsLevelPath(string fullPath);
    bool ContainsResource(InterpPreviewResourceKey key);
    IReadOnlyList<ActorProxy> FindActorsByLookup(string lookup);
    InterpPreviewSessionCommitResult CommitLevel(InterpPreviewLoadedLevel loadedLevel, bool replace);
    IReadOnlyList<ActorProxy> AddLevel(InterpPreviewLoadedLevel loadedLevel);
    IReadOnlyList<ActorProxy> ReplaceAllWithLevel(InterpPreviewLoadedLevel loadedLevel);
    void ClearLevels();
}
