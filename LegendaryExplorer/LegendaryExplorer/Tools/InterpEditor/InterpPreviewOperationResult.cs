using System;
using System.Collections.Generic;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public enum InterpPreviewOperationOutcome
{
    Committed,
    NoChange,
    Cancelled,
    Failed,
    Superseded,
    RejectedClosing
}

public sealed class InterpPreviewOperationResult
{
    private InterpPreviewOperationResult(
        InterpPreviewOperationOutcome outcome,
        IReadOnlyList<ActorProxy> addedActors,
        IReadOnlyList<ActorProxy> removedActors,
        IReadOnlyList<InterpPreviewOwnedResource> retiredResources,
        Exception error,
        string message)
    {
        Outcome = outcome;
        AddedActors = Snapshot(addedActors);
        RemovedActors = Snapshot(removedActors);
        RetiredResources = Snapshot(retiredResources);
        Error = error;
        Message = message;
    }

    public InterpPreviewOperationOutcome Outcome { get; }
    public IReadOnlyList<ActorProxy> AddedActors { get; }
    public IReadOnlyList<ActorProxy> RemovedActors { get; }
    public IReadOnlyList<InterpPreviewOwnedResource> RetiredResources { get; }
    public Exception Error { get; }
    public string Message { get; }

    public static InterpPreviewOperationResult Committed(
        IReadOnlyList<ActorProxy> addedActors,
        IReadOnlyList<ActorProxy> removedActors = null,
        IReadOnlyList<InterpPreviewOwnedResource> retiredResources = null,
        string message = null)
        => new(InterpPreviewOperationOutcome.Committed, addedActors, removedActors, retiredResources, null, message);

    public static InterpPreviewOperationResult NoChange(string message = null)
        => new(InterpPreviewOperationOutcome.NoChange, [], [], [], null, message);

    public static InterpPreviewOperationResult Cancelled(string message = null)
        => new(InterpPreviewOperationOutcome.Cancelled, [], [], [], null, message);

    public static InterpPreviewOperationResult Failed(Exception error, string message = null)
        => new(InterpPreviewOperationOutcome.Failed, [], [], [], error, message);

    public static InterpPreviewOperationResult Superseded(string message = null)
        => new(InterpPreviewOperationOutcome.Superseded, [], [], [], null, message);

    public static InterpPreviewOperationResult RejectedClosing(string message = null)
        => new(InterpPreviewOperationOutcome.RejectedClosing, [], [], [], null, message);

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> source)
    {
        if (source is null || source.Count == 0)
        {
            return [];
        }

        T[] snapshot = new T[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            snapshot[i] = source[i];
        }

        return snapshot;
    }
}
