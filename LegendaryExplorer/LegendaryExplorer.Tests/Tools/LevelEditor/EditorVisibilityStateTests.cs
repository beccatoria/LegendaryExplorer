using LegendaryExplorer.Tools.LevelEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class EditorVisibilityStateTests
{
    [TestMethod]
    public void NewPackages_InheritClassesWithoutChangingExistingActorOverrides()
    {
        var state = new EditorVisibilityState();
        state.RegisterActor("Cargo.pcc|123", "StaticMeshActor");
        state.SetActorVisibility("Cargo.pcc|123", false);
        state.RegisterActor("Freighter.pcc|123", "StaticMeshActor");
        Assert.IsFalse(state.VisibleActorKeys.Contains("Cargo.pcc|123"));
        Assert.IsTrue(state.VisibleActorKeys.Contains("Freighter.pcc|123"));

        state.SetClassVisibility("StaticMeshActor", false);
        state.RegisterActor("Later.pcc|123", "staticmeshactor");
        Assert.IsFalse(state.VisibleActorKeys.Contains("Later.pcc|123"));
    }

    [TestMethod]
    public void ClassSelection_DoesNotTurnMissingMembershipIntoAnActorOverride()
    {
        var state = new EditorVisibilityState();
        state.RegisterActor("Cargo.pcc|123", "StaticMeshActor");
        // Models a package arriving after the original flattened selection was built.
        state.ApplyClassSelection([("Cargo.pcc|123", "StaticMeshActor"), ("Freighter.pcc|123", "StaticMeshActor")],
            new HashSet<string> { "StaticMeshActor" }, new HashSet<string>(), false);
        Assert.HasCount(2, state.VisibleActorKeys);
        Assert.IsEmpty(state.ActorVisibilityOverrides);
    }

    [TestMethod]
    public void ClassSelection_UnchangedClassesRetainBothKindsOfActorOverride()
    {
        var state = new EditorVisibilityState();
        state.SetActorVisibility("Cargo.pcc|123", false);
        state.SetActorVisibility("Cargo.pcc|124", true);
        state.ApplyClassSelection([("Cargo.pcc|123", "StaticMeshActor"), ("Cargo.pcc|124", "Emitter")],
            new HashSet<string> { "StaticMeshActor" }, new HashSet<string>(), false);
        Assert.IsFalse(state.VisibleActorKeys.Contains("Cargo.pcc|123"));
        Assert.IsTrue(state.VisibleActorKeys.Contains("Cargo.pcc|124"));
        Assert.IsFalse(state.IsClassVisible("Emitter"));
    }

    [TestMethod]
    public void DeliberateClassActions_ResetOnlyThatClassAndRemainRepeatable()
    {
        var state = new EditorVisibilityState();
        var actors = new List<(string, string)> { ("Cargo.pcc|1", "StaticMeshActor"), ("Freighter.pcc|1", "StaticMeshActor"), ("Cargo.pcc|2", "Emitter") };
        var meshClass = new HashSet<string> { "StaticMeshActor" };
        state.SetActorVisibility("Cargo.pcc|2", true);
        for (int i = 0; i < 5; i++)
        {
            state.SetActorVisibility("Freighter.pcc|1", false);
            state.ApplyClassSelection(actors, meshClass, meshClass, false);
            Assert.HasCount(3, state.VisibleActorKeys);
            state.ApplyClassSelection(actors, new HashSet<string>(), meshClass, false);
            CollectionAssert.AreEquivalent(new[] { "Cargo.pcc|2" }, new List<string>(state.VisibleActorKeys));
        }
    }

    [TestMethod]
    public void ClearAndAddAll_ResetLegacyHidesAndSetNewClassDefaults()
    {
        var state = new EditorVisibilityState();
        var actors = new List<(string, string)> { ("Cargo.pcc|1", "StaticMeshActor"), ("Freighter.pcc|1", "StaticMeshActor") };
        var classes = new HashSet<string> { "StaticMeshActor" };
        state.SetActorVisibility("Cargo.pcc|1", true);
        state.SetActorVisibility("Freighter.pcc|1", false);
        state.ApplyClassSelection(actors, new HashSet<string>(), classes, false, false);
        Assert.IsEmpty(state.VisibleActorKeys);
        state.RegisterActor("New.pcc|1", "AnotherClass");
        Assert.IsEmpty(state.VisibleActorKeys);
        state.ApplyClassSelection(actors, classes, classes, false, true);
        state.RegisterActor("New.pcc|1", "AnotherClass");
        Assert.HasCount(3, state.VisibleActorKeys);
        Assert.IsEmpty(state.ActorVisibilityOverrides);
    }

    [TestMethod]
    public void IsolatedActorSelection_RemainsIsolatedWhenMorePackagesArrive()
    {
        var state = new EditorVisibilityState();
        state.Reset(false);
        state.SetActorVisibility("Cargo.pcc|1", true);
        state.RegisterActor("Freighter.pcc|1", "StaticMeshActor");
        state.RegisterActor("Cargo.pcc|2", "StaticMeshActor");
        CollectionAssert.AreEquivalent(new[] { "Cargo.pcc|1" }, new List<string>(state.VisibleActorKeys));
    }

    [TestMethod]
    public void Reload_PreservesActorChoicesWhileNewActorsInheritClassAndCloseForgetsOverrides()
    {
        var state = new EditorVisibilityState();
        state.SetActorVisibility("Cargo.pcc|1", false);
        state.RegisterActor("Cargo.pcc|1", "StaticMeshActor");
        state.RegisterActor("Cargo.pcc|2", "StaticMeshActor");
        Assert.IsFalse(state.VisibleActorKeys.Contains("Cargo.pcc|1"));
        Assert.IsTrue(state.VisibleActorKeys.Contains("Cargo.pcc|2"));
        state.ForgetActor("CARGO.PCC|1");
        state.RegisterActor("Cargo.pcc|1", "StaticMeshActor");
        Assert.IsTrue(state.VisibleActorKeys.Contains("Cargo.pcc|1"));
        state.HasUserEditedVisibleSets = true;
        state.Reset();
        Assert.IsFalse(state.HasUserEditedVisibleSets);
        state.RegisterActor("Other.pcc|1", "StaticMeshActor");
        Assert.IsTrue(state.VisibleActorKeys.Contains("Other.pcc|1"));
    }

    [TestMethod]
    public void GetActorKey_PreservesExistingSavedKeyFormat()
    {
        Assert.AreEqual(@"D:\Levels\Test.pcc|123", EditorVisibilityState.GetActorKey(@"D:\Levels\Test.pcc", 123));
    }

    [TestMethod]
    public void VisibleActorKeys_MatchPackagePathCaseInsensitively()
    {
        var state = new EditorVisibilityState();
        state.VisibleActorKeys.Add(EditorVisibilityState.GetActorKey(@"D:\Levels\Test.pcc", 123));

        Assert.IsTrue(state.IsInVisibleSet(EditorVisibilityState.GetActorKey(@"d:\levels\test.PCC", 123), 0, 100));
    }

    [TestMethod]
    public void VisibleActorKeys_KeepDifferentPackagesAndExportsDistinct()
    {
        var state = new EditorVisibilityState();
        state.VisibleActorKeys.Add(EditorVisibilityState.GetActorKey("First.pcc", 123));

        Assert.IsFalse(state.IsInVisibleSet(EditorVisibilityState.GetActorKey("Second.pcc", 123), 0, 100));
        Assert.IsFalse(state.IsInVisibleSet(EditorVisibilityState.GetActorKey("First.pcc", 124), 0, 100));
    }

    [TestMethod]
    public void IsInVisibleSet_RequiresMembershipEvenWhenNearby()
    {
        var state = new EditorVisibilityState();

        Assert.IsFalse(state.IsInVisibleSet("Test.pcc|123", 0, 100));
    }

    [TestMethod]
    public void IsInVisibleSet_IncludesDistanceBoundaryButExcludesBeyondIt()
    {
        var state = new EditorVisibilityState();
        state.VisibleActorKeys.Add("Test.pcc|123");

        Assert.IsTrue(state.IsInVisibleSet("Test.pcc|123", 100, 100));
        Assert.IsFalse(state.IsInVisibleSet("Test.pcc|123", 101, 100));
    }

    [TestMethod]
    public void IsInVisibleSet_HandlesZeroDistanceLimit()
    {
        var state = new EditorVisibilityState();
        state.VisibleActorKeys.Add("Test.pcc|123");

        Assert.IsTrue(state.IsInVisibleSet("Test.pcc|123", 0, 0));
        Assert.IsFalse(state.IsInVisibleSet("Test.pcc|123", 1, 0));
    }

    [TestMethod]
    public void HiddenActorClasses_AreIndependentOfActorMembership()
    {
        var state = new EditorVisibilityState();
        state.HiddenActorClasses.Add("CameraActor");
        state.VisibleActorKeys.Add("Test.pcc|123");

        Assert.IsTrue(state.HiddenActorClasses.Contains("cameraactor"));
        Assert.IsTrue(state.IsInVisibleSet("Test.pcc|123", 0, 100));
    }

    [TestMethod]
    public void RecentViewState_RoundTripRetainsExistingVisibilityData()
    {
        const string saved = "{\"ObjectRenderMode\":3,\"UseVisibleSetOnly\":true,\"HasUserEditedVisibleSets\":true,\"VisibleSetDistance\":5000,\"VisibleActorKeys\":[\"Test.pcc|123\"],\"HiddenActorClasses\":[\"CameraActor\"]}";
        RecentViewState view = JsonConvert.DeserializeObject<RecentViewState>(saved);
        var state = new EditorVisibilityState
        {
            HasUserEditedVisibleSets = view.HasUserEditedVisibleSets
        };
        state.VisibleActorKeys.UnionWith(view.VisibleActorKeys);
        state.HiddenActorClasses.UnionWith(view.HiddenActorClasses);

        RecentViewState roundTrip = JsonConvert.DeserializeObject<RecentViewState>(JsonConvert.SerializeObject(view));

        Assert.AreEqual(ObjectRenderMode.VisibleSetOnly, roundTrip.ObjectRenderMode);
        Assert.IsTrue(roundTrip.UseVisibleSetOnly);
        Assert.IsTrue(state.HasUserEditedVisibleSets);
        Assert.AreEqual(5000, roundTrip.VisibleSetDistance);
        Assert.IsTrue(state.IsInVisibleSet(roundTrip.VisibleActorKeys[0], 0, 100));
        Assert.IsTrue(state.HiddenActorClasses.Contains(roundTrip.HiddenActorClasses[0]));
    }
}
