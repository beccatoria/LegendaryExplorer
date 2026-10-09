using LegendaryExplorer.Tools.LevelEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class EditorVisibilityStateTests
{
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
