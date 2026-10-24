using System;
using System.Collections.Generic;
using System.Linq;

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
        IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
    {
        Outcome = outcome;
        Candidates = SnapshotList(candidates);
        Diagnostics = SnapshotList(diagnostics);
    }

    public InterpPreviewNodeDiscoveryOutcome Outcome { get; }
    public IReadOnlyList<InterpPreviewDiscoveredInterpData> Candidates { get; }
    public IReadOnlyList<InterpPreviewDiagnostic> Diagnostics { get; }

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

        return copy;
    }
}

public sealed class InterpPreviewSequenceGraphSnapshot
{
    private readonly IReadOnlyList<InterpPreviewSequenceNodeSnapshot> _nodes;
    private readonly IReadOnlyDictionary<int, InterpPreviewSequenceNodeSnapshot> _nodesByExportUIndex;

    public InterpPreviewSequenceGraphSnapshot(
        InterpPreviewSourceObjectId sequenceId,
        IReadOnlyList<InterpPreviewSequenceNodeSnapshot> nodes)
    {
        SequenceId = sequenceId;
        _nodes = SnapshotNodes(nodes);

        var byIndex = new Dictionary<int, InterpPreviewSequenceNodeSnapshot>(_nodes.Count);
        foreach (InterpPreviewSequenceNodeSnapshot node in _nodes)
        {
            if (!byIndex.TryAdd(node.NodeId.ExportUIndex, node))
            {
                throw new ArgumentException($"Duplicate node export UIndex '{node.NodeId.ExportUIndex}' in sequence graph snapshot.", nameof(nodes));
            }
        }

        _nodesByExportUIndex = byIndex;
    }

    public InterpPreviewSourceObjectId SequenceId { get; }
    public IReadOnlyList<InterpPreviewSequenceNodeSnapshot> Nodes => _nodes;

    public bool TryGetNode(int exportUIndex, out InterpPreviewSequenceNodeSnapshot node)
    {
        return _nodesByExportUIndex.TryGetValue(exportUIndex, out node);
    }

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

        return copy;
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

        return copy;
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

        return copy;
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

        return copy;
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

        return copy;
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
        bool incomplete = false;
        int visitedObjects = 0;
        int visitedEdges = 0;

        foreach (InterpPreviewSequenceNodeSnapshot root in roots)
        {
            var queue = new Queue<InterpPreviewSequenceNodeSnapshot>();
            var visited = new HashSet<int>();
            var incomingEdgesByNode = new Dictionary<int, HashSet<InterpPreviewNodeDiscoveryEdge>>();

            queue.Enqueue(root);
            visited.Add(root.NodeId.ExportUIndex);
            visitedObjects++;

            if (visitedObjects > request.MaxVisitedObjects)
            {
                diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_OBJECT_LIMIT", "Node discovery hit the traversal object limit before completion."));
                incomplete = true;
                break;
            }

            while (queue.Count > 0)
            {
                InterpPreviewSequenceNodeSnapshot current = queue.Dequeue();

                if (string.Equals(current.ClassName, "BioSeqAct_EndCurrentConvNode", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ReferenceEquals(current, root)
                    && string.Equals(current.ClassName, "BioSeqEvt_ConvNode", StringComparison.Ordinal)
                    && current.ConversationNodeId.GetValueOrDefault() != nodeExportId)
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Info, "DISCOVERY_STOPPED_AT_OTHER_NODE", $"Traversal stopped at conversation node event #{current.NodeId.ExportUIndex} for a different node id."));
                    continue;
                }

                if (string.Equals(current.ClassName, "SeqAct_Interp", StringComparison.Ordinal))
                {
                    CollectInterpDataCandidates(
                        request.SequenceGraph,
                        root,
                        current,
                        incomingEdgesByNode,
                        candidates,
                        diagnostics);
                }

                for (int outputIndex = 0; outputIndex < current.OutputLinks.Count; outputIndex++)
                {
                    InterpPreviewOutputLinkSnapshot output = current.OutputLinks[outputIndex];
                    visitedEdges++;
                    if (visitedEdges > request.MaxVisitedEdges)
                    {
                        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_EDGE_LIMIT", "Node discovery hit the traversal edge limit before completion."));
                        incomplete = true;
                        queue.Clear();
                        break;
                    }

                    if (output.DestinationNodeExportUIndex <= 0)
                    {
                        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_EDGE_INVALID_TARGET", "An output link had an invalid destination node index."));
                        continue;
                    }

                    if (!request.SequenceGraph.TryGetNode(output.DestinationNodeExportUIndex, out InterpPreviewSequenceNodeSnapshot destination))
                    {
                        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_EDGE_TARGET_MISSING", $"Output link points to missing node #{output.DestinationNodeExportUIndex}."));
                        continue;
                    }

                    var provenanceEdge = new InterpPreviewNodeDiscoveryEdge(
                        current.NodeId,
                        output.SourceOutputIndex,
                        output.SourceOutputLabel,
                        destination.NodeId,
                        output.DestinationInputIndex);

                    if (!incomingEdgesByNode.TryGetValue(destination.NodeId.ExportUIndex, out HashSet<InterpPreviewNodeDiscoveryEdge> incomingEdges))
                    {
                        incomingEdges = new HashSet<InterpPreviewNodeDiscoveryEdge>();
                        incomingEdgesByNode[destination.NodeId.ExportUIndex] = incomingEdges;
                    }

                    incomingEdges.Add(provenanceEdge);

                    if (visited.Add(destination.NodeId.ExportUIndex))
                    {
                        queue.Enqueue(destination);
                        visitedObjects++;
                        if (visitedObjects > request.MaxVisitedObjects)
                        {
                            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_OBJECT_LIMIT", "Node discovery hit the traversal object limit before completion."));
                            incomplete = true;
                            queue.Clear();
                            break;
                        }
                    }
                    else if (destination.NodeId.ExportUIndex == current.NodeId.ExportUIndex)
                    {
                        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_CYCLE_SELF", $"Traversal encountered a self-cycle at node #{current.NodeId.ExportUIndex}."));
                    }
                    else
                    {
                        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_CYCLE", $"Traversal encountered a cycle edge from node #{current.NodeId.ExportUIndex} to previously visited node #{destination.NodeId.ExportUIndex}."));
                    }
                }
            }
        }

        List<InterpPreviewDiscoveredInterpData> discovered = candidates.Values
            .Select(static c => c.Build())
            .OrderBy(static c => c.IsHintOnly)
            .ThenBy(c => c.ActionId?.ExportUIndex ?? int.MaxValue)
            .ThenBy(static c => c.InterpDataId.ExportUIndex)
            .ToList();

        ApplyHintReconciliation(source, discovered, diagnostics, ref incomplete);

        if (incomplete)
        {
            return InterpPreviewNodeDiscoveryResult.Partial(discovered, diagnostics);
        }

        if (discovered.Count == 0)
        {
            return InterpPreviewNodeDiscoveryResult.Unresolved(diagnostics);
        }

        return InterpPreviewNodeDiscoveryResult.Ready(discovered, diagnostics);
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
            return;
        }

        discovered.Add(new InterpPreviewDiscoveredInterpData(actionId: null, interpDataId: hintId, isHintOnly: true, evidence: []));
        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_HINT_ONLY", "Graph discovery returned no InterpData; using node InterpData as a partial hint."));
        incomplete = true;
    }

    private static void CollectInterpDataCandidates(
        InterpPreviewSequenceGraphSnapshot graph,
        InterpPreviewSequenceNodeSnapshot root,
        InterpPreviewSequenceNodeSnapshot action,
        Dictionary<int, HashSet<InterpPreviewNodeDiscoveryEdge>> incomingEdgesByNode,
        Dictionary<(int actionUIndex, int dataUIndex), CandidateBuilder> candidates,
        List<InterpPreviewDiagnostic> diagnostics)
    {
        for (int variableLinkIndex = 0; variableLinkIndex < action.VariableLinks.Count; variableLinkIndex++)
        {
            InterpPreviewVariableLinkSnapshot variableLink = action.VariableLinks[variableLinkIndex];
            if (!string.Equals(variableLink.LinkDesc, "Data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (int linkedVariableIndex = 0; linkedVariableIndex < variableLink.LinkedNodeExportUIndexes.Count; linkedVariableIndex++)
            {
                int dataUIndex = variableLink.LinkedNodeExportUIndexes[linkedVariableIndex];
                if (dataUIndex <= 0)
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_DATA_LINK_INVALID", $"SeqAct_Interp #{action.NodeId.ExportUIndex} has an invalid Data link index."));
                    continue;
                }

                if (!graph.TryGetNode(dataUIndex, out InterpPreviewSequenceNodeSnapshot dataNode))
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_DATA_LINK_MISSING", $"SeqAct_Interp #{action.NodeId.ExportUIndex} references missing Data node #{dataUIndex}."));
                    continue;
                }

                if (!string.Equals(dataNode.ClassName, "InterpData", StringComparison.Ordinal))
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_DATA_LINK_TYPE", $"SeqAct_Interp #{action.NodeId.ExportUIndex} Data link #{dataUIndex} is '{dataNode.ClassName}' instead of InterpData."));
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
