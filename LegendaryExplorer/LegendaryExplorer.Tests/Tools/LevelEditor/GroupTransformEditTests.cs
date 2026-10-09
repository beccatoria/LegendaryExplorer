using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.SharpDX;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class GroupTransformEditTests
{
    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);
    }

    [TestMethod]
    public void Translation_AppliesOncePerMember_AndBatchUndoRedoRestoresWholeGroup()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("GroupTests.pcc", MEGame.LE3);
        using var lead = CreateActor(package, "Lead");
        using var member = CreateActor(package, "Member");
        member.Location = new Vector3(10, 20, 30);
        var memberBefore = member.SnapshotTransform();
        var before = lead.SnapshotTransform();
        lead.Location = new Vector3(5, 6, 7);
        var after = lead.SnapshotTransform();
        var history = new UndoHistory();
        history.Push(GroupTransformEdit.Apply(lead, [lead, member, member], _ => true, before, after, "Group move"));
        var memberAfter = member.SnapshotTransform();
        Assert.AreEqual(new Vector3(15, 26, 37), member.Location);

        history.Undo();
        Assert.AreEqual(before, lead.SnapshotTransform());
        Assert.AreEqual(memberBefore, member.SnapshotTransform());
        Assert.IsFalse(history.CanUndo);
        Assert.IsTrue(history.CanRedo);
        history.Redo();
        Assert.AreEqual(after, lead.SnapshotTransform());
        Assert.AreEqual(memberAfter, member.SnapshotTransform());
    }

    [TestMethod]
    public void RotationAndTranslation_RotateMemberAroundLeadAndPreserveOrientationDelta()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("GroupTests.pcc", MEGame.LE3);
        using var lead = CreateActor(package, "Lead");
        using var member = CreateActor(package, "Member");
        lead.Location = new Vector3(10, 0, 0);
        member.Location = new Vector3(20, 0, 0);
        var before = lead.SnapshotTransform();
        lead.Rotation = new Rotator(0, 16384, 0);
        lead.Location = new Vector3(15, 0, 0);
        var after = lead.SnapshotTransform();

        GroupTransformEdit.Apply(lead, [lead, member], _ => true, before, after, "Group rotate");

        Assert.IsTrue(Vector3.Distance(new Vector3(15, 10, 0), member.Location) < 0.001f);
        Assert.IsTrue(Vector3.Distance(Vector3.TransformNormal(Vector3.UnitX, after.Rotation.ToRotationMatrix()),
            Vector3.TransformNormal(Vector3.UnitX, member.Rotation.ToRotationMatrix())) < 0.001f);
    }

    [TestMethod]
    public void Scaling_PreservesMemberScaleRatiosAndZeroAxisFallbackWithoutMovingMembers()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("GroupTests.pcc", MEGame.LE3);
        using var lead = CreateActor(package, "Lead");
        using var member = CreateActor(package, "Member");
        lead.DrawScale = 2;
        lead.DrawScale3D = new Vector3(2, 0, 4);
        member.Location = new Vector3(10, 20, 30);
        member.DrawScale = 3;
        member.DrawScale3D = new Vector3(3, 5, 8);
        var before = lead.SnapshotTransform();
        lead.DrawScale = 4;
        lead.DrawScale3D = new Vector3(4, 2, 2);

        GroupTransformEdit.Apply(lead, [lead, member], _ => true, before, lead.SnapshotTransform(), "Group scale");

        Assert.AreEqual(6f, member.DrawScale);
        Assert.AreEqual(new Vector3(6, 7, 4), member.DrawScale3D);
        Assert.AreEqual(new Vector3(10, 20, 30), member.Location);
    }

    [TestMethod]
    public void GroupEdit_SkipsReadOnlyAndUnavailableMembers()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("GroupTests.pcc", MEGame.LE3);
        using var lead = CreateActor(package, "Lead");
        using var readOnly = CreateActor(package, "ReadOnly", false);
        using var unavailable = CreateActor(package, "Unavailable");
        using var editable = CreateActor(package, "Editable");
        var before = lead.SnapshotTransform();
        lead.Location = new Vector3(5, 0, 0);

        GroupTransformEdit.Apply(lead, [lead, readOnly, unavailable, editable], actor => actor != unavailable,
            before, lead.SnapshotTransform(), "Group move");

        Assert.AreEqual(Vector3.Zero, readOnly.Location);
        Assert.AreEqual(Vector3.Zero, unavailable.Location);
        Assert.AreEqual(new Vector3(5, 0, 0), editable.Location);
    }

    [TestMethod]
    public void UndoHistory_RebindsReplacementAndPrunesUnloadedMembers()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("GroupTests.pcc", MEGame.LE3);
        using var lead = CreateActor(package, "Lead");
        using var member = CreateActor(package, "Member");
        var before = lead.SnapshotTransform();
        lead.Location = new Vector3(5, 0, 0);
        var history = new UndoHistory();
        history.Push(GroupTransformEdit.Apply(lead, [lead, member], _ => true, before, lead.SnapshotTransform(), "Group move"));
        using var replacement = new TestActor(new ActorContext(true), lead.Export);
        replacement.Location = lead.Location;

        history.RebindActors([replacement]);
        history.Undo();
        Assert.AreEqual(Vector3.Zero, replacement.Location);
        Assert.AreEqual(new Vector3(5, 0, 0), lead.Location);
        Assert.AreEqual(new Vector3(5, 0, 0), member.Location);
        history.Redo();
        Assert.AreEqual(new Vector3(5, 0, 0), replacement.Location);
        history.RebindActors([]);
        Assert.IsFalse(history.CanUndo);
        Assert.IsFalse(history.CanRedo);
    }

    [TestMethod]
    public void CollectionGroup_CommitReconstructionAndUndoPreserveComponentTransform()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("CollectionGroup.pcc", MEGame.LE3);
        var context = new ActorContext(true);
        using var lead = CreateActor(package, "Lead");
        var collectionClass = new ImportEntry(package) { ObjectName = "StaticMeshCollectionActor", ClassName = "Class", PackageFile = "Engine" };
        package.AddImport(collectionClass);
        var collectionExport = new ExportEntry(package, 0, "Collection", prePropBinary: new byte[4], properties: []) { Class = collectionClass };
        package.AddExport(collectionExport);
        var componentClass = new ImportEntry(package) { ObjectName = "StaticMeshComponent", ClassName = "Class", PackageFile = "Engine" };
        package.AddImport(componentClass);
        var componentExport = new ExportEntry(package, collectionExport, "Component", prePropBinary: new byte[8], properties: []) { Class = componentClass };
        package.AddExport(componentExport);
        collectionExport.WriteProperties([new ArrayProperty<ObjectProperty>([new ObjectProperty(componentExport.UIndex)], "StaticMeshComponents")]);
        var collection = new StaticMeshCollectionActor
        {
            Export = collectionExport,
            Components = [],
            LocalToWorldTransforms = []
        };
        collection.Components.Add(componentExport.UIndex);
        collection.LocalToWorldTransforms.Add(Matrix4x4.CreateTranslation(10, 20, 30));
        collectionExport.WriteBinary(collection);
        collection = collectionExport.GetBinaryData<StaticMeshCollectionActor>();
        using var member = new StaticMeshComponentActorProxy(context, componentExport, collection, 0);
        var before = lead.SnapshotTransform();
        var memberBefore = member.SnapshotTransform();
        lead.Location = new Vector3(5, 6, 7);
        var history = new UndoHistory();
        history.Push(GroupTransformEdit.Apply(lead, [lead, member], _ => true, before, lead.SnapshotTransform(), "Collection move"));
        var memberAfter = member.SnapshotTransform();
        member.CommitChanges(collection);
        collectionExport.WriteBinary(collection);

        var reconstructed = collectionExport.GetBinaryData<StaticMeshCollectionActor>();
        using var replacement = new StaticMeshComponentActorProxy(context, componentExport, reconstructed, 0);
        Assert.AreEqual(memberAfter, replacement.SnapshotTransform());
        Assert.AreEqual(memberAfter.Location, replacement.Components[0].LocalToWorld.Translation);
        history.RebindActors([lead, replacement]);
        history.Undo();
        Assert.AreEqual(memberBefore, replacement.SnapshotTransform());
        Assert.AreEqual(memberBefore.Location, replacement.Components[0].LocalToWorld.Translation);
        history.Redo();
        Assert.AreEqual(memberAfter, replacement.SnapshotTransform());
    }

    [TestMethod]
    public void GroupDrag_RepeatedUpdatesUseOriginalSnapshotsAndOneBatchUndo()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("GroupDrag.pcc", MEGame.LE3);
        using var lead = CreateActor(package, "Lead");
        using var pivot = CreateActor(package, "Pivot");
        using var readOnly = CreateActor(package, "ReadOnly", false);
        lead.Location = new Vector3(20, 0, 0);
        pivot.Location = new Vector3(10, 0, 0);
        var leadBefore = lead.SnapshotTransform();
        var pivotBefore = pivot.SnapshotTransform();
        var drag = new GroupTransformDrag(pivot, [lead, pivot, lead, readOnly], _ => true);
        pivot.Location += new Vector3(5, 0, 0);
        drag.Update(pivot.SnapshotTransform(), "Drag");
        Assert.AreEqual(new Vector3(25, 0, 0), lead.Location);
        pivot.Location += new Vector3(5, 0, 0);
        drag.Update(pivot.SnapshotTransform(), "Drag");
        Assert.AreEqual(new Vector3(30, 0, 0), lead.Location);
        Assert.AreEqual(Vector3.Zero, readOnly.Location);
        var history = new UndoHistory();
        history.Push(drag.Update(pivot.SnapshotTransform(), "Drag"));
        history.Undo();
        Assert.AreEqual(leadBefore, lead.SnapshotTransform());
        Assert.AreEqual(pivotBefore, pivot.SnapshotTransform());
        Assert.IsFalse(history.CanUndo);
        history.Redo();
        Assert.AreEqual(new Vector3(30, 0, 0), lead.Location);
        Assert.AreEqual(new Vector3(20, 0, 0), pivot.Location);
    }

    [TestMethod]
    public void GroupDrag_RotationScaleAndReturnToStartDoNotCompound()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("GroupDrag.pcc", MEGame.LE3);
        using var pivot = CreateActor(package, "Pivot");
        using var member = CreateActor(package, "Member");
        member.Location = new Vector3(10, 0, 0);
        member.DrawScale = 3;
        member.DrawScale3D = new Vector3(2, 3, 4);
        var before = pivot.SnapshotTransform();
        var memberBefore = member.SnapshotTransform();
        var drag = new GroupTransformDrag(pivot, [pivot, member], _ => true);
        pivot.Rotation = new Rotator(0, 16384, 0);
        pivot.DrawScale = 2;
        pivot.DrawScale3D = new Vector3(2, 1, 1);
        drag.Update(pivot.SnapshotTransform(), "Drag");
        Assert.IsTrue(Vector3.Distance(new Vector3(0, 10, 0), member.Location) < 0.001f);
        Assert.AreEqual(6f, member.DrawScale);
        Assert.AreEqual(new Vector3(4, 3, 4), member.DrawScale3D);
        pivot.Rotation = new Rotator(0, 32768, 0);
        pivot.DrawScale = 3;
        pivot.DrawScale3D = new Vector3(3, 1, 1);
        drag.Update(pivot.SnapshotTransform(), "Drag");
        Assert.IsTrue(Vector3.Distance(new Vector3(-10, 0, 0), member.Location) < 0.001f);
        Assert.AreEqual(9f, member.DrawScale);
        Assert.AreEqual(new Vector3(6, 3, 4), member.DrawScale3D);
        pivot.RestoreTransform(before);
        drag.Update(before, "Drag");
        Assert.AreEqual(memberBefore, member.SnapshotTransform());
    }

    [TestMethod]
    public void Widget_EndDragNotifiesCompletionEvenWhenReturnedToStart()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("GroupDrag.pcc", MEGame.LE3);
        using var actor = CreateActor(package, "Pivot");
        var widget = new Widget { Attach = actor };
        int starts = 0, completions = 0;
        widget.OnDragStart = pivot => { Assert.AreSame(actor, pivot); starts++; };
        widget.OnDragComplete = (pivot, before, after) =>
        {
            Assert.AreEqual(before, after);
            completions++;
        };
        widget.BeginDrag(0, 0);
        widget.EndDrag();
        widget.EndDrag();
        Assert.AreEqual(1, starts);
        Assert.AreEqual(1, completions);
        Assert.IsFalse(widget.IsDragging);
    }

    [TestMethod]
    public void UndoHistory_RebindingPrunesMiddleUndoActionWithoutChangingOrder()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("HistoryOrder.pcc", MEGame.LE3);
        using var actor = CreateActor(package, "Retained");
        using var unloaded = CreateActor(package, "Unloaded");
        var history = new UndoHistory();
        PushMove(history, actor, 10);
        PushMove(history, unloaded, 50);
        PushMove(history, actor, 20);
        using var replacement = new TestActor(new ActorContext(true), actor.Export) { Location = actor.Location };

        history.RebindActors([replacement]);

        history.Undo();
        Assert.AreEqual(new Vector3(10, 0, 0), replacement.Location);
        Assert.IsTrue(history.CanUndo);
        history.Undo();
        Assert.AreEqual(Vector3.Zero, replacement.Location);
        Assert.IsFalse(history.CanUndo);
        history.Redo();
        Assert.AreEqual(new Vector3(10, 0, 0), replacement.Location);
        history.Redo();
        Assert.AreEqual(new Vector3(20, 0, 0), replacement.Location);
        Assert.IsFalse(history.CanRedo);
        Assert.AreEqual(new Vector3(20, 0, 0), actor.Location);
        Assert.AreEqual(new Vector3(50, 0, 0), unloaded.Location);
    }

    [TestMethod]
    public void UndoHistory_RebindingPrunesMiddleRedoActionWithoutChangingOrder()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("HistoryOrder.pcc", MEGame.LE3);
        using var actor = CreateActor(package, "Retained");
        using var unloaded = CreateActor(package, "Unloaded");
        var history = new UndoHistory();
        PushMove(history, actor, 10);
        PushMove(history, actor, 20);
        PushMove(history, unloaded, 50);
        PushMove(history, actor, 30);
        history.UndoMultiple(3);
        using var replacement = new TestActor(new ActorContext(true), actor.Export) { Location = actor.Location };

        history.RebindActors([replacement]);

        Assert.IsTrue(history.CanUndo);
        history.Redo();
        Assert.AreEqual(new Vector3(20, 0, 0), replacement.Location);
        Assert.IsTrue(history.CanRedo);
        history.Redo();
        Assert.AreEqual(new Vector3(30, 0, 0), replacement.Location);
        Assert.IsFalse(history.CanRedo);
        history.UndoMultiple(3);
        Assert.AreEqual(Vector3.Zero, replacement.Location);
        Assert.IsFalse(history.CanUndo);
        Assert.AreEqual(new Vector3(10, 0, 0), actor.Location);
        Assert.AreEqual(Vector3.Zero, unloaded.Location);
    }

    private static void PushMove(UndoHistory history, ActorProxy actor, float x)
    {
        var before = actor.SnapshotTransform();
        actor.Location = new Vector3(x, 0, 0);
        history.Push(new TransformAction(actor, before, actor.SnapshotTransform(), "Move"));
    }

    private static TestActor CreateActor(IMEPackage package, string name, bool editable = true)
    {
        var classImport = new ImportEntry(package) { ObjectName = "Actor", ClassName = "Class", PackageFile = "Engine" };
        package.AddImport(classImport);
        var export = new ExportEntry(package, 0, name, prePropBinary: new byte[4], properties: []) { Class = classImport };
        package.AddExport(export);
        return new TestActor(new ActorContext(editable), export);
    }

    private sealed class TestActor(ActorContext context, ExportEntry export) : ActorProxy(context, export);

    private sealed class ActorContext(bool editable) : IActorEditorContext
    {
        public LevelEditorRenderContext RenderContext => null;
        public bool IsApplyingUndoRedo => editable;
    }
}
