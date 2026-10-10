using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.SharpDX;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using LegendaryExplorerCore.Unreal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using System.Threading.Tasks;
using LELevelEditor = LegendaryExplorer.Tools.LevelEditor.LevelEditor;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class TransformNudgeTests
{
    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);
    }

    [TestMethod]
    public void TranslateNudge_LocalCoordsUsesActorBasis_AndUndoRestores()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("NudgeTests.pcc", MEGame.LE3);
        using var actor = CreateActor(package, "Actor");
        actor.Rotation = new Rotator(0, 16384, 0);
        var before = actor.SnapshotTransform();

        var result = LELevelEditor.TransformNudge.TryApply(actor, LELevelEditor.TransformNudgeKind.Translate,
            EWidgetAxis.X, 1f, 10f, 5f, 0.1f, true, out var after);

        Assert.IsTrue(result);
        Assert.IsTrue(Vector3.Distance(new Vector3(0, 10, 0), actor.Location) < 0.001f);
        var action = new TransformAction(actor, before, after, "Nudge");
        action.Undo();
        Assert.AreEqual(Vector3.Zero, actor.Location);
        action.Redo();
        Assert.AreEqual(after, actor.SnapshotTransform());
    }

    [TestMethod]
    public void RotateNudge_WorldVsLocalProducesDifferentOrientation()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("NudgeTests.pcc", MEGame.LE3);
        using var localActor = CreateActor(package, "Local");
        using var worldActor = CreateActor(package, "World");
        var initial = new Rotator(3000, 4000, 5000);
        localActor.Rotation = initial;
        worldActor.Rotation = initial;

        LELevelEditor.TransformNudge.TryApply(localActor, LELevelEditor.TransformNudgeKind.Rotate,
            EWidgetAxis.X, 1f, 10f, 15f, 0.1f, true, out _);
        LELevelEditor.TransformNudge.TryApply(worldActor, LELevelEditor.TransformNudgeKind.Rotate,
            EWidgetAxis.X, 1f, 10f, 15f, 0.1f, false, out _);

        Assert.AreNotEqual(localActor.Rotation, worldActor.Rotation);
    }

    [TestMethod]
    public void ScaleAndUniformNudge_AdjustExpectedComponents()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("NudgeTests.pcc", MEGame.LE3);
        using var actor = CreateActor(package, "Actor");
        actor.DrawScale3D = new Vector3(1, 2, 3);
        actor.DrawScale = 1;

        Assert.IsTrue(LELevelEditor.TransformNudge.TryApply(actor, LELevelEditor.TransformNudgeKind.Scale,
            EWidgetAxis.Y, -1f, 10f, 5f, 0.25f, true, out _));
        Assert.IsTrue(LELevelEditor.TransformNudge.TryApply(actor, LELevelEditor.TransformNudgeKind.UniformScale,
            EWidgetAxis.XYZ, 1f, 10f, 5f, 0.25f, true, out _));

        Assert.AreEqual(new Vector3(1, 1.75f, 3), actor.DrawScale3D);
        Assert.AreEqual(1.25f, actor.DrawScale);
    }

    [TestMethod]
    public void ZeroIncrements_DoNotApplyNudge()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("NudgeTests.pcc", MEGame.LE3);
        using var actor = CreateActor(package, "Actor");
        var before = actor.SnapshotTransform();

        Assert.IsFalse(LELevelEditor.TransformNudge.TryApply(actor, LELevelEditor.TransformNudgeKind.Translate,
            EWidgetAxis.Z, 1f, 0f, 5f, 0.1f, true, out var afterTranslate));
        Assert.AreEqual(before, afterTranslate);
        Assert.IsFalse(LELevelEditor.TransformNudge.TryApply(actor, LELevelEditor.TransformNudgeKind.Rotate,
            EWidgetAxis.X, 1f, 10f, 0f, 0.1f, true, out var afterRotate));
        Assert.AreEqual(before, afterRotate);
        Assert.IsFalse(LELevelEditor.TransformNudge.TryApply(actor, LELevelEditor.TransformNudgeKind.Scale,
            EWidgetAxis.Y, 1f, 10f, 5f, 0f, true, out var afterScale));
        Assert.AreEqual(before, afterScale);
    }

    private static TestActor CreateActor(IMEPackage package, string name)
    {
        var classImport = new ImportEntry(package) { ObjectName = "Actor", ClassName = "Class", PackageFile = "Engine" };
        package.AddImport(classImport);
        var export = new ExportEntry(package, 0, name, prePropBinary: new byte[4], properties: []) { Class = classImport };
        package.AddExport(export);
        return new TestActor(new ActorContext(), export);
    }

    private sealed class TestActor(ActorContext context, ExportEntry export) : ActorProxy(context, export);

    private sealed class ActorContext : IActorEditorContext
    {
        public LevelEditorRenderContext RenderContext => null;
        public bool IsApplyingUndoRedo => true;
    }
}