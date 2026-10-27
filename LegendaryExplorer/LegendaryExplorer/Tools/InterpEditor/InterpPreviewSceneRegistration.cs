using System;
using System.Linq;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;

namespace LegendaryExplorer.Tools.InterpEditor;

// Preview-only registration boundary. It changes no shared Level Editor loading semantics.
public sealed class InterpPreviewSceneRegistration
{
    private readonly LevelEditorRenderContext _context;
    private readonly PreviewLevelLightRegistry _lights;
    private readonly Action<ActorProxy> _register;
    private readonly Action<ActorProxy> _unregister;

    public InterpPreviewSceneRegistration(LevelEditorRenderContext context, PreviewLevelLightRegistry lights,
        Action<ActorProxy> register = null, Action<ActorProxy> unregister = null)
    {
        _context = context;
        _lights = lights;
        _register = register ?? context.AddActor;
        _unregister = unregister ?? context.RemoveActor;
    }

    public void Apply(InterpPreviewOperationResult change, Action commitScene)
    {
        ActorProxy[] previous = _context.DrawList_3D.ToArray();
        try
        {
            foreach (ActorProxy actor in change.AddedActors) _register(actor);
            foreach (ActorProxy actor in change.RemovedActors) _unregister(actor);
            _lights.SynchronizeTransactional(_context.DrawList_3D.Select(actor => actor.Export.FileRef), commitScene);
        }
        catch
        {
            // Rebuild both draw membership and hit registration, even when a callback threw
            // after changing part of the real renderer. Never dispose actors in this layer.
            _context.UnloadActors(_context.DrawList_3D.ToList());
            foreach (ActorProxy actor in previous) _context.AddActor(actor);
            throw;
        }
    }
}
