using System;
using System.Collections.Generic;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewSourceReader
{
    InterpPreviewSourceReadResult Read(InterpPreviewSourceReadRequest request);
}

public sealed class InterpPreviewSourceReadRequest
{
    public InterpPreviewSourceReadRequest(ConversationExtended conversation, DialogueNodeExtended node)
    {
        Conversation = conversation;
        Node = node;
    }

    public ConversationExtended Conversation { get; }
    public DialogueNodeExtended Node { get; }
}

public enum InterpPreviewSourceReadOutcome
{
    Ready,
    Partial,
    Unresolved
}

public sealed class InterpPreviewSourceReadResult
{
    public InterpPreviewSourceReadResult(
        InterpPreviewSourceReadOutcome outcome,
        InterpPreviewSourceSnapshot snapshot,
        IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
    {
        Outcome = outcome;
        Snapshot = snapshot;
        Diagnostics = SnapshotList(diagnostics);
    }

    public InterpPreviewSourceReadOutcome Outcome { get; }
    public InterpPreviewSourceSnapshot Snapshot { get; }
    public IReadOnlyList<InterpPreviewDiagnostic> Diagnostics { get; }

    public static InterpPreviewSourceReadResult Unresolved(IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewSourceReadOutcome.Unresolved, snapshot: null, diagnostics);

    public static InterpPreviewSourceReadResult Ready(InterpPreviewSourceSnapshot snapshot, IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewSourceReadOutcome.Ready, snapshot, diagnostics);

    public static InterpPreviewSourceReadResult Partial(InterpPreviewSourceSnapshot snapshot, IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewSourceReadOutcome.Partial, snapshot, diagnostics);

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

public sealed class InterpPreviewNodeMetadata
{
    public InterpPreviewNodeMetadata(bool isReply, int nodeIndex, int nodeExportId, int speakerIndex, int listenerIndex, int lineStrRef)
    {
        IsReply = isReply;
        NodeIndex = nodeIndex;
        NodeExportId = nodeExportId;
        SpeakerIndex = speakerIndex;
        ListenerIndex = listenerIndex;
        LineStrRef = lineStrRef;
    }

    public bool IsReply { get; }
    public int NodeIndex { get; }
    public int NodeExportId { get; }
    public int SpeakerIndex { get; }
    public int ListenerIndex { get; }
    public int LineStrRef { get; }
}

public sealed class InterpPreviewSourceSnapshot
{
    public InterpPreviewSourceSnapshot(
        InterpPreviewSourceObjectId conversationId,
        InterpPreviewNodeId? nodeId,
        InterpPreviewSourceObjectId? sequenceId,
        InterpPreviewSourceObjectId? interpDataHintId,
        InterpPreviewNodeMetadata nodeMetadata)
    {
        ConversationId = conversationId;
        NodeId = nodeId;
        SequenceId = sequenceId;
        InterpDataHintId = interpDataHintId;
        NodeMetadata = nodeMetadata;
    }

    public InterpPreviewSourceObjectId ConversationId { get; }
    public InterpPreviewNodeId? NodeId { get; }
    public InterpPreviewSourceObjectId? SequenceId { get; }
    public InterpPreviewSourceObjectId? InterpDataHintId { get; }
    public InterpPreviewNodeMetadata NodeMetadata { get; }
}

public sealed class InterpPreviewSourceReader : IInterpPreviewSourceReader
{
    public InterpPreviewSourceReadResult Read(InterpPreviewSourceReadRequest request)
    {
        var diagnostics = new List<InterpPreviewDiagnostic>();
        if (request is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "SOURCE_REQUEST_NULL", "Source metadata request is missing."));
            return InterpPreviewSourceReadResult.Unresolved(diagnostics);
        }

        ConversationExtended conversation = request.Conversation;
        DialogueNodeExtended node = request.Node;

        if (conversation?.Export is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "SOURCE_CONVERSATION_MISSING", "Conversation export is unavailable."));
            return InterpPreviewSourceReadResult.Unresolved(diagnostics);
        }

        if (node is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "SOURCE_NODE_MISSING", "Selected dialogue node is unavailable."));
            return InterpPreviewSourceReadResult.Unresolved(diagnostics);
        }

        if (!TryCreateSourceObjectId(conversation.Export, out InterpPreviewSourceObjectId conversationId))
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "SOURCE_CONVERSATION_ID_INVALID", "Conversation identity could not be created from source metadata."));
            return InterpPreviewSourceReadResult.Unresolved(diagnostics);
        }

        int nodeExportId = ReadNodeExportId(node);
        if (nodeExportId <= 0)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_NODE_EXPORTID_MISSING", "Selected node does not have a valid nExportID; source snapshot is partial."));
        }

        InterpPreviewNodeMetadata nodeMetadata = new(
            node.IsReply,
            node.NodeCount,
            nodeExportId,
            node.SpeakerIndex,
            node.Listener,
            node.LineStrRef);

        InterpPreviewNodeId? nodeId = nodeExportId > 0
            ? new InterpPreviewNodeId(conversationId, node.IsReply, node.NodeCount, nodeExportId)
            : null;

        InterpPreviewSourceObjectId? sequenceId = null;
        if (conversation.Sequence is not null)
        {
            if (TryCreateSourceObjectId(conversation.Sequence, out InterpPreviewSourceObjectId seq))
            {
                sequenceId = seq;
            }
            else
            {
                diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_SEQUENCE_ID_INVALID", "Conversation sequence exists but does not expose a valid export identity."));
            }
        }

        InterpPreviewSourceObjectId? interpHintId = null;
        if (node.InterpData is not null)
        {
            if (TryCreateSourceObjectId(node.InterpData, out InterpPreviewSourceObjectId hint))
            {
                interpHintId = hint;
            }
            else
            {
                diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_INTERP_HINT_INVALID", "Node InterpData hint exists but does not expose a valid export identity."));
            }
        }

        var snapshot = new InterpPreviewSourceSnapshot(conversationId, nodeId, sequenceId, interpHintId, nodeMetadata);
        return nodeId.HasValue
            ? InterpPreviewSourceReadResult.Ready(snapshot, diagnostics)
            : InterpPreviewSourceReadResult.Partial(snapshot, diagnostics);
    }

    private static int ReadNodeExportId(DialogueNodeExtended node)
    {
        if (node?.NodeProp?.GetProp<IntProperty>("nExportID") is IntProperty exportIdProperty)
        {
            return exportIdProperty.Value;
        }

        return node?.ExportID ?? 0;
    }

    private static bool TryCreateSourceObjectId(IEntry entry, out InterpPreviewSourceObjectId sourceObjectId)
    {
        sourceObjectId = default;
        if (entry is null || entry.UIndex <= 0 || entry.FileRef is null)
        {
            return false;
        }

        string packagePath = entry.FileRef.FilePath;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return false;
        }

        sourceObjectId = new InterpPreviewSourceObjectId(entry.Game, packagePath, entry.UIndex);
        return true;
    }
}
