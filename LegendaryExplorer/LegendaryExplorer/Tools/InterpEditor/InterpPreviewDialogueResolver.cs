using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LegendaryExplorerCore.Dialogue;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Kismet;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Packages.CloningImportingAndRelinking;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorer.Tools.LevelEditor;

namespace LegendaryExplorer.Tools.InterpEditor;

public sealed class InterpPreviewDialogueResolver : IInterpPreviewDialogueResolver
{
    private readonly IInterpPreviewSourceReader _sourceReader;
    private readonly IInterpPreviewNodeDiscovery _nodeDiscovery;
    private readonly IInterpPreviewTrackTargetReader _trackTargetReader;
    private readonly IInterpPreviewActorBindingResolver _actorBindingResolver;

    public InterpPreviewDialogueResolver()
        : this(
            new InterpPreviewSourceReader(),
            new InterpPreviewNodeDiscovery(),
            new InterpPreviewTrackTargetReader(),
            new InterpPreviewActorBindingResolver())
    {
    }

    internal InterpPreviewDialogueResolver(
        IInterpPreviewSourceReader sourceReader,
        IInterpPreviewNodeDiscovery nodeDiscovery,
        IInterpPreviewTrackTargetReader trackTargetReader,
        IInterpPreviewActorBindingResolver actorBindingResolver)
    {
        _sourceReader = sourceReader ?? throw new ArgumentNullException(nameof(sourceReader));
        _nodeDiscovery = nodeDiscovery ?? throw new ArgumentNullException(nameof(nodeDiscovery));
        _trackTargetReader = trackTargetReader ?? throw new ArgumentNullException(nameof(trackTargetReader));
        _actorBindingResolver = actorBindingResolver ?? throw new ArgumentNullException(nameof(actorBindingResolver));
    }

    public InterpPreviewDialogueResolution Resolve(InterpPreviewDialogueResolutionRequest request)
    {
        var diagnostics = new List<InterpPreviewDiagnostic>();

        if (request is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "REQUEST_NULL", "Unable to preview line: no dialogue request was provided."));
            return InterpPreviewDialogueResolution.Unresolved(diagnostics);
        }

        ConversationExtended conversation = request.Conversation;
        DialogueNodeExtended node = request.Node;

        if (conversation is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "CONVERSATION_NULL", "Unable to preview line: conversation data is missing."));
            return InterpPreviewDialogueResolution.Unresolved(diagnostics);
        }

        if (node is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "NODE_NULL", "Unable to preview line: no dialogue line is selected."));
            return InterpPreviewDialogueResolution.Unresolved(diagnostics);
        }

        string nodeContext = BuildNodeContext(conversation, node);
        AddSceneBindingDiagnostics(request, conversation, node, diagnostics);

        InterpPreviewSourceReadResult sourceResult = _sourceReader.Read(new InterpPreviewSourceReadRequest(conversation, node));
        AddDiagnostics(diagnostics, sourceResult.Diagnostics);

        InterpPreviewNodeDiscoveryResult discoveryResult = null;
        InterpPreviewDiscoveredInterpData selectedDiscovered = null;
        if (sourceResult.Snapshot is not null)
        {
            InterpPreviewSequenceGraphSnapshot graph = BuildSequenceGraphSnapshot(conversation, sourceResult.Snapshot.SequenceId, diagnostics);
            discoveryResult = _nodeDiscovery.Discover(new InterpPreviewNodeDiscoveryRequest(sourceResult.Snapshot, graph));
            AddDiagnostics(diagnostics, discoveryResult.Diagnostics);

            if (discoveryResult.Candidates.Count > 0)
            {
                selectedDiscovered = discoveryResult.Candidates.FirstOrDefault(c => !c.IsHintOnly) ?? discoveryResult.Candidates[0];
                if (discoveryResult.Candidates.Count > 1)
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_MULTIPLE_CANDIDATES", $"Found {discoveryResult.Candidates.Count} InterpData candidates for {nodeContext}; using first deterministic candidate for compatibility."));
                }
            }
        }

        ExportEntry discoveredInterpData = null;
        InterpPreviewSourceObjectId discoveredActionId = default;
        IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> discoveredActionLinks = null;
        if (selectedDiscovered is not null)
        {
            if (TryResolveExportFromSourceId(conversation, selectedDiscovered.InterpDataId, out ExportEntry resolvedInterp)
                && resolvedInterp is not null
                && string.Equals(resolvedInterp.ClassName, "InterpData", StringComparison.Ordinal))
            {
                discoveredInterpData = resolvedInterp;
            }

            if (selectedDiscovered.ActionId is InterpPreviewSourceObjectId actionId
                && TryReadActionVariableLinksFromSourceId(conversation, actionId, out InterpPreviewSourceObjectId resolvedActionId, out IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> resolvedActionLinks))
            {
                discoveredActionId = resolvedActionId;
                discoveredActionLinks = resolvedActionLinks;
            }
        }

        ExportEntry directInterpData = node.InterpData as ExportEntry;

        ExportEntry fallbackInterpData = null;
        try
        {
            fallbackInterpData = conversation.ParseSingleNodeInterpData(node);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Error, "INTERP_RESOLVE_EXCEPTION", $"Preview lookup failed for {nodeContext}: {ex.Message}"));
        }

        ExportEntry selectedInterpData = discoveredInterpData ?? directInterpData ?? fallbackInterpData;
        if (selectedInterpData is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_MISSING", $"No preview data is linked to this line ({nodeContext})."));
            return InterpPreviewDialogueResolution.Unresolved(diagnostics);
        }

        bool selectedFromDiscovery = discoveredInterpData is not null && ReferenceEquals(selectedInterpData, discoveredInterpData);
        bool selectedFromDirect = directInterpData is not null && ReferenceEquals(selectedInterpData, directInterpData);
        bool selectedFromFallback = fallbackInterpData is not null && ReferenceEquals(selectedInterpData, fallbackInterpData);

        if (selectedFromDiscovery)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Info, "INTERP_DISCOVERY_SELECTED", $"Preview data was selected from M4 graph discovery ({nodeContext})."));
        }
        else if (selectedFromDirect)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Info, "INTERP_DIRECT", $"Preview data found on the selected line ({nodeContext})."));
        }
        else if (selectedFromFallback)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "INTERP_FALLBACK", $"Preview data was recovered from linked conversation data ({nodeContext})."));
        }

        if (TryBuildInterpDataSnapshot(selectedInterpData, out InterpPreviewInterpDataSnapshot interpSnapshot))
        {
            if (discoveredActionLinks is not null)
            {
                InterpPreviewTrackTargetReadResult targetResult = _trackTargetReader.ReadTargets(new InterpPreviewTrackTargetReadRequest(discoveredActionId, interpSnapshot, discoveredActionLinks));
                AddDiagnostics(diagnostics, targetResult.Diagnostics);

                if (request.SceneSnapshot is not null && targetResult.Targets.Count > 0)
                {
                    InterpPreviewActorBindingResult bindingResult = _actorBindingResolver.Resolve(new InterpPreviewActorBindingRequest(request.SceneSnapshot, targetResult.Targets));
                    AddDiagnostics(diagnostics, bindingResult.Diagnostics);
                }
                else if (targetResult.Targets.Count > 0)
                {
                    diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "ACTOR_BINDING_SCENE_UNAVAILABLE", "Scene snapshot was unavailable for actor binding pass."));
                }
            }
            else
            {
                diagnostics.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "TARGET_ACTION_UNRESOLVED", "SeqAct_Interp source action could not be resolved; target extraction was skipped."));
            }
        }

        return InterpPreviewDialogueResolution.Resolved(selectedInterpData, diagnostics);
    }

    private static void AddDiagnostics(List<InterpPreviewDiagnostic> destination, IReadOnlyList<InterpPreviewDiagnostic> source)
    {
        if (destination is null || source is null || source.Count == 0)
        {
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            destination.Add(source[i]);
        }
    }

    private static InterpPreviewSequenceGraphSnapshot BuildSequenceGraphSnapshot(
        ConversationExtended conversation,
        InterpPreviewSourceObjectId? sequenceId,
        List<InterpPreviewDiagnostic> diagnostics)
    {
        if (conversation?.Sequence is not ExportEntry sequenceExport || !sequenceId.HasValue)
        {
            return null;
        }

        var sequenceObjects = sequenceExport.GetProperty<ArrayProperty<ObjectProperty>>("SequenceObjects");
        if (sequenceObjects is null || sequenceObjects.Count == 0)
        {
            return new InterpPreviewSequenceGraphSnapshot(sequenceId.Value, []);
        }

        var nodes = new List<InterpPreviewSequenceNodeSnapshot>(sequenceObjects.Count);
        foreach (ObjectProperty sequenceObject in sequenceObjects)
        {
            if (sequenceObject is null || sequenceObject.Value <= 0 || !sequenceExport.FileRef.TryGetUExport(sequenceObject.Value, out ExportEntry nodeExport))
            {
                continue;
            }

            if (!TryCreateSourceObjectId(nodeExport, out InterpPreviewSourceObjectId nodeId))
            {
                continue;
            }

            int? convNodeId = null;
            if (string.Equals(nodeExport.ClassName, "BioSeqEvt_ConvNode", StringComparison.Ordinal)
                && nodeExport.GetProperty<IntProperty>("m_nNodeID") is IntProperty convNodeProperty)
            {
                convNodeId = convNodeProperty.Value;
            }

            List<string> outputNames = KismetHelper.GetOutputLinkNames(nodeExport);
            List<List<OutputLink>> outputLinks = KismetHelper.GetOutputLinksOfNode(nodeExport);
            var copiedOutputLinks = new List<InterpPreviewOutputLinkSnapshot>();
            for (int outputIndex = 0; outputIndex < outputLinks.Count; outputIndex++)
            {
                string outputName = outputIndex < outputNames.Count ? outputNames[outputIndex] : string.Empty;
                List<OutputLink> links = outputLinks[outputIndex];
                if (links is null)
                {
                    continue;
                }

                foreach (OutputLink link in links)
                {
                    if (link?.LinkedOp is not ExportEntry destination)
                    {
                        continue;
                    }

                    copiedOutputLinks.Add(new InterpPreviewOutputLinkSnapshot(
                        outputIndex,
                        outputName,
                        destination.UIndex,
                        link.InputLinkIdx));
                }
            }

            List<VarLinkInfo> variableLinks = KismetHelper.GetVariableLinksOfNode(nodeExport);
            var copiedVariableLinks = new List<InterpPreviewVariableLinkSnapshot>(variableLinks.Count);
            foreach (VarLinkInfo variableLink in variableLinks)
            {
                var linkedIndexes = variableLink.LinkedNodes
                    .OfType<ExportEntry>()
                    .Select(static export => export.UIndex)
                    .ToList();
                copiedVariableLinks.Add(new InterpPreviewVariableLinkSnapshot(variableLink.LinkDesc, linkedIndexes));
            }

            nodes.Add(new InterpPreviewSequenceNodeSnapshot(nodeId, nodeExport.ClassName, convNodeId, copiedOutputLinks, copiedVariableLinks));
        }

        try
        {
            return new InterpPreviewSequenceGraphSnapshot(sequenceId.Value, nodes);
        }
        catch (ArgumentException ex)
        {
            diagnostics?.Add(new InterpPreviewDiagnostic(InterpPreviewDiagnosticSeverity.Warning, "DISCOVERY_GRAPH_INVALID", $"Sequence graph snapshot was invalid: {ex.Message}"));
            return null;
        }
    }

    private static bool TryBuildInterpDataSnapshot(ExportEntry interpDataExport, out InterpPreviewInterpDataSnapshot snapshot)
    {
        snapshot = null;
        if (interpDataExport is null || !TryCreateSourceObjectId(interpDataExport, out InterpPreviewSourceObjectId interpDataId))
        {
            return false;
        }

        var groups = new List<InterpPreviewInterpGroupSnapshot>();
        ArrayProperty<ObjectProperty> interpGroups = interpDataExport.GetProperty<ArrayProperty<ObjectProperty>>("InterpGroups");
        if (interpGroups is not null)
        {
            foreach (ObjectProperty groupRef in interpGroups)
            {
                if (groupRef is null || groupRef.Value <= 0 || !interpDataExport.FileRef.TryGetUExport(groupRef.Value, out ExportEntry groupExport))
                {
                    continue;
                }

                if (!TryCreateSourceObjectId(groupExport, out InterpPreviewSourceObjectId groupId))
                {
                    continue;
                }

                string groupName = groupExport.GetProperty<NameProperty>("GroupName")?.Value.Instanced
                                   ?? groupExport.ObjectName.Instanced;

                var tracks = new List<InterpPreviewInterpTrackSnapshot>();
                ArrayProperty<ObjectProperty> interpTracks = groupExport.GetProperty<ArrayProperty<ObjectProperty>>("InterpTracks");
                if (interpTracks is not null)
                {
                    foreach (ObjectProperty trackRef in interpTracks)
                    {
                        if (trackRef is null || trackRef.Value <= 0 || !groupExport.FileRef.TryGetUExport(trackRef.Value, out ExportEntry trackExport))
                        {
                            continue;
                        }

                        if (!TryCreateSourceObjectId(trackExport, out InterpPreviewSourceObjectId trackId))
                        {
                            continue;
                        }

                        string findActorTag = trackExport.GetProperty<NameProperty>("m_nmFindActor")?.Value.Instanced;
                        tracks.Add(new InterpPreviewInterpTrackSnapshot(trackId, trackExport.ClassName, findActorTag));
                    }
                }

                groups.Add(new InterpPreviewInterpGroupSnapshot(groupId, groupName, tracks));
            }
        }

        snapshot = new InterpPreviewInterpDataSnapshot(interpDataId, groups);
        return true;
    }

    private static IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> BuildActionVariableLinks(ExportEntry actionExport)
    {
        if (actionExport is null)
        {
            return [];
        }

        List<VarLinkInfo> varLinks = KismetHelper.GetVariableLinksOfNode(actionExport);
        if (varLinks.Count == 0)
        {
            return [];
        }

        var links = new List<InterpPreviewActionVariableLinkSnapshot>(varLinks.Count);
        foreach (VarLinkInfo varLink in varLinks)
        {
            links.Add(new InterpPreviewActionVariableLinkSnapshot(varLink.LinkDesc));
        }

        return links;
    }

    private static bool TryResolveExportFromSourceId(ConversationExtended conversation, InterpPreviewSourceObjectId sourceId, out ExportEntry export)
    {
        export = null;
        if (conversation?.Export?.FileRef is null)
        {
            return false;
        }

        IMEPackage package = conversation.Export.FileRef;
        string packagePath = package.FilePath;
        if (!string.Equals(Path.GetFullPath(packagePath), sourceId.PackagePath, StringComparison.OrdinalIgnoreCase)
            || package.Game != sourceId.Game)
        {
            return false;
        }

        return package.TryGetUExport(sourceId.ExportUIndex, out export);
    }

    private static bool TryReadActionVariableLinksFromSourceId(
        ConversationExtended conversation,
        InterpPreviewSourceObjectId sourceId,
        out InterpPreviewSourceObjectId actionId,
        out IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> actionVariableLinks)
    {
        actionId = default;
        actionVariableLinks = null;

        if (!TryResolveExportFromSourceId(conversation, sourceId, out ExportEntry actionExport))
        {
            if (!TryResolveExportFromExternalPackage(sourceId, out actionExport, out IMEPackage temporaryPackage))
            {
                return false;
            }

            try
            {
                if (!TryCreateSourceObjectId(actionExport, out actionId))
                {
                    return false;
                }

                actionVariableLinks = BuildActionVariableLinks(actionExport);
                return true;
            }
            finally
            {
                temporaryPackage.Dispose();
            }
        }

        if (!TryCreateSourceObjectId(actionExport, out actionId))
        {
            return false;
        }

        actionVariableLinks = BuildActionVariableLinks(actionExport);
        return true;
    }

    private static bool TryResolveExportFromExternalPackage(
        InterpPreviewSourceObjectId sourceId,
        out ExportEntry export,
        out IMEPackage package)
    {
        export = null;
        package = null;

        if (string.IsNullOrWhiteSpace(sourceId.PackagePath)
            || !File.Exists(sourceId.PackagePath))
        {
            return false;
        }

        try
        {
            package = MEPackageHandler.OpenMEPackage(sourceId.PackagePath, forceLoadFromDisk: true);
            if (package is null || package.Game != sourceId.Game)
            {
                package?.Dispose();
                package = null;
                return false;
            }

            if (!package.TryGetUExport(sourceId.ExportUIndex, out export) || export is null)
            {
                package.Dispose();
                package = null;
                return false;
            }

            return true;
        }
        catch
        {
            package?.Dispose();
            package = null;
            export = null;
            return false;
        }
    }

    private static bool TryCreateSourceObjectId(IEntry entry, out InterpPreviewSourceObjectId sourceObjectId)
    {
        sourceObjectId = default;
        if (entry is null || entry.UIndex <= 0 || entry.FileRef is null || string.IsNullOrWhiteSpace(entry.FileRef.FilePath))
        {
            return false;
        }

        sourceObjectId = new InterpPreviewSourceObjectId(entry.Game, entry.FileRef.FilePath, entry.UIndex);
        return true;
    }

    private static void AddSceneBindingDiagnostics(
        InterpPreviewDialogueResolutionRequest request,
        ConversationExtended conversation,
        DialogueNodeExtended node,
        List<InterpPreviewDiagnostic> diagnostics)
    {
        if (request is null)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(
                InterpPreviewDiagnosticSeverity.Warning,
                "SCENE_BIND_SPEAKER_UNAVAILABLE",
                "Loaded scene actor lookup is unavailable."));
            return;
        }

        if (conversation?.Speakers is null)
        {
            return;
        }

        SpeakerExtended speaker = conversation.Speakers.FirstOrDefault(s => s.SpeakerID == node.SpeakerIndex);
        if (speaker is null || string.IsNullOrWhiteSpace(speaker.SpeakerName))
        {
            return;
        }

        bool isOwnerSpeaker = speaker.SpeakerName.Equals("owner", StringComparison.OrdinalIgnoreCase);
        bool isPlayerSpeaker = speaker.SpeakerName.Equals("player", StringComparison.OrdinalIgnoreCase);
        string speakerLookup = speaker.SpeakerName;
        string speakerContext = speaker.SpeakerName;

        AddLoadedLevelPathDiagnostics(request, diagnostics);
        if (isOwnerSpeaker)
        {
            OwnerBindingInfo ownerBinding = ResolveConversationOwnerBinding(conversation, request.LoadedLevelPaths);
            if (!string.IsNullOrWhiteSpace(ownerBinding.SourceDescription))
            {
                diagnostics.Add(new InterpPreviewDiagnostic(
                    InterpPreviewDiagnosticSeverity.Info,
                    "SCENE_BIND_OWNER_SOURCE",
                    ownerBinding.SourceDescription));
            }

            if (!string.IsNullOrWhiteSpace(ownerBinding.Lookup))
            {
                speakerLookup = ownerBinding.Lookup;
                speakerContext = $"owner ({ownerBinding.Lookup})";
            }
            else
            {
                diagnostics.Add(new InterpPreviewDiagnostic(
                    InterpPreviewDiagnosticSeverity.Warning,
                    "SCENE_BIND_OWNER_UNRESOLVED",
                    "Conversation owner could not be resolved from StartConversation Owner links."));
                return;
            }
        }
        else if (isPlayerSpeaker)
        {
            PlayerBindingInfo playerBinding = ResolvePlayerBinding(request, conversation);
            if (!string.IsNullOrWhiteSpace(playerBinding.SourceDescription))
            {
                diagnostics.Add(new InterpPreviewDiagnostic(
                    InterpPreviewDiagnosticSeverity.Info,
                    "SCENE_BIND_PLAYER_SOURCE",
                    playerBinding.SourceDescription));
            }

            if (!string.IsNullOrWhiteSpace(playerBinding.Lookup))
            {
                speakerLookup = playerBinding.Lookup;
                speakerContext = $"player ({playerBinding.Lookup})";
            }
        }

        IReadOnlyList<ActorProxy> matches = request.FindLoadedSceneActors(speakerLookup);
        if (matches.Count > 0)
        {
            if (isOwnerSpeaker)
            {
                diagnostics.Add(new InterpPreviewDiagnostic(
                    InterpPreviewDiagnosticSeverity.Info,
                    "SCENE_BIND_OWNER_MATCH",
                    $"Conversation owner resolved to '{speakerLookup}' with {matches.Count} loaded scene actor match(es)."));
            }
            else if (isPlayerSpeaker)
            {
                diagnostics.Add(new InterpPreviewDiagnostic(
                    InterpPreviewDiagnosticSeverity.Info,
                    "SCENE_BIND_PLAYER_MATCH",
                    $"Loaded scene contains {matches.Count} actor match(es) for Player speaker lookup '{speakerLookup}'."));
            }
            else
            {
                diagnostics.Add(new InterpPreviewDiagnostic(
                    InterpPreviewDiagnosticSeverity.Info,
                    "SCENE_BIND_SPEAKER_MATCH",
                    $"Loaded scene contains {matches.Count} actor match(es) for speaker '{speakerContext}'."));
            }
        }
        else
        {
            if (isOwnerSpeaker && !string.IsNullOrWhiteSpace(speakerLookup))
            {
                diagnostics.Add(new InterpPreviewDiagnostic(
                    InterpPreviewDiagnosticSeverity.Warning,
                    "SCENE_BIND_OWNER_MISS",
                    $"Conversation owner resolved to '{speakerLookup}', but no loaded actor match was found."));
            }
            else if (isPlayerSpeaker)
            {
                diagnostics.Add(new InterpPreviewDiagnostic(
                    InterpPreviewDiagnosticSeverity.Warning,
                    "SCENE_BIND_PLAYER_MISS",
                    $"No loaded actor match found for Player speaker lookup '{speakerLookup}'."));
            }
            else
            {
                diagnostics.Add(new InterpPreviewDiagnostic(
                    InterpPreviewDiagnosticSeverity.Warning,
                    "SCENE_BIND_SPEAKER_MISS",
                    $"No loaded actor match found for speaker '{speakerContext}'."));
            }
        }
    }

    private static PlayerBindingInfo ResolvePlayerBinding(InterpPreviewDialogueResolutionRequest request, ConversationExtended conversation)
    {
        bool preferFemale = request?.PreferFemalePlayer ?? true;

        if (TryResolvePlayerFromLoadedScene(request, out string loadedLookup, out string loadedDescription))
        {
            return new PlayerBindingInfo(loadedLookup, loadedDescription);
        }

        string playerPawnName = preferFemale ? "BioPawn_1" : "BioPawn_0";
        string playerFileName = conversation?.Export?.FileRef?.Game switch
        {
            MEGame.ME1 => "EntryMenu.SFM",
            MEGame.LE1 => "EntryMenu.pcc",
            _ => "BioP_Char.pcc"
        };

        if (conversation?.Export?.FileRef is not null
            && MELoadedFiles.TryGetHighestMountedFile(conversation.Export.FileRef.Game, playerFileName, out string playerFilePath)
            && File.Exists(playerFilePath))
        {
            return new PlayerBindingInfo(
                playerPawnName,
                $"Player speaker uses {(preferFemale ? "Female" : "Male")} default Shepard ({playerPawnName}) from {Path.GetFileName(playerFilePath)}. Load that pawn's level/package in Interp Preview for actor binding match.");
        }

        return new PlayerBindingInfo(
            playerPawnName,
            $"Player speaker uses {(preferFemale ? "Female" : "Male")} default Shepard lookup ({playerPawnName}), but default player package could not be resolved via mounted files.");
    }

    private static bool TryResolvePlayerFromLoadedScene(InterpPreviewDialogueResolutionRequest request, out string lookup, out string description)
    {
        lookup = null;
        description = null;

        if (request is null)
        {
            return false;
        }

        if (request.FindLoadedSceneActors("player") is { Count: > 0 })
        {
            lookup = "player";
            description = "Player speaker lookup resolved to loaded scene tag 'player'.";
            return true;
        }

        if (request.FindLoadedSceneActors("BioPawn_1") is { Count: > 0 })
        {
            lookup = "BioPawn_1";
            description = "Player speaker lookup resolved to loaded scene actor 'BioPawn_1' (default female Shepard).";
            return true;
        }

        if (request.FindLoadedSceneActors("BioPawn_0") is { Count: > 0 })
        {
            lookup = "BioPawn_0";
            description = "Player speaker lookup resolved to loaded scene actor 'BioPawn_0' (default male Shepard).";
            return true;
        }

        return false;
    }

    private static void AddLoadedLevelPathDiagnostics(InterpPreviewDialogueResolutionRequest request, List<InterpPreviewDiagnostic> diagnostics)
    {
        if (request?.LoadedLevelPaths is null || request.LoadedLevelPaths.Count == 0)
        {
            diagnostics.Add(new InterpPreviewDiagnostic(
                InterpPreviewDiagnosticSeverity.Warning,
                "SCENE_LEVELS_NONE",
                "Interp Preview currently has no loaded level files."));
            return;
        }

        string files = string.Join(", ", request.LoadedLevelPaths.Select(Path.GetFileName));
        diagnostics.Add(new InterpPreviewDiagnostic(
            InterpPreviewDiagnosticSeverity.Info,
            "SCENE_LEVELS_LOADED",
            $"Interp Preview loaded level files ({request.LoadedLevelPaths.Count}): {files}"));
    }

    private static OwnerBindingInfo ResolveConversationOwnerBinding(ConversationExtended conversation, IReadOnlyList<string> loadedLevelPaths)
    {
        if (conversation?.Export?.FileRef is null)
        {
            return new OwnerBindingInfo(null, "Unable to resolve conversation owner: conversation package is unavailable.");
        }

        if (loadedLevelPaths is not null)
        {
            foreach (string loadedLevelPath in loadedLevelPaths.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using IMEPackage loadedLevelPackage = MEPackageHandler.OpenMEPackage(loadedLevelPath);
                if (TryResolveOwnerBindingFromPackage(loadedLevelPackage, conversation, out OwnerBindingInfo bindingFromLoadedLevel))
                {
                    return bindingFromLoadedLevel;
                }
            }
        }

        if (conversation.Sequence is ExportEntry sequenceExport
            && TryResolveOwnerBindingFromPackage(sequenceExport.FileRef, conversation, out OwnerBindingInfo bindingFromSequence))
        {
            return bindingFromSequence;
        }

        if (TryResolveOwnerBindingFromPackage(conversation.Export.FileRef, conversation, out OwnerBindingInfo bindingFromConversationPackage))
        {
            return bindingFromConversationPackage;
        }

        string companionPath = TryGetNonLocCompanionPath(conversation.Export.FileRef.FilePath);
        if (!string.IsNullOrWhiteSpace(companionPath)
            && File.Exists(companionPath)
            && !string.Equals(companionPath, conversation.Export.FileRef.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            using IMEPackage companionPackage = MEPackageHandler.OpenMEPackage(companionPath);
            if (TryResolveOwnerBindingFromPackage(companionPackage, conversation, out OwnerBindingInfo bindingFromCompanion))
            {
                return bindingFromCompanion;
            }
        }

        string conversationFile = Path.GetFileName(conversation.Export.FileRef.FilePath);
        string conversationName = conversation.Export.ObjectName.Instanced;
        return new OwnerBindingInfo(null, $"No StartConversation Owner link referencing conversation '{conversationName}' was found in scanned packages (sequence, conversation, companion of {conversationFile}).");
    }

    private static bool TryResolveOwnerBindingFromPackage(IMEPackage package, ConversationExtended conversation, out OwnerBindingInfo binding)
    {
        binding = default;
        if (package is null)
        {
            return false;
        }

        HashSet<string> targetKeys = BuildConversationLookupKeys(conversation);
        List<ExportEntry> startConversationCandidates = package.Exports
            .Where(export => IsStartConversationCandidate(export))
            .ToList();

        List<string> unmatchedConversationRefs = [];

        foreach (ExportEntry startConversation in startConversationCandidates)
        {
            HashSet<string> candidateKeys = BuildReferencedConversationLookupKeys(startConversation);
            if (candidateKeys.Count == 0)
            {
                continue;
            }

            if (!candidateKeys.Overlaps(targetKeys))
            {
                unmatchedConversationRefs.Add($"#{startConversation.UIndex}:{string.Join("|", candidateKeys.Take(2))}");
                continue;
            }

            if (TryGetOwnerBindingFromStartConversation(startConversation, package, out binding))
            {
                return true;
            }
        }

        if (startConversationCandidates.Count > 0)
        {
            string targetSummary = string.Join("|", targetKeys.Take(3));
            string mismatchSummary = unmatchedConversationRefs.Count > 0
                ? $" Candidate Conv refs: {string.Join(", ", unmatchedConversationRefs.Take(4))}."
                : string.Empty;

            binding = new OwnerBindingInfo(
                null,
                $"Scanned {startConversationCandidates.Count} StartConversation candidate(s) in {Path.GetFileName(package.FilePath)} but none matched conversation keys '{targetSummary}'.{mismatchSummary}");
            return true;
        }

        return false;
    }

    private static bool TryGetOwnerBindingFromStartConversation(ExportEntry startConversation, IMEPackage package, out OwnerBindingInfo binding)
    {
        binding = default;
        foreach (VarLinkInfo varLink in KismetHelper.GetVariableLinksOfNode(startConversation))
        {
            if (!string.Equals(varLink.LinkDesc, "Owner", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ExportEntry ownerVar = varLink.LinkedNodes.OfType<ExportEntry>().FirstOrDefault();
            if (ownerVar is null)
            {
                binding = new OwnerBindingInfo(null, $"Found Owner link on {startConversation.ClassName} #{startConversation.UIndex} in {Path.GetFileName(package.FilePath)}, but no linked owner variable export was present.");
                return true;
            }

            if (TryResolveOwnerLookupFromVariable(ownerVar, out string lookup, out string sourceDescription))
            {
                binding = new OwnerBindingInfo(lookup, $"{sourceDescription} Source package: {Path.GetFileName(package.FilePath)}.");
                return true;
            }

            binding = new OwnerBindingInfo(null, $"Owner variable {ownerVar.ClassName} #{ownerVar.UIndex} in {Path.GetFileName(package.FilePath)} was linked, but no owner lookup value could be derived.");
            return true;
        }

        ArrayProperty<StructProperty> rawLinks = startConversation.GetProperty<ArrayProperty<StructProperty>>("VariableLinks");
        if (rawLinks is not null)
        {
            foreach (StructProperty link in rawLinks)
            {
                string linkDesc = link.GetProp<StrProperty>("LinkDesc")?.Value;
                if (!string.Equals(linkDesc, "Owner", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ArrayProperty<ObjectProperty> linkedVariables = link.GetProp<ArrayProperty<ObjectProperty>>("LinkedVariables");
                int ownerIndex = linkedVariables?.FirstOrDefault()?.Value ?? 0;
                if (ownerIndex > 0 && package.IsUExport(ownerIndex))
                {
                    ExportEntry ownerExport = package.GetUExport(ownerIndex);
                    if (TryResolveOwnerLookupFromVariable(ownerExport, out string lookup, out string sourceDescription))
                    {
                        binding = new OwnerBindingInfo(lookup, $"{sourceDescription} Source package: {Path.GetFileName(package.FilePath)} (raw VariableLinks fallback). Trigger #{startConversation.UIndex}.");
                        return true;
                    }

                    binding = new OwnerBindingInfo(null, $"Owner export #{ownerExport.UIndex} ({ownerExport.ClassName}) on trigger #{startConversation.UIndex} in {Path.GetFileName(package.FilePath)} could not be converted to a lookup value.");
                    return true;
                }

                binding = new OwnerBindingInfo(null, $"Matched trigger #{startConversation.UIndex} in {Path.GetFileName(package.FilePath)} but Owner link had no valid linked export.");
                return true;
            }
        }

        binding = new OwnerBindingInfo(null, $"Matched trigger #{startConversation.UIndex} ({startConversation.ClassName}) in {Path.GetFileName(package.FilePath)} but it had no readable Owner variable link.");
        return true;
    }

    private static HashSet<string> BuildConversationLookupKeys(ConversationExtended conversation)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        if (conversation?.Export is not ExportEntry export)
        {
            return keys;
        }

        AddLookupKey(keys, export.ObjectName.Name);
        AddLookupKey(keys, export.ObjectName.Instanced);
        AddLookupKey(keys, export.InstancedFullPath);
        return keys;
    }

    private static HashSet<string> BuildReferencedConversationLookupKeys(ExportEntry startConversation)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        ObjectProperty convProperty = GetConversationProperty(startConversation);
        if (convProperty is null || convProperty.Value == 0)
        {
            return keys;
        }

        int convIndex = convProperty.Value;
        if (convIndex > 0 && startConversation.FileRef.IsUExport(convIndex))
        {
            ExportEntry convExport = startConversation.FileRef.GetUExport(convIndex);
            AddLookupKey(keys, convExport.ObjectName.Name);
            AddLookupKey(keys, convExport.ObjectName.Instanced);
            AddLookupKey(keys, convExport.InstancedFullPath);
            return keys;
        }

        if (convIndex < 0 && startConversation.FileRef.TryGetImport(convIndex, out ImportEntry convImport))
        {
            AddLookupKey(keys, convImport.ObjectName.Name);
            AddLookupKey(keys, convImport.ObjectName.Instanced);
            AddLookupKey(keys, convImport.InstancedFullPath);

            ExportEntry resolvedConversation = EntryImporter.ResolveImport(convImport, new PackageCache());
            if (resolvedConversation is not null)
            {
                AddLookupKey(keys, resolvedConversation.ObjectName.Name);
                AddLookupKey(keys, resolvedConversation.ObjectName.Instanced);
                AddLookupKey(keys, resolvedConversation.InstancedFullPath);
            }
        }

        return keys;
    }

    private static void AddLookupKey(HashSet<string> keys, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            keys.Add(value.Trim());
        }
    }

    private static string TryGetNonLocCompanionPath(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return null;
        }

        string directory = Path.GetDirectoryName(currentPath);
        string extension = Path.GetExtension(currentPath);
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(currentPath);

        int locMarkerIndex = fileNameWithoutExtension.IndexOf("_LOC_", StringComparison.OrdinalIgnoreCase);
        if (locMarkerIndex <= 0)
        {
            return null;
        }

        string baseName = fileNameWithoutExtension[..locMarkerIndex];
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return null;
        }

        return Path.Combine(directory ?? string.Empty, baseName + extension);
    }

    private static bool TryResolveOwnerLookupFromVariable(ExportEntry ownerVariable, out string lookup, out string sourceDescription)
    {
        lookup = null;
        sourceDescription = null;

        string directTag = ownerVariable.GetProperty<NameProperty>("Tag")?.Value.Instanced;
        if (!string.IsNullOrWhiteSpace(directTag))
        {
            lookup = directTag;
            sourceDescription = $"Conversation owner resolved via direct actor link {ownerVariable.ClassName} #{ownerVariable.UIndex} tag '{directTag}'.";
            return true;
        }

        switch (ownerVariable.ClassName)
        {
            case "BioSeqVar_ObjectFindByTag":
            case "BioSeqVar_ObjectListFindByTag":
            {
                string tag = ownerVariable.GetProperty<NameProperty>("m_sObjectTagToFind")?.Value.Instanced
                             ?? ownerVariable.GetProperty<StrProperty>("m_sObjectTagToFind")?.Value;
                if (string.IsNullOrWhiteSpace(tag))
                {
                    return false;
                }

                lookup = tag;
                sourceDescription = $"Conversation owner resolved via {ownerVariable.ClassName} #{ownerVariable.UIndex} tag '{tag}'.";
                return true;
            }
            case "SeqVar_Object":
            {
                ObjectProperty objectValue = ownerVariable.GetProperty<ObjectProperty>("ObjValue");
                if (objectValue is null || objectValue.Value <= 0 || !ownerVariable.FileRef.IsUExport(objectValue.Value))
                {
                    return false;
                }

                ExportEntry actorExport = ownerVariable.FileRef.GetUExport(objectValue.Value);
                string actorTag = actorExport.GetProperty<NameProperty>("Tag")?.Value.Instanced;

                if (string.IsNullOrWhiteSpace(actorTag)
                    && actorExport.HasArchetype
                    && actorExport.Archetype is ExportEntry archetypeExport)
                {
                    actorTag = archetypeExport.GetProperty<NameProperty>("Tag")?.Value.Instanced;
                }

                lookup = !string.IsNullOrWhiteSpace(actorTag)
                    ? actorTag
                    : actorExport.ObjectName.Instanced;

                if (string.IsNullOrWhiteSpace(lookup))
                {
                    return false;
                }

                sourceDescription = $"Conversation owner resolved via SeqVar_Object #{ownerVariable.UIndex} actor #{actorExport.UIndex} ({lookup}).";
                return true;
            }
            default:
                return false;
        }
    }

    private static bool IsStartConversationCandidate(ExportEntry export)
    {
        if (export is null)
        {
            return false;
        }

        return GetConversationProperty(export) is not null;
    }

    private static ObjectProperty GetConversationProperty(ExportEntry export)
    {
        return export?.GetProperty<ObjectProperty>("Conv")
               ?? export?.GetProperty<ObjectProperty>("m_pConversation");
    }

    private readonly record struct OwnerBindingInfo(string Lookup, string SourceDescription);
    private readonly record struct PlayerBindingInfo(string Lookup, string SourceDescription);

    private static string BuildNodeContext(ConversationExtended conversation, DialogueNodeExtended node)
    {
        string nodeType = node.IsReply ? "Reply" : "Entry";
        return $"{conversation.ConvName} | {nodeType} #{node.NodeCount} | ExportID {node.ExportID} | StrRef {node.LineStrRef}";
    }
}
