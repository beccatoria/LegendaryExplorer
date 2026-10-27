using System;
using System.Collections.Generic;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;

namespace LegendaryExplorer.Tools.LevelEditor;

/// <summary>Conservative package-local reference closure of the sources read for level lights.</summary>
public sealed class LevelLightUpdateDependencies
{
    private readonly HashSet<int> _exports = [];
    private bool _complete;
    private IMEPackage _package;
    public int ModelIndex { get; private set; }
    public bool IsComplete => _complete;
    public int ExportCount => _exports.Count;

    public void Rebuild(Level level, IEnumerable<ExportEntry> lightSources)
    {
        _complete = false;
        ModelIndex = level.Model;
        _exports.Clear();
        var pending = new Stack<ExportEntry>(lightSources);
        var package = level.Export.FileRef;
        _package = package;
        try
        {
            while (pending.TryPop(out var export))
            {
                if (!ReferenceEquals(export.FileRef, package) || !_exports.Add(export.UIndex)) continue;
                // Bound indexing cost. A larger or unreadable graph uses the original broad refresh.
                if (_exports.Count > 4096) return;
                Add(export.idxArchetype);
                Add(export.idxClass);
                Add(export.idxSuperClass);
                ReadProperties(export.GetProperties());
                if (ObjectBinary.From(export) is { } binary)
                    foreach (int index in binary.GetUIndexes(package.Game)) Add(index);
            }
            _complete = true;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Light dependency index unavailable: {exception.Message}");
        }

        void Add(int index)
        {
            if (!_exports.Contains(index) && package.TryGetUExport(index, out var dependency)) pending.Push(dependency);
        }

        void ReadProperties(PropertyCollection properties)
        {
            foreach (var property in properties)
                switch (property)
                {
                    case ObjectProperty reference: Add(reference.Value); break;
                    case DelegateProperty reference: Add(reference.Value.ContainingObjectUIndex); break;
                    case StructProperty structure: ReadProperties(structure.Properties); break;
                    case ArrayProperty<ObjectProperty> references:
                        foreach (var reference in references) Add(reference.Value);
                        break;
                    case ArrayProperty<StructProperty> structures:
                        foreach (var structure in structures) ReadProperties(structure.Properties);
                        break;
                }
        }
    }

    public bool NeedsLightRefresh(IEnumerable<PackageUpdate> updates)
    {
        foreach (var update in updates)
        {
            if ((update.Change & (PackageChange.Export | PackageChange.Import | PackageChange.Name)) == 0) continue;
            // A header/table edit can change resolution or introduce a new source.
            if (!_complete || (update.Change & (PackageChange.Import | PackageChange.Name | PackageChange.Header | PackageChange.Add | PackageChange.Remove)) != 0
                || _exports.Contains(update.Index)) return true;
            // An existing actor can acquire its first LightComponent without a table/header edit.
            try
            {
                if (_package.TryGetUExport(update.Index, out var export)
                    && (export.ClassName == "StaticLightCollectionActor"
                        || export.GetProperty<ObjectProperty>("LightComponent") is { Value: not 0 })) return true;
            }
            catch { return true; }
        }
        return false;
    }

    public void Invalidate() => _complete = false;
}
