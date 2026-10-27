using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal.BinaryConverters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class VisibleSetsPackageTests
{
    public TestContext TestContext { get; set; }

    [ClassInitialize]
    public static void Initialize(TestContext _) => LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);

    // Optional local regression: no copyrighted package fixtures are committed or changed.
    // Exercises real export identities/positions and the renderer's visibility predicate without a GPU.
    [TestMethod]
    public void FreighterPackages_ClassSelectionAndReloadKeepIndependentMeshMembership()
    {
        string directory = Environment.GetEnvironmentVariable("LEX_VISIBLE_SETS_TEST_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
            Assert.Inconclusive("Set LEX_VISIBLE_SETS_TEST_DIRECTORY to the Freighter package directory to run this local regression.");

        using var freighter = MEPackageHandler.OpenMEPackage(Path.Combine(directory, "BioD_Freighter.pcc"), forceLoadFromDisk: true);
        using var cargo = MEPackageHandler.OpenMEPackage(Path.Combine(directory, "BioD_Freighter_CargoBay.pcc"), forceLoadFromDisk: true);
        var actors = ReadActors(freighter).Concat(ReadActors(cargo)).ToList();
        try
        {
            var context = new LevelEditorRenderContext { UseVisibleSetOnly = true, VisibleSetDistance = 5000 };
            context.Camera.IsOrthographic = true;
            context.Camera.Position = new Vector3(-50.293232f, 6543.474f, 40000);
            var identities = actors.Select(a => (a.VisibilityKey, "StaticMeshActor")).ToList();
            var classes = new HashSet<string> { "StaticMeshActor" };
            var state = context.Visibility;

            // Recreate the observed saved failure: only CargoBay's static meshes are members.
            foreach (var actor in actors.Where(a => ReferenceEquals(a.Export.FileRef, cargo)))
                state.VisibleActorKeys.Add(actor.VisibilityKey);
            Assert.IsFalse(actors.Where(a => ReferenceEquals(a.Export.FileRef, freighter)).Any(context.IsActorVisible));
            state.ApplyClassSelection(identities, classes, classes, false, true);
            Assert.HasCount(actors.Count, state.VisibleActorKeys);

            foreach (var package in new[] { freighter, cargo })
            {
                var fileActors = actors.Where(a => ReferenceEquals(a.Export.FileRef, package)).ToList();
                Assert.IsNotEmpty(fileActors);
                foreach (var actor in fileActors)
                    Assert.AreEqual(context.GetVisibleSetDistanceSquared(actor) <= 5000f * 5000f, context.IsActorVisible(actor), actor.VisibilityKey);
                TestContext.WriteLine($"{Path.GetFileName(package.FilePath)}: {fileActors.Count} static meshes in set; {fileActors.Count(context.IsActorVisible)} within the saved top-down distance limit.");
            }

            var hidden = actors.First(a => ReferenceEquals(a.Export.FileRef, freighter) && context.IsActorVisible(a));
            var retained = actors.First(a => ReferenceEquals(a.Export.FileRef, cargo) && context.IsActorVisible(a));
            for (int i = 0; i < 5; i++)
            {
                state.SetActorVisibility(hidden.VisibilityKey, false);
                state.ApplyClassSelection(identities, classes, new HashSet<string>(), false);
                Assert.IsFalse(context.IsActorVisible(hidden));
                Assert.IsTrue(context.IsActorVisible(retained));
                context.UseVisibleSetOnly = false;
                Assert.IsTrue(context.IsActorVisible(hidden));
                context.UseVisibleSetOnly = true;
                Assert.IsFalse(context.IsActorVisible(hidden));
                state.ApplyClassSelection(identities, new HashSet<string>(), classes, false, false);
                Assert.IsEmpty(state.VisibleActorKeys);
                state.ApplyClassSelection(identities, classes, classes, false, true);
                Assert.IsTrue(context.IsActorVisible(hidden));
            }

            state.SetActorVisibility(hidden.VisibilityKey, false);
            using var reloaded = MEPackageHandler.OpenMEPackage(freighter.FilePath, forceLoadFromDisk: true);
            var replacements = ReadActors(reloaded).ToList();
            try
            {
                foreach (var actor in replacements) state.RegisterActor(actor.VisibilityKey, "StaticMeshActor");
                Assert.IsFalse(context.IsActorVisible(replacements.Single(a => a.VisibilityKey == hidden.VisibilityKey)));
                Assert.IsTrue(context.IsActorVisible(retained));
            }
            finally { foreach (var actor in replacements) actor.Dispose(); }
            Assert.IsFalse(freighter.IsModified);
            Assert.IsFalse(cargo.IsModified);
            Assert.IsFalse(reloaded.IsModified);
        }
        finally { foreach (var actor in actors) actor.Dispose(); }
    }

    private static IEnumerable<ActorProxy> ReadActors(IMEPackage package)
    {
        var level = package.Exports.First(e => e.ClassName == "Level").GetBinaryData<Level>();
        return level.Actors.Where(package.IsUExport).Select(package.GetUExport)
            .Where(e => e.ClassName == "StaticMeshActor").Select(e => new PositionActor(e));
    }

    private sealed class PositionActor(ExportEntry export) : ActorProxy(null, export);
}
