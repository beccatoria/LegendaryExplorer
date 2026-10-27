using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
public class LevelLightUpdateDependenciesTests
{
    public TestContext TestContext { get; set; }
    [ClassInitialize]
    public static void Initialize(TestContext _) => LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);

    [TestMethod]
    public void ReferenceClosure_CoversNestedPropertiesArchetypesAndCyclesButExcludesUnrelatedData()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("LightDependencies.pcc", MEGame.LE2);
        var levelExport = CreateExport(package, "Level", "Level");
        var unrelated = CreateExport(package, "Sequence", "Sequence");
        var volume = CreateExport(package, "Exclusion", "Actor");
        var archetype = CreateExport(package, "Template", "Actor");
        var source = CreateExport(package, "Source", "SeqAct_Interp",
            [new ArrayProperty<StructProperty>([new StructProperty("SeqVarLink", false,
                new ArrayProperty<ObjectProperty>([new ObjectProperty(volume.UIndex)], "LinkedVariables"))], "VariableLinks")]);
        source.Archetype = archetype;
        volume.WriteProperty(new ObjectProperty(source.UIndex, "Cycle"));
        var level = Level.Create(package.Game);
        levelExport.WriteBinary(level);
        level = levelExport.GetBinaryData<Level>();
        var index = new LevelLightUpdateDependencies();
        index.Rebuild(level, [source]);
        Assert.IsTrue(index.IsComplete);
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportData, source.UIndex)]));
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportData, volume.UIndex)]));
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportData, archetype.UIndex)]));
        Assert.IsFalse(index.NeedsLightRefresh([new(PackageChange.ExportData, unrelated.UIndex)]));
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportHeader, unrelated.UIndex)]));
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportAdd, unrelated.UIndex)]));
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ImportHeader, -1)]));
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.NameEdit, 0)]));

        // Replacing the source's references must remove old dependencies and discover new ones.
        source.WriteProperties([new ObjectProperty(unrelated.UIndex, "Replacement")]);
        source.Archetype = null;
        index.Rebuild(level, [source]);
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportData, unrelated.UIndex)]));
        Assert.IsFalse(index.NeedsLightRefresh([new(PackageChange.ExportData, volume.UIndex)]));
    }

    [TestMethod]
    public void MissingOrUnreadableIndex_FallsBackToRefreshingAndNewLightSourcesAreDetected()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("LightFallback.pcc", MEGame.LE2);
        var levelExport = CreateExport(package, "Level", "Level");
        levelExport.WriteBinary(Level.Create(package.Game));
        var actor = CreateExport(package, "Actor", "Actor");
        var component = CreateExport(package, "Component", "Actor");
        var broken = CreateExport(package, "BrokenMesh", "StaticMesh"); // Deliberately missing mesh binary.
        var index = new LevelLightUpdateDependencies();
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportData, actor.UIndex)]));
        var level = levelExport.GetBinaryData<Level>();
        index.Rebuild(level, []);
        Assert.IsFalse(index.NeedsLightRefresh([new(PackageChange.ExportData, actor.UIndex)]));
        actor.WriteProperty(new ObjectProperty(component.UIndex, "LightComponent"));
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportData, actor.UIndex)]));
        index.Rebuild(level, [broken]);
        Assert.IsFalse(index.IsComplete);
        Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportData, component.UIndex)]));
    }

    [TestMethod]
    public void FreighterPackages_IndexSkipsUnrelatedMeshUpdatesAndTracksActualLightSources()
    {
        string directory = Environment.GetEnvironmentVariable("LEX_VISIBLE_SETS_TEST_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) Assert.Inconclusive("Set LEX_VISIBLE_SETS_TEST_DIRECTORY for real-package checks.");
        foreach (string name in new[] { "BioD_Freighter.pcc", "BioD_Freighter_CargoBay.pcc" })
        {
            using var package = MEPackageHandler.OpenMEPackage(Path.Combine(directory, name), forceLoadFromDisk: true);
            using var cache = new PackageCache();
            var level = package.Exports.First(e => e.ClassName == "Level").GetBinaryData<Level>();
            var sources = new HashSet<ExportEntry>();
            var lights = SceneLight.LoadLevelLights(level, cache, source => sources.Add(source));
            var index = new LevelLightUpdateDependencies();
            var watch = Stopwatch.StartNew();
            index.Rebuild(level, sources);
            watch.Stop();
            Assert.IsTrue(index.IsComplete, $"{name}: expected a usable dependency index.");
            foreach (var light in lights)
                Assert.IsTrue(index.NeedsLightRefresh([new(PackageChange.ExportData, light.Export.UIndex)]));
            var meshes = level.Actors.Where(package.IsUExport).Select(package.GetUExport).Where(e => e.ClassName == "StaticMeshActor").ToList();
            int skipped = meshes.Count(e => !index.NeedsLightRefresh([new(PackageChange.ExportData, e.UIndex)]));
            Assert.IsGreaterThan(0, skipped);
            TestContext.WriteLine($"{name}: {lights.Count} lights; dependency index {index.ExportCount} exports in {watch.Elapsed.TotalMilliseconds:F3} ms; avoids light rebuild for {skipped}/{meshes.Count} static mesh updates.");
            Assert.IsFalse(package.IsModified);
        }
    }

    private static ExportEntry CreateExport(IMEPackage package, string name, string className, PropertyCollection properties = null)
    {
        var import = new ImportEntry(package) { ObjectName = className, ClassName = "Class", PackageFile = "Engine" };
        package.AddImport(import);
        var export = new ExportEntry(package, null, name, properties: properties ?? []) { Class = import };
        package.AddExport(export);
        return export;
    }
}
