# Interp Preview M3.5 — Lifecycle, Ownership, and Concurrency Design

## Status

- **Design state:** Complete; ready for implementation review
- **Scope:** Interp Preview only, on `becca-LEX`
- **Production code changed by this step:** None
- **Precondition for M4:** The M3.5 implementation and exit criteria must pass before playback plans retain scene or actor bindings.

## Purpose

Define one coherent lifecycle and ownership model for Interp Preview so that loading, replacing, Player-context injection, dialogue resolution, cancellation, rendering, and shutdown cannot race or leak resources.

This design replaces the current split responsibility in which:

- the loader creates packages and actor proxies;
- the shell separately commits them;
- the session stores package and actor lists;
- the render context disposes actor proxies;
- the session disposes packages; and
- the load coordinator can be disposed without first joining its active operation.

The target design gives every disposable resource exactly one owner, makes scene changes transactional, and confines mutable scene/render state to the WPF dispatcher.

## Protected Boundaries

The implementation must preserve all of the following:

1. Interp Preview opens independent package instances. It does not borrow or dispose `LevelEditor.OpenFiles` packages or actors.
2. Level Editor remains usable and independently closable while the shared Interp Preview remains open.
3. Interp Preview does not change Level Editor visible-set behavior or globally alter `LevelEditorRenderContext.LoadActors()` semantics.
4. Player remains the canonical logical speaker name `player`.
5. Conversation owner tags remain canonical; no alias expansion is introduced.
6. The shared preview window remains reusable by Dialogue Editor and Level Editor.

## Design Invariants

These rules are mandatory implementation invariants:

1. **Single resource owner:** Every source package and every `ActorProxy` is owned by exactly one candidate or one committed scene resource.
2. **Render registration is non-owning:** Registering an actor for rendering never transfers ownership and never disposes it.
3. **Indexing is non-owning:** Actor lookup indexes contain references/identities only and never dispose actors or packages.
4. **Candidate-before-commit:** New resources remain candidate-owned until the complete operation is validated and committed.
5. **All-or-nothing publication:** A failed, canceled, stale, or closing-time operation cannot partially alter the committed scene.
6. **Latest request publishes:** Only the latest accepted operation may publish diagnostics, update status, commit a scene, inject Player context, or publish dialogue resolution.
7. **Generation-bound data:** Any result that retains scene actor bindings is stamped with the committed scene generation and is invalid after that generation changes.
8. **Dispatcher-confined mutation:** Scene membership, actor indexes, render registration, proxy construction/disposal, diagnostics publication, and WPF state mutation occur on the preview window dispatcher.
9. **No blocking dispatcher joins:** Shutdown and supersession await work asynchronously; the dispatcher is never synchronously blocked waiting for work that needs the dispatcher to finish.
10. **Exact-once registration/disposal:** Every committed actor is registered once, unregistered once, and disposed once. A candidate actor that never commits is disposed once and is never left registered.
11. **Package-after-proxy disposal:** A resource’s actor proxies are disposed before its source package.
12. **Closing is terminal:** Once lifecycle state becomes `Closing`, no new request is accepted and no operation may publish user-visible or scene state.

## Terminology

### Operation ID

A monotonically increasing `long` assigned to every accepted preview request.

- It identifies request ordering.
- A newly accepted request supersedes and cancels the prior request.
- Only the operation whose ID equals the coordinator’s latest accepted ID may commit or publish.
- It does not identify scene contents.

### Scene generation

A monotonically increasing `long` assigned only when committed scene membership changes successfully.

It advances after:

- successful level collection replacement;
- successful level addition/removal;
- successful Player-context add, replacement, or removal; or
- explicit scene clear while open.

It does **not** advance after:

- cancellation;
- failure;
- stale/superseded completion;
- duplicate/no-change requests; or
- dialogue resolution that does not mutate the scene.

### Resource key

A structured identity, not a pseudo-file path:

- resource kind (`Level` or `PlayerContext`);
- game;
- canonical full source path using case-insensitive Windows path comparison; and
- Player variant when kind is `PlayerContext`.

A `#PlayerContext:Female` suffix must not be embedded in a filesystem path. Diagnostics can format a display label separately.

### Actor identity

Stable identity within a scene:

- resource key; plus
- export UIndex.

Tags, object names, display text, and class names are lookup metadata, not final actor identity.

## Lifecycle State Machine

The preview runtime has exactly three externally meaningful lifecycle states.

### `Open`

- Accepts operations.
- May own a committed scene.
- Rendering and diagnostics publication are allowed.
- New requests receive a new operation ID and supersede older requests.

### `Closing`

- Entered once and never reversed.
- New operations are rejected with a non-exceptional `RejectedClosing` outcome.
- Active/pending operations are canceled.
- No stale operation may commit or publish.
- The runtime asynchronously waits for operation cleanup before tearing down committed resources.

### `Disposed`

- No active operation, committed resource, render registration, event subscription, or graphics resource remains.
- Public operations are rejected or throw `ObjectDisposedException` according to the API contract; UI entrypoints should receive `RejectedClosing` before disposal rather than invoke disposed objects.
- Disposal is idempotent.

### Operation substate

`Idle` and `Running` are coordinator substates, not lifecycle states. Lifecycle and operation state must not be conflated.

## High-Level Operation Model

Replace the load-only coordinator with one preview-scoped operation coordinator. A high-level request owns the whole workflow rather than exposing a loaded result for the shell to commit later.

The coordinator covers:

1. request acceptance and operation ID allocation;
2. prior-operation cancellation;
3. canonicalization and validation;
4. candidate package loading/parsing;
5. dispatcher-owned actor realization;
6. duplicate/no-change revalidation;
7. transactional scene commit;
8. optional Player-context update;
9. dialogue resolution against the committed generation;
10. diagnostics/status publication; and
11. candidate cleanup on every non-commit path.

There must be no callback equivalent to the current `onReplace`. Replacement is an operation mode, not an early destructive callback.

### Request categories

The coordinator should support explicit request categories:

- **Replace scene:** Replace the base level collection as one batch transaction.
- **Add level:** Add one resource without changing existing resources.
- **Clear scene:** Remove all resources while remaining open.
- **Set Player context:** Ensure, replace, or remove the single Player resource.
- **Open dialogue node:** Compose required scene loading, Player policy, and dialogue resolution into one ordered request.
- **Resolve dialogue:** Resolve against a captured committed generation without changing scene membership.

Dialogue Editor and Level Editor should each submit one high-level request. They must not loop over public single-level commits or perform Player injection and dialogue resolution as separate uncoordinated calls.

### Operation outcomes

Use explicit outcomes with no ownership ambiguity:

- `Committed`
- `NoChange`
- `Superseded`
- `Cancelled`
- `Failed`
- `RejectedClosing`

A result may contain immutable diagnostics and the resulting scene generation. It must never return an owned package/proxy candidate to UI code.

## Latest-Request Semantics

1. Accepting request N increments the latest operation ID and cancels request N-1.
2. Only one operation pipeline builds/commits at a time.
3. Cancellation is cooperative; superseded work remains tracked until its cleanup completes.
4. A candidate reaching the commit boundary must re-check:
   - lifecycle is `Open`;
   - its operation ID is still latest;
   - cancellation has not been requested; and
   - assumptions used during preflight still match the current scene generation/resource keys.
5. If any check fails, the candidate is disposed and returns `Superseded`, `Cancelled`, or `RejectedClosing` without changing the scene.
6. Diagnostics and caller status updates carry operation ID and are ignored unless current.

The coordinator owns cancellation sources. It cancels them but does not dispose synchronization primitives until the corresponding operation has exited.

## Scene and Resource Ownership

### `InterpPreviewOwnedResource`

One owned unit represents either one level or one Player context. Conceptually it contains:

- structured resource key;
- diagnostic display name;
- independently opened source `IMEPackage`;
- actor proxies belonging to that resource;
- stable actor identities and lookup metadata; and
- ownership state (`Candidate`, `Committed`, `Transferred`, `Disposed`).

Disposal order:

1. assert/unregister any remaining render registrations through the render registry;
2. dispose every actor proxy exactly once;
3. clear actor/index references;
4. dispose the source package exactly once; and
5. mark the unit disposed.

The resource does not own `LevelEditorRenderContext.PackageCache`. That cache remains owned by the preview render context. Per-resource removal must not globally release it while other committed actors remain.

`PackageCache`, texture caches, shader caches, and other shared render caches have render-context lifetime rather than scene-resource lifetime. Candidate rollback and resource retirement dispose candidate-owned models/proxies immediately, then request dispatcher-owned stale-cache maintenance that is safe while other resources remain. Full cache release occurs only during clear/shutdown or through an existing cache API proven not to invalidate committed actors.

### `InterpPreviewSceneCandidate`

A candidate owns all newly staged resources until commit.

- It may be built incrementally.
- It disposes partially created proxies if realization fails or cancellation arrives.
- Successful commit transfers its owned resources to the committed scene and leaves the candidate empty/non-owning.
- Candidate cleanup is in the operation’s `finally` path.

### Committed scene

The session becomes the authoritative owner of committed resource units. It maintains:

- an ordered resource collection;
- at most one Player-context resource;
- current scene generation; and
- an immutable scene snapshot projection.

The immutable snapshot contains:

- scene generation;
- ordered resource descriptors;
- actor references/identities;
- immutable lookup candidate arrays; and
- current game/Player-context metadata.

Snapshot collections are immutable, but actor proxies remain dispatcher-confined mutable render objects. Consumers must not retain a snapshot beyond its generation for playback binding.

- A snapshot containing `ActorProxy` references may be captured and consumed only on the preview dispatcher.
- Background resolution must use a detached immutable metadata DTO containing actor identities, lookup keys, and binding provenance—never live proxies or packages.
- The legacy `LoadedLevelPaths` projection includes only resources of kind `Level`. Player-context source paths are exposed through structured Player metadata and must not be searched as conversation-owner level packages.

### Render registry

The preview render coordinator becomes a non-owning exact-once registry.

- Register only newly committed actor deltas.
- Track registrations by actor reference/identity.
- Reject or ignore duplicate registration.
- Unregister without disposing.
- Render/update from the current immutable scene snapshot, not a mutable session list.
- Assert that no registered actors remain before render-context disposal.

Routine preview transactions must not call `LevelEditorRenderContext.UnloadLevel()`, because that method combines render clearing with proxy disposal. The committed resource owner controls disposal. The preview should unregister actor deltas and then dispose their owning resources.

## Transaction Semantics

All commit work occurs synchronously on the preview dispatcher with no `await` inside the commit critical section. WPF rendering cannot interleave with that section.

Before commit, the candidate has already:

- loaded and validated all required resources;
- realized all required proxies;
- resolved attachments;
- built its lookup metadata; and
- passed cancellation/staleness checks.

### Replace scene

1. Stage the complete requested collection while the current scene remains committed and usable.
2. At commit, re-check lifecycle, operation ID, and canonical desired resource set.
3. Register candidate actors through a rollback-capable preview render registry.
4. Unregister actors belonging to resources that will retire.
5. Transfer candidate resources into the committed scene and atomically publish the new immutable snapshot with `SceneGeneration + 1`.
6. Dispose retired resources after they are no longer visible to rendering or lookup.
7. Publish success diagnostics/status for the current operation.

If registration or in-memory publication fails:

- remove every newly registered candidate actor;
- restore any retired registration if removal had begun;
- retain the previous committed scene and generation; and
- dispose the candidate.

Disposal failures after a successful publication are reported and aggregated, but do not roll back the now-valid new scene.

### Add level

1. Preflight canonical key against the captured snapshot.
2. Build a candidate resource.
3. Re-check for duplicate at commit.
4. Register only candidate actors.
5. Transfer the resource into the scene, rebuild immutable projections, and increment generation.
6. A duplicate at either check is `NoChange`; no generation increment occurs.

### Player context

Player is a structured singleton resource, not an ordinary level path.

- `Ensure(variant)` is `NoChange` if the correct game and variant are committed.
- Changing variant stages the replacement pawn before retiring the existing Player resource.
- Successful replacement increments scene generation.
- A scene change to another game removes an incompatible Player resource.
- Player lookup metadata adds canonical alias `player` to the selected pawn without writing a synthetic `Tag` property into the source package.
- At most one Player resource can exist after any commit.

### Clear scene

- Cancel/supersede any older request.
- On dispatcher, unregister all committed actors.
- Publish an empty snapshot with the next generation.
- Dispose all detached resources in proxy-before-package order.
- Preserve the render context/window for future loads.

### Dialogue resolution

- Capture a scene snapshot and generation.
- Resolve only against that snapshot.
- Stamp the result with the captured generation.
- Publish it only if the operation remains current and the scene generation is unchanged.
- M4 playback plans must reject use when their generation differs from the current scene generation.

## Loading and Thread-Affinity Model

### Background-safe phase

A worker thread may operate only on resources exclusively owned by that operation. It may:

- canonicalize and validate paths;
- open independent package instances;
- locate and parse the level export;
- enumerate actor/component export identities;
- collect plain-data build descriptors; and
- perform CPU-only parsing proven not to touch render/WPF state.

No package may be accessed concurrently by background and dispatcher phases. Ownership is handed off at an explicit await/queue boundary.

### Dispatcher/render-owner phase

The preview dispatcher exclusively owns:

- `ActorProxy.Create` and component proxy construction;
- `MeshRenderContext`, `PackageCache`, texture cache, and graphics resources;
- actor attachment resolution involving proxies;
- actor proxy disposal after realization begins;
- render registration/unregistration;
- committed scene and lookup publication;
- lifecycle transitions;
- diagnostics event publication;
- observable collections, status text, and all WPF controls; and
- scene-viewer disposal.

Current actor constructors resolve through `MeshRenderContext.PackageCache` and create `ModelPreview`/graphics resources, so they are not background-safe.

### Responsive realization

To avoid replacing one UI freeze with a correct but unresponsive implementation:

1. realize proxies on the dispatcher in bounded batches;
2. yield to the dispatcher between batches;
3. check operation ID, lifecycle, and cancellation between batches; and
4. retain candidate ownership of every successfully realized proxy until commit.

Batch size should be configurable/internal and selected through responsiveness testing. Correctness must not depend on a specific batch size.

Do not add locks around live render enumeration. Dispatcher confinement plus immutable snapshot publication is the synchronization model.

## Asynchronous Shutdown Contract

Window closing must use a cancel-first asynchronous close pattern.

### WPF close flow

1. On the first `Closing` event while `Open`, set `e.Cancel = true`.
2. Transition lifecycle atomically to `Closing`.
3. Disable new commands and stop requesting new render frames.
4. Start/await `ShutdownAsync()` without synchronously blocking the dispatcher.
5. When shutdown completes, set an internal allow-close flag and invoke `Close()` again on the dispatcher.
6. The second close proceeds without repeating shutdown.

### `ShutdownAsync()` order

1. Reject new operations.
2. Increment/invalidate the latest operation ID and cancel active/pending operations.
3. Await the operation pump, including candidate cleanup. The coordinator/gate remains alive during this wait.
4. On the dispatcher, detach update/render callbacks.
5. Unregister every committed actor without disposing it through the render context.
6. Detach all committed resource units and publish/retain no live snapshot for consumers.
7. Dispose actor proxies, then source packages, for every detached resource.
8. Unsubscribe diagnostics and shared-window handlers.
9. Dispose coordinator synchronization/cancellation objects.
10. Dispose the scene viewer/render context and its caches.
11. Transition to `Disposed`.

No active operation may reference the render context, session, diagnostics publisher, or window after step 3.

Shutdown is idempotent. Exceptions during cleanup are aggregated/logged while remaining cleanup steps continue.

## Diagnostics and UI Publication

- Runtime diagnostics are immutable value snapshots.
- Diagnostics changes are marshaled to the preview dispatcher before raising UI-facing events.
- Each operation-produced diagnostic batch includes operation ID and scene generation where applicable.
- A superseded operation may complete cleanup but cannot replace current diagnostics or status.
- During `Closing`, cleanup errors may be logged, but no event is raised into a window being torn down.
- UI launch methods should await a task-returning API. `async void` remains only at the outer event/command boundary, with exception handling and current-operation result checks.

## Shared-Window Ownership

Dialogue Editor and Level Editor are clients, not owners, of the shared preview.

- They submit requests and may activate the window.
- They do not dispose the runtime, scene resources, or preview window.
- They do not retain preview-owned tasks or packages.
- Shared-window discovery should use a preview window registry/service with a weak reference or application window lookup.
- Remove the Level Editor lambda that subscribes to preview `Closed` while capturing the Level Editor. Prefer no reverse event subscription; otherwise use a named handler and unsubscribe when either side closes.
- Closing any Level Editor must not close the preview or affect another editor’s request except through an explicit new preview operation.

## Proposed Contract Direction

Names may be adjusted during implementation, but responsibilities must remain separated.

### Operation coordinator

`IInterpPreviewOperationCoordinator`

- `SubmitAsync(InterpPreviewOperationRequest, CancellationToken)`
- `CancelActive()`
- `StopAsync()`
- exposes lifecycle/current operation only as immutable state

It owns request ordering, cancellation sources, the serialized operation pump, and candidate cleanup.

Operation acceptance is thread-safe, but all current UI clients submit from the preview dispatcher. Allocation of operation IDs and cancellation-source replacement occurs under one coordinator synchronization boundary; a source is disposed only by the operation that owns it after that operation exits.

### Scene owner

`IInterpPreviewScene`

- exposes current immutable snapshot;
- owns committed `InterpPreviewOwnedResource` instances;
- performs dispatcher-only transaction commits;
- returns detached retired resources to the transaction for ordered disposal; and
- does not directly manipulate WPF controls.

### Resource builder

Split the current loader responsibilities:

- background package/descriptor loader; and
- dispatcher-owned actor realizer.

Both produce or extend one candidate whose ownership is explicit.

### Render registry

`IInterpPreviewRenderRegistry`

- register actor delta with rollback tracking;
- unregister actor delta without disposal;
- expose/assert registered identities for tests; and
- attach/detach render callbacks.

### Runtime facade

The runtime accepts high-level operations, exposes immutable state/results, and owns coordinator + scene + render registry. The shell owns the runtime and initiates asynchronous shutdown.

The runtime exposes `ShutdownAsync()` (or implements `IAsyncDisposable`) as its authoritative lifecycle endpoint. A synchronous `Dispose()` may remain only as an idempotent final release after shutdown has joined all work; it must not initiate fire-and-forget cancellation or dispose an active coordinator.

The shell must not separately call “load,” “add to session,” and “load actors.” Those actions are one runtime transaction.

## Error and Cancellation Rules

- Cancellation is not an error diagnostic unless user-visible context needs an informational status.
- Cancellation never disposes synchronization objects still reachable by an active operation.
- Every candidate is disposed in a `finally` path unless ownership was transferred.
- Proxy realization failure disposes already realized proxies before package disposal.
- A failed replacement preserves the previous scene, generation, camera, and playback-plan validity.
- A successful replacement invalidates old generation-bound playback plans.
- Cleanup continues after individual disposal failures and reports an aggregate diagnostic/log entry.
- Public results distinguish user cancellation, supersession, shutdown rejection, validation failure, and load/build failure.

## Level Editor Non-Regression Rules

Implementation must not:

- share `OpenLevelFile.Package` or Level Editor actor proxies with Interp Preview;
- make Level Editor wait for preview shutdown;
- close Interp Preview when Level Editor closes;
- modify global Level Editor actor registration semantics to compensate for preview misuse;
- alter visible-set behavior, actor ordering, transform editing, package notifications, or save behavior; or
- move Level Editor render work across threads.

Any required shared Scene3D change needs separate justification and Level Editor regression tests. Prefer preview-local adapters/coordinators.

## M4 Integration Contract

M4 may begin only after this design is implemented and validated.

M4 must:

- capture scene generation in every resolved playback plan;
- identify actors by resource key + export UIndex;
- retain lookup provenance and duplicate-tag candidate sets;
- invalidate plans immediately after any scene-generation change;
- never retain a candidate-owned actor or package reference; and
- never resolve against mutable session collections while a transaction can publish.

## Implementation Sequence

1. Introduce lifecycle state, operation/result types, structured resource keys, and generation values.
2. Introduce owned resource and candidate types with exact-once disposal tests.
3. Replace parallel session lists with committed resource ownership plus immutable snapshot publication.
4. Add preview render registry with exact-once delta registration and rollback tests.
5. Split package descriptor loading from dispatcher actor realization.
6. Implement the serialized latest-request operation pump and transactional add/replace/clear commits.
7. Fold Player context into the resource/transaction model.
8. Fold Dialogue Editor and Level Editor entrypoints into high-level task-returning operations.
9. Implement asynchronous cancel-and-join window shutdown.
10. Add lifecycle/concurrency tests and run manual smoke/soak validation.
11. Conduct an adversarial ownership/threading review before M4.

## Required Automated Tests

### Lifecycle and operation ordering

- New request supersedes an in-flight request; only the newest may publish.
- Closing rejects new requests.
- Closing during background load cancels and joins before disposal.
- Closing during dispatcher realization cancels between batches and disposes the candidate.
- Coordinator synchronization primitives remain alive until active work exits.

### Transactions

- Failed replacement preserves the prior scene and generation.
- Canceled/stale replacement preserves the prior scene and generation.
- Successful replacement advances generation exactly once.
- Add duplicate is `NoChange` and does not advance generation.
- Concurrent same-path requests produce one committed resource.
- Batch collection replacement cannot leave a partial collection.

### Ownership

- Partial actor realization failure disposes all realized proxies before the package.
- Uncommitted candidate disposal is exact once.
- Committed resource removal unregisters, disposes proxies, then disposes package exactly once.
- Shutdown releases every committed and candidate resource exactly once.

### Rendering

- Each committed actor is registered once.
- Adding a second level does not re-register actors from the first.
- Player injection does not re-register level actors.
- Replacement rollback restores the prior registration set.
- Render callbacks enumerate one immutable generation snapshot.

### Player context

- Female-to-male and male-to-female changes leave one Player resource.
- Player alias `player` resolves to the selected pawn without mutating the package tag.
- Same variant/game is `NoChange`.
- Changing game removes an incompatible Player context.

### Shared-window behavior

- Overlapping Dialogue Editor and Level Editor requests obey latest-request semantics.
- A stale Dialogue Editor continuation cannot resolve/publish its old node.
- Closing a Level Editor does not close the preview and does not retain the closed editor.
- Preview packages are independent from Level Editor packages.

## Manual Validation

1. Load a heavy level and immediately request another scene; UI remains responsive and only the latest scene appears.
2. Cancel and close during background loading and during proxy realization; no crash or post-close update occurs.
3. Attempt a replacement with an invalid package; the previous scene remains visible and usable.
4. Repeatedly add levels and Player context; actor count, draw registration count, and lookup candidates do not multiply unexpectedly.
5. Toggle Player variant repeatedly; exactly one selected pawn remains.
6. Launch alternately from Dialogue Editor and multiple Level Editor windows; the latest request wins predictably.
7. Close originating editors while preview remains open; preview remains usable and editors are collectible.
8. Run repeated open/load/replace/close cycles and inspect memory/graphics-resource stability.

## Acceptance Criteria for Design Implementation

M3.5 implementation is complete only when:

- all invariants in this document hold;
- all required automated tests pass;
- failed/canceled/stale operations leave the previous valid scene intact;
- close waits asynchronously for cleanup and permits no post-disposal mutation;
- each actor/package has provable exact-once ownership and disposal;
- render registration matches the committed snapshot exactly;
- one Player context is enforced;
- shared-window clients do not own or retain each other; and
- Level Editor baseline behavior remains unchanged under smoke and soak validation.

## Design Decisions Closed by This Document

- Use separate operation IDs and scene generations.
- Use latest-request-wins publication with cooperative cancellation and joined cleanup.
- Use transactional scene replacement rather than clear-before-load.
- Make committed scene resources the authoritative owner of packages and actor proxies.
- Treat render lists and lookup indexes as non-owning projections.
- Keep proxy creation/disposal on the render owner and batch it for responsiveness.
- Use asynchronous cancel-and-join shutdown.
- Model Player as a singleton structured resource, not a synthetic level path.
- Protect Level Editor through independent packages and preview-local coordination.

No unresolved architectural decision blocks the first M3.5 implementation step. Implementation details may refine type names, but any change to these ownership, transaction, lifecycle, or thread-affinity rules must update this document before code is changed.
