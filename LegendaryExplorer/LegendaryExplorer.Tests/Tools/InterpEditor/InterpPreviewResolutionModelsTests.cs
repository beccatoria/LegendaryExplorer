using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorerCore.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewResolutionModelsTests
{
    [TestMethod]
    public void SourceObjectId_UsesCaseInsensitiveCanonicalPathEquality()
    {
        var first = new InterpPreviewSourceObjectId(MEGame.LE1, "C:\\Test\\Packages\\BioA.pcc", 42);
        var second = new InterpPreviewSourceObjectId(MEGame.LE1, "c:\\test\\packages\\BIOA.pcc", 42);

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    public void SceneStamp_RequiresSessionAndSupportsGenerationIdentity()
    {
        Guid session = Guid.NewGuid();
        var first = new InterpPreviewSceneStamp(session, 1);
        var second = new InterpPreviewSceneStamp(session, 2);

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void ActorId_ComposesResourceIdentityAndExportUIndex()
    {
        InterpPreviewResourceKey resourceKey = InterpPreviewResourceKey.ForPlayerContext(
            "C:\\Test\\BioP_Char.pcc",
            MEGame.LE2,
            InterpPreviewPlayerVariant.Female);

        var actorId = new InterpPreviewActorId(resourceKey, 1234);

        Assert.AreEqual(resourceKey, actorId.ResourceKey);
        Assert.AreEqual(1234, actorId.ActorExportUIndex);
        Assert.AreEqual(new InterpPreviewSourceObjectId(MEGame.LE2, "C:\\Test\\BioP_Char.pcc", 1234), actorId.SourceObjectId);
    }

    [TestMethod]
    public void SceneSnapshot_CopiesCollections_AndBuildsTagAndSourceIndexes()
    {
        Guid sessionId = Guid.NewGuid();
        var stamp = new InterpPreviewSceneStamp(sessionId, 5);

        var resources = new List<InterpPreviewSceneResourceDescriptor>
        {
            new(InterpPreviewResourceKey.ForLevel("C:\\Test\\LevelA.pcc", MEGame.LE3), "Level A")
        };

        InterpPreviewResourceKey playerResource = InterpPreviewResourceKey.ForPlayerContext("C:\\Test\\BioP_Char.pcc", MEGame.LE3, InterpPreviewPlayerVariant.Male);
        var actors = new List<InterpPreviewSceneActorDescriptor>
        {
            new(new InterpPreviewActorId(playerResource, 10), "player", "BioPawn_Player", isPlayerRole: true),
            new(new InterpPreviewActorId(InterpPreviewResourceKey.ForLevel("C:\\Test\\LevelA.pcc", MEGame.LE3), 20), "hench_mystic", "HenchMystic", isPlayerRole: false)
        };

        var snapshot = new InterpPreviewSceneSnapshot(stamp, resources, actors, InterpPreviewPlayerVariant.Male);

        resources.Add(new InterpPreviewSceneResourceDescriptor(InterpPreviewResourceKey.ForLevel("C:\\Test\\LevelB.pcc", MEGame.LE3), "Level B"));
        actors.Clear();

        Assert.AreEqual(stamp, snapshot.Stamp);
        Assert.AreEqual(1, snapshot.Resources.Count);
        Assert.AreEqual(2, snapshot.Actors.Count);
        Assert.AreEqual(InterpPreviewPlayerVariant.Male, snapshot.LoadedPlayerVariant);
        Assert.IsTrue(snapshot.HasPlayerRoleActors);

        Assert.IsTrue(snapshot.ActorsByTag.ContainsKey("player"));
        Assert.AreEqual(1, snapshot.ActorsByTag["player"].Count);

        var expectedSource = new InterpPreviewSourceObjectId(MEGame.LE3, "C:\\Test\\BioP_Char.pcc", 10);
        Assert.IsTrue(snapshot.ActorsBySourceId.ContainsKey(expectedSource));
        Assert.AreEqual(1, snapshot.ActorsBySourceId[expectedSource].Count);
    }

    [TestMethod]
    public void NodeId_RequiresExportIdAndCapturesEntryReplyIdentity()
    {
        var conversationId = new InterpPreviewSourceObjectId(MEGame.LE1, "C:\\Test\\Conversation.pcc", 200);

        var entry = new InterpPreviewNodeId(conversationId, isReply: false, nodeIndex: 4, nodeExportId: 3210);
        var reply = new InterpPreviewNodeId(conversationId, isReply: true, nodeIndex: 4, nodeExportId: 3210);

        Assert.AreNotEqual(entry, reply);
    }
}
