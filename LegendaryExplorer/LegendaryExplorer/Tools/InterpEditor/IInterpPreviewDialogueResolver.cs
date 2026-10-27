using System;
using System.Collections.Generic;
using System.Threading;
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
    private InterpPreviewSceneSnapshot _sceneSnapshot;

    public InterpPreviewDialogueResolutionRequest(ConversationExtended conversation, DialogueNodeExtended node)
    {
        Conversation = conversation;
        Node = node;
    }

    public ConversationExtended Conversation { get; }
    public DialogueNodeExtended Node { get; }
    public IReadOnlyList<string> LoadedLevelPaths => _loadedLevelPaths;
    public InterpPreviewSceneSnapshot SceneSnapshot => _sceneSnapshot;
    public bool PreferFemalePlayer { get; internal set; } = true;
    public CancellationToken CancellationToken { get; init; }
    public long RequestId { get; internal set; }
    internal Func<bool> CanContinue { get; set; } = () => true;
    public IReadOnlyDictionary<InterpPreviewSourceObjectId, int> BranchChoices { get; init; }

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

    internal void SetSceneSnapshot(InterpPreviewSceneSnapshot sceneSnapshot)
    {
        _sceneSnapshot = sceneSnapshot;
    }
}

public sealed class InterpPreviewDialogueResolution
{
    public InterpPreviewDialogueResolution(InterpPreviewResolutionOutcome outcome,
        IReadOnlyList<InterpPreviewPlanInstance> plans, IReadOnlyList<InterpPreviewDiagnostic> diagnostics,
        InterpPreviewResolutionStamp stamp = null, InterpPreviewNodeDiscoveryResult discovery = null,
        IReadOnlyList<InterpPreviewSourceReference> unresolvedReferences = null,
        InterpPreviewResolutionInput input = null)
    {
        Outcome = outcome;
        Plans = InterpPreviewValues.Copy(plans);
        Diagnostics = InterpPreviewValues.Copy(diagnostics);
        Stamp = stamp;
        Discovery = discovery;
        UnresolvedReferences = InterpPreviewValues.Copy(unresolvedReferences);
        Input = input;
    }
    public InterpPreviewResolutionOutcome Outcome { get; }
    public IReadOnlyList<InterpPreviewPlanInstance> Plans { get; }
    public IReadOnlyList<InterpPreviewDiagnostic> Diagnostics { get; }
    public InterpPreviewResolutionStamp Stamp { get; }
    public InterpPreviewNodeDiscoveryResult Discovery { get; }
    public IReadOnlyList<InterpPreviewSourceReference> UnresolvedReferences { get; }
    public InterpPreviewResolutionInput Input { get; }
    public bool HasPreviewData => System.Linq.Enumerable.Any(Plans, plan => plan.Data is not null);
    public static InterpPreviewDialogueResolution Unresolved(IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewResolutionOutcome.Unresolved, [], diagnostics);
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
