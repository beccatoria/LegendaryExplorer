# Interp Preview M3.5 Step 4 — Thread-Ownership Split Design

## Status

- **Design state:** Complete; ready for Codex implementation
- **Scope:** M3.5 step 4 only, on `becca-LEX`
- **Production code changed by this design:** None
- **Governing design:** [InterpPreview_M3_5_LifecycleDesign.md](./InterpPreview_M3_5_LifecycleDesign.md)
- **Deferred slices:** Exact-once render registry and Player singleton replacement remain later M3.5 work.

## Purpose

Separate package preparation that is safe on a worker thread from actor realization, scene mutation, rendering, diagnostics, and WPF work that must remain on the Interp Preview dispatcher.

The current `InterpPreviewLevelLoader.LoadLevel` mixes all of these responsibilities in one synchronous call. Because it is reached after dispatcher-capturing awaits, package I/O and parsing block the UI; moving that whole call to `Task.Run` would instead move graphics-backed `ActorProxy` construction and preview render-cache access onto an invalid thread.

Step 4 introduces an explicit two-phase pipeline with a one-way ownership handoff:

1. **Worker preparation:** open an independent package and extract immutable plain-data build descriptors while the package is exclusively worker-owned.
2. **Dispatcher realization:** take exclusive ownership of that package, realize proxies in bounded batches, resolve proxy relationships, and submit the complete candidate to the existing transactional commit boundary.

## Non-Negotiable Invariants

1. A package is never accessed concurrently by worker and dispatcher code.
2. Worker preparation opens a new package with `forceLoadFromDisk: true`; it never borrows a globally shared or Level Editor package.
3. Worker output contains no `ActorProxy`, component proxy, `ExportEntry`, `IEntry`, `ObjectBinary`, `PackageCache`, render context, graphics object, WPF object, or callback into the shell.
4. Actor and component proxy creation occurs only through the preview dispatcher.
5. Preview `PackageCache`, texture/cache access, model creation, proxy attachment resolution, session mutation, render mutation, diagnostics events, status, and disposal after realization begins are dispatcher-owned.
6. A prepared package changes thread ownership exactly once at a quiescent handoff. After handoff, worker code cannot read or dispose it.
7. Candidate proxies are never published incrementally. A canceled, failed, stale, or closing-time realization disposes all realized proxies and the package without changing the committed scene.
8. Commit remains synchronous on the dispatcher, with no `await` inside its latest-operation check and scene mutation critical section.
9. Shutdown invalidates/cancels work, awaits worker completion and dispatcher candidate cleanup, and only then disposes committed scene/render/coordinator resources.
10. Correctness does not depend on ambient `SynchronizationContext` capture or `ConfigureAwait(true)`; dispatcher affinity is explicit and testable.

## Current Pipeline Classification

### Worker-safe when operation-owned

The following may run on a worker only while using resources owned exclusively by that operation:

- canonicalize and validate the requested path;
- check file existence and basic request values;
- open an independent source package from disk;
- locate and parse the package's `Level` export;
- enumerate actor UIndexes from `Level.Actors`;
- parse `StaticMeshCollectionActor` and `StaticLightCollectionActor` binaries;
- copy collection component UIndexes, collection index, local-to-world matrix, and decomposed transform values into immutable descriptors;
- collect plain identity/class metadata needed to select the dispatcher factory;
- sort descriptors deterministically; and
- perform cancellation checks between package/level/collection parsing stages.

These operations are not globally guaranteed thread-safe. They are safe here because the operation has an independent package instance and no other thread may access it before handoff.

### Dispatcher-only

The preview dispatcher exclusively owns:

- resolving descriptor UIndexes back to entries after handoff;
- `ActorProxy.CanCreate` and `ActorProxy.Create`;
- `StaticMeshComponentActorProxy` and `StaticLightComponentActorProxy` construction;
- all primitive/component proxy construction;
- `IActorEditorContext.RenderContext` and its `PackageCache`;
- texture, mesh, model, shader, hit-proxy, and graphics resources;
- proxy attachment resolution and any proxy graph mutation;
- candidate proxy disposal once realization has started;
- session duplicate revalidation and transactional mutation;
- render registration/unregistration;
- diagnostics/event publication and all WPF state;
- lifecycle transitions and scene-viewer disposal.

## Explicit Dispatcher Contract

Do not rely on a caller happening to start on WPF or on `ConfigureAwait(true)` restoring the desired context.

Introduce a preview-local abstraction, conceptually:

- `IInterpPreviewDispatcher`
  - `bool CheckAccess()`
  - `Task InvokeAsync(Action action, CancellationToken token = default)`
  - `Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token = default)`
  - `Task YieldAsync(CancellationToken token = default)`
  - `void VerifyAccess()`

The WPF implementation wraps the `InterpPreviewShellWindow.Dispatcher`. Tests use a deterministic fake that records affinity and controls queued work.

Required use:

- lifecycle acceptance and operation IDs may remain thread-safe runtime state;
- every realization batch is invoked/continued through this dispatcher;
- attachment resolution, commit, session/render mutation, operation diagnostics, and disposal of a realizing/realized candidate call `VerifyAccess()`;
- shutdown awaits accepted work without blocking the dispatcher, then performs final dispatcher-owned teardown through this abstraction.

## Worker Preparation Contract

### Prepared resource

Introduce a one-shot disposable candidate, conceptually `InterpPreviewPreparedResource`, containing:

- canonical full source path;
- structured resource kind and Player variant fields (for future-compatible reuse);
- game captured from the independent package;
- the exclusively owned `IMEPackage`;
- immutable ordered actor build descriptors; and
- atomic phase/ownership state.

It must not expose the package publicly to arbitrary callers. Ownership transfer occurs through one internal `TryBeginRealization(...)` method that atomically changes state and returns the package/descriptors to the dispatcher realizer.

### Descriptor shape

Use immutable records/value objects containing plain data only.

`InterpPreviewActorBuildDescriptor` should contain:

- actor form: ordinary actor, static-mesh collection component, or static-light collection component;
- actor/component export UIndex;
- source actor/collection export UIndex where applicable;
- collection component index where applicable;
- copied `Matrix4x4` local-to-world value where applicable;
- copied decomposed location, scale, and rotation values where required by current proxy behavior; and
- deterministic source order/sort key.

Class name may be copied for diagnostics and preflight only. The dispatcher must revalidate the UIndex and actual export class before constructing a proxy.

Descriptors must not retain parsed `Level`, `StaticCollectionActor`, property collections, package entries, or package-backed binary objects.

### Independent package rule

Use:

`MEPackageHandler.OpenMEPackage(fullPath, forceLoadFromDisk: true)`

This is required even if global package sharing is enabled. Interp Preview remains independent from Level Editor and other tools.

### Worker failure and cancellation

Before handoff:

- the prepared resource owns only its package and plain descriptors;
- cancellation or failure disposes the package on the worker;
- descriptors require no disposal;
- no dispatcher cleanup is needed because no proxy exists;
- disposal is idempotent and the package is released exactly once.

The worker must never dispose a package after successful handoff.

## Descriptor-Aware Collection Proxy Construction

Current collection component proxy constructors require a live `StaticCollectionActor` and read its transform by index. That object is package-backed and cannot be part of the detached descriptor contract.

Implementation must add a compatible constructor/factory seam for preview realization. Preferred direction:

- add internal constructor overloads to `CollectionActorComponentProxy`, `StaticMeshComponentActorProxy`, and `StaticLightComponentActorProxy` accepting:
  - collection export;
  - component export;
  - copied local-to-world matrix;
  - copied location, scale, and rotation; and
- keep existing Level Editor constructors unchanged by delegating them to the new overload after extracting the same values from `StaticCollectionActor`.

This preserves Level Editor behavior while allowing Interp Preview to consume detached collection descriptors. A preview-only factory may wrap these overloads, but it must not duplicate component-proxy creation logic.

## Ownership State Machine

The operation-owned resource has these implementation states:

### `Preparing`

- worker owns package and mutable descriptor builder;
- worker may read package;
- no proxy exists;
- worker disposes on failure/cancel.

### `Prepared`

- worker owns sealed package + immutable descriptor array;
- package is quiescent;
- no code accesses it while handoff is pending.

### `Realizing`

- ownership was atomically transferred to the preview dispatcher;
- worker no longer holds usable package access;
- dispatcher owns package and all proxies created so far;
- cancellation/failure/stale/closing cleanup is dispatcher-only.

### `Loaded`

- all proxies exist, attachments are resolved, and candidate validation passed;
- dispatcher owns package/proxies until transactional commit transfers them.

### `Committed`

- `InterpPreviewSession`/`InterpPreviewOwnedResource` owns package and proxies;
- existing exact-once committed disposal rules apply.

### `Disposed`

- terminal and idempotent;
- package and any proxies were disposed by the phase owner exactly once.

Only these transitions are allowed:

- `Preparing -> Prepared -> Realizing -> Loaded -> Committed`
- `Preparing|Prepared -> Disposed` on the worker
- `Realizing|Loaded -> Disposed` on the dispatcher

No reverse transition and no second ownership transfer is allowed.

## Dispatcher Realization Pipeline

1. Receive the prepared resource on the explicit preview dispatcher.
2. Re-check lifecycle, latest operation ID, and cancellation before ownership transfer.
3. Atomically call `TryBeginRealization`; after this point the dispatcher owns cleanup.
4. Resolve descriptors to exports by UIndex against the handed-off package.
5. Revalidate expected descriptor form/class; fail rather than silently constructing a mismatched resource.
6. Realize proxies in bounded batches.
7. Between batches:
   - re-check lifecycle/latest/cancellation;
   - yield through `IInterpPreviewDispatcher.YieldAsync` at a low/background dispatcher priority; and
   - keep every proxy candidate-owned and unpublished.
8. After all proxies exist, resolve attachments as one dispatcher-owned pass.
9. Sort proxies deterministically and create `InterpPreviewLoadedLevel` (or its successor candidate type).
10. Re-check lifecycle/latest/cancellation at the commit boundary.
11. Commit synchronously with no `await`; transfer ownership to the session only on success.
12. On any non-commit path, dispose candidate proxies before package on the dispatcher.

The initial batch size should be an internal constant with a test override. Correctness tests must run with batch sizes of one and greater than the descriptor count.

## Transaction and Publication Rules

Step 4 does not redesign the later render registry, but its output must be compatible with it.

- No session collection, lookup index, render list, status, or diagnostic is updated during preparation or realization.
- Duplicate/no-change assumptions are revalidated immediately before commit.
- Failed/canceled/stale realization leaves the prior scene untouched.
- The commit result exposes only the committed actor delta, never a package-owning prepared resource.
- A later render-registry slice may move actor registration inside the transaction without changing this preparation/realization ownership model.
- Player preparation may reuse this pipeline later, but step 4 does not implement singleton replacement policy.

## Cancellation and Supersession

### During worker preparation

- request cancellation is observed between expensive parse/enumeration stages;
- the worker disposes its package;
- no dispatcher candidate exists;
- result is `Cancelled`, `Superseded`, or `RejectedClosing` according to runtime state.

### While queued for dispatcher

- lifecycle/latest/cancellation is checked before handoff;
- stale work disposes the still-worker-owned prepared resource without realizing proxies;
- no dispatcher mutation occurs.

### During realization

- checks occur before each batch, after each yield, before attachment resolution, and before commit;
- dispatcher disposes all realized proxies, then the package;
- no partial scene or render publication occurs.

### At commit

- latest/lifecycle validation and session mutation remain one synchronous dispatcher critical section;
- accepting a newer request cannot interleave between validation and publication;
- successful commit owns the package/proxies even if a newer request is accepted immediately afterward.

## Shutdown Interaction

`ShutdownAsync` must cover the complete two-phase pipeline.

1. Transition to `Closing`, invalidate latest operation ID, and cancel active preparation/realization.
2. Reject new requests and immediately dispose any caller-provided unaccepted candidate according to its current phase.
3. Await worker preparation completion or cancellation.
4. Await queued/in-progress dispatcher realization and candidate cleanup.
5. Confirm no operation-owned prepared/realizing resource remains.
6. Detach rendering and retire/dispose committed resources on the dispatcher.
7. Dispose coordinator gates, dispatcher adapter subscriptions, scene viewer, and render resources.
8. Transition to `Disposed`.

No synchronous dispatcher wait is permitted. Synchronization primitives remain alive until all accepted operations have exited.

## Contract Direction for Implementation

Names may be refined, but responsibilities must remain distinct.

### `IInterpPreviewPackagePreparer`

- `Task<InterpPreviewPrepareResult> PrepareAsync(InterpPreviewPrepareRequest request, CancellationToken token)`
- implementation explicitly uses worker execution;
- owns path validation, independent package open, Level/collection parse, descriptor creation, and pre-handoff cleanup;
- does not accept `IActorEditorContext`.

### `IInterpPreviewActorRealizer`

- `Task<InterpPreviewRealizeResult> RealizeAsync(InterpPreviewPreparedResource prepared, IActorEditorContext context, OperationGuard guard, CancellationToken token)`
- requires `IInterpPreviewDispatcher` access;
- owns handoff, batches, proxy factory, attachments, and candidate cleanup;
- returns a fully realized candidate or an explicit non-owning outcome.

### `IInterpPreviewActorProxyFactory`

- resolves/revalidates descriptor UIndexes on dispatcher;
- creates ordinary or collection component proxies;
- contains no operation ordering, session, or render publication logic.

### `IInterpPreviewDispatcher`

- is injected into runtime/realizer;
- provides explicit access checks, invocation, and yielding;
- enables deterministic thread-affinity tests.

### Runtime/coordinator

- remains the owner of operation IDs, latest-request cancellation, serialization, lifecycle, and transactional commit;
- calls prepare off-dispatcher and realize/commit on dispatcher;
- guarantees prepared/candidate disposal on every non-commit outcome.

## Migration Sequence for Codex

1. Add immutable descriptor, prepare request/result, and prepared-resource state types with exact-once transition tests.
2. Add the explicit dispatcher abstraction and WPF/test implementations.
3. Split current `InterpPreviewLevelLoader` into package preparer and actor realizer without changing shell behavior.
4. Add descriptor-aware collection proxy constructor/factory seams while preserving existing Level Editor constructors.
5. Update the runtime operation pipeline to await worker preparation, then dispatcher realization, then existing transactional commit.
6. Remove `IActorEditorContext` from the worker preparation contract.
7. Route candidate cleanup by phase/thread and include both phases in `ShutdownAsync` draining.
8. Retire or narrow the old `IInterpPreviewLevelLoader`/load-coordinator API after all callers and tests move to the split pipeline.
9. Run focused automated tests and the human smoke checklist before advancing to render registry/Player singleton work.

## Required Automated Tests

### Thread affinity

- package preparation runs with no dispatcher access;
- actor and component proxy factory calls run only when dispatcher `CheckAccess()` is true;
- attachment resolution, session mutation, diagnostics publication, and candidate proxy disposal are dispatcher-only;
- tests fail if ambient synchronization context rather than the injected dispatcher is used.

### Descriptor boundary

- descriptors contain no package-backed or render/WPF reference types;
- ordinary actor ordering is deterministic by UIndex;
- static mesh/light collection descriptors preserve component UIndex, collection UIndex/index, and copied transforms;
- dispatcher revalidation rejects missing or class-mismatched UIndexes.

### Ownership and handoff

- worker failure/cancellation disposes package once and creates no proxy;
- stale work before handoff disposes package once without dispatcher realization;
- handoff is one-shot; worker disposal after handoff is a no-op or rejected;
- realization failure disposes all created proxies before package on dispatcher;
- successful commit transfers disposal to exactly one committed resource.

### Ordering and shutdown

- only one operation pipeline realizes/commits at a time;
- newer request during preparation prevents older realization/commit;
- newer request between realization batches causes dispatcher cleanup and no publication;
- failed/canceled/stale replace preserves the previous scene;
- close during preparation joins worker cleanup before coordinator disposal;
- close during realization joins dispatcher cleanup before session/render disposal;
- synchronization gates remain usable until every accepted operation exits.

### Responsiveness

- batch size one yields between descriptors and honors cancellation;
- large batch/no-yield test produces the same actors/order as batch size one;
- no actor is visible in session/render state before complete commit.

## Human Validation After Implementation

1. Start a heavy level load; verify the window continues repainting and can be moved while package preparation runs.
2. During proxy realization, interact with the viewport and issue a second load; only the latest scene commits.
3. Close during package preparation and during visible realization progress; close completes without errors or late updates.
4. Replace with an invalid package; prior scene remains visible and usable.
5. Load collection-heavy levels; static mesh/light collection placement matches pre-split behavior.
6. Repeat open/load/replace/close cycles and confirm no duplicated actors, hit proxies, or retained package handles.

## Exit Criteria

Step 4 implementation is complete when:

- package preparation is demonstrably off-dispatcher;
- actor realization and all render/session/UI mutation are explicitly dispatcher-owned;
- no package is accessed concurrently or borrowed from global/Level Editor ownership;
- descriptors are immutable plain data with no package-backed object references;
- cancellation and shutdown clean the phase-owned resources on the correct thread;
- stale/failure/cancel paths cannot publish partial scene state;
- collection actor transforms match baseline behavior;
- focused automated tests and the human checklist pass; and
- no broad redesign of Level Editor rendering is required.

## Sol Design Review Resolution

The bounded design review identified and this document resolves three blockers:

1. **Collection constructors required live binary objects.** Resolution: add descriptor-aware constructor/factory overloads using copied transforms while preserving existing Level Editor constructors.
2. **Package disposal affinity was underspecified.** Resolution: formal `Preparing/Prepared/Realizing/Loaded/Committed/Disposed` states assign disposal to the worker before handoff and the dispatcher after handoff.
3. **Dispatcher affinity relied on continuation capture.** Resolution: inject and enforce `IInterpPreviewDispatcher`; do not rely on `ConfigureAwait(true)` or ambient `SynchronizationContext`.

The design gate is now **go for Codex implementation**. No second broad design review is required unless implementation reveals a contradiction with these invariants.
