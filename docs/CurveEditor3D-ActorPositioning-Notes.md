# 3D Curve Editor — Actor Positioning Notes

Design notes for a future feature: more accurately positioning pawns/actors in the
3D Curve Editor (`CurveEditor3D`). Captured so work can resume later.

## Problem statement

The 3D Curve Editor currently renders a level backdrop by walking the level's
`Actors` array and drawing each actor at its **authored/starting** `Location`. Two
positioning gaps exist:

1. **No player pawn representation.** The player (`SFXPawn_Player`) is spawned at
   runtime and is not a static actor in the level `.pcc`, so `LoadActors` has
   nothing to draw for it.
2. **NPC pawns show at their starting positions.** Actors render at their authored
   `Location`. The editor has no knowledge of what later moves them, so they sit at
   spawn rather than where they end up during a scene.

Actors are moved by several different mechanisms:
- `InterpTrackMove` (matinee move tracks) — already understood by the editor.
- `SetPawnFacing` kismet actions placing pawns on BioStage nodes.
- Animations that carry the pawn (root motion).

## Current loading code

- `CurveEditor3D.xaml.cs`
  - `LoadLevelAsync(string path, bool replace)` (~line 1451): opens a level package,
	finds the `Level` binary, calls `LoadActors`.
  - `LoadActors(Level level)` (~line 1494): builds `ActorProxy` objects from the
	level's `Actors` array. Handles `StaticMeshCollectionActor`; **skips**
	`StaticLightCollectionActor` (light proxies not ported to this branch); uses
	`ActorProxy.Create` for everything else. Actors render at their `Location`.
  - Camera playback already evaluates the move track:
	`model.PositionTrack?.Eval(time, Vector3.Zero)` (~line 1192).

## Existing animation/skeletal machinery (already present in this branch)

This branch already contains a complete skeletal animation system — the same
machinery Scott's animation viewers use. This is good news: much of the hard work
for animation-based posing already exists.

| Component | File | Role |
|---|---|---|
| `AnimSequencePlayer` | `LegendaryExplorerCore/Unreal/Animation/AnimSequencePlayer.cs` | Samples an `AnimSequence` per-frame; builds component-space bone transforms + skinning matrices. Key methods: `SetAnimation(AnimSequence)`, `ComputeSkinningMatrices()`, `SamplePosition/SampleRotation`. |
| `AnimPlayer` (base) | `LegendaryExplorerCore/Unreal/Animation/AnimPlayer.cs` | Abstract base. Exposes `BoneComponentSpaceTransforms[]` — **the key to root motion**. `ComputeBindPose()` builds bind/inverse-bind matrices from `SkeletalMesh.RefSkeleton`. |
| `SkinnedMeshRenderer` | `Tools/LevelEditor/Scene3D/SkinnedMeshRenderer.cs` | GPU skinning. `BuildFromSkeletalMesh()`, `UpdateSkinning()` (calls `animPlayer.ComputeSkinningMatrices()`). |
| `SkeletalMeshComponentProxy` | `Tools/LevelEditor/Components.cs:312` | Ties a skeletal mesh actor to an `AnimSequencePlayer`. `SetAnimation()`, `UpdateScene()`, `ApplyMorph()`. |
| `PawnProxy` / `BioPawnProxy` | `Tools/LevelEditor/Actors.cs:924-969` | Pawn representation. `BioPawnProxy` is multi-mesh (body/head/hair/accessories); `SetAnimation()` applies to all sub-meshes. |
| `AnimationPreviewControl` | `UserControls/SharedToolControls/AnimationPreviewControl.xaml.cs` | Reference implementation for load mesh -> load anim -> scrub/play. `LoadSkeletalMesh()`, `LoadAnimSequence()`, `UpdateSkinningOneShot()`, `OnUpdateScene()`. |
| `AnimSequence` (data) | `LegendaryExplorerCore/Unreal/BinaryConverters/AnimSequence.cs` | `RawAnimationData` -> `AnimTrack` (`Positions`, `Rotations`); `DecompressAnimationData()`. |
| `SkeletalMesh` (data) | `LegendaryExplorerCore/Unreal/BinaryConverters/SkeletalMesh.cs` | `RefSkeleton` array of `MeshBone` (`Name`, `Orientation`, `Position`, `ParentIndex`). |

## What is genuinely missing / harder

1. **Animation-driven travel (root motion).** To move a pawn to where an animation
   carries it, extract the root bone's world position from
   `AnimPlayer.BoneComponentSpaceTransforms[0]` at a chosen frame and add it to the
   actor's `LocalToWorld`. Data is available; needs wiring + a chosen sample time.

2. **`SetPawnFacing` / BioStage nodes — NO existing resolver.** Confirmed gap:
   - `BioStage.cs` stores only **camera** positions (`CameraList`), NOT pawn
	 placements.
   - `StageDirection` (`LegendaryExplorerCore/Dialogue/StageDirection.cs`) is
	 **descriptive text only** — no position/rotation.
   - `SetPawnFacing` is a **runtime Kismet action** (`SeqAct_*`); there is no static
	 position stored anywhere. Resolving it requires new logic: parse the sequence,
	 find the target node/actor reference, resolve its world location. This does not
	 exist in LEX today.

## Suggested roadmap (easiest -> hardest)

1. **Pose NPCs with their `InterpTrackMove`** (if present). Reuse the same track-eval
   already used for camera playback (`PositionTrack.Eval`). Associate each
   `ActorProxy` with the `InterpTrackMove` that drives it (walk InterpTrackMove ->
   InterpGroup -> InterpData/SeqAct_Interp -> target actor), then offset the proxy's
   transform. Lowest effort, high value.

2. **Player pawn placeholder.** Draw a simple capsule/marker at the trajectory's
   evaluated position (the editor already knows `model.PositionTrack`). No runtime
   data needed. Optionally resolve whether the interp targets the player group to
   label it.

3. **Animation root-motion posing.** Reuse `SkeletalMeshComponentProxy.SetAnimation()`
   + read root bone transform from `BoneComponentSpaceTransforms[0]`. Medium effort;
   all pieces already exist in the branch.

4. **`SetPawnFacing` / BioStage resolution.** Requires a new Kismet/sequence resolver
   to turn runtime pawn-placement actions into world positions. Highest effort;
   genuinely new code.

## Connection points in `CurveEditor3D`

- Actor list + transforms: `levelActors` (`List<ActorProxy>`), `LoadActors`,
  `ActorProxy.LocalToWorld` / `Location` / `Rotation`.
- Track evaluation already used: `model.PositionTrack?.Eval(time, ...)`.
- Rendering loop: `foreach (ActorProxy actor in RenderContext.DrawList_3D)`
  (~line 1280).

## Notes on Scott's fork

Scott's fork (remote `scottina`) has animation viewers in various places. The
underlying `AnimSequencePlayer` / `SkinnedMeshRenderer` / `SkeletalMeshComponentProxy`
stack referenced above is already present in this branch, so those are the primary
references for reusing animation posing rather than re-porting fork-specific viewers.
