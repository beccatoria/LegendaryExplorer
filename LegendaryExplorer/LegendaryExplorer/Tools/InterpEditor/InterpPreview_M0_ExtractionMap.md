# M0 — Branch Archaeology & Feasibility Baseline (Extraction Map)

> Deliverable for Milestone M0 of `InterpPreview_Plan.md`.
> Produced from `scott-LEX` (current) with cross-reference to `origin/curve-editor-3d-port`.

## 1. Branch Inventory

### Branches examined
| Branch | Role | Relevance |
|---|---|---|
| `scott-LEX` (current) | Active fork with matured dialogue-node preview | **Primary source of truth** — contains the most complete previewer |
| `origin/curve-editor-3d-port` | Clean port of the 3D curve editor + actor positioning research | Reference for lifecycle notes and the reusable animation/rendering stack |
| `remotes/scottina/Beta` | Upstream of scott's fork | Historical origin of viewers |
| `upstream/Beta`, `upstream/master` | ME3Tweaks mainline | Target integration baseline; does NOT contain previewer |

### Key commits (scott-LEX preview lineage)
- `90ead5ad6` More dialogue preview improvements
- `36ad5f50a` Added LE2 squadmates support for dialogue preview
- `59eab8dc1` More improvements to dialogue node preview
- `affa1841d` Can now edit stage cameras from dialogue node preview
- `9657d5b03` Added undo + Redo to dialogue preview
- `4978f3bf7` Massive performance optimizations to dialogue preview
- `f468362d0` Squadmate selection added to dialogue preview
- `8f5687e08` Integrated package editor with 3d curve editor node preview
- `687704178` Major refactor of dialogue node preview
- `4f84f91de` Significantly improved multi dialogue preview

### Key commits (curve-editor-3d-port lineage)
- `e8750831f` Port 3D Curve Editor from scottina fork and wire into Package Editor
- Reverts `d2a60c7f0`, `1f0b2dff4`, `df7c4df71` — earlier "interp editor visualisation" attempts were **reverted** (known-unstable milestones 1/2 + "crappy attempt at 3D interpeditor rendering"). These are the documented instability precedents.

## 2. Reusable Components (Extraction Targets)

### Tier A — Pure/near-pure logic (directly reusable, low risk)
| Component | Path | Notes |
|---|---|---|
| `CurveEditor3DModel` | `UserControls/ExportLoaderControls/CurveEditor3DModel.cs` (509) | Position/Rotation `InterpCurve<Vector3>`; `Load()`, `SampleTrajectory()`, `CommitChanges()`, `Changed` event. No WPF. |
| `CurveEditor3DFovModel` | `.../CurveEditor3DFovModel.cs` (223) | `InterpCurve<float>` FOV track; same pattern. No WPF. |
| `CurveEditor3DKeyframe` / `CurveEditor3DFovKeyframe` | `.../CurveEditor3DKeyframe.cs` (328), `.../CurveEditor3DFovKeyframe.cs` (133) | Value objects over `InterpCurvePoint`; callback-based commit. |
| Track evaluation math | `CurveEditor3D.xaml.cs` `EvaluateTrackMove`, `EvaluateQuaternionTrackRotation` | Standalone interpolation (slerp/linear). Candidate for extraction into a `TimelineEvaluator`. |
| `InterpTrackMoveTransform` | `Tools/InterpEditor/InterpTrackMoveTransform.cs` (27) | Small transform helper. |
| Test coverage | `LegendaryExplorer.Tests/UserControls/InterpTrackMove3D/*` | `CurveEditor3DFovModelTests`, `CurveEditor3DModelCacheTests`, `InterpTrackMoveTransformTests` — headless, protect the models. |

### Tier B — Dialogue-domain helpers (reusable with contract shaping, medium risk)
| Component | Path | Notes |
|---|---|---|
| `StageBoneOriginResolver` | `Tools/InterpEditor/StageBoneOriginResolver.cs` (1073) | Stage camera/bone/actor-attachment resolution. Dialogue-specific coordinate logic. |
| `CameraActorAnchors` | `Tools/InterpEditor/CameraActorAnchors.cs` (402) | Bone-name → camera anchor lookup. |
| `CameraPresets` | `Tools/InterpEditor/CameraPresets.cs` (1315) | Named camera/FOV presets. |
| `SavedCameraPresetManager` / `SavedDialogueCachePresetManager` | `Tools/InterpEditor/*.cs` | Persisted preset + cache preset storage. |
| `InterpEditorTracks` | `UserControls/ExportLoaderControls/InterpDataTimeline/InterpEditorTracks.cs` | Track data structures shared with the Interp timeline editor. |
| `ConversationExtended` / `DialogueNodeExtended` | `LegendaryExplorerCore/Dialogue/*` | Conversation graph model used to build timelines. |

### Tier C — UI-coupled (reference only; re-implement against contracts, high risk to lift directly)
| Component | Path | Notes |
|---|---|---|
| `CurveEditor3D.xaml.cs` | `.../CurveEditor3D.xaml.cs` (**14,751 lines**) | God-object. Contains dialogue config records, timeline building, runtime caching, playback loop, working-package editing, FaceFX, audio, undo/redo, WPF tab sync. Primary extraction subject — must be decomposed, not copied. |
| `ActorPreviewControl.xaml.cs` | `.../ActorPreviewControl.xaml.cs` (1468) | Skeleton/anim preview; bound to `LevelEditorRenderContext`. Reference for animation posing. |
| `AnimationPreviewControl` | `UserControls/SharedToolControls/AnimationPreviewControl.xaml.cs` | Canonical load-mesh→load-anim→scrub reference. |
| `GesturePreviewExportLoader` | `.../GesturePreviewExportLoader.xaml.cs` | Gesture/AnimControl timeline resolution. |

### Shared rendering/scene stack (already in target-ish shape)
`LevelEditorRenderContext`, `ActorProxy`, `SkeletalMeshComponentProxy`, `SkinnedMeshRenderer`, `AnimSequencePlayer`, `ModelPreview`, `PreviewTextureCache` (under `Tools/LevelEditor/Scene3D` and `UserControls/SharedToolControls/Scene3D`). These are the primary references for M1's rendering foundation and are already used by the previewer.

## 3. Implemented vs Unsupported Track Behavior

### Implemented (in scott-LEX previewer)
- Camera position/rotation/FOV evaluation (`ApplyCameraAtTime`, `EvaluateTrackMove`, FOV model).
- Actor transform tracks (`ApplyActorsAtTime`), quaternion slerp rotation.
- Director cut tracks (`BuildDirectorCameraCuts`, `GetPlaybackDirectorCut`).
- Multicam option enumeration (`RefreshMulticamPlaybackOptions`).
- Gesture / AnimControl playback (`RefreshAvailableGestureTracks`, gesture assignments).
- FaceFX attachment + line binding (`LoadDialoguePreviewFaceFxAssets`, `ApplyDialoguePreviewFaceFx`).
- Face-only VO events (`SFXInterpTrackPlayFaceOnlyVO`).
- Dialogue conversation tree building + scene-shop branch enumeration.
- Working-package editing with undo/redo (350ms batched snapshots).
- LE2 squadmate/henchman selection.

### Partial / harder (from `curve-editor-3d-port` positioning notes)
1. **Animation-driven travel (root motion)** — data available via `AnimPlayer.BoneComponentSpaceTransforms[0]`; needs wiring + chosen sample time.
2. **NPC posing via their own `InterpTrackMove`** — lowest-effort/high-value; reuse existing eval.
3. **Player pawn placeholder marker** — trivial; no runtime data needed.

### Unsupported / genuinely new work
- **`SetPawnFacing` / BioStage pawn placement** — CONFIRMED GAP. `BioStage.cs` stores only camera positions; `StageDirection` is descriptive text only; `SetPawnFacing` is a runtime Kismet action with no static stored position. Requires a new sequence/Kismet resolver. Highest effort.
- Full Unreal runtime parity, all track classes across all games, pixel-identical lighting/post — explicitly out of scope per plan.

## 4. Known Instability Patterns & Reproduction Notes
- **Reverted early visualisation attempts** (`interp editor visualisation basics/milestone 2`, `crappy attempt at 3D interpeditor rendering`) were rolled back on `curve-editor-3d-port` — earlier tight coupling of playback + rendering proved unstable. Lesson: keep rendering foundation (M1) independent of playback complexity (M2+).
- **God-object risk**: `CurveEditor3D.xaml.cs` at ~14.7k lines mixes lifecycle, threading, rendering, editing, audio, and undo. High regression surface; extraction must introduce contracts (M2) before feature growth.
- **Resource lifetime**: positioning notes and plan both flag disposal/thread-ownership for level packages (`levelPackages`, `levelActors`) and render thread as the top lifecycle risk — validate open/close soak in M1/M3.
- **Playback/audio clock ownership**: multiple audio flags (`dialoguePreviewAudioStarted`, `faceOnlyVoAudioStarted`) indicate clock-source ambiguity — must define single source of truth (deferred to M7 contract).

## 5. Extraction Plan for Clean Architecture
1. **Preserve the Tier-A models as-is** and keep their headless tests green; treat them as the seed of the contracts layer (M2).
2. **Extract track-evaluation math** (`EvaluateTrackMove`, quaternion rotation, FOV eval) out of `CurveEditor3D.xaml.cs` into a UI-free `TimelineEvaluator` behind a contract (feeds M2/M5).
3. **Wrap dialogue-domain helpers** (Tier B) behind a "dialogue/interp resolution contract" so the resolver (M4) does not depend on WPF.
4. **Reference, do not lift, Tier-C UI** — re-home logic into contracts; leave WPF glue thin. `CurveEditor3D.xaml.cs` becomes an orchestration shell over the new services.
5. **Reuse the Scene3D stack directly** for M1's rendering foundation (already decoupled enough).
6. **Sequence risky work last**: `SetPawnFacing`/BioStage resolver is new code — schedule under M6/M9 compatibility expansion, not the MVP path.

## 6. Feasibility Verdict
**GO.** A near-complete previewer already exists on `scott-LEX`; M0 confirms the work is decomposition/hardening rather than green-field. No no-go risks discovered. The single largest risk is the 14.7k-line god-object; the plan's M2 contracts milestone directly mitigates it.

### No-go risks
- None blocking. `SetPawnFacing`/BioStage posing is the only "genuinely new" subsystem and is out of the MVP critical path.
