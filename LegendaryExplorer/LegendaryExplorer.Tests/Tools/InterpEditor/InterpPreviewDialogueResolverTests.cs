using LegendaryExplorer.Tools.InterpEditor;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LegendaryExplorer.Tests.Tools.InterpEditor;

[TestClass]
public class InterpPreviewDialogueResolverTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        LegendaryExplorerCoreLib.InitLib(System.Threading.Tasks.TaskScheduler.Default);
    }

    [TestMethod]
    public void Resolve_WhenDiscoveryFindsInterpData_PrefersDiscoveryOverDirectCompatibility()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("ResolverIntegration.pcc", MEGame.LE3);
        ExportEntry conversationExport = CreateExport(package, "Conversation", "BioConversation");
        ExportEntry actionExport = CreateExport(package, "InterpAction", "SeqAct_Interp");
        ExportEntry directInterpData = CreateExport(package, "DirectData", "InterpData");
        ExportEntry discoveredInterpData = CreateExport(package, "DiscoveredData", "InterpData");

        var conversation = new ConversationExtended(conversationExport);
        var node = CreateNode(nodeExportId: 111, isReply: false);
        node.InterpData = directInterpData;

        InterpPreviewSourceObjectId conversationId = new(conversationExport.Game, conversationExport.FileRef.FilePath, conversationExport.UIndex);
        var sourceSnapshot = new InterpPreviewSourceSnapshot(
            conversationId,
            new InterpPreviewNodeId(conversationId, isReply: false, nodeIndex: 0, nodeExportId: 111),
            sequenceId: null,
            interpDataHintId: null,
            new InterpPreviewNodeMetadata(false, 0, 111, 0, 0, 0));

        var sourceReader = new StubSourceReader(InterpPreviewSourceReadResult.Ready(sourceSnapshot,
        [
            new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Info, "PIPE_SOURCE_OK", "Source read succeeded.")
        ]));
        var discovery = new StubNodeDiscovery(InterpPreviewNodeDiscoveryResult.Ready(
            [
                new InterpPreviewDiscoveredInterpData(
                    new InterpPreviewSourceObjectId(actionExport.Game, actionExport.FileRef.FilePath, actionExport.UIndex),
                    new InterpPreviewSourceObjectId(discoveredInterpData.Game, discoveredInterpData.FileRef.FilePath, discoveredInterpData.UIndex),
                    isHintOnly: false,
                    evidence: [])
            ],
            []));
        var targetReader = new StubTrackTargetReader(InterpPreviewTrackTargetReadResult.Unresolved([]));
        var bindingResolver = new StubActorBindingResolver(InterpPreviewActorBindingResult.Unresolved([]));

        var resolver = new InterpPreviewDialogueResolver(sourceReader, discovery, targetReader, bindingResolver);
        InterpPreviewDialogueResolution result = resolver.Resolve(new InterpPreviewDialogueResolutionRequest(conversation, node));

        Assert.IsTrue(result.IsResolved);
        Assert.AreEqual(directInterpData, result.InterpData);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, "INTERP_DIRECT"));
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, "PIPE_SOURCE_OK"));
    }

    [TestMethod]
    public void Resolve_WhenDiscoveredActionCannotBeResolved_AddsTargetActionUnresolvedDiagnostic()
    {
        using var package = MEPackageHandler.CreateMemoryEmptyPackage("ResolverIntegration.pcc", MEGame.LE3);
        ExportEntry conversationExport = CreateExport(package, "Conversation", "BioConversation");
        ExportEntry actionExport = CreateExport(package, "InterpAction", "SeqAct_Interp");
        ExportEntry discoveredInterpData = CreateExport(package, "DiscoveredData", "InterpData");
        ExportEntry directInterpData = CreateExport(package, "DirectData", "InterpData");

        var conversation = new ConversationExtended(conversationExport);
        var node = CreateNode(nodeExportId: 222, isReply: true);
        node.InterpData = directInterpData;

        InterpPreviewSourceObjectId conversationId = new(conversationExport.Game, conversationExport.FileRef.FilePath, conversationExport.UIndex);
        var sourceSnapshot = new InterpPreviewSourceSnapshot(
            conversationId,
            new InterpPreviewNodeId(conversationId, isReply: true, nodeIndex: 1, nodeExportId: 222),
            sequenceId: null,
            interpDataHintId: null,
            new InterpPreviewNodeMetadata(true, 1, 222, 0, 0, 0));

        var sourceReader = new StubSourceReader(InterpPreviewSourceReadResult.Ready(sourceSnapshot, []));
        var discovery = new StubNodeDiscovery(InterpPreviewNodeDiscoveryResult.Ready(
            [
                new InterpPreviewDiscoveredInterpData(
                    new InterpPreviewSourceObjectId(actionExport.Game, "C:/different/package/path.pcc", actionExport.UIndex),
                    new InterpPreviewSourceObjectId(discoveredInterpData.Game, discoveredInterpData.FileRef.FilePath, discoveredInterpData.UIndex),
                    isHintOnly: false,
                    evidence: [])
            ],
            []));

        var targetReader = new StubTrackTargetReader(InterpPreviewTrackTargetReadResult.Unresolved([]));

        var bindingResolver = new StubActorBindingResolver(InterpPreviewActorBindingResult.Unresolved([]));

        var resolver = new InterpPreviewDialogueResolver(sourceReader, discovery, targetReader, bindingResolver);
        InterpPreviewDialogueResolution result = resolver.Resolve(new InterpPreviewDialogueResolutionRequest(conversation, node));

        Assert.IsTrue(result.IsResolved);
        Assert.AreEqual(directInterpData, result.InterpData);
        Assert.IsTrue(HasDiagnosticCode(result.Diagnostics, "TARGET_ACTION_UNRESOLVED"));
    }

    [TestMethod]
    public void Resolve_WhenDiscoveredActionIsInExternalPackage_ResolvesActionLinksWithoutUnresolvedDiagnostic()
    {
        using var conversationPackage = MEPackageHandler.CreateMemoryEmptyPackage("ResolverIntegration.pcc", MEGame.LE3);
        ExportEntry conversationExport = CreateExport(conversationPackage, "Conversation", "BioConversation");
        ExportEntry discoveredInterpData = CreateExport(conversationPackage, "DiscoveredData", "InterpData");
        ExportEntry directInterpData = CreateExport(conversationPackage, "DirectData", "InterpData");

        string tempPackagePath = Path.Combine(Path.GetTempPath(), $"InterpPreviewExternalAction_{Guid.NewGuid():N}.pcc");
        using var externalActionPackage = MEPackageHandler.CreateMemoryEmptyPackage(tempPackagePath, MEGame.LE3);
        ExportEntry externalAction = CreateExport(externalActionPackage, "InterpActionExternal", "SeqAct_Interp");
        externalActionPackage.Save(tempPackagePath);

        try
        {
            var conversation = new ConversationExtended(conversationExport);
            var node = CreateNode(nodeExportId: 333, isReply: false);
            node.InterpData = directInterpData;

            InterpPreviewSourceObjectId conversationId = new(conversationExport.Game, conversationExport.FileRef.FilePath, conversationExport.UIndex);
            var sourceSnapshot = new InterpPreviewSourceSnapshot(
                conversationId,
                new InterpPreviewNodeId(conversationId, isReply: false, nodeIndex: 2, nodeExportId: 333),
                sequenceId: null,
                interpDataHintId: null,
                new InterpPreviewNodeMetadata(false, 2, 333, 0, 0, 0));

            var sourceReader = new StubSourceReader(InterpPreviewSourceReadResult.Ready(sourceSnapshot, []));
            var discovery = new StubNodeDiscovery(InterpPreviewNodeDiscoveryResult.Ready(
            [
                new InterpPreviewDiscoveredInterpData(
                    new InterpPreviewSourceObjectId(externalAction.Game, tempPackagePath, externalAction.UIndex),
                    new InterpPreviewSourceObjectId(discoveredInterpData.Game, discoveredInterpData.FileRef.FilePath, discoveredInterpData.UIndex),
                    isHintOnly: false,
                    evidence: [])
            ],
            []));

            var targetReader = new CapturingTrackTargetReader(InterpPreviewTrackTargetReadResult.Unresolved([]));
            var bindingResolver = new StubActorBindingResolver(InterpPreviewActorBindingResult.Unresolved([]));

            var resolver = new InterpPreviewDialogueResolver(sourceReader, discovery, targetReader, bindingResolver);
            InterpPreviewDialogueResolution result = resolver.Resolve(new InterpPreviewDialogueResolutionRequest(conversation, node));

            Assert.IsTrue(result.IsResolved);
            Assert.AreEqual(directInterpData, result.InterpData);
            Assert.IsFalse(HasDiagnosticCode(result.Diagnostics, "TARGET_ACTION_UNRESOLVED"));
            Assert.AreEqual(1, targetReader.CallCount);
            Assert.IsNotNull(targetReader.LastRequest);
            Assert.AreEqual(externalAction.UIndex, targetReader.LastRequest.ActionId.ExportUIndex);
            Assert.AreEqual(MEGame.LE3, targetReader.LastRequest.ActionId.Game);
            Assert.AreEqual(Path.GetFullPath(tempPackagePath), targetReader.LastRequest.ActionId.PackagePath);
        }
        finally
        {
            if (File.Exists(tempPackagePath))
            {
                File.Delete(tempPackagePath);
            }
        }
    }

    private static DialogueNodeExtended CreateNode(int nodeExportId, bool isReply)
    {
        var nodeProp = new StructProperty(
            "BioDialogNode",
            isImmutable: false,
            new IntProperty(nodeExportId, "nExportID"));

        return new DialogueNodeExtended(
            nodeProp,
            isReply,
            nodeCount: 0,
            speakerIndex: 0,
            lineStrRef: 0,
            line: "line",
            firesConditional: false,
            conditionalOrBool: 0,
            transition: 0,
            replyType: EReplyTypes.REPLY_STANDARD)
        {
            ExportID = nodeExportId
        };
    }

    private static ExportEntry CreateExport(IMEPackage package, string name, string className)
    {
        ImportEntry classImport = package.Imports.Count > 0
            ? package.Imports[0]
            : new ImportEntry(package) { ObjectName = className, ClassName = "Class", PackageFile = "Engine" };

        if (package.Imports.Count == 0)
        {
            package.AddImport(classImport);
        }

        var export = new ExportEntry(package, 0, name, prePropBinary: new byte[4], properties: []);
        export.Class = classImport;
        package.AddExport(export);
        return export;
    }

    private static bool HasDiagnosticCode(IReadOnlyList<InterpPreviewDiagnostic> diagnostics, string code)
    {
        return diagnostics.Any(d => string.Equals(d.Code, code, StringComparison.Ordinal));
    }

    private sealed class StubSourceReader : IInterpPreviewSourceReader
    {
        private readonly InterpPreviewSourceReadResult _result;

        public StubSourceReader(InterpPreviewSourceReadResult result)
        {
            _result = result;
        }

        public InterpPreviewSourceReadResult Read(InterpPreviewSourceReadRequest request) => _result;
    }

    private sealed class StubNodeDiscovery : IInterpPreviewNodeDiscovery
    {
        private readonly InterpPreviewNodeDiscoveryResult _result;

        public StubNodeDiscovery(InterpPreviewNodeDiscoveryResult result)
        {
            _result = result;
        }

        public InterpPreviewNodeDiscoveryResult Discover(InterpPreviewNodeDiscoveryRequest request) => _result;
    }

    private sealed class StubTrackTargetReader : IInterpPreviewTrackTargetReader
    {
        private readonly InterpPreviewTrackTargetReadResult _result;

        public StubTrackTargetReader(InterpPreviewTrackTargetReadResult result)
        {
            _result = result;
        }

        public InterpPreviewTrackTargetReadResult ReadTargets(InterpPreviewTrackTargetReadRequest request) => _result;
    }

    private sealed class CapturingTrackTargetReader : IInterpPreviewTrackTargetReader
    {
        private readonly InterpPreviewTrackTargetReadResult _result;

        public CapturingTrackTargetReader(InterpPreviewTrackTargetReadResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }
        public InterpPreviewTrackTargetReadRequest LastRequest { get; private set; }

        public InterpPreviewTrackTargetReadResult ReadTargets(InterpPreviewTrackTargetReadRequest request)
        {
            CallCount++;
            LastRequest = request;
            return _result;
        }
    }

    private sealed class StubActorBindingResolver : IInterpPreviewActorBindingResolver
    {
        private readonly InterpPreviewActorBindingResult _result;

        public StubActorBindingResolver(InterpPreviewActorBindingResult result)
        {
            _result = result;
        }

        public InterpPreviewActorBindingResult Resolve(InterpPreviewActorBindingRequest request) => _result;
    }
}
