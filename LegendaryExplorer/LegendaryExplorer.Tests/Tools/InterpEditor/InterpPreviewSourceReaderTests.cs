using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewSourceReaderTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        LegendaryExplorerCoreLib.InitLib(TaskScheduler.Default);
    }

    [TestMethod]
    public void Read_WithValidConversationNodeAndHint_ReturnsReadySnapshot()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("SourceReaderTests.pcc", MEGame.LE3);
        ExportEntry conversationExport = CreateExport(package, "Conversation", "BioConversation");
        ExportEntry sequenceExport = CreateExport(package, "Sequence", "Sequence");
        ExportEntry hintExport = CreateExport(package, "InterpHint", "InterpData");

        var conversation = new ConversationExtended(conversationExport)
        {
            Sequence = sequenceExport
        };

        var node = CreateNode(nodeCount: 5, speakerIndex: 3, listenerIndex: 1, lineStrRef: 7001, isReply: false, nodeExportId: 2222);
        node.InterpData = hintExport;

        AttachNode(conversation, node);
        var reader = new InterpPreviewSourceReader();
        InterpPreviewSourceReadResult result = reader.Read(new InterpPreviewSourceReadRequest(conversation, node));

        Assert.AreEqual(InterpPreviewSourceReadOutcome.Ready, result.Outcome);
        Assert.IsNotNull(result.Snapshot);
        Assert.IsTrue(result.Snapshot.NodeId.HasValue);

        InterpPreviewSourceObjectId expectedConversationId = new(conversationExport.Game, conversationExport.FileRef.FilePath, conversationExport.UIndex);
        InterpPreviewSourceObjectId expectedSequenceId = new(sequenceExport.Game, sequenceExport.FileRef.FilePath, sequenceExport.UIndex);
        InterpPreviewSourceObjectId expectedHintId = new(hintExport.Game, hintExport.FileRef.FilePath, hintExport.UIndex);

        Assert.AreEqual(expectedConversationId, result.Snapshot.ConversationId);
        Assert.AreEqual(expectedSequenceId, result.Snapshot.SequenceId.Value);
        Assert.AreEqual(expectedHintId, result.Snapshot.InterpDataHintId.Value);
        Assert.AreEqual(5, result.Snapshot.NodeMetadata.NodeIndex);
        Assert.AreEqual(2222, result.Snapshot.NodeMetadata.NodeExportId);
        Assert.AreEqual(3, result.Snapshot.NodeMetadata.SpeakerIndex);
        Assert.AreEqual(1, result.Snapshot.NodeMetadata.ListenerIndex);
        Assert.AreEqual(7001, result.Snapshot.NodeMetadata.LineStrRef);
    }

    [TestMethod]
    public void Read_WhenNodeExportIdMissing_ReturnsPartialAndDiagnostic()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("SourceReaderTests.pcc", MEGame.LE2);
        ExportEntry conversationExport = CreateExport(package, "Conversation", "BioConversation");

        var conversation = new ConversationExtended(conversationExport);
        var node = CreateNode(nodeCount: 1, speakerIndex: -2, listenerIndex: 0, lineStrRef: 100, isReply: true, nodeExportId: 0);
        node.ExportID = 0;

        AttachNode(conversation, node);
        var reader = new InterpPreviewSourceReader();
        InterpPreviewSourceReadResult result = reader.Read(new InterpPreviewSourceReadRequest(conversation, node));

        Assert.AreEqual(InterpPreviewSourceReadOutcome.Partial, result.Outcome);
        Assert.IsNotNull(result.Snapshot);
        Assert.IsFalse(result.Snapshot.NodeId.HasValue);
        Assert.IsTrue(HasDiagnosticCode(result, "SOURCE_NODE_EXPORTID_MISSING"));
    }

    [TestMethod]
    public void Read_UsesExportIdFallbackWhenNodePropMissing()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("SourceReaderTests.pcc", MEGame.LE1);
        ExportEntry conversationExport = CreateExport(package, "Conversation", "BioConversation");

        var conversation = new ConversationExtended(conversationExport);
        var node = new DialogueNodeExtended(
            nodeProp: null,
            isReply: false,
            nodeCount: 3,
            speakerIndex: 4,
            lineStrRef: 555,
            line: "line",
            firesConditional: false,
            conditionalOrBool: 0,
            transition: 0,
            replyType: EReplyTypes.REPLY_STANDARD)
        {
            ExportID = 4567,
            Listener = 8
        };

        AttachNode(conversation, node);
        var reader = new InterpPreviewSourceReader();
        InterpPreviewSourceReadResult result = reader.Read(new InterpPreviewSourceReadRequest(conversation, node));

        Assert.AreEqual(InterpPreviewSourceReadOutcome.Partial, result.Outcome);
        Assert.IsTrue(result.Snapshot.NodeId.HasValue);
        Assert.AreEqual(4567, result.Snapshot.NodeId.Value.NodeExportId);
    }

    [TestMethod]
    public void Read_CopiesNodeMetadataValues_WithoutLiveReference()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("SourceReaderTests.pcc", MEGame.LE3);
        ExportEntry conversationExport = CreateExport(package, "Conversation", "BioConversation");
        var conversation = new ConversationExtended(conversationExport);
        var node = CreateNode(nodeCount: 7, speakerIndex: 11, listenerIndex: 2, lineStrRef: 321, isReply: false, nodeExportId: 91);

        AttachNode(conversation, node);
        var reader = new InterpPreviewSourceReader();
        InterpPreviewSourceReadResult result = reader.Read(new InterpPreviewSourceReadRequest(conversation, node));

        node.NodeCount = 99;
        node.SpeakerIndex = 100;
        node.Listener = 101;

        Assert.AreEqual(7, result.Snapshot.NodeMetadata.NodeIndex);
        Assert.AreEqual(11, result.Snapshot.NodeMetadata.SpeakerIndex);
        Assert.AreEqual(2, result.Snapshot.NodeMetadata.ListenerIndex);
    }

    [TestMethod]
    public void Read_WhenRequestMissing_ReturnsUnresolved()
    {
        var reader = new InterpPreviewSourceReader();

        InterpPreviewSourceReadResult result = reader.Read(request: null);

        Assert.AreEqual(InterpPreviewSourceReadOutcome.Unresolved, result.Outcome);
        Assert.IsTrue(HasDiagnosticCode(result, "SOURCE_REQUEST_NULL"));
    }

    private static DialogueNodeExtended CreateNode(int nodeCount, int speakerIndex, int listenerIndex, int lineStrRef, bool isReply, int nodeExportId)
    {
        var nodeProp = new StructProperty(
            "BioDialogNode",
            isImmutable: false,
            new IntProperty(nodeExportId, "nExportID"));

        var node = new DialogueNodeExtended(
            nodeProp,
            isReply,
            nodeCount,
            speakerIndex,
            lineStrRef,
            line: "line",
            firesConditional: false,
            conditionalOrBool: 0,
            transition: 0,
            replyType: EReplyTypes.REPLY_STANDARD)
        {
            Listener = listenerIndex,
            ExportID = nodeExportId
        };

        return node;
    }

    private static void AttachNode(ConversationExtended conversation, DialogueNodeExtended node)
    {
        var nodes = node.IsReply ? conversation.ReplyList : conversation.EntryList;
        while (nodes.Count < node.NodeCount)
            nodes.Add(CreateNode(nodes.Count, 0, 0, 0, node.IsReply, nodes.Count + 1));
        nodes.Add(node);
    }

    private static ExportEntry CreateExport(IMEPackage package, string name, string className)
    {
        ImportEntry classImport = package.Imports.FirstOrDefault(import =>
            import.ClassName == "Class" && import.ObjectName.Instanced == className);
        if (classImport is null)
        {
            classImport = new ImportEntry(package) { ObjectName = className, ClassName = "Class", PackageFile = "Engine" };
            package.AddImport(classImport);
        }

        var export = new ExportEntry(package, 0, name, prePropBinary: new byte[4], properties: []);
        export.Class = classImport;
        package.AddExport(export);
        Assert.AreEqual(className, export.ClassName, "Fixture exports must use the requested Unreal class.");
        return export;
    }

    private static bool HasDiagnosticCode(InterpPreviewSourceReadResult result, string code)
    {
        foreach (InterpPreviewDiagnostic diagnostic in result.Diagnostics)
        {
            if (string.Equals(diagnostic.Code, code, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
