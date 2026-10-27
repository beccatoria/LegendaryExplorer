using System;
using System.Collections.Generic;
using System.Linq;

namespace LegendaryExplorer.Tools.InterpEditor;

public interface IInterpPreviewTrackTargetReader
{
    InterpPreviewTrackTargetReadResult ReadTargets(InterpPreviewTrackTargetReadRequest request);
}

public sealed class InterpPreviewTrackTargetReadRequest
{
    public InterpPreviewTrackTargetReadRequest(
        InterpPreviewSourceObjectId actionId,
        InterpPreviewInterpDataSnapshot interpData,
        IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> actionVariableLinks)
    {
        ActionId = actionId;
        InterpData = interpData;
        ActionVariableLinks = SnapshotList(actionVariableLinks);
    }

    public InterpPreviewSourceObjectId ActionId { get; }
    public InterpPreviewInterpDataSnapshot InterpData { get; }
    public IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> ActionVariableLinks { get; }

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

public enum InterpPreviewTrackTargetReadOutcome
{
    Ready,
    Partial,
    Unresolved
}

public enum InterpPreviewTrackTargetRole
{
    GroupVariable,
    DirectTag
}

public sealed class InterpPreviewTrackTargetReadResult
{
    public InterpPreviewTrackTargetReadResult(
        InterpPreviewTrackTargetReadOutcome outcome,
        IReadOnlyList<InterpPreviewTrackTargetDescriptor> targets,
        IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
    {
        Outcome = outcome;
        Targets = SnapshotList(targets);
        Diagnostics = SnapshotList(diagnostics);
    }

    public InterpPreviewTrackTargetReadOutcome Outcome { get; }
    public IReadOnlyList<InterpPreviewTrackTargetDescriptor> Targets { get; }
    public IReadOnlyList<InterpPreviewDiagnostic> Diagnostics { get; }

    public static InterpPreviewTrackTargetReadResult Ready(IReadOnlyList<InterpPreviewTrackTargetDescriptor> targets, IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewTrackTargetReadOutcome.Ready, targets, diagnostics);

    public static InterpPreviewTrackTargetReadResult Partial(IReadOnlyList<InterpPreviewTrackTargetDescriptor> targets, IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewTrackTargetReadOutcome.Partial, targets, diagnostics);

    public static InterpPreviewTrackTargetReadResult Unresolved(IReadOnlyList<InterpPreviewDiagnostic> diagnostics)
        => new(InterpPreviewTrackTargetReadOutcome.Unresolved, [], diagnostics);

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

public sealed class InterpPreviewTrackTargetDescriptor
{
    public InterpPreviewTrackTargetDescriptor(
        InterpPreviewSourceObjectId actionId,
        InterpPreviewSourceObjectId interpDataId,
        InterpPreviewSourceObjectId trackId,
        string trackClassName,
        string groupName,
        InterpPreviewTrackTargetRole role,
        string targetKey,
        int groupIndex,
        int trackIndex)
    {
        ActionId = actionId;
        InterpDataId = interpDataId;
        TrackId = trackId;
        TrackClassName = trackClassName ?? string.Empty;
        GroupName = groupName ?? string.Empty;
        Role = role;
        TargetKey = targetKey ?? string.Empty;
        GroupIndex = groupIndex;
        TrackIndex = trackIndex;
    }

    public InterpPreviewSourceObjectId ActionId { get; }
    public InterpPreviewSourceObjectId InterpDataId { get; }
    public InterpPreviewSourceObjectId TrackId { get; }
    public string TrackClassName { get; }
    public string GroupName { get; }
    public InterpPreviewTrackTargetRole Role { get; }
    public string TargetKey { get; }
    public int GroupIndex { get; }
    public int TrackIndex { get; }
}

public sealed class InterpPreviewInterpDataSnapshot
{
    private readonly IReadOnlyList<InterpPreviewInterpGroupSnapshot> _groups;

    public InterpPreviewInterpDataSnapshot(
        InterpPreviewSourceObjectId interpDataId,
        IReadOnlyList<InterpPreviewInterpGroupSnapshot> groups)
    {
        InterpDataId = interpDataId;
        _groups = SnapshotList(groups);
    }

    public InterpPreviewSourceObjectId InterpDataId { get; }
    public IReadOnlyList<InterpPreviewInterpGroupSnapshot> Groups => _groups;

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

public sealed class InterpPreviewInterpGroupSnapshot
{
    private readonly IReadOnlyList<InterpPreviewInterpTrackSnapshot> _tracks;

    public InterpPreviewInterpGroupSnapshot(
        InterpPreviewSourceObjectId groupId,
        string groupName,
        IReadOnlyList<InterpPreviewInterpTrackSnapshot> tracks)
    {
        GroupId = groupId;
        GroupName = groupName ?? string.Empty;
        _tracks = SnapshotList(tracks);
    }

    public InterpPreviewSourceObjectId GroupId { get; }
    public string GroupName { get; }
    public IReadOnlyList<InterpPreviewInterpTrackSnapshot> Tracks => _tracks;

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

public sealed class InterpPreviewInterpTrackSnapshot
{
    public InterpPreviewInterpTrackSnapshot(
        InterpPreviewSourceObjectId trackId,
        string className,
        string findActorTag = null)
    {
        TrackId = trackId;
        ClassName = className ?? string.Empty;
        FindActorTag = findActorTag;
    }

    public InterpPreviewSourceObjectId TrackId { get; }
    public string ClassName { get; }
    public string FindActorTag { get; }
}

public sealed class InterpPreviewActionVariableLinkSnapshot
{
    public InterpPreviewActionVariableLinkSnapshot(string linkDesc, InterpPreviewSourceObjectId? actionId = null,
        IReadOnlyList<InterpPreviewSourceReference> linkedVariables = null)
    {
        LinkDesc = linkDesc ?? string.Empty;
        ActionId = actionId;
        LinkedVariables = InterpPreviewValues.Copy(linkedVariables);
    }

    public string LinkDesc { get; }
    public InterpPreviewSourceObjectId? ActionId { get; }
    public IReadOnlyList<InterpPreviewSourceReference> LinkedVariables { get; }
}

public sealed class InterpPreviewTrackTargetReader : IInterpPreviewTrackTargetReader
{
    public InterpPreviewTrackTargetReadResult ReadTargets(InterpPreviewTrackTargetReadRequest request)
    {
        var diagnostics = new List<InterpPreviewDiagnostic>();
        if (request is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "TARGET_REQUEST_NULL", "Track target read request is missing."));
            return InterpPreviewTrackTargetReadResult.Unresolved(diagnostics);
        }

        if (request.InterpData is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "TARGET_INTERPDATA_MISSING", "InterpData snapshot is missing."));
            return InterpPreviewTrackTargetReadResult.Unresolved(diagnostics);
        }

        if (request.InterpData.Groups.Count == 0)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "TARGET_GROUPS_EMPTY", "InterpData snapshot does not contain any groups."));
            return InterpPreviewTrackTargetReadResult.Unresolved(diagnostics);
        }

        var targets = new List<InterpPreviewTrackTargetDescriptor>();
        var dedupe = new HashSet<(int trackId, string role, string key)>(StringTupleComparer.OrdinalIgnoreCase);
        var unsupportedTrackClassCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        bool partial = false;

        for (int groupIndex = 0; groupIndex < request.InterpData.Groups.Count; groupIndex++)
        {
            InterpPreviewInterpGroupSnapshot group = request.InterpData.Groups[groupIndex];
            string groupName = group.GroupName?.Trim() ?? string.Empty;
            for (int trackIndex = 0; trackIndex < group.Tracks.Count; trackIndex++)
            {
                InterpPreviewInterpTrackSnapshot track = group.Tracks[trackIndex];
                if (string.Equals(track.ClassName, "BioEvtSysTrackLookAt", StringComparison.Ordinal))
                {
                    string lookAtTag = NormalizeTag(track.FindActorTag);
                    if (!string.IsNullOrWhiteSpace(lookAtTag))
                    {
                        AddTarget(
                            targets,
                            dedupe,
                            request.ActionId,
                            request.InterpData.InterpDataId,
                            track,
                            groupName,
                            InterpPreviewTrackTargetRole.DirectTag,
                            lookAtTag,
                            groupIndex,
                            trackIndex);

                        continue;
                    }
                }

                if (string.Equals(track.ClassName, "InterpTrackMove", StringComparison.Ordinal)
                    || string.Equals(track.ClassName, "InterpTrackFloatProp", StringComparison.Ordinal)
                    || string.Equals(track.ClassName, "BioEvtSysTrackSetFacing", StringComparison.Ordinal)
                    || string.Equals(track.ClassName, "BioEvtSysTrackLookAt", StringComparison.Ordinal)
                    || string.Equals(track.ClassName, "BioEvtSysTrackSwitchCamera", StringComparison.Ordinal)
                    || string.Equals(track.ClassName, "BioEvtSysTrackDOF", StringComparison.Ordinal)
                    || string.Equals(track.ClassName, "BioEvtSysTrackVOElements", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(groupName))
                    {
                        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "TARGET_GROUPNAME_MISSING", $"{track.ClassName} #{track.TrackId.ExportUIndex} is missing GroupName."));
                        partial = true;
                        continue;
                    }

                    if (!HasActionVariableLink(request.ActionVariableLinks, groupName))
                    {
                        if (IsDirectorStyleGroup(groupName))
                        {
                            diagnostics.Add(new InterpPreviewDiagnostic(
                                InterpPreviewDiagnosticSeverity.Warning,
                                "TARGET_GROUPVAR_DIRECTOR",
                                $"Director-style group '{groupName}' has no SeqAct_Interp variable link for {track.ClassName} #{track.TrackId.ExportUIndex}; this group may be cinematic-only and not actor-bound in this action."));
                        }
                        else
                        {
                            diagnostics.Add(new InterpPreviewDiagnostic(
                                InterpPreviewDiagnosticSeverity.Warning,
                                "TARGET_GROUPVAR_MISSING",
                                $"No SeqAct_Interp variable link matches group '{groupName}' for {track.ClassName} #{track.TrackId.ExportUIndex}."));
                        }

                        partial = true;
                        continue;
                    }

                    AddTarget(
                        targets,
                        dedupe,
                        request.ActionId,
                        request.InterpData.InterpDataId,
                        track,
                        groupName,
                        InterpPreviewTrackTargetRole.GroupVariable,
                        groupName,
                        groupIndex,
                        trackIndex);

                    continue;
                }

                if (string.Equals(track.ClassName, "BioEvtSysTrackGesture", StringComparison.Ordinal))
                {
                    string tag = NormalizeTag(track.FindActorTag);
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "TARGET_GESTURE_TAG_MISSING", $"BioEvtSysTrackGesture #{track.TrackId.ExportUIndex} is missing m_nmFindActor."));
                        partial = true;
                        continue;
                    }

                    AddTarget(
                        targets,
                        dedupe,
                        request.ActionId,
                        request.InterpData.InterpDataId,
                        track,
                        groupName,
                        InterpPreviewTrackTargetRole.DirectTag,
                        tag,
                        groupIndex,
                        trackIndex);

                    continue;
                }

                if (string.Equals(track.ClassName, "InterpTrackDirector", StringComparison.Ordinal))
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(
                        InterpPreviewDiagnosticSeverity.Info,
                        "TARGET_TRACK_NONACTOR",
                        $"Track class '{track.ClassName}' controls cinematic cuts and does not provide an actor-binding target."));
                    partial = true;
                    continue;
                }

                string unsupportedClass = string.IsNullOrWhiteSpace(track.ClassName) ? "<unknown>" : track.ClassName;
                unsupportedTrackClassCounts.TryGetValue(unsupportedClass, out int currentCount);
                unsupportedTrackClassCounts[unsupportedClass] = currentCount + 1;
                partial = true;
            }
        }

        foreach ((string className, int count) in unsupportedTrackClassCounts.OrderBy(static kvp => kvp.Key, StringComparer.Ordinal))
        {
            diagnostics.Add(new InterpPreviewDiagnostic(
                InterpPreviewDiagnosticSeverity.Info,
                "TARGET_TRACK_UNSUPPORTED",
                count == 1
                    ? $"Track class '{className}' is not yet supported for target extraction."
                    : $"Track class '{className}' is not yet supported for target extraction ({count} track(s))."));
        }

        if (targets.Count == 0)
        {
            return partial
                ? InterpPreviewTrackTargetReadResult.Partial(targets, diagnostics)
                : InterpPreviewTrackTargetReadResult.Unresolved(diagnostics);
        }

        return partial
            ? InterpPreviewTrackTargetReadResult.Partial(targets, diagnostics)
            : InterpPreviewTrackTargetReadResult.Ready(targets, diagnostics);
    }

    private static void AddTarget(
        List<InterpPreviewTrackTargetDescriptor> targets,
        HashSet<(int trackId, string role, string key)> dedupe,
        InterpPreviewSourceObjectId actionId,
        InterpPreviewSourceObjectId interpDataId,
        InterpPreviewInterpTrackSnapshot track,
        string groupName,
        InterpPreviewTrackTargetRole role,
        string targetKey,
        int groupIndex,
        int trackIndex)
    {
        string canonicalKey = targetKey?.Trim() ?? string.Empty;
        var dedupeKey = (track.TrackId.ExportUIndex, role.ToString(), canonicalKey);
        if (!dedupe.Add(dedupeKey))
        {
            return;
        }

        targets.Add(new InterpPreviewTrackTargetDescriptor(
            actionId,
            interpDataId,
            track.TrackId,
            track.ClassName,
            groupName,
            role,
            canonicalKey,
            groupIndex,
            trackIndex));
    }

    private static bool HasActionVariableLink(IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> links, string groupName)
    {
        if (links is null || links.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < links.Count; i++)
        {
            if (string.Equals(links[i]?.LinkDesc?.Trim(), groupName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        if (string.Equals(trimmed, "None", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return trimmed;
    }

    private static bool IsDirectorStyleGroup(string groupName)
    {
        return !string.IsNullOrWhiteSpace(groupName)
               && groupName.StartsWith("InterpGroupDirector", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StringTupleComparer : IEqualityComparer<(int trackId, string role, string key)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();

        public bool Equals((int trackId, string role, string key) x, (int trackId, string role, string key) y)
        {
            return x.trackId == y.trackId
                   && string.Equals(x.role, y.role, StringComparison.Ordinal)
                   && string.Equals(x.key, y.key, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((int trackId, string role, string key) obj)
        {
            return HashCode.Combine(obj.trackId, obj.role, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.key ?? string.Empty));
        }
    }
}
