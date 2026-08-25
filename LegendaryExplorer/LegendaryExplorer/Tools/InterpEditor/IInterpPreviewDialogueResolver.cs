using System.Collections.Generic;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewDialogueResolver
{
    InterpPreviewDialogueResolution Resolve(InterpPreviewDialogueResolutionRequest request);
}

public sealed class InterpPreviewDialogueResolutionRequest
{
    public InterpPreviewDialogueResolutionRequest(ConversationExtended conversation, DialogueNodeExtended node)
    {
        Conversation = conversation;
        Node = node;
    }

    public ConversationExtended Conversation { get; }
    public DialogueNodeExtended Node { get; }
}

public sealed class InterpPreviewDialogueResolution
{
    public InterpPreviewDialogueResolution(ExportEntry interpData, IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
    {
        InterpData = interpData;
        Diagnostics = diagnostics;
    }

    public ExportEntry InterpData { get; }
    public IReadOnlyList<InterpPreviewDiagnostic> Diagnostics { get; }
    public bool IsResolved => InterpData is not null;

    public static InterpPreviewDialogueResolution Resolved(ExportEntry interpData, IReadOnlyList<InterpPreviewDiagnostic> diagnostics = null)
    {
        return new InterpPreviewDialogueResolution(interpData, diagnostics ?? []);
    }

    public static InterpPreviewDialogueResolution Unresolved(IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
    {
        return new InterpPreviewDialogueResolution(null, diagnostics ?? []);
    }
}

public sealed class InterpPreviewDiagnostic
{
    public InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity severity, string code, string message)
    {
        Severity = severity;
        Code = code;
        Message = message;
    }

    public InterpPreviewDiagnosticSeverity Severity { get; }
    public string Code { get; }
    public string Message { get; }
}

public enum InterpPreviewDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
