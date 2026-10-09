# Level Editor upstream integration — master progress

## Current checkpoint

**Implementation is authorized.** Work stays on `becca-LEX`; nothing is pushed automatically.

- Current checkpoint: **phases 1–12 complete for the practical milestone; user reports being happy that everything is working and accepts delivery. Fresh solution/test-project builds passed. Full suite: 138 tests, 135 passed, 0 failed, 3 existing Castle-proxy skips. Actual tool locations and coverage limits are documented below. Interp is launched separately; its game-shader/new-lighting activation and broader work remain deferred**.
- Technical integration: **complete for the agreed practical scope, not exhaustive modernization of every preview or game asset**. Renderer, layout, navigation/settings, shared visibility/light policy, live snapshots, prefab visibility, group/undo rebinding and practical shared-preview contracts are implemented/tested. Final combined manual acceptance remains separate. Dynamic-shadow appearance in the current scene is inconclusive and explicitly non-blocking.
- User visual validation: **the user confirms the previously requested lighting/group checks were already performed**, alongside accepted volumetrics, blue highlighting and baseline lighting recovery. Do not repeat those checklists as a phase gate. Dynamic-shadow toggling is not visually established in the current scene; final combined integration acceptance remains separate.
- Reference upstream snapshot: `e798b5a1e6a5b820ba72d33223e82009a26439eb` (`upstream/Beta` as fetched for this work).
- Starting fork commit: `ae24c5335ff5718181ead28b17509195209947a6`. The user committed the recent fixes after planning; the working tree was clean when implementation began.
- Recovery branch: `backup/level-editor-integration-20261009-103143`.
- Recovery after sleep: `backup/level-editor-phase2-20261009-124426` preserves tracked phase-2 work. All eight changed/untracked files were also copied to `LegendaryExplorer/.vs/LevelEditorIntegrationRecovery/20261009-124426` (including the master document, tests, helpers, and local instructions). These recovery copies are local, not committed.
- Saved settings backup: `C:\Users\becca\AppData\Local\Temp\becca-LEX-LevelEditor-settings-20261009-103215\RECENTSETS`. Original settings were not modified. This local backup contains private file paths and is not committed.

Update this document at each implementation checkpoint. Record builds/tests separately from user visual acceptance. An unfinished phase must not be marked complete merely because the solution compiles.

## Agreed outcome

Use upstream's new renderer and redesigned interface as the foundation, and adapt the fork's custom tools to it. Reworking code is acceptable; losing the user's capabilities is not.

Preserve:

- Existing extended actor types and markers: camera/capture actors, BioStages and editable stage nodes/cameras, lights and collection lights, decals as currently represented, sounds, emitters, location actors, meshes, and volumes.
- Visible Sets Manager, class-level settings, per-actor exceptions, clear/show-all/isolate/add/remove/nearby operations, distance controls, and recent-set persistence.
- Individual visibility toggles and existing shortcuts; ordinary enabled props must remain solid while unwanted volumes can be hidden.
- Ctrl multi-selection and blue highlighting on every selected actor, with a primary actor for the gizmo.
- Create/ungroup/reselect transform groups, grouped translation/rotation/scale, read-only exclusions, and batch undo/redo.
- Turbo movement (default multiplier 10), Shift slow movement (0.25), Ctrl fast movement and their current combination rules, focus and orthographic views.
- Camera coordinate copy/paste and existing actor transform copy/paste behavior.
- Multi-file load order, collapse state, commit/save/reload behavior, and package/file-lock protections.
- Package Editor, Dialogue Editor, and Interp Preview loading/rendering and existing resource/dispatcher ownership.

Browsing groups in the new outliner are distinct from transform groups. Lighting mode is distinct from object display mode. Showing engine-hidden editing aids must not mutate their game properties.

### Approved light policy

- With global light-marker display enabled, individually hiding a light disables its real-time contribution.
- With global marker display disabled, hide markers but let all loaded lights contribute. Retain individual hides for re-enabling marker display.
- The separate Dynamic Lights switch still controls dynamic illumination.
- Marker distance and camera culling must not accidentally switch illumination off.
- Baked contributions cannot be subtracted per light from existing lightmaps; retain the upstream Lightmaps control and explain this limitation.

## Master implementation checklist

| Phase | Work | Technical status | User validation |
|---|---|---|---|
| 1 | Capture working baseline, recovery point, saved settings, fresh build/tests | Complete | Baseline behavior previously reported by user; new integration untested |
| 2 | Establish shared editor-state contracts for visibility, selection, and groups where needed | Complete — visibility/selection bridge wired; existing group contract preserved | Covered by accepted later visibility/group checks |
| 3 | Integrate the coherent upstream Scene3D rendering bundle | Complete — source and callers/loading connected through phases 4–5 | Materials/volumetrics/lighting visually exercised; combined acceptance pending |
| 4 | Adapt fork actor/component proxies with upstream lighting and editing fixes | Complete — build and automated tests passed | Later accepted scene/group checks supersede original smoke request; asset coverage is not exhaustive |
| 5 | Integrate per-level lighting/shader/resource ownership | Complete — build/tests passed; lighting/floor behavior confirmed | Floor resolved and light behavior accepted; resource lifecycle combined checks pending |
| 6 | Connect unified visibility and approved light policy to renderer/picking/shadows | Complete for milestone — visibility/light policy, live snapshots and prefab instance visibility implemented/tested | Prior lighting checks accepted; dynamic-shadow appearance inconclusive/non-blocking in current scene |
| 7 | Restore multi-selection and transform-group editing on upgraded renderer | Complete — transforms/highlight, stable group/undo rebinding and collection serialization tested | User confirms prior checks already performed; group transforms/highlight accepted |
| 8 | Integrate redesigned interface with all custom tools | Complete — layout/theme, group drag and runtime regressions validated | User reports all done and tests successful |
| 9 | Restore navigation and backward-compatible saved view settings | Complete — modifier parity, camera/recent-state compatibility and new settings tested | User confirms this is working |
| 10 | Adapt shared preview consumers without unrelated lifecycle changes | Complete for practical milestone — pass/light contracts tested; existing shader defaults and separate legacy viewer retained | User tentatively accepts preserved partial Interp functionality; game shaders/new lighting not visually established and deferred |
| 11 | Add focused regression coverage for the integrated behavior | Complete — coverage audited; eight deterministic regressions added; full suite passed with three existing skips | Not applicable to automated tests |
| 12 | Validate combined editor and publish tool guide/coverage report | Technical delivery complete — fresh builds/full suite passed; actual guide and coverage report published | Final combined acceptance pending — see limited checks below |

### Durable progress tracker

This checklist survives chat loss. Use the table above for validation details; checked items mean technical implementation only.

- [x] Phase 1 — source/settings recovery and baseline.
- [x] Phase 2 — shared visibility/selection state.
- [x] Phase 3 — upstream rendering bundle.
- [x] Phase 4 — fork actor/component integration.
- [x] Phase 5 — per-level light/resource ownership.
- [x] Phase 6 — visibility/light policy and live snapshots; dynamic-shadow appearance documented as non-blocking/inconclusive.
- [x] Phase 7 — group/highlight, proxy/undo rebinding and collection coverage.
- [x] Phase 8 — redesigned layout with custom tools; user accepted.
- [x] Phase 9 — navigation and saved settings; user accepted.
- [x] Phase 10 — shared preview compatibility for practical milestone; broader Interp renderer work deferred.
- [x] Phase 11 — integrated regression tests; 138 total, 135 passed, 0 failed, 3 existing skips.
- [x] Phase 12 — automated delivery validation and actual tool guide; final user acceptance remains separate.
- [x] User visual acceptance of the practical integration — user reports being happy that everything is working; not exhaustive asset validation.

### Phase details and execution rules

1. Preserve all current tracked, staged, and untracked work, then capture recoverable source/settings backups and fresh validation. Do not overwrite instructions or discard user work.
2. Separate shared selection/visibility contracts from WPF list items and rendering modes where necessary. Retain existing commands, group lifetime, and serialization. Helpers must be exercised by real callers and tests, not empty future scaffolding.
3. Integrate shaders, C# constant/vertex layouts, material fallback, textures/mips, depth/projection, MSAA/render targets, HDR/resolve, hit buffers, caches/batching, lighting/shadow/culling files as a coherent subsystem. Do not add stub APIs or restore whole old files to hide incompatibilities.
4. Combine lighting registration, bounds/hidden/transform hooks and upstream fixes with every existing fork proxy. Retain BioStage editing/skinning, marker picking, and `IActorEditorContext`. Invalidate geometry/light caches after edits and undo without losing collection actors.
5. Add upstream light ownership to per-level load/add/remove/reload/commit flows. Preserve package snapshots/locks, load order, cancellation/replacement and disposal ownership. Establish a compilable backend checkpoint across phases 3–5.
6. Use a shared logical visibility policy for base/hair/translucency/lighting receivers/collision/picking/gizmos/dynamic shadows. Separate viewport frustum culling from logical visibility so offscreen geometry can cast shadows. Implement and version the approved light policy without deleting lights or editing game flags.
7. Preserve all-selected blue highlighting in game and fallback shader paths, Ctrl toggling, primary gizmo attachment, grouped transforms/batch undo, and actor-identity remapping when proxies are reconstructed.
8. Adopt upstream XAML/styles/outliner/lighting controls, then restore custom tools, stage details, file-order/collapse behavior, shortcut guards, and focus behavior. Do not confuse category/level browsing groups with transform groups.
9. Wire current movement combinations, camera/actor clipboard operations, focus/orthographic controls and recent-set data into the new interface. Read old settings compatibly; do not clear them to make the integration appear to work.
10. Adapt ActorPreviewControl, shader-scan tools and Interp Preview passes/resource loading. Respect existing session/dispatcher boundaries and verify Dialogue Editor loading with Level Editor open. No unrelated conversation/lifecycle redesign.
11. Cover visibility and light policy, selection, transforms/undo, proxy replacement, and old saved-state compatibility with game-independent tests. Extend shared-preview tests only where contracts change; use installed game fixtures only when available.
12. Build, run relevant tests and provide staged manual checklists. Publish actual functionality coverage and final tool locations. Do not mark user visual validation complete without their report or push without a request.

## Requested upstream coverage

All eight commits were already in Git ancestry, but their direct editor implementation had largely been removed during the previous conflict/compatibility restore. Phases 3–10 restored/adapted the coherent renderer and callers rather than relying on ancestry. The statuses below describe the delivered practical milestone, not a guarantee that every game asset renders correctly.

| Commit | Required functionality | Current integration status |
|---|---|---|
| `31d2bccb9` | Game shaders, compatible vertex/material layouts, shader loading/caches, fallback, actor preview and scan tooling | Level Editor renderer integrated; shared preview passes adapted and scan readers audited. Existing preview shader defaults retained; Interp game-shader activation and legacy viewer port deferred |
| `c608dfa51` | MSAA/sample fallback, mipmaps, backface culling, reversed depth, correct projection/picking, linear/HDR resolve | Coherent renderer bundle integrated; selection resolve shaders tested at 1/2/4/8 samples. GPU/device and asset coverage is not exhaustive |
| `95d7d5b24` | Level lights, per-level light ownership, texture/vertex lightmaps, dynamic interactions, lighting/translucency passes | Integrated; approved visibility policy, non-writing live snapshots, package membership and preview pass order covered by tests; Level Editor lighting accepted |
| `4f8cd11c4` | Frustum culling, caches, resource-binding reuse and batching | Included in integrated renderer; visibility ownership adapted. No performance benchmark or exhaustive GPU cache/lifetime claim |
| `5929001ad` | Redesigned layout/styles and category/level outliner | Dark docked layout delivered with fork tools and collapsible file/category browsing; runtime XAML tests pass and user accepts appearance |
| `6ae96003e` | Unlit/Preview/Level controls plus Dynamic Lights/Lightmaps | Separate viewport lighting controls delivered; defaults and saved-state compatibility tested |
| `6fde52a4e` | Sky/directional lighting, light environments, geometry, attenuation and dynamic shadows | Renderer support integrated; visibility/shadow eligibility wired. Dynamic-shadow appearance in the tested scene remains inconclusive and non-blocking |
| `d90753abe` | Collection actors surviving edits, stunt-actor hair movement, LMT_1D swizzling, skeletal lighting without LightEnvironment | Collection transform serialization/reconstruction/undo tested; skeletal/component adaptation and renderer lighting support retained. No independent exhaustive hair/LMT_1D/skeletal asset visual acceptance claim |

Already merged unrelated compiler/Doxygen changes are not to be reverted.

## Key integration surfaces

- `LevelEditor.xaml` / `.xaml.cs`: interface, commands, visibility/selection/groups, file loading, recent-set data.
- `Actors.cs` / `Components.cs`: fork actor support plus upstream lighting/transform contracts.
- `LevelEditorRenderContext.cs` / `IActorEditorContext.cs`: shared rendering, visibility, selection, picking and registrations.
- `OpenLevelFile.cs`: actor/light/package ownership, file ordering and collapse state.
- `UndoSystem.cs` / `UIElement.cs`: group undo and transform widgets.
- `Scene3D/` and `Resources/LevelEditorShader.hlsl`: coherent rendering implementation and matching shader layouts.
- `Dialogs/VisibleSetsManagerDialog.xaml(.cs)`: existing class/per-actor semantics.
- `LegendaryExplorerCore/Packages/PackageExtensions.cs`: audit inherited-property reference safety; foreign indices are package-specific.
- `UserControls/ExportLoaderControls/ActorPreviewControl.xaml(.cs)` and shader-scan experiment/menu files: upgraded renderer consumers.
- `Tools/InterpEditor/InterpPreviewRenderCoordinator.cs`, loader/realizer/session/ownership code: compatible shared previews.
- `LegendaryExplorer.Tests/Tools/LevelEditor/` (new coverage) and existing `Tools/InterpEditor/`, plus relevant core tests.

## Validation ledger

### Initial baseline run — before integration edits

- Full solution build: **passed**.
- Initial `LegendaryExplorer.Tests` run: **68 executed; 59 passed; 9 failed**. After explicitly rebuilding the test project, these failures no longer reproduce. Treat this initial run as outdated discovery/build evidence, not nine unresolved source failures.
- Initial reported failures (historical):
  - `CancelPendingLoad_Throws_WhenDispatcherAccessDenied`
  - `OwnedResource_DisposesActorsBeforePackage`
  - `LoadLevelAsync_InvokesReplaceCallback_WhenReplaceRequested`
  - `LoadLevelAsync_SecondLoadCancelsFirstInFlightLoad`
  - `LoadedLevel_ToOwnedResource_TransfersOwnershipOnlyOnce_AndDisposalTransfersToOwnedResource`
  - `Session_PlayerContext_IndexesCanonicalPlayerAlias`
  - `LoadedLevel_Dispose_IsExactOnce_ForAbandonedCandidate`
  - `LoadLevelAsync_ReturnsCancelled_WhenCancelPendingLoadCalledDuringLoad`
  - `NewerRequest_SupersedesOlderRequest_BeforeCommit`
- Observed causes in that initial run include dispatcher-denied disposal, Castle proxy `TypeLoadException` (`set_IsModified`), and load-coordinator assertions. Existing tests were not modified in this implementation checkpoint.

### Phase 2 — shared-state checkpoint

- Added `EditorVisibilityState.cs`: owns case-insensitive visible actor keys, hidden class keys, user-edited flag and the existing membership/distance predicate. The saved key remains `packagePath|exportIndex`.
- Added `EditorSelectionState.cs`: owns primary and multiple selected actor identities independently of WPF list items.
- Connected both states to `LevelEditorRenderContext` and the existing editor. Selection-change boundaries synchronize the list into shared selection state; render passes consult that state for blue highlighting instead of rebuilding list selection for each pass. Whole-scene unload clears selection.
- Existing visibility commands, saved-view schema, navigation, primary gizmo/BioStage selection and transform-group implementation remain in place. This is not phase 6's full pass/picking/shadow policy integration or phase 7's upgraded-renderer group validation.
- Added 15 game-independent tests: identity separation/case handling, membership/distance boundaries, old saved-view round-trip, multiple selected keys, deselection/replacement/clear, primary selection without a list, and matching reconstructed actor identity.
- Full solution build: **passed**. Explicit test-project build: **passed**.
- Focused tests: **8 visibility tests + 7 selection tests passed**.
- Authoritative rebuilt full `LegendaryExplorer.Tests` run: **86 executed; 83 passed; 0 failed; 3 skipped**.
- Existing skipped tests: `OwnedResource_DisposesActorsBeforePackage`, `LoadedLevel_ToOwnedResource_TransfersOwnershipOnlyOnce_AndDisposalTransfersToOwnedResource`, and `LoadedLevel_Dispose_IsExactOnce_ForAbandonedCandidate`. They report an existing environment-specific Castle proxy `set_IsModified` incompatibility. No new skip/workaround was added.
- Test Explorer initially ran zero new tests until the test project was explicitly rebuilt and discovery refreshed. Count actual executed tests, not discovery alone.
- User visual validation: **pending**; no claim that the new lighting/layout is implemented or tested.

### User visual acceptance — all pending for the new integration

### Phase 3 — renderer source checkpoint

- Reintroduced the exact pinned upstream `Scene3D` source through Git: 11 changed renderer files and 10 previously removed lighting/shadow/culling support files. Actors, Components, editor controls, shared state helpers, tests and local instructions were not replaced.
- Forward-ported turbo/Shift-slow/Ctrl-fast movement and combination rules, key-tap movement, original mouse/scroll sensitivities, safe initial FPS calculation, optional coordinate/debug overlay, and the actual double-click input route.
- Full solution build: **passed** after these adaptations.
- This is a source/backend checkpoint, not complete visual delivery. Component shader/lighting registrations and full render-pass/loading integration are still pending in phases 4–5; do not claim game lighting is enabled merely because these files compile.

### User visual checklist

### Phase 4 — actor/component checkpoint

- Kept the fork's actor factory, extended markers, collection lights, light-property editing, commit/clean hooks and BioStage skeletal/animation/morph support.
- Mesh components now use `LEVertex` and connect static lightmaps, dynamic skeletal lighting, light-environment membership and disposal, shadow visibility callbacks and level-geometry invalidation after movement. Model previews own/dispose their static-lighting resources.
- Added read-only actor/component game-hidden flags; no game properties are changed. Unified editor visibility remains phase 6.
- Forward-ported the animation-parent transform fix: hair retains its own offset relative to its body when actors move. Added a real `LEVertex` wireframe draw path for BioStages, with the correct shader/input layout and state restoration.
- Verified the upstream collection-rebuild fix is already present: updates rebuild only the owning collection and retain visible keys. Pinned renderer source already includes LMT_1D swizzling.
- Added four game/GPU-independent tests using real in-memory packages: actor hidden flag, separate component hidden flag, hair offsets after movement, and disabled animation-parent behavior.
- Full solution build and explicit test-project build: **passed**. Full post-renderer/component suite: **90 tests; 87 passed, 0 failed, 3 existing Castle-proxy skips**. Initial failures in new tests were corrected fixture headers/edit authorization, not suppressed production checks.
- Manual testing is now useful for mesh-format/transform regressions: open a familiar level, check solid props/visible sets and Ctrl-selection; check BioStage wireframes/nodes/cameras; move a stunt actor and check hair follows without offset jumps, then undo. Use a disposable copy and do not save changes for this smoke check. If available, also open a dialogue preview with Level Editor open.
- Do **not** assess final lighting, translucency or the new layout yet: per-level shader preparation and complete render-pass wiring are still phase 5. Manual results remain pending until the user reports them.
- No model switch is needed for this checkpoint. A separate review before final combined delivery may be useful; do not assume model superiority.

### Phase 4 — user smoke-check report (LE2)

- User reports the editor is largely working. This is a limited smoke-check report, not acceptance of every checklist item.
- The view looks unlit compared with the earlier partial merge. Game shaders/level lighting and complete passes are not connected yet; reassess after phase 5 rather than treating current appearance as final.
- Volumetric flashlight cones and large light-volume meshes are missing. Code confirms the new material path assigns translucent materials to `RenderPass.Translucent`, while the editor still executes only Base/Hair/Collision. This is a concrete missing integration path to address in phase 5; affected assets still need visual confirmation afterward.
- Floor tiles in one level have an unexpected color. Cause is unconfirmed; do not assume lighting explains it. User supplied comparison screenshots and exact references below. The new LEVertex path currently falls back to a guessed diffuse texture until game shaders are connected; texture selection/material evaluation is a candidate to check, not a confirmed diagnosis.
- Exact LE2 repeat-test targets (read-only game assets; never edit these to repair editor rendering):
  - Missing piano light cone: `D:\Origin Games\Mass Effect Legendary Edition\Game\ME2\BioGame\DLC\DLC_MOD_CHANCE\CookedPCConsole\Freighter\BioD_Freighter_CargoBay.pcc`, export **1920**.
  - Floor tiles: `D:\Origin Games\Mass Effect Legendary Edition\Game\ME2\BioGame\DLC\DLC_MOD_CHANCE\CookedPCConsole\Freighter\BioA_Freighter.pcc`, exports **6308** and **6696**.
  - User describes 6308 as the right-most tile with a solid brown center; 6696 as the left-most tile with a smaller brown section. Preserve export identifiers as the authoritative targets rather than inferring attachment order from screenshots.
- After phase 5, repeat these LE2 checks before claiming lighting/translucency acceptance. No model handoff needed at this point.

### Combined acceptance checklist

### Phase 5 — level lighting/render-pass checkpoint

- Each `OpenLevelFile` now owns its `SceneLight` list. Loading replaces/registers that file's lights and registers its BSP geometry; closing/reloading removes its lights and forgets level-owned geometry/settings before releasing the package. Whole-scene unload clears all light registrations.
- Enabled game shaders for Level Editor, with background shader/light preparation during initial file loading. Reloads and package update notifications refresh lights and prepare newly created material/light shaders; supported game materials use their actual shader path rather than the guessed diffuse fallback.
- Rendering now executes Base, Hair, Lighting, `EndLightingPass`, Translucent, then optional Collision. Existing visible-set/hidden/wireframe filters apply to the new mesh passes, avoiding newly visible hidden translucent props.
- Kept BioStage forced-wireframe geometry and brush overlays out of lighting/translucent passes. No UI/layout was replaced and no game asset was edited.
- Added two real in-memory LE2 lighting tests: removing one file's lights retains another file's lights and increments the cache version; whole-level unload clears light references/version. Full solution and explicit test-project builds passed. Full suite: **92 tests; 89 passed, 0 failed, 3 existing Castle-proxy skips**.
- **User testing requested now:** load the Freighter files together with the same filters as before; verify CargoBay export 1920's cone and BioA exports 6308/6696's floor materials; compare overall lighting. Report load failures, persistent material changes or missing cones with a screenshot. Adding/closing/reopening a file should not remove other files' actors/lights.
- Appearance is **not visually validated**. Missing/unusable game shaders still use the upstream fallback; no claim that the floor-color issue is resolved without the user's report.
- Phase 6 must finish unified shadow/picking/editor visibility, the approved global-marker/per-light policy, and live uncommitted light edits. Until then, do not use marker hiding as a lighting-policy acceptance test. Lighting mode/shader switches await the redesigned UI; shared preview consumers await phase 10.
- No model switch is needed at this checkpoint; a fresh independent review remains useful before combined delivery.

### Remaining combined acceptance

### Sleep recovery — volumetric checkpoint within phase 6

- Recovered actual source on `becca-LEX`: phases 1–5 remain present. Additional work survived that was not recorded here: `VolumetricMeshClassifier`, classification for static and skeletal components using mesh/material paths, computed actor volumetric status, category toggle synchronization, persisted selected-outline option, and five classifier/settings tests. No existing work or instructions were replaced.
- User confirms floor material issue is fixed; remaining floor variation is baked-lightmap related and is not an active regression. User confirms volumetric geometry is selectable/highlighted but invisible otherwise; rendering acceptance remains open.
- Visible Sets Manager now lists **Volumetric meshes** separately from ordinary static/skeletal actor classes. Existing Show Volumetrics synchronization also controls these meshes in visible-set mode. Classification recognizes `Volumetric`/`VolumeLight` in mesh or material paths; it does not treat every translucent mesh as an effect. Other names need asset evidence before expanding classification.
- Selected volumetrics retain cyan geometry edges and now have thick opaque yellow bounds beams sized with camera distance/orthographic width, drawn by the existing editor overlay. The persisted selected-outline toggle remains intact.
- Identified a concrete missing shader input: material scene depth was always bound to null. Added a translucent-pass depth snapshot using the existing single-sample depth-copy renderer, with its point-clamp sampler and render-state restoration. It is refreshed per frame and cleared before opaque rendering. Scene-color-dependent materials are still unsupported; do not claim all effect materials fixed.
- Solution and explicit test-project builds passed. Full suite: **97 tests; 94 passed, 0 failed, 3 existing Castle-proxy skips**. These are automated checks, not GPU/material/outline visual acceptance.
- **Manual check requested:** Freighter CargoBay export 1920 should be checked unselected, then selected for strong bounds. In Visible Sets Manager, hide **Volumetric meshes** and confirm regular skeletal props remain visible and the hidden volumes no longer intercept viewport clicks. Re-enable the group and compare. Use Show Volumetrics as another category-level check. Report any mesh not classified or any still-invisible effect by export number.
- Phase 6 remains open: unified shadow/picking visibility, approved global-marker/per-light illumination and live uncommitted light preview are not complete. No model switch is needed for this checkpoint.

### Volumetric preview style — independent menu toggle

- User confirms static/skeletal volumetric recognition and selected outlines work well. Original volumetric materials remain invisible in the reported scene; the depth input did not establish a complete material fix. Keep this rendering limitation open.
- Per the user's chosen UI, the visibility dropdown and its binary Show Volumetric Meshes checkbox are unchanged. Added **Visible Sets > Tinted volumetric preview** beside Edit Visible Sets. Unchecked (default) leaves the original material rendering path; checked adds a faint cyan, depth-tested, non-depth-writing fill to classified volumetric meshes. Both retain selected outlines and existing visibility/picking behavior. No package materials/properties are edited.
- Preview style is saved independently from visibility in recent view settings; old settings default to Normal. Explicit test-project build passed; all six volumetric classifier/settings tests passed, including the new independent tint round-trip/default test. GPU fill appearance requires manual validation.
- Manual check: enable Show Volumetric Meshes, switch the new menu toggle on/off, verify the faint fill and selected outlines, then hide volumetrics and confirm no fill remains. No model handoff needed.

- Same problematic level: all-enabled visible set renders solid props; unwanted volumes hide correctly.
- Individual/class/category visibility, clear/show-all/isolate/nearby and collision overlays.
- All Ctrl-selected objects stay blue; normal click/deselect and gizmo lead behave correctly.
- Groups translate/rotate/scale and undo/redo/save/reload without dropped collection meshes or incorrect hair offsets.
- Extended markers and BioStage nodes/cameras remain displayed, selectable and editable.
- Game/fallback materials, translucency, lightmaps, dynamic lighting/shadows and each lighting mode.
- Approved global marker/per-light illumination behavior, including re-enabling markers.
- Turbo/slow/fast movement and coordinate copying/pasting.
- Multiple files keep order/collapse state and saved recent-set/camera/filter settings.
- Package/Dialogue Editor and Interp Preview continue loading/rendering with Level Editor open; closed files release resources/locks.

### Resumed integration — shared visibility and light policy checkpoint

- Recovered progress from this master document, not the transient chat progress bar. Phases 1–5 remain technically complete; phase 6 is still in progress. The new execution checklist covers this checkpoint only, not a restart of all twelve phases.
- `LevelEditorRenderContext.IsActorVisible` now owns actor category, Visible Sets membership/distance and Hidden object-mode eligibility. All main editor render passes, including collision, use it. Existing component `IsShown` shadow callbacks now consult that same policy instead of the previous always-true base implementation. Hidden actors' transform widgets and stale hit-buffer actor/marker/axis results are suppressed without clearing selection or changing game properties.
- Light ownership is separate from contribution. The renderer retains all loaded lights for removal/unload, but exposes only contributing lights to lighting/light-environment caches. Effective-list changes increment the light version; unchanged frames do not allocate a new list or invalidate those caches.
- With global light markers enabled and Visible Sets filtering active, excluded lights no longer contribute real-time illumination. With markers disabled, all loaded lights contribute; individual membership is retained for re-enabling markers. Global marker toggles no longer rewrite light membership, and Visible Sets edits no longer infer the global marker switch from membership. Outside Visible Sets filtering, loaded lights contribute as before. Marker distance and camera position do not enter the contribution predicate. Dynamic Lights and baked lightmaps remain separate controls.
- Standalone light identity uses the actor export; collection light identity uses the individually editable component export, matching existing fork proxies.
- Added six game-independent regressions for marker on/off restoration, retained membership, distance independence, stable/change-based cache versions, hidden-light removal, standalone/collection identities and actor display eligibility.
- Full solution and explicit test-project builds: **passed**. Full `LegendaryExplorer.Tests` run: **104 tests; 101 passed, 0 failed, 3 existing Castle-proxy skips**. The file-only WPF generated-control diagnostics did not reproduce in the full build. No unrelated source fixes or new test skips were added.
- **Manual validation remains pending.** Enable light markers, hide one light through Visible Sets and check its real-time effect; disable all light markers and check that contribution returns; re-enable markers and check the individual hide is retained. Moving beyond marker distance must not change illumination. Hide a mesh and check that its collision overlay, picking, gizmo and dynamic shadow disappear together. Baked lighting may remain. Also complete the earlier tinted-volumetric check.
- Remaining phase 6 work: preview uncommitted light property/transform edits, audit nested/prefab and component visibility boundaries, and resolve any user-reported GPU regressions. Later phases 7–12 remain pending. No model handoff is needed now. No commit or push was performed.

### User acceptance and phase-7 start

- User explicitly clarified that the earlier “Perfect!” confirmed successful volumetric testing, and now reports the lights work perfectly as envisaged. Record volumetric preview and the approved global-marker/per-light illumination behavior as **manually accepted**, not pending.
- Proceeding to phase 7 at the user's request. Phase 6 is not marked complete: live uncommitted light editing and remaining nested/component visibility audits are carried forward explicitly. The user report does not establish acceptance of unimplemented features or all shadow/collision cases.
- Phase 7 preserves the existing multi-selection/group workflow and scale semantics, connects testable grouped transform operations and adds batch undo/editability coverage. Group GPU/manual workflow validation remains pending.

### Phase 7 — group operation and selection checkpoint

- Preserved existing group semantics while extracting `GroupTransformEdit.Apply` into the shared undo/editing layer: members translate with the lead, rotate around the lead with matching orientation delta, and retain proportional per-member scaling with additive fallback for zero lead axes. Scaling does not introduce new pivot-based member movement.
- The main editor's property edits and completed widget drags use that shared operation under the existing group recursion guard, creating one `TransformBatchAction`. Read-only and unavailable members are excluded, and duplicate members are applied once.
- Group creation explicitly synchronizes shared viewport selection after selecting the lead; group reselection explicitly refreshes selection count, selected keys and primary actor after restoring the group list. Existing Ctrl multi-selection, group commands and customized UI are preserved.
- Added four in-memory regression tests: translation/duplicate handling and whole-group undo/redo, combined rotation/translation, proportional/zero-axis scaling without movement, and read-only/unavailable exclusions. Existing selection-state tests also pass.
- Full solution and explicit test-project builds: **passed**. Full `LegendaryExplorer.Tests`: **108 tests; 105 passed, 0 failed, 3 existing Castle-proxy skips**. An extraction-time missing extension import was fixed; accompanying generated-WPF diagnostics disappeared on the successful build.
- **Manual check requested:** Ctrl-select two or three editable actors and verify each is highlighted; create a group, move/rotate/scale it using the gizmo and transform fields, then undo/redo. Select elsewhere and use Reselect Group; check all members and the lead gizmo return. Ungroup and confirm edits affect only the selected actor. Include a collection mesh if convenient; ensure its geometry follows the edit and survives commit/reload on a disposable test copy. Read-only actors should not join/edit with the group.
- Phase 7 remains open for collection/proxy workflow checks and the user's manual result; this checkpoint does not establish save/reload or GPU drag acceptance. Phase 8 redesigned layout has not started. No agent switch, commit or push was needed.

### Phase 7 follow-up — material-independent selection highlighting

- User confirms group selection and transformation work well, but selected floor meshes turn blue while some crates/tables do not. Group workflow acceptance is recorded; inconsistent highlighting remains a separate reported defect.
- Found material-dependent tint in both renderer paths: fallback doubled existing blue, while game-shader hit proxies multiplied existing RGB before additive lighting. Dark/low-blue materials could remain visually unchanged, and later lighting could dilute the tint.
- Replaced those multiplicative tints with a shared per-sample blue blend in the final resolve after lighting and game tonemapping. Both fallback and game-shader hit outputs carry selection in their existing UNorm alpha channel (selected zero, unselected/background one); RGB hit IDs are unchanged. Game scene alpha still marks tonemapped pixels. Editor primitives retain their own colors; package materials are not changed.
- Added six actual HLSL compilation cases: fallback pixel shader, game hit-proxy shader, and final resolve at 1/2/4/8 samples. Solution and explicit test-project builds **passed**. Full suite: **114 tests; 111 passed, 0 failed, 3 existing Castle-proxy skips**.
- **Manual check pending:** select the previously unhighlighted crate/table alongside the floor; each should have a visible blue tint. Deselect to confirm original colors return, and verify Ctrl-selection and picking still work. Report any remaining miss by package/export number; shader compilation does not establish GPU appearance acceptance.
- Phase 7 remains open until the reported highlight defect is visually resolved; no layout changes, commit or push were made.

### Phases 6–7 reconciliation — no silent carry-forward

- User confirms the revised blue-highlight visual check passes. Volumetrics, approved lighting behavior, group selection/transformation and revised selection tint are manually accepted. Earlier pending statements in historical checkpoint sections are superseded by this checkpoint and the current table.
- The user requests finishing outstanding phase-6/7 work before advancing. Audited the source rather than marking missing work complete. Real gaps: unsaved light edits did not update SceneLight; prefab shadow membership used archetype identities; group and undo references could point to disposed proxies after package updates/reload.
- Implemented cached, non-writing light snapshots from cloned condensed properties plus current brightness/color/radius/source radius/channels and component transform. Removed cached parent transform from preview input and zeroed translation because the current component transform already includes it. Changed snapshots replace both renderer and per-file ownership; unchanged snapshots reuse their identity. Engine-default Static/Dynamic/CompositeDynamic channels are preserved for missing fields. No preview writes export properties.
- Prefab child visibility now binds to its owning instance for Visible Sets membership, with category/distance checks retained. Nested drawing and component shadow eligibility consult the same policy.
- Groups rebind members/leads by stable package/export key after replacement. Transform undo/redo entries rebind too, pruning truly unloaded members while retaining other loaded actors and stack order. Reload reconciliation occurs after replacements load, not during the temporary empty state. Targeted package updates also reconcile after rebuilding proxies.
- Added in-memory tests for non-writing live edits, unchanged snapshot reuse, transform/color/radius inputs, preserved default channels, renderer replacement/removal/version, and undo replacement/unload pruning.
- Solution and explicit test-project builds **passed**. Full run before the final default-channel correction: **116 tests; 113 passed, 0 failed, 3 existing Castle-proxy skips**. After that correction, both builds passed again and all **7 component tests passed**, including the extended live-light/default-channel regression.
- **Remaining acceptance checks, not vague deferred implementation:** change a light's brightness/color/radius and position before commit and verify real-time appearance follows; reset/reload and verify it returns. Hide a prefab/mesh and check its dynamic shadow disappears. On a disposable package copy, edit a group containing a collection mesh, commit/reload, reselect the group and check undo/redo acts on current geometry; closing one file must not leave undo targeting its disposed actors. These runtime/package-specific paths are not established by the in-memory tests.
- Phases 6–7 remain open only as shown in the current table; do not start phase 8 or claim collection/save/reload acceptance without the targeted result. No model switch, commit or push was performed.

### Blocking regression — black props after live-light snapshot integration

- User reports many props became fully black after the reconciliation changes, while baked surfaces remain lit. Prior lighting acceptance applies to the earlier checkpoint, not this regressed build.
- Identified that the first live-preview implementation reconstructed every light even when untouched, overwriting renderer inputs with editor-marker color/channel defaults and replacing cached parent transforms. This could alter dynamic/light-environment illumination without an edit; the initial tests failed to assert baseline identity/equivalence.
- Fixed preview ownership to retain each original loaded SceneLight unchanged until a real proxy edit. Preview snapshots now override only changed fields, preserving original color/channels and cached transform for property-only edits, and restoring the original snapshot if changes are reversed. Collection lights retain their collection owner class instead of using the component class for lighting policy.
- Added a baseline regression proving untouched identity, brightness-only color scaling with unchanged channel/position/direction, stable edited snapshots and exact baseline restoration without export writes. Existing combined live property/transform test remains passing.
- Both builds **passed**. Full suite: **117 tests; 114 passed, 0 failed, 3 existing Castle-proxy skips**. These establish code/fixture behavior, not GPU recovery.
- **Blocking manual check:** restart the updated editor and reload the same scene without editing lights; the formerly black props must regain their previous appearance. Only after that succeeds should live brightness/color/radius/position edits be checked. Do not mark phase 6 complete or advance to phase 8 until recovery is confirmed. Selection/group fixes are retained; no game/package properties were written by preview.

### Lighting recovery acceptance

- User reports the scene seems fine after the baseline-preserving lighting fix and authorizes continuing. Baseline lighting recovery is **visually accepted**; the blocking black-prop report is no longer active.
- This report does not independently establish live light edit/revert, nested shadow or collection commit/reload acceptance. Complete targeted technical verification next; preserve the accepted renderer baseline and do not silently carry missing code forward.

### Post-recovery targeted verification

- Preserved the accepted lighting baseline; this checkpoint changes tests and tracking only, not renderer/material behavior.
- Added a real `StaticMeshComponentActorProxy` group test with an in-memory `StaticMeshCollectionActor`: grouped translation, component transform synchronization, binary commit/readback, reconstructed proxy identity and undo/redo rebound to that proxy all pass. This verifies collection serialization and replacement, not a GPU mesh draw or disk save/reopen.
- Added live color/channel edit-and-revert coverage: edited red color and disabled Dynamic channel affect the snapshot, Static remains enabled, position stays unchanged, export properties remain untouched and reverting restores the exact original light.
- The initial collection test fixture omitted its binary Export owner and failed during serialization; corrected the fixture to match loaded production binaries. No production workaround was needed.
- Solution and explicit test-project builds **passed**. Full suite: **119 tests; 116 passed, 0 failed, 3 existing Castle-proxy skips**.
- Confirmed manual results remain: volumetrics, approved light visibility policy, group selection/transforms, blue highlight and baseline lighting recovery. The remaining phase-6/7 acceptance list is limited to live light edits/reset in the viewport, nested visibility/shadows, and group/collection commit/reload through the actual editor UI. These are unreported manual cases, not claims of unfinished snapshot or collection serialization implementation.
- Next manual check: change one light's brightness/color/radius and position without committing, then reset/reload; on a disposable file copy, commit/reload a group containing a collection mesh and try Reselect Group and undo/redo. Hiding a prefab/mesh should also hide its dynamic shadow. Record results before closing phases 6–7 and advancing to phase 8. No agent switch, commit or push was performed.

### Acceptance clarification — phases 6–7 closed for milestone

- User explicitly says the previously requested checks were already performed and lighting behavior was already tested. Accept that correction; repeated manual checklists in prior historical entries are superseded and must not remain a gate.
- User reports mesh/prefab dynamic shadows do not clearly toggle, but the scene may not have usable visible dynamic shadows and baked lightmaps remain. Do not claim this proves shadow correctness or that baked lightmaps override dynamic shadows in every case. Record the result as inconclusive and non-blocking per the user's stated priority.
- Phases 6–7 are technically complete for the practical milestone with their implemented/tested contracts and accepted prior checks. Preserve the shadow appearance limitation for final coverage reporting; do not broaden it into another mandatory investigation now. Phase 8 is next; no phase-8 layout implementation is claimed yet.

### Phase 8 — automated layout validation

- User closed LegendaryExplorer; solution and explicit test-project builds now **pass** without the earlier executable lock.
- Corrected the window icon URI to name its owning LegendaryExplorer assembly. The icon exists; the prior failure was test-host resource resolution, not a missing asset. Removed the test's invalid reassignment of WPF `Application.ResourceAssembly` and retained the production `AppResources.xaml` dictionary.
- Runtime STA construction test **passes**, verifying preserved named controls, original lighting defaults and category-group toggling without removing file grouping. Full suite: **120 tests; 117 passed, 0 failed, 3 existing Castle-proxy skips**.
- These checks establish construction and tested contracts, not visual usability, populated group templates or GPU rendering. Phase 8 remains in progress; do not move to phase 9 until the user checks the redesigned layout.
- Next manual check: open a familiar level and confirm the outliner, viewport and scrollable Details panel are readable and usable. Toggle category grouping and confirm file headers/actions remain available; check access to Visible Sets, transform/group tools and light/BioStage details where present. No repeat of accepted phase-6/7 rendering checks is requested.
- No renderer behavior changes, commit or push at this validation checkpoint.

### Phase 8 — viewport group drag correction

- User confirms group editing through Details works, but viewport translate/rotate/scale gizmos affect only one actor. User also reports contrast/theme issues and explicitly defers that appearance pass until reference photographs are available; no theme changes are made here.
- Traced the live drag gap: Widget changes only its attached actor while property-based group propagation is suppressed during dragging. Previous group propagation ran only at completion and required the original group lead.
- Added drag-start/update/completion wiring and a group drag snapshot owner. Each live update derives all editable member transforms from their original snapshots, supporting any group member as pivot, avoiding compounded deltas and retaining one batch undo/redo action. Returning to the starting transform restores members without an undo entry. Existing Details semantics and read-only exclusions remain intact.
- Solution and explicit test-project builds **pass**. Added repeated translation/non-lead pivot/undo, rotation/scale/reset and completion lifecycle regressions. Full suite: **123 tests; 120 passed, 0 failed, 3 existing Castle-proxy skips**.
- Manual confirmation remains pending: restart the updated application, drag a group's translate/rotate/scale gizmos and verify all editable members respond during the drag; undo/redo should restore/reapply the entire drag. Phase 8 is still open, with the appearance pass deferred by user request. No commit or push.

### Phase 8 — dark-theme readability pass

- User reports **groups now work correctly**; viewport group-gizmo correction is manually accepted. Do not repeat that check as a gate.
- User supplied current/upstream screenshots showing black labels on dark backgrounds and bright default widgets. Preserve collapsible level headers/category grouping and transform radio buttons; no package shortcut strip is added.
- Completed editor-local styles for checkbox/radio labels and indicators, buttons/toggles, dropdowns/popups, menus/status bar, numeric inputs and spinner buttons. Replaced bright GroupBox outlines with dark section headers/panels. Retained numeric parsing, keyboard editing and visible spinner arrows; corrected commit-button style inheritance and undo/redo icon foregrounds. No application-wide theme or renderer changes.
- Pinned upstream's package strip is a set of OpenFiles buttons, not independent scene tabs. FocusFileCommand selects/reveals the package's first actor; tooltips show paths and dirty dots show pending edits. All loaded files remain in one scene. Explained this without changing the fork's file workflow.
- Runtime test checks light foregrounds/custom templates, dark numeric backgrounds, inherited numeric text colors and the retained decrease spinner arrow. Solution and explicit test-project builds **pass**. Full suite: **123 tests; 120 passed, 0 failed, 3 existing Castle-proxy skips**.
- Appearance acceptance remains pending: restart and inspect top controls, viewport switches, Snap/numeric inputs and dropdown/menu states. Confirm the preserved file groups remain usable. Phase 8 stays open until the user reports the result; no commit or push.

### Phase 8 — welcome, scrollbars and toolbar follow-up

- User reports improved appearance but supplies remaining light welcome-screen and scrollbar examples. User selected both smaller/tighter toolbar controls and moving Top Down to the viewport row.
- Replaced the welcome screen's hard-coded light background with the local dark panel palette; centered/bounded the welcome content, improved link contrast and recent-set spacing/truncation, retaining recent-set commands and tooltips. Added a scrollable welcome area for smaller windows.
- Main toolbar now uses 11-point text with tighter button padding. Top Down moved beside viewport lighting options with its existing binding/Numpad 5 shortcut preserved. Controls still wrap when necessary at narrower widths rather than being hidden.
- Added local dark scrollbar tracks/thumbs/arrows with vertical/horizontal line/page commands and two-way thumb value updates. Clarified Local Coords: movement/rotation follow actor axes when enabled, fixed level axes when disabled; scale always uses actor axes. No transform behavior changed.
- Separate Release test build **passed**. Extended runtime layout checks cover welcome background, toolbar font, Top Down placement, both scrollbar orientations/commands and track value propagation. Full suite: **123 tests; 120 passed, 0 failed, 3 existing Castle-proxy skips**.
- Main solution build is blocked at executable copy by running LegendaryExplorer PID 5704 (`MSB3027`/`MSB3021`), not a remaining compile error; the application was not terminated. Close it and rebuild before visual review. Appearance/scroll interaction confirmation remains pending; phase 8 remains open. No commit or push.

### Phase 8 accepted; phase 9 navigation/settings checkpoint

- User reports layout/theme work all done and tests successful, authorizing the next phase. Phase 8 is accepted; prior historical requests for its visual checks are superseded.
- Audited movement, focus/orthographic toggles, typing guards, camera/actor clipboard operations and recent-set capture/restore. Existing focus, clipboard formats and typing guards were retained without redesign.
- Corrected orthographic immediate key taps to apply the same Shift/Control/Turbo multiplier as held movement. Shift remains 0.25 and takes priority over Control (4); Turbo applies afterward with default 10. Invalid Turbo factors fall back to 10.
- Added camera snapshots preserving perspective position/yaw/pitch/focus depth when a top-down recent view is reopened. Old JSON without a perspective snapshot falls back to its recorded camera rather than unrelated startup position; missing/invalid orthographic width falls back to 5000.
- Recent view state now captures/restores Game Shaders, Lighting Mode, Dynamic Lights, Lightmaps and Local Coords with original defaults for old files. Null path/visibility lists are handled; package/read-only path matching is case-insensitive. No settings files were reset or written during automated testing.
- Solution and explicit test-project builds **pass**. Four new regressions cover modifier combinations, legacy defaults, top-down/perspective JSON restoration and camera focus. Full suite: **127 tests; 124 passed, 0 failed, 3 existing Castle-proxy skips**. Tests establish calculations/state round trips, not physical key input or clipboard/UI execution.
- Targeted manual confirmation pending: check brief/held movement in perspective and Top Down with Shift/Control/Turbo; close/reopen a recent set saved in Top Down, verify lighting/local-axis choices, then turn Top Down off and confirm the previous perspective view returns. Do not repeat phase-8 layout or accepted group/lighting checks. Phase 10 has not started; no commit or push.

### Right-click menu and Package Editor handoff correction

- User confirms phase 9 works; navigation/settings manual acceptance is recorded. Reports malformed right-click menu chrome and Package Editor staying busy/unresponsive until Level Editor closes.
- Handoff already uses nonmodal Show. Traced Package Editor tree completion to synchronous Dispatcher.Invoke(ApplicationIdle) immediately before queued export navigation/busy cleanup. Continuous viewport rendering can starve that wait; GoToNumber selects completed tree models and does not require that idle barrier. Removed the barrier without pausing Level Editor, changing shared package ownership or moving UI work to a background thread.
- Added editor-local dark context-menu surface, leaf-item and separator templates to remove default pale gutters/white separator blocks. Retained checkmarks, command bindings, gesture labels, disabled states and native submenu-header behavior.
- Solution and explicit test-project builds **pass**; runtime menu-template checks **pass**. Full suite: **127 tests; 124 passed, 0 failed, 3 existing Castle-proxy skips**. Automated tests do not reproduce the concurrent GPU/window busy-state symptom.
- Manual confirmation pending: restart, right-click an actor and inspect menu rows/separators; use Open in Package Editor while keeping Level Editor open. Package Editor should select the actor export and remain interactive. Do not repeat accepted navigation/layout/group checks. Phase 10's broader shared-consumer audit remains ahead; no commit or push.

### Phase 10 — shared preview technical checkpoint

- User confirms right-click menu/Package Editor handoff works and authorizes phase 10. Prior handoff manual gate is accepted; do not repeat it.
- Audited Actor Preview, Animation Preview, Interp rendering/loading, shader inspection and Dialogue launcher paths. Actor/Interp loops omitted Lighting/Translucent; Animation iterated all enum values including ANY without pass boundaries. Added a shared explicit Base/Hair/Lighting/Translucent pipeline (optional Collision), with lighting completion and translucency setup and wireframe restoration.
- Actor Preview now uses read-only preview lighting and retains category/volumetric marker visibility when using shared visibility checks. Existing shader activation defaults remain unchanged; this checkpoint does not claim game-shader preview is enabled by default.
- Interp Preview synchronizes parsed level lights by committed actor package before disposing retired resources, and detaches lights before closing levels. Registry does not own/dispose packages; session/dispatcher boundaries and player/conversation binding remain unchanged. Volumetric visibility retained.
- Shader inspection tools read core shader caches and do not depend on renderer enum/API changes. The separate LegacyScene3D mesh/texture viewer is intentionally retained, not upgraded or claimed to have all new renderer features. A full viewer port is deferred and must remain explicit in final coverage; phase 10 is not a blanket all-consumer completion claim.
- Added pass order/boundary and package-light dedup/removal/reload regressions. Focused tests: **9 passed**. Final solution and explicit test-project builds **pass**; full suite: **130 tests; 127 passed, 0 failed, 3 existing Castle-proxy skips**.
- Manual check pending: with Level Editor still open, inspect an actor in Package Editor's Actor Preview; launch Interp Preview from Dialogue Editor and confirm visual loading/interaction. Replace/close its level set and reopen to check stale-light/resource behavior. Animation Preview can be checked where a suitable asset exists. Automated tests establish pass contracts/light membership, not GPU visuals. No commit or push; phases 11–12 not started.

### Phase 11 — integrated regression coverage checkpoint

- Audited existing visibility/selection, live-light/component, volumetric, group/collection/undo, navigation/settings, runtime layout/menu, preview pass/light and Interp ownership tests against the accepted milestone. Existing selection/proxy identity, non-writing snapshots, collection reconstruction, XAML construction and pass sequencing coverage is retained; no duplicate visual acceptance gates were added.
- Added four lighting regressions: replacement preserves loaded order and hidden membership across marker toggles/removal; same/unregistered replacements leave the version unchanged; marker distance and owner visibility remain separate from contribution policy; preview registry cleanup preserves unrelated lights, is repeatable and allows the same package to be loaded again.
- Added two undo regressions: pruning a fully unloaded middle action preserves both undo and redo order, retained actions target replacement proxies, and old/unloaded proxies remain untouched.
- Added two saved-view regressions: missing legacy visibility fields retain original defaults; explicitly saved false/zero visibility and volumetric/stage preferences survive serialization.
- Changes are tests and this tracker only. The first test-project build caught an inaccessible internal owner setter in the fixture; corrected the fixture without widening production access. No renderer/UI/lifecycle behavior changes were needed.
- Solution and explicit test-project builds **passed**. Focused lighting/group/navigation suite: **28 passed, 0 failed**. Full suite: **138 tests; 135 passed, 0 failed, 3 existing Castle-proxy skips** (`set_IsModified` DynamicProxy generation remains inconclusive).
- Phase 11 is **technically complete**. Automated results do not establish GPU appearance, concurrent-window behavior or final combined user acceptance. Phase 12 remains pending; do not repeat already accepted manual checks or reopen deferred Interp shaders/new lighting. No commit or push was performed.

### Phase 12 — final technical delivery

- Rechecked actual XAML labels, command behavior and scope boundaries. Replaced obsolete upstream "pending" rows and planned tool destinations with the delivered coverage table and guide. Existing accepted lighting/group/layout/navigation/handoff results remain accepted, not reopened.
- Fresh solution and explicit test-project builds **passed**. Fresh full suite: **138 tests; 135 passed, 0 failed, 3 existing skips**. The skipped `InterpPreviewOwnershipTests` are `OwnedResource_DisposesActorsBeforePackage`, `LoadedLevel_ToOwnedResource_TransfersOwnershipOnlyOnce_AndDisposalTransfersToOwnedResource`, and `LoadedLevel_Dispose_IsExactOnce_ForAbandonedCandidate`; Castle DynamicProxy `set_IsModified` generation prevents these fixtures from running.
- Branch confirmed as `becca-LEX`. No production changes, commit, push, settings reset or process termination in phase 12. `git diff --check` reports an existing trailing-space comment at `Scene3D/MaterialRenderProxy.cs:136`; it is not a compile/runtime blocker and unrelated renderer source was left unchanged.
- Technical delivery is **complete for the practical milestone**. Final combined manual acceptance is **not complete** until the user reports it. This does not enable deferred Interp shaders/new lighting, port the legacy viewer or claim exhaustive asset/GPU validation. No agent switch is required.

### Final user acceptance

- User reports being happy that everything is working. Practical milestone delivery is accepted; no further manual checklist is required as a gate.
- User clarifies that Interp Preview must be launched separately. The earlier combined-preview wording must not be interpreted as a required Level Editor-to-Interp launch workflow.
- This general acceptance does not establish individual results for every proposed ownership test or every game asset, and does not expand the deferred Interp scope. Historical pending-acceptance statements above are superseded by this checkpoint.

### Historical combined acceptance suggestions — no longer a gate

Do not repeat already accepted lighting, volumetric, group/highlight, layout/theme, navigation/settings or Package Editor handoff checks. Only the following combined ownership/workflow results remain unreported; if already performed, reporting that result is sufficient.

1. **Concurrent previews:** keep Level Editor open and check Package Editor Actor Preview plus the existing Dialogue-launched Interp preview still load and remain interactive. Close/reopen or replace the preview level set and confirm no stale scene/light or loading failure. Judge Interp against its prior partial functionality, not deferred game shaders/new lighting. Animation Preview is optional where a suitable asset exists.
2. **Multi-file persistence/ownership:** on disposable copies, keep more than one level open, commit/save a small edit, close one file and reopen the saved set. Confirm the retained file stays usable, saved changes return and there is no stale selection/undo reference or unexpected file-lock failure. Do not redo accepted group-transform or lighting visual checks.

These were proposed checks, not independently reported test results. The user's subsequent practical acceptance closes the delivery gate; do not request them again unless investigating a reported regression.

## Delivered tool-location guide

| Task | Actual location / shortcut | Notes |
|---|---|---|
| Open or add levels | **File → Open** (`Ctrl+O`), **Add File...** (`Ctrl+Shift+O`), **Load Related Levels**, **Recent** | Open replaces the scene; Add retains loaded files. Recent sets retain saved view preferences |
| Commit, save, reset | Top toolbar **Commit Changes** or **Edit → Commit Changes**; **File → Save All** (`Ctrl+S`), **Save As...** (`Ctrl+Shift+S`); **Edit → Reset Uncommitted Changes...** | Commit updates package data but does **not** save to disk. Use disposable copies for validation |
| Per-file operations | Collapsible file header in **World Outliner**: **Commit**, **Save**, lock, **X** | Lock prevents actor editing; X closes that file. Category browsing does not create transform groups |
| Find and focus | Outliner search and **UIndex / Goto #**; viewport **Focus Selected**; actor right-click **Focus Camera** (`F`) | Single-key shortcuts are guarded while editing text |
| Game materials and lighting | Above viewport: **Game Shaders**, **Level lighting / Preview lighting / Unlit**, **Dynamic Lights**, **Lightmaps** | Independent of Objects mode. Baked contributions cannot be removed per light from existing lightmaps |
| Object display and categories | Top toolbar **Objects → Full / Wireframe / Hidden / Visible Set Only**; **Display Filters** | Filters expose lights, collision, volumes, volumetric meshes, emitters, location, sound, cinematic and decal/effect markers |
| Edit visible classes | **Visible Sets → Edit Visible Sets...** | Visible/Hidden Classes, Add All, Clear All and Clear Actor Level Settings; volumetrics use their own class grouping |
| Actor/class visibility | Actor right-click: show only, add/remove actor or class, **Clear All Sets**, **Show All**; `Ctrl+V` toggles the selected actor | **Visible Sets Dist** limits visible-set display; **Light Dist** limits markers, not illumination |
| Volumetric preview | **Display Filters → Show Volumetric Meshes / Outline Selected Volumetrics**; **Visible Sets → Tinted volumetric preview** | Tint and outline are editor-only; Normal means tint unchecked. Hiding volumetrics permits selecting objects behind them |
| Select and group | Ctrl multi-selection; **Groups → Group Selected Actors** (`Ctrl+G`), **Ungroup Actors** (`Ctrl+Shift+G`), **Reselect Group**; actor context menu also has group/ungroup | Details shows the Transform Group summary; blue highlight covers selected actors |
| Transform and undo | Top toolbar **Translate / Rotate / Scale / Uniform Scale / Local Coords**; right **Details** transform and Snap controls; **Edit → Undo / Redo** (`Ctrl+Z` / `Ctrl+Y`) | Local Coords uses actor axes for move/rotate; off uses world axes. Scale uses actor axes. Group edits use batch history; read-only members are excluded |
| Navigate | Top toolbar **Turbo Move / Turbo x**; above viewport **Top Down** (`Numpad 5`) | Shift slow is 0.25x, Ctrl fast is 4x; Shift wins if both held. Turbo multiplies either and defaults to 10x |
| Copy coordinates | Top toolbar **Show Camera Coords** arrow → **Copy Camera Coordinates / Paste to Selected Actor**; Details Location/Rotation copy/paste buttons | Camera-menu paste reads clipboard XYZ and moves the selected editable actor after confirmation; it does not move the camera |
| Edit lights | Select supported light → **Details → Light** | Brightness, radius, source radius, color and channels preview without writing exports until commit; undo/reset retains baseline light inputs |
| BioStage markers | Select BioStage → **Details → BioStage Overlay → Markers** | Node/camera display and selected-marker transforms. Marker edits affect the shared stage parent asset and therefore all instances |
| Other editors | Actor right-click **Open in Package Editor**; launch Interp Preview separately through its existing workflow | Package handoff accepted with Level Editor open. A File menu handoff exists in source, but is not the user's required Interp launch workflow. Interp retains prior partial functionality; this milestone does not complete the wider Interp project |

### Coverage boundaries

- Light marker visibility and illumination remain separate: with Show Lights enabled, individual hides disable real-time contribution; with Show Lights disabled, markers hide and all loaded lights may contribute. Dynamic Lights still controls dynamic illumination.
- Nearby visibility commands remain in code; they are not new menu entries in this layout. No dedicated engine-hidden, file-order/reload, MSAA or shadow toolbar control is advertised by this guide. Reopen files/recent sets through File rather than assuming a new Reload button exists.
- Extended fork actors, multi-file ownership, group/undo behavior, coordinate tools and saved-state compatibility are preserved; browsing categories and transform groups remain distinct.
- Interp shaders/new lighting, full Interp completion and the separate legacy mesh/texture viewer port are deferred. Shared pass/light contract tests do not prove GPU appearance or concurrent-window resource behavior.
- Known limitations: three Castle-proxy ownership tests remain skipped/inconclusive; dynamic-shadow appearance is non-blocking/inconclusive in the current scene; game-material/asset compatibility and imported performance features are not exhaustively validated.

## Risks and model handoffs

- Renderer, visibility/UI and practical shared-preview integration are delivered for this milestone; final combined user acceptance remains separate. Better-looking views alone are not evidence of exhaustive game-asset or deferred-preview coverage.
- The previous condensed-property filter opt-out was not a proven root cause: most callers still use `resolveImports: false`. Re-audit before retaining unsafe cross-package references.
- Baked lighting/precomputed shadows cannot be regenerated by visibility switches. Unsupported game shaders/materials must retain meaningful fallback rendering.
- UI and shared-preview regressions need runtime validation even after a successful build.
- Keep the current reasoning-capable agent for state/ownership and integration decisions. No model switch is required at this checkpoint. A separate implementation agent may use this document for mechanical ports once contracts are stable; a fresh reasoning review is useful before user delivery. Model names alone are not evidence of comparative capability.

## Change log

- Implementation start: confirmed clean `ae24c5335`, created recovery branch and saved settings copy, passed baseline build, and recorded 59/68 passing baseline tests with nine pre-existing failures.
- Phase 2 checkpoint: connected shared visibility/selection state without replacing the current interface or renderer; added and passed 15 tests. Explicit test-project rebuild refreshed outdated discovery; full suite is now 83 passed, 0 failed, 3 existing skips.
- Recovery after sleep: independently checked Git, helpers, render/context wiring and the master document. Rebuilt the solution successfully and reran all 86 tests: 83 passed, 0 failed, three existing Castle-proxy skips. Restored the active progress tracker at phase 3 and added a durable checklist here; local instruction edits were preserved.
- Phase 3: imported the coherent upstream renderer and lighting support bundle, forward-ported real navigation/overlay/double-click behavior, and passed the solution build. Actor/component adaptation is the next active phase; no visual acceptance or new layout completion has been claimed.
- Phase 4: adapted fork components to lighting/LEVertex contracts without replacing extended actors; fixed animation-parent offsets and retained BioStage wireframes. Builds passed and 90 tests ran (87 passed, 0 failed, 3 existing skips). Requested a limited visual regression smoke check; lighting/layout acceptance remains premature. Phase 5 is next.
- Resume at phase 3: integrate the pinned upstream rendering bundle together with actor/component and per-level ownership changes in phases 4–5. Keep coherent backend builds; do not transplant old files back to solve missing APIs. No model switch is required before this architectural integration. Working changes are not committed or pushed.
