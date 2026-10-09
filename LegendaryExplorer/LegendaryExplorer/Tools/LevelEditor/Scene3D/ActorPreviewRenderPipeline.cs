using System;
using System.Collections.Generic;

namespace LegendaryExplorer.Tools.LevelEditor.Scene3D;

public static class ActorPreviewRenderPipeline
{
    public static void Execute(bool showCollision, Action<RenderPass> renderPass, Action beginTranslucent, Action endLighting)
    {
        Span<RenderPass> passes = showCollision
            ? [RenderPass.Base, RenderPass.Hair, RenderPass.Lighting, RenderPass.Translucent, RenderPass.Collision]
            : [RenderPass.Base, RenderPass.Hair, RenderPass.Lighting, RenderPass.Translucent];
        foreach (RenderPass pass in passes)
        {
            if (pass == RenderPass.Translucent) beginTranslucent();
            renderPass(pass);
            if (pass == RenderPass.Lighting) endLighting();
        }
    }

    public static void Render(LevelEditorRenderContext context, IEnumerable<ActorProxy> actors, bool showCollision = false)
    {
        bool wireframe = context.Wireframe;
        try
        {
            Execute(showCollision, pass =>
            {
                foreach (ActorProxy actor in actors)
                {
                    if (!context.IsActorVisible(actor)) continue;
                    actor.Render(context, pass);
                    context.Wireframe = wireframe;
                }
            }, context.BeginTranslucentPass, context.EndLightingPass);
        }
        finally
        {
            context.Wireframe = wireframe;
        }
        context.DrawUI();
    }
}
