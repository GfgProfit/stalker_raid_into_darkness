---
name: archive-management
description: Plan, retain and safely clean Raid Into Darkness diagnostic outputs, reports and temporary Unity Player builds under the project Archive. Use when generating these artifacts or maintaining Archive within authorized scope.
---

# Archive Management

You MUST use the Production and Archive roots specified in the root AGENTS.md. That file controls permissions and project scope. This skill is REQUIRED before generating diagnostic outputs or selecting cleanup targets. Production code, assets, configuration, required Unity fixtures, essential regression tests and their `.meta` files MUST remain in the owning project.

## Plan and place outputs

- You MUST give each task an Archive directory and use unique run subdirectories or names to prevent overwriting existing results and backups. You MUST configure output paths before starting the tool and put reports, test results, logs, PNG/EXR/CSV/JSON, benchmark data, profiler captures, CPU/GPU readbacks, temporary exports and Player builds there. Diagnostic folders are FORBIDDEN inside Production or isolated Unity projects.
- Before bulk captures, profiling, benchmarks or builds, you MUST check free space and estimate output size. You MUST limit resolution, frame count, the capture matrix and retained runs to the question. You MUST reuse a validated Player build when code, assets, build settings and configuration match.
- You MUST keep the final report, test results, necessary CSV/JSON and representative PNG evidence. You MUST capture EXR, RGBA32F, full Profiler `.raw` or per-frame readbacks only when precision or timeline is necessary. You MUST retain the smallest set of raw evidence that lets someone reproduce or investigate the result. PNG does not preserve HDR values.
- If a tool genuinely requires temporary files in Production, you MUST remove them after validation only if this task created them, they are no longer required and the operation is authorized. This is separate from Archive disposal.

## Decide what may be removed

- For the current task, you MUST automatically remove superseded or completed disposable builds, abandoned failed-run output, regenerable caches, duplicate delivery packages, intermediate captures and temporary exports once unused. Where needed, you MUST stop only the task's own processes. Before removing build binaries, you MUST extract and retain reports, manifests, metrics, screenshots and source or material backups. A build directory's parent may contain unique evidence. You MUST NOT delete a whole task folder merely because it contains a build.
- You MUST preserve originals, required fixtures, `.meta` files and GUID references, final reports, unique source/asset and rollback backups, evidence needed for unresolved checks and requested deliverables or builds while required. If ownership, reproducibility or use is uncertain, you MUST retain the item and report the uncertainty.
- In an authorized revisit to a known historical task directory, you MUST select only redundant or reproducible bulk. You MUST record that originals still exist and supported regeneration is available, or verify an exact retained duplicate. You MUST confirm there are no active consumers. Age, name, extension and a successful later run are insufficient proof. You MUST treat unique EXR as evidence if its precision or provenance is needed. Separate explicit authorization is REQUIRED for broad historical maintenance. You MUST NOT scan all of Archive for a routine task.

## Verify targets and report

1. You MUST resolve the Archive root and each selected target to absolute paths, verify containment by path components, and reject reparse points, symlinks and junctions anywhere in the traversal, including a selected directory's descendants before recursive removal. You MUST use explicit file selections or verified task-owned artifact directories. You MUST NOT pass an unchecked computed path to a recursive command.
2. You MUST exclude artifacts used by other tasks or people. This cleanup permission applies exclusively to disposable generated artifacts under Archive. It does not cover Production, any isolated or legacy project, Git data, user deliverables or unique backups. Git cleanup is FORBIDDEN for this procedure.
3. You MUST record paths, reasons, proof for historical selections, bytes actually removed and any failures. You MUST remove only empty directories left by the current cleanup, measure retained bytes and final free space, and save the cleanup record in the task's Archive directory. The final report MUST include absolute paths, retained size and actual reclaimed space. If deletion is prohibited for the task, you MUST respect the limit and record that no bytes were removed.
