using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal.BinaryConverters;

namespace LegendaryExplorer.Tools.LevelEditor.Scene3D;

public sealed class PreviewLevelLightRegistry
{
    private readonly MeshRenderContext _context;
    private readonly Func<IMEPackage, List<SceneLight>> _loadLights;
    private readonly Dictionary<IMEPackage, List<SceneLight>> _lights = new();

    public PreviewLevelLightRegistry(MeshRenderContext context, Func<IMEPackage, List<SceneLight>> loadLights = null)
    {
        _context = context;
        _loadLights = loadLights ?? LoadLights;
    }

    private List<SceneLight> LoadLights(IMEPackage package)
    {
        var export = package.Exports.FirstOrDefault(entry => entry.ClassName == "Level");
        return export is null ? [] : SceneLight.LoadLevelLights(export.GetBinaryData<Level>(), _context.PackageCache);
    }

    public void Synchronize(IEnumerable<IMEPackage> packages)
    {
        var active = packages.ToHashSet();
        foreach (var package in _lights.Keys.Where(package => !active.Contains(package)).ToArray())
        {
            _context.RemoveLights(_lights[package]);
            _context.ForgetLevel(package);
            _lights.Remove(package);
        }
        foreach (var package in active.Where(package => !_lights.ContainsKey(package)))
        {
            var lights = _loadLights(package);
            _lights.Add(package, lights);
            _context.AddLights(lights);
        }
    }

    public void Clear() => Synchronize(Array.Empty<IMEPackage>());
}
