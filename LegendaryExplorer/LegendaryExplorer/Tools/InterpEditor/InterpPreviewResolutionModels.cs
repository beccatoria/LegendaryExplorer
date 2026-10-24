using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public readonly struct InterpPreviewSourceObjectId : IEquatable<InterpPreviewSourceObjectId>
{
    public InterpPreviewSourceObjectId(MEGame game, string packagePath, int exportUIndex)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new ArgumentException("Package path must not be null or whitespace.", nameof(packagePath));
        }

        if (exportUIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exportUIndex), exportUIndex, "Export UIndex must be positive.");
        }

        Game = game;
        PackagePath = Path.GetFullPath(packagePath);
        ExportUIndex = exportUIndex;
    }

    public MEGame Game { get; }
    public string PackagePath { get; }
    public int ExportUIndex { get; }

    public bool Equals(InterpPreviewSourceObjectId other)
    {
        return Game == other.Game
               && ExportUIndex == other.ExportUIndex
               && string.Equals(PackagePath, other.PackagePath, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj)
    {
        return obj is InterpPreviewSourceObjectId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Game, ExportUIndex, StringComparer.OrdinalIgnoreCase.GetHashCode(PackagePath));
    }

    public override string ToString()
    {
        return $"{Game}:{PackagePath}#{ExportUIndex}";
    }
}

public readonly struct InterpPreviewSceneStamp : IEquatable<InterpPreviewSceneStamp>
{
    public InterpPreviewSceneStamp(Guid sessionId, long generation)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session id must not be empty.", nameof(sessionId));
        }

        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), generation, "Generation must not be negative.");
        }

        SessionId = sessionId;
        Generation = generation;
    }

    public Guid SessionId { get; }
    public long Generation { get; }

    public bool Equals(InterpPreviewSceneStamp other)
    {
        return SessionId.Equals(other.SessionId) && Generation == other.Generation;
    }

    public override bool Equals(object obj)
    {
        return obj is InterpPreviewSceneStamp other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(SessionId, Generation);
    }

    public override string ToString()
    {
        return $"{SessionId:N}:{Generation}";
    }
}

public readonly struct InterpPreviewActorId : IEquatable<InterpPreviewActorId>
{
    public InterpPreviewActorId(InterpPreviewResourceKey resourceKey, int actorExportUIndex)
    {
        if (actorExportUIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actorExportUIndex), actorExportUIndex, "Actor export UIndex must be positive.");
        }

        ResourceKey = resourceKey;
        ActorExportUIndex = actorExportUIndex;
        SourceObjectId = new InterpPreviewSourceObjectId(resourceKey.Game, resourceKey.SourcePath, actorExportUIndex);
    }

    public InterpPreviewResourceKey ResourceKey { get; }
    public int ActorExportUIndex { get; }
    public InterpPreviewSourceObjectId SourceObjectId { get; }

    public bool Equals(InterpPreviewActorId other)
    {
        return ResourceKey.Equals(other.ResourceKey) && ActorExportUIndex == other.ActorExportUIndex;
    }

    public override bool Equals(object obj)
    {
        return obj is InterpPreviewActorId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ResourceKey, ActorExportUIndex);
    }

    public override string ToString()
    {
        return $"{ResourceKey}#{ActorExportUIndex}";
    }
}

public readonly struct InterpPreviewNodeId : IEquatable<InterpPreviewNodeId>
{
    public InterpPreviewNodeId(InterpPreviewSourceObjectId conversationId, bool isReply, int nodeIndex, int nodeExportId)
    {
        if (nodeIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeIndex), nodeIndex, "Node index must not be negative.");
        }

        if (nodeExportId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeExportId), nodeExportId, "Node export id must be positive.");
        }

        ConversationId = conversationId;
        IsReply = isReply;
        NodeIndex = nodeIndex;
        NodeExportId = nodeExportId;
    }

    public InterpPreviewSourceObjectId ConversationId { get; }
    public bool IsReply { get; }
    public int NodeIndex { get; }
    public int NodeExportId { get; }

    public bool Equals(InterpPreviewNodeId other)
    {
        return ConversationId.Equals(other.ConversationId)
               && IsReply == other.IsReply
               && NodeIndex == other.NodeIndex
               && NodeExportId == other.NodeExportId;
    }

    public override bool Equals(object obj)
    {
        return obj is InterpPreviewNodeId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ConversationId, IsReply, NodeIndex, NodeExportId);
    }
}

public sealed class InterpPreviewSceneResourceDescriptor
{
    public InterpPreviewSceneResourceDescriptor(InterpPreviewResourceKey key, string displayName)
    {
        Key = key;
        DisplayName = displayName ?? key.SourcePath;
    }

    public InterpPreviewResourceKey Key { get; }
    public string DisplayName { get; }
}

public sealed class InterpPreviewSceneActorDescriptor
{
    public InterpPreviewSceneActorDescriptor(
        InterpPreviewActorId actorId,
        string actorTag,
        string actorName,
        bool isPlayerRole)
    {
        ActorId = actorId;
        ActorTag = actorTag;
        ActorName = actorName;
        IsPlayerRole = isPlayerRole;
    }

    public InterpPreviewActorId ActorId { get; }
    public string ActorTag { get; }
    public string ActorName { get; }
    public bool IsPlayerRole { get; }
}

public sealed class InterpPreviewSceneSnapshot
{
    private readonly IReadOnlyList<InterpPreviewSceneResourceDescriptor> _resources;
    private readonly IReadOnlyList<InterpPreviewSceneActorDescriptor> _actors;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<InterpPreviewSceneActorDescriptor>> _actorsByTag;
    private readonly IReadOnlyDictionary<InterpPreviewSourceObjectId, IReadOnlyList<InterpPreviewSceneActorDescriptor>> _actorsBySourceId;

    public InterpPreviewSceneSnapshot(
        InterpPreviewSceneStamp stamp,
        IReadOnlyList<InterpPreviewSceneResourceDescriptor> resources,
        IReadOnlyList<InterpPreviewSceneActorDescriptor> actors,
        InterpPreviewPlayerVariant loadedPlayerVariant)
    {
        Stamp = stamp;
        _resources = Snapshot(resources);
        _actors = Snapshot(actors);
        LoadedPlayerVariant = loadedPlayerVariant;
        HasPlayerRoleActors = _actors.Any(actor => actor?.IsPlayerRole == true);

        _actorsByTag = BuildTagIndex(_actors);
        _actorsBySourceId = BuildSourceIdIndex(_actors);
    }

    public InterpPreviewSceneStamp Stamp { get; }
    public InterpPreviewPlayerVariant LoadedPlayerVariant { get; }
    public bool HasPlayerRoleActors { get; }
    public IReadOnlyList<InterpPreviewSceneResourceDescriptor> Resources => _resources;
    public IReadOnlyList<InterpPreviewSceneActorDescriptor> Actors => _actors;
    public IReadOnlyDictionary<string, IReadOnlyList<InterpPreviewSceneActorDescriptor>> ActorsByTag => _actorsByTag;
    public IReadOnlyDictionary<InterpPreviewSourceObjectId, IReadOnlyList<InterpPreviewSceneActorDescriptor>> ActorsBySourceId => _actorsBySourceId;

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> source)
    {
        if (source is null || source.Count == 0)
        {
            return [];
        }

        T[] copy = new T[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            copy[i] = source[i];
        }

        return copy;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<InterpPreviewSceneActorDescriptor>> BuildTagIndex(IReadOnlyList<InterpPreviewSceneActorDescriptor> actors)
    {
        var index = new Dictionary<string, List<InterpPreviewSceneActorDescriptor>>(StringComparer.OrdinalIgnoreCase);
        foreach (InterpPreviewSceneActorDescriptor actor in actors)
        {
            if (actor is null || string.IsNullOrWhiteSpace(actor.ActorTag))
            {
                continue;
            }

            string tagKey = actor.ActorTag.Trim();
            if (tagKey.Length == 0)
            {
                continue;
            }

            if (!index.TryGetValue(tagKey, out List<InterpPreviewSceneActorDescriptor> group))
            {
                group = [];
                index[tagKey] = group;
            }

            group.Add(actor);
        }

        return index.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<InterpPreviewSceneActorDescriptor>)Snapshot(kvp.Value), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<InterpPreviewSourceObjectId, IReadOnlyList<InterpPreviewSceneActorDescriptor>> BuildSourceIdIndex(IReadOnlyList<InterpPreviewSceneActorDescriptor> actors)
    {
        var index = new Dictionary<InterpPreviewSourceObjectId, List<InterpPreviewSceneActorDescriptor>>();
        foreach (InterpPreviewSceneActorDescriptor actor in actors)
        {
            if (actor is null)
            {
                continue;
            }

            InterpPreviewSourceObjectId key = actor.ActorId.SourceObjectId;
            if (!index.TryGetValue(key, out List<InterpPreviewSceneActorDescriptor> group))
            {
                group = [];
                index[key] = group;
            }

            group.Add(actor);
        }

        return index.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<InterpPreviewSceneActorDescriptor>)Snapshot(kvp.Value));
    }
}

public enum InterpPreviewResolutionOutcome
{
    Ready,
    Partial,
    NeedsSelection,
    Unresolved,
    Stale,
    RejectedClosing
}
