using System;
using System.Collections.Generic;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewActorBindingResolver
{
    InterpPreviewActorBindingResult Resolve(InterpPreviewActorBindingRequest request);
}

public sealed class InterpPreviewActorBindingRequest
{
    public InterpPreviewActorBindingRequest(
        InterpPreviewSceneSnapshot scene,
        IReadOnlyList<InterpPreviewTrackTargetDescriptor> targets)
    {
        Scene = scene;
        Targets = SnapshotList(targets);
    }

    public InterpPreviewSceneSnapshot Scene { get; }
    public IReadOnlyList<InterpPreviewTrackTargetDescriptor> Targets { get; }

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

public enum InterpPreviewActorBindingOutcome
{
    Ready,
    Partial,
    Unresolved
}

public enum InterpPreviewActorBindingStatus
{
    Bound,
    Missing,
    Ambiguous,
    Unsupported
}

public sealed class InterpPreviewActorBindingResult
{
    public InterpPreviewActorBindingResult(
        InterpPreviewActorBindingOutcome outcome,
        IReadOnlyList<InterpPreviewActorBindingDescriptor> bindings,
        IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
    {
        Outcome = outcome;
        Bindings = SnapshotList(bindings);
        Diagnostics = SnapshotList(diagnostics);
    }

    public InterpPreviewActorBindingOutcome Outcome { get; }
    public IReadOnlyList<InterpPreviewActorBindingDescriptor> Bindings { get; }
    public IReadOnlyList<InterpPreviewDiagnostic> Diagnostics { get; }

    public static InterpPreviewActorBindingResult Ready(
        IReadOnlyList<InterpPreviewActorBindingDescriptor> bindings,
        IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewActorBindingOutcome.Ready, bindings, diagnostics);

    public static InterpPreviewActorBindingResult Partial(
        IReadOnlyList<InterpPreviewActorBindingDescriptor> bindings,
        IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewActorBindingOutcome.Partial, bindings, diagnostics);

    public static InterpPreviewActorBindingResult Unresolved(IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewActorBindingOutcome.Unresolved, [], diagnostics);

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

public sealed class InterpPreviewActorBindingDescriptor
{
    private readonly IReadOnlyList<InterpPreviewSceneActorDescriptor> _candidates;

    public InterpPreviewActorBindingDescriptor(
        InterpPreviewTrackTargetDescriptor target,
        InterpPreviewActorBindingStatus status,
        InterpPreviewSceneActorDescriptor selectedActor,
        IReadOnlyList<InterpPreviewSceneActorDescriptor> candidates)
    {
        Target = target;
        Status = status;
        SelectedActor = selectedActor;
        _candidates = SnapshotList(candidates);
    }

    public InterpPreviewTrackTargetDescriptor Target { get; }
    public InterpPreviewActorBindingStatus Status { get; }
    public InterpPreviewSceneActorDescriptor SelectedActor { get; }
    public IReadOnlyList<InterpPreviewSceneActorDescriptor> Candidates => _candidates;

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

public sealed class InterpPreviewActorBindingResolver : IInterpPreviewActorBindingResolver
{
    public InterpPreviewActorBindingResult Resolve(InterpPreviewActorBindingRequest request)
    {
        var diagnostics = new List<InterpPreviewDiagnostic>();
        if (request is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "ACTOR_BINDING_REQUEST_NULL", "Actor binding request is missing."));
            return InterpPreviewActorBindingResult.Unresolved(diagnostics);
        }

        if (request.Scene is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "ACTOR_BINDING_SCENE_MISSING", "Scene snapshot is missing for actor binding."));
            return InterpPreviewActorBindingResult.Unresolved(diagnostics);
        }

        if (request.Targets.Count == 0)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "ACTOR_BINDING_TARGETS_EMPTY", "No track targets were provided for actor binding."));
            return InterpPreviewActorBindingResult.Unresolved(diagnostics);
        }

        var bindings = new List<InterpPreviewActorBindingDescriptor>(request.Targets.Count);
        bool partial = false;

        for (int i = 0; i < request.Targets.Count; i++)
        {
            InterpPreviewTrackTargetDescriptor target = request.Targets[i];
            if (target is null)
            {
                diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "ACTOR_BINDING_TARGET_NULL", "A track target entry was null and could not be resolved."));
                partial = true;
                continue;
            }

            switch (target.Role)
            {
                case InterpPreviewTrackTargetRole.GroupVariable:
                case InterpPreviewTrackTargetRole.DirectTag:
                {
                    string lookupTag = NormalizeTag(target.TargetKey);
                    if (lookupTag.Length == 0)
                    {
                        bindings.Add(new InterpPreviewActorBindingDescriptor(target, InterpPreviewActorBindingStatus.Missing, selectedActor: null, candidates: []));
                        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "ACTOR_TAG_MISSING", $"Target key is empty for track #{target.TrackId.ExportUIndex}."));
                        partial = true;
                        break;
                    }

                    IReadOnlyList<InterpPreviewSceneActorDescriptor> matches = ResolveByTag(request.Scene, lookupTag);
                    if (matches.Count == 0)
                    {
                        bindings.Add(new InterpPreviewActorBindingDescriptor(target, InterpPreviewActorBindingStatus.Missing, selectedActor: null, candidates: []));
                        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "ACTOR_TAG_MISSING", $"No loaded actor matches tag '{lookupTag}' for track #{target.TrackId.ExportUIndex}."));
                        partial = true;
                        break;
                    }

                    if (matches.Count == 1)
                    {
                        bindings.Add(new InterpPreviewActorBindingDescriptor(target, InterpPreviewActorBindingStatus.Bound, selectedActor: matches[0], candidates: matches));
                        break;
                    }

                    bindings.Add(new InterpPreviewActorBindingDescriptor(target, InterpPreviewActorBindingStatus.Ambiguous, selectedActor: null, candidates: matches));
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "ACTOR_TAG_AMBIGUOUS", $"Loaded scene has {matches.Count} tag matches for '{lookupTag}' on track #{target.TrackId.ExportUIndex}."));
                    partial = true;
                    break;
                }

                default:
                    bindings.Add(new InterpPreviewActorBindingDescriptor(target, InterpPreviewActorBindingStatus.Unsupported, selectedActor: null, candidates: []));
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "ACTOR_BINDING_ROLE_UNSUPPORTED", $"Target role '{target.Role}' is not supported for track #{target.TrackId.ExportUIndex}."));
                    partial = true;
                    break;
            }
        }

        if (bindings.Count == 0)
        {
            return InterpPreviewActorBindingResult.Unresolved(diagnostics);
        }

        return partial
            ? InterpPreviewActorBindingResult.Partial(bindings, diagnostics)
            : InterpPreviewActorBindingResult.Ready(bindings, diagnostics);
    }

    private static IReadOnlyList<InterpPreviewSceneActorDescriptor> ResolveByTag(InterpPreviewSceneSnapshot scene, string tag)
    {
        if (scene.ActorsByTag is null || !scene.ActorsByTag.TryGetValue(tag, out IReadOnlyList<InterpPreviewSceneActorDescriptor> matches) || matches is null)
        {
            return [];
        }

        if (matches.Count <= 1)
        {
            return matches;
        }

        InterpPreviewSceneActorDescriptor[] ordered = new InterpPreviewSceneActorDescriptor[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            ordered[i] = matches[i];
        }

        Array.Sort(ordered, static (left, right) => CompareActors(left, right));
        return ordered;
    }

    private static int CompareActors(InterpPreviewSceneActorDescriptor left, InterpPreviewSceneActorDescriptor right)
    {
        string leftPath = left.ActorId.ResourceKey.SourcePath ?? string.Empty;
        string rightPath = right.ActorId.ResourceKey.SourcePath ?? string.Empty;
        int pathCompare = string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
        if (pathCompare != 0)
        {
            return pathCompare;
        }

        int uindexCompare = left.ActorId.ActorExportUIndex.CompareTo(right.ActorId.ActorExportUIndex);
        if (uindexCompare != 0)
        {
            return uindexCompare;
        }

        return string.Compare(left.ActorName ?? string.Empty, right.ActorName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        string trimmed = tag.Trim();
        if (string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return trimmed;
    }
}
