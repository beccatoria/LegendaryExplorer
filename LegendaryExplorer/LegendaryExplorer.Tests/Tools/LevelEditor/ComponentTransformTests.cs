using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class ComponentTransformTests
{
    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);
    }

    [TestMethod]
    public void ActorHiddenFlag_IsReadWithoutChangingProperties()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("ComponentTests.pcc", MEGame.LE3);
        var export = CreateExport(package, null, "Actor", "Actor", [new BoolProperty(true, "bHidden")]);
        using var actor = new TestActor(export);

        Assert.IsTrue(actor.IsHidden);
        Assert.IsTrue(export.GetProperty<BoolProperty>("bHidden").Value);
    }

    [TestMethod]
    public void ComponentHiddenFlag_IsSeparateFromActorHiddenFlag()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("ComponentTests.pcc", MEGame.LE3);
        var export = CreateExport(package, null, "Actor", "Actor", []);
        using var actor = new TestActor(export);
        var componentExport = CreateExport(package, export, "Body", "SkeletalMeshComponent", [new BoolProperty(true, "HiddenGame")]);
        using var component = new SkeletalMeshComponentProxy(null, componentExport, actor);

        Assert.IsFalse(actor.IsHidden);
        Assert.IsTrue(component.HiddenGame);
    }

    [TestMethod]
    public void AnimationParent_PreservesChildOffsetWhenActorMoves()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("ComponentTests.pcc", MEGame.LE3);
        var export = CreateExport(package, null, "Actor", "Actor", []);
        using var actor = new TestActor(export);
        var bodyExport = CreateExport(package, export, "Body", "SkeletalMeshComponent",
            [CommonStructs.Vector3Prop(new Vector3(10, 0, 0), "Translation")]);
        var body = new SkeletalMeshComponentProxy(null, bodyExport, actor);
        actor.Components.Add(body);
        var hairExport = CreateExport(package, export, "Hair", "SkeletalMeshComponent",
            [new ObjectProperty(bodyExport.UIndex, "ParentAnimComponent"), CommonStructs.Vector3Prop(new Vector3(0, 5, 0), "Translation")]);
        var hair = new SkeletalMeshComponentProxy(null, hairExport, actor);
        actor.Components.Add(hair);

        Assert.AreEqual(new Vector3(10, 5, 0), hair.LocalToWorld.Translation);
        actor.Location = new Vector3(100, 200, 300);
        Assert.AreEqual(new Vector3(110, 205, 300), hair.LocalToWorld.Translation);
        actor.Location = Vector3.Zero;
        Assert.AreEqual(new Vector3(10, 5, 0), hair.LocalToWorld.Translation);
    }

    [TestMethod]
    public void DisabledAnimationParent_UsesActorTransform()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("ComponentTests.pcc", MEGame.LE3);
        var export = CreateExport(package, null, "Actor", "Actor", []);
        using var actor = new TestActor(export);
        var bodyExport = CreateExport(package, export, "Body", "SkeletalMeshComponent",
            [CommonStructs.Vector3Prop(new Vector3(10, 0, 0), "Translation")]);
        var body = new SkeletalMeshComponentProxy(null, bodyExport, actor);
        actor.Components.Add(body);
        var hairExport = CreateExport(package, export, "Hair", "SkeletalMeshComponent",
            [new ObjectProperty(bodyExport.UIndex, "ParentAnimComponent"), new BoolProperty(false, "bTransformFromAnimParent"),
             CommonStructs.Vector3Prop(new Vector3(0, 5, 0), "Translation")]);
        var hair = new SkeletalMeshComponentProxy(null, hairExport, actor);
        actor.Components.Add(hair);

        actor.Location = new Vector3(100, 200, 300);
        Assert.AreEqual(new Vector3(100, 205, 300), hair.LocalToWorld.Translation);
    }

    [TestMethod]
    public void ActorDisplayEligibility_UsesMembershipDistanceAndObjectMode()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("VisibilityPolicy.pcc", MEGame.LE3);
        var export = CreateExport(package, null, "Actor", "Actor", []);
        using var actor = new TestActor(export);
        var context = new LevelEditorRenderContext { UseVisibleSetOnly = true, VisibleSetDistance = 10 };
        context.Camera.Position = Vector3.Zero;
        Assert.IsFalse(context.IsActorVisible(actor));

        context.Visibility.VisibleActorKeys.Add(EditorVisibilityState.GetActorKey(package.FilePath, export.UIndex));
        Assert.IsTrue(context.IsActorVisible(actor));
        actor.Location = new Vector3(11, 0, 0);
        Assert.IsFalse(context.IsActorVisible(actor));

        context.UseVisibleSetOnly = false;
        Assert.IsTrue(context.IsActorVisible(actor));
        context.HideOrdinaryMeshes = true;
        Assert.IsFalse(context.IsActorVisible(actor));
    }

    [TestMethod]
    public void LightMarkerEligibility_UsesGlobalToggleAndDistance()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("VisibilityPolicy.pcc", MEGame.LE3);
        var export = CreateExport(package, null, "Light", "Actor", []);
        using var actor = new TestLightActor(export);
        var context = new LevelEditorRenderContext { ShowLights = true, LightRenderDistance = 10, HideOrdinaryMeshes = true };
        context.Camera.Position = Vector3.Zero;
        Assert.IsTrue(context.IsActorVisible(actor));
        context.ShowLights = false;
        Assert.IsFalse(context.IsActorVisible(actor));
        context.ShowLights = true;
        actor.Location = new Vector3(11, 0, 0);
        Assert.IsFalse(context.IsActorVisible(actor));
    }

    [TestMethod]
    public void LiveLightPreview_UsesEditsWithoutWritingExportsAndReusesUnchangedSnapshot()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("LightPreview.pcc", MEGame.LE3);
        var export = CreateExport(package, null, "Actor", "PointLight", []);
        using var actor = new TestActor(export);
        var lightExport = CreateExport(package, export, "Light", "PointLightComponent",
            [new FloatProperty(2, "Brightness"), new FloatProperty(100, "Radius")]);
        var context = new LevelEditorRenderContext();
        using var component = new LightComponentProxy(context, lightExport, actor);
        actor.Components.Add(component);
        var initial = SceneLight.Create(lightExport, "PointLight", context.PackageCache, actor.LocalToWorld, actor.Export);
        Assert.IsNotNull(initial);
        Assert.IsTrue(initial.LightingChannels.OverlapsWith(LightingChannels.StaticPrimitiveDefault));
        Assert.IsTrue(initial.LightingChannels.OverlapsWith(LightingChannels.DynamicPrimitiveDefault));
        Assert.AreSame(initial, component.GetPreviewLight(context.PackageCache, initial));
        context.AddLights([initial]);
        context.GetLights(out int beforeVersion);

        component.Brightness = 4;
        component.Radius = 200;
        actor.Location = new Vector3(10, 20, 30);
        var edited = component.GetPreviewLight(context.PackageCache, initial);
        Assert.AreEqual(initial.Color * 2, edited.Color);
        Assert.AreEqual(200f, edited.Radius);
        Assert.AreEqual(component.LocalToWorld.Translation, edited.Position);
        Assert.AreEqual(2f, lightExport.GetProperty<FloatProperty>("Brightness").Value);
        Assert.AreEqual(100f, lightExport.GetProperty<FloatProperty>("Radius").Value);
        context.ReplaceLight(initial, edited);
        Assert.AreSame(edited, context.GetLights(out int afterVersion)[0]);
        Assert.IsTrue(afterVersion > beforeVersion);
        context.RemoveLights([edited]);
        Assert.HasCount(0, context.GetLights(out _));
        actor.Components.Clear();
    }

    [TestMethod]
    public void LiveLightPreview_PreservesBaselineChannelsColorAndPositionUntilActuallyEdited()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("LightBaseline.pcc", MEGame.LE3);
        var export = CreateExport(package, null, "Actor", "PointLight", []);
        using var actor = new TestActor(export);
        var channels = new StructProperty("LightingChannelContainer", false,
            new BoolProperty(true, "bInitialized"), new BoolProperty(false, "Static"),
            new BoolProperty(true, "Dynamic")) { Name = "LightingChannels" };
        var lightExport = CreateExport(package, export, "Light", "PointLightComponent",
            [new FloatProperty(2, "Brightness"), channels, CommonStructs.Vector3Prop(new Vector3(7, 8, 9), "Translation")]);
        var context = new LevelEditorRenderContext();
        using var component = new LightComponentProxy(context, lightExport, actor);
        var original = SceneLight.Create(lightExport, "PointLight", context.PackageCache, actor.LocalToWorld, actor.Export);
        Assert.AreSame(original, component.GetPreviewLight(context.PackageCache, original));

        component.Brightness = 4;
        var edited = component.GetPreviewLight(context.PackageCache, original);
        Assert.AreEqual(original.Color * 2, edited.Color);
        Assert.AreEqual(original.LightingChannels, edited.LightingChannels);
        Assert.AreEqual(original.Position, edited.Position);
        Assert.AreEqual(original.Direction, edited.Direction);
        Assert.AreSame(edited, component.GetPreviewLight(context.PackageCache, edited));

        component.Brightness = 2;
        Assert.AreSame(original, component.GetPreviewLight(context.PackageCache, edited));
        Assert.IsNull(lightExport.GetProperty<StructProperty>("LightColor"));
        Assert.AreEqual(2f, lightExport.GetProperty<FloatProperty>("Brightness").Value);
    }

    [TestMethod]
    public void LiveLightPreview_ColorAndChannelEditsRevertToOriginalWithoutWrites()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("LightEdits.pcc", MEGame.LE3);
        var export = CreateExport(package, null, "Actor", "PointLight", []);
        using var actor = new TestActor(export);
        var lightExport = CreateExport(package, export, "Light", "PointLightComponent", []);
        var context = new LevelEditorRenderContext();
        using var component = new LightComponentProxy(context, lightExport, actor);
        var original = SceneLight.Create(lightExport, "PointLight", context.PackageCache, actor.LocalToWorld, actor.Export);
        var originalMarkerColor = component.LightColor;
        Assert.AreSame(original, component.GetPreviewLight(context.PackageCache, original));

        component.LightColor = System.Windows.Media.Colors.Red;
        component.LightingChannelDynamic = false;
        var edited = component.GetPreviewLight(context.PackageCache, original);
        Assert.AreEqual(new Vector3(1, 0, 0), edited.Color);
        Assert.IsFalse(edited.LightingChannels.OverlapsWith(LightingChannels.DynamicPrimitiveDefault));
        Assert.IsTrue(edited.LightingChannels.OverlapsWith(LightingChannels.StaticPrimitiveDefault));
        Assert.AreEqual(original.Position, edited.Position);
        Assert.IsNull(lightExport.GetProperty<StructProperty>("LightColor"));
        Assert.IsNull(lightExport.GetProperty<StructProperty>("LightingChannels"));

        component.LightColor = originalMarkerColor;
        component.LightingChannelDynamic = true;
        Assert.AreSame(original, component.GetPreviewLight(context.PackageCache, edited));
    }

    private static ExportEntry CreateExport(IMEPackage package, IEntry parent, string name, string className, PropertyCollection properties)
    {
        var classImport = new ImportEntry(package)
        {
            ObjectName = className,
            ClassName = "Class",
            PackageFile = "Engine"
        };
        package.AddImport(classImport);
        byte[] prePropertyBinary = className.EndsWith("Component") ? new byte[8] : new byte[4];
        var export = new ExportEntry(package, parent, name, prePropBinary: prePropertyBinary, properties: properties) { Class = classImport };
        package.AddExport(export);
        return export;
    }

    private sealed class TestActor(ExportEntry export) : ActorProxy(new TestContextForActor(), export);

    private sealed class TestLightActor : ActorProxy
    {
        public TestLightActor(ExportEntry export) : base(new TestContextForActor(), export)
        {
            IsLight = true;
        }
    }

    private sealed class TestContextForActor : IActorEditorContext
    {
        public LevelEditorRenderContext RenderContext => null;
        public bool IsApplyingUndoRedo => true;
    }
}
