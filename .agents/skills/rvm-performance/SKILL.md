---
name: rvm-performance
description: Apply project performance and validation rules when changing runtime code, rendering, import, resource loading, effects, memory, or performance in Raid Into Darkness. Use with the owning subsystem skill; do not use for prose-only changes.
---

# RVM performance

Maximally optimized code implements the required behavior with controlled CPU, GPU, memory and loading cost, preserves correctness, and remains practical to maintain. A shorter frame or smaller loop alone does not establish equivalence.

## Project baseline

Production uses Unity 6000.6.7f1 and HDRP 17.6.0. Inspect the installed implementation and nearby code before choosing APIs. Generic Unity 6.3 guidance is supporting material, not proof about this version. Follow AGENTS.md for canonical paths, scene safety, GameConfig ownership, tests and Archive cleanup. Require explicit authorization for isolated testing and synchronization.

## Reduce work first

- Remove unnecessary work and reduce the number of objects processed before optimizing individual operations. Consider one object, a whole location, and multiple cameras.
- Do not scan the whole world every frame or camera callback when spatial selection, events or a valid cache can solve the task. Include index traversal, updates and synchronization in the cost comparison.
- Give every cache explicit invalidation, a bounded memory policy and release behavior. Track mutable inputs and root transforms. Never assume an exposed array is immutable.
- In mass per-frame work avoid repeated component/object searches, temporary collections, LINQ, boxing, string formatting and managed allocations. Reuse bounded buffers and pools; define overflow behavior.
- Use Jobs, Burst, unsafe or complex data structures only with evidence that preparation and synchronization costs are justified. Do not add packages without authorization.

## Rendering correctness

- Preserve conservative bounds, shadows, reflections, multiple cameras, LOD, current/previous matrices and motion vectors. Include wind, transformed roots, large objects crossing cells, moving instances and reappearance after invisibility.
- Camera image candidates and shadow candidates are different sets. An offscreen object can cast a visible shadow. Do not use one camera's visibility to globally toggle Renderer.enabled.
- Preserve matrix history timing unless equivalence is demonstrated. Keep payload, material resolution, ordering and existing submission partitioning unless the task specifically changes them.
- Treat image quality reductions as separate behavior changes. Disabled lights, shadows, LOD experiments or lower resolution are not equivalent optimizations.
- Do not duplicate Unity culling for ordinary MeshRenderers without evidence. Custom instanced submissions need their own supported integration; baked occlusion or GPU Resident Drawer settings do not automatically cover Graphics.RenderMeshInstanced.

## Import and configuration

- Every accepted data-dependent optimization needs one idempotent preparation/update path for new imports, reimports, drag-and-drop, resources-only and targeted updates, and existing content.
- Version derived data, fingerprint all relevant inputs/settings and invalidate dependents. Write completion only after successful preparation. Missing/stale data must use a correct fallback and a defined repair route.
- Check importer early exits. Updating an index must not require rebuilding all geometry or recreating gameplay entities. Guard AssetPostprocessor recursion. An unchanged second pass must avoid duplicate assets and file changes.
- Preserve GUIDs, .meta, source identity, transforms, references, user objects and prefab overrides. Use stable location/source identity, never process-local IDs or traversal order as persistent IDs.
- Reuse GameConfig and referenced domain configuration for shared runtime rules. Location overrides require explicit opt-in. Keep imported facts and implementation constants with their owner, importer settings in Editor tools, and mutable state outside shared assets.

## World and networking boundaries

- Separate local presentation/resources, required simulation regions and client network relevance. Visibility must not disable colliders, triggers, AI or simulation, spawn/despawn entities, change authority or observers.
- Host simulation survives outside its camera. Dedicated server must not require Camera.main or visual assets to maintain gameplay. Support independent views; do not install Mirror solely to express these boundaries.
- Distinguish grouping, CPU preparation/submissions, visibility rejection and actual resource streaming. Resources held by references remain resident.
- Before memory streaming, establish resource ownership/release, conservative prefetch, separate enter/exit distances (hysteresis) and per-frame loading/memory budgets. Implement only the boundaries needed now, not an unused framework.

## Evidence and diagnostics

- Validate the specific correctness risks and use a suitable Player. Separate elapsed frame time/FPS, CPU work and GPU work, averages and rare stalls. Report coverage and unresolved uncertainty.
- For this campaign keep the established 8-second warmup, four 5-second positions and explicit Full/Stable boundary rules. Start with A1, B1, B2, A2 under matching light/shadow/quality/resolution/route settings. Preserve rejected attempts with reasons; do not repeat until a desired result appears.
- Close all Unity Editors and related load before Player measurements, preserving dirty scenes. Record foreground and background load. Keep monitoring/cooling behavior safe and restore any task changes.
- Collect detailed counters separately if they materially affect timing. Measure or bound instrumentation overhead; a disabled flag alone does not prove zero cost. Report index memory, candidates, submissions, P95/P99/max and long intervals alongside averages.
- Run the minimum sufficient checks. Do not reopen historical spike investigations or collect broad traces without a concrete need.
- Benchmark is an optional, removable consumer of game APIs. Dependencies point benchmark to game. Production optimizations, ordinary import/build and functional HDRP fixes work with the module physically absent.
- Keep forced measurement conditions, experimental modes, startup hooks, build hooks, package diagnostics, generators and generated benchmark data inside the module. Avoid diagnostic hooks/stubs and benchmark conditionals scattered through gameplay code.
- Maintain a removal inventory and verify absence by compiling Runtime/Editor and building/running an ordinary Release Player. Restore the module while the campaign needs it. Final removal clears current assets, references, registrations and delivered Player, while preserving Git history and Archive evidence.
