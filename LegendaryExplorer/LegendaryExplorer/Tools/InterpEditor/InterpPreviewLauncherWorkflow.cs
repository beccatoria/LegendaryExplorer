using System.Collections.Generic;
using System.Threading;
using LegendaryExplorerCore.Dialogue;

namespace LegendaryExplorer.Tools.InterpEditor;

public enum InterpPreviewLauncherOutcome
{
    Committed,
    NoChange,
    Cancelled,
    Superseded,
    Failed,
    RejectedClosing
}

public sealed class InterpPreviewLauncherRequest
{
    public InterpPreviewLauncherRequest(IReadOnlyList<string> levelPaths)
    {
        LevelPaths = levelPaths ?? [];
    }

    public IReadOnlyList<string> LevelPaths { get; }
    public bool ReplaceOnFirstLevel { get; init; } = true;
    public ConversationExtended Conversation { get; init; }
    public DialogueNodeExtended Node { get; init; }
    public bool EnsurePlayerContext { get; init; }
    public bool ResolveDialogue { get; init; }
    public bool? PreferFemalePlayer { get; init; }
}

public sealed class InterpPreviewLauncherResult
{
    private InterpPreviewLauncherResult(
        InterpPreviewLauncherOutcome outcome,
        int loadedCount,
        int totalCount,
        bool hadFailure,
        InterpPreviewDialogueResolution dialogueResolution,
        string message)
    {
        Outcome = outcome;
        LoadedCount = loadedCount;
        TotalCount = totalCount;
        HadFailure = hadFailure;
        DialogueResolution = dialogueResolution;
        Message = message;
    }

    public InterpPreviewLauncherOutcome Outcome { get; }
    public int LoadedCount { get; }
    public int TotalCount { get; }
    public bool HadFailure { get; }
    public InterpPreviewDialogueResolution DialogueResolution { get; }
    public string Message { get; }

    public static InterpPreviewLauncherResult Completed(int loadedCount, int totalCount, bool hadFailure, InterpPreviewDialogueResolution dialogueResolution = null, string message = null)
    {
        InterpPreviewLauncherOutcome outcome = hadFailure
            ? InterpPreviewLauncherOutcome.Failed
            : loadedCount > 0 || dialogueResolution is not null
                ? InterpPreviewLauncherOutcome.Committed
                : InterpPreviewLauncherOutcome.NoChange;

        return new InterpPreviewLauncherResult(outcome, loadedCount, totalCount, hadFailure, dialogueResolution, message);
    }

    public static InterpPreviewLauncherResult Cancelled(string message = null)
        => new(InterpPreviewLauncherOutcome.Cancelled, 0, 0, true, null, message);

    public static InterpPreviewLauncherResult Superseded(string message = null)
        => new(InterpPreviewLauncherOutcome.Superseded, 0, 0, true, null, message);

    public static InterpPreviewLauncherResult Failed(string message = null)
        => new(InterpPreviewLauncherOutcome.Failed, 0, 0, true, null, message);

    public static InterpPreviewLauncherResult RejectedClosing(string message = null)
        => new(InterpPreviewLauncherOutcome.RejectedClosing, 0, 0, true, null, message);
}

public sealed class InterpPreviewLauncherRequestTracker
{
    private readonly System.Action _onRequestStarted;
    private long _latestRequestId;

    public InterpPreviewLauncherRequestTracker(System.Action onRequestStarted)
    {
        _onRequestStarted = onRequestStarted;
    }

    public long BeginRequest()
    {
        _onRequestStarted?.Invoke();
        return Interlocked.Increment(ref _latestRequestId);
    }

    public bool IsCurrent(long requestId)
    {
        return Volatile.Read(ref _latestRequestId) == requestId;
    }
}
