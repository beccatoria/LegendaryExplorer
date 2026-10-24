using System;
using System.Collections.Generic;
using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorerCore.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewActorBindingResolverTests
{
    [TestMethod]
    public void Resolve_WhenSingleTagMatch_BindsActor()
    {
        InterpPreviewSceneSnapshot scene = CreateSceneSnapshot([
            CreateActor("D:/Levels/MapA.pcc", 40, "hench_mystic", "MysticA", isPlayerRole: false)
        ]);

        var request = new InterpPreviewActorBindingRequest(
            scene,
            [CreateTarget(trackUIndex: 100, role: InterpPreviewTrackTargetRole.DirectTag, targetKey: "hench_mystic")]);

        var resolver = new InterpPreviewActorBindingResolver();
        InterpPreviewActorBindingResult result = resolver.Resolve(request);

        Assert.AreEqual(InterpPreviewActorBindingOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Bindings.Count);
        Assert.AreEqual(InterpPreviewActorBindingStatus.Bound, result.Bindings[0].Status);
        Assert.IsNotNull(result.Bindings[0].SelectedActor);
        Assert.AreEqual(40, result.Bindings[0].SelectedActor.ActorId.ActorExportUIndex);
    }

    [TestMethod]
    public void Resolve_WhenNoTagMatch_ReturnsMissingAndPartial()
    {
        InterpPreviewSceneSnapshot scene = CreateSceneSnapshot([
            CreateActor("D:/Levels/MapA.pcc", 41, "hench_soldier", "Soldier", isPlayerRole: false)
        ]);

        var request = new InterpPreviewActorBindingRequest(
            scene,
            [CreateTarget(trackUIndex: 101, role: InterpPreviewTrackTargetRole.GroupVariable, targetKey: "hench_mystic")]);

        var resolver = new InterpPreviewActorBindingResolver();
        InterpPreviewActorBindingResult result = resolver.Resolve(request);

        Assert.AreEqual(InterpPreviewActorBindingOutcome.Partial, result.Outcome);
        Assert.AreEqual(1, result.Bindings.Count);
        Assert.AreEqual(InterpPreviewActorBindingStatus.Missing, result.Bindings[0].Status);
        Assert.IsTrue(HasDiagnosticCode(result, "ACTOR_TAG_MISSING"));
    }

    [TestMethod]
    public void Resolve_WhenMultipleTagMatches_ReturnsAmbiguousAndStableOrdering()
    {
        InterpPreviewSceneSnapshot scene = CreateSceneSnapshot([
            CreateActor("D:/Levels/B.pcc", 55, "owner", "OwnerB", isPlayerRole: false),
            CreateActor("D:/Levels/A.pcc", 12, "owner", "OwnerA", isPlayerRole: false),
            CreateActor("D:/Levels/A.pcc", 10, "owner", "OwnerA2", isPlayerRole: false)
        ]);

        var request = new InterpPreviewActorBindingRequest(
            scene,
            [CreateTarget(trackUIndex: 102, role: InterpPreviewTrackTargetRole.DirectTag, targetKey: "OWNER")]);

        var resolver = new InterpPreviewActorBindingResolver();
        InterpPreviewActorBindingResult result = resolver.Resolve(request);

        Assert.AreEqual(InterpPreviewActorBindingOutcome.Partial, result.Outcome);
        Assert.AreEqual(1, result.Bindings.Count);
        Assert.AreEqual(InterpPreviewActorBindingStatus.Ambiguous, result.Bindings[0].Status);
        Assert.AreEqual(3, result.Bindings[0].Candidates.Count);

        Assert.AreEqual("D:\\Levels\\A.pcc", result.Bindings[0].Candidates[0].ActorId.ResourceKey.SourcePath);
        Assert.AreEqual(10, result.Bindings[0].Candidates[0].ActorId.ActorExportUIndex);
        Assert.AreEqual("D:\\Levels\\A.pcc", result.Bindings[0].Candidates[1].ActorId.ResourceKey.SourcePath);
        Assert.AreEqual(12, result.Bindings[0].Candidates[1].ActorId.ActorExportUIndex);
        Assert.AreEqual("D:\\Levels\\B.pcc", result.Bindings[0].Candidates[2].ActorId.ResourceKey.SourcePath);
        Assert.AreEqual(55, result.Bindings[0].Candidates[2].ActorId.ActorExportUIndex);
        Assert.IsTrue(HasDiagnosticCode(result, "ACTOR_TAG_AMBIGUOUS"));
    }

    [TestMethod]
    public void Resolve_DoesNotAliasExpandTagLookup()
    {
        InterpPreviewSceneSnapshot scene = CreateSceneSnapshot([
            CreateActor("D:/Levels/MapA.pcc", 77, "shepard", "PlayerAlias", isPlayerRole: true)
        ]);

        var request = new InterpPreviewActorBindingRequest(
            scene,
            [CreateTarget(trackUIndex: 103, role: InterpPreviewTrackTargetRole.DirectTag, targetKey: "player")]);

        var resolver = new InterpPreviewActorBindingResolver();
        InterpPreviewActorBindingResult result = resolver.Resolve(request);

        Assert.AreEqual(InterpPreviewActorBindingOutcome.Partial, result.Outcome);
        Assert.AreEqual(InterpPreviewActorBindingStatus.Missing, result.Bindings[0].Status);
        Assert.IsTrue(HasDiagnosticCode(result, "ACTOR_TAG_MISSING"));
    }

    [TestMethod]
    public void Resolve_WhenRequestNull_ReturnsUnresolved()
    {
        var resolver = new InterpPreviewActorBindingResolver();

        InterpPreviewActorBindingResult result = resolver.Resolve(request: null);

        Assert.AreEqual(InterpPreviewActorBindingOutcome.Unresolved, result.Outcome);
        Assert.IsTrue(HasDiagnosticCode(result, "ACTOR_BINDING_REQUEST_NULL"));
    }

    private static InterpPreviewTrackTargetDescriptor CreateTarget(int trackUIndex, InterpPreviewTrackTargetRole role, string targetKey)
    {
        InterpPreviewSourceObjectId actionId = new(MEGame.LE3, "D:/Test/Conversation.pcc", 500);
        InterpPreviewSourceObjectId interpDataId = new(MEGame.LE3, "D:/Test/Conversation.pcc", 600);
        InterpPreviewSourceObjectId trackId = new(MEGame.LE3, "D:/Test/Conversation.pcc", trackUIndex);
        return new InterpPreviewTrackTargetDescriptor(actionId, interpDataId, trackId, "BioEvtSysTrackGesture", "group", role, targetKey, 0, 0);
    }

    private static InterpPreviewSceneActorDescriptor CreateActor(string sourcePath, int actorUIndex, string tag, string name, bool isPlayerRole)
    {
        InterpPreviewResourceKey key = InterpPreviewResourceKey.ForLevel(sourcePath, MEGame.LE3);
        return new InterpPreviewSceneActorDescriptor(new InterpPreviewActorId(key, actorUIndex), tag, name, isPlayerRole);
    }

    private static InterpPreviewSceneSnapshot CreateSceneSnapshot(IReadOnlyList<InterpPreviewSceneActorDescriptor> actors)
    {
        var stamp = new InterpPreviewSceneStamp(Guid.NewGuid(), 1);
        var resources = new List<InterpPreviewSceneResourceDescriptor>
        {
            new(InterpPreviewResourceKey.ForLevel("D:/Levels/MapA.pcc", MEGame.LE3), "MapA")
        };

        return new InterpPreviewSceneSnapshot(stamp, resources, actors, InterpPreviewPlayerVariant.None);
    }

    private static bool HasDiagnosticCode(InterpPreviewActorBindingResult result, string code)
    {
        foreach (InterpPreviewDiagnostic diagnostic in result.Diagnostics)
        {
            if (string.Equals(diagnostic.Code, code, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
