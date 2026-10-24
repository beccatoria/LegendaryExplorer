using System;
using System.Collections.Generic;
using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorerCore.Packages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewNodeDiscoveryTests
{
    [TestMethod]
    public void Discover_FindsInterpDataFromMatchingRoot()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 5001, hintId: null);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 10, nodeId: 5001, [new InterpPreviewOutputLinkSnapshot(0, "Out", 20, 0)]),
            CreateActionNode(sequenceId, uindex: 20, [new InterpPreviewVariableLinkSnapshot("Data", [30])]),
            CreateNode(sequenceId, uindex: 30, className: "InterpData")
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Candidates.Count);
        Assert.IsFalse(result.Candidates[0].IsHintOnly);
        Assert.AreEqual(20, result.Candidates[0].ActionId?.ExportUIndex);
        Assert.AreEqual(30, result.Candidates[0].InterpDataId.ExportUIndex);
    }

    [TestMethod]
    public void Discover_CollectsMultipleDataLinks_AndKeepsValidSiblings()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 88, hintId: null);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 10, nodeId: 88, [new InterpPreviewOutputLinkSnapshot(0, "Out", 20, 0)]),
            CreateActionNode(sequenceId, uindex: 20, [new InterpPreviewVariableLinkSnapshot("Data", [30, -1, 999, 31])]),
            CreateNode(sequenceId, uindex: 30, className: "InterpData"),
            CreateNode(sequenceId, uindex: 31, className: "InterpData")
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Ready, result.Outcome);
        Assert.AreEqual(2, result.Candidates.Count);
        Assert.IsTrue(HasDiagnosticCode(result, "DISCOVERY_DATA_LINK_INVALID"));
        Assert.IsTrue(HasDiagnosticCode(result, "DISCOVERY_DATA_LINK_MISSING"));
    }

    [TestMethod]
    public void Discover_StopsTraversalAtDifferentConversationNode()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 55, hintId: null);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 10, nodeId: 55, [new InterpPreviewOutputLinkSnapshot(0, "Out", 15, 0)]),
            CreateConvNode(sequenceId, uindex: 15, nodeId: 66, [new InterpPreviewOutputLinkSnapshot(0, "Out", 20, 0)]),
            CreateActionNode(sequenceId, uindex: 20, [new InterpPreviewVariableLinkSnapshot("Data", [30])]),
            CreateNode(sequenceId, uindex: 30, className: "InterpData")
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Unresolved, result.Outcome);
        Assert.AreEqual(0, result.Candidates.Count);
        Assert.IsTrue(HasDiagnosticCode(result, "DISCOVERY_STOPPED_AT_OTHER_NODE"));
    }

    [TestMethod]
    public void Discover_WhenGraphDoesNotResolve_UsesHintOnlyPartial()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceObjectId hintId = CreateId(99);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 777, hintId);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 10, nodeId: 1, [])
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Partial, result.Outcome);
        Assert.AreEqual(1, result.Candidates.Count);
        Assert.IsTrue(result.Candidates[0].IsHintOnly);
        Assert.AreEqual(hintId, result.Candidates[0].InterpDataId);
        Assert.IsTrue(HasDiagnosticCode(result, "INTERP_HINT_ONLY"));
    }

    [TestMethod]
    public void Discover_ReportsDuplicateMatchingRoots()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 42, hintId: null);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 10, nodeId: 42, [new InterpPreviewOutputLinkSnapshot(0, "Out", 20, 0)]),
            CreateConvNode(sequenceId, uindex: 11, nodeId: 42, [new InterpPreviewOutputLinkSnapshot(0, "Out", 21, 0)]),
            CreateActionNode(sequenceId, uindex: 20, [new InterpPreviewVariableLinkSnapshot("Data", [30])]),
            CreateActionNode(sequenceId, uindex: 21, [new InterpPreviewVariableLinkSnapshot("Data", [31])]),
            CreateNode(sequenceId, uindex: 30, className: "InterpData"),
            CreateNode(sequenceId, uindex: 31, className: "InterpData")
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Ready, result.Outcome);
        Assert.AreEqual(2, result.Candidates.Count);
        Assert.IsTrue(HasDiagnosticCode(result, "DISCOVERY_DUPLICATE_ROOTS"));
        Assert.AreEqual(20, result.Candidates[0].ActionId?.ExportUIndex);
        Assert.AreEqual(21, result.Candidates[1].ActionId?.ExportUIndex);
    }

    [TestMethod]
    public void Discover_StopsAtObjectLimit_ReturnsPartialAndKeepsCollectedCandidates()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 300, hintId: null);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 10, nodeId: 300, [new InterpPreviewOutputLinkSnapshot(0, "Out", 20, 0)]),
            CreateActionNode(sequenceId, uindex: 20,
            [
                new InterpPreviewVariableLinkSnapshot("Data", [30]),
                new InterpPreviewVariableLinkSnapshot("Unused", [])
            ],
            [new InterpPreviewOutputLinkSnapshot(0, "Next", 40, 0)]),
            CreateNode(sequenceId, uindex: 30, className: "InterpData"),
            CreateActionNode(sequenceId, uindex: 40,
            [
                new InterpPreviewVariableLinkSnapshot("Data", [50])
            ],
            [new InterpPreviewOutputLinkSnapshot(0, "Next", 60, 0)]),
            CreateNode(sequenceId, uindex: 50, className: "InterpData"),
            CreateNode(sequenceId, uindex: 60, className: "SeqAct_Log")
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph, maxVisitedObjects: 3, maxVisitedEdges: 50));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Partial, result.Outcome);
        Assert.AreEqual(2, result.Candidates.Count);
        Assert.AreEqual(20, result.Candidates[0].ActionId?.ExportUIndex);
        Assert.AreEqual(40, result.Candidates[1].ActionId?.ExportUIndex);
        Assert.IsTrue(HasDiagnosticCode(result, "DISCOVERY_OBJECT_LIMIT"));
    }

    [TestMethod]
    public void Discover_StopsAtEdgeLimit_ReturnsPartial()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 301, hintId: null);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 10, nodeId: 301,
            [
                new InterpPreviewOutputLinkSnapshot(0, "OutA", 20, 0),
                new InterpPreviewOutputLinkSnapshot(1, "OutB", 21, 0)
            ]),
            CreateActionNode(sequenceId, uindex: 20, [new InterpPreviewVariableLinkSnapshot("Data", [30])]),
            CreateActionNode(sequenceId, uindex: 21, [new InterpPreviewVariableLinkSnapshot("Data", [31])]),
            CreateNode(sequenceId, uindex: 30, className: "InterpData"),
            CreateNode(sequenceId, uindex: 31, className: "InterpData")
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph, maxVisitedObjects: 50, maxVisitedEdges: 1));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Partial, result.Outcome);
        Assert.AreEqual(0, result.Candidates.Count);
        Assert.IsTrue(HasDiagnosticCode(result, "DISCOVERY_EDGE_LIMIT"));
    }

    [TestMethod]
    public void Discover_ReportsCycleAndTerminates()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 302, hintId: null);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 10, nodeId: 302, [new InterpPreviewOutputLinkSnapshot(0, "Out", 20, 0)]),
            CreateActionNode(sequenceId, uindex: 20, [new InterpPreviewVariableLinkSnapshot("Data", [30])], [new InterpPreviewOutputLinkSnapshot(0, "Loop", 10, 0)]),
            CreateNode(sequenceId, uindex: 30, className: "InterpData")
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Candidates.Count);
        Assert.IsTrue(HasDiagnosticCode(result, "DISCOVERY_CYCLE"));
    }

    [TestMethod]
    public void Discover_DeduplicatesIdenticalDataLinkCandidates()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 303, hintId: null);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 10, nodeId: 303, [new InterpPreviewOutputLinkSnapshot(0, "Out", 20, 0)]),
            CreateActionNode(sequenceId, uindex: 20,
            [
                new InterpPreviewVariableLinkSnapshot("Data", [30, 30])
            ]),
            CreateNode(sequenceId, uindex: 30, className: "InterpData")
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Ready, result.Outcome);
        Assert.AreEqual(1, result.Candidates.Count);
        Assert.AreEqual(2, result.Candidates[0].Evidence.Count);
    }

    [TestMethod]
    public void Discover_ProducesDeterministicCandidateOrderAcrossRootsAndDataIds()
    {
        InterpPreviewSourceObjectId conversationId = CreateId(1);
        InterpPreviewSourceObjectId sequenceId = CreateId(2);
        InterpPreviewSourceSnapshot source = CreateSourceSnapshot(conversationId, sequenceId, nodeExportId: 304, hintId: null);

        var graph = new InterpPreviewSequenceGraphSnapshot(sequenceId,
        [
            CreateConvNode(sequenceId, uindex: 11, nodeId: 304, [new InterpPreviewOutputLinkSnapshot(0, "Out", 21, 0)]),
            CreateConvNode(sequenceId, uindex: 10, nodeId: 304, [new InterpPreviewOutputLinkSnapshot(0, "Out", 20, 0)]),
            CreateActionNode(sequenceId, uindex: 21, [new InterpPreviewVariableLinkSnapshot("Data", [31])]),
            CreateActionNode(sequenceId, uindex: 20, [new InterpPreviewVariableLinkSnapshot("Data", [30])]),
            CreateNode(sequenceId, uindex: 31, className: "InterpData"),
            CreateNode(sequenceId, uindex: 30, className: "InterpData")
        ]);

        var discovery = new InterpPreviewNodeDiscovery();
        InterpPreviewNodeDiscoveryResult result = discovery.Discover(new InterpPreviewNodeDiscoveryRequest(source, graph));

        Assert.AreEqual(InterpPreviewNodeDiscoveryOutcome.Ready, result.Outcome);
        Assert.AreEqual(2, result.Candidates.Count);
        Assert.AreEqual(20, result.Candidates[0].ActionId?.ExportUIndex);
        Assert.AreEqual(30, result.Candidates[0].InterpDataId.ExportUIndex);
        Assert.AreEqual(21, result.Candidates[1].ActionId?.ExportUIndex);
        Assert.AreEqual(31, result.Candidates[1].InterpDataId.ExportUIndex);
    }

    private static InterpPreviewSourceSnapshot CreateSourceSnapshot(
        InterpPreviewSourceObjectId conversationId,
        InterpPreviewSourceObjectId sequenceId,
        int nodeExportId,
        InterpPreviewSourceObjectId? hintId)
    {
        InterpPreviewNodeMetadata metadata = new(
            isReply: false,
            nodeIndex: 0,
            nodeExportId: nodeExportId,
            speakerIndex: 0,
            listenerIndex: 0,
            lineStrRef: 0);

        InterpPreviewNodeId nodeId = new(conversationId, isReply: false, nodeIndex: 0, nodeExportId: nodeExportId);
        return new InterpPreviewSourceSnapshot(conversationId, nodeId, sequenceId, hintId, metadata);
    }

    private static InterpPreviewSequenceNodeSnapshot CreateConvNode(
        InterpPreviewSourceObjectId sequenceId,
        int uindex,
        int nodeId,
        IReadOnlyList<InterpPreviewOutputLinkSnapshot> outputs)
    {
        return new InterpPreviewSequenceNodeSnapshot(
            CreateSequenceNodeId(sequenceId, uindex),
            "BioSeqEvt_ConvNode",
            nodeId,
            outputs,
            []);
    }

    private static InterpPreviewSequenceNodeSnapshot CreateActionNode(
        InterpPreviewSourceObjectId sequenceId,
        int uindex,
        IReadOnlyList<InterpPreviewVariableLinkSnapshot> variableLinks,
        IReadOnlyList<InterpPreviewOutputLinkSnapshot> outputs = null)
    {
        return new InterpPreviewSequenceNodeSnapshot(
            CreateSequenceNodeId(sequenceId, uindex),
            "SeqAct_Interp",
            null,
            outputs ?? [],
            variableLinks);
    }

    private static InterpPreviewSequenceNodeSnapshot CreateNode(
        InterpPreviewSourceObjectId sequenceId,
        int uindex,
        string className)
    {
        return new InterpPreviewSequenceNodeSnapshot(
            CreateSequenceNodeId(sequenceId, uindex),
            className,
            null,
            [],
            []);
    }

    private static InterpPreviewSourceObjectId CreateSequenceNodeId(InterpPreviewSourceObjectId sequenceId, int uindex)
    {
        return new InterpPreviewSourceObjectId(sequenceId.Game, sequenceId.PackagePath, uindex);
    }

    private static InterpPreviewSourceObjectId CreateId(int uindex)
    {
        return new InterpPreviewSourceObjectId(MEGame.LE3, "C:/tests/InterpPreviewNodeDiscovery.pcc", uindex);
    }

    private static bool HasDiagnosticCode(InterpPreviewNodeDiscoveryResult result, string code)
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
