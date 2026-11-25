---
name: csharp-style
description: "Apply project C# file organization, naming and formatting before creating or editing handwritten C#, including legacy nested or partial types, and verify the affected compilation scope before finishing."
---

# C# structure and style

This skill is REQUIRED before creating or directly editing project-owned handwritten C# in Runtime, Editor, tests, networking, persistence, rendering, importers, or Burst/Jobs. [AGENTS.md](../../../AGENTS.md) defines the rules and precedence. Source organization MUST take precedence over formatting. For any changes to C#, shaders, or helper script code, you MUST follow [cs-style-check](../cs-style-check/SKILL.md) for mechanical verification.

## One Type per C# File

- A handwritten type-definition file MUST declare exactly one namespace-level type. Each type MUST have exactly one complete handwritten declaration in one file. This includes class, struct, interface, enum, record class/struct, and delegate declarations regardless of accessibility, modifiers, or kind. DTOs, requests/responses, serializable wrappers, save/config models, events, helpers, jobs, and private implementation types follow the same rule.
- A type's filename MUST match its simple name, such as `InventoryResponse.cs` or `Result.cs` for `Result<T>`. For genuinely distinct types with the same name, you MUST use different subdirectories or an explicit arity suffix such as `Result.Generic1.cs` where Unity script binding allows it. Unity's type/file-name matching MUST be preserved.
- Nested types, multiple namespaces or conditional branches used to hide extra declarations, and handwritten partial fragmentation are FORBIDDEN. Files with role suffixes such as `PlayerController.Input.cs`, `PlayerController.Lifecycle.cs`, and `PlayerController.Part1.cs` MUST NOT store fragments. Regions MAY organize one cohesive type within its file. They do not split responsibilities.
- A single handwritten `partial` declaration is permitted only when an existing compiler, source generator, or tooling contract requires generated counterparts. You MUST verify that contract. New handwritten fragments, new generation to evade the rule, and hand edits to generated or third-party files for style compliance are FORBIDDEN. Assembly/module attributes, global usings and other legitimate non-type directives require no artificial type.
- Members, constructors, properties, fields, methods, local functions, and events using existing delegates MUST remain in their owning type. Generic parameters are not types declared by a file. Related declarations MUST stay in the owning feature/domain folder without new global DTO/helper folders, forwarding-only wrappers or layers.
- You SHOULD prefer the existing folders under `Assets/_Source/Scripts`: `Gameplay`, `Inventory`, `Items`, `UI`, `Rendering`, `Editor`, `Services`, `Infrastructure` and `Configs`. A clear reason is REQUIRED for a new top-level architecture folder.

## Safe legacy changes

1. Before choosing a change, you MUST inspect the whole directly edited file, all conditional branches, assemblies, and all declarations of each affected type. When directly touching a fragmented type, you MUST locate all handwritten partial declarations even when filenames do not match.
2. You MUST extract any existing nested or additional type to its own namespace-level file with the narrowest compatible accessibility and consolidate a cohesive fragmented type into the canonical file. For genuinely independent responsibilities, you MUST decompose into cohesive, separately owned types with explicit dependencies, composition and lifecycle ownership. Filename fragments, wrapper-only classes or extensions that hide a god object, and splitting for line count cannot satisfy SRP.
3. You MUST preserve behavior, public contracts, accessibility, namespaces where compatible, assembly boundaries, Unity `.meta` GUIDs, MonoBehaviour/ScriptableObject bindings, serialization and `[SerializeReference]` identities, save compatibility, and network contracts. You MUST resolve references before moving or removing script assets and keep unrelated legacy types outside the change.
4. When safe extraction or consolidation needs broader scope, approval, or a compatibility migration, you MUST report the specific blocker. A small fix cannot waive the rule or authorize an unsafe migration. No performance or architecture exception requires combining handwritten declarations, introducing indirection, or adding allocations.

## Style

- Private field names MUST start with `_`. Constants MUST use `UPPER_CASE`, and interfaces MUST have an `I` prefix. Classes, structs, interfaces, public properties and methods, events, and enum names and values MUST use PascalCase. Local variables and parameters MUST use camelCase.
- You MUST avoid public fields and preserve existing bindings and serialization. You SHOULD prefer `[SerializeField] private` for Unity fields.
- You MUST keep method parameters and `if`, `while`, `for`, `foreach`, `switch`, `using`, `lock`, lambda and ternary conditions on one line. Braces MUST be on separate lines in Allman style.
- Consecutive members MUST be grouped by access and member kind (constants, static fields, serialized fields, instance fields, properties). There MUST be zero blank lines within each group and one blank line between groups.
- You MUST follow the nearby compliant indentation, namespace style, and ordering of fields, methods and lifecycle callbacks. You MUST use expression-bodied members only where surrounding code uses them. Running formatters is FORBIDDEN.
- Adding source comments, including XML documentation, header blocks, explanatory summaries, or commented-out code, is FORBIDDEN. You MUST preserve existing comments unless the task requires a change. Functional syntax such as `#if`, `#else`, `#endif`, `#define`, `#pragma`, `#include`, `#nullable`, or a shebang remains allowed. Explanatory text MUST go in documentation or Skills. Required functional metadata MUST stay in its owning configuration.

## Verify

You MUST check the final source across the full affected compilation scope. You MUST count all declarations including nested or conditional types, confirm matching filenames, verify only one handwritten declaration per type with no suffix fragments, and establish any required generated partial counterpart. For extracted types, you MUST verify references and the compatibility requirements above. The mechanical check complements this review and cannot establish declaration or serialization correctness. Where the checker is incomplete, you MUST manually review interface names, conditions and parameters, source comments versus functional directives, and relevant Editor/runtime boundaries.
