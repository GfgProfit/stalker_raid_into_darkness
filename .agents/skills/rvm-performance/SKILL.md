---
name: rvm-performance
description: Apply project performance and validation rules when changing runtime code, rendering, import, resource loading, effects, memory, or performance in Raid Into Darkness. Use with the owning subsystem skill; do not use for prose-only changes.
---

# RVM performance

Optimized code MUST implement the required behavior with controlled CPU, GPU, memory and loading cost, preserve correctness, and remain practical to maintain. A shorter frame or smaller loop alone does not establish equivalence.

## Project baseline

Production uses Unity 6000.6.7f1 and HDRP 17.6.0. You MUST inspect the installed implementation and nearby code before choosing APIs. Generic Unity 6.3 guidance is supporting material and cannot verify behavior in the installed version. You MUST follow AGENTS.md for canonical paths, scene safety, GameConfig ownership, tests and Archive cleanup. Explicit authorization is REQUIRED for isolated testing and synchronization.

## Reduce work first

- You MUST remove unnecessary work and reduce the number of objects processed before optimizing individual operations. You MUST consider one object, a whole location, and multiple cameras.
- You MUST NOT scan the whole world every frame or camera callback when spatial selection, events or a valid cache can solve the task. The cost comparison MUST include index traversal, updates and synchronization.
- Every cache MUST have explicit invalidation, a bounded memory policy and release behavior. You MUST track mutable inputs and root transforms. You MUST NOT assume an exposed array is immutable.
- In mass per-frame work, you MUST avoid repeated component/object searches, temporary collections, LINQ, boxing, string formatting and managed allocations. You MUST reuse bounded buffers and pools and define overflow behavior.
- You MUST use Jobs, Burst, unsafe or complex data structures only with evidence that preparation and synchronization costs are justified. Authorization is REQUIRED to add packages.

## Rendering correctness

- You MUST preserve conservative bounds, shadows, reflections, multiple cameras, LOD, current/previous matrices and motion vectors. You MUST account for wind, transformed roots, large objects crossing cells, moving instances and reappearance after invisibility.
- Camera image candidates and shadow candidates are different sets. An offscreen object can cast a visible shadow. You MUST NOT use one camera's visibility to globally toggle Renderer.enabled.
- You MUST preserve matrix history timing unless equivalence is demonstrated. You MUST keep payload, material resolution, ordering and existing submission partitioning unless the task specifically changes them.
- You MUST treat image quality reductions as separate behavior changes. Disabled lights, shadows, LOD experiments or lower resolution are not equivalent optimizations.
- You MUST NOT duplicate Unity culling for ordinary MeshRenderers without evidence. Custom instanced submissions MUST use their own supported integration. Baked occlusion or GPU Resident Drawer settings do not automatically cover Graphics.RenderMeshInstanced.

## Import and configuration

- Every accepted data-dependent optimization MUST have one idempotent preparation/update path for new imports, reimports, drag-and-drop, resources-only and targeted updates, and existing content.
- You MUST version derived data, fingerprint all relevant inputs/settings and invalidate dependents. You MUST write completion only after successful preparation. Missing/stale data MUST use a correct fallback and a defined repair route.
- You MUST check importer early exits and guard AssetPostprocessor recursion. Updating an index MUST NOT require rebuilding all geometry or recreating gameplay entities. An unchanged second pass MUST avoid duplicate assets and file changes.
- You MUST preserve GUIDs, .meta, source identity, transforms, references, user objects and prefab overrides. You MUST use stable location/source identity and MUST NOT use process-local IDs or traversal order as persistent IDs.
- You MUST reuse GameConfig and referenced domain configuration for shared runtime rules. Explicit opt-in is REQUIRED for location overrides. You MUST keep imported facts and implementation constants with their owner, importer settings in Editor tools, and mutable state outside shared assets.

## World and networking boundaries

- You MUST separate local presentation/resources, required simulation regions and client network relevance. Visibility MUST NOT disable colliders, triggers, AI or simulation, spawn/despawn entities, or change authority or observers.
- Host simulation MUST remain active outside its camera's view. A dedicated server MUST NOT require Camera.main or visual assets to maintain gameplay. You MUST support independent views and MUST NOT install Mirror solely to express these boundaries.
- You MUST distinguish grouping, CPU preparation/submissions, visibility rejection and actual resource streaming. Resources held by references remain resident.
- Before memory streaming, you MUST establish resource ownership/release, conservative prefetch, separate enter/exit distances (hysteresis) and per-frame loading/memory budgets. You MUST implement only the boundaries currently needed.

## Evidence and diagnostics

- You MUST validate the specific correctness risks and use a suitable Player. You MUST distinguish elapsed frame time/FPS, CPU work and GPU work, and distinguish averages from rare stalls. You MUST report coverage and unresolved uncertainty.
- For the established comparison campaign, you MUST keep the 8-second warmup, four 5-second positions and explicit Full/Stable boundary rules. You MUST start with A1, B1, B2, A2 under matching light/shadow/quality/resolution/route settings and preserve rejected attempts with reasons. Repeating runs to obtain a desired result is FORBIDDEN.
- For the isolated-load baseline for Player measurements, all Unity Editors MUST be closed and related load MUST stop. You MUST preserve dirty scenes. Explicit authorization is REQUIRED before closing user-owned sessions or stopping user-owned load. If that baseline cannot be obtained safely, you MUST report the coverage limit. You MUST record foreground and background load, keep monitoring/cooling behavior safe, and restore any task changes.
- You MUST collect detailed counters separately if they materially affect timing. You MUST measure or bound instrumentation overhead. A disabled flag alone does not prove zero cost. You MUST report index memory, candidates, submissions, P95/P99/max and long intervals alongside averages.
- You MUST run the minimum sufficient checks. You MUST NOT reopen historical spike investigations or collect broad traces without a concrete need.
- The benchmark module MUST remain an optional, removable consumer of game APIs. Dependencies MUST point from benchmark to game. Production optimizations, ordinary import/build and functional HDRP fixes MUST work with the module physically absent.
- You MUST keep forced measurement conditions, experimental modes, startup hooks, build hooks, package diagnostics, generators and generated benchmark data inside the module. You MUST avoid diagnostic hooks/stubs and benchmark conditionals scattered through gameplay code.
- You MUST maintain a removal inventory and verify absence by compiling Runtime/Editor and building/running an ordinary Release Player. You MUST restore the module while the campaign needs it. Final removal MUST clear current assets, references, registrations and delivered Player while preserving Git history and Archive evidence.
