using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LegendaryExplorer.Tools.InterpEditor;

internal static class InterpPreviewValues
{
    public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
        => Array.AsReadOnly(values?.ToArray() ?? Array.Empty<T>());
}

public sealed class InterpPreviewSourceReference
{
    public InterpPreviewSourceReference(InterpPreviewSourceObjectId owner, string property, int referencedUIndex)
    {
        Owner = owner;
        Property = property;
        ReferencedUIndex = referencedUIndex;
    }
    public InterpPreviewSourceObjectId Owner { get; }
    public string Property { get; }
    public int ReferencedUIndex { get; }
}

// These values can outlive every source package and the editor that originated the request.
public sealed class InterpPreviewResolutionStamp
{
    public InterpPreviewResolutionStamp(long requestId, Guid sourceSnapshotId,
        InterpPreviewSceneStamp? sceneStamp, InterpPreviewPlayerVariant requestedPlayerVariant)
    {
        RequestId = requestId;
        SourceSnapshotId = sourceSnapshotId;
        SceneStamp = sceneStamp;
        RequestedPlayerVariant = requestedPlayerVariant;
    }
    public long RequestId { get; }
    public Guid SourceSnapshotId { get; }
    public InterpPreviewSceneStamp? SceneStamp { get; }
    public InterpPreviewPlayerVariant RequestedPlayerVariant { get; }
}

public sealed class InterpPreviewResolutionInput
{
    public InterpPreviewResolutionInput(InterpPreviewSourceSnapshot source, InterpPreviewSequenceGraphSnapshot graph,
        InterpPreviewSceneSnapshot scene, InterpPreviewResolutionStamp stamp,
        IReadOnlyDictionary<InterpPreviewSourceObjectId, int> branchChoices)
    {
        Source = source;
        Graph = graph;
        Scene = scene;
        Stamp = stamp;
        BranchChoices = new ReadOnlyDictionary<InterpPreviewSourceObjectId, int>(branchChoices?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? []);
    }
    public InterpPreviewSourceSnapshot Source { get; }
    public InterpPreviewSequenceGraphSnapshot Graph { get; }
    public InterpPreviewSceneSnapshot Scene { get; }
    public InterpPreviewResolutionStamp Stamp { get; }
    public IReadOnlyDictionary<InterpPreviewSourceObjectId, int> BranchChoices { get; }
}

public sealed class InterpPreviewPlanInstance
{
    public InterpPreviewPlanInstance(InterpPreviewNodeId? nodeId, InterpPreviewResolutionStamp stamp,
        InterpPreviewDiscoveredInterpData discovery, InterpPreviewInterpDataSnapshot data,
        IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> actionLinks,
        InterpPreviewTrackTargetReadResult targets, InterpPreviewActorBindingResult bindings, bool complete)
    {
        NodeId = nodeId;
        Stamp = stamp;
        Discovery = discovery;
        Data = data;
        ActionLinks = InterpPreviewValues.Copy(actionLinks);
        Targets = targets;
        Bindings = bindings;
        IsComplete = complete;
    }
    public InterpPreviewNodeId? NodeId { get; }
    public InterpPreviewResolutionStamp Stamp { get; }
    public InterpPreviewDiscoveredInterpData Discovery { get; }
    public InterpPreviewSourceObjectId? ActionId => Discovery.ActionId;
    public InterpPreviewSourceObjectId InterpDataId => Discovery.InterpDataId;
    public InterpPreviewInterpDataSnapshot Data { get; }
    public IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> ActionLinks { get; }
    public InterpPreviewTrackTargetReadResult Targets { get; }
    public InterpPreviewActorBindingResult Bindings { get; }
    public bool IsComplete { get; }
}
