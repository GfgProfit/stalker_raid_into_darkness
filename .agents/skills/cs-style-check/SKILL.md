---
name: cs-style-check
description: "Run the project's changed-line style checker before finishing a C#, shader, or helper-script change and before an authorized git-commit-ru commit. Review type organization and uncovered rules separately."
---

# Project Style Check

You MUST run the checker from the verified project root before finishing C#, shader, or helper-script changes and before a commit under `git-commit-ru`. If `STALKER_PROJECT` is set, you MUST verify that it points to the intended repository. [AGENTS.md](../../../AGENTS.md) defines the mandatory rules.

| Command | Scope |
|---|---|
| `node .agents/skills/cs-style-check/scripts/check.js` | Added lines in the unstaged diff and entire new, non-ignored untracked files. |
| `node .agents/skills/cs-style-check/scripts/check.js --staged` | Added line numbers from the staged diff. REQUIRED before committing. |
| `node .agents/skills/cs-style-check/scripts/check.js <files>` | Complete files given by paths relative to the project root. You MUST quote paths containing spaces. |
| `node .agents/skills/cs-style-check/scripts/check.js --all` | All tracked `.cs` files outside `Assets/Plugins` and `Assets/TextMesh Pro`. Older files can contain existing violations. |

Exit code 1 indicates errors. Lines with `warning:` do not affect the exit code. The script reads files from the working tree even in `--staged` mode. If a file has both staged and unstaged changes, you MUST inspect the actual staged content separately with `git diff --cached` or `git show :<path>`. When code changes are already staged, the final review MUST cover both parts of the diff. You MUST NOT change staging to run the check. The working-tree diff can contain other people's edits. You MUST identify the lines relevant to the task and leave unrelated edits to their owners.

## What the script checks

| Check | Details |
|---|---|
| `comment` | Heuristics for `//`, `/* */`, and `///` in supported `.cs`, `.shader`, `.hlsl`, `.js`, `.sh`, and `.ps1` files. For `.sh` and `.ps1`, the script also checks lines containing `#` except shebangs. New comments are FORBIDDEN in all project source languages and Skill scripts. Explanations and usage MUST go in documentation. |
| `condition split across lines` | C# `if`, `else if`, `while`, `for`, `foreach`, `switch`, `using`, and `lock` conditions with unclosed parentheses. |
| `parameters split across lines` | A C# line ends in `(` or `,` with an unclosed parenthesis. |
| `brace must be on its own line` | Opening braces at the end of C# lines that the checker recognizes. |
| `const must be UPPER_CASE` | Constant casing. |
| `private field must start with _` | Private and `[SerializeField]` fields. The checker skips uppercase `static readonly` fields. You MUST still apply the project rules to fields the checker misses. |
| `blank line between members of the same group` | Recognized adjacent fields or properties in the same access and modifier group. |
| `warning: public mutable field` | In ordinary cases you MUST use a property or `[SerializeField] private`. Public mutable fields are allowed for `JsonUtility` DTOs. |

## Required manual checks

- The checker uses text heuristics. You MUST check for missed violations and false positives, including functional `#` constructs such as PowerShell `#requires` and syntax outside the supported extensions. You MUST keep valid directives and explain the false positive in the check result. You MUST correct actual violations within the task scope.
- You MUST check One Type per C# File across the affected compilation scope under `AGENTS.md`, including conditional and nested declarations, `partial`, filenames, and the compatibility of extracted types. You MUST check naming, lambda and ternary conditions, fields, member and lifecycle method ordering, and expression-bodied members against surrounding code.
- You MUST verify Editor folders and assemblies and confirm the absence of runtime references to `UnityEditor`, unnecessary `Update()` methods, and allocations or LINQ on hot paths. You MUST also verify disposal of R3 and event subscriptions.
- You MUST keep changes within the task scope and use the five `AGENTS.md` final-response sections in the language required by the project and the user's request.
