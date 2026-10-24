# Interp Preview M4 — Resolution Design and Codex Handoff

## Status and purpose

- Design pass complete; ready for Codex implementation in the order below.
- Production implementation: NOT STARTED by this pass. M4 tests and human validation: NOT RUN.
- Branch: `becca-LEX`. No commits requested or made; keep assistant planning artifacts uncommitted.
- Master: [InterpPreview_Plan.md](./InterpPreview_Plan.md).
- Preserve [M3.5 ownership invariants](./InterpPreview_M3_5_LifecycleDesign.md) and [thread ownership](./InterpPreview_M3_5_ThreadOwnershipDesign.md).

In plain language, M4 finds the scene data for a selected dialogue line and determines which actors its tracks refer to. When the files do not give a definite answer, it explains the uncertainty instead of selecting an arbitrary actor or scene. It does not play, move, animate, or pose anything.

Headless M4 work can start immediately. Live publication of plans must wait for the bounded scene-transaction prerequisite in section 2. Do not treat the previous M3.5 checkmarks as evidence of unobserved manual tests.

## 1. Current code and reuse boundaries

Paths below are relative to this directory unless stated otherwise; line ranges describe the code at design time.

| Existing code | Evidence and M4 consequence |
|---|---|
| `IInterpPreviewDialogueResolver.cs:14–66` | Request exposes live `ActorProxy` lookup; result holds one `ExportEntry`. Replace durable output with copied descriptors and stable identities. |
| `InterpPreviewDialogueResolver.cs:17–68` | Returns `node.InterpData` immediately or calls the singular core fallback. Does not enumerate node branches, groups, or track targets. |
| `../../../LegendaryExplorerCore/Dialogue/ConversationExtended.cs:268–359` | `nExportID` matches `BioSeqEvt_ConvNode.m_nNodeID`; traversal finds the first `SeqAct_Interp`, then first `Data.LinkedVariables` item, with depth 10. Useful schema evidence, not a complete M4 algorithm. Leave this shared editor API unchanged. |
| `../../../LegendaryExplorerCore/Dialogue/DialogueNodeExtended.cs` | Entry/reply distinction, node index, export ID, speaker and listener are separate fields. TLK string reference is not node identity. |
| `InterpPreviewSession.cs` | Resource ownership and Player replacement exist; broad lookup mixes tag, name, display text, and class. No scene generation or immutable actor catalog exists. |
| `InterpPreviewRuntime.cs:177–197` | Resolution attaches live session lookup and immediately publishes diagnostics. Operation identity exists elsewhere, but is not a scene generation. |
| `InterpPreviewDialogueResolver.cs:275–433,435–582` | Owner comes from `StartConversation` links, but first-match logic and conversion of direct object links to strings lose authority. A package with unrelated starts can stop the search before later packages. |
| `InterpPreviewShellWindow.cs:437–513,548–594` | Default Player package loading already exists, but only for a player-speaker node. Missing package/pawn can silently return NoChange. Toggle changes preference only. |
| `../LevelEditor/Matinee/MatineeTracks.cs:23–64,168–226` | Evidence for action `Data` links, group variable links, `m_eSFXFindActorMode`, and `m_nmSFXFindActor`. Do not instantiate these execution objects: they create actors and attach to Level Editor. |
| `../../../LegendaryExplorerCore/Matinee/MatineeHelper.cs:271–369` | Group/track array schema, Gesture `m_nmFindActor`, and camera `InterpTrackFloatProp.PropertyName = FOVAngle`. |
| `../../UserControls/ExportLoaderControls/InterpDataTimeline/InterpEditorTracks.cs` | Evidence for `InterpGroups`, `InterpTracks`, track classes, and secondary key arrays. Existing objects are UI-coupled, not immutable M4 contracts. |
| `../../../LegendaryExplorerCore/Kismet/KismetHelper.cs` | Reuse read-only link parsing where suitable. Do not call `ResolveObjectVarValue` as binding authority: tag results collapse to one actor, wrappers can recurse, and hench selection includes random fallback. |

### Evidence limits

The two tests added in the preceding session cover same-path cancellation in a load coordinator and reuse of an already-existing fake window. They are not an end-to-end shared-launcher transaction test. The preceding session reported a successful solution build and those two tests passing; this design pass does not claim a fresh test run.

The user reported successful general level loading and dialogue checks, and a working Male/Female preference control, but no visible Player or confirming log. This is not proof of Player resource replacement, failed-replacement preservation, close-during-load, or a ten-cycle soak. Record those checks separately rather than inventing passes.

## 2. Bounded prerequisite: a committed scene really is complete

`InterpPreviewShellWindow.ExecuteLauncherRequestAsync` currently commits/applies each level in a loop (lines 281–314), continues after a failed level, and can still resolve dialogue. A common request ID prevents some stale continuations but does not make the whole collection replacement atomic.

Before wiring M4 publication, add a deterministic regression using the production orchestration (factored behind injected loader/render/session seams if necessary): start with scene A; request replacement B+C; let B prepare successfully and C fail. Assert that A, its generation, and its render registration remain unchanged and all uncommitted resources are released. Add overlap and shutdown variants. Do not replace this with a stub that merely throws before mutation.

Correct only this workflow boundary, following the existing M3.5 design:

1. One accepted request owns collection preparation, optional Player preparation, commit, and result publication. Pass its identity through the work; do not start a new unrelated operation per item.
2. Stage all requested levels before mutation. Normalize/deduplicate paths before loading. A required-level failure, cancellation, supersession, or closing aborts the candidate set.
3. Build replacement membership and indexes off to the side. Swap the complete state on the dispatcher after the latest-request/lifecycle check. Advance generation once for the complete change; never per staged file.
4. Unregister retired actors before disposing them/packages, preserving current preview-only ownership. Do not alter shared Level Editor renderer behavior.
5. A missing optional default Player package is a resolution limitation, not a failed level collection: commit valid levels with an explicit Player-missing result. On a Player-only replacement failure, keep the old resource but report the requested variant unavailable; never label the old variant as the requested one.
6. Publish scene summary and dialogue result only for that request and committed generation. Recheck after awaits. Closing/obsolete results cannot update the window or bring it to the front.

Keep headless discovery/binding development independent of this fix. Do not reopen lighting, rendering fidelity, or unrelated AFC/DLC work. If fixing this prerequisite needs a broader ownership redesign, stop with the failing regression and a specific question rather than inventing a workaround.

## 3. Data contracts and ownership

Create preview-only value types (suggested new file: `InterpPreviewResolutionModels.cs`). Names may follow project conventions; these semantics are mandatory.

| Value | Required contents |
|---|---|
| Source object ID | Game, canonical full package path, positive export UIndex. Use ordinal-ignore-case Windows path equality; never compare by filename or UIndex alone. Imports retain their origin reference until resolved to an export ID. |
| Scene stamp | Session ID plus monotonically increasing committed generation. Session ID prevents two preview windows at generation 1 being confused. |
| Actor ID | Existing resource key plus actor export UIndex. Its source object ID enables exact cross-package matching. Resource kind/Player variant distinguishes separately owned representations of the same source. |
| Scene snapshot | Scene stamp, immutable resource/actor descriptors, tag-only index, source-ID index, explicit Player-role membership and actual loaded variant. No live `ActorProxy`, package, WPF collection, or delegate to session state. |
| Node ID | Conversation source ID, Entry/Reply, node array index, and `nExportID`. Export ID is a graph key, not a globally unique node ID. |
| Resolution input | Copied node/speaker metadata, copied node-scoped graph and track descriptors, scene snapshot, requested Player variant, latest-request ID, source snapshot ID, optional explicit branch/activation evidence. |
| Plan instance | Node ID, originating ConvNode/action ID, InterpData ID, group/track descriptors, binding results, branch edges/provenance, scene stamp and source/request stamp. Same InterpData used by two actions is two instances because group variable links can differ. |
| Binding request | Source group/track/key ID, target role (animated actor, look-at target, camera reference, speaker, listener, etc.), explicit target kind and value, source chain, expected multiplicity. |
| Binding result | Bound, Ambiguous, Missing, Unsupported, or NotApplicable; ordered candidates and evidence; selected identity only when justified. Group targets that genuinely represent multiple actors are not forced into a singleton. |
| Resolution result | Immutable plan instances, diagnostics and discovery completeness; outcome Ready, Partial, NeedsSelection, Unresolved, Stale, or RejectedClosing. Ready means structurally resolved for M4, not playable. |

Collections must be defensive copies, including nested records. An `IReadOnlyList` backed by a caller-owned mutable list is not sufficient.

### Generations and freshness

- Increment generation exactly once after each successful public scene mutation: collection add/replace/remove, Player add/replace/remove, or nonempty clear. Internal helper calls must not double-increment.
- Duplicate/no-change, failed/cancelled/superseded loads, resolution-only requests, and preference toggles do not advance scene generation.
- A preference change still invalidates a current resolution using the old preference. Clear the current result or mark it stale; re-resolve on the next node launch. The toggle itself does not become a new scene-load operation in M4.
- Capture immutable scene metadata on the owner dispatcher, then bind only against that snapshot. On publication verify Open lifecycle, current request, source snapshot, scene stamp, and requested preference.
- Clear/invalidate the current result on scene mutation and shutdown. A future M5 consumer must repeat the stamp check before translating IDs into actor proxies.
- Read source metadata anew for every launch; do not cache results by node number. Source edits require re-opening/re-resolving the node in M4. Use existing source-change notification seams to invalidate if available, but do not promise live editing synchronization or add a general editor event framework.

### Package reading boundary

A preview-only metadata reader (`InterpPreviewSourceReader.cs`) converts UE properties to the value types. Capture the selected node and unsaved conversation/sequence state on their owner dispatcher. Do not pass editor-owned packages into `Task.Run`, reopen them from disk and silently lose edits, mutate their properties, or dispose them.

For external imports/companion metadata, use an explicitly scoped reader-owned independent package/cache lifetime. Copy all required values before disposal. File I/O can run in the background; dispatcher reads must be bounded/cancellation-aware. The pure discovery/binding services never open files or create proxies. Missing imports remain diagnostic results, not broad automatic loading of every referenced level.

Migrate all preview consumers of `InterpPreviewDialogueResolution.InterpData` (runtime, shell, launcher, test factories) to descriptors/counts. Do not store a convenience live ExportEntry in a durable plan. Leave `DialogueNodeExtended.InterpData` and its shared editor consumers unchanged.

## 4. Node-scoped InterpData discovery

Implement a preview-only `InterpPreviewNodeDiscovery` over copied graph data.

1. Validate conversation/node membership and game. Distinguish Entry/Reply. Read `nExportID` without modifying the source node. Missing/zero ID has an explicit diagnostic, not a package-wide search.
2. Resolve the conversation's actual `Sequence` identity. Find all `BioSeqEvt_ConvNode` roots with matching `m_nNodeID` in that scope, preserving duplicate-root evidence. Do not merge other conversations merely because node IDs match.
3. Follow `OutputLinks` as graph edges, preserving source output index/label and destination input index. Collect every valid `SeqAct_Interp` and every valid `Data` variable-link InterpData, not just index zero. Validate referenced types and indices independently so one malformed link does not discard good siblings.
4. Preserve sequential actions as well as alternatives. Continue through linked actions within the selected node's execution scope; stop at `BioSeqAct_EndCurrentConvNode`, a different conversation-node event, or leaving the proven scope. Do not collect unrelated InterpData just because it shares a package/parent subtree.
5. Nested sequences/references require explicit entry/exit connector adapters; do not visit all child SequenceObjects indiscriminately. Unknown crossings produce an incomplete-discovery diagnostic with the source edge.
6. Use visited graph identities and preserve incoming edges at merges. Keep a graph, not an exponential list of every possible walk. Cycles terminate and are reported; repeated executions inside runtime loops are not simulated. Bound traversal (initial configurable limits: 10,000 objects and 50,000 edges) and check cancellation; reaching a limit means Partial, never complete.
7. Keep action+Data instance identity. Deduplicate repeated identical links while retaining their provenance. Order roots by source identity and links by stored array order; stabilize diagnostics/candidates separately. Never interpret sorted order as runtime branch choice.
8. Treat `node.InterpData` as a compatibility hint, not proof that enumeration is complete. Reconcile it with graph results. If graph discovery is unavailable but the hint is a valid InterpData, return a partial descriptor with `INTERP_HINT_ONLY`, unknown action-dependent bindings, and a clear limitation. If it disagrees with a resolved graph, report stale/conflicting hint; do not silently append unrelated data or choose it over the graph.

### Branches and scene shop

- Preserve alternative edges and the condition source. Without save-game/plot/runtime input, state is Unknown; never assume true, choose first, or concatenate exclusive branches as one timeline.
- The copied graph may accept explicit branch choices keyed by source node/connector. Validate that each choice belongs to this graph. Exactly one evidence-selected route can be selected; otherwise retain alternatives and return NeedsSelection. No new branch-picker UI is required by this pass.
- Duplicate actor candidates can be filtered only by proven activation evidence scoped to the same selected route. Unknown or conflicting state keeps them ambiguous. Level Editor marker/visibility preferences are not game activation evidence.
- The current branch has plot-check references (`SFXSceneShopNodePlotCheck`, `c_shopcondition`) but this inspection did not establish a complete scene-shop edge/timeline schema. Do not invent fields or treat ordinary Kismet links as proof of scene-shop support.
- During implementation, capture a small representative property fixture for each scene-shop/nested-sequence adapter before implementing it. If unavailable, emit `SCENE_SHOP_UNSUPPORTED` / `SEQUENCE_BOUNDARY_UNSUPPORTED`, preserve discovery limits, and leave the corresponding M4 checklist item open. Core M4 implementation can proceed; full M4 sign-off requires the claimed fixture coverage or an explicit user-approved scope change.
- Do not execute plot transitions, scripts, random selection, or arbitrary Unreal runtime logic.

## 5. Actor resolution rules

Implement `InterpPreviewActorBindingResolver` using the scene snapshot. Keep source extraction separate from candidate selection.

### Authority is semantic, not a generic fallback chain

1. Decode the target mode/role from the applicable class/game schema. A track-specific explicit target governs that track; a group link governs group-target tracks. Do not let an unrelated direct reference elsewhere override an explicit track tag.
2. Within a declared source, an authoritative direct object reference resolves by full source identity. If that actor is not loaded, return Missing with its package/ID. Do not fall back to a same-tag actor and conceal the missing file.
3. A find-by-tag source searches only actor tag metadata, case-insensitively with whitespace trimmed and instanced name preserved. No class/display-name/object-name matching, prefix guessing, or alias expansion. Empty/None is not a tag.
4. Resolve exact `GroupName` to `SeqAct_Interp.VariableLinks.LinkDesc` for that action instance; decode all linked variables and preserve their order/source. GroupName by itself is not an actor tag.
5. Group inheritance is allowed only where the track schema declares it. A missing/unsupported explicit target is not permission to bind to the current speaker.
6. Candidate order is stable by source path, export UIndex, and resource identity, independent of level-load order. Deduplicate identical actor IDs but retain separate evidence chains.
7. Multiple possible singleton candidates return Ambiguous with no selected actor. An explicit multi-object/list variable retains a multi-target binding when the consuming track supports it; otherwise report unsupported multiplicity.

### Variable adapters

| Source | Rule |
|---|---|
| Direct Actor export / `SeqVar_Object.ObjValue` | Preserve exact export identity (including resolved import provenance). Require Actor type; never convert to its Tag as the binding authority. |
| `BioSeqVar_ObjectFindByTag` | Read schema-appropriate Name/Str tag fields. Current preview uses `m_sObjectTagToFind`; core helper also contains pre-game-3 `m_sActorTagToFind`. Cover observed forms with per-game fixtures; conflicting nonempty fields are diagnostic, not a silent winner. |
| `BioSeqVar_ObjectListFindByTag` | Preserve list semantics and tag candidates; do not select the first actor. Verify class/field shape in fixture. |
| `SeqVar_Player` | Resolve explicit canonical Player role for the requested variant. Do not call the package-opening core helper. |
| `SeqVar_External` | Follow `VariableLabel` / parent sequence variable-link mapping; retain all legitimate linked variables. |
| `SeqVar_Named` / `SeqVar_ScopedNamed` | Resolve `FindVarName` against `VarName` in documented scope. Scoped lookup does not silently escape scope. Multiple matches remain ambiguous. |
| Dynamic hench/runtime variable or unknown class | Unsupported/dynamic with source ID; never random squadmate or alias-expanded tag. |

Wrapper traversal needs a visited set and a bounded chain depth (initial limit 64). A cycle, broken import, or exceeded limit is an explicit diagnostic.

### Owner

- Owner is a logical role, not a request to find an actor literally tagged `owner`.
- Match `StartConversation` (`Conv` / `m_pConversation`) to the actual conversation reference. Prefer exact export/import identity, including game and package context. A short conversation name alone cannot prove a match across files.
- Where localization uses an unresolved conversation import, a fully qualified import/object path plus validated companion/package context may identify candidates; retain uncertainty rather than matching on bare names.
- Search copied metadata from loaded levels, conversation sequence/package, and a valid non-LOC companion. Do not stop just because an earlier package has unrelated StartConversation objects.
- Follow every relevant `Owner` variable link through the adapters above. A caller-provided exact StartConversation/selected branch can disambiguate; otherwise conflicting starts remain alternatives, not a first-loaded-level choice.
- Preserve canonical tags such as `hench_mystic` exactly. Direct links remain direct even if the actor has no tag, has an archetype tag, or shares a tag with another actor.

### Player

- Canonical logical label is always `player`. Do not introduce `shepard` or global `BioPawn_*` lookup aliases.
- Separate requested variant from actual committed Player-resource variant and from render availability.
- Use explicit Player-role membership in the snapshot. A random level actor named BioPawn_0/1 does not qualify. An explicit source link to a pawn still resolves as that exact actor; it does not redefine the canonical Player role by name.
- Discover Player requirements across the speaker, listener, all group variables, and all track targets. A non-player speaking line can still move/look at the player.
- Orchestration may reuse the existing default-package loader when any discovered requirement needs Player: ME1 `EntryMenu.SFM`; LE1 `EntryMenu.pcc`; ME2/3 and LE2/3 `BioP_Char.pcc`; Female `TheWorld.PersistentLevel.BioPawn_1`, Male `...BioPawn_0`. These are the existing default mappings, not a promise of custom player appearance or game-position accuracy. Unsupported game values do not use a catch-all mapping.
- Resolve requirements first, stage any needed Player resource, then bind against the final committed snapshot. Pure resolver code never loads or creates actors.
- Missing file, pawn export, or renderable proxy must have a distinct reason. No false success from NoChange and no incorrect match to the opposite variant.
- Preserve the current toggle workflow: show “Player choice: Male. Reopen the dialogue node to apply.” No immediate automatic reload on toggle is required. Reopening must report actual loaded/reused/missing variant and canonical binding result.

## 6. Track target inventory, not track playback

Create a small explicit schema reader (suggested `InterpPreviewTrackTargetReader.cs`). Copy group/track IDs, classes, source order, disabled/active-condition metadata and every recognized target role. Do not reflect over every NameProperty and assume it names an actor.

| Data shape | Initial interpretation / evidence |
|---|---|
| `InterpData.InterpGroups` → `InterpGroup.InterpTracks` | Enumerate every referenced group/track. Bad links reported separately. Duplicate names are not unique IDs. |
| `InterpTrackMove`, `InterpTrackAnimControl`, property tracks including FOV | Primary target is parent group's resolved actor(s). `m_eSFXFindActorMode = UseGroupActor` uses action variable links; `FindActorByTag` uses `m_nmSFXFindActor`. Do not copy the legacy uncertain fallback between modes. Missing mode requires game/class default evidence or a partial result. |
| `BioEvtSysTrackGesture` | Track-level `m_nmFindActor` is evidenced by MatineeHelper presets. Resolve the explicit track target even when it differs from the speaker/group. Inspect associated find mode/defaults in the fixture. |
| LookAt / SetFacing / SwitchCamera | Timeline reader proves classes and arrays `m_aLookAtKeys`, `m_aFacingKeys`, `m_aCameras`, not all nested target fields. Add fixture-backed adapters for primary and secondary targets; unknown fields/modes yield Unsupported, not guessed actor names. |
| Director/camera selection | May refer to a camera group or stage reference rather than an Actor tag. Preserve typed group/stage reference and resolve only proven schema. Do not require an actor for an actorless director/effect track. |
| `FindActorByNode`, dynamic stage/camera generation | Existing Matinee code explicitly lacks semantics. Preserve source and diagnostic; no synthetic actor creation in M4. |
| Unsupported/disabled track | Still inventory it; indicate disabled/conditional status and any proven targets. Unsupported evaluation is separate from unsupported target extraction. M5/M6 implement motion, animation, key evaluation, and stage placement. |

No global guarantee that every game/track is supported. Keep a tested support table in the implementation notes. Do not mark the “all non-speaker targets” checklist complete while representative primary/secondary target fixtures are unimplemented.

## 7. Diagnostics and user-visible acceptance

Use existing resizable Diagnostics / Copy Log UI; no Level Editor redesign. Extend diagnostics with optional node/action/group/track/key source IDs and candidate IDs rather than forcing users to decode prose.

Required categories: invalid input, missing sequence/data, hint-only/conflicting hint, incomplete traversal, unresolved import, ambiguous branch, unsupported scene shop, missing direct actor, ambiguous tag, missing/conflicting owner, requested/loaded Player mismatch, missing Player package/pawn/proxy, stale result, unsupported track target/mode.

Stable codes can include `INTERP_HINT_ONLY`, `INTERP_DISCOVERY_INCOMPLETE`, `BRANCH_SELECTION_REQUIRED`, `ACTOR_DIRECT_MISSING`, `ACTOR_TAG_AMBIGUOUS`, `OWNER_AMBIGUOUS`, `PLAYER_PACKAGE_MISSING`, `PLAYER_VARIANT_UNAVAILABLE`, and `RESOLUTION_STALE`. Preserve existing diagnostic codes where their meaning remains accurate; update tests/consumers when replacing them.

Examples of plain-language summaries:

- “Found 2 possible scene paths for this line. No path selected.”
- “Track #123 needs actor 'hench_mystic'. Two actors match; neither has been selected.”
- “Track #123 points to actor #456 in Example.pcc. That actor is not in the loaded scene.”
- “Player: Female loaded from BioP_Char.pcc. Bound as 'player'.”
- “Player: Male requested, but its package was not found. Player tracks remain unresolved.”
- “Scene changed. Reopen the dialogue node to refresh its actor matches.”

Do not say “ready for playback.” Replace the shell's singular `InterpData #...` success wording with counts and structural resolution status. Show loaded Player information even when the selected speaker is not Player. General binding diagnostics must not depend on a speaker record being present.

## 8. Automated test matrix

Use MSTest and synthetic in-memory packages / copied descriptors; follow `InterpPreviewOwnershipTests.cs` for package construction. No installed game, GPU, WPF window, or external binary fixture required for core logic. Use small sanitized property fixtures for verified per-game schemas. Reader tests exercise real UE property parsing; pure tests exercise selection; integration tests use real production orchestration with controlled fakes only at I/O/render boundaries.

| Area | Required assertions |
|---|---|
| Node identity | Entry/reply and two conversations with same numeric node ID stay separate; null/zero/missing node data has explicit outcome. |
| Discovery | One direct route; route longer than 10 hops; sequential Interps; parallel/conditional routes; multiple Data links; duplicate roots; duplicate identical links; shared Data under different actions. |
| Boundaries | Cycles terminate; merge preserves incoming alternatives; stop at other node/end-node; nested connectors verified; malformed/imported links preserve good siblings; limit/cancel marks incomplete. |
| Cached hint | Reconciled hint; stale hint conflict; valid hint-only partial result; wrong-class hint rejected. |
| Scene identity | Equal UIndex in different packages differs; case/slash-normalized path matches; same filename in different directories does not; multiple resource representations remain distinguishable. |
| Group/track | Move via action group link; Gesture explicit tag differs from speaker; FOV uses camera group; non-speaker primary and secondary targets; actorless tracks; duplicate group names; missing/unknown find modes. |
| Tag lookup | Display/class/name collisions do not count as tags; canonical casing/instanced names; empty/None; duplicate tags retain sorted candidates independent of load order. |
| Direct links | Exact reference wins within its declared source; missing direct target never falls back to matching tag; import identity; no-tag actor; duplicate-tag actors; multiple explicit targets. |
| Variables | Named/scoped/external chains, multiple linked vars, cycles, broken links, verified pre-game-3 and game-3 tag fields; dynamic hench remains unresolved. |
| Owner | Tag Owner; direct Owner; multiple StartConversation owners; earlier unrelated package does not stop later valid match; same short conversation name in another file does not match. |
| Player | Both variants, reuse, missing file/export/proxy; wrong variant cannot pass; non-player speaker whose track/listener needs Player; unrelated BioPawn_1 not treated as player; source Tag never mutated. |
| Branch/activation | No input keeps Unknown; valid explicit choice filters route; invalid choice rejected; duplicate-tag active/inactive fixture selects only with proven branch-scoped evidence; conflicting/unknown evidence stays ambiguous. |
| Snapshot | Defensive copy survives input mutation and package disposal; no live exports/proxies in plan; scene/session/source/request stamps validated. |
| Generation | Successful scene/Player change advances once; failed/cancelled/duplicate/resolution-only operation does not; toggle invalidates preference-bound result without fake scene mutation; old-window stamp rejected. |
| Publication | Failed B+C replacement preserves A; overlapping launch requests cannot publish old plans/logs; close during source reading/loading prevents publication; stale candidate resources released. |
| UI summary | Counts/status distinguish Ready, Partial, NeedsSelection, Missing; changing toggle is not reported as loaded; no claim that binding success proves visible rendering. |

Before sign-off run the full Interp Preview test set, not only newly added methods, plus solution build. Record actual counts and failures. Validate relevant Level Editor regressions if any shared interfaces/callers change. A test-discovery miss is not a passing test run.

## 9. Ordered implementation slices for Codex

Each slice ends with its focused tests/build check and a master-plan note. Do not implement all of M4 as one unreviewable patch.

1. **Create resolution value contracts** in `InterpPreviewResolutionModels.cs` with identity/immutability tests in a new `InterpPreviewResolutionModelsTests.cs`.
2. **Implement scene snapshots and generation** in `IInterpPreviewSession.cs` / `InterpPreviewSession.cs`; extend ownership tests to cover every mutation path and no-change outcome. Preserve existing broad lookup for other callers; add a separate typed binding catalog.
3. **Create the source metadata reader** in `InterpPreviewSourceReader.cs` with reader tests for real in-memory UE properties, source lifetime, imports, and invalid indices. Keep external reads injected/scoped.
4. **Create node discovery** in `InterpPreviewNodeDiscovery.cs` with `InterpPreviewNodeDiscoveryTests.cs`; implement scoped multi-result traversal and branch graph preservation, not scene execution.
5. **Create target extraction** in `InterpPreviewTrackTargetReader.cs` with fixture-backed schema tests. Cover Move/group variables and Gesture direct tags first; add verified secondary/camera/scene-shop adapters without guessing. Log unsupported cases and leave corresponding exit criteria open.
6. **Create actor binding** in `InterpPreviewActorBindingResolver.cs` with tests for direct identity, tags, Owner, Player, wrappers, candidate ordering, multiplicity and activation evidence.
7. **Fix the bounded launcher transaction prerequisite** through the existing runtime/session/shell seams (section 2). Add production-orchestration failed-collection/overlap/shutdown tests before durable M4 publication. Reuse prepared/owned resources; no parallel second lifecycle subsystem.
8. **Integrate the resolver** in `IInterpPreviewDialogueResolver.cs`, `InterpPreviewDialogueResolver.cs`, `IInterpPreviewRuntime.cs` and `InterpPreviewRuntime.cs`: discovery requirements → optional Player preparation → final scene snapshot → pure bind → guarded publication. Update runtime tests and preview-only legacy result consumers.
9. **Update preview feedback** in `InterpPreviewShellWindow.cs` and launcher consumers as needed: counts/ambiguity, actual Player outcomes, preference-only toggle wording, invalidation. Preserve launcher entry points, current Level Editor UI and visual loading behavior. Add summary-format tests rather than relying only on screenshots.
10. **Validate and document M4**: full focused tests + solution build; record schema support/limitations, then request the short human checklist below. Mark automated vs human results separately. No commit unless requested.

Suggested first Codex session: slices 1–2 only, then continue in order. This produces concrete progress without mixing graph changes with lifecycle changes. Return to a reasoning review only for a demonstrated architectural blocker or one bounded end-of-M4 review; do not begin another open-ended review cycle.

## 10. Human checks after implementation (not before)

The UI must make each expectation visible. Ask the user to:

1. Open a known dialogue line and confirm the log lists scene data and actor matches, or explains what is missing. No playback expected.
2. Open an NPC-speaking line with a non-speaker target and confirm that target is listed too.
3. Change Male/Female, reopen the appropriate node, and report the explicit loaded/missing Player message. Seeing the button change alone is not the test. A missing package is a supported outcome with a clear reason, not proof of visual Player replacement.
4. If a supplied fixture has duplicate tags or alternative branches, confirm the log reports alternatives instead of silently selecting one.
5. Replace/unload the scene and confirm the old binding summary is cleared/marked stale; reopen a node to rebuild it.
6. Keep Level Editor open while launching from Dialogue Editor, confirming the existing visual-loading workflow is unchanged.

Retain the separate M3.5 close/failure/soak checklist for what remains unreported. Do not mark a human check complete until the user reports the actual outcome. Playback, animation, lipsync, exact player appearance/placement, and camera motion remain later milestones.
