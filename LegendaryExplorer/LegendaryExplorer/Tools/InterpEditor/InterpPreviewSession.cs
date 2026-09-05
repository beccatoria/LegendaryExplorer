using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewSession : IInterpPreviewSession
{
    private readonly List<InterpPreviewOwnedResource> _resources = [];
    private readonly HashSet<InterpPreviewResourceKey> _resourceKeys = [];
    private readonly List<ActorProxy> _actors = [];
    private readonly Dictionary<string, List<ActorProxy>> _actorLookupIndex = new(StringComparer.OrdinalIgnoreCase);

    public IList<ActorProxy> Actors => _actors;
    public int LoadedLevelCount => _resources.Count(resource => resource.Key.Kind == InterpPreviewResourceKind.Level);
    public int TotalResourceCount => _resources.Count;
    public int LookupKeyCount => _actorLookupIndex.Count;
    public IReadOnlyList<string> LoadedLevelPaths => _resources
        .Where(resource => resource.Key.Kind == InterpPreviewResourceKind.Level)
        .Select(resource => resource.Key.SourcePath)
        .ToArray();

    public bool ContainsLevelPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        string normalizedPath = Path.GetFullPath(fullPath);
        return _resourceKeys.Any(existingKey =>
            existingKey.Kind == InterpPreviewResourceKind.Level
            && string.Equals(existingKey.SourcePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    public bool ContainsResource(InterpPreviewResourceKey key)
    {
        return _resourceKeys.Contains(key);
    }

    public InterpPreviewSessionCommitResult CommitLevel(InterpPreviewLoadedLevel loadedLevel, bool replace)
    {
        if (loadedLevel is null)
        {
            throw new ArgumentNullException(nameof(loadedLevel));
        }

        if (replace)
        {
            IReadOnlyList<ActorProxy> removedActors = _actors.ToArray();
            IReadOnlyList<ActorProxy> addedActors = ReplaceAllWithLevelCore(loadedLevel, out IReadOnlyList<InterpPreviewOwnedResource> retiredResources);
            return new InterpPreviewSessionCommitResult(addedActors, removedActors, retiredResources);
        }

        if (loadedLevel.ResourceKind == InterpPreviewResourceKind.PlayerContext)
        {
            return CommitPlayerContext(loadedLevel);
        }

        return new InterpPreviewSessionCommitResult(AddLevel(loadedLevel), [], []);
    }

    public IReadOnlyList<ActorProxy> AddLevel(InterpPreviewLoadedLevel loadedLevel)
    {
        if (loadedLevel is null)
        {
            throw new ArgumentNullException(nameof(loadedLevel));
        }

        InterpPreviewOwnedResource ownedResource = loadedLevel.ToOwnedResource();
        _resources.Add(ownedResource);
        _resourceKeys.Add(ownedResource.Key);
        _actors.AddRange(ownedResource.Actors);
        foreach (ActorProxy actor in ownedResource.Actors)
        {
            IndexActor(actor);
        }

        return ownedResource.Actors;
    }

    private InterpPreviewSessionCommitResult CommitPlayerContext(InterpPreviewLoadedLevel loadedLevel)
    {
        InterpPreviewOwnedResource replacement = loadedLevel.ToOwnedResource();
        var removedActors = new List<ActorProxy>();
        try
        {
            List<InterpPreviewOwnedResource> removedResources = _resources
                .Where(resource => resource.Key.Kind == InterpPreviewResourceKind.PlayerContext)
                .ToList();

            foreach (InterpPreviewOwnedResource resource in removedResources)
            {
                _resources.Remove(resource);
                _resourceKeys.Remove(resource.Key);
                foreach (ActorProxy actor in resource.Actors)
                {
                    if (actor is null)
                    {
                        continue;
                    }

                    _actors.Remove(actor);
                    removedActors.Add(actor);
                }
            }

            _resources.Add(replacement);
            _resourceKeys.Add(replacement.Key);
            _actors.AddRange(replacement.Actors);
            foreach (ActorProxy actor in replacement.Actors)
            {
                IndexActor(actor, addPlayerAlias: true);
            }

            RebuildLookupIndex();
            return new InterpPreviewSessionCommitResult(replacement.Actors, removedActors, removedResources);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    private void RebuildLookupIndex()
    {
        _actorLookupIndex.Clear();
        foreach (InterpPreviewOwnedResource resource in _resources)
        {
            bool addPlayerAlias = resource.Key.Kind == InterpPreviewResourceKind.PlayerContext;
            foreach (ActorProxy actor in resource.Actors)
            {
                IndexActor(actor, addPlayerAlias);
            }
        }
    }

    public IReadOnlyList<ActorProxy> ReplaceAllWithLevel(InterpPreviewLoadedLevel loadedLevel)
    {
        IReadOnlyList<ActorProxy> addedActors = ReplaceAllWithLevelCore(loadedLevel, out IReadOnlyList<InterpPreviewOwnedResource> retiredResources);
        foreach (InterpPreviewOwnedResource retiredResource in retiredResources)
        {
            retiredResource.Dispose();
        }

        return addedActors;
    }

    private IReadOnlyList<ActorProxy> ReplaceAllWithLevelCore(InterpPreviewLoadedLevel loadedLevel, out IReadOnlyList<InterpPreviewOwnedResource> retiredResources)
    {
        if (loadedLevel is null)
        {
            throw new ArgumentNullException(nameof(loadedLevel));
        }

        InterpPreviewOwnedResource replacement = loadedLevel.ToOwnedResource();
        try
        {
            List<InterpPreviewOwnedResource> oldResources = [.. _resources];
            var newResources = new List<InterpPreviewOwnedResource> { replacement };
            var newResourceKeys = new HashSet<InterpPreviewResourceKey> { replacement.Key };
            var newActors = new List<ActorProxy>(replacement.Actors);
            var newActorLookupIndex = new Dictionary<string, List<ActorProxy>>(StringComparer.OrdinalIgnoreCase);
            foreach (ActorProxy actor in replacement.Actors)
            {
                IndexActorInto(newActorLookupIndex, actor);
            }

            _resources.Clear();
            _resources.AddRange(newResources);

            _resourceKeys.Clear();
            _resourceKeys.UnionWith(newResourceKeys);

            _actors.Clear();
            _actors.AddRange(newActors);

            _actorLookupIndex.Clear();
            foreach ((string key, List<ActorProxy> value) in newActorLookupIndex)
            {
                _actorLookupIndex[key] = value;
            }

            retiredResources = oldResources;

            return replacement.Actors;
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    public IReadOnlyList<ActorProxy> FindActorsByLookup(string lookup)
    {
        if (string.IsNullOrWhiteSpace(lookup))
        {
            return [];
        }

        string normalized = NormalizeLookup(lookup);
        if (normalized.Length == 0)
        {
            return [];
        }

        return _actorLookupIndex.TryGetValue(normalized, out List<ActorProxy> matches)
            ? matches
            : [];
    }

    public void ClearLevels()
    {
        _actors.Clear();
        _actorLookupIndex.Clear();
        foreach (InterpPreviewOwnedResource resource in _resources)
        {
            resource.Dispose();
        }

        _resources.Clear();
        _resourceKeys.Clear();
    }

    public void Dispose()
    {
        ClearLevels();
    }

    private void IndexActor(ActorProxy actor, bool addPlayerAlias = false)
    {
        if (actor is null)
        {
            return;
        }

        IndexActorInto(_actorLookupIndex, actor, addPlayerAlias);
    }

    private static void IndexActorInto(Dictionary<string, List<ActorProxy>> actorLookupIndex, ActorProxy actor, bool addPlayerAlias = false)
    {
        if (actor is null)
        {
            return;
        }

        AddLookup(actorLookupIndex, actor.Export?.ObjectName.Instanced, actor);
        AddLookup(actorLookupIndex, actor.DisplayText, actor);
        AddLookup(actorLookupIndex, actor.Tag.Instanced, actor);
        AddLookup(actorLookupIndex, actor.Export?.ClassName, actor);
        if (addPlayerAlias)
        {
            AddLookup(actorLookupIndex, "player", actor);
        }
    }

    private static void AddLookup(Dictionary<string, List<ActorProxy>> actorLookupIndex, string rawLookup, ActorProxy actor)
    {
        string normalized = NormalizeLookup(rawLookup);
        if (normalized.Length == 0)
        {
            return;
        }

        if (!actorLookupIndex.TryGetValue(normalized, out List<ActorProxy> actors))
        {
            actors = [];
            actorLookupIndex[normalized] = actors;
        }

        if (!actors.Contains(actor))
        {
            actors.Add(actor);
        }
    }

    private static string NormalizeLookup(string lookup)
    {
        return string.IsNullOrWhiteSpace(lookup)
            ? string.Empty
            : lookup.Trim();
    }

}
