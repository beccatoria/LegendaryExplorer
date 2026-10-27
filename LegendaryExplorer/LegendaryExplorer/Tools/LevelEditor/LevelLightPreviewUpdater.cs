using System.Collections.Generic;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore.Packages;

namespace LegendaryExplorer.Tools.LevelEditor;

/// <summary>Indexes the file's light components once; steady frames only visit actual lights.</summary>
public sealed class LevelLightPreviewUpdater
{
    private readonly Dictionary<ExportEntry, int> _indices = new();
    private readonly List<(LightComponentProxy Component, int Index)> _bindings = [];
    private readonly Dictionary<SceneLight, SceneLight> _replacements = new();
    private List<SceneLight> _lights;
    private int _lightCount;
    private bool _dirty = true;

    public void Invalidate() => _dirty = true;

    public void Update(IReadOnlyList<ActorProxy> actors, List<SceneLight> lights, MeshRenderContext context)
    {
        if (_dirty || !ReferenceEquals(_lights, lights) || _lightCount != lights.Count)
        {
            _indices.Clear();
            _bindings.Clear();
            for (int i = 0; i < lights.Count; i++) _indices.TryAdd(lights[i].Export, i);
            foreach (var actor in actors)
            {
                if (!actor.IsLight) continue;
                foreach (var component in actor.Components)
                    if (component is LightComponentProxy light && _indices.TryGetValue(light.Export, out int index))
                        _bindings.Add((light, index));
            }
            _lights = lights;
            _lightCount = lights.Count;
            _dirty = false;
        }
        _replacements.Clear();
        foreach (var (component, index) in _bindings)
        {
            var baseline = lights[index];
            var preview = component.GetPreviewLight(context.PackageCache, baseline);
            if (preview is null || ReferenceEquals(baseline, preview)) continue;
            _replacements[baseline] = preview;
            lights[index] = preview;
        }
        context.ReplaceLights(_replacements);
        _replacements.Clear();
    }

    public void Clear()
    {
        _indices.Clear();
        _bindings.Clear();
        _replacements.Clear();
        _lights = null;
        _dirty = true;
    }
}
