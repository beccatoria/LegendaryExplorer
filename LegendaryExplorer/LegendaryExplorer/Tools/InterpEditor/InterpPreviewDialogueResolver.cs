using System;
using System.Collections.Generic;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewDialogueResolver : IInterpPreviewDialogueResolver
{
    public InterpPreviewDialogueResolution Resolve(InterpPreviewDialogueResolutionRequest request)
    {
        var diagnostics = new List<InterpPreviewDiagnostic>();

        if (request is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "REQUEST_NULL", "Unable to preview line: no dialogue request was provided."));
            return InterpPreviewDialogueResolution.Unresolved(diagnostics);
        }

        ConversationExtended conversation = request.Conversation;
        DialogueNodeExtended node = request.Node;

        if (conversation is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "CONVERSATION_NULL", "Unable to preview line: conversation data is missing."));
            return InterpPreviewDialogueResolution.Unresolved(diagnostics);
        }

        if (node is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "NODE_NULL", "Unable to preview line: no dialogue line is selected."));
            return InterpPreviewDialogueResolution.Unresolved(diagnostics);
        }

        string nodeContext = BuildNodeContext(conversation, node);

        if (node.InterpData is ExportEntry directInterpData)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Info, "INTERP_DIRECT", $"Preview data found on the selected line ({nodeContext})."));
            return InterpPreviewDialogueResolution.Resolved(directInterpData, diagnostics);
        }

        try
        {
            ExportEntry fallbackInterpData = conversation.ParseSingleNodeInterpData(node);
            if (fallbackInterpData is not null)
            {
                diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_FALLBACK", $"Preview data was recovered from linked conversation data ({nodeContext})."));
                return InterpPreviewDialogueResolution.Resolved(fallbackInterpData, diagnostics);
            }

            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_MISSING", $"No preview data is linked to this line ({nodeContext})."));
            return InterpPreviewDialogueResolution.Unresolved(diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "INTERP_RESOLVE_EXCEPTION", $"Preview lookup failed for {nodeContext}: {ex.Message}"));
            return InterpPreviewDialogueResolution.Unresolved(diagnostics);
        }
    }

    private static string BuildNodeContext(ConversationExtended conversation, DialogueNodeExtended node)
    {
        string nodeType = node.IsReply ? "Reply" : "Entry";
        return $"{conversation.ConvName} | {nodeType} #{node.NodeCount} | ExportID {node.ExportID} | StrRef {node.LineStrRef}";
    }
}
