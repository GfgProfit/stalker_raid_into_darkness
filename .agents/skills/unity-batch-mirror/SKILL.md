---
name: unity-batch-mirror
description: Use before a headless Unity launch or batch fallback, including CLI tests, captures and compile checks, and before working in an authorized isolated project or mirror.
---

# Unity validation and launch

## Project selection

- For routine compilation, setup and tests, you MUST use Unity Open MCP in the open Production Editor. Verify the absolute project path and loaded scene state. An unavailable MCP or bridge does not establish that Unity has exited.
- Before a batch launch, including automatic tool fallback, you MUST verify the path, project lock and matching processes. Launch batch only for a free, authorized project. Routine validation REQUIRES the exact installed Unity version from `ProjectSettings/ProjectVersion.txt`. Use a different version only in a specifically authorized experiment. If the project is in use, you MUST use the open session or an explicitly authorized isolated project. You MUST NOT close the user's Editor for batch or start a second Unity instance for the same project.
- An isolated project REQUIRES explicit authorization and a user-specified path. Before tests, you MUST verify its absolute path, Unity/HDRP versions and the files relevant to the tested Production revision. Account for uncommitted changes. Report differences that affect the result. A copy without Git has no independently verified Git revision.
- The scripts in `scripts/` belong to the legacy workflow. You MUST NOT run them for routine validation. Creating, synchronizing or transferring data from a mirror REQUIRES separate authorization for the specified project. When synchronization is authorized, you MUST compare the exact files, preserve GUIDs, serialized references and unrelated changes, and exclude regenerable caches. Set `STALKER_MIRROR` to the authorized absolute project path before using a legacy mirror helper. Configure `STALKER_WORK` or `STALKER_BUILD` under the task’s Archive directory before running the legacy compiler.

## Launch and evidence

- Before launch, you MUST set the verified absolute `-projectPath` and unique absolute paths for `-logFile`, `-testResults`, captures and other outputs under the task's Archive directory, as specified in [AGENTS.md](../../../AGENTS.md). Confirm that MCP batch can comply with the same artifact constraints. If a safe launch is unavailable, run the checks that are available and report the coverage gap.
- For CLI `-runTests`, you MUST specify the relevant `-testPlatform` and a filter when needed. You MUST omit `-quit` so Unity does not exit after asset refresh and before testing. Verify that the current run completed and its XML records executed tests. Exit code 0 without fresh XML or with zero tests does not establish a pass. Exit code 2 means tests failed. Exit code 3 means TestRunner setup failed.
- For graphical tests, graphics MUST remain enabled. Use `-nographics` only for checks that do not need graphics. You MUST NOT use `WaitForEndOfFrame` in batch. Use `RenderTexture` and `ReadPixels` for frame readback.
- The final report MUST name the modified and tested projects, selected tests, status, limitations and absolute result paths. Identify any changes that exist only in the isolated project. Before moving a successful experiment into Production, you MUST establish reproducibility, identify the smallest necessary change, obtain any required authorization, and run Production regression checks while preserving GUIDs and serialized references.
