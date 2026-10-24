# Interp Preview M4 Handover (2026-10-24)

## Scope of this handover
This handover captures where the Interp Preview M4 implementation currently stands on branch `becca-LEX`, what was validated, what remains, and what a new agent should do first.

## Environment and branch
- Workspace root: `D:\sources\beccaLEX\LegendaryExplorer\`
- Solution: `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\LegendaryExplorer.sln`
- Active branch: `becca-LEX`
- Do not work on `scott-LEX` (reference only if needed).

## High-level progress
M4 resolution pipeline and diagnostics have advanced substantially. The latest slices and follow-ups are implemented and repeatedly validated in focused test runs plus human runtime logs.

Recent completed follow-ups include:
- Source diagnostic exclusivity (`INTERP_DISCOVERY_SELECTED` vs `INTERP_DIRECT` vs `INTERP_FALLBACK`)
- Status feedback refinement in preview shell
- Scene-binding diagnostic de-duplication for owner/player cases
- Target extraction support additions for:
  - `InterpTrackFloatProp`
  - `BioEvtSysTrackSetFacing`
  - `BioEvtSysTrackSwitchCamera`
  - `BioEvtSysTrackDOF`
  - `BioEvtSysTrackVOElements`
- `InterpTrackDirector` reclassified to a non-actor track with explicit diagnostic
- Director-group missing link clarity (`TARGET_GROUPVAR_DIRECTOR`)
- LookAt refinement: prefer direct tag (`m_nmFindActor`) when present; fallback to group-variable path

## Current behavior snapshot (important)
1. Source selection diagnostics are mutually exclusive and now match selected source precedence.
2. Owner/player speaker diagnostics are less noisy (no redundant generic speaker line for those cases).
3. Director-style group misses are explicitly labeled and no longer mixed with generic missing-link wording.
4. `InterpTrackDirector` is no longer treated as unsupported actor-target extraction; it emits `TARGET_TRACK_NONACTOR`.
5. Several formerly unsupported classes now resolve into concrete target attempts, which means logs now often show `ACTOR_TAG_MISSING` (expected when scene actors are absent).

## What remains (M4-focused)
### A) Immediate open validation gate
- `LookAt Direct-Tag Refinement` still needs a clean manual confirmation log where direct-tag usage is clearly distinguishable from group-name fallback behavior.

### B) Remaining M4 functional gaps
- Actor binding still depends on currently loaded scene actors; cross-file pawn resolution for missing owner/player/other targets is still deferred.
- Additional track classes may still appear under `TARGET_TRACK_UNSUPPORTED` depending on node content.
- Broader M4 completion items in the master checklist (identity/provenance breadth, ambiguity handling depth, fixture breadth) remain partially complete.

### C) Stability/process items
- Keep sequential test/build execution for reliability in this workspace.
- Full-solution builds may still surface unrelated WPF/generated file contention or environment-specific issues; focused project/test validation has been reliable.

## Key files changed and/or central to continue work
- `LegendaryExplorer/Tools/InterpEditor/InterpPreviewTrackTargetReader.cs`
- `LegendaryExplorer.Tests/Tools/InterpEditor/InterpPreviewTrackTargetReaderTests.cs`
- `LegendaryExplorer/Tools/InterpEditor/InterpPreviewDialogueResolver.cs`
- `LegendaryExplorer.Tests/Tools/InterpEditor/InterpPreviewDialogueResolverTests.cs`
- `LegendaryExplorer/Tools/InterpEditor/InterpPreviewShellWindow.cs`
- `LegendaryExplorer/Tools/Dialogue Editor/DialogueEditorWindow.xaml.cs`
- `LegendaryExplorer/Tools/InterpEditor/InterpPreview_Plan.md`

## Recommended first actions for a new agent
1. Read the project instructions and design docs listed below.
2. Read the latest sections in `InterpPreview_Plan.md` (M4 follow-up checkpoints).
3. Re-run focused validation command (below) to establish a clean baseline.
4. Request/collect one runtime log that explicitly proves LookAt direct-tag preference in a case where `m_nmFindActor` differs from group-name fallback.
5. Continue with the next highest-frequency unsupported track class seen in logs, while preserving existing diagnostics semantics.

## Validation commands (known good)
Run from `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer`:

- Focused tests used throughout recent work:
  - `dotnet test LegendaryExplorer.Tests\LegendaryExplorer.Tests.csproj -c WinRelease --filter "FullyQualifiedName~InterpPreviewTrackTargetReaderTests|FullyQualifiedName~InterpPreviewDialogueResolverTests"`

- Optional broader focused run used earlier in this effort:
  - `dotnet test LegendaryExplorer.Tests\LegendaryExplorer.Tests.csproj -c WinRelease --filter "FullyQualifiedName~InterpPreview"`

## Known diagnostic codes now in active use
- Source selection: `INTERP_DISCOVERY_SELECTED`, `INTERP_DIRECT`, `INTERP_FALLBACK`
- Speaker/owner/player: `SCENE_BIND_OWNER_*`, `SCENE_BIND_PLAYER_*`, limited generic `SCENE_BIND_SPEAKER_*`
- Target extraction/binding: `TARGET_GROUPVAR_MISSING`, `TARGET_GROUPVAR_DIRECTOR`, `TARGET_TRACK_NONACTOR`, `TARGET_TRACK_UNSUPPORTED`, `ACTOR_TAG_MISSING`, `ACTOR_TAG_AMBIGUOUS`, `ACTOR_TAG_BOUND`

## Risks and cautions for continuation
- Do not regress Dialogue Editor shared-window behavior and Level Editor context retention.
- Do not reintroduce duplicate source diagnostics.
- Keep canonical owner/player conventions (player is `player`; owner tags are canonical, no alias expansion fallback hacks).
- Do not mark manual validation complete without user-provided runtime evidence.

## Local document links to provide to the next agent
Use these as the primary handoff bundle:

1. Project instructions
   - `D:\sources\beccaLEX\LegendaryExplorer\.github\copilot-instructions.md`

2. Master tracking plan (latest checkpoints)
   - `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\Tools\InterpEditor\InterpPreview_Plan.md`

3. M4 design handoff
   - `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\Tools\InterpEditor\InterpPreview_M4_ResolutionDesign.md`

4. M3.5 lifecycle design (prereq context)
   - `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\Tools\InterpEditor\InterpPreview_M3_5_LifecycleDesign.md`

5. M3.5 thread ownership design
   - `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\Tools\InterpEditor\InterpPreview_M3_5_ThreadOwnershipDesign.md`

6. Target reader implementation
   - `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\Tools\InterpEditor\InterpPreviewTrackTargetReader.cs`

7. Target reader tests
   - `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer.Tests\Tools\InterpEditor\InterpPreviewTrackTargetReaderTests.cs`

8. Resolver implementation
   - `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\Tools\InterpEditor\InterpPreviewDialogueResolver.cs`

9. Resolver tests
   - `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer.Tests\Tools\InterpEditor\InterpPreviewDialogueResolverTests.cs`

10. Shell/status behavior
	- `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\Tools\InterpEditor\InterpPreviewShellWindow.cs`

11. Dialogue Editor launch/reuse path
	- `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\Tools\Dialogue Editor\DialogueEditorWindow.xaml.cs`

12. This handover file
	- `D:\sources\beccaLEX\LegendaryExplorer\LegendaryExplorer\Tools\InterpEditor\InterpPreview_Handover_2026-10-24.md`
