using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.ObjectInfo;

namespace LegendaryExplorer.Tools.InterpEditor;

// Transient dispatcher-owned reader. Borrowed editor packages preserve unsaved state;
// independently opened packages are disposed only after every descriptor has been copied.
internal sealed class InterpPreviewSourceScope : IDisposable
{
    private readonly Dictionary<string, IMEPackage> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IMEPackage> _owned = [];
    private readonly HashSet<string> _conflictingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, IMEPackage> _open;
    private readonly InterpPreviewDialogueResolutionRequest _request;
    private readonly List<InterpPreviewDiagnostic> _diagnostics;
    private int _referenceReads;
    public List<InterpPreviewSourceReference> UnresolvedReferences { get; } = [];
    public bool CleanupFailed { get; private set; }

    public InterpPreviewSourceScope(InterpPreviewDialogueResolutionRequest request,
        List<InterpPreviewDiagnostic> diagnostics, Func<string, IMEPackage> open)
    {
        _request = request;
        _diagnostics = diagnostics;
        _open = open ?? InterpPreviewPackageOwnership.OpenIndependent;
        foreach (var package in new[] { request.Conversation.Export.FileRef, request.Conversation.Sequence?.FileRef, request.Node.InterpData?.FileRef })
            if (package is not null)
            {
                string path = Path.GetFullPath(package.FilePath);
                if (_packages.TryGetValue(path, out var existing) && !ReferenceEquals(existing, package))
                {
                    _conflictingPaths.Add(path);
                    diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_PACKAGE_VERSION_CONFLICT",
                        $"Multiple editor package instances represent {path}; their edits cannot be safely combined."));
                }
                else _packages.TryAdd(path, package);
            }
    }

    public void Check()
    {
        _request.CancellationToken.ThrowIfCancellationRequested();
        if (!_request.CanContinue()) throw new OperationCanceledException("Source request was superseded or closing.");
    }

    private bool WithinBudget(ref bool complete)
    {
        Check();
        if (++_referenceReads <= 50_000) return true;
        complete = false;
        if (_referenceReads == 50_001)
            _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_REFERENCE_LIMIT", "Source copying reached its reference limit; the copied graph/data are incomplete."));
        return false;
    }

    public ExportEntry Resolve(InterpPreviewSourceObjectId id, string expectedClass)
    {
        Check();
        try
        {
            if (id.Game != _request.Conversation.Export.Game || id.ExportUIndex <= 0 || string.IsNullOrWhiteSpace(id.PackagePath))
                throw new InvalidOperationException("Source identity has the wrong game or an invalid index/path.");
            if (_conflictingPaths.Contains(id.PackagePath)) throw new InvalidOperationException("Conflicting editor package versions require a fresh source selection.");
            if (!_packages.TryGetValue(id.PackagePath, out var package))
            {
                package = _open(id.PackagePath) ?? throw new IOException("Source package was unavailable.");
                _owned.Add(package);
                _packages.Add(id.PackagePath, package);
            }
            if (package.Game != id.Game || !package.TryGetUExport(id.ExportUIndex, out var export)
                || export.ClassName != expectedClass)
                throw new InvalidOperationException($"Expected {expectedClass} at {id}.");
            return export;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_OBJECT_UNRESOLVED", $"{id}: {ex.Message}"));
            return null;
        }
    }

    public static InterpPreviewSourceObjectId Id(ExportEntry export) => new(export.Game, export.FileRef.FilePath, export.UIndex);

    private ExportEntry LocalReference(ExportEntry owner, int index, string property, ref bool complete)
    {
        Check();
        if (index > 0 && owner.FileRef.TryGetUExport(index, out var export)) return export;
        complete = false;
        UnresolvedReferences.Add(new(Id(owner), property, index));
        _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_REFERENCE_UNRESOLVED",
            $"{Id(owner)} {property} retains unresolved reference {index}."));
        return null;
    }

    public InterpPreviewSequenceGraphSnapshot ReadGraph(InterpPreviewSourceObjectId sequenceId)
    {
        var sequence = Resolve(sequenceId, "Sequence");
        if (sequence is null) return null;
        bool complete = true;
        var nodes = new Dictionary<InterpPreviewSourceObjectId, InterpPreviewSequenceNodeSnapshot>();
        var objects = sequence.GetProperty<ArrayProperty<ObjectProperty>>("SequenceObjects");
        if (objects is null)
        {
            _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_SEQUENCE_OBJECTS_MISSING", "The selected Sequence has no readable SequenceObjects array."));
            return new(sequenceId, [], isComplete: false);
        }
        foreach (var reference in objects)
        {
            if (!WithinBudget(ref complete)) break;
            if (nodes.Count >= 10_000)
            {
                complete = false;
                _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_OBJECT_LIMIT", "Source copying reached its object limit."));
                break;
            }
            var node = LocalReference(sequence, reference?.Value ?? 0, "SequenceObjects", ref complete);
            if (node is null) continue;
            try
            {
                var outputs = new List<InterpPreviewOutputLinkSnapshot>();
                var rawOutputs = node.GetProperty<ArrayProperty<StructProperty>>("OutputLinks");
                if (rawOutputs is not null)
                    for (int i = 0; i < rawOutputs.Count; i++)
                    {
                        if (!WithinBudget(ref complete)) break;
                        var output = rawOutputs[i];
                        var links = output?.GetProp<ArrayProperty<StructProperty>>("Links");
                        if (links is null) { complete = false; continue; }
                        foreach (var link in links)
                        {
                            if (!WithinBudget(ref complete)) break;
                            int target = link?.GetProp<ObjectProperty>("LinkedOp")?.Value ?? 0;
                            int input = link?.GetProp<IntProperty>("InputLinkIdx")?.Value ?? -1;
                            if (target <= 0 || input < 0)
                            {
                                complete = false;
                                UnresolvedReferences.Add(new(Id(node), $"OutputLinks[{i}]", target));
                            }
                            outputs.Add(new(i, output.GetProp<StrProperty>("LinkDesc")?.Value, target, input));
                        }
                    }
                var variables = ReadVariableLinks(node, ref complete);
                var snapshot = new InterpPreviewSequenceNodeSnapshot(Id(node), node.ClassName,
                    node.GetProperty<IntProperty>("m_nNodeID")?.Value, outputs, variables);
                if (!nodes.TryAdd(snapshot.NodeId, snapshot))
                {
                    complete = false;
                    _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_SEQUENCE_OBJECT_DUPLICATE", $"SequenceObjects repeats {snapshot.NodeId}."));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                complete = false;
                _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_GRAPH_NODE_UNREADABLE", $"{Id(node)}: {ex.Message}; other nodes were retained."));
            }
        }
        // Data variables may be referenced by an action without appearing in SequenceObjects.
        // Add only the exact referenced InterpData, never arbitrary package-wide actions.
        foreach (var action in nodes.Values.Where(node => node.ClassName == "SeqAct_Interp").ToArray())
            foreach (int index in action.VariableLinks.Where(link => link.LinkDesc.Equals("Data", StringComparison.OrdinalIgnoreCase))
                         .SelectMany(link => link.LinkedNodeExportUIndexes))
                if (index > 0 && sequence.FileRef.TryGetUExport(index, out var data) && data.ClassName == "InterpData")
                    nodes.TryAdd(Id(data), new(Id(data), data.ClassName, null, [], []));
        if (!complete) _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_GRAPH_PARTIAL", "Some graph references or connector fields could not be read; valid siblings were retained."));
        return new(sequenceId, nodes.Values.ToArray(), complete);
    }

    public IReadOnlyList<InterpPreviewVariableLinkSnapshot> ReadVariableLinks(ExportEntry action, ref bool complete)
    {
        var result = new List<InterpPreviewVariableLinkSnapshot>();
        var links = action.GetProperty<ArrayProperty<StructProperty>>("VariableLinks");
        if (links is null) return result;
        foreach (var link in links)
        {
            if (!WithinBudget(ref complete)) break;
            var variables = link?.GetProp<ArrayProperty<ObjectProperty>>("LinkedVariables");
            if (variables is null) complete = false;
            var copied = new List<int>();
            foreach (var variable in variables?.AsEnumerable() ?? Enumerable.Empty<ObjectProperty>())
            {
                if (!WithinBudget(ref complete)) break;
                copied.Add(variable?.Value ?? 0);
                if (variable is null || variable.Value <= 0 || !action.FileRef.IsUExport(variable.Value))
                {
                    complete = false;
                    UnresolvedReferences.Add(new(Id(action), "VariableLinks/" + link?.GetProp<StrProperty>("LinkDesc")?.Value, variable?.Value ?? 0));
                }
            }
            result.Add(new(link?.GetProp<StrProperty>("LinkDesc")?.Value,
                copied));
        }
        return result;
    }

    public InterpPreviewInterpDataSnapshot ReadData(ExportEntry data, ref bool complete)
    {
        Check();
        var groups = new List<InterpPreviewInterpGroupSnapshot>();
        var rawGroups = data.GetProperty<ArrayProperty<ObjectProperty>>("InterpGroups");
        foreach (var groupReference in rawGroups?.AsEnumerable() ?? Enumerable.Empty<ObjectProperty>())
        {
            if (!WithinBudget(ref complete)) break;
            var group = LocalReference(data, groupReference?.Value ?? 0, "InterpGroups", ref complete);
            if (group is null) continue;
            if (!group.IsA("InterpGroup"))
            {
                complete = false;
                _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_GROUP_TYPE_INVALID", $"{Id(group)} is not an InterpGroup."));
                continue;
            }
            var tracks = new List<InterpPreviewInterpTrackSnapshot>();
            foreach (var trackReference in group.GetProperty<ArrayProperty<ObjectProperty>>("InterpTracks")?.AsEnumerable() ?? Enumerable.Empty<ObjectProperty>())
            {
                if (!WithinBudget(ref complete)) break;
                var track = LocalReference(group, trackReference?.Value ?? 0, "InterpTracks", ref complete);
                if (track is null) continue;
                tracks.Add(new(Id(track), track.ClassName, track.GetProperty<NameProperty>("m_nmFindActor")?.Value.Instanced));
            }
            groups.Add(new(Id(group), group.GetProperty<NameProperty>("GroupName")?.Value.Instanced ?? group.ObjectName.Instanced, tracks));
        }
        return new(Id(data), groups);
    }

    public void Dispose()
    {
        var errors = new List<Exception>();
        foreach (var package in _owned)
        {
            try { package.Dispose(); }
            catch (Exception ex) { errors.Add(ex); }
        }
        _owned.Clear();
        if (errors.Count > 0)
        {
            CleanupFailed = true;
            _diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_CLEANUP_FAILED",
                new AggregateException("Independent source package cleanup failed.", errors).ToString()));
        }
    }
}
