# Level Editor loading and performance follow-up — 27 October 2026

The Visible Sets fix was confirmed working by the user. This follow-up addresses the loading, update and verification tasks identified in `VisibleSetsInvestigation.md`.

## Changes

### Loading, closing and package updates

`LevelEditorOperationQueue` serializes scene mutations. Appending levels preserves request order. A replacement cancels older active/queued requests, waits for their workers to finish, then starts changing the shared renderer. Window close also cancels and drains outstanding work before disposing packages or Direct3D resources. An obsolete worker's late error is logged without presenting an irrelevant failure dialog over its replacement.

Incoming files listen for package notifications during preparation but enter OpenFiles/the outliner only after preparation succeeds. Cancelled/failed loads dispose completed proxies and remove their lights/model state. `SceneLoadResourceScope` also rolls back newly registered static lighting, light-environment primitives and pending materials, including registrations left by a constructor that failed before returning a proxy. Committed scopes release their bookkeeping references.

Package notifications are marshalled onto the owning dispatcher, combined by file/export while a worker is active, and applied after it settles. Late notifications for disposed files are ignored. Nested render pauses resume only when the last guard finishes; a save-completion notification cannot unlock an active load. Drag/drop replacement now actually unloads the previous scene when the user chooses replacement.

Scene teardown clears selection, BioStage markers, transform groups, drag state and double-click history. These could otherwise retain old actor/package references after the files were closed. Removing/replacing a selected actor also interrupts its obsolete drag safely.

### Repeated lighting work

- `LevelLightPreviewUpdater` indexes a file's light/component bindings after actor or light-list changes. Ordinary frames visit actual light components without scanning every actor or searching the light list once per component.
- Multiple changed light previews publish one renderer snapshot per file/frame, preserving light order and selective membership.
- An externally refreshed light baseline is no longer overwritten by the marker's cached older snapshot. Uncommitted marker edits remain supported.
- Light visibility keys are cached while retaining package/export identity checks.
- `LevelLightUpdateDependencies` builds a bounded package-local reference closure from light sources, including property references, nested arrays/structs, archetypes/classes and supported binary references. Unrelated data changes skip the full-file light refresh. Header/table/name changes, unreadable data and oversized graphs retain conservative broad refresh behaviour. New light sources are detected, and BSP model edits invalidate the renderer's model state.
- Shader preparation runs when actors/lights changed or materials are pending, rather than after every unrelated export notification.
- Disposed models remove pending shader work; duplicate pending materials are coalesced. A temporary Direct2D surface wrapper is now disposed explicitly.
- Stationary camera coordinates reuse their existing formatted text.

The existing combined wireframe/Visible Sets behaviour is retained and tested. No broad UI-mode rewrite was needed.

## Measured CPU result

An isolated test process compared a reproduction of the previous scan/key-formatting loop with the production indexed updater. The scene had 256 light components, all included, with no edits during measurement. Each result is the median of five 300-frame batches after warm-up.

| Light-preview/contribution CPU loop | Time per 300 frames | Allocated bytes per batch |
| --- | ---: | ---: |
| Previous algorithm | 75.774 ms | 19,459,200 |
| Indexed production path | 24.166 ms | 0 |

This is approximately **3.14× faster for this CPU loop**. It is not a claim about whole-frame allocations or displayed viewport FPS. The reference loop omits some of the old renderer's additional snapshot-comparison work, so it does not exaggerate the old loop by adding rendering costs to it.

On the actual packages, the index contains 31 exports for Freighter's 26 lights and 6 exports for CargoBay's 3 lights. In the final full run, indexing took 1.401 ms and 0.198 ms respectively. It avoided rebuilding all lights for **96/96 Freighter** and **75/75 CargoBay** ordinary static-mesh updates. Direct source changes and conservative fallbacks remain covered by tests.

## Hardware and resource verification

The opt-in offscreen test creates the actual hardware Direct3D renderer, loads both unmodified packages, and executes the editor's rendering callbacks with game shaders and level lighting. It also performs:

1. Two overlapping replacements; only CargoBay's latest request is published (104 actors).
2. Adding Freighter and rendering both files: 331 actor proxies and 29 lights.
3. A mesh update while that mesh is hidden, verifying that the hide survives and the light snapshot/version stays unchanged.
4. Five GPU reloads, preserving 331 actors, 194 static-lighting registrations and the selective hide.
5. Cancellation during a further load, followed by draining and complete scene/context teardown.

The rendered image was read back and visually inspected; no renderer ErrorText was reported. `ReviewValidation/LevelEditor-Freighter-GPU.png` records the fixed top-down test camera at 640×360. Eight submissions plus readback took 128.91 ms in the final suite; other runs differed, so this is a verification measurement rather than an interactive FPS result.

After forced collections between reloads, the final full run recorded:

| Diagnostic | Five reloads |
| --- | --- |
| Tracked SharpDX COM wrappers | 2228, 2228, 2228, 2228, 2228 |
| Live managed heap, MiB | 490.1, 490.2, 490.2, 490.3, 490.3 |
| Private process memory, MiB | 1936.8, 1893.4, 1871.6, 1812.9, 1818.9 |

Tracked COM wrappers fell to **zero** after renderer teardown. Actor, lighting and pending-shader counts also returned to zero. These short tests found no accumulating live graphics-object count; they do not prove that every possible map/workflow is leak-free. Process reservation, caches and garbage collection make private-memory totals unsuitable as a leak assertion by themselves. Forced collection and native-object tracking are diagnostic test features only; they were not added to ordinary editor frames.

## Final validation and delivery

- WinRelease/x64 build succeeds with zero errors; existing dependency/analyzer warnings remain.
- **291/291 application tests pass**, including all 100 Level Editor tests and the opt-in package/GPU/performance checks.
- Both original PCC SHA-256 hashes match before and after this follow-up. No source package was saved.
- Headless editor tests disable recent-history persistence; GPU checks do not replace the user's saved view/preferences.
- Existing unrelated changes, including Interp Preview work, remain preserved.

The rebuilt executable is `LegendaryExplorer/LegendaryExplorer/bin/x64/WinRelease/net10.0-windows/LegendaryExplorer.exe`, relative to the repository root. Restart the development build to use these changes.

Evidence in `ReviewValidation`:

- `LevelEditor-performance-verified-build.log`
- `LevelEditor-performance-full-regression.trx` and `.log`
- `LevelEditor-performance-isolated-benchmark.trx` and `.log`
- `LevelEditor-performance-gpu-memory.trx` and `.log`
- `LevelEditor-performance-packages-before.json` and `-after.json`

The optional checks require `LEX_VISIBLE_SETS_TEST_DIRECTORY` pointing to the Freighter directory. Set `LEX_LEVEL_EDITOR_GPU=1` for hardware checks and `LEX_LEVEL_EDITOR_PERF=1` for the CPU benchmark; optionally set `LEX_LEVEL_EDITOR_ARTIFACT_DIRECTORY` for the image destination. Without those switches, ordinary tests do not require the game files or create a GPU renderer.
