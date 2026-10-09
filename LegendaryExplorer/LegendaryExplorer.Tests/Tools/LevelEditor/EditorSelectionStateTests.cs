using LegendaryExplorer.Tools.LevelEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.LevelEditor;

[TestClass]
public class EditorSelectionStateTests
{
    [TestMethod]
    public void SetSelection_KeepsEverySelectedActorHighlighted()
    {
        var state = new EditorSelectionState();
        state.SetSelection(["Test.pcc|1", "Test.pcc|2", "Test.pcc|3"], "Test.pcc|3");

        Assert.IsTrue(state.IsSelected("Test.pcc|1"));
        Assert.IsTrue(state.IsSelected("Test.pcc|2"));
        Assert.IsTrue(state.IsSelected("Test.pcc|3"));
        Assert.AreEqual("Test.pcc|3", state.PrimaryActorKey);
    }

    [TestMethod]
    public void SetSelection_RemovesToggledOffActorWithoutClearingOtherSelections()
    {
        var state = new EditorSelectionState();
        state.SetSelection(["Test.pcc|1", "Test.pcc|2", "Test.pcc|3"], "Test.pcc|3");
        state.SetSelection(["Test.pcc|1", "Test.pcc|3"], "Test.pcc|3");

        Assert.IsTrue(state.IsSelected("Test.pcc|1"));
        Assert.IsFalse(state.IsSelected("Test.pcc|2"));
        Assert.IsTrue(state.IsSelected("Test.pcc|3"));
    }

    [TestMethod]
    public void SetSelection_NormalSelectionReplacesPreviousSet()
    {
        var state = new EditorSelectionState();
        state.SetSelection(["Test.pcc|1", "Test.pcc|2"], "Test.pcc|2");
        state.SetSelection(["Test.pcc|3"], "Test.pcc|3");

        Assert.IsFalse(state.IsSelected("Test.pcc|1"));
        Assert.IsFalse(state.IsSelected("Test.pcc|2"));
        Assert.IsTrue(state.IsSelected("Test.pcc|3"));
    }

    [TestMethod]
    public void SetSelection_PrimarySelectionIsHighlightedWithoutListSelection()
    {
        var state = new EditorSelectionState();
        state.SetSelection([], "Test.pcc|1");

        Assert.IsTrue(state.IsSelected("Test.pcc|1"));
    }

    [TestMethod]
    public void IsSelected_MatchesStableActorIdentityAfterProxyReplacement()
    {
        var state = new EditorSelectionState();
        state.SetSelection([EditorVisibilityState.GetActorKey(@"D:\Levels\Test.pcc", 1)], null);

        Assert.IsTrue(state.IsSelected(EditorVisibilityState.GetActorKey(@"d:\levels\test.PCC", 1)));
        Assert.IsFalse(state.IsSelected(EditorVisibilityState.GetActorKey(@"D:\Levels\Other.pcc", 1)));
    }

    [TestMethod]
    public void Clear_RemovesPrimaryAndMultiSelection()
    {
        var state = new EditorSelectionState();
        state.SetSelection(["Test.pcc|1", "Test.pcc|2"], "Test.pcc|2");
        state.Clear();

        Assert.IsNull(state.PrimaryActorKey);
        Assert.IsFalse(state.IsSelected("Test.pcc|1"));
        Assert.IsFalse(state.IsSelected("Test.pcc|2"));
        Assert.IsFalse(state.IsSelected(null));
    }

    [TestMethod]
    public void SetSelection_EmptySelectionClearsPreviousSet()
    {
        var state = new EditorSelectionState();
        state.SetSelection(["Test.pcc|1"], "Test.pcc|1");
        state.SetSelection([], null);

        Assert.IsNull(state.PrimaryActorKey);
        Assert.IsFalse(state.IsSelected("Test.pcc|1"));
    }
}
