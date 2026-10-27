using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewNodeDiscovery
{
    InterpPreviewNodeDiscoveryResult Discover(InterpPreviewNodeDiscoveryRequest request);
}

public sealed class InterpPreviewNodeDiscoveryRequest
{
    public InterpPreviewNodeDiscoveryRequest(
        InterpPreviewSourceSnapshot sourceSnapshot,
        InterpPreviewSequenceGraphSnapshot sequenceGraph,
        int maxVisitedObjects = 10_000,
        int maxVisitedEdges = 50_000)
    {
        SourceSnapshot = sourceSnapshot;
        SequenceGraph = sequenceGraph;
        MaxVisitedObjects = maxVisitedObjects;
        MaxVisitedEdges = maxVisitedEdges;
    }

    public InterpPreviewSourceSnapshot SourceSnapshot { get; }
    public InterpPreviewSequenceGraphSnapshot SequenceGraph { get; }
    public int MaxVisitedObjects { get; }
    public int MaxVisitedEdges { get; }
    public CancellationToken CancellationToken { get; init; }
    public IReadOnlyDictionary<InterpPreviewSourceObjectId, int> BranchChoices { get; init; }
}

public enum InterpPreviewNodeDiscoveryOutcome
{
    Ready,
    Partial,
    Unresolved
}

public sealed class InterpPreviewNodeDiscoveryResult
{
    public InterpPreviewNodeDiscoveryResult(
        InterpPreviewNodeDiscoveryOutcome outcome,
        IReadOnlyList<InterpPreviewDiscoveredInterpData> candidates,
        IReadOnlyList<InterpPreviewDiagnostic> diagnostics,
        IReadOnlyList<InterpPreviewNodeDiscoveryEdge> edges = null, bool requiresSelection = false)
    {
        Outcome = outcome;
        Candidates = SnapshotList(candidates);
        Diagnostics = SnapshotList(diagnostics);
        Edges = SnapshotList(edges);
        RequiresSelection = requiresSelection;
    }

    public InterpPreviewNodeDiscoveryOutcome Outcome { get; }
    public IReadOnlyList<InterpPreviewDiscoveredInterpData> Candidates { get; }
    public IReadOnlyList<InterpPreviewDiagnostic> Diagnostics { get; }
    public IReadOnlyList<InterpPreviewNodeDiscoveryEdge> Edges { get; }
    public bool RequiresSelection { get; }

    public static InterpPreviewNodeDiscoveryResult Unresolved(IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewNodeDiscoveryOutcome.Unresolved, [], diagnostics);

    public static InterpPreviewNodeDiscoveryResult Ready(
        IReadOnlyList<InterpPreviewDiscoveredInterpData> candidates,
        IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewNodeDiscoveryOutcome.Ready, candidates, diagnostics);

    public static InterpPreviewNodeDiscoveryResult Partial(
        IReadOnlyList<InterpPreviewDiscoveredInterpData> candidates,
        IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewNodeDiscoveryOutcome.Partial, candidates, diagnostics);

    private static IReadOnlyList<T> SnapshotList<T>(IReadOnlyList<T> source)
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

        return Array.AsReadOnly(copy);
    }
}

public sealed class InterpPreviewSequenceGraphSnapshot
{
    private readonly IReadOnlyList<InterpPreviewSequenceNodeSnapshot> _nodes;
    private readonly IReadOnlyDictionary<InterpPreviewSourceObjectId, InterpPreviewSequenceNodeSnapshot> _nodesById;

    public InterpPreviewSequenceGraphSnapshot(
        InterpPreviewSourceObjectId sequenceId,
        IReadOnlyList<InterpPreviewSequenceNodeSnapshot> nodes, bool isComplete = true)
    {
        SequenceId = sequenceId;
        IsComplete = isComplete;
        _nodes = SnapshotNodes(nodes);

        var byIndex = new Dictionary<InterpPreviewSourceObjectId, InterpPreviewSequenceNodeSnapshot>(_nodes.Count);
        foreach (InterpPreviewSequenceNodeSnapshot node in _nodes)
        {
            if (node.NodeId.Game != sequenceId.Game
                || !string.Equals(node.NodeId.PackagePath, sequenceId.PackagePath, StringComparison.OrdinalIgnoreCase)
                || !byIndex.TryAdd(node.NodeId, node))
            {
                throw new ArgumentException($"Duplicate or out-of-scope source identity '{node.NodeId}' in sequence graph snapshot.", nameof(nodes));
            }
        }

        _nodesById = new ReadOnlyDictionary<InterpPreviewSourceObjectId, InterpPreviewSequenceNodeSnapshot>(byIndex);
    }

    public InterpPreviewSourceObjectId SequenceId { get; }
    public IReadOnlyList<InterpPreviewSequenceNodeSnapshot> Nodes => _nodes;
    public bool IsComplete { get; }

    public bool TryGetNode(int exportUIndex, out InterpPreviewSequenceNodeSnapshot node)
    {
        node = null;
        return exportUIndex > 0 && TryGetNode(new InterpPreviewSourceObjectId(SequenceId.Game, SequenceId.PackagePath, exportUIndex), out node);
    }
    public bool TryGetNode(InterpPreviewSourceObjectId id, out InterpPreviewSequenceNodeSnapshot node)
        => _nodesById.TryGetValue(id, out node);

    private static IReadOnlyList<InterpPreviewSequenceNodeSnapshot> SnapshotNodes(IReadOnlyList<InterpPreviewSequenceNodeSnapshot> nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return [];
        }

        InterpPreviewSequenceNodeSnapshot[] copy = new InterpPreviewSequenceNodeSnapshot[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            copy[i] = nodes[i] ?? throw new ArgumentException("Graph node snapshot must not be null.", nameof(nodes));
        }

        return Array.AsReadOnly(copy);
    }
}

public sealed class InterpPreviewSequenceNodeSnapshot
{
    private readonly IReadOnlyList<InterpPreviewOutputLinkSnapshot> _outputLinks;
    private readonly IReadOnlyList<InterpPreviewVariableLinkSnapshot> _variableLinks;

    public InterpPreviewSequenceNodeSnapshot(
        InterpPreviewSourceObjectId nodeId,
        string className,
        int? conversationNodeId,
        IReadOnlyList<InterpPreviewOutputLinkSnapshot> outputLinks,
        IReadOnlyList<InterpPreviewVariableLinkSnapshot> variableLinks)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("Class name must not be null or whitespace.", nameof(className));
        }

        NodeId = nodeId;
        ClassName = className;
        ConversationNodeId = conversationNodeId;
        _outputLinks = SnapshotList(outputLinks);
        _variableLinks = SnapshotList(variableLinks);
    }

    public InterpPreviewSourceObjectId NodeId { get; }
    public string ClassName { get; }
    public int? ConversationNodeId { get; }
    public IReadOnlyList<InterpPreviewOutputLinkSnapshot> OutputLinks => _outputLinks;
    public IReadOnlyList<InterpPreviewVariableLinkSnapshot> VariableLinks => _variableLinks;

    private static IReadOnlyList<T> SnapshotList<T>(IReadOnlyList<T> source)
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

        return Array.AsReadOnly(copy);
    }
}

public sealed class InterpPreviewOutputLinkSnapshot
{
    public InterpPreviewOutputLinkSnapshot(int sourceOutputIndex, string sourceOutputLabel, int destinationNodeExportUIndex, int destinationInputIndex)
    {
        SourceOutputIndex = sourceOutputIndex;
        SourceOutputLabel = sourceOutputLabel ?? string.Empty;
        DestinationNodeExportUIndex = destinationNodeExportUIndex;
        DestinationInputIndex = destinationInputIndex;
    }

    public int SourceOutputIndex { get; }
    public string SourceOutputLabel { get; }
    public int DestinationNodeExportUIndex { get; }
    public int DestinationInputIndex { get; }
}

public sealed class InterpPreviewVariableLinkSnapshot
{
    private readonly IReadOnlyList<int> _linkedNodeExportUIndexes;

    public InterpPreviewVariableLinkSnapshot(string linkDesc, IReadOnlyList<int> linkedNodeExportUIndexes)
    {
        LinkDesc = linkDesc ?? string.Empty;
        _linkedNodeExportUIndexes = SnapshotLinkedNodes(linkedNodeExportUIndexes);
    }

    public string LinkDesc { get; }
    public IReadOnlyList<int> LinkedNodeExportUIndexes => _linkedNodeExportUIndexes;

    private static IReadOnlyList<int> SnapshotLinkedNodes(IReadOnlyList<int> source)
    {
        if (source is null || source.Count == 0)
        {
            return [];
        }

        int[] copy = new int[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            copy[i] = source[i];
        }

        return Array.AsReadOnly(copy);
    }
}

public sealed class InterpPreviewDiscoveredInterpData
{
    private readonly IReadOnlyList<InterpPreviewNodeDiscoveryEvidence> _evidence;

    public InterpPreviewDiscoveredInterpData(
        InterpPreviewSourceObjectId? actionId,
        InterpPreviewSourceObjectId interpDataId,
        bool isHintOnly,
        IReadOnlyList<InterpPreviewNodeDiscoveryEvidence> evidence)
    {
        ActionId = actionId;
        InterpDataId = interpDataId;
        IsHintOnly = isHintOnly;
        _evidence = SnapshotEvidence(evidence);
    }

    public InterpPreviewSourceObjectId? ActionId { get; }
    public InterpPreviewSourceObjectId InterpDataId { get; }
    public bool IsHintOnly { get; }
    public IReadOnlyList<InterpPreviewNodeDiscoveryEvidence> Evidence => _evidence;

    private static IReadOnlyList<InterpPreviewNodeDiscoveryEvidence> SnapshotEvidence(IReadOnlyList<InterpPreviewNodeDiscoveryEvidence> source)
    {
        if (source is null || source.Count == 0)
        {
            return [];
        }

        InterpPreviewNodeDiscoveryEvidence[] copy = new InterpPreviewNodeDiscoveryEvidence[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            copy[i] = source[i];
        }

        return Array.AsReadOnly(copy);
    }
}

public sealed class InterpPreviewNodeDiscoveryEvidence
{
    private readonly IReadOnlyList<InterpPreviewNodeDiscoveryEdge> _incomingActionEdges;

    public InterpPreviewNodeDiscoveryEvidence(
        InterpPreviewSourceObjectId rootNodeId,
        InterpPreviewSourceObjectId actionNodeId,
        int variableLinkIndex,
        int linkedVariableIndex,
        IReadOnlyList<InterpPreviewNodeDiscoveryEdge> incomingActionEdges)
    {
        RootNodeId = rootNodeId;
        ActionNodeId = actionNodeId;
        VariableLinkIndex = variableLinkIndex;
        LinkedVariableIndex = linkedVariableIndex;
        _incomingActionEdges = SnapshotList(incomingActionEdges);
    }

    public InterpPreviewSourceObjectId RootNodeId { get; }
    public InterpPreviewSourceObjectId ActionNodeId { get; }
    public int VariableLinkIndex { get; }
    public int LinkedVariableIndex { get; }
    public IReadOnlyList<InterpPreviewNodeDiscoveryEdge> IncomingActionEdges => _incomingActionEdges;

    private static IReadOnlyList<T> SnapshotList<T>(IReadOnlyList<T> source)
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

        return Array.AsReadOnly(copy);
    }
}

public sealed class InterpPreviewNodeDiscoveryEdge : IEquatable<InterpPreviewNodeDiscoveryEdge>
{
    public InterpPreviewNodeDiscoveryEdge(
        InterpPreviewSourceObjectId fromNodeId,
        int sourceOutputIndex,
        string sourceOutputLabel,
        InterpPreviewSourceObjectId toNodeId,
        int destinationInputIndex)
    {
        FromNodeId = fromNodeId;
        SourceOutputIndex = sourceOutputIndex;
        SourceOutputLabel = sourceOutputLabel ?? string.Empty;
        ToNodeId = toNodeId;
        DestinationInputIndex = destinationInputIndex;
    }

    public InterpPreviewSourceObjectId FromNodeId { get; }
    public int SourceOutputIndex { get; }
    public string SourceOutputLabel { get; }
    public InterpPreviewSourceObjectId ToNodeId { get; }
    public int DestinationInputIndex { get; }

    public bool Equals(InterpPreviewNodeDiscoveryEdge other)
    {
        return other is not null
               && FromNodeId.Equals(other.FromNodeId)
               && SourceOutputIndex == other.SourceOutputIndex
               && string.Equals(SourceOutputLabel, other.SourceOutputLabel, StringComparison.Ordinal)
               && ToNodeId.Equals(other.ToNodeId)
               && DestinationInputIndex == other.DestinationInputIndex;
    }

    public override bool Equals(object obj)
    {
        return obj is InterpPreviewNodeDiscoveryEdge other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FromNodeId, SourceOutputIndex, SourceOutputLabel, ToNodeId, DestinationInputIndex);
    }
}

public sealed class InterpPreviewNodeDiscovery : IInterpPreviewNodeDiscovery
{
    public InterpPreviewNodeDiscoveryResult Discover(InterpPreviewNodeDiscoveryRequest request)
    {
        var diagnostics = new List<InterpPreviewDiagnostic>();

        if (request is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "DISCOVERY_REQUEST_NULL", "Node discovery request is missing."));
            return InterpPreviewNodeDiscoveryResult.Unresolved(diagnostics);
        }

        if (request.SourceSnapshot is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "DISCOVERY_SOURCE_MISSING", "Source snapshot is missing."));
            return InterpPreviewNodeDiscoveryResult.Unresolved(diagnostics);
        }
        if (request.CancellationToken.IsCancellationRequested)
            return InterpPreviewNodeDiscoveryResult.Partial([], [new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_CANCELLED", "Discovery was cancelled before traversal.")]);

        if (request.SequenceGraph is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "DISCOVERY_GRAPH_MISSING", "Sequence graph snapshot is missing."));
            return BuildHintAwareFallback(request.SourceSnapshot, diagnostics);
        }

        if (request.MaxVisitedObjects <= 0)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "DISCOVERY_LIMIT_OBJECTS_INVALID", "Traversal object limit must be greater than zero."));
            return InterpPreviewNodeDiscoveryResult.Unresolved(diagnostics);
        }

        if (request.MaxVisitedEdges <= 0)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "DISCOVERY_LIMIT_EDGES_INVALID", "Traversal edge limit must be greater than zero."));
            return InterpPreviewNodeDiscoveryResult.Unresolved(diagnostics);
        }

        InterpPreviewSourceSnapshot source = request.SourceSnapshot;
        if (source.NodeId is { } nodeIdentity && !nodeIdentity.ConversationId.Equals(source.ConversationId)
            || source.ConversationId.Game != request.SequenceGraph.SequenceId.Game)
            return InterpPreviewNodeDiscoveryResult.Unresolved([new(InterpPreviewDiagnosticSeverity.Error, "DISCOVERY_SOURCE_MISMATCH", "Node, conversation and graph source identities disagree.")]);
        if (!source.SequenceId.HasValue)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_SEQUENCE_ID_MISSING", "Source snapshot does not include a sequence identity."));
            return BuildHintAwareFallback(source, diagnostics);
        }

        if (!source.SequenceId.Value.Equals(request.SequenceGraph.SequenceId))
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_SEQUENCE_MISMATCH", "Source snapshot sequence does not match the supplied graph snapshot."));
            return BuildHintAwareFallback(source, diagnostics);
        }

        int nodeExportId = source.NodeId?.NodeExportId ?? source.NodeMetadata?.NodeExportId ?? 0;
        if (nodeExportId <= 0)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_NODE_EXPORTID_MISSING", "Selected node does not have a valid nExportID; graph discovery was skipped."));
            return BuildHintAwareFallback(source, diagnostics);
        }

        List<InterpPreviewSequenceNodeSnapshot> roots = request.SequenceGraph.Nodes
            .Where(static n => string.Equals(n.ClassName, "BioSeqEvt_ConvNode", StringComparison.Ordinal))
            .Where(n => n.ConversationNodeId.HasValue && n.ConversationNodeId.Value == nodeExportId)
            .OrderBy(n => n.NodeId.ExportUIndex)
            .ToList();

        if (roots.Count == 0)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_ROOT_NOT_FOUND", "No matching BioSeqEvt_ConvNode was found for the selected node in the selected sequence scope."));
            return BuildHintAwareFallback(source, diagnostics);
        }

        if (roots.Count > 1)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_DUPLICATE_ROOTS", $"Found {roots.Count} matching BioSeqEvt_ConvNode roots for this node."));
        }

        var candidates = new Dictionary<(int actionUIndex, int dataUIndex), CandidateBuilder>();
        bool incomplete = !request.SequenceGraph.IsComplete;
        bool requiresSelection = roots.Count > 1;
        int visitedObjects = 0, visitedEdges = 0;
        var allEdges = new HashSet<InterpPreviewNodeDiscoveryEdge>();
        var choices = request.BranchChoices?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<InterpPreviewSourceObjectId, int>();
        foreach (var choice in choices)
        {
            if (!request.SequenceGraph.TryGetNode(choice.Key, out var chosenNode)
                || !chosenNode.OutputLinks.Any(output => output.SourceOutputIndex == choice.Value))
            {
                diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Error, "DISCOVERY_BRANCH_INVALID", "A branch choice does not belong to this source graph."));
                return InterpPreviewNodeDiscoveryResult.Partial([], diagnostics);
            }
        }

        foreach (InterpPreviewSequenceNodeSnapshot root in roots)
        {
            var queue = new Queue<InterpPreviewSequenceNodeSnapshot>();
            var reached = new Dictionary<int, InterpPreviewSequenceNodeSnapshot>();
            var incoming = new Dictionary<int, HashSet<InterpPreviewNodeDiscoveryEdge>>();
            var rootEdges = new List<InterpPreviewNodeDiscoveryEdge>();
            if (++visitedObjects > request.MaxVisitedObjects)
            {
                diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_OBJECT_LIMIT", "Discovery reached the object limit before another root."));
                incomplete = true;
                break;
            }
            queue.Enqueue(root);
            reached.Add(root.NodeId.ExportUIndex, root);
            bool stopped = false;
            while (queue.Count > 0 && !stopped)
            {
                if (request.CancellationToken.IsCancellationRequested)
                {
                    diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_CANCELLED", "Discovery was cancelled; collected candidates are incomplete."));
                    incomplete = stopped = true;
                    break;
                }
                var current = queue.Dequeue();
                if (current.ClassName == "BioSeqAct_EndCurrentConvNode") continue;
                if (current != root && current.ClassName == "BioSeqEvt_ConvNode"
                    && current.ConversationNodeId.GetValueOrDefault() != nodeExportId)
                {
                    diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Info, "DISCOVERY_STOPPED_AT_OTHER_NODE", "Traversal stopped at another conversation line."));
                    continue;
                }
                if (current.ClassName is "Sequence" or "SequenceReference" || current.ClassName.StartsWith("SFXSceneShop", StringComparison.Ordinal))
                {
                    diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning,
                        current.ClassName.StartsWith("SFXSceneShop", StringComparison.Ordinal) ? "SCENE_SHOP_UNSUPPORTED" : "SEQUENCE_BOUNDARY_UNSUPPORTED",
                        $"No verified connector adapter exists for {current.NodeId} ({current.ClassName})."));
                    incomplete = true;
                    continue;
                }
                bool hasChoice = choices.TryGetValue(current.NodeId, out int selectedOutput);
                if (!hasChoice && current.OutputLinks.Select(output => output.SourceOutputIndex).Distinct().Take(2).Count() > 1)
                    requiresSelection = true;
                foreach (var output in current.OutputLinks)
                {
                    if (hasChoice && output.SourceOutputIndex != selectedOutput) continue;
                    if (++visitedEdges > request.MaxVisitedEdges)
                    {
                        diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_EDGE_LIMIT", "Discovery reached the edge limit."));
                        incomplete = stopped = true;
                        break;
                    }
                    if (output.SourceOutputIndex < 0 || output.DestinationInputIndex < 0)
                    {
                        incomplete = true;
                        diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_EDGE_CONNECTOR_INVALID", $"{current.NodeId} has an unreadable output/input connector index."));
                        continue;
                    }
                    if (output.DestinationNodeExportUIndex <= 0)
                    {
                        diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_EDGE_INVALID_TARGET",
                            $"{current.NodeId} output {output.SourceOutputIndex} retains unresolved reference {output.DestinationNodeExportUIndex}."));
                        incomplete = true;
                        continue;
                    }
                    if (!request.SequenceGraph.TryGetNode(output.DestinationNodeExportUIndex, out var destination))
                    {
                        diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_EDGE_TARGET_MISSING",
                            $"{current.NodeId} output points outside the proven scope: #{output.DestinationNodeExportUIndex}."));
                        incomplete = true;
                        continue;
                    }
                    if (!reached.ContainsKey(destination.NodeId.ExportUIndex))
                    {
                        if (++visitedObjects > request.MaxVisitedObjects)
                        {
                            diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_OBJECT_LIMIT", "Discovery reached the object limit."));
                            incomplete = stopped = true;
                            break;
                        }
                        reached.Add(destination.NodeId.ExportUIndex, destination);
                        queue.Enqueue(destination);
                    }
                    var edge = new InterpPreviewNodeDiscoveryEdge(current.NodeId, output.SourceOutputIndex,
                        output.SourceOutputLabel, destination.NodeId, output.DestinationInputIndex);
                    if (!incoming.TryGetValue(destination.NodeId.ExportUIndex, out var edges))
                        incoming[destination.NodeId.ExportUIndex] = edges = [];
                    edges.Add(edge);
                    rootEdges.Add(edge);
                    allEdges.Add(edge);
                }
            }
            // Project only after reachability is complete: a later, longer route may add
            // another incoming edge to an action that was processed much earlier.
            foreach (var action in reached.Values.Where(node => node.ClassName == "SeqAct_Interp"))
                incomplete |= !CollectInterpDataCandidates(request.SequenceGraph, root, action, incoming, candidates, diagnostics);
            if (HasCycle(rootEdges, request.CancellationToken))
            {
                incomplete = true;
                diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_CYCLE", "The reachable graph contains a cycle; repeated runtime executions are not simulated."));
            }
            if (stopped) break;
        }
        if (choices.Keys.Any(id => !allEdges.Any(edge => edge.FromNodeId.Equals(id))))
        {
            incomplete = true;
            diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_BRANCH_UNREACHABLE", "A supplied branch choice was outside the reachable selected-line graph."));
        }
        requiresSelection |= candidates.Keys.GroupBy(key => key.actionUIndex).Any(group => group.Count() > 1);

        List<InterpPreviewDiscoveredInterpData> discovered = candidates.Values
            .Select(static c => c.Build())
            .OrderBy(static c => c.IsHintOnly)
            .ThenBy(c => c.ActionId?.ExportUIndex ?? int.MaxValue)
            .ThenBy(static c => c.InterpDataId.ExportUIndex)
            .ToList();

        ApplyHintReconciliation(source, discovered, diagnostics, ref incomplete);

        if (incomplete)
        {
            return new InterpPreviewNodeDiscoveryResult(InterpPreviewNodeDiscoveryOutcome.Partial, discovered, diagnostics, allEdges.ToArray(), requiresSelection);
        }

        if (discovered.Count == 0)
        {
            return new(InterpPreviewNodeDiscoveryOutcome.Unresolved, discovered, diagnostics, allEdges.ToArray(), requiresSelection);
        }

        return new InterpPreviewNodeDiscoveryResult(InterpPreviewNodeDiscoveryOutcome.Ready, discovered, diagnostics, allEdges.ToArray(), requiresSelection);
    }

    private static InterpPreviewNodeDiscoveryResult BuildHintAwareFallback(InterpPreviewSourceSnapshot source, List<InterpPreviewDiagnostic> diagnostics)
    {
        if (source?.InterpDataHintId is InterpPreviewSourceObjectId hintId)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_HINT_ONLY", "Graph discovery was unavailable; using node InterpData as a partial hint."));

            var hintOnly = new InterpPreviewDiscoveredInterpData(
                actionId: null,
                interpDataId: hintId,
                isHintOnly: true,
                evidence: []);

            return InterpPreviewNodeDiscoveryResult.Partial([hintOnly], diagnostics);
        }

        return InterpPreviewNodeDiscoveryResult.Unresolved(diagnostics);
    }

    private static void ApplyHintReconciliation(
        InterpPreviewSourceSnapshot source,
        List<InterpPreviewDiscoveredInterpData> discovered,
        List<InterpPreviewDiagnostic> diagnostics,
        ref bool incomplete)
    {
        if (source?.InterpDataHintId is not InterpPreviewSourceObjectId hintId)
        {
            return;
        }

        bool hintFound = discovered.Any(c => c.InterpDataId.Equals(hintId));
        if (hintFound)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Info, "INTERP_HINT_MATCH", "Node InterpData hint matches discovered graph data."));
            return;
        }

        if (discovered.Count > 0)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_HINT_CONFLICT", "Node InterpData hint differs from discovered graph data."));
            incomplete = true;
            return;
        }

        discovered.Add(new InterpPreviewDiscoveredInterpData(actionId: null, interpDataId: hintId, isHintOnly: true, evidence: []));
        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_HINT_ONLY", "Graph discovery returned no InterpData; using node InterpData as a partial hint."));
        incomplete = true;
    }

    private static bool CollectInterpDataCandidates(
        InterpPreviewSequenceGraphSnapshot graph,
        InterpPreviewSequenceNodeSnapshot root,
        InterpPreviewSequenceNodeSnapshot action,
        Dictionary<int, HashSet<InterpPreviewNodeDiscoveryEdge>> incomingEdgesByNode,
        Dictionary<(int actionUIndex, int dataUIndex), CandidateBuilder> candidates,
        List<InterpPreviewDiagnostic> diagnostics)
    {
        bool complete = true;
        bool hasData = false;
        for (int variableLinkIndex = 0; variableLinkIndex < action.VariableLinks.Count; variableLinkIndex++)
        {
            InterpPreviewVariableLinkSnapshot variableLink = action.VariableLinks[variableLinkIndex];
            if (!string.Equals(variableLink.LinkDesc, "Data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            hasData = variableLink.LinkedNodeExportUIndexes.Count > 0 || hasData;
            for (int linkedVariableIndex = 0; linkedVariableIndex < variableLink.LinkedNodeExportUIndexes.Count; linkedVariableIndex++)
            {
                int dataUIndex = variableLink.LinkedNodeExportUIndexes[linkedVariableIndex];
                if (dataUIndex <= 0)
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_DATA_LINK_INVALID", $"SeqAct_Interp #{action.NodeId.ExportUIndex} has an invalid Data link index."));
                    complete = false;
                    continue;
                }

                if (!graph.TryGetNode(dataUIndex, out InterpPreviewSequenceNodeSnapshot dataNode))
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_DATA_LINK_MISSING", $"SeqAct_Interp #{action.NodeId.ExportUIndex} references missing Data node #{dataUIndex}."));
                    complete = false;
                    continue;
                }

                if (!string.Equals(dataNode.ClassName, "InterpData", StringComparison.Ordinal))
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_DATA_LINK_TYPE", $"SeqAct_Interp #{action.NodeId.ExportUIndex} Data link #{dataUIndex} is '{dataNode.ClassName}' instead of InterpData."));
                    complete = false;
                    continue;
                }

                var key = (action.NodeId.ExportUIndex, dataNode.NodeId.ExportUIndex);
                if (!candidates.TryGetValue(key, out CandidateBuilder candidate))
                {
                    candidate = new CandidateBuilder(action.NodeId, dataNode.NodeId);
                    candidates.Add(key, candidate);
                }

                incomingEdgesByNode.TryGetValue(action.NodeId.ExportUIndex, out HashSet<InterpPreviewNodeDiscoveryEdge> incomingEdges);
                var evidence = new InterpPreviewNodeDiscoveryEvidence(
                    root.NodeId,
                    action.NodeId,
                    variableLinkIndex,
                    linkedVariableIndex,
                    incomingEdges?.OrderBy(e => e.FromNodeId.ExportUIndex).ThenBy(e => e.SourceOutputIndex).ToList() ?? []);

                candidate.AddEvidence(evidence);
            }
        }
        if (!hasData) diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_DATA_LINK_EMPTY", $"{action.NodeId} has no usable Data link."));
        return complete && hasData;
    }

    private static bool HasCycle(IReadOnlyList<InterpPreviewNodeDiscoveryEdge> edges, CancellationToken token)
    {
        var adjacency = edges.GroupBy(edge => edge.FromNodeId).ToDictionary(group => group.Key, group => group.Select(edge => edge.ToNodeId).Distinct().ToArray());
        var colors = new Dictionary<InterpPreviewSourceObjectId, int>();
        foreach (var start in adjacency.Keys)
        {
            if (colors.ContainsKey(start)) continue;
            var stack = new Stack<(InterpPreviewSourceObjectId id, bool exit)>();
            stack.Push((start, false));
            while (stack.Count > 0)
            {
                if (token.IsCancellationRequested) return false;
                var (id, exit) = stack.Pop();
                if (exit) { colors[id] = 2; continue; }
                if (colors.TryGetValue(id, out int color)) { if (color == 1) return true; continue; }
                colors[id] = 1;
                stack.Push((id, true));
                if (adjacency.TryGetValue(id, out var destinations))
                    for (int i = destinations.Length - 1; i >= 0; i--) stack.Push((destinations[i], false));
            }
        }
        return false;
    }

    private sealed class CandidateBuilder
    {
        private readonly InterpPreviewSourceObjectId _actionId;
        private readonly InterpPreviewSourceObjectId _interpDataId;
        private readonly List<InterpPreviewNodeDiscoveryEvidence> _evidence = [];
        private readonly HashSet<(int root, int action, int varIdx, int linkedIdx)> _dedupe = [];

        public CandidateBuilder(InterpPreviewSourceObjectId actionId, InterpPreviewSourceObjectId interpDataId)
        {
            _actionId = actionId;
            _interpDataId = interpDataId;
        }

        public void AddEvidence(InterpPreviewNodeDiscoveryEvidence evidence)
        {
            var key = (
                evidence.RootNodeId.ExportUIndex,
                evidence.ActionNodeId.ExportUIndex,
                evidence.VariableLinkIndex,
                evidence.LinkedVariableIndex);

            if (_dedupe.Add(key))
            {
                _evidence.Add(evidence);
            }
        }

        public InterpPreviewDiscoveredInterpData Build()
        {
            return new InterpPreviewDiscoveredInterpData(_actionId, _interpDataId, isHintOnly: false, _evidence);
        }
    }
}
