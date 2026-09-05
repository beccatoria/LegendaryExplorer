using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewOwnershipTests
{
    [TestMethod]
    public void LoadedLevel_Dispose_IsExactOnce_ForAbandonedCandidate()
    {
        IMEPackage package = CreatePackageSubstituteOrInconclusive();
        ActorProxy actor = CreateDisposableTestActor();
        var loaded = new InterpPreviewLoadedLevel("C:\\Test\\LevelA.pcc", package, new List<ActorProxy> { actor });

        loaded.Dispose();
        loaded.Dispose();

        package.Received(1).Dispose();
        Assert.IsTrue(IsActorDisposed(actor));
    }

    [TestMethod]
    public void LoadedLevel_ToOwnedResource_TransfersOwnershipOnlyOnce_AndDisposalTransfersToOwnedResource()
    {
        IMEPackage package = CreatePackageSubstituteOrInconclusive();
        ActorProxy actor = CreateDisposableTestActor();
        var loaded = new InterpPreviewLoadedLevel("C:\\Test\\LevelA.pcc", package, new List<ActorProxy> { actor });

        InterpPreviewOwnedResource resource = loaded.ToOwnedResource();

        loaded.Dispose();
        loaded.Dispose();

        package.DidNotReceive().Dispose();
        Assert.IsFalse(loaded.IsDisposed);
        Assert.IsTrue(loaded.IsTransferred);

        Assert.Throws<InvalidOperationException>(() => loaded.ToOwnedResource());

        resource.Dispose();
        resource.Dispose();

        package.Received(1).Dispose();
        Assert.IsTrue(IsActorDisposed(actor));
    }

    [TestMethod]
    public void OwnedResource_DisposesActorsBeforePackage()
    {
        IMEPackage package = CreatePackageSubstituteOrInconclusive();
        ActorProxy actor = CreateDisposableTestActor();
        var resource = new InterpPreviewOwnedResource(
            InterpPreviewResourceKey.ForLevel("C:\\Test\\LevelA.pcc", MEGame.Unknown),
            "C:\\Test\\LevelA.pcc",
            package,
            new List<ActorProxy> { actor });

        package
            .When(p => p.Dispose())
            .Do(_ => Assert.IsTrue(IsActorDisposed(actor), "Actor should be disposed before package disposal."));

        resource.Dispose();
        resource.Dispose();

        package.Received(1).Dispose();
    }

    [TestMethod]
    public void Session_ProjectsOnlyLevelPaths_WhenPlayerContextIsCommitted()
    {
        using var session = new InterpPreviewSession();

        string levelPath = Path.GetFullPath("C:\\Test\\LevelA.pcc");
        string playerPath = Path.GetFullPath("C:\\Test\\BioP_Char.pcc");

        session.AddLevel(new InterpPreviewLoadedLevel(levelPath, package: null, actors: new List<ActorProxy>()));
        session.AddLevel(new InterpPreviewLoadedLevel(
            playerPath,
            package: null,
            actors: new List<ActorProxy>(),
            InterpPreviewResourceKind.PlayerContext,
            InterpPreviewPlayerVariant.Female));

        CollectionAssert.AreEqual(new[] { levelPath }, new List<string>(session.LoadedLevelPaths));
        Assert.AreEqual(1, session.LoadedLevelCount);
        Assert.AreEqual(2, session.TotalResourceCount);
    }

    [TestMethod]
    public void Session_ContainsLevelPath_IgnoresPlayerContextResources()
    {
        using var session = new InterpPreviewSession();

        string playerPath = Path.GetFullPath("C:\\Test\\BioP_Char.pcc");
        session.AddLevel(new InterpPreviewLoadedLevel(
            playerPath,
            package: null,
            actors: new List<ActorProxy>(),
            InterpPreviewResourceKind.PlayerContext,
            InterpPreviewPlayerVariant.Female));

        Assert.IsFalse(session.ContainsLevelPath(playerPath));
    }

    [TestMethod]
    public void Session_ContainsResource_UsesStructuredPlayerIdentity()
    {
        using var session = new InterpPreviewSession();

        string playerPath = Path.GetFullPath("C:\\Test\\BioP_Char.pcc");
        session.AddLevel(new InterpPreviewLoadedLevel(
            playerPath,
            package: null,
            actors: new List<ActorProxy>(),
            InterpPreviewResourceKind.PlayerContext,
            InterpPreviewPlayerVariant.Female));

        InterpPreviewResourceKey femaleKey = InterpPreviewResourceKey.ForPlayerContext(playerPath, MEGame.Unknown, InterpPreviewPlayerVariant.Female);
        InterpPreviewResourceKey maleKey = InterpPreviewResourceKey.ForPlayerContext(playerPath, MEGame.Unknown, InterpPreviewPlayerVariant.Male);

        Assert.IsTrue(session.ContainsResource(femaleKey));
        Assert.IsFalse(session.ContainsResource(maleKey));
    }

    [TestMethod]
    public void Session_CommitLevel_PlayerVariantReplacement_IsSingleResourceAndReportsRemovedActors()
    {
        using var session = new InterpPreviewSession();

        string playerPath = Path.GetFullPath("C:\\Test\\BioP_Char.pcc");
        ActorProxy femaleActor = CreateDisposableTestActor();
        ActorProxy maleActor = CreateDisposableTestActor();

        InterpPreviewSessionCommitResult firstCommit = session.CommitLevel(
            new InterpPreviewLoadedLevel(
                playerPath,
                package: null,
                actors: new List<ActorProxy> { femaleActor },
                InterpPreviewResourceKind.PlayerContext,
                InterpPreviewPlayerVariant.Female),
            replace: false);

        Assert.AreEqual(1, firstCommit.AddedActors.Count);
        Assert.AreEqual(0, firstCommit.RemovedActors.Count);

        InterpPreviewSessionCommitResult secondCommit = session.CommitLevel(
            new InterpPreviewLoadedLevel(
                playerPath,
                package: null,
                actors: new List<ActorProxy> { maleActor },
                InterpPreviewResourceKind.PlayerContext,
                InterpPreviewPlayerVariant.Male),
            replace: false);

        Assert.AreEqual(1, secondCommit.AddedActors.Count);
        Assert.AreEqual(1, secondCommit.RemovedActors.Count);
        Assert.AreSame(femaleActor, secondCommit.RemovedActors[0]);
        Assert.AreSame(maleActor, secondCommit.AddedActors[0]);
        Assert.AreEqual(1, session.TotalResourceCount);
        Assert.IsFalse(session.ContainsResource(InterpPreviewResourceKey.ForPlayerContext(playerPath, MEGame.Unknown, InterpPreviewPlayerVariant.Female)));
        Assert.IsTrue(session.ContainsResource(InterpPreviewResourceKey.ForPlayerContext(playerPath, MEGame.Unknown, InterpPreviewPlayerVariant.Male)));
        Assert.AreEqual(1, secondCommit.RetiredResources.Count);
        Assert.IsFalse(IsActorDisposed(femaleActor));
        foreach (InterpPreviewOwnedResource retiredResource in secondCommit.RetiredResources)
        {
            retiredResource.Dispose();
        }

        Assert.IsTrue(IsActorDisposed(femaleActor));
        Assert.AreEqual(1, firstCommit.AddedActors.Count);
        Assert.AreSame(femaleActor, firstCommit.AddedActors[0]);
    }

    [TestMethod]
    public void Session_CommitLevel_Replace_ReportsPriorActorsAsRemoved()
    {
        using var session = new InterpPreviewSession();

        ActorProxy actorA = CreateDisposableTestActor();
        ActorProxy actorB = CreateDisposableTestActor();
        ActorProxy replacementActor = CreateDisposableTestActor();

        session.CommitLevel(new InterpPreviewLoadedLevel(
            "C:\\Test\\LevelA.pcc",
            package: null,
            actors: new List<ActorProxy> { actorA, actorB }),
            replace: false);

        InterpPreviewSessionCommitResult replaceCommit = session.CommitLevel(
            new InterpPreviewLoadedLevel(
                "C:\\Test\\LevelB.pcc",
                package: null,
                actors: new List<ActorProxy> { replacementActor }),
            replace: true);

        Assert.AreEqual(1, replaceCommit.AddedActors.Count);
        Assert.AreEqual(2, replaceCommit.RemovedActors.Count);
        CollectionAssert.Contains(new List<ActorProxy>(replaceCommit.RemovedActors), actorA);
        CollectionAssert.Contains(new List<ActorProxy>(replaceCommit.RemovedActors), actorB);
        Assert.AreEqual(1, replaceCommit.RetiredResources.Count);
        Assert.AreEqual(1, session.Actors.Count);
        Assert.AreSame(replacementActor, session.Actors[0]);
        Assert.IsFalse(IsActorDisposed(actorA));
        Assert.IsFalse(IsActorDisposed(actorB));
        foreach (InterpPreviewOwnedResource retiredResource in replaceCommit.RetiredResources)
        {
            retiredResource.Dispose();
        }

        Assert.IsTrue(IsActorDisposed(actorA));
        Assert.IsTrue(IsActorDisposed(actorB));
    }

    [TestMethod]
    public void Session_PlayerSwitch_DisposesReplacedPlayerActor_ExactlyOnce()
    {
        using var session = new InterpPreviewSession();

        string playerPath = Path.GetFullPath("C:\\Test\\BioP_Char.pcc");
        ActorProxy femaleActor = CreateDisposableTestActor();
        ActorProxy maleActor = CreateDisposableTestActor();

        session.CommitLevel(new InterpPreviewLoadedLevel(
            playerPath,
            package: null,
            actors: new List<ActorProxy> { femaleActor },
            InterpPreviewResourceKind.PlayerContext,
            InterpPreviewPlayerVariant.Female),
            replace: false);

        InterpPreviewSessionCommitResult switchCommit = session.CommitLevel(new InterpPreviewLoadedLevel(
            playerPath,
            package: null,
            actors: new List<ActorProxy> { maleActor },
            InterpPreviewResourceKind.PlayerContext,
            InterpPreviewPlayerVariant.Male),
            replace: false);

        Assert.IsFalse(IsActorDisposed(femaleActor));
        foreach (InterpPreviewOwnedResource retiredResource in switchCommit.RetiredResources)
        {
            retiredResource.Dispose();
        }

        Assert.IsTrue(IsActorDisposed(femaleActor));
        session.Dispose();
        Assert.IsTrue(IsActorDisposed(femaleActor));
        Assert.IsTrue(IsActorDisposed(maleActor));
    }

    [TestMethod]
    public void Session_PlayerContext_IndexesCanonicalPlayerAlias()
    {
        using var session = new InterpPreviewSession();

        string playerPath = Path.GetFullPath("C:\\Test\\BioP_Char.pcc");
        ActorProxy playerActor = CreateDisposableTestActor();
        session.CommitLevel(new InterpPreviewLoadedLevel(
            playerPath,
            package: null,
            actors: new List<ActorProxy> { playerActor },
            InterpPreviewResourceKind.PlayerContext,
            InterpPreviewPlayerVariant.Female),
            replace: false);

        IReadOnlyList<ActorProxy> matches = session.FindActorsByLookup("player");
        Assert.AreEqual(1, matches.Count);
        Assert.AreSame(playerActor, matches[0]);
    }

    private static ActorProxy CreateDisposableTestActor()
    {
        var actor = (ActorProxy)RuntimeHelpers.GetUninitializedObject(typeof(ActorProxy));
        actor.Components = [];
        return actor;
    }

    private static bool IsActorDisposed(ActorProxy actor)
    {
        FieldInfo disposedField = typeof(ActorProxy).GetField("isDisposed", BindingFlags.Instance | BindingFlags.NonPublic);
        return disposedField is not null && disposedField.GetValue(actor) is bool disposed && disposed;
    }

    private static IMEPackage CreatePackageSubstituteOrInconclusive()
    {
        try
        {
            return Substitute.For<IMEPackage>();
        }
        catch (TypeLoadException ex)
        {
            Assert.Inconclusive($"Dynamic proxy generation failed in this environment: {ex.Message}");
            throw;
        }
    }
}
