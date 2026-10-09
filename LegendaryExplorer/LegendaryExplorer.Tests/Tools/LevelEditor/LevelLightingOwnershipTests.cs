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
public class LevelLightingOwnershipTests
{
    [ClassInitialize]
    public static void Initialize(TestContext context)
    {
        LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);
    }

    [TestMethod]
    public void PreviewRegistry_SynchronizesPackagesWithoutDuplicateOrRetiredLights()
    {
        using var first = MEPackageHandler.CreateMemoryEmptyPackage("PreviewFirst.pcc", MEGame.LE2);
        using var second = MEPackageHandler.CreateMemoryEmptyPackage("PreviewSecond.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext();
        int loads = 0;
        var registry = new PreviewLevelLightRegistry(context, package =>
        {
            loads++;
            return [CreateLight(package, context.PackageCache)];
        });
        registry.Synchronize([first, first, second]);
        Assert.HasCount(2, context.GetLights(out _));
        registry.Synchronize([first, second]);
        Assert.AreEqual(2, loads);
        registry.Synchronize([second]);
        Assert.HasCount(1, context.GetLights(out _));
        Assert.AreSame(second, context.GetLights(out _)[0].Export.FileRef);
        registry.Clear();
        Assert.HasCount(0, context.GetLights(out _));
        registry.Synchronize([first]);
        Assert.AreEqual(3, loads);
        registry.Clear();
    }

    [TestMethod]
    public void RemovingFileLights_RetainsOtherFilesAndChangesVersion()
    {
        using var firstPackage = MEPackageHandler.CreateMemoryEmptyPackage("FirstLevel.pcc", MEGame.LE2);
        using var secondPackage = MEPackageHandler.CreateMemoryEmptyPackage("SecondLevel.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext();
        var firstLight = CreateLight(firstPackage, context.PackageCache);
        var secondLight = CreateLight(secondPackage, context.PackageCache);
        Assert.IsNotNull(firstLight);
        Assert.IsNotNull(secondLight);
        context.AddLights([firstLight]);
        context.AddLights([secondLight]);
        Assert.HasCount(2, context.GetLights(out int beforeVersion));

        context.RemoveLights([firstLight]);

        var remaining = context.GetLights(out int afterVersion);
        Assert.HasCount(1, remaining);
        Assert.AreSame(secondLight, remaining[0]);
        Assert.IsTrue(afterVersion > beforeVersion);
    }

    [TestMethod]
    public void UnloadLevel_ClearsLightReferencesAndChangesVersion()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("FirstLevel.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext();
        var light = CreateLight(package, context.PackageCache);
        Assert.IsNotNull(light);
        context.AddLights([light]);
        context.GetLights(out int beforeVersion);

        context.UnloadLevel();

        Assert.HasCount(0, context.GetLights(out int afterVersion));
        Assert.IsTrue(afterVersion > beforeVersion);
    }

    [TestMethod]
    public void MarkerToggle_RestoresAllContributionsWithoutLosingIndividualHides()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("LightPolicy.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext { ShowLights = true, UseVisibleSetOnly = true };
        var visible = CreateLight(package, context.PackageCache);
        var hidden = CreateLight(package, context.PackageCache);
        string visibleKey = EditorVisibilityState.GetActorKey(package.FilePath, visible.VisibilityExport.UIndex);
        context.Visibility.VisibleActorKeys.Add(visibleKey);
        context.AddLights([visible, hidden]);
        Assert.HasCount(1, context.GetLights(out int filteredVersion));

        context.ShowLights = false;
        context.RefreshLightVisibility();
        Assert.HasCount(2, context.GetLights(out int allVersion));
        Assert.IsTrue(allVersion > filteredVersion);
        Assert.HasCount(1, context.Visibility.VisibleActorKeys);
        Assert.IsTrue(context.Visibility.VisibleActorKeys.Contains(visibleKey));

        context.ShowLights = true;
        context.RefreshLightVisibility();
        var lights = context.GetLights(out int restoredVersion);
        Assert.HasCount(1, lights);
        Assert.AreSame(visible, lights[0]);
        Assert.IsTrue(restoredVersion > allVersion);
    }

    [TestMethod]
    public void LightContribution_IgnoresMarkerDistanceAndRefreshesOnlyWhenMembershipChanges()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("LightPolicy.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext
        {
            ShowLights = true, UseVisibleSetOnly = true, LightRenderDistance = 0, VisibleSetDistance = 0
        };
        context.Camera.Position = new Vector3(10000, 10000, 10000);
        var light = CreateLight(package, context.PackageCache);
        string key = EditorVisibilityState.GetActorKey(package.FilePath, light.VisibilityExport.UIndex);
        context.Visibility.VisibleActorKeys.Add(key);
        context.AddLights([light]);
        Assert.HasCount(1, context.GetLights(out int originalVersion));

        context.RefreshLightVisibility();
        context.GetLights(out int unchangedVersion);
        Assert.AreEqual(originalVersion, unchangedVersion);

        context.Visibility.VisibleActorKeys.Remove(key);
        context.RefreshLightVisibility();
        Assert.HasCount(0, context.GetLights(out int hiddenVersion));
        Assert.IsTrue(hiddenVersion > originalVersion);

        context.UseVisibleSetOnly = false;
        context.RefreshLightVisibility();
        Assert.HasCount(1, context.GetLights(out _));
    }

    [TestMethod]
    public void RemovingHiddenLights_DoesNotResurrectThemWhenMarkersAreDisabled()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("LightPolicy.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext { ShowLights = true, UseVisibleSetOnly = true };
        var light = CreateLight(package, context.PackageCache);
        context.AddLights([light]);
        Assert.HasCount(0, context.GetLights(out _));

        context.RemoveLights([light]);
        context.ShowLights = false;
        context.RefreshLightVisibility();
        Assert.HasCount(0, context.GetLights(out _));
    }

    [TestMethod]
    public void LightVisibilityIdentity_UsesExplicitOwnerOrDefaultsToCollectionComponent()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("LightPolicy.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext { ShowLights = true, UseVisibleSetOnly = true };
        var collectionLight = CreateLight(package, context.PackageCache);
        var actorExport = new ExportEntry(package, 0, "StandaloneActor", prePropBinary: new byte[4], properties: []);
        package.AddExport(actorExport);
        var standaloneLight = SceneLight.Create(collectionLight.Export, "PointLight", context.PackageCache,
            Matrix4x4.Identity, actorExport);
        Assert.AreSame(collectionLight.Export, collectionLight.VisibilityExport);
        Assert.AreSame(actorExport, standaloneLight.VisibilityExport);
        context.Visibility.VisibleActorKeys.Add(EditorVisibilityState.GetActorKey(package.FilePath, actorExport.UIndex));
        context.AddLights([collectionLight, standaloneLight]);

        var lights = context.GetLights(out _);
        Assert.HasCount(1, lights);
        Assert.AreSame(standaloneLight, lights[0]);
    }

    [TestMethod]
    public void ReplaceLight_PreservesOrderAndHiddenMembershipAcrossMarkerToggles()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("Replacement.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext { ShowLights = true, UseVisibleSetOnly = true };
        var first = CreateLight(package, context.PackageCache);
        var hidden = CreateLight(package, context.PackageCache);
        var last = CreateLight(package, context.PackageCache);
        context.Visibility.VisibleActorKeys.Add(EditorVisibilityState.GetActorKey(package.FilePath, first.Export.UIndex));
        context.Visibility.VisibleActorKeys.Add(EditorVisibilityState.GetActorKey(package.FilePath, last.Export.UIndex));
        context.AddLights([first, hidden, last]);
        context.GetLights(out int beforeVersion);
        var replacement = SceneLight.Create(hidden.Export, "PointLight", context.PackageCache, Matrix4x4.Identity);

        context.ReplaceLight(hidden, replacement);

        CollectionAssert.AreEqual(new[] { first, last }, context.GetLights(out int replacedVersion));
        Assert.IsTrue(replacedVersion > beforeVersion);
        context.ShowLights = false;
        context.RefreshLightVisibility();
        CollectionAssert.AreEqual(new[] { first, replacement, last }, context.GetLights(out _));
        context.ShowLights = true;
        context.RefreshLightVisibility();
        CollectionAssert.AreEqual(new[] { first, last }, context.GetLights(out _));
        context.RemoveLights([replacement]);
        context.ShowLights = false;
        context.RefreshLightVisibility();
        CollectionAssert.AreEqual(new[] { first, last }, context.GetLights(out _));
    }

    [TestMethod]
    public void ReplaceLight_SameOrUnregisteredInstanceDoesNotChangeVersion()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("Replacement.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext();
        var registered = CreateLight(package, context.PackageCache);
        var unregistered = CreateLight(package, context.PackageCache);
        context.AddLights([registered]);
        var original = context.GetLights(out int beforeVersion);

        context.ReplaceLight(registered, registered);
        context.ReplaceLight(unregistered, registered);

        Assert.AreSame(original, context.GetLights(out int afterVersion));
        Assert.AreEqual(beforeVersion, afterVersion);
    }

    [TestMethod]
    public void LightMarker_DistanceAndOwnerVisibilityDoNotOverrideContributionPolicy()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("MarkerPolicy.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext { ShowLights = true, UseVisibleSetOnly = true, LightRenderDistance = 100 };
        context.Camera.Position = new Vector3(0, 0, 1000);
        var light = CreateLight(package, context.PackageCache);
        using var owner = new MarkerActor(light.Export, false);
        using var marker = new MarkerActor(light.Export, true);
        typeof(ActorProxy).GetProperty(nameof(ActorProxy.VisibilityOwner)).SetValue(marker, owner);
        context.Visibility.VisibleActorKeys.Add(EditorVisibilityState.GetActorKey(package.FilePath, light.Export.UIndex));
        context.AddLights([light]);

        Assert.IsFalse(context.IsActorVisible(marker));
        CollectionAssert.AreEqual(new[] { light }, context.GetLights(out _));
        context.Camera.Position = Vector3.Zero;
        Assert.IsTrue(context.IsActorVisible(marker));
        context.Visibility.VisibleActorKeys.Clear();
        context.RefreshLightVisibility();
        Assert.IsFalse(context.IsActorVisible(owner));
        Assert.IsFalse(context.IsActorVisible(marker));
        Assert.HasCount(0, context.GetLights(out _));
        context.ShowLights = false;
        context.RefreshLightVisibility();
        Assert.IsFalse(context.IsActorVisible(marker));
        CollectionAssert.AreEqual(new[] { light }, context.GetLights(out _));
    }

    [TestMethod]
    public void PreviewRegistry_ClearPreservesUnrelatedLightsAndAllowsPackageReuse()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("Registry.pcc", MEGame.LE2);
        var context = new LevelEditorRenderContext();
        var unrelated = CreateLight(package, context.PackageCache);
        context.AddLights([unrelated]);
        int loads = 0;
        var registry = new PreviewLevelLightRegistry(context, active =>
        {
            loads++;
            return [CreateLight(active, context.PackageCache)];
        });
        registry.Synchronize([package]);
        Assert.HasCount(2, context.GetLights(out _));
        registry.Clear();
        CollectionAssert.AreEqual(new[] { unrelated }, context.GetLights(out int clearedVersion));
        registry.Clear();
        context.GetLights(out int unchangedVersion);
        Assert.AreEqual(clearedVersion, unchangedVersion);
        registry.Synchronize([package]);
        Assert.AreEqual(2, loads);
        Assert.HasCount(2, context.GetLights(out _));
        registry.Clear();
        CollectionAssert.AreEqual(new[] { unrelated }, context.GetLights(out _));
    }

    private sealed class MarkerActor : ActorProxy
    {
        public MarkerActor(ExportEntry export, bool isLight) : base(export)
        {
            IsLight = isLight;
        }
    }

    private static SceneLight CreateLight(IMEPackage package, PackageCache cache)
    {
        var classImport = new ImportEntry(package)
        {
            ObjectName = "PointLightComponent",
            ClassName = "Class",
            PackageFile = "Engine"
        };
        package.AddImport(classImport);
        var export = new ExportEntry(package, 0, "TestLight", prePropBinary: new byte[8], properties: []) { Class = classImport };
        package.AddExport(export);
        return SceneLight.Create(export, "PointLight", cache, Matrix4x4.Identity);
    }
}
