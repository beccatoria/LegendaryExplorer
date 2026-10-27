using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class LevelLightPreviewTests
{
    public TestContext TestContext { get; set; }

    [ClassInitialize]
    public static void Initialize(TestContext _) => LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);

    [TestMethod]
    public void LightPreview_BatchesEditsWithoutLosingHiddenMembershipOrWritingExports()
    {
        using var fixture = new Fixture(8);
        var updater = new LevelLightPreviewUpdater();
        updater.Update(fixture.Actors, fixture.Lights, fixture.Context);
        fixture.Context.Visibility.SetActorVisibility(fixture.Actors[1].VisibilityKey, false);
        fixture.Context.RefreshLightVisibility();
        fixture.Context.GetLights(out int before);
        var oldLights = fixture.Lights.ToArray();
        foreach (var actor in fixture.Actors) ((LightComponentProxy)actor.Components[0]).Brightness = 4;
        updater.Update(fixture.Actors, fixture.Lights, fixture.Context);
        var lights = fixture.Context.GetLights(out int after);
        Assert.HasCount(7, lights);
        Assert.AreEqual(before + 2, after, "One batch must replace and filter the snapshot once.");
        for (int i = 0; i < fixture.Lights.Count; i++)
        {
            Assert.AreEqual(oldLights[i].Color * 2, fixture.Lights[i].Color);
            Assert.AreEqual(2f, fixture.Actors[i].Components[0].Export.GetProperty<FloatProperty>("Brightness").Value);
        }
        var unchanged = fixture.Context.GetLights(out int version);
        updater.Update(fixture.Actors, fixture.Lights, fixture.Context);
        Assert.AreSame(unchanged, fixture.Context.GetLights(out int repeated));
        Assert.AreEqual(version, repeated);
    }

    [TestMethod]
    public void NewBaseline_IsRetainedRatherThanOverwrittenByTheOldPreviewSnapshot()
    {
        using var fixture = new Fixture(1);
        var updater = new LevelLightPreviewUpdater();
        updater.Update(fixture.Actors, fixture.Lights, fixture.Context);
        var component = (LightComponentProxy)fixture.Actors[0].Components[0];
        var previous = fixture.Lights[0];
        component.Export.WriteProperty(new FloatProperty(200, "Radius"));
        var fresh = SceneLight.Create(component.Export, "PointLight", fixture.Context.PackageCache,
            fixture.Actors[0].LocalToWorld, fixture.Actors[0].Export);
        fixture.Context.ReplaceLight(previous, fresh);
        fixture.Lights[0] = fresh;
        updater.Update(fixture.Actors, fixture.Lights, fixture.Context);
        Assert.AreSame(fresh, fixture.Lights[0]);
        Assert.AreEqual(200f, fixture.Context.GetLights(out _)[0].Radius);

        component.Brightness = 4;
        updater.Update(fixture.Actors, fixture.Lights, fixture.Context);
        Assert.AreEqual(200f, fixture.Lights[0].Radius, "Fresh baseline values must survive an uncommitted brightness edit.");
        Assert.AreEqual(fresh.Color * 2, fixture.Lights[0].Color);
    }

    [TestMethod]
    public void Bindings_RebuildAfterActorsOrLightListsAreReplaced()
    {
        using var fixture = new Fixture(3);
        var updater = new LevelLightPreviewUpdater();
        updater.Update(fixture.Actors, fixture.Lights, fixture.Context);
        var reversed = fixture.Lights.AsEnumerable().Reverse().ToList();
        ((LightComponentProxy)fixture.Actors[0].Components[0]).Brightness = 4;
        updater.Update(fixture.Actors, reversed, fixture.Context);
        Assert.AreEqual(fixture.Actors[0].Components[0].Export, reversed[2].Export);
        Assert.AreEqual(fixture.Lights[0].Color * 2, reversed[2].Color);

        var old = reversed[2];
        fixture.Actors[0].Dispose();
        fixture.Actors.RemoveAt(0);
        updater.Invalidate();
        updater.Update(fixture.Actors, reversed, fixture.Context);
        Assert.AreSame(old, reversed[2]);
        updater.Clear();
        updater.Update([], [], fixture.Context);
    }

    [TestMethod]
    public void CancelledMaterialPreparation_RemovesPendingWorkAndModelDisposalReleasesIt()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("PendingShaders.pcc", MEGame.LE3);
        var context = new LevelEditorRenderContext();
        var export = Fixture.CreateExport(package, null, "Material", "MaterialInstanceConstant", []);
        var material = new MaterialRenderProxy(context, export);
        material.QueueGameShaderLoad(ELightMapType.LMT_None);
        material.QueueGameShaderLoad(ELightMapType.LMT_None);
        Assert.IsTrue(context.HasPendingGameShaderLoads);
        material.CancelPendingShaderLoad();
        Assert.IsFalse(context.HasPendingGameShaderLoads);
        var preview = new ModelPreview<LEVertex>(context, new Mesh<LEVertex>(null, [], []));
        var tracked = new TrackedMaterial(context, export, material);
        preview.Materials.Add("tracked", tracked);
        material.QueueGameShaderLoad(ELightMapType.LMT_None);
        preview.Dispose();
        Assert.IsTrue(tracked.Released);
        Assert.IsFalse(context.HasPendingGameShaderLoads);
    }

    [TestMethod]
    public void FailedConstructionScope_RemovesNewRegistrationsAndPreservesExistingMaterials()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("ShaderRollback.pcc", MEGame.LE3);
        var context = new LevelEditorRenderContext();
        var export = Fixture.CreateExport(package, null, "Material", "MaterialInstanceConstant", []);
        var original = new MaterialRenderProxy(context, export);
        original.QueueGameShaderLoad(ELightMapType.LMT_None);
        using (context.TrackSceneLoadResources())
        {
            var incomplete = new MaterialRenderProxy(context, export);
            incomplete.QueueGameShaderLoad(ELightMapType.LMT_None);
        }
        Assert.IsTrue(context.HasPendingGameShaderLoads);
        original.CancelPendingShaderLoad();
        Assert.IsFalse(context.HasPendingGameShaderLoads, "The failed construction's material retained a pending reference.");
        using (var scope = context.TrackSceneLoadResources())
        {
            original.QueueGameShaderLoad(ELightMapType.LMT_None);
            scope.Commit();
        }
        Assert.IsTrue(context.HasPendingGameShaderLoads);
        original.CancelPendingShaderLoad();
    }

    [TestMethod]
    [TestCategory("Performance")]
    public void SteadyLightFrames_ComparePreviousScanWithIndexedProductionPath()
    {
        if (Environment.GetEnvironmentVariable("LEX_LEVEL_EDITOR_PERF") != "1")
            Assert.Inconclusive("Set LEX_LEVEL_EDITOR_PERF=1 to run the CPU benchmark.");
        using var fixture = new Fixture(256);
        var updater = new LevelLightPreviewUpdater();
        Action previous = () =>
        {
            foreach (var actor in fixture.Actors.Where(actor => actor.IsLight))
                foreach (var component in actor.Components.OfType<LightComponentProxy>())
                {
                    int index = fixture.Lights.FindIndex(light => ReferenceEquals(light.Export, component.Export));
                    var baseline = fixture.Lights[index];
                    var light = component.GetPreviewLight(fixture.Context.PackageCache, baseline);
                    if (light is not null && !ReferenceEquals(baseline, light))
                    {
                        fixture.Context.ReplaceLight(baseline, light);
                        fixture.Lights[index] = light;
                    }
                }
            // The previous contribution predicate formatted a fresh key for each light/frame.
            int included = 0;
            foreach (var light in fixture.Lights)
                if (fixture.Context.Visibility.VisibleActorKeys.Contains(
                    EditorVisibilityState.GetActorKey(light.VisibilityExport.FileRef.FilePath, light.VisibilityExport.UIndex))) included++;
            if (included != fixture.Lights.Count) throw new AssertFailedException("Reference membership changed.");
        };
        Action current = () =>
        {
            updater.Update(fixture.Actors, fixture.Lights, fixture.Context);
            fixture.Context.RefreshLightVisibility();
        };
        for (int i = 0; i < 20; i++) { previous(); current(); }
        var oldSamples = new List<(double Milliseconds, long Allocations)>();
        var newSamples = new List<(double Milliseconds, long Allocations)>();
        for (int i = 0; i < 5; i++)
        {
            oldSamples.Add(Measure(previous, 300));
            newSamples.Add(Measure(current, 300));
        }
        var oldSample = oldSamples.OrderBy(s => s.Milliseconds).ElementAt(2);
        var newSample = newSamples.OrderBy(s => s.Milliseconds).ElementAt(2);
        TestContext.WriteLine($"256 lights, 300 steady frames, median of 5; previous CPU loop: {oldSample.Milliseconds:F3} ms, {oldSample.Allocations} B; production indexed loop: {newSample.Milliseconds:F3} ms, {newSample.Allocations} B.");
        Assert.HasCount(256, fixture.Context.GetLights(out _));
    }

    private static (double Milliseconds, long Allocations) Measure(Action action, int count)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++) action();
        return (Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    private sealed class TrackedMaterial(MeshRenderContext context, ExportEntry export, MaterialRenderProxy proxy)
        : ModelPreviewMaterial<LEVertex>(context, export)
    {
        public bool Released { get; private set; }
        protected override LegendaryExplorerCore.Unreal.Classes.MaterialInstanceConstantLevelEditor CreateMaterial(MeshRenderContext renderContext, ExportEntry entry)
            => new(entry, renderContext.PackageCache);
        public override void ReleasePendingShaders() { Released = true; proxy.CancelPendingShaderLoad(); }
        public override void RenderSection(ModelPreviewLOD<LEVertex> lod, ModelPreviewSection section, MeshRenderContext context) { }
    }

    private sealed class Fixture : IDisposable, IActorEditorContext
    {
        private readonly IMEPackage _package = MEPackageHandler.CreateMemoryEmptyPackage("LightFrames.pcc", MEGame.LE2);
        public List<ActorProxy> Actors { get; } = [];
        public List<SceneLight> Lights { get; } = [];
        public LevelEditorRenderContext Context { get; } = new() { ShowLights = true, UseVisibleSetOnly = true };
        public LevelEditorRenderContext RenderContext => Context;
        public bool IsApplyingUndoRedo => true;

        public Fixture(int count)
        {
            for (int i = 0; i < count; i++)
            {
                var actorExport = CreateExport(_package, null, $"Actor{i}", "PointLight", []);
                var actor = new LightActor(this, actorExport);
                var componentExport = CreateExport(_package, actorExport, $"Light{i}", "PointLightComponent",
                    [new FloatProperty(2, "Brightness"), new FloatProperty(100, "Radius")]);
                actor.Components.Add(new LightComponentProxy(Context, componentExport, actor));
                Actors.Add(actor);
                Context.Visibility.SetActorVisibility(actor.VisibilityKey, true);
                Lights.Add(SceneLight.Create(componentExport, "PointLight", Context.PackageCache, actor.LocalToWorld, actorExport));
            }
            Context.AddLights(Lights);
        }

        public static ExportEntry CreateExport(IMEPackage package, IEntry parent, string name, string className, PropertyCollection props)
        {
            var classImport = new ImportEntry(package) { ObjectName = className, ClassName = "Class", PackageFile = "Engine" };
            package.AddImport(classImport);
            var export = new ExportEntry(package, parent, name, prePropBinary: new byte[className.EndsWith("Component") ? 8 : 4], properties: props) { Class = classImport };
            package.AddExport(export);
            return export;
        }

        public void Dispose()
        {
            foreach (var actor in Actors) actor.Dispose();
            Context.UnloadLevel();
            _package.Dispose();
        }
    }

    private sealed class LightActor : ActorProxy
    {
        public LightActor(IActorEditorContext context, ExportEntry export) : base(context, export) => IsLight = true;
    }
}
