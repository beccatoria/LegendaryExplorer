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
    private readonly Func<string, IMEPackage> _openSourcePackage;

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
        IInterpPreviewActorBindingResolver actorBindingResolver, Func<string, IMEPackage> openSourcePackage = null)
    {
        _openSourcePackage = openSourcePackage;
        _sourceReader = sourceReader ?? throw new ArgumentNullException(nameof(sourceReader));
        _nodeDiscovery = nodeDiscovery ?? throw new ArgumentNullException(nameof(nodeDiscovery));
        _trackTargetReader = trackTargetReader ?? throw new ArgumentNullException(nameof(trackTargetReader));
        _actorBindingResolver = actorBindingResolver ?? throw new ArgumentNullException(nameof(actorBindingResolver));
    }

    public InterpPreviewDialogueResolution Resolve(InterpPreviewDialogueResolutionRequest request)
    {
        var diagnostics = new List<InterpPreviewDiagnostic>();
        var plans = new List<InterpPreviewPlanInstance>();
        InterpPreviewResolutionStamp stamp = null;
        InterpPreviewNodeDiscoveryResult discovery = null;
        InterpPreviewResolutionInput input = null;
        IReadOnlyList<InterpPreviewSourceReference> unresolvedReferences = [];
        if (request?.Conversation?.Export is null || request.Node is null)
            return InterpPreviewDialogueResolution.Unresolved([new(InterpPreviewDiagnosticSeverity.Error,
                "SOURCE_REQUEST_INVALID", "A conversation and one of its selected lines are required.")]);
        try
        {
            var sourceResult = _sourceReader.Read(new(request.Conversation, request.Node) { CancellationToken = request.CancellationToken });
            diagnostics.AddRange(sourceResult.Diagnostics);
            if (sourceResult.Snapshot is not { } source) return InterpPreviewDialogueResolution.Unresolved(diagnostics);
            stamp = new(request.RequestId, source.SnapshotId, request.SceneSnapshot?.Stamp,
                request.PreferFemalePlayer ? InterpPreviewPlayerVariant.Female : InterpPreviewPlayerVariant.Male);
            using var scope = new InterpPreviewSourceScope(request, diagnostics, _openSourcePackage);
            unresolvedReferences = scope.UnresolvedReferences;
            scope.Check();
            var graph = source.SequenceId is { } sequenceId ? scope.ReadGraph(sequenceId) : null;
            input = new(source, graph, request.SceneSnapshot, stamp, request.BranchChoices);
            discovery = _nodeDiscovery.Discover(new(source, graph)
            {
                CancellationToken = request.CancellationToken,
                BranchChoices = input.BranchChoices
            });
            diagnostics.AddRange(discovery.Diagnostics);
            bool complete = sourceResult.Outcome == InterpPreviewSourceReadOutcome.Ready
                && discovery.Outcome == InterpPreviewNodeDiscoveryOutcome.Ready;
            // Source-choice diagnostics describe the complete set, never a first chosen export.
            if (discovery.Candidates.Any(candidate => !candidate.IsHintOnly))
                diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Info, "INTERP_DISCOVERY_SELECTED",
                    $"Preserved {discovery.Candidates.Count} action/Data instance(s) from the selected line's graph."));
            foreach (var candidate in discovery.Candidates)
            {
                scope.Check();
                InterpPreviewInterpDataSnapshot copiedData = null;
                IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> copiedLinks = [];
                try
                {
                    bool instanceComplete = !candidate.IsHintOnly;
                    var dataExport = scope.Resolve(candidate.InterpDataId, "InterpData");
                    var data = dataExport is null ? null : scope.ReadData(dataExport, ref instanceComplete);
                    copiedData = data;
                    IReadOnlyList<InterpPreviewActionVariableLinkSnapshot> actionLinks = [];
                    InterpPreviewTrackTargetReadResult targets = null;
                    InterpPreviewActorBindingResult bindings = null;
                    if (candidate.ActionId is { } actionId && data is not null)
                    {
                        var action = scope.Resolve(actionId, "SeqAct_Interp");
                        if (action is not null)
                        {
                            var rawLinks = scope.ReadVariableLinks(action, ref instanceComplete);
                            // Proof of association is the action's actual Data link. Neither a cached
                            // node hint nor an unrelated action may lend its variable links to this Data.
                            bool associated = rawLinks.Where(link => link.LinkDesc.Equals("Data", StringComparison.OrdinalIgnoreCase))
                                .SelectMany(link => link.LinkedNodeExportUIndexes).Any(index => index > 0
                                    && action.FileRef.TryGetUExport(index, out var linkedData)
                                    && InterpPreviewSourceScope.Id(linkedData).Equals(candidate.InterpDataId));
                            if (associated)
                            {
                                actionLinks = rawLinks.Select((link, index) => new InterpPreviewActionVariableLinkSnapshot(link.LinkDesc,
                                    actionId, link.LinkedNodeExportUIndexes.Select(variable => new InterpPreviewSourceReference(actionId,
                                        $"VariableLinks[{index}]/{link.LinkDesc}", variable)).ToArray())).ToArray();
                                copiedLinks = actionLinks;
                                targets = _trackTargetReader.ReadTargets(new(actionId, data, actionLinks));
                                diagnostics.AddRange(targets.Diagnostics);
                                instanceComplete &= targets.Outcome == InterpPreviewTrackTargetReadOutcome.Ready;
                                if (targets.Targets.Count > 0 && request.SceneSnapshot is not null)
                                {
                                    bindings = _actorBindingResolver.Resolve(new(request.SceneSnapshot, targets.Targets));
                                    diagnostics.AddRange(bindings.Diagnostics);
                                    instanceComplete &= bindings.Outcome == InterpPreviewActorBindingOutcome.Ready;
                                }
                                else if (targets.Targets.Count > 0)
                                {
                                    instanceComplete = false;
                                    diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "ACTOR_BINDING_SCENE_UNAVAILABLE", "The copied scene snapshot is unavailable for target binding."));
                                }
                                // S5 replaces the existing group-label shortcut with actual variable authority.
                                if (targets.Targets.Any(target => target.Role == InterpPreviewTrackTargetRole.GroupVariable))
                                {
                                    instanceComplete = false;
                                    diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "TARGET_VARIABLE_RESOLUTION_PENDING", "Actors linked through this group have not yet been fully resolved; tag matches alone are inconclusive."));
                                }
                            }
                            else
                            {
                                instanceComplete = false;
                                diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "TARGET_ACTION_DATA_MISMATCH", $"{actionId} has no proven Data link to {candidate.InterpDataId}; no action bindings were borrowed."));
                            }
                        }
                        else instanceComplete = false;
                    }
                    else instanceComplete = false;
                    if (targets is null)
                        diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "TARGET_ACTION_UNRESOLVED", "Action-dependent target extraction is unavailable for this instance."));
                    instanceComplete &= data is not null;
                    plans.Add(new(source.NodeId, stamp, candidate, data, actionLinks, targets, bindings, instanceComplete));
                    complete &= instanceComplete;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    complete = false;
                    diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_CANDIDATE_READ_FAILED",
                        $"{candidate.InterpDataId}: {ex.Message}; other instances were retained."));
                    plans.Add(new(source.NodeId, stamp, candidate, copiedData, copiedLinks, null, null, false));
                }
            }
            scope.Check();
            scope.Dispose(); // Report cleanup before copying the durable diagnostics.
            complete &= !scope.CleanupFailed;
            AddSceneBindingDiagnostics(request, request.Conversation, request.Node, diagnostics);
            var outcome = plans.Count == 0 ? InterpPreviewResolutionOutcome.Unresolved
                : discovery.RequiresSelection ? InterpPreviewResolutionOutcome.NeedsSelection
                : complete ? InterpPreviewResolutionOutcome.Ready : InterpPreviewResolutionOutcome.Partial;
            return new(outcome, plans, diagnostics, stamp, discovery, scope.UnresolvedReferences, input);
        }
        catch (OperationCanceledException)
        {
            diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_CANCELLED", "Source discovery was interrupted; no complete plan is claimed."));
            return new(InterpPreviewResolutionOutcome.Partial, plans, diagnostics, stamp, discovery, unresolvedReferences, input);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new(InterpPreviewDiagnosticSeverity.Warning, "SOURCE_READ_FAILED", $"Source copying was incomplete: {ex.Message}"));
            return new(InterpPreviewResolutionOutcome.Partial, plans, diagnostics, stamp, discovery, unresolvedReferences, input);
        }
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

            using var importCache = new PackageCache();
            ExportEntry resolvedConversation = EntryImporter.ResolveImport(convImport, importCache);
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

}
