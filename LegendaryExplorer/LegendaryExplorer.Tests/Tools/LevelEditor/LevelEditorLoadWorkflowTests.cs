using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Threading;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using EditorWindow = LegendaryExplorer.Tools.LevelEditor.LevelEditor;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class LevelEditorLoadWorkflowTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _) => LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);

    [TestMethod]
    public async Task PackageNotifications_MarshalCoalesceAndFlushOnlyAfterSceneWorkerCompletes()
    {
        await LevelEditorTestHost.RunAsync(async editor =>
        {
            editor.UseGameShaders = false;
            using var package = MEPackageHandler.CreateMemoryEmptyPackage("QueuedUpdates.pcc", MEGame.LE2);
            var levelImport = new ImportEntry(package) { ObjectName = "Level", ClassName = "Class", PackageFile = "Engine" };
            package.AddImport(levelImport);
            var levelExport = new ExportEntry(package, null, "Level", properties: []) { Class = levelImport };
            package.AddExport(levelExport);
            levelExport.WriteBinary(Level.Create(package.Game));
            var sequenceImport = new ImportEntry(package) { ObjectName = "Sequence", ClassName = "Class", PackageFile = "Engine" };
            package.AddImport(sequenceImport);
            var sequence = new ExportEntry(package, null, "Sequence", properties: []) { Class = sequenceImport };
            package.AddExport(sequence);
            var file = new OpenLevelFile(editor, package, levelExport, 0);
            file.LightDependencies.Rebuild(levelExport.GetBinaryData<Level>(), []);
            package.RegisterTool(file);
            editor.OpenFiles.Add(file);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int thread = Environment.CurrentManagedThreadId;
            var load = LevelEditorTestHost.RunOperation(editor, false, async _ =>
            {
                entered.SetResult();
                await release.Task;
                Assert.AreEqual(thread, Environment.CurrentManagedThreadId, "Publication must return to the UI thread.");
            });
            await entered.Task;
            file.HandleSaveStateChange(false);
            Assert.IsTrue(editor.IsBusy, "A save completion must not unlock a load in progress.");
            editor.RenderContext.GetLights(out int before);
            await Task.Run(() =>
            {
                file.HandleUpdate([new(PackageChange.ExportData, sequence.UIndex)]);
                file.HandleUpdate([new(PackageChange.ExportData, sequence.UIndex)]);
            });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var pending = (IDictionary)typeof(EditorWindow).GetField("_pendingFileUpdates", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual(1, ((IDictionary)pending[file]).Count);
            editor.RenderContext.GetLights(out int during);
            Assert.AreEqual(before, during);
            release.SetResult();
            await load;
            Assert.AreEqual(0, pending.Count);
            editor.RenderContext.GetLights(out int after);
            Assert.AreEqual(before, after, "Unrelated Sequence updates should not rebuild lights or shaders.");
            file.Dispose();
            file.HandleUpdate([new(PackageChange.ExportData, levelExport.UIndex)]);
            Assert.AreEqual(0, pending.Count, "A late callback from a closed file must be ignored.");
        }).WaitAsync(TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public async Task CombinedWireframeFilteringAndCameraTextRemainStableAcrossRestoration()
    {
        await LevelEditorTestHost.RunAsync(editor =>
        {
            editor.ObjectRenderMode = ObjectRenderMode.Wireframe;
            editor.UseVisibleSetOnly = true;
            var saved = LevelEditorTestHost.Invoke(editor, "CaptureCurrentViewState");
            editor.ObjectRenderMode = ObjectRenderMode.Full;
            LevelEditorTestHost.Invoke(editor, "ApplyViewState", saved);
            Assert.AreEqual(ObjectRenderMode.Wireframe, editor.ObjectRenderMode);
            Assert.IsTrue(editor.UseVisibleSetOnly);
            var update = typeof(EditorWindow).GetMethod("UpdateCameraCoordinatesText", BindingFlags.Instance | BindingFlags.NonPublic)
                .CreateDelegate<Action>(editor);
            update();
            string original = editor.CameraCoordinates;
            for (int i = 0; i < 100; i++) update();
            Assert.AreSame(original, editor.CameraCoordinates);
            editor.RenderContext.Camera.Position += System.Numerics.Vector3.UnitX * 10;
            update();
            Assert.AreNotEqual(original, editor.CameraCoordinates);
            return Task.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(15));
    }

    [TestMethod]
    public async Task ClosingScene_DropsSelectionGroupAndClickHistoryReferences()
    {
        await LevelEditorTestHost.RunAsync(editor =>
        {
            using var package = MEPackageHandler.CreateMemoryEmptyPackage("ClosedScene.pcc", MEGame.LE2);
            var actorClass = new ImportEntry(package) { ObjectName = "Actor", ClassName = "Class", PackageFile = "Engine" };
            package.AddImport(actorClass);
            var firstExport = new ExportEntry(package, null, "First", properties: []) { Class = actorClass };
            var secondExport = new ExportEntry(package, null, "Second", properties: []) { Class = actorClass };
            package.AddExport(firstExport);
            package.AddExport(secondExport);
            var first = new SimpleActor(firstExport);
            var second = new SimpleActor(secondExport);
            editor.AddActor(first);
            editor.AddActor(second);
            editor.SelectedActor = first;
            typeof(EditorWindow).GetProperty("ActiveTransformGroup").SetValue(editor, new ActorTransformGroup("Closed", first, [first, second]));
            var lastClick = typeof(LevelEditorRenderContext).GetField("_lastLeftClickedActor", BindingFlags.Instance | BindingFlags.NonPublic);
            lastClick.SetValue(editor.RenderContext, first);
            editor.RenderContext.TransformWidget.IsDragging = true;
            LevelEditorTestHost.Invoke(editor, "CloseAllFiles");
            Assert.IsNull(editor.SelectedActor);
            Assert.IsNull(editor.ActiveTransformGroup);
            Assert.IsNull(editor.RenderContext.SelectedActor);
            Assert.IsNull(editor.RenderContext.SelectedBioStageMarker);
            Assert.IsNull(editor.RenderContext.TransformWidget.Attach);
            Assert.IsFalse(editor.RenderContext.TransformWidget.IsDragging);
            Assert.IsNull(lastClick.GetValue(editor.RenderContext));
            return Task.CompletedTask;
        }).WaitAsync(TimeSpan.FromSeconds(15));
    }

    private sealed class SimpleActor(ExportEntry export) : ActorProxy(null, export);
}
