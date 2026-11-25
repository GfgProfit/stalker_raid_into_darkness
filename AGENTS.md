# AGENTS.md

## Project Context

This is a Unity game project.

Primary stack:
- Unity 6000.6.7f1 (Production)
- C#
- HDRP 17.6.0 (Production)
- TextMeshPro
- DOTween, only if already present in the project
- R3, mandatory for new UI (use the existing package; see Dependency Rules)
- UniTask, only if already present in the project
- Custom DI container under `Assets/_Source/Scripts/DI`

Main scripts are located under:

```text
Assets/_Source/Scripts
```

The project contains gameplay systems, inventory, equipment, UI, item configs, rendering tools, editor tools, and asset import workflows.

Work narrowly.

## Instruction Priority

1. Higher-priority platform, system, developer, and tool instructions.
2. Explicit user instructions for the current task.
3. Project-specific rules in this file.
4. Existing project patterns, where compatible with these rules.
5. General best practices.

Explicit user instructions override conflicting project rules within higher-priority constraints. Existing code, personal preference, generic best practices, and ordinary implementation requests do not authorize exceptions to mandatory standards.

## Hard Constraints

These rules override all implementation preferences.

FORBIDDEN:
- Sending progress updates
- Sending intermediate messages
- Sending partial summaries
- Writing any comments or summaries in code

REQUIRED:
- Reply only once after the task is fully completed
- Provide a substantive, self-contained technical final response with detail proportional to the task
- Use Russian in user-facing responses unless the user explicitly requests another language
- Preserve existing code style
- Minimize unnecessary context and tool output without reducing the completeness of the final response
- Implement the smallest safe change that solves the task and complies with the mandatory architecture rules
- Enforce the One Type per C# File rule for all new and directly modified handwritten C# source files; forbid handwritten partial-class fragmentation and responsibility-based file suffixes used to split a type

## Mandatory Architecture Rules

SOLID, DRY, KISS, YAGNI, DI for application-level services, MVVM + R3 for new UI, reactive UI updates, separation of simulation/state/visualization, and Single Source of Truth (SSOT) are mandatory, not preferences. Apply them to new code and changed responsibilities within scope. Existing patterns cannot override them. Only the legacy-maintenance boundaries in Existing Code First / Change Scope Control and the justified low-level exceptions below apply.

### One Type per C# File (Mandatory)

**Exactly one declared type per handwritten `.cs` type-definition file, and exactly one handwritten source file per type.** This is a hard source-organization rule, not a naming preference. It applies to Runtime, Editor, tests, networking, persistence, rendering, importers, Burst/Jobs, and all other project-owned C# code.

- Every class (including static, abstract, sealed, generic, and attribute classes), struct (including readonly, ref, and generic structs), interface, enum, record class, record struct, and delegate declaration must have its own dedicated `.cs` file, regardless of accessibility or size.
- DTOs, request/response contracts, serializable wrappers, save models, configuration types, event payloads, helper types, job structs, and private implementation types are not exceptions. Never collect multiple related types in one file merely because they belong to the same feature or protocol.
- Nested type declarations are forbidden, including nested private classes, structs, interfaces, enums, records, delegates, and generic types. Extract them as separate namespace-level types with the narrowest compatible accessibility. Do not work around the rule by nesting types, using multiple namespaces, or placing additional declarations in conditional compilation branches.
- The file name must match the declared type name, e.g. `ServerResponse.cs`, `InventoryResponse.cs`, `IInventoryService.cs`, `ItemCategory.cs`, and `Result.cs` for `Result<T>`. If existing contracts require different types with the same simple name in the same directory, use distinct subdirectories or an explicit generic arity suffix (e.g. `Result.Generic1.cs`); do not combine declarations or rename public contracts unnecessarily. Unity script assets must retain their required type/file-name matching.
- **No partial-type fragmentation or cosmetic decomposition.** A handwritten type must have exactly one complete handwritten declaration in exactly one `.cs` file. Do not use `partial` classes, structs, interfaces, or records to distribute fields, methods, Unity lifecycle callbacks, input, movement, state, or other responsibilities across handwritten files. This prohibition applies even if every individual file contains only one declaration.
- FORBIDDEN: `PlayerController.Input.cs`, `PlayerController.Lifecycle.cs`, `PlayerController.Movement.cs`, `PlayerController.State.cs`, `PlayerController.Core.cs`, `PlayerController.Part1.cs`, and any equivalent naming scheme that stores fragments of `PlayerController` in separate files. Using folders, regions, conditional compilation, or different file names does not make fragments separate types or satisfy SRP. Regions may organize a cohesive type within its single file, but do not count as architectural decomposition. The generic arity suffix exception above is only for genuinely distinct types, never fragments of the same type.
- When a type genuinely has multiple independent responsibilities, decompose it architecturally into cohesive, separately declared types with explicit dependencies, ownership, and composition. For example, `PlayerInputHandler.cs`, `PlayerMovement.cs`, and `PlayerController.cs` must declare genuinely distinct types with meaningful responsibilities, not partial declarations, forwarding-only wrappers, or extensions that merely conceal the original god object. Keep lifecycle coordination with its appropriate owner. Do not create one class per method or extract cohesive behavior merely to reduce line count; apply SOLID, DRY, KISS, YAGNI, DI, and SSOT together.
- For an existing fragmented handwritten type directly modified by the task, inspect all its partial declarations and resolve the violation within the authorized scope: consolidate a cohesive type into its canonical file, or perform real responsibility-based decomposition when required. Preserve behavior, Unity bindings and GUIDs, accessibility, serialization, save/network contracts, and assembly boundaries. Do not rename or delete existing script assets blindly. If safe remediation requires a broader migration or authorization, report the blocker; do not add another fragment or claim compliance. Do not migrate unrelated legacy partial types without explicit authorization.
- The only allowed `partial` use for project-owned handwritten code is a single handwritten declaration required by an existing compiler/source-generator/tooling contract with generated counterparts. Do not add handwritten fragments to such a type or introduce code generation to evade this rule. Do not hand-edit generated or third-party files to impose this standard. Files containing only assembly/module attributes, global usings, or other legitimate non-type compilation directives do not require an artificial type declaration.
- Keep related files together in their owning feature/domain folder; no unnecessary global DTO/helper folders or additional wrappers. Members, constructors, properties, fields, methods, events using existing delegate types, and local functions remain inside their owning type. Generic parameters are part of a type declaration, not additional types.
- Enforce the rule when creating or editing project-owned handwritten C# files. If a directly edited file already contains multiple or nested types, perform the smallest safe extraction of those existing types, rather than adding to or perpetuating the violation. Preserve behavior, namespaces where compatible, assembly boundaries, accessibility constraints, serialization, Unity `.meta` GUIDs, MonoBehaviour/ScriptableObject bindings, `[SerializeReference]` identities, save compatibility, and network contracts. If safe extraction requires a broader migration or approval, report the blocker instead of silently waiving the rule or breaking compatibility.
- Do not conduct an unrelated repository-wide file-splitting migration without explicit authorization. Existing violations outside the current task scope may remain temporarily; they are legacy debt, not valid patterns for new or directly edited files.
- No low-level, performance, SOLID, DRY, KISS, YAGNI, or existing-pattern exception authorizes multiple handwritten type declarations in one file or fragmentation of a handwritten type across files. Separate file placement alone does not require runtime indirection, allocations, or additional architectural layers.

### SOLID, DRY, KISS, YAGNI

- **SRP:** Each class/module/service must have a cohesive responsibility. Do not mix unrelated simulation, presentation, persistence, and orchestration in god objects.
- **OCP:** Preserve stable behavior; use composition and meaningful extension points for real requirements.
- **LSP:** Implementations must honor contract behavior, invariants, and lifecycle guarantees.
- **ISP:** Expose focused consumer contracts, not oversized interfaces.
- **DIP:** High-level policy depends on appropriate abstractions and explicit dependencies, not concrete infrastructure/presentation where a meaningful boundary exists. Use existing DI at application boundaries.
- **DRY:** Maintain one implementation of shared rules, formulas, transitions, and configuration values. Similar code with different semantics need not share an abstraction.
- **KISS:** Prefer the simplest correct, maintainable, performant design. Avoid unnecessary wrappers, factories, layers, and indirection.
- **YAGNI:** Implement actual requirements and requested extension points, not speculative features or frameworks.

Prefer composition, focused services, and clear dependency direction. Use interfaces for real boundaries, substitution, DI, or testing, not for every class. Do not mechanically split responsibilities or create abstractions merely to satisfy SOLID. Moving existing type declarations into dedicated files to satisfy One Type per C# File is mandatory source organization, not a responsibility split. KISS/YAGNI do not excuse bypassing mandatory DI, ownership, or UI rules.

### Simulation, State, and Visualization

- **Simulation:** Gameplay rules, calculations, validation, transitions. Independent of UI, cameras, renderers, audio, and VFX; prefer plain C# or data-oriented code where Unity APIs are unnecessary.
- **State:** Authoritative mutable runtime data belongs to its owning system, separate from read-only config, save snapshots, and UI-local state. Expose controlled writes and explicit reads.
- **Visualization:** Consume state, notifications, or snapshots; own only presentation behavior and visual state. Unity adapters may forward engine/physics/input signals to the owning system.

Flow: input/command -> simulation/owner -> committed state -> notification/snapshot -> visualization.

Visual culling, disabling visuals, closing UI, or streaming out presentation must not implicitly alter unrelated simulation. Intentional activation/sleep/scheduling belongs to the simulation/lifecycle owner. Keep save/load and potential networking at explicit state/command boundaries; do not implement unrequested networking. Separate responsibilities logically without forcing managed abstractions or allocations into data-oriented code.

### Single Source of Truth (SSOT)

- Each gameplay fact has exactly one authoritative owner at a time, not one global god manager. Identify the owner before changing state. Route writes through its operations/commands/controlled simulation path; expose read-only queries, contracts, snapshots, or notifications.
- UI, Views, ViewModels, visuals, and other services must not maintain competing writable copies. ViewModels own only UI-local state and derived projections.
- Save DTOs, caches, reactive projections, spatial indices, render/native buffers, and snapshots are allowed, not authoritative copies. Define owner, synchronization direction, update/invalidation, and lifetime. Restore snapshots through the owner or controlled initialization; config assets remain read-only.
- For inventory membership, quantities, equipment assignments, item durability, ammo and other mutable facts, identify and reuse the authoritative owner established by the actual architecture. Coordinate through explicit boundaries; never duplicate writes or rules. Do not invent a competing owner or change an existing ownership contract without a task-specific reason.

### Low-Level and Performance-Critical Exceptions

Burst, Jobs, native containers, BRG/culling, shaders, rendering, import pipelines, and measured hot paths may use specialized designs only when application patterns conflict with concrete execution/API constraints or cause measured overhead.

- Allow structs, contiguous buffers, explicit memory ownership, batching, static pure helpers, direct data access, explicit loops, fused operations, specialized algorithms, fixed-step simulation, and frame-driven rendering. Do not force R3/managed abstractions into inner loops or resolve DI per entity/job/frame/kernel. Owning application services still use DI.
- Allow data-oriented projections/buffers/duplication for locality with explicit ownership and synchronization. Keep correctness, state ownership, simulation/visual separation, thread safety, JobHandle/native-memory and resource lifetimes, and dependency direction intact. Never trade correctness or determinism required by the mechanic for an unverified optimization.
- Localize exceptions; apply `rvm-performance`. Require concrete technical constraints or profiler/benchmark evidence; measure claimed improvements and regressions. Report scope, justification, evidence, and validation limits; diagnostics in Archive, never source comments.
- No exemption for unnecessary UI polling, unowned global mutable state, application-level DI, dependency restrictions, or unrelated redesign. DRY/KISS/YAGNI apply except for justified local trade-offs.

## Misc

All reports, validation results, diagnostic EXR/PNG/CSV files, logs, temporary Player Builds, benchmarks, and other generated artifacts must be stored **exclusively outside the Unity project**:

`E:\Projects\stalker_raid_into_darkness_Archive`

- Do not create diagnostic directories or temporary builds in the project root or inside `Assets`.
- Production code, essential automated regression tests, required production Unity Assets, `.meta` files, and configuration files must remain inside the production project.
- Create a separate subdirectory in the archive for each task, for example, `XRayMO2LevelImporter_Validation`.
- Always include absolute paths to reports and validation results in the final response.
- Preserve final reports, unique evidence, source and asset backups, and required deliverables from other tasks. Automatically remove disposable generated artifacts under Archive according to Section F; never overwrite retained results.
- If a tool requires temporarily creating files inside the project, remove them after validation only if they were created by the current task and are no longer required for the project to function.

## Communication Rules

Do not send progress updates, status messages, intermediate summaries, or partial results.

Do not say what you are about to do while working.

Do not ask for confirmation unless the task is impossible or unsafe without missing information.

Do not explain basic Unity, C#, Git, or IDE concepts unless asked.

Respond only after the task is completed.

The final response must use Russian unless the user explicitly requests English.

Code, class names, method names, variable names, namespaces, file names, folder names, package names, and technical identifiers must remain in English.

For commit message language and format, follow the `git-commit-ru` skill: Russian text, exactly one Gitmoji, and unchanged English technical identifiers.

## Final Response Format

The final response must contain only these sections:

1. Short summary
2. Changed files
3. Important implementation details
4. Risks or limitations
5. Manual Unity steps, if any

Write a detailed final report that lets the user understand the result without reading progress messages, opening reports, or inspecting the diff first. Keep the short summary brief and develop the relevant details in the other sections.

For nontrivial implementation, debugging, audit, or visual tasks, normally aim for 300–600 words. Use more detail when the scope or findings require it. Small targeted edits may use a shorter response, provided it explains the actual changes and verification. Treat these lengths as guidance; avoid filler, repetition, and unrelated explanations.

Content of the required sections:

1. **Short summary:** describe the problem addressed, the resulting behavior, and whether the requested work is complete. Distinguish completed implementation from unresolved validation or missing prerequisites.
2. **Changed files:** identify the relevant files or related groups with absolute links and explain their purpose. State which Unity project was modified. Include commit information only when a commit was actually created.
3. **Important implementation details:** explain the established cause, the chosen solution, why it fits the task, and any material behavior or integration changes. Describe the checks performed, their scope, results, and what was not checked. Include absolute paths to reports and generated artifacts where applicable. Links, commit hashes, and test counts supplement this explanation rather than replace it.
4. **Risks or limitations:** describe remaining issues, unverified behavior, compatibility constraints, skipped checks, and their practical impact. Separate confirmed results from assumptions. If no material risks remain, say so without inventing warnings.
5. **Manual Unity steps, if any:** give concrete actions and expected results when user intervention is required. State explicitly when no manual steps are needed.

For documentation-only tasks, explain the changed rules or wording and the consistency checks performed; Unity tests are not required.

If validation was not run because it is forbidden or unsafe, say so in `Risks or limitations` and list manual checks in `Manual Unity steps`.

## Skill Integration

Apply [rvm-performance](.agents/skills/rvm-performance/SKILL.md) whenever changing runtime code, rendering, import, resource loading, effects, memory, or performance. Its correctness, automatic application, networking boundaries, and removable diagnostics rules are mandatory within the task scope.

Project skills:
- `.claude/skills/...`

Apply compatible principles from both skills to all user-facing responses by default:
- Put the result or next concrete action first, within the required final response format.
- Keep responses specific, structured, and easy to scan. Give the final response enough detail to understand the result, decisions, verification, and remaining work. Number multi-step instructions only when useful.
- Use natural, direct Russian without generic AI phrasing, filler, hype, empty praise, or redundant conclusions.
- Preserve meaning, technical accuracy, project terminology, identifiers, and existing code style.
- Do not add extra final response sections or override any project hard constraints.

For documentation, UI text, descriptions, and other prose writing or editing tasks, invoke `no-ai-slop` when relevant and preserve the author's voice. Do not apply prose-editing rules to source code or rename identifiers for stylistic reasons.

Project constraints and explicit user instructions take precedence over these style preferences.

## Project Directories and Testing Policy

### A. Canonical Paths

Always use these exact locations:

```text
Production:
E:\Projects\stalker_raid_into_darkness


Archive:
E:\Projects\stalker_raid_into_darkness_Archive
```

Production is the authoritative source of code and assets. Use Unity Open MCP in the active Production Editor for routine validation. An isolated test project requires explicit authorization and a user-specified location.

### B. Directory Roles

- Production code, required Runtime/Editor systems, X-Ray importers, Monolith shaders, required scenes/assets/configuration, and essential automated regression tests belong in Production.
- Reports, validation results, PNG/EXR captures, CSV measurements, logs, temporary Player Builds, benchmark outputs, GPU/CPU readbacks, diagnostic JSON, temporary exports, and historical investigations belong in Archive when they are not required Unity assets.

### C. Production Safety

Never modify production scenes, materials, imported levels, ProjectSettings, or other important assets merely to run an experimental test. Run potentially unsafe experiments only in an explicitly authorized isolated project.

Do not unexpectedly change the active Unity project. Verify its absolute path before using Unity tools. Respect existing dirty-scene protections and user instructions before opening or replacing scenes, saving changes, or discarding unsaved state.

### D. Isolated Testing

Verify the isolated project path, Unity/HDRP versions, and relevant files against the Production revision before running tests. Document differences that affect the result.

### E. Synchronization

Require explicit authorization for synchronization. Compare the exact files, preserve GUIDs and unrelated changes, and exclude regenerable caches. Do not create or synchronize a mirror automatically.

### F. Test Results and Archive

Configure diagnostic output paths under Archive before running tools. Use a task-specific subdirectory and unique run directories or filenames; never overwrite retained results. Categories such as `CleanupAudits`, `ShaderValidation`, `RendererLifecycle`, `MO2ImporterValidation`, and `PerformanceBenchmarks` are illustrative, not alternate project roots.

Do not store permanent reports or large diagnostic artifacts in `Assets`, the Production project root, or an isolated test project. Preserve required Unity fixtures and their `.meta` files inside the appropriate project. Keep unrelated historical reports unchanged.

#### Artifact Lifetime and Automatic Cleanup

Archive is a working output location. Keeping every generated run indefinitely is forbidden. Cleanup is part of completing any task that generates artifacts and does not require a separate user request or confirmation within the scope below.

- Before bulk captures, benchmarks, profiling, or Player builds, check available disk space and estimate output size. Limit the capture matrix, resolution, frame count, and retained runs to what the task needs. Reuse a validated Player build when the tested code, assets, build settings, and configuration match.
- Keep a concise final report, test results, necessary CSV/JSON metrics, and representative PNG evidence. Generate EXR, RGBA32F, full Profiler `.raw` files, and per-frame readbacks only when their precision or timeline is needed. After analysis, remove large intermediate data that is no longer needed to support the result. Preserve the minimal raw evidence needed for unresolved failures or checks that summaries and previews cannot establish; PNG previews do not preserve EXR HDR precision.
- Automatically delete superseded and completed temporary Player builds, abandoned outputs from failed runs, disposable caches, duplicate delivery packages, temporary exports, and intermediate captures created by the current task once they are no longer needed. Stop only task-owned processes before removing their files. Preserve reports, manifests, metrics, screenshots, and source or material backups stored inside build directories before deleting runtime binaries. Do not delete a task directory merely because it contains a build.
- Preserve builds and other deliverables explicitly requested by the user until delivery and while they remain required. Preserve original inputs, unique source and asset backups, rollback copies, required regression fixtures, `.meta` files and GUID references, final reports, and evidence still needed for investigation. If ownership, reproducibility, or remaining use is uncertain, retain the affected files and state the reason in the final response.
- When revisiting an Archive task directory, also remove older generated bulk that is confirmed redundant or reproducible. Record proof that the original inputs still exist and regeneration is supported, or verify an exact retained duplicate before deleting it. A directory name, file extension, age, or a successful newer run alone is insufficient evidence. Do not run a full Archive scan for every small task.
- This standing authorization applies only to disposable generated artifacts inside `E:\Projects\stalker_raid_into_darkness_Archive`. Resolve and verify every deletion target stays inside that root, reject reparse points and junction traversal, and exclude files used by other active tasks. It does not authorize cleanup of Production, isolated projects, legacy projects, Git data, or unique backups. Use file selections or verified artifact directories; never construct broad recursive deletions from unchecked paths.
- Before completing the task, measure retained output size and free disk space, remove only empty directories left by the current cleanup, and save a compact cleanup record with paths, reasons, deleted bytes, and any failures in the task's Archive directory. Report retained artifacts and actual reclaimed space within the existing final response sections. Retain only the reports and helpers needed to understand or reproduce the final result.

### G. Production Regression Tests

Keep essential automated regression tests in Production unless an audit proves relocation safe and the user explicitly approves it.

This includes X-Ray format parsing, importer correctness, material generation, Original TBN, Hemisphere, Flora, MT_LOD, Decals, renderer lifecycle, MO2 resolution, and GUID preservation. Specialized one-time diagnostic tools may be proposed for relocation only with dependency evidence and explicit approval; move complete assembly/asset dependency groups, never isolated pieces that break compilation.

### H. Final Reporting

Within the existing final response sections, state which Unity project was modified, which project was used for testing, and which tests ran. If an isolated project was used, state whether its relevant files matched the tested Production revision.

Report the absolute Windows paths of reports and diagnostic results. State which changes, if any, exist only in the isolated project.

### I. Git Safety

Before Git commands that inspect or modify repository state, verify the intended repository root. Prefer an explicit working directory or `git -C`. Do not confuse a copied test repository with the production repository; a copied directory without Git metadata has no independently verified commit identity.

Do not perform Git cleanup without explicit permission. Do not automatically stage, unstage, commit, reset, restore, delete untracked repository files, or rewrite history. During a read-only repository audit, modify only the instruction files explicitly authorized by that task; do not start repository cleanup until the user reviews and approves the audit. Automatic cleanup of disposable Archive artifacts under Section F is authorized separately and must not use Git cleanup commands.

### J. Production Integration

When an experimental change succeeds in an isolated project, identify the minimal production change and confirm reproducibility. Obtain approval when required by the task, apply only the necessary implementation to Production, preserve GUIDs and serialized references, run the relevant production regression tests, and document the production result.

Never silently merge experimental changes into Production or present a change that exists only in an isolated project as a completed production implementation.

## Unity MCP Workflow

- Use Unity Open MCP for Unity Editor inspection and validation.
- Verify the active Unity project before modifying assets.
- Use the main project for normal development and validation.
- Prefer read-only MCP operations during investigation.
- Do not modify scenes, prefabs, materials, shaders, or project settings unless required by the task.
- Never execute destructive MCP operations without explicit authorization.
- Do not copy entire projects for routine validation.

## MCP Token Efficiency

- Prefer targeted Unity MCP calls over broad project scans.
- Avoid redundant MCP calls and repeated validation.
- Retrieve only task-relevant Unity data.
- Do not dump large MCP results into conversation context.

## Existing Code First

Before introducing a new pattern, inspect nearby relevant code and identify existing state owners, contracts, composition roots, DI registrations, ViewModels, and configuration assets.

Prefer consistency with existing project code over theoretical improvements only when the existing pattern satisfies the mandatory architecture rules.

When modifying an existing feature:
- Follow surrounding architecture where compliant
- Follow surrounding naming
- Follow surrounding folder structure
- Follow surrounding dependency patterns where compliant
- Follow surrounding formatting
- Follow existing serialization patterns
- Preserve GUIDs, serialized references, public contracts, and save compatibility unless the task requires a change

New features, new application-level services, new UI functionality, and changed responsibilities must meet mandatory architecture standards even when adjacent legacy code does not. All new and directly edited handwritten C# files must also satisfy One Type per C# File. Do not use legacy architecture as a justification for introducing new violations.

Do not rewrite working code solely to match a preferred pattern. Existing unrelated legacy code may remain unchanged. Fix architectural violations directly causing the issue or blocking compliant integration; use the smallest suitable adapter or targeted refactor. Do not create competing state owners, parallel DI containers, or parallel architectural layers.

If the project already uses R3, UniTask, MVVM, DOTween, or the custom DI container in the affected area, continue using the same approach, subject to the mandatory architecture and dependency rules.

## Scope Discipline

Start from files explicitly mentioned by the user.

If no file is mentioned, start from the smallest likely folder based on the feature name.

Expand scope only when a discovered dependency requires it.

## Change Scope Control

Implement the smallest safe, architecturally compliant change that solves the requested task.

Do not:
- Rewrite working code unrelated to the task
- Split or merge type responsibilities merely to satisfy an abstract design preference; mandatory extraction of existing type declarations and remediation of directly modified fragmented types under One Type per C# File are not prohibited
- Use handwritten partial declarations, responsibility-suffixed fragment files, or cosmetic decomposition as a substitute for actual architectural boundaries
- Create new architecture layers without direct need
- Convert code to another paradigm without direct need
- Change public APIs unless required by the task
- Change behavior outside the requested scope

Bug fixes must be surgical. Preserve existing behavior and contracts unless changing them is part of the task.

Focused structural changes, including splitting responsibilities, introducing an explicit dependency, or adapting legacy UI/state boundaries, are permitted when necessary to implement the requested feature or fix in compliance with the mandatory architecture rules. Keep them inside the discovered dependency scope. A broad legacy migration or project-wide architectural rewrite requires explicit authorization.

Feature additions must integrate with existing compliant patterns. If an existing architecture violates a mandatory rule, do not copy the violation into new code. Report relevant legacy limitations that remain outside the task scope.

## Token Discipline

Minimize context usage.

Avoid:
- Repeating file contents in the final response
- Explaining unrelated architecture
- Listing unrelated findings

Use task-relevant information without repetition, while preserving complete explanations and verification results in the final response.

## C# Code Style

Apply One Type per C# File before all formatting and naming rules below. Never use nearby multi-type, nested-type, or fragmented partial-type files as a precedent. A type and its canonical file must match; responsibility suffixes must not be used for fragments of a type.

Private fields must start with `_`.

```csharp
private int _currentAmmo;
[SerializeField] private Transform _spawnPoint;
```

Constants must use UPPER_CASE.

```csharp
private const int MAX_ITEMS = 64;
```

Interfaces must start with `I`.

```csharp
public interface IInventoryService
{
}
```

Use PascalCase for:
- Classes
- Structs
- Interfaces
- Public properties
- Public methods
- Events
- Enum names
- Enum values

Use camelCase for:
- Local variables
- Method parameters

Avoid public fields.

Prefer `[SerializeField] private` fields over public serialized fields.

Do not split method parameters across multiple lines.

Correct:

```csharp
public void Initialize(IInventoryService inventoryService, IItemFactory itemFactory, int maxItems)
{
}
```

Incorrect:

```csharp
public void Initialize(
    IInventoryService inventoryService,
    IItemFactory itemFactory,
    int maxItems)
{
}
```

Do not split `if`, `while`, `for`, `foreach`, `switch`, `using`, `lock`, lambda, or ternary conditions across multiple lines.

Correct:

```csharp
if (_inventoryService != null && _itemFactory != null && _maxItems > 0)
{
}
```

Incorrect:

```csharp
if (_inventoryService != null &&
    _itemFactory != null &&
    _maxItems > 0)
{
}
```

Keep braces on separate lines.

Do not separate consecutive member declarations of the same group with blank lines.
Use a single blank line only between different groups.

A group is defined by access modifier and member kind: constants, static fields, serialized fields, instance fields, properties.

Correct:

```csharp
public const int MAX_ITEMS = 64;
public const int MIN_ITEMS = 1;

private const int DEFAULT_ITEMS = 8;

[SerializeField] private Transform _spawnPoint;
[SerializeField] private float _speed;

private int _currentAmmo;
private int _maxAmmo;

public int CurrentAmmo => _currentAmmo;
public int MaxAmmo => _maxAmmo;
```

Incorrect:

```csharp
private int _currentAmmo;

private int _maxAmmo;

public int CurrentAmmo => _currentAmmo;

public int MaxAmmo => _maxAmmo;
```

Do not use expression-bodied members if surrounding code does not use them.

FORBIDDEN: writing any comments in code of any language, including `//`, `/* */`, `#`, XML documentation comments (`/// <summary>`), summaries, header blocks and commented-out code.

When editing a file that contains comments, do not add new ones.

## Formatting Restrictions

Do not run automatic code formatters.

Keep method parameters on one line.

Keep conditions on one line.

Keep existing indentation style.

Keep existing namespace style.

Keep existing ordering of fields, methods, and Unity lifecycle methods unless changing it is required.

## Folder Boundaries

Main scripts are under:

```text
Assets/_Source/Scripts
```

Custom DI container folder:

```text
Assets/_Source/Scripts/DI
```

The DI folder contains the existing custom dependency injection container. Its use is mandatory for application-level services under the Dependency Injection Rules. Do not refactor or replace it unless explicitly requested.

Preferred folders:

```text
Assets/_Source/Scripts/Gameplay
Assets/_Source/Scripts/Inventory
Assets/_Source/Scripts/Items
Assets/_Source/Scripts/UI
Assets/_Source/Scripts/Rendering
Assets/_Source/Scripts/Editor
Assets/_Source/Scripts/Services
Assets/_Source/Scripts/Infrastructure
Assets/_Source/Scripts/Configs
```

Do not create new top-level architecture folders without a clear reason.

Editor-only scripts must be placed in an `Editor` folder.

Runtime scripts must not depend on editor-only APIs.

## Dependency Rules

Do not add new dependencies unless explicitly requested.

Do not add:
- NuGet packages
- Unity packages
- Third-party libraries
- Editor extensions
- Reactive frameworks
- Async frameworks
- DI frameworks

Prefer dependencies already present in the project.

Use DOTween only if already present in the project.

R3 is mandatory for new UI. Verify that the existing R3 package and relevant assembly references are available before implementation. If R3 is missing or inaccessible, do not install it without explicit authorization, substitute another reactive framework, or fall back to polling. Report the blocker and request authorization where needed.

Use UniTask only if already present in the project.

Do not replace existing project dependencies with alternatives.

## Dependency Injection Rules

Mandatory for new application-level services (gameplay orchestration, inventory, equipment, persistence, session/config/resource management, shared services). Use `Assets/_Source/Scripts/DI` and existing composition root/registration patterns. Declare dependencies explicitly; prefer constructor injection for plain C#, existing injection/initialization for Unity objects. Assign appropriate lifetimes, not all singletons; dispose owned resources. Resolve at composition boundaries only.

No new service locators, global singletons, scene searches, or ad hoc service construction in MonoBehaviours/Views/ViewModels to bypass DI. Do not replace, duplicate, refactor the container, or add DI frameworks without explicit authorization. Value objects, DTOs, pure/local helpers, short-lived domain objects, and low-level implementation objects need no registration; pass them explicitly. Do not extend legacy service-location patterns.

## Global Game Configuration Policy

Use exactly one root `GameConfig` ScriptableObject as the authoritative entry point for shared runtime game configuration. Treat `GameConfig` and the configuration assets it references as read-only at runtime.

### Configuration Ownership

- **Global game configuration.** Store game-wide defaults, shared rules, and behavior intended to be consistent across locations or systems in `GameConfig` or a domain configuration it references.
- **Subsystem configuration.** Keep subsystem-specific settings with their owning subsystem. Use a dedicated ScriptableObject for complex settings where appropriate and make it reachable through the established configuration entry point.
- **Location-specific configuration.** Use explicit location overrides for values that genuinely depend on a particular level.
- **Runtime state.** Keep the current time, active weather, running effects, player state, timers, and other mutable simulation data in runtime state owned by the relevant system.
- **Save data.** Store player progress and persisted runtime state in the save system.
- **Editor and importer settings.** Keep source paths, import options, diagnostic flags, and Editor-only preferences in separate settings owned by the relevant tools.

### Required Decision Process

When developing new mechanics, the agent must independently decide which parameters belong in global configuration and which should stay local or with another owner.

1. Inspect existing configuration assets and nearby code before adding or changing a configurable value.
2. Classify the value using the ownership rules above and reuse an existing field that represents the same setting.
3. Put new shared settings in `GameConfig` or the appropriate referenced domain configuration. Keep a single source for each global value and let scene components and prefabs inherit it by default.
4. Keep source-derived constants, mathematical formulas, imported data, and internal implementation details with their owning systems unless a game-design requirement makes them configurable.
5. Preserve GUIDs, ScriptableObject references, intentional user-authored overrides, and existing runtime behavior.

### Configuration Resolution

Document and apply the following order from lowest to highest priority.

Global defaults → explicitly enabled location overrides → runtime/session overrides → restored save state where applicable.

Require explicit opt-in for location overrides. A legacy serialized value alone does not enable an override. Apply restored saves only to the applicable runtime state and keep all overrides separate from the shared configuration assets.

Use the existing DI container and reference patterns. Group complex settings by domain and keep access through explicit dependencies.

### Scope and Validation

Limit classification and migration to the task scope. For a dedicated configuration architecture task, perform a targeted project-wide audit and produce a list of migration candidates before applying changes.

For changes to runtime configuration resolution or integration, follow the Project Directories and Testing Policy. Verify shared configuration references, default inheritance, override precedence, scene reload, Play Mode, Player builds, and compatibility with existing scenes. Verify that changing a global value reaches all consumers that inherit it without reimporting locations.

## MonoBehaviour Rules

Keep MonoBehaviours small and focused.

MonoBehaviours should coordinate Unity lifecycle, Unity references, scene references, and visual components.

MonoBehaviours should not own unrelated systems.

Avoid oversized classes such as:
- `GameManager`
- `Bootstrap`
- `InventoryController`
- `PlayerController`

Do not put persistence, inventory rules, equipment rules, or business logic directly into UI MonoBehaviours.

Do not introduce hidden scene dependencies.

Use explicit serialized references for Unity objects and injected dependencies for application-level services according to the Dependency Injection Rules.

Unity-dependent systems may use MonoBehaviour adapters while preserving the simulation, state, and visualization boundaries. Do not move authoritative gameplay state into a presentation component.

## R3 Rules

R3 is mandatory for new UI; retain existing R3 usage. Use `ReactiveProperty`, read-only properties, streams, `CompositeDisposable` or equivalent. Domain systems may use explicit events/query contracts adapted at presentation boundary; no R3 in native hot loops.

- Private writable state; read-only projections for consumers; publish meaningful committed changes and initial state. Filter/batch redundant emissions without losing required events; avoid feedback loops/allocations.
- Every subscription has explicit ownership and disposal tied to actual View/ViewModel/service/operation lifetime; no duplicates on reopen/re-enable/pooling/rebind. Unity object callbacks on main thread.
- No orphan subscriptions, unowned static reactive state, destroyed Unity object captures in long-lived subscriptions, mixing R3/UniRx, alternative reactive frameworks, per-frame polling streams, or competing mutable gameplay copies.

## UniTask Rules

Use UniTask only if it is already present in the project.

If surrounding code uses UniTask, continue using UniTask.

Prefer UniTask over `Task` for Unity runtime code.

Use cancellation tokens when lifetime matters.

CancellationToken must be propagated through the entire async chain.

Cancellation should be tied to MonoBehaviour, service, or operation lifetime where appropriate.

FORBIDDEN:
- `async void` except Unity event entry points where unavoidable
- Fire-and-forget async code without explicit intent
- `Task.Run` for Unity gameplay code
- Blocking async calls with `.Result`
- Blocking async calls with `.Wait()`
- Ignoring cancellation for long-running operations
- Replacing simple synchronous code with async without a concrete reason

## MVVM Rules

MVVM + R3 is mandatory for new UI features, screens, windows, menus, interactive widgets with state/commands, and new functionality in legacy UI. Existing MVVM features retain MVVM + R3 for new bindings. Pure stateless visual controls can remain View-only under the feature; no ViewModel per label/icon.

- **View:** Unity references, TMP, visuals, layout, animation, pooling, lifecycle; R3 bindings; forwards input to ViewModel commands.
- **ViewModel:** Presentation logic, UI-local state, commands, formatting, read-only reactive projections; prefer plain C#; explicit dependencies via DI/feature composition; delegates gameplay operations/validation; owns subscriptions/cancellable work.
- **Model/services:** Gameplay, inventory, equipment, persistence, rules, validation, authoritative state; queries/commands/notifications; independent of UI/visibility.

No gameplay rules in Views/ViewModels, direct scene dependencies or visual object manipulation in new ViewModels, direct gameplay-state mutation from UI, duplicate writable domain state, or polling controller replacing MVVM. No unrelated legacy migration.

## UI Rules

New UI and new bindings in existing UI must update reactively on relevant changes, not poll. When maintaining existing bindings, eliminate polling that directly causes the issue or is replaced within the task scope; do not migrate unrelated legacy screens. Bind current and changed gameplay values (inventory, equipment, health, ammo, settings) through R3 projections from owner. Add minimal notification/adapter if legacy owner lacks it. Input -> ViewModel command -> service; display committed result/errors. Update affected controls only; avoid full rebuilds, repeated formatting, unchanged assignments. Reopen/rebind must reflect current state without duplicate subscriptions.

FORBIDDEN: constant gameplay-state polling from UI via `Update`, `LateUpdate`, `FixedUpdate`, coroutines, repeating timers, or per-frame observables. Genuine frame-dependent animation, interpolation, drag, input, and visuals are allowed. If an external/engine API only exposes sampled data, sample in owner/adapter at justified cadence; publish changes reactively.

Follow the TextMeshPro Rules. Avoid repeated string allocations in hot paths. Do not mix UI rendering concerns with inventory, equipment, persistence, or item configuration rules.

## TextMeshPro Rules

Use TextMeshPro types explicitly:
- `TMP_Text`
- `TextMeshProUGUI`
- `TMP_InputField`

Do not use legacy `UnityEngine.UI.Text` for new UI unless surrounding code requires it.

Avoid repeated string allocations in frequently updated UI.

## Editor Scripts

Editor-only scripts must be placed in an `Editor` folder.

Editor scripts must not be included in runtime assemblies.

Runtime code must not reference `UnityEditor`.

For editor tools:
- Support Undo where appropriate

## HDRP Rules

This project uses HDRP.

When working with materials or shaders:
- Prefer HDRP-compatible shaders
- Use `HDRP/Lit` for Lit materials
- Be careful with texture property names
- Do not assume URP shader properties are valid in HDRP
- Do not downgrade materials to Built-in or URP shaders unless explicitly requested
- Do not convert HDRP assets to URP unless explicitly requested

## Item, Inventory, and Equipment Rules

Keep item data, item configs, inventory logic, and equipment logic separated.

Prefer ScriptableObjects for static item data and configs.

Do not put inventory or equipment rules directly into UI components.

Each mutable item fact must have exactly one authoritative owner. Do not introduce competing writable item state between systems. Derived read-only projections, caches, save snapshots, and native buffers are allowed only with defined synchronization, invalidation, and lifetime under the Single Source of Truth rules.

Do not introduce hidden dependencies between item configs and scene objects.

Use existing compliant item and inventory patterns before adding new abstractions. Route mutations through the authoritative owner or its explicit commands; UI and visualization consume the committed state.

## Services and Persistence Rules

Services must have clear ownership and lifetime. Application-level services must use the existing DI container.

Do not introduce static services or global singletons for mutable gameplay state. Do not extend legacy static-access patterns; leave unrelated existing code unchanged.

Persistence reads and restores authoritative state through explicit owner contracts or controlled initialization. Save DTOs are snapshots, not competing runtime state.

Persistence code must be explicit and isolated.

Do not change save data schema unless explicitly required.

If save data schema changes are required:
- Preserve backward compatibility when practical
- Explain migration risks
- Mention manual validation steps

## Error Handling and Null Safety

Prefer explicit null checks where Unity references may be missing.

Do not hide errors silently.

For editor tools, report actionable errors with enough context.

For runtime code, avoid spammy logs in hot paths.

Do not add excessive logging.

Use `Debug.LogError`, `Debug.LogWarning`, or exceptions according to nearby project conventions.

## Performance Rules

Avoid allocations in hot paths.

Avoid LINQ in hot paths. Existing use is not justification for introducing additional allocations or overhead.

Avoid repeated `GetComponent` calls in update loops.

Avoid unnecessary `FindObjectOfType`, `FindObjectsOfType`, `GameObject.Find`, or tag searches.

Cache references when appropriate.

Do not optimize prematurely outside the task scope.

Apply the Low-Level and Performance-Critical Exceptions rules and the `rvm-performance` skill. Do not introduce managed reactive subscriptions, per-frame DI resolution, unnecessary virtual dispatch, or transient allocations into measured hot paths merely to satisfy an application-level pattern. Preserve explicit data ownership and validate the claimed improvement against a suitable baseline.

## Forbidden Unless Explicitly Requested

Do not:
- Add new packages
- Replace the DI container
- Add new dependencies
- Introduce large architectural rewrites for small bug fixes
- Run automatic formatters

## Final Verification

Before completing the task, verify:
- Code style matches surrounding files where compliant with mandatory rules
- Every new or directly edited handwritten C# type-definition file contains exactly one type declaration, including declarations nested inside other types and in conditional compilation branches; the filename matches the type under One Type per C# File
- Verify affected types across their compilation scope, not only within changed files: no type has multiple handwritten partial declarations, no responsibility-suffixed fragment files remain for directly modified types, and any generated partial counterpart is required by an existing tooling contract. Any decomposition creates genuinely distinct cohesive types rather than cosmetic file splits or forwarding-only wrappers
- Type extraction preserves relevant namespaces, assembly boundaries, accessibility, Unity asset GUIDs and serialized bindings, save compatibility, and network contracts; any blocking legacy violation or required broader migration is explicitly reported
- No new code comments or summaries were added; existing comments remain untouched unless changing them is required by the task
- Private fields use `_`
- Interfaces use `I` prefix
- Constants use UPPER_CASE
- Method parameters are not split across multiple lines
- Conditions are not split across multiple lines
- New and changed responsibilities comply with SOLID, DRY, KISS, and YAGNI
- Application-level services use the existing DI container, with explicit dependencies, appropriate lifetimes, and no service locator or singleton bypass
- Simulation, authoritative state, configuration, persistence, and visualization are separated; each mutable gameplay fact has a single owner and controlled write paths
- Caches, snapshots, reactive projections, and native buffers have defined ownership, synchronization, invalidation, and lifetimes
- New UI and new UI functionality follow MVVM + R3; gameplay state is updated reactively, not by constant polling; commands delegate to authoritative services
- R3 subscriptions have valid lifetimes, initial state is correct, reopening/rebinding does not create stale or duplicate subscriptions, and Unity visual callbacks run on the main thread
- Any low-level architecture exception is localized, technically justified, and validated or explicitly reported as unverified
- Legacy changes are limited to the task scope; no unrelated migrations or competing state owners were introduced
- UniTask cancellation is propagated if UniTask was used
- Editor/runtime separation is preserved
- Generated artifacts were reviewed, disposable outputs were removed under Section F, and required evidence and deliverables were preserved
- Tasks that generated artifacts include retained output size, actual reclaimed space, and cleanup record paths in the final response
- Final response is substantive, self-contained, sufficiently detailed, and uses the required format

If any applicable item fails, revise the implementation within the authorized task scope before responding. If missing dependencies, tools, or authorization block completion or validation, report the exact blocker and remaining work. Do not silently waive mandatory standards, claim unverified success, or expand into unrelated legacy migration. Documentation-only tasks require consistency checks, not Unity execution.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- A committed graphify-out/graph.json is rewritten whole on every build, so a large diff to it in a pull request is expected. Do not flag it as churn or suggest skipping the graph rebuild or its hooks.
