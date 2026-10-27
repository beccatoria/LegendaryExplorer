using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewSession : IInterpPreviewSession
{
    private readonly Guid _sessionId = Guid.NewGuid();
    private List<InterpPreviewOwnedResource> _resources = [];
    private HashSet<InterpPreviewResourceKey> _resourceKeys = [];
    private List<ActorProxy> _actors = [];
    private Dictionary<string, List<ActorProxy>> _actorLookupIndex = new(StringComparer.OrdinalIgnoreCase);
    private long _sceneGeneration;

    public Guid SessionId => _sessionId;
    public long SceneGeneration => _sceneGeneration;
    public InterpPreviewSceneStamp SceneStamp => new(_sessionId, _sceneGeneration);
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
        ArgumentNullException.ThrowIfNull(loadedLevel);
        return CommitCollection([loadedLevel], replace ? new[] { loadedLevel.FullPath } : null,
            (_, commitScene) => commitScene());
    }

    public IReadOnlyList<ActorProxy> AddLevel(InterpPreviewLoadedLevel loadedLevel)
    {
        InterpPreviewSessionCommitResult result = CommitLevel(loadedLevel, false);
        // This session-only convenience API returns no retirement delta to its caller.
        foreach (InterpPreviewOwnedResource retired in result.RetiredResources) retired.Dispose();
        return result.AddedActors;
    }

    public IReadOnlyList<ActorProxy> ReplaceAllWithLevel(InterpPreviewLoadedLevel loadedLevel)
    {
        InterpPreviewSessionCommitResult result = CommitLevel(loadedLevel, true);
        foreach (InterpPreviewOwnedResource retired in result.RetiredResources) retired.Dispose();
        return result.AddedActors;
    }

    public InterpPreviewSessionCommitResult CommitCollection(IReadOnlyList<InterpPreviewLoadedLevel> candidates,
        IReadOnlyList<string> desiredLevelPaths, Action<InterpPreviewSessionCommitResult, Action> publish)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(publish);
        // Construct all membership/indexes before publication. Newly transferred resources
        // remain transaction-owned until registration and the final reference swap succeed.
        var created = new List<InterpPreviewOwnedResource>();
        var previousResources = _resources;
        var previousKeys = _resourceKeys;
        var previousActors = _actors;
        var previousIndex = _actorLookupIndex;
        long previousGeneration = _sceneGeneration;
        try
        {
            if (candidates.Any(candidate => candidate is null || candidate.IsDisposed || candidate.IsTransferred))
                throw new InvalidOperationException("A scene candidate is missing or no longer owned by the request.");

            var desired = desiredLevelPaths?.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var next = new List<InterpPreviewOwnedResource>(_resources);
            foreach (InterpPreviewLoadedLevel candidate in candidates)
            {
                var key = candidate.ResourceKey;
                bool duplicate = next.Any(resource => resource.Key.Equals(key)
                    || (key.Kind == InterpPreviewResourceKind.Level && resource.Key.Kind == key.Kind
                        && string.Equals(resource.Key.SourcePath, key.SourcePath, StringComparison.OrdinalIgnoreCase)));
                if (duplicate) continue; // Unused candidates remain request-owned for cleanup.
                if (key.Kind == InterpPreviewResourceKind.PlayerContext)
                    next.RemoveAll(resource => resource.Key.Kind == InterpPreviewResourceKind.PlayerContext);
                InterpPreviewOwnedResource resource = candidate.ToOwnedResource();
                created.Add(resource);
                next.Add(resource);
            }

            if (desired is not null)
            {
                var levels = desired.Select(path => next.SingleOrDefault(resource => resource.Key.Kind == InterpPreviewResourceKind.Level
                    && string.Equals(resource.Key.SourcePath, path, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"Requested level was not staged: {path}")).ToList();
                var player = next.SingleOrDefault(resource => resource.Key.Kind == InterpPreviewResourceKind.PlayerContext);
                if (player is not null && levels.Count > 0 && levels.All(level => level.Key.Game == player.Key.Game)) levels.Add(player);
                next = levels;
            }
            if (created.Any(resource => !next.Contains(resource)))
                throw new InvalidOperationException("A candidate does not belong to the requested scene.");

            // Reordering an identical set does not invalidate scene-bound results.
            if (next.Count == _resources.Count && next.All(_resources.Contains))
                return new InterpPreviewSessionCommitResult([], [], [], changed: false);

            var keys = new HashSet<InterpPreviewResourceKey>(next.Select(resource => resource.Key));
            var actors = next.SelectMany(resource => resource.Actors).ToList();
            var index = new Dictionary<string, List<ActorProxy>>(StringComparer.OrdinalIgnoreCase);
            foreach (InterpPreviewOwnedResource resource in next)
                foreach (ActorProxy actor in resource.Actors)
                    IndexActorInto(index, actor, resource.Key.Kind == InterpPreviewResourceKind.PlayerContext);
            var retired = _resources.Where(resource => !next.Contains(resource)).ToArray();
            var result = new InterpPreviewSessionCommitResult(created.SelectMany(resource => resource.Actors).ToArray(),
                retired.SelectMany(resource => resource.Actors).ToArray(), retired);
            bool committed = false;
            publish(result, () =>
            {
                if (committed) throw new InvalidOperationException("Scene publication was invoked twice.");
                _resources = next;
                _resourceKeys = keys;
                _actors = actors;
                _actorLookupIndex = index;
                _sceneGeneration = previousGeneration + 1;
                committed = true;
            });
            if (!committed) throw new InvalidOperationException("Scene registration did not publish membership.");
            return result;
        }
        catch (Exception failure)
        {
            _resources = previousResources;
            _resourceKeys = previousKeys;
            _actors = previousActors;
            _actorLookupIndex = previousIndex;
            _sceneGeneration = previousGeneration;
            foreach (InterpPreviewOwnedResource resource in created) resource.Dispose();
            var cleanupErrors = created.Where(resource => resource.DisposalError is not null)
                .Select(resource => resource.DisposalError).ToArray();
            if (cleanupErrors.Length > 0)
                throw new AggregateException("Scene publication and candidate cleanup failed.", new[] { failure }.Concat(cleanupErrors));
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
        bool hadLevels = _resources.Count > 0;
        _actors.Clear();
        _actorLookupIndex.Clear();
        foreach (InterpPreviewOwnedResource resource in _resources)
        {
            resource.Dispose();
        }

        _resources.Clear();
        _resourceKeys.Clear();

        if (hadLevels)
        {
            IncrementSceneGeneration();
        }
    }

    public InterpPreviewSceneSnapshot CreateSceneSnapshot()
    {
        var resources = new List<InterpPreviewSceneResourceDescriptor>(_resources.Count);
        var actors = new List<InterpPreviewSceneActorDescriptor>(_actors.Count);
        InterpPreviewPlayerVariant loadedPlayerVariant = InterpPreviewPlayerVariant.None;

        foreach (InterpPreviewOwnedResource resource in _resources)
        {
            resources.Add(new InterpPreviewSceneResourceDescriptor(resource.Key, resource.DisplayName));
            if (resource.Key.Kind == InterpPreviewResourceKind.PlayerContext)
            {
                loadedPlayerVariant = resource.Key.PlayerVariant;
            }

            foreach (ActorProxy actor in resource.Actors)
            {
                if (actor?.Export is null || actor.Export.UIndex <= 0)
                {
                    continue;
                }

                var actorId = new InterpPreviewActorId(resource.Key, actor.Export.UIndex);
                actors.Add(new InterpPreviewSceneActorDescriptor(
                    actorId,
                    actor.Tag.Instanced,
                    actor.DisplayText,
                    isPlayerRole: resource.Key.Kind == InterpPreviewResourceKind.PlayerContext));
            }
        }

        return new InterpPreviewSceneSnapshot(SceneStamp, resources, actors, loadedPlayerVariant);
    }

    public void Dispose()
    {
        ClearLevels();
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

    private void IncrementSceneGeneration()
    {
        _sceneGeneration++;
    }

}
