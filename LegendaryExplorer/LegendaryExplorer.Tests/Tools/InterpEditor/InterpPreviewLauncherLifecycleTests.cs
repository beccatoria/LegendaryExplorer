using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorer.Tools.LevelEditor.Scene3D;
using System.Numerics;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewLauncherLifecycleTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _) => LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);

    [TestMethod, Timeout(15000)]
    public async Task Shutdown_DuringSecondCollectionPreparation_JoinsStagedCandidateCleanup()
    {
        using var h = new Harness();
        var entered = Signal();
        var release = Signal();
        h.Preparer.Prepare = async (path, _) =>
        {
            if (path.EndsWith("C.pcc", StringComparison.OrdinalIgnoreCase))
            {
                entered.TrySetResult(true);
                await release.Task; // Deliberately non-cooperative: shutdown must still join it.
            }
            return h.CreatePrepared(path);
        };
        Task<InterpPreviewLauncherResult> launch = h.Launch(["C:/tests/B.pcc", "C:/tests/C.pcc"]);
        await entered.Task;
        Assert.AreEqual(1, h.Factory.Actors.Count, "B must actually have been realized and staged.");

        int diagnosticsAfterClosing = 0;
        Task shutdown = h.Runtime.ShutdownAsync();
        h.Runtime.DiagnosticsChanged += (_, _) => diagnosticsAfterClosing++;
        Assert.IsFalse(shutdown.IsCompleted, "A staged launcher request must remain tracked through cleanup.");
        release.TrySetResult(true);

        InterpPreviewLauncherResult result = await launch;
        await shutdown;
        Assert.AreEqual(InterpPreviewLauncherOutcome.RejectedClosing, result.Outcome);
        Assert.AreEqual(0, h.Session.TotalResourceCount);
        Assert.AreEqual(0, h.Host.Applied);
        Assert.AreEqual(0, h.Host.Published);
        Assert.AreEqual(0, diagnosticsAfterClosing);
        Assert.IsTrue(h.Packages.All(p => p.RefCount == 0));
        Assert.AreEqual(1, h.Factory.Actors[0].DisposeCalls);
        CollectionAssert.AreEqual(new[] { "package:C", "actor:B", "package:B" }, h.Events);
    }

    [TestMethod, Timeout(15000)]
    public async Task Shutdown_BetweenRealizationBatches_CleansOnDispatcherBeforeTeardown()
    {
        using var h = new Harness();
        h.ActorCount = 3;
        h.Dispatcher.PauseYield = true;
        Task<InterpPreviewLauncherResult> launch = h.Launch(["C:/tests/Slow.pcc"]);
        await h.Dispatcher.YieldEntered.Task;
        Task shutdown = h.Runtime.ShutdownAsync();
        InterpPreviewLauncherResult result = await launch;
        await shutdown;

        Assert.AreEqual(InterpPreviewLauncherOutcome.RejectedClosing, result.Outcome);
        Assert.AreEqual(1, h.Factory.Actors.Count);
        Assert.AreEqual(1, h.Factory.Actors[0].DisposeCalls);
        Assert.AreEqual(0, h.Packages.Single().RefCount);
        Assert.AreEqual(0, h.Host.Applied);
        Assert.IsTrue(h.Dispatcher.Verifications > 0);
    }

    [TestMethod, Timeout(15000)]
    public async Task NewLaunch_SupersedesWholeOlderCollection_WithoutOldDialogueOrRegistration()
    {
        using var h = new Harness();
        var entered = Signal();
        var release = Signal();
        h.Preparer.Prepare = async (path, _) =>
        {
            if (path.EndsWith("Slow.pcc")) { entered.TrySetResult(true); await release.Task; }
            return h.CreatePrepared(path);
        };
        Task<InterpPreviewLauncherResult> old = h.Launch(["C:/tests/B.pcc", "C:/tests/Slow.pcc"], resolve: true);
        await entered.Task;
        Task<InterpPreviewLauncherResult> latest = h.Launch(["C:/tests/Latest.pcc"], resolve: true);
        release.TrySetResult(true);

        InterpPreviewLauncherResult obsolete = await old;
        InterpPreviewLauncherResult current = await latest;
        Assert.AreEqual(InterpPreviewLauncherOutcome.Superseded, obsolete.Outcome);
        Assert.AreEqual(InterpPreviewLauncherOutcome.Committed, current.Outcome);
        Assert.IsFalse(h.Runtime.IsRequestCurrent(obsolete.RequestId));
        Assert.IsTrue(h.Runtime.IsRequestCurrent(current.RequestId));
        Assert.AreEqual(1, h.Host.Applied);
        Assert.AreEqual(1, h.Host.Published);
        Assert.AreEqual(1, h.Resolver.Calls);
        Assert.AreEqual("Latest", System.IO.Path.GetFileNameWithoutExtension(h.Session.LoadedLevelPaths.Single()));
        Assert.AreEqual(1, h.Factory.Actors.First().DisposeCalls, "The old staged B proxy must be released.");
    }

    [TestMethod, Timeout(15000)]
    public async Task ClearRequest_SupersedesLoading_AndInvalidatesCompletedFeedback()
    {
        using var h = new Harness();
        InterpPreviewLauncherResult completed = await h.Launch(["C:/tests/A.pcc"]);
        var entered = Signal();
        var release = Signal();
        h.Preparer.Prepare = async (path, _) => { entered.TrySetResult(true); await release.Task; return h.CreatePrepared(path); };
        Task<InterpPreviewLauncherResult> old = h.Launch(["C:/tests/B.pcc"]);
        await entered.Task;
        Task<InterpPreviewLauncherResult> clear = h.Runtime.ExecuteLauncherRequestAsync(
            new InterpPreviewLauncherRequest([]) { ClearScene = true }, null, h.Host);
        release.TrySetResult(true);
        Assert.AreEqual(InterpPreviewLauncherOutcome.Superseded, (await old).Outcome);
        InterpPreviewLauncherResult cleared = await clear;
        Assert.IsTrue(h.Runtime.IsRequestCurrent(cleared.RequestId));
        Assert.IsFalse(h.Runtime.IsRequestCurrent(completed.RequestId), "Deferred focus/status from A must be ignored.");
        Assert.AreEqual(0, h.Session.TotalResourceCount);
        Assert.AreEqual(2, h.Host.Applied, "The initial scene and the atomic clear each publish once.");
        Assert.IsTrue(h.Packages.All(p => p.RefCount == 0));
    }

    [TestMethod, Timeout(15000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PendingPlayerPreparation_IsJoinedAndCannotCommitAfterSupersessionOrClosing(bool closing)
    {
        using var h = new Harness();
        h.ActorCount = 2;
        h.Factory.ThrowOnFirstDispose = closing;
        var entered = Signal();
        var release = Signal();
        InterpPreviewLoadedLevel player = null;
        h.Host.PreparePlayer = async () =>
        {
            entered.TrySetResult(true);
            await release.Task;
            var prepared = h.CreatePrepared("C:/tests/Player.pcc");
            var realizer = new InterpPreviewActorRealizer(h.Dispatcher, h.Factory);
            player = (await realizer.RealizeAsync(prepared.PreparedResource, null, () => true, CancellationToken.None)).LoadedLevel;
            return player;
        };
        Task<InterpPreviewLauncherResult> old = h.Runtime.ExecuteLauncherRequestAsync(
            new InterpPreviewLauncherRequest([]) { EnsurePlayerContext = true, ResolveDialogue = true }, null, h.Host);
        await entered.Task;
        Task next = closing ? h.Runtime.ShutdownAsync() : h.Launch(["C:/tests/Latest.pcc"]);
        Assert.IsFalse(next.IsCompleted);
        release.TrySetResult(true);
        InterpPreviewLauncherResult result = await old;
        await next;
        Assert.AreEqual(closing ? InterpPreviewLauncherOutcome.RejectedClosing : InterpPreviewLauncherOutcome.Superseded, result.Outcome);
        Assert.IsTrue(player.IsDisposed);
        Assert.AreEqual(0, h.Packages.First().RefCount);
        Assert.IsTrue(h.Factory.Actors.Where(a => a.Export.FileRef == h.Packages.First()).All(a => a.DisposeCalls == 1));
        Assert.AreEqual(0, h.Host.Published);
    }

    [TestMethod, Timeout(15000)]
    public async Task LaterCollectionPreparationFailure_PreservesExistingSceneAndCleansRealStagedProxies()
    {
        using var h = new Harness();
        await h.Launch(["C:/tests/A.pcc"]);
        long generation = h.Session.SceneGeneration;
        ActorProxy actorA = h.Session.Actors.Single();
        h.Preparer.Prepare = (path, _) => path.EndsWith("C.pcc")
            ? Task.FromResult(InterpPreviewPrepareResult.Failed(new InvalidOperationException("C could not load")))
            : Task.FromResult(h.CreatePrepared(path));

        InterpPreviewLauncherResult result = await h.Launch(["C:/tests/B.pcc", "C:/tests/C.pcc"]);

        Assert.AreEqual(InterpPreviewLauncherOutcome.Failed, result.Outcome);
        Assert.AreEqual(generation, h.Session.SceneGeneration);
        Assert.AreSame(actorA, h.Session.Actors.Single());
        CollectionAssert.AreEquivalent(new[] { actorA }, h.Host.Registered.ToArray());
        Assert.AreEqual(1, h.Packages[0].RefCount);
        Assert.AreEqual(0, h.Packages[1].RefCount);
        Assert.AreEqual(1, h.Factory.Actors[1].DisposeCalls);
    }

    [TestMethod, Timeout(15000)]
    public async Task RealizerFailure_ContinuesCleanupAfterThrowingProxy_AndReturnsFailure()
    {
        using var h = new Harness();
        h.ActorCount = 3;
        h.Factory.ThrowOnCreate = 3;
        h.Factory.ThrowOnFirstDispose = true;
        InterpPreviewLauncherResult result = await h.Launch(["C:/tests/Broken.pcc"]);

        Assert.AreEqual(InterpPreviewLauncherOutcome.Failed, result.Outcome);
        StringAssert.Contains(result.Message, "construction failed");
        StringAssert.Contains(result.Message, "disposal failed");
        Assert.AreEqual(2, h.Factory.Actors.Count);
        Assert.IsTrue(h.Factory.Actors.All(a => a.DisposeCalls == 1));
        Assert.AreEqual(0, h.Packages.Single().RefCount);
        Assert.AreEqual(0, h.Session.TotalResourceCount);
        Assert.AreEqual(0, h.Host.Applied);
        CollectionAssert.AreEqual(new[] { "actor:Broken", "actor:Broken", "package:Broken" }, h.Events);
        Assert.IsTrue(h.Runtime.Diagnostics.Any(d => d.Code == "LAUNCH_FAILED"));
    }

    [TestMethod, Timeout(15000)]
    public async Task Realizer_UsesExplicitDispatcherAndYields_WithoutAmbientContext()
    {
        using var h = new Harness();
        h.ActorCount = 3;
        InterpPreviewLauncherResult result = await Task.Run(() => h.Launch(["C:/tests/A.pcc"]));
        Assert.AreEqual(InterpPreviewLauncherOutcome.Committed, result.Outcome);
        Assert.AreEqual(2, h.Dispatcher.Yields);
        Assert.AreEqual(3, h.Factory.Actors.Count);
        Assert.AreEqual(3, h.Host.Registered.Count);
        Assert.AreEqual(1, h.Session.TotalResourceCount);
    }

    [TestMethod, Timeout(15000)]
    public async Task HostPreparationException_ReturnsFailure_AndClosingRejectsWithoutCallbacks()
    {
        using var h = new Harness();
        h.Host.PreparePlayer = () => throw new InvalidOperationException("player package failed");
        var request = new InterpPreviewLauncherRequest([]) { EnsurePlayerContext = true, ResolveDialogue = true };
        InterpPreviewLauncherResult result = await h.Runtime.ExecuteLauncherRequestAsync(request, null, h.Host);
        Assert.AreEqual(InterpPreviewLauncherOutcome.Failed, result.Outcome);
        Assert.IsTrue(result.AllowsFeedback);
        StringAssert.Contains(result.Message, "player package failed");
        Assert.AreEqual(0, h.Host.Published);
        await h.Runtime.ShutdownAsync();
        Assert.IsFalse(h.Runtime.IsRequestCurrent(result.RequestId));
        int callbackCount = h.Host.Preparations;
        var rejected = await h.Runtime.ExecuteLauncherRequestAsync(request, null, h.Host);
        Assert.AreEqual(InterpPreviewLauncherOutcome.RejectedClosing, rejected.Outcome);
        Assert.IsFalse(rejected.AllowsFeedback);
        Assert.AreEqual(callbackCount, h.Host.Preparations);
    }

    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [TestMethod, Timeout(15000)]
    public async Task CollectionCommit_PublishesBothLevelsOnce_AndRetiresOnlyAfterUnregistration()
    {
        using var h = new Harness();
        await h.Launch(["C:/tests/A.pcc"]);
        long generation = h.Session.SceneGeneration;
        TestActor old = h.Factory.Actors.Single();
        old.OnDispose = () =>
        {
            Assert.IsFalse(h.Host.Registered.Contains(old));
            Assert.IsFalse(h.Session.Actors.Contains(old));
            Assert.AreEqual(2, h.Session.LoadedLevelCount);
            Assert.AreEqual(1, h.Packages[0].RefCount, "Package must outlive proxy cleanup.");
        };

        var result = await h.Launch(["C:/tests/B.pcc", "C:/tests/C.pcc"]);

        Assert.AreEqual(InterpPreviewLauncherOutcome.Committed, result.Outcome);
        Assert.AreEqual(2, result.LoadedCount);
        Assert.AreEqual(generation + 1, h.Session.SceneGeneration);
        Assert.AreEqual(2, h.Host.Applied, "One initial commit, one collection commit.");
        CollectionAssert.AreEquivalent(h.Session.Actors.ToArray(), h.Host.Registered.ToArray());
        CollectionAssert.AreEquivalent(h.Session.Actors.ToArray(), h.Session.FindActorsByLookup("Actor0").ToArray());
        Assert.AreEqual(1, old.DisposeCalls);
        Assert.AreEqual(0, h.Packages[0].RefCount);
        Assert.IsTrue(h.Packages.Skip(1).All(package => package.RefCount == 1));
    }

    [TestMethod, Timeout(15000)]
    public async Task OverlappingReplacement_RetainsA_AddsC_RemovesB_ThenCanShrinkToA()
    {
        using var h = new Harness();
        await h.Launch(["C:/tests/A.pcc", "C:/tests/B.pcc"]);
        TestActor actorA = h.Factory.Actors[0], actorB = h.Factory.Actors[1];
        long generation = h.Session.SceneGeneration;

        Assert.AreEqual(InterpPreviewLauncherOutcome.Committed,
            (await h.Launch(["c:/TESTS/a.pcc", "C:/tests/C.pcc", "C:/tests/C.pcc"])).Outcome);
        Assert.AreEqual(3, h.Packages.Count, "A must not be opened or realized again.");
        Assert.AreEqual(1, actorB.DisposeCalls);
        Assert.AreEqual(0, actorA.DisposeCalls);
        Assert.AreEqual(generation + 1, h.Session.SceneGeneration);
        Assert.IsTrue(h.Host.Registered.Contains(actorA));

        Assert.AreEqual(InterpPreviewLauncherOutcome.Committed, (await h.Launch(["C:/tests/A.pcc"])).Outcome);
        Assert.AreEqual(generation + 2, h.Session.SceneGeneration);
        Assert.AreSame(actorA, h.Session.Actors.Single());
        Assert.AreSame(actorA, h.Host.Registered.Single());
        Assert.AreEqual(1, h.Factory.Actors[2].DisposeCalls);
        Assert.AreEqual(1, h.Packages[0].RefCount);
    }

    [TestMethod, Timeout(15000)]
    public async Task IdenticalCollection_AndDuplicateAdd_AreNoChangeWithoutRegistrationOrGeneration()
    {
        using var h = new Harness();
        await h.Launch(["C:/tests/A.pcc", "C:/tests/B.pcc"]);
        long generation = h.Session.SceneGeneration;
        var same = await h.Launch(["C:/tests/B.pcc", "C:/tests/A.pcc", "c:/tests/a.pcc"]);
        var add = await h.Runtime.ExecuteLauncherRequestAsync(new InterpPreviewLauncherRequest(["C:/tests/A.pcc"])
            { ReplaceOnFirstLevel = false }, null, h.Host);
        Assert.AreEqual(InterpPreviewLauncherOutcome.NoChange, same.Outcome);
        Assert.AreEqual(InterpPreviewLauncherOutcome.NoChange, add.Outcome);
        Assert.AreEqual(generation, h.Session.SceneGeneration);
        Assert.AreEqual(1, h.Host.Applied);
        Assert.AreEqual(2, h.Packages.Count);
    }

    [TestMethod, Timeout(15000)]
    [DataRow("register")]
    [DataRow("unregister")]
    [DataRow("lights")]
    [DataRow("publish")]
    public async Task CollectionPublicationFailure_RestoresRealRegistrationAndLights_AndPreservesScene(string phase)
    {
        using var h = new Harness();
        h.Host.LoadLights = package => [CreateLight(package, h.Host.Context.PackageCache)];
        await h.Launch(["C:/tests/A.pcc"]);
        ActorProxy oldActor = h.Session.Actors.Single();
        SceneLight oldLight = h.Host.Context.GetLights(out _).Single();
        long generation = h.Session.SceneGeneration;
        if (phase == "register") h.Host.ThrowAfterRegister = 2;
        if (phase == "unregister") h.Host.ThrowAfterUnregister = 1;
        if (phase == "publish") h.Host.ThrowAfterCommit = true;
        if (phase == "lights") h.Host.LoadLights = package => package.FilePath.EndsWith("C.pcc")
            ? throw new InvalidOperationException("light loading failed") : [CreateLight(package, h.Host.Context.PackageCache)];

        var result = await h.Launch(["C:/tests/B.pcc", "C:/tests/C.pcc"]);

        Assert.AreEqual(InterpPreviewLauncherOutcome.Failed, result.Outcome);
        Assert.AreEqual(generation, h.Session.SceneGeneration);
        Assert.AreSame(oldActor, h.Session.Actors.Single());
        Assert.AreSame(oldActor, h.Host.Registered.Single());
        Assert.AreSame(oldLight, h.Host.Context.GetLights(out _).Single());
        h.Host.AssertHitRegistration();
        Assert.AreSame(oldActor, h.Session.FindActorsByLookup("Actor0").Single());
        Assert.AreEqual(1, h.Packages[0].RefCount);
        Assert.IsTrue(h.Packages.Skip(1).All(package => package.RefCount == 0));
        Assert.IsTrue(h.Factory.Actors.Skip(1).All(actor => actor.DisposeCalls == 1));
        Assert.AreEqual(1, h.Host.SceneFeedback, "No success feedback for the failed replacement.");
        h.Host.ThrowAfterRegister = h.Host.ThrowAfterUnregister = 0;
        h.Host.ThrowAfterCommit = false;
        await h.Runtime.ExecuteLauncherRequestAsync(new InterpPreviewLauncherRequest([]) { ClearScene = true }, null, h.Host);
        Assert.AreEqual(0, h.Host.Registered.Count);
        Assert.HasCount(0, h.Host.Context.GetLights(out _));
        h.Host.AssertHitRegistration();
    }

    [TestMethod, Timeout(15000)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RequestInvalidatedDuringRegistration_RollsBackBeforePublication(bool closing)
    {
        using var h = new Harness();
        h.Host.LoadLights = package => [CreateLight(package, h.Host.Context.PackageCache)];
        await h.Launch(["C:/tests/A.pcc"]);
        ActorProxy oldActor = h.Session.Actors.Single();
        SceneLight oldLight = h.Host.Context.GetLights(out _).Single();
        long generation = h.Session.SceneGeneration;
        Task next = null;
        h.Host.AfterRegister = () =>
        {
            h.Host.AfterRegister = null;
            next = closing ? h.Runtime.ShutdownAsync() : h.Launch(["C:/tests/A.pcc"]);
        };

        var result = await h.Launch(["C:/tests/B.pcc", "C:/tests/C.pcc"]);
        await next;
        Assert.AreEqual(closing ? InterpPreviewLauncherOutcome.RejectedClosing : InterpPreviewLauncherOutcome.Superseded, result.Outcome);
        Assert.AreEqual(generation, h.Session.SceneGeneration);
        Assert.AreSame(oldActor, h.Session.Actors.Single());
        Assert.AreSame(oldActor, h.Host.Registered.Single());
        Assert.AreSame(oldLight, h.Host.Context.GetLights(out _).Single());
        h.Host.AssertHitRegistration();
        Assert.IsTrue(h.Factory.Actors.Skip(1).All(actor => actor.DisposeCalls == 1));
        Assert.IsTrue(h.Packages.Skip(1).All(package => package.RefCount == 0));
        Assert.AreEqual(1, h.Host.Applied);
    }

    [TestMethod, Timeout(15000)]
    public async Task PlayerAndLevels_AreOneTransaction_AndPreparationFailureKeepsOldScene()
    {
        using var h = new Harness();
        await h.Launch(["C:/tests/A.pcc"]);
        long generation = h.Session.SceneGeneration;
        h.Host.PreparePlayer = () => throw new InvalidOperationException("Player unavailable");
        var request = new InterpPreviewLauncherRequest(["C:/tests/B.pcc", "C:/tests/C.pcc"]) { EnsurePlayerContext = true };
        Assert.AreEqual(InterpPreviewLauncherOutcome.Failed,
            (await h.Runtime.ExecuteLauncherRequestAsync(request, null, h.Host)).Outcome);
        Assert.AreEqual(generation, h.Session.SceneGeneration);
        Assert.AreEqual("A", System.IO.Path.GetFileNameWithoutExtension(h.Session.LoadedLevelPaths.Single()));
        Assert.IsTrue(h.Packages.Skip(1).All(package => package.RefCount == 0));

        h.Host.PreparePlayer = () => h.CreatePlayer();
        var result = await h.Runtime.ExecuteLauncherRequestAsync(request, null, h.Host);
        Assert.AreEqual(InterpPreviewLauncherOutcome.Committed, result.Outcome);
        Assert.AreEqual(generation + 1, h.Session.SceneGeneration);
        Assert.AreEqual(2, h.Session.LoadedLevelCount);
        Assert.AreEqual(3, h.Session.TotalResourceCount);
        Assert.HasCount(1, h.Session.FindActorsByLookup("player"));
        Assert.AreEqual(2, h.Host.Applied);
        CollectionAssert.AreEquivalent(h.Session.Actors.ToArray(), h.Host.Registered.ToArray());
    }

    [TestMethod, Timeout(15000)]
    public async Task RetirementAndFeedbackFailures_AreReportedWithoutUndoingSuccessfulScene()
    {
        using var h = new Harness();
        h.Factory.ThrowOnFirstDispose = true;
        await h.Launch(["C:/tests/A.pcc"]);
        h.Host.ThrowOnFeedback = true;
        long generation = h.Session.SceneGeneration;

        var result = await h.Launch(["C:/tests/B.pcc", "C:/tests/C.pcc"]);

        Assert.AreEqual(InterpPreviewLauncherOutcome.Committed, result.Outcome);
        Assert.AreEqual(generation + 1, h.Session.SceneGeneration);
        Assert.AreEqual(2, h.Session.TotalResourceCount);
        Assert.AreEqual(0, h.Packages[0].RefCount);
        Assert.AreEqual(1, h.Factory.Actors[0].DisposeCalls);
        CollectionAssert.AreEquivalent(h.Session.Actors.ToArray(), h.Host.Registered.ToArray());
        Assert.IsTrue(h.Runtime.Diagnostics.Any(d => d.Code == "RESOURCE_CLEANUP_FAILED"));
        Assert.IsTrue(h.Runtime.Diagnostics.Any(d => d.Code == "SCENE_FEEDBACK_FAILED"));
    }

    private static SceneLight CreateLight(IMEPackage package, PackageCache cache)
    {
        var classImport = new ImportEntry(package) { ObjectName = "PointLightComponent", ClassName = "Class", PackageFile = "Engine" };
        package.AddImport(classImport);
        var export = new ExportEntry(package, 0, "TestLight", prePropBinary: new byte[8], properties: []) { Class = classImport };
        package.AddExport(export);
        return SceneLight.Create(export, "PointLight", cache, Matrix4x4.Identity);
    }

    [TestMethod, Timeout(15000)]
    public async Task CommitTimeDuplicateCandidate_IsNotTransferred_AndDoesNotReplaceOrInvalidateExistingResource()
    {
        using var h = new Harness();
        await h.Launch(["C:/tests/A.pcc"]);
        ActorProxy old = h.Session.Actors.Single();
        long generation = h.Session.SceneGeneration;
        var duplicate = await h.CreateCandidate("c:/tests/a.pcc");
        var duplicateActor = (TestActor)duplicate.Actors.Single();

        var result = await h.Dispatcher.InvokeAsync(() => h.Session.CommitCollection([duplicate], null,
            (_, _) => Assert.Fail("A duplicate-only request must not touch registration.")));

        Assert.IsFalse(result.Changed);
        Assert.IsFalse(duplicate.IsTransferred);
        Assert.AreEqual(generation, h.Session.SceneGeneration);
        Assert.AreSame(old, h.Session.Actors.Single());
        Assert.AreSame(old, h.Host.Registered.Single());
        await h.Dispatcher.InvokeAsync(duplicate.Dispose);
        Assert.AreEqual(1, duplicateActor.DisposeCalls);
        Assert.AreEqual(0, h.Packages[1].RefCount);
        Assert.AreEqual(1, h.Packages[0].RefCount);
    }

    [TestMethod, Timeout(15000)]
    public async Task SessionMissingDesiredResource_RejectsBeforeRegistration_AndCleansTransferredCandidate()
    {
        using var h = new Harness();
        await h.Launch(["C:/tests/A.pcc"]);
        ActorProxy old = h.Session.Actors.Single();
        long generation = h.Session.SceneGeneration;
        using var candidate = await h.CreateCandidate("C:/tests/B.pcc");
        var candidateActor = (TestActor)candidate.Actors.Single();

        await h.Dispatcher.InvokeAsync(() => Assert.ThrowsExactly<InvalidOperationException>(() =>
            h.Session.CommitCollection([candidate], ["C:/tests/B.pcc", "C:/tests/Missing.pcc"],
                (_, _) => Assert.Fail("Invalid desired membership must not reach registration."))));

        Assert.AreEqual(generation, h.Session.SceneGeneration);
        Assert.AreSame(old, h.Session.Actors.Single());
        Assert.AreSame(old, h.Host.Registered.Single());
        Assert.AreEqual(1, candidateActor.DisposeCalls);
        Assert.AreEqual(0, h.Packages[1].RefCount);
        Assert.AreEqual(1, h.Packages[0].RefCount);
    }

    [TestMethod, Timeout(15000)]
    public async Task RollbackCleanupFailure_PreservesOriginalScene_AndReportsBothErrors()
    {
        using var h = new Harness();
        // Make only the first staged B proxy throw on disposal, not the existing A proxy.
        await h.Launch(["C:/tests/A.pcc"]);
        ActorProxy old = h.Session.Actors.Single();
        h.Factory.ThrowOnDisposeCall = 2;
        h.Host.ThrowAfterRegister = 1;
        var result = await h.Launch(["C:/tests/B.pcc", "C:/tests/C.pcc"]);
        Assert.AreEqual(InterpPreviewLauncherOutcome.Failed, result.Outcome);
        StringAssert.Contains(result.Message, "registration failed");
        StringAssert.Contains(result.Message, "disposal failed");
        Assert.AreSame(old, h.Session.Actors.Single());
        Assert.AreSame(old, h.Host.Registered.Single());
        Assert.IsTrue(h.Factory.Actors.Skip(1).All(actor => actor.DisposeCalls == 1));
        Assert.IsTrue(h.Packages.Skip(1).All(package => package.RefCount == 0));
    }

    [TestMethod, Timeout(15000)]
    public async Task MultiLevelAdd_StagesEverything_AndAdvancesOnceWithoutRemovingExistingActors()
    {
        using var h = new Harness();
        await h.Launch(["C:/tests/A.pcc"]);
        ActorProxy old = h.Session.Actors.Single();
        long generation = h.Session.SceneGeneration;
        var request = new InterpPreviewLauncherRequest(["C:/tests/A.pcc", "C:/tests/B.pcc", "C:/tests/C.pcc"])
            { ReplaceOnFirstLevel = false };
        h.Preparer.Prepare = (path, _) => path.EndsWith("C.pcc")
            ? Task.FromResult(InterpPreviewPrepareResult.Failed(new InvalidOperationException("C unavailable")))
            : Task.FromResult(h.CreatePrepared(path));
        Assert.AreEqual(InterpPreviewLauncherOutcome.Failed,
            (await h.Runtime.ExecuteLauncherRequestAsync(request, null, h.Host)).Outcome);
        Assert.AreEqual(generation, h.Session.SceneGeneration);
        Assert.AreSame(old, h.Session.Actors.Single());
        Assert.AreSame(old, h.Host.Registered.Single());
        Assert.AreEqual(0, h.Packages[1].RefCount);

        h.Preparer.Prepare = (path, _) => Task.FromResult(h.CreatePrepared(path));
        var result = await h.Runtime.ExecuteLauncherRequestAsync(request, null, h.Host);
        Assert.AreEqual(InterpPreviewLauncherOutcome.Committed, result.Outcome);
        Assert.AreEqual(generation + 1, h.Session.SceneGeneration);
        Assert.AreEqual(3, h.Session.LoadedLevelCount);
        Assert.IsTrue(h.Session.Actors.Contains(old));
        Assert.AreEqual(1, h.Packages[0].RefCount);
        Assert.AreEqual(2, h.Host.Applied);
        h.Host.AssertHitRegistration();
    }

    [TestMethod, Timeout(15000)]
    public async Task PlayerVariantChange_RollsBackWithLevels_ThenPublishesOneSingletonAndOneGeneration()
    {
        using var h = new Harness();
        h.Host.PreparePlayer = () => h.CreatePlayer();
        await h.Runtime.ExecuteLauncherRequestAsync(new InterpPreviewLauncherRequest(["C:/tests/A.pcc"])
            { EnsurePlayerContext = true }, null, h.Host);
        ActorProxy female = h.Session.FindActorsByLookup("player").Single();
        ActorProxy actorA = h.Session.Actors.First();
        long generation = h.Session.SceneGeneration;
        h.Host.PreparePlayer = () => h.CreatePlayer(InterpPreviewPlayerVariant.Male);
        h.Host.ThrowAfterRegister = 2;
        var request = new InterpPreviewLauncherRequest(["C:/tests/A.pcc", "C:/tests/B.pcc"])
            { EnsurePlayerContext = true, PreferFemalePlayer = false };
        Assert.AreEqual(InterpPreviewLauncherOutcome.Failed,
            (await h.Runtime.ExecuteLauncherRequestAsync(request, null, h.Host)).Outcome);
        Assert.AreEqual(generation, h.Session.SceneGeneration);
        Assert.AreSame(female, h.Session.FindActorsByLookup("player").Single());
        Assert.AreEqual(InterpPreviewPlayerVariant.Female, h.Session.CreateSceneSnapshot().LoadedPlayerVariant);
        h.Host.AssertHitRegistration();

        h.Host.ThrowAfterRegister = 0;
        Assert.AreEqual(InterpPreviewLauncherOutcome.Committed,
            (await h.Runtime.ExecuteLauncherRequestAsync(request, null, h.Host)).Outcome);
        Assert.AreEqual(generation + 1, h.Session.SceneGeneration);
        Assert.AreEqual(InterpPreviewPlayerVariant.Male, h.Session.CreateSceneSnapshot().LoadedPlayerVariant);
        Assert.AreEqual(3, h.Session.TotalResourceCount);
        Assert.AreNotSame(female, h.Session.FindActorsByLookup("player").Single());
        Assert.IsTrue(h.Session.Actors.Contains(actorA));
        Assert.AreEqual(1, ((TestActor)female).DisposeCalls);
        Assert.AreEqual(0, h.Packages[1].RefCount);
        h.Host.AssertHitRegistration();
    }

    [TestMethod, Timeout(15000)]
    public async Task FeedbackAndDeferredActivation_RequireCurrentRequestAndOpenLifecycle()
    {
        using var h = new Harness();
        InterpPreviewLauncherResult first = await h.Launch(["C:/tests/A.pcc"]);
        int feedback = 0;
        Assert.IsTrue(await h.Dispatcher.InvokeAsync(() => h.Runtime.TryPublishFeedback(first, () => feedback++)));
        Action deferredActivation = () => h.Runtime.TryPublishFeedback(first, () => feedback++);

        InterpPreviewLauncherResult next = await h.Launch(["C:/tests/B.pcc"]);
        await h.Dispatcher.InvokeAsync(deferredActivation);
        Assert.AreEqual(1, feedback, "A delayed activation from A must not run after B was accepted.");
        await h.Runtime.ShutdownAsync();
        Assert.IsFalse(await h.Dispatcher.InvokeAsync(() => h.Runtime.TryPublishFeedback(next, () => feedback++)));
        Assert.AreEqual(1, feedback, "Closing must suppress feedback even from the last successful request.");
    }

    [TestMethod, Timeout(15000)]
    public async Task WpfDispatcher_YieldLetsPendingInputRunBeforeNextBatch()
    {
        var finished = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    var adapter = new InterpPreviewWpfDispatcher(dispatcher);
                    var order = new List<string>();
                    Task yielding = adapter.YieldAsync();
                    dispatcher.BeginInvoke(new Action(() => order.Add("input")), System.Windows.Threading.DispatcherPriority.Input);
                    await yielding;
                    order.Add("next batch");
                    finished.TrySetResult(order.ToArray());
                }
                catch (Exception ex) { finished.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Send); }
            }));
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        CollectionAssert.AreEqual(new[] { "input", "next batch" }, await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private sealed class Harness : IDisposable
    {
        public readonly TestDispatcher Dispatcher = new();
        public readonly InterpPreviewSession Session = new();
        public readonly TestPreparer Preparer = new();
        public readonly TestFactory Factory;
        public readonly TestHost Host;
        public readonly TestResolver Resolver = new();
        public readonly InterpPreviewRuntime Runtime;
        public readonly List<MEPackage> Packages = [];
        public readonly List<string> Events = [];
        public int ActorCount = 1;

        public Harness()
        {
            Factory = new TestFactory(Dispatcher, Events);
            Host = new TestHost(Dispatcher, Session);
            Preparer.Prepare = (path, _) => Task.FromResult(CreatePrepared(path));
            var coordinator = new InterpPreviewLoadCoordinator(Preparer,
                new InterpPreviewActorRealizer(Dispatcher, Factory, batchSize: 1), Dispatcher, Session);
            Runtime = new InterpPreviewRuntime(Session, new TestRenderCoordinator(), coordinator, Resolver, Dispatcher);
        }

        public Task<InterpPreviewLauncherResult> Launch(string[] paths, bool resolve = false) => Runtime.ExecuteLauncherRequestAsync(
            new InterpPreviewLauncherRequest(paths) { ResolveDialogue = resolve }, null, Host);

        public InterpPreviewPrepareResult CreatePrepared(string path)
        {
            var package = (MEPackage)MEPackageHandler.CreateMemoryEmptyPackage(path, MEGame.LE3);
            Packages.Add(package);
            package.NoLongerUsed += _ => Events.Add("package:" + System.IO.Path.GetFileNameWithoutExtension(path));
            var descriptors = new List<InterpPreviewActorBuildDescriptor>();
            for (int i = 0; i < ActorCount; i++)
            {
                var export = new ExportEntry(package, 0, "Actor" + i, prePropBinary: new byte[4], properties: []);
                package.AddExport(export);
                descriptors.Add(new InterpPreviewActorBuildDescriptor(InterpPreviewActorBuildKind.Actor,
                    export.UIndex, "Actor", 0, -1, default, default, default, default, i));
            }
            return InterpPreviewPrepareResult.Prepared(new InterpPreviewPreparedResource(path, package, descriptors));
        }

        public void Dispose()
        {
            Task shutdown = Runtime.ShutdownAsync();
            Assert.IsTrue(shutdown.IsCompleted, "The test must drain its requests before teardown.");
            Dispatcher.InvokeAsync(() => { Host.ClearScene(); Runtime.Dispose(); }).GetAwaiter().GetResult();
        }

        public async Task<InterpPreviewLoadedLevel> CreatePlayer(InterpPreviewPlayerVariant variant = InterpPreviewPlayerVariant.Female)
        {
            using var candidate = await CreateCandidate("C:/tests/Player.pcc");
            Assert.IsTrue(candidate.TryTransferOwnership(out var package, out var actors));
            return new InterpPreviewLoadedLevel("C:/tests/Player.pcc", package, actors,
                InterpPreviewResourceKind.PlayerContext, variant);
        }

        public async Task<InterpPreviewLoadedLevel> CreateCandidate(string path)
        {
            using var prepared = CreatePrepared(path).PreparedResource;
            return (await new InterpPreviewActorRealizer(Dispatcher, Factory)
                .RealizeAsync(prepared, null, () => true, CancellationToken.None)).LoadedLevel;
        }
    }

    private sealed class TestPreparer : IInterpPreviewPackagePreparer
    {
        public Func<string, CancellationToken, Task<InterpPreviewPrepareResult>> Prepare;
        public Task<InterpPreviewPrepareResult> PrepareAsync(string path, CancellationToken cancellationToken) => Prepare(path, cancellationToken);
    }

    private sealed class TestFactory(TestDispatcher dispatcher, List<string> events) : IInterpPreviewActorProxyFactory
    {
        public List<TestActor> Actors = [];
        public int ThrowOnCreate;
        public bool ThrowOnFirstDispose;
        public int ThrowOnDisposeCall;
        private int _calls;
        public ActorProxy Create(IActorEditorContext context, IMEPackage package, InterpPreviewActorBuildDescriptor descriptor)
        {
            dispatcher.VerifyAccess();
            if (++_calls == ThrowOnCreate) throw new InvalidOperationException("construction failed");
            var actor = new TestActor(package.GetUExport(descriptor.ExportUIndex), dispatcher, events,
                (ThrowOnFirstDispose && _calls == 1) || _calls == ThrowOnDisposeCall);
            Actors.Add(actor);
            return actor;
        }
    }

    private sealed class TestActor(ExportEntry export, TestDispatcher dispatcher, List<string> events, bool throwOnDispose) : ActorProxy(export)
    {
        public int DisposeCalls;
        public Action OnDispose;
        protected override void Dispose(bool disposing)
        {
            dispatcher.VerifyAccess();
            DisposeCalls++;
            OnDispose?.Invoke();
            base.Dispose(disposing);
            events.Add("actor:" + System.IO.Path.GetFileNameWithoutExtension(Export.FileRef.FilePath));
            if (throwOnDispose) throw new InvalidOperationException("disposal failed");
        }
    }

    private sealed class TestHost : IInterpPreviewLauncherHost
    {
        private readonly TestDispatcher _dispatcher;
        private readonly InterpPreviewSession _session;
        private readonly PreviewLevelLightRegistry _lights;
        public readonly LevelEditorRenderContext Context = new(readOnly: true);
        public Func<IMEPackage, List<SceneLight>> LoadLights = _ => [];
        public Func<Task<InterpPreviewLoadedLevel>> PreparePlayer = () => Task.FromResult<InterpPreviewLoadedLevel>(null);
        public List<ActorProxy> Registered => Context.DrawList_3D;
        public int Preparations, Applied, Published, SceneFeedback;
        public int ThrowAfterRegister, ThrowAfterUnregister;
        public Action AfterRegister;
        public bool ThrowOnFeedback;
        public bool ThrowAfterCommit;
        public TestHost(TestDispatcher dispatcher, InterpPreviewSession session)
        {
            _dispatcher = dispatcher;
            _session = session;
            _lights = new PreviewLevelLightRegistry(Context, package => LoadLights(package));
        }
        public Task<InterpPreviewLoadedLevel> PreparePlayerAsync(InterpPreviewLauncherRequest request, Func<bool> canContinue)
        {
            _dispatcher.VerifyAccess();
            Preparations++;
            return PreparePlayer();
        }
        public void ApplySceneChange(InterpPreviewOperationResult result, Action commitScene)
        {
            _dispatcher.VerifyAccess();
            int added = 0, removed = 0;
            var registration = new InterpPreviewSceneRegistration(Context, _lights, actor =>
            {
                Context.AddActor(actor);
                AfterRegister?.Invoke();
                if (++added == ThrowAfterRegister) throw new InvalidOperationException("registration failed");
            }, actor =>
            {
                Context.RemoveActor(actor);
                if (++removed == ThrowAfterUnregister) throw new InvalidOperationException("unregistration failed");
            });
            registration.Apply(result, () =>
            {
                commitScene();
                if (ThrowAfterCommit) throw new InvalidOperationException("publication failed");
            });
            Applied++;
        }
        public void PublishSceneChange(InterpPreviewOperationResult result, bool updateCamera)
        {
            _dispatcher.VerifyAccess();
            SceneFeedback++;
            if (ThrowOnFeedback) throw new InvalidOperationException("feedback failed");
        }
        public void PublishDialogue(InterpPreviewDialogueResolution resolution, InterpPreviewLauncherRequest request)
        {
            _dispatcher.VerifyAccess();
            Published++;
        }
        public void ClearScene()
        {
            _dispatcher.VerifyAccess();
            Context.UnloadActors(Registered.ToList());
            _lights.Clear();
            _session.ClearLevels();
        }
        public void AssertHitRegistration()
        {
            // Picking has no graphics-free public query. Inspect the actual sparse registry
            // to catch stale hit proxies that a draw-list-only assertion would miss.
            var field = typeof(LevelEditorRenderContext).GetField("HitProxies",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var hits = (USparseArray<IHitProxy>)field.GetValue(Context);
            CollectionAssert.AreEquivalent(Registered.ToArray(), hits.Cast<ActorProxy>().ToArray());
            foreach (ActorProxy actor in Registered)
            {
                Assert.IsTrue(hits.TryGetAt(actor.HitID, out var hit));
                Assert.AreSame(actor, hit);
            }
        }
    }

    private sealed class TestResolver : IInterpPreviewDialogueResolver
    {
        public int Calls;
        public InterpPreviewDialogueResolution Resolve(InterpPreviewDialogueResolutionRequest request)
        {
            Calls++;
            return InterpPreviewDialogueResolution.Unresolved([]);
        }
    }

    private sealed class TestRenderCoordinator : IInterpPreviewRenderCoordinator
    {
        public void Attach(LevelEditorRenderContext context, IInterpPreviewSession session) { }
        public void Detach(LevelEditorRenderContext context) { }
    }

    private sealed class TestDispatcher : IInterpPreviewDispatcher
    {
        private readonly AsyncLocal<int> _depth = new();
        public readonly TaskCompletionSource<bool> YieldEntered = Signal();
        public bool PauseYield;
        public int Yields, Verifications;
        public bool CheckAccess() => _depth.Value > 0;
        public void VerifyAccess() { Verifications++; Assert.IsTrue(CheckAccess(), "Mutation must use the explicit dispatcher."); }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
            => InvokeAsync(() => { action(); return true; }, cancellationToken);
        public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _depth.Value++;
            try { return Task.FromResult(action()); }
            finally { _depth.Value--; }
        }
        public async Task YieldAsync(CancellationToken cancellationToken = default)
        {
            Yields++;
            YieldEntered.TrySetResult(true);
            if (PauseYield) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
