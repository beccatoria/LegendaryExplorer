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
    private Dictionary<IMEPackage, List<SceneLight>> _lights = new();

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

    // Opt-in for Interp Preview transactions; ordinary Level Editor synchronization is unchanged.
    public void SynchronizeTransactional(IEnumerable<IMEPackage> packages, Action commitScene)
    {
        var active = packages.ToHashSet();
        // Load every incoming light set before removing old lights. Rollback reuses the
        // original SceneLight objects, so it never relies on re-reading a failing package.
        var added = active.Where(package => !_lights.ContainsKey(package))
            .ToDictionary(package => package, _loadLights);
        var removed = _lights.Where(pair => !active.Contains(pair.Key)).ToArray();
        var next = _lights.Where(pair => active.Contains(pair.Key)).Concat(added)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        try
        {
            foreach (var pair in added) _context.AddLights(pair.Value);
            foreach (var pair in removed) _context.RemoveLights(pair.Value);
            // Cache eviction is safe before publication; these caches rebuild on demand.
            foreach (var pair in removed) _context.ForgetLevel(pair.Key);
            commitScene();
        }
        catch
        {
            foreach (var pair in added) _context.RemoveLights(pair.Value);
            foreach (var pair in removed)
            {
                _context.RemoveLights(pair.Value);
                _context.AddLights(pair.Value);
            }
            throw;
        }
        _lights = next;
    }
}
