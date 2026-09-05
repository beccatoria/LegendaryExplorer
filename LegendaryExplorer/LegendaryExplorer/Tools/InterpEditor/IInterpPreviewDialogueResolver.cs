using System;
using System.Collections.Generic;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.Packages;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewDialogueResolver
{
    InterpPreviewDialogueResolution Resolve(InterpPreviewDialogueResolutionRequest request);
}

public sealed class InterpPreviewDialogueResolutionRequest
{
    private Func<string, IReadOnlyList<ActorProxy>> _loadedSceneActorLookup;
    private IReadOnlyList<string> _loadedLevelPaths = [];

    public InterpPreviewDialogueResolutionRequest(ConversationExtended conversation, DialogueNodeExtended node)
    {
        Conversation = conversation;
        Node = node;
    }

    public ConversationExtended Conversation { get; }
    public DialogueNodeExtended Node { get; }
    public IReadOnlyList<string> LoadedLevelPaths => _loadedLevelPaths;
    public bool PreferFemalePlayer { get; internal set; } = true;

    public IReadOnlyList<ActorProxy> FindLoadedSceneActors(string lookup)
    {
        return _loadedSceneActorLookup?.Invoke(lookup) ?? [];
    }

    internal void SetLoadedSceneActorLookup(Func<string, IReadOnlyList<ActorProxy>> lookup)
    {
        _loadedSceneActorLookup = lookup;
    }

    internal void SetLoadedLevelPaths(IReadOnlyList<string> loadedLevelPaths)
    {
        _loadedLevelPaths = loadedLevelPaths ?? [];
    }
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
