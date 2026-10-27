using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorer.Dialogs;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using EditorWindow = LegendaryExplorer.Tools.LevelEditor.LevelEditor;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class VisibleSetsWorkflowTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _) => LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);

    [TestMethod]
    public void EditorCommands_RestoreLegacySetsRecoverAllAndPreserveSelectionsAcrossModesAndReloads()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = System.Windows.Application.Current ?? new System.Windows.Application();
                application.Resources = (System.Windows.ResourceDictionary)System.Windows.Application.LoadComponent(
                    new Uri("/LegendaryExplorer;component/AppResources.xaml", UriKind.Relative));
                var editor = new EditorWindow(); // Never shown or closed: no GPU or preference writes.
                using var cargo = MEPackageHandler.CreateMemoryEmptyPackage("Cargo.pcc", MEGame.LE2);
                using var freighter = MEPackageHandler.CreateMemoryEmptyPackage("Freighter.pcc", MEGame.LE2);
                var first = CreateActor(cargo, "Mesh", "StaticMeshActor");
                var second = CreateActor(freighter, "Mesh", "StaticMeshActor");
                editor.AddActor(first);
                editor.AddActor(second);
                var state = editor.RenderContext.Visibility;

                // This is the saved failure: the class exists in both files, but only Cargo is included.
                var legacy = new RecentViewState
                {
                    HasUserEditedVisibleSets = true, UseVisibleSetOnly = true,
                    ObjectRenderMode = ObjectRenderMode.VisibleSetOnly,
                    VisibleActorKeys = [first.VisibilityKey], HiddenActorClasses = []
                };
                Invoke(editor, "ApplyViewState", legacy);
                Assert.HasCount(1, state.VisibleActorKeys);
                Assert.IsFalse(state.VisibleActorKeys.Contains(second.VisibilityKey));

                // Add All must work even if the class is already in the Visible list.
                var dialog = new VisibleSetsManagerDialog(["StaticMeshActor"], ["StaticMeshActor"], null);
                Invoke(dialog, "AddAll_Click", null, new System.Windows.RoutedEventArgs());
                Assert.IsTrue(dialog.ResetActorSettingsForClasses.Contains("StaticMeshActor"));
                state.ApplyClassSelection([(first.VisibilityKey, "StaticMeshActor"), (second.VisibilityKey, "StaticMeshActor")],
                    dialog.GetVisibleClasses(), dialog.ResetActorSettingsForClasses, dialog.ClearActorLevelSettingsRequested, dialog.AllClassesVisibility);
                Assert.HasCount(2, state.VisibleActorKeys);
                Assert.IsEmpty(state.ActorVisibilityOverrides);

                editor.SelectedActor = second;
                Invoke(editor, "RemoveSelectedFromVisibleSet");
                for (int i = 0; i < 5; i++)
                {
                    editor.ObjectRenderMode = ObjectRenderMode.Full;
                    editor.ObjectRenderMode = ObjectRenderMode.VisibleSetOnly;
                    Assert.IsFalse(state.VisibleActorKeys.Contains(second.VisibilityKey));
                    Assert.IsTrue(state.VisibleActorKeys.Contains(first.VisibilityKey));
                }

                var saved = JsonConvert.DeserializeObject<RecentViewState>(JsonConvert.SerializeObject(Invoke(editor, "CaptureCurrentViewState")));
                Invoke(editor, "ShowAllMeshes");
                Invoke(editor, "ApplyViewState", saved);
                Assert.IsFalse(state.VisibleActorKeys.Contains(second.VisibilityKey));
                Assert.IsTrue(state.VisibleActorKeys.Contains(first.VisibilityKey));

                // Replacement proxies use the same qualified identity and retain a deliberate hide.
                editor.RemoveActor(second, preserveVisibility: true);
                second = new TestActor(second.Export);
                editor.AddActor(second);
                Assert.IsFalse(state.VisibleActorKeys.Contains(second.VisibilityKey));
                var later = CreateActor(freighter, "Later", "StaticMeshActor");
                editor.AddActor(later);
                Assert.IsTrue(state.VisibleActorKeys.Contains(later.VisibilityKey));

                // Clear All must stay empty through mode changes and a removal in Full mode.
                editor.SelectedActor = first;
                Invoke(editor, "ClearVisibleSet");
                editor.ObjectRenderMode = ObjectRenderMode.Full;
                Invoke(editor, "RemoveSelectedFromVisibleSet");
                editor.ObjectRenderMode = ObjectRenderMode.VisibleSetOnly;
                Assert.IsEmpty(state.VisibleActorKeys);

                // Adding one filtered marker must not accidentally add its entire category.
                var emitterA = CreateActor(cargo, "EmitterA", "Emitter", emitter: true);
                var emitterB = CreateActor(cargo, "EmitterB", "Emitter", emitter: true);
                editor.AddActor(emitterA);
                editor.AddActor(emitterB);
                editor.SelectedActor = emitterA;
                var viewer = editor.FindName("SceneViewer");
                var renderFlag = viewer.GetType().GetField("_shouldRender", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsFalse((bool)renderFlag.GetValue(viewer));
                Invoke(editor, "AddSelectedToVisibleSet");
                Assert.IsFalse((bool)renderFlag.GetValue(viewer), "A visibility edit must not resume a paused viewport.");
                Assert.IsTrue(editor.ShowEmitters);
                CollectionAssert.AreEquivalent(new[] { emitterA.VisibilityKey }, new List<string>(state.VisibleActorKeys));

                // Top-down nearby selection ignores Z and large limits cannot overflow int32.
                Invoke(editor, "ClearVisibleSet");
                editor.IsOrthographicView = true;
                editor.RenderContext.Camera.Position = new System.Numerics.Vector3(0, 0, 40000);
                editor.VisibleSetDistance = 5000;
                Invoke(editor, "AddNearbyToVisibleSet");
                Assert.IsTrue(state.VisibleActorKeys.Contains(first.VisibilityKey));
                editor.IsOrthographicView = false;
                editor.RenderContext.Camera.Position = new System.Numerics.Vector3(0, 0, 49000);
                editor.VisibleSetDistance = 50000;
                Invoke(editor, "ShowOnlyNearby");
                Assert.IsTrue(state.VisibleActorKeys.Contains(first.VisibilityKey));

                Invoke(editor, "CloseAllFiles");
                Assert.IsFalse(state.HasUserEditedVisibleSets);
                Assert.IsEmpty(state.ActorVisibilityOverrides);
                var fresh = CreateActor(cargo, "Fresh", "StaticMeshActor");
                editor.AddActor(fresh);
                editor.ObjectRenderMode = ObjectRenderMode.VisibleSetOnly;
                Assert.IsTrue(state.VisibleActorKeys.Contains(fresh.VisibilityKey));
                Invoke(editor, "CloseAllFiles");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new AssertFailedException($"Visible Sets workflow failed: {failure}", failure);
    }

    private static object Invoke(object target, string name, params object[] arguments)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);

    private static TestActor CreateActor(IMEPackage package, string name, string className, bool emitter = false)
    {
        var classImport = new ImportEntry(package) { ObjectName = className, ClassName = "Class", PackageFile = "Engine" };
        package.AddImport(classImport);
        var export = new ExportEntry(package, null, name, properties: []) { Class = classImport };
        package.AddExport(export);
        return new TestActor(export, emitter);
    }

    private sealed class TestActor : ActorProxy
    {
        public TestActor(ExportEntry export, bool emitter = false) : base(null, export) => IsEmitter = emitter;
    }
}
