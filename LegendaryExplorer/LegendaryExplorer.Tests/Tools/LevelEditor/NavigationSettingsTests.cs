using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System.Numerics;
using System.Windows.Input;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class NavigationSettingsTests
{
    [TestMethod]
    public void MovementModifiers_PreserveShiftPriorityAndTurboCombination()
    {
        Assert.AreEqual(1f, Multiplier(ModifierKeys.None));
        Assert.AreEqual(4f, Multiplier(ModifierKeys.Control));
        Assert.AreEqual(0.25f, Multiplier(ModifierKeys.Shift));
        Assert.AreEqual(0.25f, Multiplier(ModifierKeys.Control | ModifierKeys.Shift));
        Assert.AreEqual(10f, Multiplier(ModifierKeys.None, true));
        Assert.AreEqual(40f, Multiplier(ModifierKeys.Control, true));
        Assert.AreEqual(2.5f, Multiplier(ModifierKeys.Control | ModifierKeys.Shift, true));
        Assert.AreEqual(10f, Multiplier(ModifierKeys.None, true, float.NaN));
        Assert.AreEqual(10f, Multiplier(ModifierKeys.None, true, 0));
    }

    private static float Multiplier(ModifierKeys modifiers, bool turbo = false, float factor = 10)
        => MeshRenderContext.CalculateCameraMovementSpeedMultiplier(modifiers, turbo, factor);

    [TestMethod]
    public void LegacyViewState_RetainsOriginalLightingAndNavigationDefaults()
    {
        var state = JsonConvert.DeserializeObject<RecentViewState>("{\"CameraX\":12,\"IsOrthographicView\":true,\"CameraOrthoWidth\":0}");
        Assert.IsTrue(state.UseGameShaders);
        Assert.IsTrue(state.UseDynamicLighting);
        Assert.IsTrue(state.UseLightMaps);
        Assert.IsTrue(state.UseLocalCoordsForWidget);
        Assert.AreEqual(ViewportLightingMode.Level, state.LightingMode);
        Assert.AreEqual(10, state.TurboCameraMovementMultiplier);
        Assert.IsFalse(state.TurboCameraMovementEnabled);
        Assert.IsNull(state.PerspectiveCamera);
        var camera = new SceneCamera();
        state.RestoreCamera(camera);
        Assert.AreEqual(5000f, camera.OrthoWidth);
        camera.RestorePerspectiveState();
        Assert.AreEqual(new Vector3(12, 0, 0), camera.Position);
    }

    [TestMethod]
    public void LegacyViewState_MissingVisibilityFieldsKeepOriginalDefaults()
    {
        var state = JsonConvert.DeserializeObject<RecentViewState>("{\"CameraX\":12}");

        Assert.IsTrue(state.ShowLights);
        Assert.AreEqual(1000, state.LightRenderDistance);
        Assert.AreEqual(5000, state.VisibleSetDistance);
        Assert.IsFalse(state.UseVisibleSetOnly);
        Assert.IsFalse(state.HasUserEditedVisibleSets);
        Assert.HasCount(0, state.VisibleActorKeys);
        Assert.IsNull(state.HiddenActorClasses);
        Assert.AreEqual(ObjectRenderMode.Full, state.ObjectRenderMode);
        Assert.IsTrue(state.OutlineSelectedVolumetrics);
        Assert.IsFalse(state.TintVolumetricPreview);
        Assert.IsTrue(state.ShowStageNodes);
        Assert.IsTrue(state.ShowStageCameras);
    }

    [TestMethod]
    public void ViewState_ExplicitVisibilityOverridesSurviveRoundTrip()
    {
        var state = new RecentViewState
        {
            ShowLights = false, LightRenderDistance = 0, VisibleSetDistance = 0,
            UseVisibleSetOnly = true, HasUserEditedVisibleSets = true,
            VisibleActorKeys = ["Level.pcc:12"], HiddenActorClasses = ["Volume"],
            OutlineSelectedVolumetrics = false, TintVolumetricPreview = true,
            ShowStageNodes = false, ShowStageCameras = false
        };

        var loaded = JsonConvert.DeserializeObject<RecentViewState>(JsonConvert.SerializeObject(state));

        Assert.IsFalse(loaded.ShowLights);
        Assert.AreEqual(0, loaded.LightRenderDistance);
        Assert.AreEqual(0, loaded.VisibleSetDistance);
        Assert.IsTrue(loaded.UseVisibleSetOnly);
        Assert.IsTrue(loaded.HasUserEditedVisibleSets);
        CollectionAssert.AreEqual(state.VisibleActorKeys, loaded.VisibleActorKeys);
        CollectionAssert.AreEqual(state.HiddenActorClasses, loaded.HiddenActorClasses);
        Assert.IsFalse(loaded.OutlineSelectedVolumetrics);
        Assert.IsTrue(loaded.TintVolumetricPreview);
        Assert.IsFalse(loaded.ShowStageNodes);
        Assert.IsFalse(loaded.ShowStageCameras);
    }

    [TestMethod]
    public void TopDownState_RoundTripsPerspectiveCameraAndNewSettings()
    {
        var camera = new SceneCamera { Position = new Vector3(10, 20, 30), Pitch = 0.4f, Yaw = 1.2f, FocusDepth = 600 };
        var perspective = camera.CaptureView();
        camera.SavePerspectiveState();
        camera.IsOrthographic = true;
        camera.Position = new Vector3(90, 80, 40000);
        camera.OrthoWidth = 2400;
        var state = new RecentViewState { UseGameShaders = false, UseDynamicLighting = false, UseLightMaps = false,
            UseLocalCoordsForWidget = false, LightingMode = ViewportLightingMode.Unlit,
            TurboCameraMovementEnabled = true, TurboCameraMovementMultiplier = 15 };
        state.CaptureCamera(camera);
        var loaded = JsonConvert.DeserializeObject<RecentViewState>(JsonConvert.SerializeObject(state));
        var restored = new SceneCamera();
        loaded.RestoreCamera(restored);
        Assert.IsTrue(restored.IsOrthographic);
        Assert.AreEqual(camera.CaptureView(), restored.CaptureView());
        Assert.AreEqual(2400f, restored.OrthoWidth);
        Assert.AreEqual(perspective, restored.SavedPerspectiveView.Value);
        restored.IsOrthographic = false;
        restored.RestorePerspectiveState();
        Assert.AreEqual(perspective, restored.CaptureView());
        Assert.IsFalse(loaded.UseGameShaders);
        Assert.IsFalse(loaded.UseDynamicLighting);
        Assert.IsFalse(loaded.UseLightMaps);
        Assert.IsFalse(loaded.UseLocalCoordsForWidget);
        Assert.AreEqual(ViewportLightingMode.Unlit, loaded.LightingMode);
        Assert.AreEqual(15, loaded.TurboCameraMovementMultiplier);
    }

    [TestMethod]
    public void PerspectiveState_RoundTripsCameraFocusWithoutChangingMode()
    {
        var camera = new SceneCamera { Position = new Vector3(11, 22, 33), Pitch = 0.3f, Yaw = 0.6f, FocusDepth = 450 };
        var state = new RecentViewState();
        state.CaptureCamera(camera);
        var restored = new SceneCamera();
        state.RestoreCamera(restored);
        Assert.IsFalse(restored.IsOrthographic);
        Assert.AreEqual(camera.CaptureView(), restored.CaptureView());
    }
}
