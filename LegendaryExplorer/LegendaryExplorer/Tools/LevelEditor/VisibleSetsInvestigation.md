# Visible Sets investigation — 27 October 2026

## Confirmed cause and package evidence

The most recent saved view opens `BioD_Freighter_CargoBay.pcc` and `BioD_Freighter.pcc`, with Visible Set Only enabled, a distance of 5,000, and a top-down camera at approximately (-50, 6543, 40000). Its 199 actor keys include 104 CargoBay actors and 95 Freighter actors. Comparing those keys with the actual level actor tables found:

| Package | StaticMeshActors in the level | StaticMeshActors in the saved set |
| --- | ---: | ---: |
| BioD_Freighter.pcc | 96 | 0 |
| BioD_Freighter_CargoBay.pcc | 75 | 75 |

This is missing membership, rather than a package-wide mesh-loading or position failure. Static mesh actor/component positions were inspected; none had a component offset over 1,000 units. Package-qualified keys identify the correct files and exports. The main meshes in these files are ordinary StaticMeshActors, not StaticMeshCollectionActors.

Previously, `OpenVisibleSetsManager` inferred visible classes from any included actor. Because CargoBay included StaticMeshActors, it considered that class visible. It then classified **every absent Freighter mesh** as an explicit per-actor hide and reapplied those hides after rebuilding class membership. Consequently the dialog could show StaticMeshActor as visible while keeping the entire other file's meshes excluded. Add All did nothing when every class was already in the visible list. Clearing membership sometimes removed enough information to avoid that inference on the next attempt.

`AddLevelFile` also published new actors without updating membership. Closing all files cleared the keys but retained `HasUserEditedVisibleSets`, which could prevent initialization in the next scene. These are concrete paths to an incomplete or empty selection. The exact original sequence that produced the user's saved selection cannot be recovered from the saved file alone.

The faulty override inference is present in commit `ebcfcdff6` (29 May 2026), before the recent upstream integration. The evidence does not establish that the merge introduced this fault.

## Implementation

- Persist class policy and explicit actor overrides independently. Materialize package-qualified actor keys for the renderer's existing membership and distance check.
- Register new and replacement actors against that policy; preserve explicit choices during package updates and reloads. An isolated actor selection stays isolated when further actors arrive.
- Class moves deliberately reset overrides for the affected classes. Add All and Clear All apply even when their destination list already contains every class. Accepting unchanged classes preserves explicit actor edits; Clear Actor Level Settings removes them.
- Preserve legacy saved membership on restoration. Add All can explicitly recover a legacy incomplete selection. Save the new policy on the normal save path; do not silently rewrite preferences or switch the user to Full as a recovery mechanism.
- Reset visibility policy when the scene is closed, and suppress category synchronization while restoring a saved view or enabling a single actor's marker category.
- Keep visibility edits from restarting a viewport paused for loading. Membership is already evaluated every frame.
- Use floating-point distance squaring for nearby selection, avoiding integer overflow above 46,340. Use the same XY distance in top-down selection and rendering.
- Cache actor visibility keys, invalidating the cache if the package path or export index changes. Remove iterator allocations from the frequently evaluated volumetric category check. Sort once after a package-update batch, rather than once per replaced actor.

## Initial verification

The WinRelease/x64 application and tests build successfully with .NET SDK 10.0.401. The complete application test assembly passes **272/272 tests**, including 81 Level Editor tests. Build warnings include existing dependency audit and analyzer warnings; the build has zero errors.

New coverage includes class/actor independence, package arrival, repeated hide/show and clear/re-add cycles, Full/Visible Set mode changes, legacy restoration, new-format serialization, replacement proxies, isolated actors, category markers, paused rendering, large nearby distances, and top-down distance handling. Existing collection-transform, lighting, shader and selection tests also pass.

An optional test reads both actual Freighter packages, reconstructs the observed missing-membership condition, applies class selection, tests selective hiding and repeated mode changes, then reopens Freighter and checks that the explicit hide survives. At the saved camera and distance, the corrected policy admits 79/96 Freighter meshes and 75/75 CargoBay meshes. The other 17 Freighter origins are beyond the distance limit; increasing that limit is still necessary to display them from this camera.

The initial real-package test uses actual export identities and actor positions with the renderer's visibility predicate. It does not instantiate GPU mesh resources or certify final rendered pixels. The follow-up below adds hardware rendering, GPU reloads and overlapping-load/cancellation checks.

Both source PCCs have identical SHA-256 hashes before and after investigation. Existing Interp Preview edits, including the pre-existing changes in LevelEditor.xaml.cs and PreviewLevelLightRegistry.cs, were preserved.

Validation artifacts are in the repository's `ReviewValidation` directory:

- `VisibleSets-full-regression.trx` and `.log`: final 272-test run.
- `VisibleSets-verified-build.log`: final build.
- `VisibleSets-packages-saved-view.trx`: original package/selection evidence.
- `VisibleSets-package-hashes-before.json` and `-after.json`: package integrity.

To rerun the local package regression, set `LEX_VISIBLE_SETS_TEST_DIRECTORY` to the Freighter directory and run the tests. It is skipped when that environment variable is absent.

## Follow-up status

The user subsequently authorized the recommended follow-up work. `LevelEditorPerformanceValidation.md` records the completed changes and measurements:

1. Serialized loading, cancellation, deferred package updates and resource cleanup now protect overlapping open/close operations. Hardware checks cover overlapping replacements, reload and cancellation.
2. Indexed/batched light previews and a conservative dependency index reduce repeated work. The isolated light-loop benchmark is about 3.14× faster with zero measured steady-frame allocation in that loop. Unrelated mesh updates no longer rebuild every light.
3. Combined wireframe/filter behaviour is retained and covered by a restoration test. Scene teardown also drops obsolete selection/group references.

The follow-up build passes 291 application tests. Its offscreen GPU and memory checks complement the user's confirmation that the original Visible Sets problem is resolved.

## Manual viewport check

1. Start the rebuilt application and load both Freighter packages. Open Visible Sets → Edit Visible Sets, press Add All, then OK once to clear legacy per-actor exclusions. This deliberately resets actor overrides for the listed classes.
2. Select Visible Set Only. Increase Visible Sets Dist as needed to cover the level; Full ignores this limit.
3. Hide one Freighter mesh; verify CargoBay remains visible. Repeat class hide/show and Clear All/Add All, then switch Full → Visible Set Only repeatedly.
4. Reload the packages and check that deliberate actor hides persist. Check normal mesh rendering, picking, collision overlays and shadows in the live viewport.

See the follow-up report for the measured scope and limitations; no displayed viewport FPS claim is made.
