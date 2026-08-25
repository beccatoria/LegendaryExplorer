using System;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewRenderCoordinator : IInterpPreviewRenderCoordinator
{
    private IInterpPreviewSession _session;
    private LevelEditorRenderContext _renderContext;

    public void Attach(LevelEditorRenderContext renderContext, IInterpPreviewSession session)
    {
        Detach(renderContext);
        _renderContext = renderContext;
        _session = session;
        renderContext.UpdateScene += OnUpdateScene;
        renderContext.RenderScene += OnRenderScene;
    }

    public void Detach(LevelEditorRenderContext renderContext)
    {
        renderContext.UpdateScene -= OnUpdateScene;
        renderContext.RenderScene -= OnRenderScene;
        _session = null;
        if (ReferenceEquals(_renderContext, renderContext))
        {
            _renderContext = null;
        }
    }

    private void OnUpdateScene(object sender, float deltaTime)
    {
        if (_renderContext is null || _session is null)
        {
            return;
        }

        foreach (ActorProxy actor in _session.Actors)
        {
            actor.UpdateScene(_renderContext, deltaTime);
        }
    }

    private void OnRenderScene(object sender, EventArgs e)
    {
        if (_renderContext is null || _session is null)
        {
            return;
        }

        bool baseWireframe = _renderContext.Wireframe;
        Span<RenderPass> passes = [RenderPass.Base, RenderPass.Hair];
        foreach (RenderPass pass in passes)
        {
            foreach (ActorProxy actor in _session.Actors)
            {
                actor.Render(_renderContext, pass);
                _renderContext.Wireframe = baseWireframe;
            }
        }

        _renderContext.Wireframe = baseWireframe;
        _renderContext.DrawUI();
    }
}
