---
name: game-architecture
description: "Apply the project's mandatory architecture to gameplay, services, runtime fixes, inventory, equipment, persistence, GameConfig, and changes to ownership or composition."
---

# Game architecture

This skill is REQUIRED before designing or changing gameplay, runtime application behavior, application-level services, authoritative state, persistence, shared runtime configuration, or their composition. The requirements apply to the changed responsibilities, including small fixes. [AGENTS.md](../../../AGENTS.md) controls scope, mandatory routing, and conflict resolution.

## Locate the existing owner and contracts

- You MUST start with the named files or the smallest matching feature folder and expand only across a dependency needed for the task. Before introducing a pattern, you MUST inspect the existing state owner, commands, queries, configuration, payloads, nearby code, callers, and DI composition.
- You MUST preserve public APIs, existing behavior, serialization, scripts' Unity GUIDs, save/network contracts and assembly boundaries unless the task requires a change. Where a feature or mandatory boundary requires a structural change, you MUST keep it focused. You MUST report relevant legacy limitations outside the task. Explicit authorization is REQUIRED for a project-wide migration or rewrite.

## Design rules

- **SRP.** Each type MUST have a cohesive responsibility. You MUST keep unrelated simulation, presentation, persistence, and orchestration separate and keep lifecycle coordination with the responsible owner.
- **OCP.** You MUST preserve stable behavior and use composition and extension points for actual requirements.
- **LSP.** Implementations MUST preserve contract behavior, invariants, and lifecycle guarantees.
- **ISP.** Consumer contracts MUST be focused.
- **DIP.** Dependencies MUST be explicit. You MUST use abstractions at meaningful application, infrastructure, presentation, DI, substitution, or testing boundaries.
- **DRY.** Shared rules, formulas, transitions, and configuration values MUST have one implementation. Similar code with different semantics may stay separate.
- **KISS and YAGNI.** You MUST make the smallest correct, maintainable, performant change and add abstractions, helpers, factories, wrappers, layers, and extension points only for a concrete need. Dedicated files are REQUIRED under One Type per C# File. You MUST keep cohesive behavior intact and enforce ownership, DI, and UI rules.

## State and application boundaries

- Simulation MUST own rules, calculations, validation, and transitions independently of UI, cameras, renderers, audio, and VFX. You MUST use plain C# or data-oriented code where Unity APIs are unnecessary.
- You MUST identify exactly one authority for each mutable gameplay fact at a time and send writes through its operation, command, or controlled simulation path. You MUST keep runtime data separate from read-only configuration, save snapshots, presentation state, and UI-local state, and expose explicit read-only queries, notifications, or snapshots.
- You MUST use this flow: input/command → simulation/owner → committed state → notification/snapshot → visualization. Visualization MUST consume committed state and own visual behavior and state. Unity adapters MAY forward engine, physics, or input signals. Culling, disabling visuals, closing UI, and streaming out presentation MUST NOT implicitly affect unrelated simulation. The simulation/lifecycle owner MUST own intentional activation, sleep, and scheduling.
- For save DTOs, caches, reactive projections, spatial indices, render/native buffers, and other derived snapshots, you MUST define the owner, direction of synchronization, update/invalidation, and lifetime. Views, ViewModels, visuals, and downstream services MUST NOT become competing writable sources. You MUST keep explicit command/state boundaries for future networking and MUST NOT add networking the task does not require.
- In inventory and equipment, you MUST reuse the established owner of membership, quantity, assignment, durability, ammo, and other mutable item facts. You MUST keep item data, static item configs, inventory logic, and equipment logic separate. You SHOULD prefer ScriptableObjects for static item data and configs. Item configs MUST NOT link implicitly to scene objects. You MUST NOT duplicate writes or rules or change ownership contracts without a task-specific reason.
- Persistence MUST be explicit and isolated. You MUST read and restore through the owner contract or controlled initialization. Save DTOs are snapshots. You MUST change the save schema only when explicitly required. If it changes, you MUST preserve backward compatibility where practical, explain migration risks, and list manual validation steps.

## DI and Unity adapters

- You MUST use the existing container in `Assets/_Source/Scripts/DI` for new application-level services, including gameplay orchestration, inventory, equipment, persistence, session/config/resource management, and shared services. You MUST reuse the composition root and registration patterns. You SHOULD prefer constructor injection for plain C# and the existing injection/initialization path for Unity objects.
- Services MUST have an explicit owner and a lifetime suited to their scope. You MUST dispose owned resources and resolve dependencies at composition boundaries. You MUST pass value objects, DTOs, pure/local helpers, short-lived domain objects, and low-level implementation objects explicitly. They need no DI registration.
- You MUST NOT introduce or extend service locators, static services for mutable gameplay state, global singletons, scene searches, or ad hoc construction in MonoBehaviours, Views, or ViewModels to bypass DI. Explicit authorization is REQUIRED to refactor, replace, duplicate, or add a container/framework.
- MonoBehaviours MUST focus on Unity lifecycle, serialized Unity/scene references, and visuals. They MUST NOT accumulate unrelated systems or use hidden scene dependencies. UI MonoBehaviours MUST NOT own persistence, inventory, equipment, or business rules. An adapter MAY coordinate an engine-dependent system without owning gameplay facts in the presentation. You SHOULD prefer explicit Unity null checks and actionable errors. You MUST follow nearby conventions for `Debug.LogError`, `Debug.LogWarning`, or exceptions and avoid silent failures and excessive or hot-path logging.

## GameConfig

Exactly one root `GameConfig` ScriptableObject MUST serve as the shared runtime configuration entry point. The root and referenced assets MUST be read-only at runtime.

1. You MUST inspect nearby code and existing assets, decide what belongs globally and what belongs with another owner, and reuse an existing field that represents the same value.
2. You MUST put shared defaults and cross-location/system rules in `GameConfig` or a referenced domain config and keep subsystem settings with their owner. Where appropriate, you MUST use a dedicated ScriptableObject for complex settings and expose it through the established config entry point. Scene components and prefabs MUST inherit shared values by default.
3. You MUST add location overrides only for values that genuinely vary by location. Runtime time, weather, effects, player state, and timers MUST stay with their runtime owners. Progress and persisted runtime state MUST use saves. Source paths, importer options, diagnostics flags, and Editor preferences MUST stay with their tools.
4. Source-derived constants, formulas, imported data, and internal implementation details MUST stay with their owners unless a game-design requirement makes them configurable. You MUST preserve GUIDs, references, intentional user-authored overrides, and existing behavior.
5. You MUST document and apply precedence from lowest to highest: global defaults → explicitly enabled location overrides → runtime/session overrides → applicable restored save state. A legacy serialized field alone MUST NOT opt in. Overrides MUST remain separate from shared assets, and saved values MUST restore only applicable runtime state. You MUST use the existing DI and reference patterns.
6. You MUST classify and migrate within task scope. For a dedicated configuration architecture task, you MUST audit the project and list migration candidates before editing. For a change to runtime configuration resolution or integration, you MUST verify shared references, default inheritance, override precedence, scene reload, Play Mode, Player builds, and compatibility with existing scenes under the Production validation policy. You MUST verify that a global value change reaches all inheriting consumers without reimporting locations and use a safe path under the Production safety policy.

## Low-level boundaries

- For Burst/Jobs, native containers, BRG/culling, shaders, rendering, import pipelines and measured hot paths, specialization is permitted only when an application pattern conflicts with a concrete execution/API constraint or incurs measured overhead. Where justified, you MAY use structs, contiguous buffers, explicit ownership, batching, static pure helpers, direct access, explicit loops, fused operations, specialized algorithms, fixed-step simulation, and frame-driven rendering.
- You MUST keep managed reactive/DI work out of inner loops and MUST NOT resolve services per entity, job, frame, or kernel. DI remains REQUIRED for owning application services. You MUST define ownership and synchronization for data duplicated for locality and preserve dependency direction, correctness, required determinism, thread safety, `JobHandle` dependencies, native memory and resource lifetimes, and separation of simulation and visualization.
- You MUST keep exceptions local under [rvm-performance](../rvm-performance/SKILL.md), base them on concrete constraints or profiler/benchmark evidence, and measure claimed gains and regressions. You MUST report scope, justification, evidence and validation limits and keep diagnostics in Archive. Exceptions MUST NOT waive the type-file rule, dependency restrictions, DI at application boundaries, SSOT, restrictions on UI polling, or the task scope. DRY/KISS/YAGNI remain REQUIRED except for justified local trade-offs.

## Verify

You MUST trace the affected commands, state, notifications, and restore path to the current owner and check registrations, lifetime and disposal, compatibility, and the absence of competing state or bypasses. For checks in Unity, you MUST follow the validation skill routing in the root AGENTS.md.
