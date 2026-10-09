using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class ActorPreviewRenderPipelineTests
{
    [TestMethod]
    public void PreviewPasses_CompleteLightingBeforeBeginningTranslucency()
    {
        var calls = new List<string>();
        ActorPreviewRenderPipeline.Execute(false, pass => calls.Add(pass.ToString()),
            () => calls.Add("BeginTranslucent"), () => calls.Add("EndLighting"));
        CollectionAssert.AreEqual(new[] { "Base", "Hair", "Lighting", "EndLighting", "BeginTranslucent", "Translucent" }, calls);
    }

    [TestMethod]
    public void CollisionPreview_RendersCollisionAfterTranslucency()
    {
        var calls = new List<string>();
        ActorPreviewRenderPipeline.Execute(true, pass => calls.Add(pass.ToString()),
            () => calls.Add("BeginTranslucent"), () => calls.Add("EndLighting"));
        CollectionAssert.AreEqual(new[] { "Base", "Hair", "Lighting", "EndLighting", "BeginTranslucent", "Translucent", "Collision" }, calls);
    }
}
