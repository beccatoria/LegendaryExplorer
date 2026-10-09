using LegendaryExplorer.Tools.LevelEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class VolumetricMeshClassifierTests
{
    [TestMethod]
    public void OrdinarySkeletalMesh_WithVolumeLightMaterial_IsVolumetric()
    {
        Assert.IsTrue(VolumetricMeshClassifier.IsVolumetric("Props.ConeSkeletalMesh", ["Effects.VolumeLightCone"]));
    }

    [TestMethod]
    public void OrdinaryStaticMesh_WithVolumetricMaterial_IsVolumetric()
    {
        Assert.IsTrue(VolumetricMeshClassifier.IsVolumetric("Props.Sphere", ["Effects.volumetricFog"]));
    }

    [TestMethod]
    public void ExistingVolumetricMeshName_RemainsRecognized()
    {
        Assert.IsTrue(VolumetricMeshClassifier.IsVolumetric("Effects.VolumetricSphere", []));
    }

    [TestMethod]
    public void OrdinaryTranslucentMaterial_IsNotAutomaticallyVolumetric()
    {
        Assert.IsFalse(VolumetricMeshClassifier.IsVolumetric("Props.Window", ["Materials.TranslucentGlass"]));
        Assert.IsFalse(VolumetricMeshClassifier.IsVolumetric(null, null));
    }

    [TestMethod]
    public void OldViewState_DefaultsToSelectedOutline_AndDisabledOptionRoundTrips()
    {
        Assert.IsTrue(JsonConvert.DeserializeObject<RecentViewState>("{}").OutlineSelectedVolumetrics);
        var state = new RecentViewState { OutlineSelectedVolumetrics = false };
        Assert.IsFalse(JsonConvert.DeserializeObject<RecentViewState>(JsonConvert.SerializeObject(state)).OutlineSelectedVolumetrics);
    }

    [TestMethod]
    public void TintPreview_DefaultsToNormal_AndRemainsIndependentOfVisibility()
    {
        Assert.IsFalse(JsonConvert.DeserializeObject<RecentViewState>("{}").TintVolumetricPreview);
        var state = new RecentViewState { TintVolumetricPreview = true, ShowVolumetrics = false };
        var restored = JsonConvert.DeserializeObject<RecentViewState>(JsonConvert.SerializeObject(state));
        Assert.IsTrue(restored.TintVolumetricPreview);
        Assert.IsFalse(restored.ShowVolumetrics);
        Assert.IsTrue(restored.OutlineSelectedVolumetrics);
    }
}
