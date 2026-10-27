using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorer.Tools.LevelEditor;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewDiscoveryIntegrationTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _) => LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);

    [TestMethod]
    public void RealProperties_SequentialActionsUsingSameData_ProduceSeparateDetachedBoundPlans()
    {
        using var f = new Fixture();
        var data = f.Data("Shared");
        var first = f.Action("First", data, "GroupA");
        var second = f.Action("Second", data, "GroupB");
        Outputs(f.Root, (0, first.UIndex));
        Outputs(first, (0, second.UIndex));
        f.Node.ExportID = 999; // stale cache must neither win over nExportID nor be rewritten.

        var result = f.Resolve();

        Assert.AreEqual(InterpPreviewResolutionOutcome.Ready, result.Outcome);
        Assert.HasCount(2, result.Plans);
        Assert.AreEqual(result.Plans[0].InterpDataId, result.Plans[1].InterpDataId);
        Assert.AreNotEqual(result.Plans[0].ActionId, result.Plans[1].ActionId);
        Assert.IsTrue(result.Plans[0].ActionLinks.Any(link => link.LinkDesc == "GroupA"));
        Assert.IsTrue(result.Plans[1].ActionLinks.Any(link => link.LinkDesc == "GroupB"));
        Assert.IsTrue(result.Plans.All(plan => plan.Bindings.Bindings.Single().Status == InterpPreviewActorBindingStatus.Bound));
        Assert.AreEqual(999, f.Node.ExportID);
        Assert.AreEqual(77, f.Node.NodeProp.GetProp<IntProperty>("nExportID").Value);
        Assert.AreEqual(41L, result.Stamp.RequestId);
        Assert.AreEqual(f.Scene.Stamp, result.Stamp.SceneStamp);
        Assert.AreEqual(InterpPreviewPlayerVariant.Female, result.Stamp.RequestedPlayerVariant);
        Assert.IsFalse(result.Diagnostics.Any(d => d.Code is "INTERP_FALLBACK" or "INTERP_RESOLVE_EXCEPTION"));
        AssertDetached(result);
    }

    [TestMethod]
    public void RealProperties_AlternativeConnectors_KeepBothPlansAndRequireSelection()
    {
        using var f = new Fixture();
        var first = f.Action("First", f.Data("A"));
        var second = f.Action("Second", f.Data("B"));
        Outputs(f.Root, (0, first.UIndex), (1, second.UIndex));
        var result = f.Resolve();
        Assert.AreEqual(InterpPreviewResolutionOutcome.NeedsSelection, result.Outcome);
        Assert.HasCount(2, result.Plans);
        Assert.HasCount(2, result.Discovery.Edges);
        Assert.IsTrue(result.Discovery.RequiresSelection);
    }

    [TestMethod]
    public void RealProperties_UnsavedExternalSequence_IsReadFromItsBorrowedPackageAndSurvivesEditorClosure()
    {
        using var f = new Fixture(externalSequence: true);
        var action = f.Action("Action", f.Data("ExternalData"));
        Outputs(f.Root, (0, action.UIndex));
        var result = f.Resolve();
        Assert.AreEqual(InterpPreviewResolutionOutcome.Ready, result.Outcome);
        Assert.AreEqual(Path.GetFullPath(f.SequencePackage.FilePath), result.Plans.Single().InterpDataId.PackagePath);
        Assert.AreEqual(1, ((MEPackage)f.Package).RefCount);
        Assert.AreEqual(1, ((MEPackage)f.SequencePackage).RefCount, "Borrowed unsaved packages must not be disposed or reopened.");
        f.Dispose();
        Assert.AreEqual("focus", result.Plans.Single().Data.Groups.Single().Tracks.Single().FindActorTag);
        Assert.AreEqual(InterpPreviewActorBindingStatus.Bound, result.Plans.Single().Bindings.Bindings.Single().Status);
        AssertDetached(result);
    }

    [TestMethod]
    public void RealProperties_EditsAreRereadAndOldSnapshotRemainsUnchanged()
    {
        using var f = new Fixture();
        var data = f.Data("Data");
        Outputs(f.Root, (0, f.Action("Action", data).UIndex));
        var before = f.Resolve();
        f.LastTrack.WriteProperty(new NameProperty("changed_unsaved", "m_nmFindActor"));
        var after = f.Resolve();
        Assert.AreEqual("focus", before.Plans.Single().Data.Groups.Single().Tracks.Single().FindActorTag);
        Assert.AreEqual("changed_unsaved", after.Plans.Single().Data.Groups.Single().Tracks.Single().FindActorTag);
        Assert.AreNotEqual(before.Stamp.SourceSnapshotId, after.Stamp.SourceSnapshotId);
        Assert.AreEqual(InterpPreviewResolutionOutcome.Partial, after.Outcome);
    }

    [TestMethod]
    public void HintConflict_DoesNotBorrowActionLinksOrAppendUnrelatedData()
    {
        using var f = new Fixture();
        var graphData = f.Data("GraphData");
        var hint = f.Data("UnrelatedHint");
        f.Node.InterpData = hint;
        Outputs(f.Root, (0, f.Action("Action", graphData).UIndex));
        var result = f.Resolve();
        Assert.AreEqual(graphData.UIndex, result.Plans.Single().InterpDataId.ExportUIndex);
        Assert.AreSame(hint, f.Node.InterpData);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "INTERP_HINT_CONFLICT"));
        Assert.AreEqual(InterpPreviewResolutionOutcome.Partial, result.Outcome, "A stale hint must remain an explicit limitation.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InvalidHintClassOrGame_IsRejectedWithoutUnrelatedLookup(bool wrongGame)
    {
        using var f = new Fixture();
        using var foreign = MEPackageHandler.CreateMemoryEmptyPackage("WrongGame.pcc", MEGame.LE2);
        f.Node.InterpData = Export(wrongGame ? foreign : f.Package, "WrongHint", wrongGame ? "InterpData" : "SeqAct_Log");
        f.Conversation.Sequence = null;
        var result = f.Resolve();
        Assert.AreEqual(InterpPreviewResolutionOutcome.Unresolved, result.Outcome);
        Assert.IsEmpty(result.Plans);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "SOURCE_INTERP_HINT_INVALID"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NodeMembershipAndArrayIndex_AreValidatedBeforeGraphOrHintUse(bool wrongIndex)
    {
        using var f = new Fixture();
        f.Node.InterpData = f.Data("Hint");
        if (wrongIndex) f.Node.NodeCount = 1;
        else f.Conversation.EntryList.Clear();
        var result = f.Resolve();
        Assert.AreEqual(InterpPreviewResolutionOutcome.Unresolved, result.Outcome);
        Assert.IsEmpty(result.Plans);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "SOURCE_NODE_MEMBERSHIP_INVALID"));
    }

    [TestMethod]
    public void ImportedAndMissingLinks_RemainIdentifiableWhileValidSiblingIsPreserved()
    {
        using var f = new Fixture();
        var data = f.Data("ValidData");
        var action = f.Action("Action", data);
        var import = new ImportEntry(f.Package) { ObjectName = "MissingAction", ClassName = "SeqAct_Log", PackageFile = "External" };
        f.Package.AddImport(import);
        Outputs(f.Root, (0, import.UIndex), (0, action.UIndex));
        Variables(action, "Data", [data.UIndex, import.UIndex, 9000]);
        var result = f.Resolve();
        Assert.AreEqual(InterpPreviewResolutionOutcome.Partial, result.Outcome);
        Assert.HasCount(1, result.Plans);
        Assert.IsTrue(result.UnresolvedReferences.Any(reference => reference.Owner.Equals(InterpPreviewSourceScope.Id(f.Root)) && reference.ReferencedUIndex == import.UIndex));
        Assert.IsTrue(result.UnresolvedReferences.Any(reference => reference.Owner.Equals(InterpPreviewSourceScope.Id(action)) && reference.ReferencedUIndex == import.UIndex));
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "DISCOVERY_DATA_LINK_MISSING"));
    }

    [TestMethod]
    public void DataWithBrokenGroupReference_IsPartialAndRetainsTheUnresolvedOrigin()
    {
        using var f = new Fixture();
        var data = f.Data("Data");
        var groups = data.GetProperty<ArrayProperty<ObjectProperty>>("InterpGroups");
        groups.Add(new ObjectProperty(-42));
        data.WriteProperty(groups);
        Outputs(f.Root, (0, f.Action("Action", data).UIndex));
        var result = f.Resolve();
        Assert.AreEqual(InterpPreviewResolutionOutcome.Partial, result.Outcome);
        Assert.HasCount(1, result.Plans.Single().Data.Groups);
        Assert.IsTrue(result.UnresolvedReferences.Any(reference => reference.Owner.Equals(InterpPreviewSourceScope.Id(data)) && reference.Property == "InterpGroups" && reference.ReferencedUIndex == -42));
    }

    [TestMethod]
    public void SnapshotsAndNestedCollections_RejectMutationIncludingDictionaryDowncasts()
    {
        using var f = new Fixture();
        Outputs(f.Root, (0, f.Action("Action", f.Data("Data")).UIndex));
        var result = f.Resolve();
        var plan = result.Plans.Single();
        RejectWrite(result.Plans);
        RejectWrite(result.Input.Graph.Nodes);
        RejectWrite(result.Input.Graph.Nodes[0].OutputLinks);
        RejectWrite(result.Input.Graph.Nodes.Single(node => node.ClassName == "SeqAct_Interp").VariableLinks);
        RejectWrite(result.Input.Graph.Nodes.Single(node => node.ClassName == "SeqAct_Interp").VariableLinks.Single().LinkedNodeExportUIndexes);
        RejectWrite(result.Diagnostics);
        RejectWrite(result.Discovery.Candidates);
        RejectWrite(result.Discovery.Edges);
        RejectWrite(plan.ActionLinks);
        RejectWrite(plan.ActionLinks.Single().LinkedVariables);
        RejectWrite(plan.Discovery.Evidence);
        RejectWrite(plan.Discovery.Evidence.Single().IncomingActionEdges);
        RejectWrite(plan.Data.Groups);
        RejectWrite(plan.Data.Groups.Single().Tracks);
        RejectWrite(plan.Targets.Targets);
        RejectWrite(plan.Bindings.Bindings);
        RejectWrite(plan.Bindings.Bindings.Single().Candidates);
        RejectWrite(f.Scene.Actors);
        RejectWrite(f.Scene.Resources);
        RejectWrite(f.Scene.ActorsByTag["focus"]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<InterpPreviewSceneActorDescriptor>>)f.Scene.ActorsByTag).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<InterpPreviewSourceObjectId, IReadOnlyList<InterpPreviewSceneActorDescriptor>>)f.Scene.ActorsBySourceId).Clear());
        AssertDetached(result);
    }

    [TestMethod]
    public void ResolutionInput_CopiesSpeakerMetadataAndBranchChoicesWithoutRetainingEditorState()
    {
        using var f = new Fixture();
        var first = f.Action("First", f.Data("FirstData"));
        var second = f.Action("Second", f.Data("SecondData"));
        Outputs(f.Root, (0, first.UIndex), (1, second.UIndex));
        var speaker = new SpeakerExtended(4, "hench_mystic");
        f.Conversation.Speakers.Add(speaker);
        var choices = new Dictionary<InterpPreviewSourceObjectId, int> { [InterpPreviewSourceScope.Id(f.Root)] = 1 };
        var request = f.Request(choices: choices);
        var result = new InterpPreviewDialogueResolver().Resolve(request);
        choices.Clear();
        speaker.SpeakerName = "changed";
        Assert.AreEqual("hench_mystic", result.Input.Source.Speakers.Single().Name);
        Assert.HasCount(1, result.Input.BranchChoices);
        Assert.AreEqual(second.UIndex, result.Plans.Single().ActionId.Value.ExportUIndex);
        Assert.AreEqual(InterpPreviewResolutionOutcome.Ready, result.Outcome);
        RejectWrite(result.Input.Source.Speakers);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<InterpPreviewSourceObjectId, int>)result.Input.BranchChoices).Clear());
        AssertDetached(result);
    }

    [TestMethod]
    public void Cancellation_BeforeSourceCaptureReturnsIncompleteWithoutMutatingNode()
    {
        using var f = new Fixture();
        var request = f.Request(new CancellationToken(true));
        var result = new InterpPreviewDialogueResolver().Resolve(request);
        Assert.AreEqual(InterpPreviewResolutionOutcome.Partial, result.Outcome);
        Assert.IsEmpty(result.Plans);
        Assert.AreEqual(77, f.Node.ExportID);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "SOURCE_CANCELLED"));
    }

    [TestMethod]
    public void ConflictingBorrowedPackageVersions_AreReportedInsteadOfSilentlyMixingTheirEdits()
    {
        using var f = new Fixture();
        using var other = MEPackageHandler.CreateMemoryEmptyPackage(f.Package.FilePath, MEGame.LE3);
        f.Conversation.Sequence = Export(other, "DifferentUnsavedSequence", "Sequence");
        f.Node.InterpData = f.Data("CachedHint");
        var result = f.Resolve();
        Assert.AreEqual(InterpPreviewResolutionOutcome.Partial, result.Outcome);
        Assert.IsFalse(result.HasPreviewData);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "SOURCE_PACKAGE_VERSION_CONFLICT"));
        Assert.AreEqual(1, ((MEPackage)f.Package).RefCount);
        Assert.AreEqual(1, ((MEPackage)other).RefCount);
    }

    [TestMethod]
    public void EndCurrentNode_PreventsFollowingActionsFromLeakingIntoSelectedLine()
    {
        using var f = new Fixture();
        var action = f.Action("NextLineAction", f.Data("OtherLineData"));
        var end = Export(f.SequencePackage, "End", "BioSeqAct_EndCurrentConvNode");
        f.Nodes.Add(end);
        Outputs(f.Root, (0, end.UIndex));
        Outputs(end, (0, action.UIndex));
        var result = f.Resolve();
        Assert.AreEqual(InterpPreviewResolutionOutcome.Unresolved, result.Outcome);
        Assert.IsEmpty(result.Plans);
        Assert.HasCount(1, result.Discovery.Edges);
    }

    private static void RejectWrite<T>(IReadOnlyList<T> values)
    {
        Assert.IsNotEmpty(values);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<T>)values)[0] = values[0]);
    }

    [TestMethod]
    [DataRow("read")]
    [DataRow("missing")]
    [DataRow("cancel")]
    public void IndependentlyOpenedSources_AreReleasedOnReadFailureAndCancellation(string mode)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        string path = Path.Combine(Path.GetTempPath(), $"S4OwnedSource_{Guid.NewGuid():N}.pcc");
        using var source = MEPackageHandler.CreateMemoryEmptyPackage(path, MEGame.LE3);
        var data = Export(source, "ExternalData", "InterpData");
        var action = Export(source, "ExternalAction", "SeqAct_Interp");
        Variables(action, "Data", [data.UIndex]);
        source.Save(path);
        MEPackage opened = null;
        int releases = 0;
        try
        {
            var candidates = new[] { new InterpPreviewDiscoveredInterpData(InterpPreviewSourceScope.Id(action),
                mode == "missing" ? new(MEGame.LE3, path, 999) : InterpPreviewSourceScope.Id(data), false, []) };
            var resolver = new InterpPreviewDialogueResolver(new InterpPreviewSourceReader(), new FixedDiscovery(candidates),
                new InterpPreviewTrackTargetReader(), new InterpPreviewActorBindingResolver(), packagePath =>
                {
                    opened = (MEPackage)InterpPreviewPackageOwnership.OpenIndependent(packagePath);
                    Assert.AreEqual(1, opened.RefCount);
                    opened.NoLongerUsed += _ => releases++;
                    if (mode == "cancel") cancellation.Cancel();
                    return opened;
                });
            var result = resolver.Resolve(f.Request(cancellation.Token));
            Assert.IsNotNull(opened);
            Assert.AreEqual(0, opened.RefCount, "The independent read must finish releasing its package before returning.");
            Assert.AreEqual(1, releases, "Cleanup must reach the actual release event exactly once.");
            Assert.AreEqual(1, ((MEPackage)f.Package).RefCount);
            if (mode == "cancel") Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "SOURCE_CANCELLED"));
            else
            {
                Assert.HasCount(1, result.Plans);
                Assert.AreEqual(mode != "missing", result.HasPreviewData);
                if (mode == "missing") Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "SOURCE_OBJECT_UNRESOLVED"));
            }
            AssertDetached(result);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [TestMethod]
    public void DiscoveredActionDataMismatch_DoesNotBorrowLinksForCachedData()
    {
        using var f = new Fixture();
        var actualData = f.Data("Actual");
        var unrelated = f.Data("Unrelated");
        var action = f.Action("Action", actualData);
        f.Node.InterpData = unrelated;
        var targetReader = new CountingTargetReader();
        var resolver = new InterpPreviewDialogueResolver(new InterpPreviewSourceReader(),
            new FixedDiscovery([new(InterpPreviewSourceScope.Id(action), InterpPreviewSourceScope.Id(unrelated), false, [])]),
            targetReader, new InterpPreviewActorBindingResolver());
        var result = resolver.Resolve(f.Request());
        Assert.AreEqual(0, targetReader.Calls);
        Assert.AreEqual(InterpPreviewResolutionOutcome.Partial, result.Outcome);
        Assert.AreEqual(unrelated.UIndex, result.Plans.Single().InterpDataId.ExportUIndex);
        Assert.IsNull(result.Plans.Single().Targets);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "TARGET_ACTION_DATA_MISMATCH"));
    }

    [TestMethod]
    public void OneCandidateReadFailure_DoesNotDiscardCopiedDataOrOtherCandidateBindings()
    {
        using var f = new Fixture();
        var first = f.Action("First", f.Data("FirstData"));
        var second = f.Action("Second", f.Data("SecondData"));
        Outputs(f.Root, (0, first.UIndex));
        Outputs(first, (0, second.UIndex));
        var reader = new CountingTargetReader { ThrowForAction = first.UIndex };
        var resolver = new InterpPreviewDialogueResolver(new InterpPreviewSourceReader(), new InterpPreviewNodeDiscovery(), reader, new InterpPreviewActorBindingResolver());
        var result = resolver.Resolve(f.Request());
        Assert.AreEqual(InterpPreviewResolutionOutcome.Partial, result.Outcome);
        Assert.HasCount(2, result.Plans);
        Assert.IsNotNull(result.Plans[0].Data);
        Assert.IsNull(result.Plans[0].Targets);
        Assert.AreEqual(InterpPreviewActorBindingStatus.Bound, result.Plans[1].Bindings.Bindings.Single().Status);
        Assert.AreEqual(2, reader.Calls);
        Assert.IsTrue(result.Diagnostics.Any(d => d.Code == "SOURCE_CANDIDATE_READ_FAILED"));
    }

    private sealed class FixedDiscovery(IReadOnlyList<InterpPreviewDiscoveredInterpData> candidates) : IInterpPreviewNodeDiscovery
    {
        public InterpPreviewNodeDiscoveryResult Discover(InterpPreviewNodeDiscoveryRequest request) => InterpPreviewNodeDiscoveryResult.Ready(candidates, []);
    }

    private sealed class CountingTargetReader : IInterpPreviewTrackTargetReader
    {
        public int Calls, ThrowForAction;
        public InterpPreviewTrackTargetReadResult ReadTargets(InterpPreviewTrackTargetReadRequest request)
        {
            Calls++;
            if (request.ActionId.ExportUIndex == ThrowForAction) throw new InvalidDataException("Malformed candidate track data.");
            return new InterpPreviewTrackTargetReader().ReadTargets(request);
        }
    }

    private static void AssertDetached(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Inspect(object value)
        {
            if (value is null || value is string || value.GetType().IsPrimitive || !visited.Add(value)) return;
            Assert.IsFalse(value is IEntry or IMEPackage or ActorProxy or Delegate, $"Durable output retained {value.GetType().Name}.");
            if (value is IEnumerable sequence) { foreach (var item in sequence) Inspect(item); return; }
            if (value.GetType().Namespace != typeof(InterpPreviewPlanInstance).Namespace) return;
            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.GetIndexParameters().Length == 0))
                Inspect(property.GetValue(value));
        }
        Inspect(root);
    }

    private static ExportEntry Export(IMEPackage package, string name, string className)
    {
        var classImport = package.Imports.FirstOrDefault(import => import.ClassName == "Class" && import.ObjectName.Instanced == className);
        if (classImport is null)
        {
            classImport = new ImportEntry(package) { ObjectName = className, ClassName = "Class", PackageFile = "Engine" };
            package.AddImport(classImport);
        }
        var export = new ExportEntry(package, 0, name, prePropBinary: new byte[4], properties: []) { Class = classImport };
        package.AddExport(export);
        Assert.AreEqual(className, export.ClassName);
        return export;
    }

    private static void Variables(ExportEntry action, string description, int[] indexes)
        => action.WriteProperty(new ArrayProperty<StructProperty>([new StructProperty("SeqVarLink", new PropertyCollection
        {
            new StrProperty(description, "LinkDesc"),
            new ArrayProperty<ObjectProperty>(indexes.Select(index => new ObjectProperty(index)), "LinkedVariables")
        })], "VariableLinks"));

    private static void Outputs(ExportEntry node, params (int output, int target)[] links)
    {
        var outputs = new List<StructProperty>();
        for (int index = 0; index <= links.Select(link => link.output).DefaultIfEmpty(-1).Max(); index++)
            outputs.Add(new StructProperty("SeqOpOutputLink", new PropertyCollection
            {
                new StrProperty("Connector" + index, "LinkDesc"),
                new ArrayProperty<StructProperty>(links.Where(link => link.output == index).Select(link => new StructProperty("SeqOpOutputInputLink", new PropertyCollection
                {
                    new ObjectProperty(link.target, "LinkedOp"), new IntProperty(0, "InputLinkIdx")
                })), "Links")
            }));
        node.WriteProperty(new ArrayProperty<StructProperty>(outputs, "OutputLinks"));
    }

    private sealed class Fixture : IDisposable
    {
        public readonly IMEPackage Package = MEPackageHandler.CreateMemoryEmptyPackage("S4Conversation.pcc", MEGame.LE3);
        public readonly IMEPackage SequencePackage;
        public readonly ConversationExtended Conversation;
        public readonly DialogueNodeExtended Node;
        public readonly ExportEntry Sequence, Root;
        public readonly List<ExportEntry> Nodes = [];
        public readonly InterpPreviewSceneSnapshot Scene;
        public ExportEntry LastTrack;
        private bool _disposed;

        public Fixture(bool externalSequence = false)
        {
            SequencePackage = externalSequence ? MEPackageHandler.CreateMemoryEmptyPackage("UnsavedExternalSequence.pcc", MEGame.LE3) : Package;
            Conversation = new(Export(Package, "Conversation", "BioConversation"));
            Sequence = Export(SequencePackage, "Sequence", "Sequence");
            Conversation.Sequence = Sequence;
            Root = Export(SequencePackage, "Root", "BioSeqEvt_ConvNode");
            Root.WriteProperty(new IntProperty(77, "m_nNodeID"));
            Nodes.Add(Root);
            Node = new(new StructProperty("BioDialogNode", false, new IntProperty(77, "nExportID")), false, 0, 0, 42, "Line", false, 0, 0, EReplyTypes.REPLY_STANDARD) { ExportID = 77 };
            Conversation.EntryList.Add(Node);
            var key = InterpPreviewResourceKey.ForLevel("C:/tests/Scene.pcc", MEGame.LE3);
            Scene = new(new(Guid.NewGuid(), 5), [new(key, "Scene")], [new(new(key, 42), "focus", "Actor", false)], InterpPreviewPlayerVariant.None);
        }
        public ExportEntry Data(string name)
        {
            var data = Export(SequencePackage, name, "InterpData");
            var group = Export(SequencePackage, name + "Group", "InterpGroup");
            LastTrack = Export(SequencePackage, name + "LookAt", "BioEvtSysTrackLookAt");
            LastTrack.WriteProperty(new NameProperty("focus", "m_nmFindActor"));
            group.WriteProperty(new NameProperty("GroupFallback", "GroupName"));
            group.WriteProperty(new ArrayProperty<ObjectProperty>([new ObjectProperty(LastTrack.UIndex)], "InterpTracks"));
            data.WriteProperty(new ArrayProperty<ObjectProperty>([new ObjectProperty(group.UIndex)], "InterpGroups"));
            return data;
        }
        public ExportEntry Action(string name, ExportEntry data, string extraLink = null)
        {
            var action = Export(SequencePackage, name, "SeqAct_Interp");
            Variables(action, "Data", [data.UIndex]);
            if (extraLink is not null)
            {
                var links = action.GetProperty<ArrayProperty<StructProperty>>("VariableLinks");
                links.Add(new StructProperty("SeqVarLink", new PropertyCollection { new StrProperty(extraLink, "LinkDesc"), new ArrayProperty<ObjectProperty>("LinkedVariables") }));
                action.WriteProperty(links);
            }
            Nodes.Add(action);
            return action;
        }
        public InterpPreviewDialogueResolutionRequest Request(CancellationToken token = default,
            IReadOnlyDictionary<InterpPreviewSourceObjectId, int> choices = null)
        {
            Sequence.WriteProperty(new ArrayProperty<ObjectProperty>(Nodes.Select(node => new ObjectProperty(node.UIndex)), "SequenceObjects"));
            var request = new InterpPreviewDialogueResolutionRequest(Conversation, Node) { CancellationToken = token, RequestId = 41, BranchChoices = choices };
            request.SetSceneSnapshot(Scene);
            return request;
        }
        public InterpPreviewDialogueResolution Resolve() => new InterpPreviewDialogueResolver().Resolve(Request());
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!ReferenceEquals(Package, SequencePackage)) SequencePackage.Dispose();
            Package.Dispose();
        }
    }
}
