using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewOwnershipTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        LegendaryExplorerCoreLib.InitLib(System.Threading.Tasks.TaskScheduler.Default);
    }

    [TestMethod]
    public void LoadedLevel_Dispose_IsExactOnce_ForAbandonedCandidate()
    {
        var events = new List<string>();
        MEPackage package = CreateTrackedPackage(events);
        DisposalTrackingActor actor = CreateTrackingActor(package, events, "ActorA");
        var loaded = new InterpPreviewLoadedLevel(package.FilePath, package, [actor]);

        loaded.Dispose();
        loaded.Dispose();

        Assert.AreEqual(0, package.RefCount, "The candidate should release its single package reference exactly once.");
        Assert.AreEqual(1, actor.DisposeCalls);
        CollectionAssert.AreEqual(new[] { "ActorA", "package" }, events);
    }

    [TestMethod]
    public void LoadedLevel_ToOwnedResource_TransfersOwnershipOnlyOnce_AndDisposalTransfersToOwnedResource()
    {
        var events = new List<string>();
        MEPackage package = CreateTrackedPackage(events);
        DisposalTrackingActor actor = CreateTrackingActor(package, events, "ActorA");
        var loaded = new InterpPreviewLoadedLevel(package.FilePath, package, [actor]);

        InterpPreviewOwnedResource resource = loaded.ToOwnedResource();

        loaded.Dispose();
        loaded.Dispose();

        Assert.AreEqual(1, package.RefCount, "Transferred resources must remain alive when the old candidate is disposed.");
        Assert.AreEqual(0, actor.DisposeCalls);
        Assert.AreEqual(0, events.Count);
        Assert.IsFalse(loaded.IsDisposed);
        Assert.IsTrue(loaded.IsTransferred);

        Assert.Throws<InvalidOperationException>(() => loaded.ToOwnedResource());
        Assert.AreEqual(1, package.RefCount);
        Assert.AreEqual(0, events.Count);

        resource.Dispose();
        resource.Dispose();

        Assert.AreEqual(0, package.RefCount);
        Assert.AreEqual(1, actor.DisposeCalls);
        CollectionAssert.AreEqual(new[] { "ActorA", "package" }, events);
    }

    [TestMethod]
    public void OwnedResource_DisposesActorsBeforePackage()
    {
        var events = new List<string>();
        MEPackage package = CreateTrackedPackage(events);
        DisposalTrackingActor firstActor = CreateTrackingActor(package, events, "ActorA");
        DisposalTrackingActor secondActor = CreateTrackingActor(package, events, "ActorB");
        var resource = new InterpPreviewOwnedResource(
            InterpPreviewResourceKey.ForLevel(package.FilePath, package.Game),
            package.FilePath,
            package,
            [firstActor, secondActor]);

        resource.Dispose();
        resource.Dispose();

        Assert.AreEqual(0, package.RefCount);
        Assert.AreEqual(1, firstActor.DisposeCalls);
        Assert.AreEqual(1, secondActor.DisposeCalls);
        CollectionAssert.AreEqual(new[] { "ActorA", "ActorB", "package" }, events);
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

    [TestMethod]
    public void Session_AddLevel_PlayerSwitch_RetiresPreviousContextWithoutDiscardingOwnership()
    {
        using var session = new InterpPreviewSession();
        ActorProxy female = CreateDisposableTestActor();
        ActorProxy male = CreateDisposableTestActor();
        session.AddLevel(new InterpPreviewLoadedLevel("C:/tests/Player.pcc", null, [female],
            InterpPreviewResourceKind.PlayerContext, InterpPreviewPlayerVariant.Female));
        session.AddLevel(new InterpPreviewLoadedLevel("C:/tests/Player.pcc", null, [male],
            InterpPreviewResourceKind.PlayerContext, InterpPreviewPlayerVariant.Male));
        Assert.AreEqual(1, session.TotalResourceCount);
        Assert.IsTrue(IsActorDisposed(female));
        Assert.IsFalse(IsActorDisposed(male));
        Assert.AreSame(male, session.FindActorsByLookup("player")[0]);
        session.Dispose();
        Assert.IsTrue(IsActorDisposed(male));
    }

    [TestMethod]
    public void Session_SceneGeneration_IncrementsOnPublicMutations_AndNotOnEmptyClear()
    {
        using var session = new InterpPreviewSession();

        long generation0 = session.SceneGeneration;
        session.CommitLevel(new InterpPreviewLoadedLevel("C:\\Test\\LevelA.pcc", package: null, actors: new List<ActorProxy>()), replace: false);
        long generation1 = session.SceneGeneration;

        session.CommitLevel(new InterpPreviewLoadedLevel("C:\\Test\\LevelB.pcc", package: null, actors: new List<ActorProxy>()), replace: true);
        long generation2 = session.SceneGeneration;

        session.CommitLevel(new InterpPreviewLoadedLevel(
            "C:\\Test\\BioP_Char.pcc",
            package: null,
            actors: new List<ActorProxy>(),
            InterpPreviewResourceKind.PlayerContext,
            InterpPreviewPlayerVariant.Female),
            replace: false);
        long generation3 = session.SceneGeneration;

        session.ClearLevels();
        long generation4 = session.SceneGeneration;

        session.ClearLevels();
        long generation5 = session.SceneGeneration;

        Assert.AreEqual(generation0 + 1, generation1);
        Assert.AreEqual(generation1 + 1, generation2);
        Assert.AreEqual(generation2 + 1, generation3);
        Assert.AreEqual(generation3 + 1, generation4);
        Assert.AreEqual(generation4, generation5);
    }

    [TestMethod]
    public void Session_CreateSceneSnapshot_ReturnsImmutableSceneStampAndDescriptors()
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
            InterpPreviewPlayerVariant.Male));

        InterpPreviewSceneSnapshot snapshot = session.CreateSceneSnapshot();
        long generationAtSnapshot = session.SceneGeneration;

        session.ClearLevels();

        Assert.AreEqual(session.SessionId, snapshot.Stamp.SessionId);
        Assert.AreEqual(generationAtSnapshot, snapshot.Stamp.Generation);
        Assert.AreEqual(2, snapshot.Resources.Count);
        Assert.AreEqual(InterpPreviewPlayerVariant.Male, snapshot.LoadedPlayerVariant);
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

    private static MEPackage CreateTrackedPackage(List<string> events)
    {
        var package = (MEPackage)MEPackageHandler.CreateMemoryEmptyPackage("OwnershipTests.pcc", MEGame.LE3);
        Assert.AreEqual(1, package.RefCount, "The fixture must begin with one owned package reference.");
        package.NoLongerUsed += _ => events.Add("package");
        return package;
    }

    private static DisposalTrackingActor CreateTrackingActor(IMEPackage package, List<string> events, string name)
    {
        var export = new ExportEntry(package, 0, name, prePropBinary: new byte[4], properties: []);
        package.AddExport(export);
        return new DisposalTrackingActor(export, events, name);
    }

    private sealed class DisposalTrackingActor : ActorProxy
    {
        private readonly List<string> _events;
        private readonly string _name;

        public DisposalTrackingActor(ExportEntry export, List<string> events, string name) : base(export)
        {
            _events = events;
            _name = name;
        }

        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            DisposeCalls++;
            base.Dispose(disposing);
            _events.Add(_name);
        }
    }
}
