# AGENTS.md

## Project Context

Raid Into Darkness uses Unity 6000.6.7f1, C#, HDRP 17.6.0, TextMeshPro, R3 and the project DI container at `Assets/_Source/Scripts/DI`. Codex is the supported coding agent.

- Production and authoritative code/assets live at `E:\Projects\stalker_raid_into_darkness`.
- Archive lives at `E:\Projects\stalker_raid_into_darkness_Archive`.
- See [Project Folder Layout](#project-folder-layout) for code and asset placement.

## Instruction Priority

1. Platform, system, developer and tool constraints.
2. Explicit user instructions for the task within those constraints.
3. This file and applicable scoped project instructions, including the mandatory Skills routed below.
4. Compliant existing project patterns.
5. General practices and external examples.

`AGENTS.md` governs the project. Scoped instructions add requirements. Guides, examples, legacy violations and routine requests MUST NOT waive project rules. Agents MUST apply compatible rules together and resolve conflicts by priority and scope. If equally authoritative requirements conflict on correctness, contracts, safety or authorization, the affected action MUST stop with the conflict reported. Independent authorized work MUST continue.

Agents MUST follow documented client discovery and read governing `AGENTS.md` files and routed Skills directly if the client has not loaded them. Subagents MUST receive applicable rules and paths. Skills live under `.agents/skills` with Graphify under `.codex/skills`. Each rule set MUST have one source.

## Hard Constraints and Change Scope

### Scope and compatibility

- Agents MUST start at the user's files or the smallest relevant feature and expand only for a proven dependency. Before introducing a pattern, they MUST identify owners, contracts, composition, DI registrations, ViewModels and config.
- Changes MUST be minimal, safe and compliant, reuse compatible nearby patterns and preserve behavior, APIs, accessibility, assemblies, GUIDs, bindings and save/network compatibility unless the task requires changes.
- Agents MUST fix in-scope violations that cause the issue or block integration with the smallest necessary adapter or structural change. Explicit user authorization is REQUIRED for broad migrations, unrelated refactors, dependency changes, DI replacement and automatic formatting.

### Source and tooling

- Agents SHOULD use explicit Unity null checks. They MUST report actionable Editor errors and follow nearby `Debug.LogError`, `Debug.LogWarning` and exception conventions. Silent failures and excessive or spammy hot-path logs are FORBIDDEN.
- New source comments, XML documentation, headers, summaries and commented-out code in any language are FORBIDDEN. Existing comments MUST stay unless the task requires changing them. Functional directives (`#if`, `#else`, `#endif`, `#define`, `#pragma`, `#include`, `#nullable`, shebangs), required metadata and documentation prose are permitted.
- Editor-only scripts MUST live in `Editor` folders outside Runtime assemblies. Runtime MUST NOT reference `UnityEditor`. Editor tools MUST support Undo where appropriate.

## Mandatory Architecture

### Responsibilities and ownership

- SOLID, DRY, KISS and YAGNI are REQUIRED for new code and changed responsibilities. Responsibilities MUST be cohesive, contracts focused, invariants and lifecycles preserved, dependencies explicit and shared rules reused. Composition and meaningful boundaries SHOULD be preferred. Parallel layers, cosmetic splits and speculative abstractions are FORBIDDEN.
- Application services MUST use existing DI and composition roots, resolve only at composition boundaries and own explicit lifetimes and disposal. Agents SHOULD prefer constructor injection for plain C# where practical. Unity objects MUST use existing injection/initialization. Locators, mutable global/static services, singleton, scene-search or ad hoc construction bypasses are FORBIDDEN. Local helpers, value objects, DTOs and short-lived domain or low-level objects need no registration.
- Simulation, authoritative state, read-only config, persistence and presentation MUST be separate. Command → owner → committed state → notification/snapshot → visualization. Culling, hidden visuals, streaming and closed UI MUST NOT implicitly affect unrelated simulation. Activation, sleep and scheduling MUST stay with its owner.
- Each mutable gameplay fact MUST have one owner. Agents MUST reuse it and write through commands or controlled initialization, including inventory membership, quantity, equipment, durability and ammo. Caches, snapshots, reactive projections and indices or render/native buffers MUST define owner, sync direction, invalidation and lifetime. UI owns UI-local state. Save/restore and potential networking MUST stay at explicit boundaries. Save schemas MUST stay unless explicitly required to change.

### Feature layers

Each feature MUST organize its runtime code by the responsibilities below. Place new code under the owning feature's `Domain/`, `Application/`, `Infrastructure/`, or `Presentation/` folder, or reuse an existing folder with the same responsibility, such as `Core/` for domain logic and `UI/` for presentation. Create a layer folder only when it has code to own. Existing files, folders, namespaces, and assemblies MUST stay in place unless a separate task authorizes a migration.

| Layer | Responsibility |
|---|---|
| `Domain` | Own gameplay rules, invariants, authoritative state, and state transitions. Expose operations and read-only queries through the domain owner. |
| `Application` | Orchestrate feature use cases, commands, and queries. Invoke domain operations, coordinate external work through explicit contracts, and return results or committed state to callers. The domain owner remains responsible for gameplay decisions and state changes. |
| `Infrastructure` | Implement external dependencies such as file persistence, platform APIs, and non-visual Unity integrations. Map external data to and from the contracts of the feature. |
| `Presentation` | Own Views, ViewModels, user input, visual adapters, and UI-local state. Forward user intent to application commands and render committed state through read-only queries or notifications. |

- Source dependencies MUST point inward. `Presentation` depends on `Application` contracts, and `Application` depends on `Domain`. `Presentation` MAY consume existing read-only domain contracts. `Domain` MUST remain independent of the other feature layers, DI resolution, file I/O, and Unity presentation and scene lifecycles.
- `Infrastructure` depends on the inner contracts it implements. The layer that needs an external capability MUST own its contract. `Application` and `Presentation` MUST NOT depend on concrete infrastructure implementations. The existing DI composition roots wire implementations to contracts and own their lifetimes. Cyclic dependencies are FORBIDDEN.
- Layers define source responsibilities. Reuse current assembly boundaries and the public contracts of other features. A separate assembly is justified only by the dependency, test, or platform boundary rules in [Project Folder Layout](#project-folder-layout).

### Configuration and Unity adapters

- Exactly one `GameConfig` ScriptableObject is REQUIRED for shared runtime config. It and referenced domain configs MUST stay read-only at runtime. Location values, runtime state, saves and Editor/importer settings MUST stay with their owners. Precedence MUST be global defaults → explicitly enabled location overrides → runtime/session overrides → applicable restored state. Legacy serialized values alone MUST NOT enable overrides. Agents MUST reuse settings and preserve authored overrides and references.
- MonoBehaviours MUST focus on Unity lifecycle, explicit references and adapter/presentation duties. Inventory, equipment, item config and persistence logic MUST stay out of UI. Agents MUST use existing owner contracts and MUST NOT introduce hidden scene dependencies.

## Project Folder Layout

Files MUST stay with their owning feature, domain, or tool. Use the smallest existing folder that fits the work. Subfolders SHOULD reflect a distinct responsibility, such as inventory transactions or optics. A new top-level code or asset folder requires a concrete ownership or Unity integration reason.

| Area | Placement |
|---|---|
| Gameplay code and feature-specific UI | `Assets/_Source/Scripts/Gameplay/<Feature>/`. Apply [Feature layers](#feature-layers) within the owning feature. Keep inventory views and ViewModels under `Assets/_Source/Scripts/Gameplay/Inventory/UI/`. |
| Composition, graphics, and application menus | Reuse `Assets/_Source/Scripts/DI/Runtime/`, `Assets/_Source/Scripts/Graphics/`, or `Assets/_Source/Scripts/UI/` for their respective responsibilities. Use `Assets/_Source/Scripts/Utils/` only for genuinely shared utilities. |
| Editor code | Reuse `Assets/_Source/Scripts/Editor/<Feature>/` or an existing feature-local `Editor/` assembly. Under a runtime `.asmdef`, route Editor scripts to an Editor-only assembly using an `.asmdef` with only `Editor` in `includePlatforms`, or an `.asmref` to an existing Editor-only assembly. |
| Game content | Place new location scenes and their content under `Assets/_Source/Levels/<Location>/`, following the existing hierarchy for imported locations. Keep menus, previews, and fixtures with their respective owners. Preserve existing build-scene paths. Reuse existing content owners under `Assets/_Source/`, such as `Anomalies/`, `Effects/`, `FirstPerson/`, `UI/` and `Shaders/`. Item content follows [Game Assets](#game-assets). |
| Settings | Keep shared runtime configuration assets under `Assets/_Source/Settings/` with their owner. The existing bootstrap asset is `Assets/_Source/Settings/Resources/GameConfig.asset`. Unity project settings belong under the repository root's `ProjectSettings/`. |
| Tests and fixtures | Reuse `Assets/_Source/Tests/<Feature>/Editor/` and `Assets/_Source/Tests/<Feature>/PlayMode/` as applicable. Tools with their own test assemblies keep their tests and fixtures with the tool. |
| Tools and dependencies | Project-owned import tooling and its runtime support stay under `Assets/_Tools/`. Unity Package Manager packages and manifests belong under `Packages/`. Preserve third-party import roots. `Assets/_BENCHMARK/` contains checked-in benchmark sources and scenarios. Generated outputs follow [Archive Safety](#archive-safety). |

- Agents MUST verify the enclosing `.asmdef` or `.asmref`, platform filters, and dependency direction before adding or moving C# files. Introduce an assembly only for a concrete dependency, test, or platform boundary. Runtime and test assemblies MUST preserve their existing dependency contracts.
- Unity includes content under `Resources/` in Player builds. Preserve the existing item root and runtime lookup keys. Elsewhere, prefer serialized references or the owning catalog. A new `Resources/` folder or path-based lookup requires a concrete loader contract.
- Before an authorized move, agents MUST check GUID and serialized references, string-based paths (including `Resources.Load`, `AssetDatabase`, and importer destinations), scene/build configuration, and assembly membership where applicable. Move Unity assets with their `.meta` files. A folder convention alone grants no migration authorization.

## C# Source and Style

### Type declarations and compatibility

- **One Type per C# File and one handwritten declaration per type are REQUIRED** for new and directly edited project-owned handwritten type definitions, including tests, Jobs, DTOs, delegates and private types. Each type MUST be at namespace level in its matching file. Nested or multiple types, handwritten `partial` fragmentation and responsibility-suffixed fragments are FORBIDDEN. Conditional branches MUST NOT conceal extra declarations. An arity suffix or subfolder may disambiguate distinct types if Unity file-name rules are preserved.
- A single handwritten `partial` is allowed only for an existing compiler/source-generator/tooling contract with generated counterparts. Legitimate directive-only files need no type. Hand-editing generated or third-party files for this rule, evasion by code generation and cosmetic splits via regions, forwarding wrappers or extensions are FORBIDDEN. Low-level code has no exemption.
- For a directly edited multi-type, nested or fragmented file, agents MUST inspect all declarations in the compilation scope and extract minimal safe types, consolidate cohesive fragments or separate genuine responsibilities with explicit ownership. They MUST verify names, access, assemblies, behavior, `.meta`/GUIDs, MonoBehaviour/ScriptableObject and `[SerializeReference]` identities, saves and networking. If safe remediation needs wider scope or approval, they MUST stop and report the blocker. Unrelated legacy debt MUST stay outside scope.

### Formatting

- Naming MUST use `_privateField`, `UPPER_CASE` constants, `IInterface`, PascalCase types and public members, and camelCase locals and parameters. Agents SHOULD avoid public fields and prefer `[SerializeField] private`. Braces MUST be on separate lines and parameters and conditions on one line. Member groups MUST use no internal blank lines and one blank line between groups. Compliant surrounding indentation, namespaces, ordering and expression-body conventions MUST be retained.

## Dependencies and UI

### Dependency policy

- Existing dependencies MUST be reused. Explicit authorization is REQUIRED to add or replace packages, NuGet, libraries, Editor extensions, reactive/async/DI frameworks or change the container. DOTween and UniTask MUST already exist. Existing UniTask usage MUST be retained and SHOULD be preferred over `Task` for Unity runtime. Cancellation MUST propagate end-to-end and follow the owner lifetime. Unjustified async, fire-and-forget without intent, gameplay `Task.Run`, blocking `.Result`/`.Wait()`, ignoring long-running cancellation and `async void` beyond unavoidable Unity events are FORBIDDEN.

### UI ownership and updates

- **MVVM + R3 are REQUIRED** for new UI, new legacy-UI functionality and new bindings. Agents MUST verify R3 and assembly references first and report a blocker if absent or inaccessible. Installation requires explicit authorization. Substitutes and polling are FORBIDDEN. Stateless visuals may remain View-only within their feature.
- Current and committed values MUST bind reactively. Views own visuals, Unity references and forwarding. ViewModels own presentation state, read-only projections and commands to domain owners. Gameplay rules, direct writes and duplicate state in UI are FORBIDDEN. UI MUST NOT poll gameplay in frame callbacks, coroutines, timers or streams. Frame-dependent visuals/input are allowed. When only sampled external/engine data are available, the owner/adapter MUST sample at a justified cadence and publish changes.
- Subscriptions MUST have explicit ownership and disposal on the correct lifecycle boundary, including pooling, reopen and rebind. Unity callbacks MUST run on the main thread. Redundant updates MUST be suppressed without losing committed changes. UI MUST use `TMP_Text`, `TextMeshProUGUI` and `TMP_InputField` as appropriate. Legacy `UnityEngine.UI.Text` is permitted for new UI only when surrounding code requires it.

## Game Assets

- Game assets MUST live with the feature, location, or item that owns them. Each new item or item being migrated MUST have an item folder under `Assets/_Source/Resources/Items/<Category>/`. Reuse the existing category and applicable subcategory path, then use `<ItemName>/` for the item. Keep the definition, source models, prefabs, and other item-specific files in that folder. Store textures and materials in `Textures/` and `Materials/` within the item folder, including folders generated beside source models. Moving existing assets or changing importer outputs requires authorization within the task scope.
- Shared assets MUST have one maintained copy with their owning feature or category. Store shared item sounds in the owning category's `_Shared Audio/` folder. Keep atlas-backed icons in their existing atlases. The shared item atlas location is `Assets/_Source/UI/Item Icons/`. If an item needs independent material or texture edits, keep the item-specific asset in its own folder.
- World, first-person, inventory, and preview views MUST reuse one source visual prefab when they show the same model. Views can reference or nest it and set their own pose, camera, visibility, and view-specific components. Distinct geometry or rigging may use a variant with the same owner. Variants MUST reuse compatible shared assets. Edits to the source visual MUST reach each view that uses it.
- Runtime systems MUST resolve assets through the owning definition, catalog, or explicit serialized references. Definitions MUST remain read-only at runtime. Mutable item state MUST belong to the runtime instance and be saved through the existing persistence system.
- Agents MUST preserve `.meta` files, GUIDs, and serialized references when moving or renaming assets. Before deletion, agents MUST check consumers. Feature and item removal MUST preserve shared assets and unrelated atlas entries and remove only assets exclusive to the removed owner.

## Performance and Rendering

- `rvm-performance` and applicable subsystem Skills are REQUIRED for changes to runtime, rendering, import, resource loading, effects, memory or performance. CPU, GPU, GC and loading cost MUST stay bounded. Hot paths MUST avoid allocations, LINQ, repeated lookups, unnecessary scene searches and redundant work. Caches MUST define lifetime and invalidation. Out-of-scope optimization is FORBIDDEN.
- Burst, Jobs, native containers, BRG/culling, shaders, rendering, import and measured hot paths may specialize locally for concrete execution/API constraints or measured cost. Correctness, determinism, ownership, synchronization, thread safety, resource/JobHandle lifetimes and dependency direction MUST hold. Per-frame/entity/job/kernel DI resolution and managed R3 in inner loops are FORBIDDEN. Unnecessary virtual dispatch, indirection and allocations MUST be avoided. Application DI remains REQUIRED. This exception MUST NOT waive UI polling, state, dependency, source or scope rules.
- Image quality and simulation/visual separation MUST be preserved. Materials MUST use compatible HDRP shaders and `HDRP/Lit` for Lit. Agents MUST verify HDRP texture properties. Pipeline conversion or downgrade requires explicit user instruction. Exception scope, basis, measured baseline comparison and limitations MUST be reported. Diagnostics MUST go to Archive.

## Production Safety and Validation

### Project and session safety

- Routine Editor investigation and validation MUST use Unity Open MCP in active Production after absolute path verification. Agents SHOULD prefer scoped read-only calls and compact results. If live checks fail, agents MUST use documented recovery/read-only routes and report gaps. Destructive MCP actions require explicit authorization.
- Agents MUST protect dirty scenes and user state. Unexpected project switches, scene replacement, saves or discards are FORBIDDEN. Changes to scenes, prefabs, materials, shaders, levels, ProjectSettings and other assets MUST be task-required. Agents MUST NOT edit Production assets merely to run an experiment.
- Potentially unsafe experiments and isolated testing require explicit authorization and a user-specified project path. Before testing, agents MUST compare Unity/HDRP and affected files to Production. Routine copies and silent batch/isolated switches are FORBIDDEN. The project MUST be free before any tool that may auto-launch headless Unity.
- Synchronization requires separate explicit authorization. Agents MUST compare exact files, preserve GUIDs and unrelated edits and exclude regenerable caches.

### Verification and production integration

- Essential regressions and fixtures MUST stay with `.meta` in Production, including X-Ray parsing, import, material generation, Original TBN, Hemisphere, Flora, MT_LOD, Decals, renderer lifecycle, MO2 resolution and GUID retention. Test or one-time diagnostic relocation requires dependency evidence and explicit approval. Only complete assembly/asset groups may move.
- Before minimal authorized Production integration, agents MUST verify experimental equivalence and reproducibility. They MUST preserve GUIDs and references, run relevant Production regressions and report results. Isolated-only work MUST NOT be reported as completed Production work.
- Before finishing, agents MUST verify the scoped diff and applicable Skill checklists, including compatibility, C# declarations across compilation scope, comments, style, ownership, DI, reactive lifetimes, cancellation, Editor/runtime and cleanup. In-scope failures MUST be fixed or reported with the exact blocker and outstanding validation. Documentation-only work requires consistency and link checks without Unity execution.

## Archive Safety

- Generated output MUST go to a unique Archive task directory configured before tooling starts. Required code, assets, configuration, fixtures and `.meta` MUST stay in their project. After validation, agents MUST remove obsolete task-created files temporarily required by tooling in Production. That rule covers only task-owned files.
- Originals, unique evidence/backups, deliverables, fixtures, results and files in use or of uncertain ownership or reproducibility MUST be preserved. Overwriting retained files is FORBIDDEN. Agents MUST measure space before bulk runs and retain the evidence needed for the result.
- Only disposable generated artifacts inside Archive may be automatically cleaned up. Agents MUST resolve targets, verify containment, reject reparse points and junctions and exclude active consumers. Only task-owned processes may be stopped for cleanup. Git cleanup and removal of a task folder merely because it contains a build are FORBIDDEN.
- In an authorized historical revisit, deletion requires recorded proof of a retained exact duplicate or supported regeneration from retained inputs and no active consumer. Name, age, extension or a new run alone are insufficient. Broad Archive maintenance requires explicit authorization. `archive-management` is REQUIRED for cleanup evidence, retained size, reclaimed bytes and free space.

## Git Safety

- Agents MUST verify the repo root before Git inspection or mutation and use an explicit working directory or `git -C`. A copy without Git metadata has no verified commit identity.
- Automatic stage, unstage, commit, reset, restore, clean, deletion of untracked repo files and history rewrites are FORBIDDEN. User work MUST be preserved. A read-only audit may edit only explicitly authorized instructions. Repo cleanup requires separate authorization.

## Mandatory Skill Routing

### Load before the governed action

Agents MUST read **every applicable Skill** before its action and only applicable references. Routes are additive. If a mandatory Skill is unreadable and its requirements cannot be met reliably, agents MUST report the blocked action. External prerequisites and related Skills are advisory unless routed here or required by the user. Examples MUST be verified against installed Unity/HDRP and project rules.

### Routes by task

| Task or affected responsibility | Mandatory Skills |
|---|---|
| Gameplay/runtime feature or fix, item/inventory/equipment state, services, DI, architecture, GameConfig or persistence | [game-architecture](.agents/skills/game-architecture/SKILL.md) |
| Project-owned handwritten C# creation or editing | [csharp-style](.agents/skills/csharp-style/SKILL.md) |
| Final checks after C#, shader or helper-script code changes, or preparing a project Git commit/message | [cs-style-check](.agents/skills/cs-style-check/SKILL.md) |
| UI feature, binding or change to presentation state, subscriptions or lifecycle | [unity-ui](.agents/skills/unity-ui/SKILL.md). New UI functionality and changes to UI/domain boundaries also use `game-architecture`. |
| Changes to runtime, rendering, imports, resource loading, effects, memory or performance | [rvm-performance](.agents/skills/rvm-performance/SKILL.md) plus the applicable subsystem Skills |
| Unity MonoBehaviour/ScriptableObject implementation or Unity C# APIs | [unity-scripting](.agents/skills/unity-scripting/SKILL.md) |
| Unity lifecycle, initialization/teardown or ordering behavior | [unity-lifecycle](.agents/skills/unity-lifecycle/SKILL.md) |
| Unity async, coroutines, cancellation or async resource lifetime | [unity-async-patterns](.agents/skills/unity-async-patterns/SKILL.md) |
| Render pipeline, shaders, materials, textures, cameras, post-processing, BRG or rendering optimization | [unity-graphics](.agents/skills/unity-graphics/SKILL.md) |
| Lighting, probes, GI, particles, VFX or post-processing effects | [unity-lighting-vfx](.agents/skills/unity-lighting-vfx/SKILL.md) |
| Profiling, performance or memory optimization, Profiler/Memory Profiler/Frame Debugger | [unity-performance](.agents/skills/unity-performance/SKILL.md) |
| Unity Editor inspection/work, asset changes, compile checks, Play Mode, builds or validation | [unity-open-mcp](.agents/skills/unity-open-mcp/SKILL.md) |
| Headless Unity launch or batch-capable fallback, including CLI tests, captures and compile checks, and work in an authorized isolated project or mirror | [unity-batch-mirror](.agents/skills/unity-batch-mirror/SKILL.md) and [unity-open-mcp](.agents/skills/unity-open-mcp/SKILL.md) |
| Generated diagnostics, reports, captures, measurements, logs, builds or Archive maintenance | [archive-management](.agents/skills/archive-management/SKILL.md) |
| Codebase content, architecture, relationships or dependencies | [graphify](.codex/skills/graphify/SKILL.md) and the navigation rules below |
| GRA/Anomaly original behavior, balance, installation, MO2 configuration or game assets | [gra-research](.agents/skills/gra-research/SKILL.md) |
| Weapons, attachments, weapon items, HUD/world models, animation, sounds, effects or stats | [weapon-iwp](.agents/skills/weapon-iwp/SKILL.md) |
| X-Ray/GRA asset import, importer or Editor wiring of imported assets | [xray-gra-import](.agents/skills/xray-gra-import/SKILL.md) |
| Requested Russian Git commit or commit message | [git-commit-ru](.agents/skills/git-commit-ru/SKILL.md) |
| Relevant prose work, including docs, UI text, descriptions or an explicit prose audit | [no-ai-slop](.agents/skills/no-ai-slop/SKILL.md) |
| Explicit request for ADHD-oriented coaching | [i-have-adhd](.agents/skills/i-have-adhd/SKILL.md) |

## Graphify Navigation

- For `/graphify`, agents MUST read the installed Skill first. On codebase questions with `graphify-out/graph.json` and the CLI, they MUST run `graphify query "<question>"` from the repo before source search or reads. This MUST be in subagent code-investigation prompts. Agents MUST use scoped `path`/`explain` for focused relationships and the wiki for broad navigation when available. `GRAPH_REPORT.md` is reserved for broad review or insufficient scoped results.
- Source is authoritative for exact facts. Scoped fallback is allowed for insufficient query results, missing graph/CLI, proven staleness, graph-correctness or instruction work, or user opt-out. Dirtiness alone is insufficient. Navigation MUST NOT install, change hooks, rebuild, `save-result` or `reflect`. Strict read-only scope includes CLI cache writes. For explicit modes, if local references are unavailable, agents MUST use matching installed `graphifyy` references under `skills/codex/references` or report the blocker.
- After code changes, `graphify update .` (AST-only, no API cost) is REQUIRED within authorized scope. Agents MUST respect read-only limits and report failed or unavailable updates. A tracked `graph.json` is rewritten as a whole. Size alone MUST NOT justify skipping updates or hooks. Instruction-only edits need no rebuild.

## Communication

### During the task

At task start, before substantive work, agents MUST send one short message naming planned Skills or stating that none apply. User-facing text MUST be direct Russian unless another language is requested. Technical identifiers MUST stay in English. Agents MUST ask only when missing information or approval blocks safe work and provide one self-contained final response. Subject to higher-priority rules, further progress/status updates and partial summaries are FORBIDDEN. Output MUST stay scoped. Filler, unrequested basics and unrelated findings SHOULD be omitted.

### Final response

The final response MUST use exactly these sections, with depth proportional to the task:

1. **Short summary** — issue, outcome and implementation status.
2. **Changed files** — absolute links and purpose, modified Unity project, and commit only if actually created.
3. **Important implementation details** — cause, rationale, integration and behavior, checks and outcomes, test project, isolated revision differences and isolated-only changes if applicable, absolute paths to the reports and validation outputs. For generated artifacts include retained size, reclaimed space and the cleanup record path.
4. **Risks or limitations** — remaining issues, assumptions, skipped checks and their practical impact. Distinguish confirmed results from unverified behavior. If none remain, say so.
5. **Manual Unity steps, if any** — actions and expected results for required manual checks, or explicitly say none. If validation is unsafe or prohibited, report the reason in Risks or limitations.

Small tasks SHOULD be compact. Nontrivial reports SHOULD provide reviewable detail, typically 300–600 words. Documentation reports MUST explain rule changes and consistency checks. `git-commit-ru` requires Russian commit text, exactly one Gitmoji and unchanged English identifiers.

## Instruction Language

Project-owned AI instructions and Skill metadata MUST use technical English. MUST, MUST NOT, REQUIRED and FORBIDDEN mark requirements. SHOULD marks a recommendation. Translations MUST preserve priorities, scope, conditions, identifiers and exceptions, including Russian output examples. Skill descriptions MUST match English and Russian task intents.
