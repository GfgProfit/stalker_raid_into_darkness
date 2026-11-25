# Routing and lifecycle

## Lifecycle & scene safety


| Policy                | Meaning                                                                             | Tools                                                                                                                                                                                                                              |
| --------------------- | ----------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `none`                | Read-only, returns immediately                                                      | `ping`, `find_members`, `validate_edit`, `checkpoint_create`, `delta`, `find_references`, `dependencies`, `scan_paths`, `read_asset`, `search_assets`, `list_assets`, `editor_status`, `read_console`, `screenshot`, `profiler_*`, `spatial_query`, `visual_compare` |
| `editor_settle`       | Mutating; bridge waits for asset refresh/serialization to finish                    | `apply_fix`, `reserialize`                                                                                                                                                                                                         |
| `restart_then_settle` | Mutating; may trigger domain reload; bridge blocks until compile finishes (cap 60s) | `execute_csharp`, `invoke_method`, `execute_menu`, `compile_check`                                                                                                                                                                 |
| `custom_confirmation` | Async; returns immediately, result via external completion signal you poll          | `run_tests`                                                                                                                                                                                                                        |


> The table above is the bridge's settle-timing surface (how long it blocks before returning). For the **recovery-axis** taxonomy — `none` / `compile-reload` / `modal-dialog` / `scene-dirty` / `process-stale`, describing what to do when a call fails or stalls — read `capabilities.tools[].lifecycle` (+ `lifecycleNote`) per tool, and `capabilities.lifecycleBlock` for the full class table. See the docs §Lifecycle policy for the `compile-reload` agent limits (notably `compile_check` is batch-only and returns `editor_instance_locked` when a live Editor holds the project lock).

**Active-scene dirty guard.** Before a `restart_then_settle` operation, the bridge preflights loaded scenes. Unsaved scenes cause `scene_dirty` with `dirtyScenes[]` and `agentNextSteps`. You MUST preserve the user's work and inspect the affected scenes before choosing a route. Use a non-disruptive path if possible. Saving MUST be limited to authorized changes. Discarding with `EditorSceneManager.RestoreSavedSceneState()` or bypassing a dirty guard with `ignore_scene_dirty: true` REQUIRES explicit authorization for that action and scene. The bypass parameter is available on `execute_csharp` / `invoke_method` / `execute_menu` / `scene_create` / `scene_open` / `editor_set_state` / `build_set_target` / `build_set_defines` / `settings_set_player`. If none of the permitted routes preserve existing work, you MUST report what remains blocked.

`scene_create` / `scene_open` are guarded only in their default Single mode (it closes every open scene without saving); `mode: "additive"` keeps open scenes and skips the guard. `apply_fix`, `reserialize`, and the other scene mutators (`scene_save` / `scene_unload` / `scene_set_active` / `scene_focus` / `sceneview_set_camera`) are **not** guarded. You MUST check unsaved state and task authorization before these actions, including additive scene changes. You MUST NOT change the active project or scene unexpectedly.

**Power-tool deny heuristic.** `execute_csharp` / `execute_menu` are blocked from destructive patterns by default (`EditorApplication.Exit`, `Application.Quit`, `AssetDatabase.DeleteAsset`, `BuildPipeline.BuildPlayer`, `File/Quit`, `TestRunnerApi`). Refused calls return `error.code = "denied_by_policy"` (csharp) or `"menu_blocked"` (menu) with the matched pattern + alternative. Use a scoped typed alternative when available. For an explicitly authorized bypass, the tool REQUIRES both `gate: "off"` and `confirm_bypass: true` and audits the action. Tool flags and audit records grant no permission. `File/Quit` remains FORBIDDEN. `execute_menu` also tells `menu_not_found` (no item at that path — fix the path) from `menu_disabled` (the item exists but its validate function rejected the current Editor state, e.g. nothing selected — change the selection/state, not the path).

`execute_csharp` runs on Unity's main thread. You MUST NOT block it. The snippet executes synchronously inline on `EditorApplication.update`, so any blocking primitive wedges the editor **unrecoverably**: `WaitOne` / `.Result` / `Thread.Sleep` / `while(!done)` waiting for a callback, and driving `TestRunnerApi` (which delivers its callbacks on the same main thread), all deadlock. The HTTP timeout fires on the worker thread and **cannot self-heal a stuck main thread** — no further editor tick runs, so a confirmed permanent wedge may require authorized restart by the operator. On timeout, you MUST follow `agentNextSteps` and verify state with `editor_status` / `bridge_status` before retrying or raising `timeout_ms`. For tests, you MUST use `unity_senses_run_tests` (asynchronous). For other operations, you SHOULD prefer a typed tool. Any snippet MUST be non-blocking (fire-and-forget with a follow-up poll).

## Routing rules

You MUST use `capabilities.routePolicy` and `batchCapable` as the source of truth. `batchCapable` is about the **headless batch-spawn fallback** only (`capabilities.routing.perToolFlagMeaning` says so inline) — it does not decide what may be a nested `batch_execute` step.

- **Live is the default** — when the bridge is connected, most tools route to `POST /tools/{name}` on the Editor.
- **Batch fallback** — spawns headless Unity (`-batchmode`) **only** for `batchCapable: true` tools when the live bridge is unavailable. Before calling any tool with this route, you MUST verify the project and launch conditions in the batch skill linked from the entrypoint. A dead bridge does not mean the project lock is free. `find_members`, `execute_csharp`, `invoke_method`, and the allowed `execute_menu` subset can use this route; mutating power tools do not get the live gate/checkpoint flow in headless mode.
- **Exact Editor guard** — before a headless spawn, the server reads `ProjectSettings/ProjectVersion.txt` and requires that exact installed Unity. `unity_version_not_installed` / `unity_version_mismatch` mean nothing was spawned. Select an already installed exact Editor or report the dependency blocker (no server restart needed). `UNITY_OPEN_MCP_ALLOW_VERSION_MISMATCH=1` is a tool capability for deliberate, authorized version-change work. You MUST keep the exact Editor guard for routine Production validation because another version can rewrite package/project metadata during load.
- **Senses are live-only** — `run_tests`, screenshots, profiler, console, spatial queries have no batch form.
- **Offline reads** (`list_assets`, `find_references`, `read_asset`, `search_assets`) parse the project from disk and never need Unity. Coverage: text-serialized Unity YAML (`.prefab`/`.unity`/`.asset`/`.mat`/`.controller`/`.anim`/`.playable`/`.preset`/`.spriteatlas`/`.terrainlayer`/`.vfx`) **plus** JSON assets not covered by YAML-only parsers (`.asmdef`, `.shadergraph`/`.shadersubgraph`). `read_asset` also reconstructs the full hierarchy offline, parses prefab-variant overrides (matching `prefab_get_overrides`), and surfaces `integrity[]` signals (missing refs, missing scripts, malformed JSON, orphaned prefab instances) on the response — act on them before a `validate_edit` round-trip.
- **`dependencies` is live-only** — it reuses the verify `Dependencies.Scanner` (forward) + `ReferenceGraph` (reverse), both of which call AssetDatabase. No offline form.
- **`compile_check` is always batch** — spawns a fresh headless Unity that recompiles from scratch, even when the live bridge is up.
- **Verify scans (`scan_all`, `baseline_create`, `regression_check`) are always batch** — they run in the headless verify package and are not registered on the live bridge, so even with the bridge up they spawn fresh Unity (`_route.fallbackReason: "verify_always_batch"`). Expect `editor_instance_locked` when a live Editor holds the project lock. For routine checks with the user's Editor open, you MUST use live `validate_edit` / `scan_paths` for scoped verification and report the limited coverage.


Snippets import `Object = UnityEngine.Object` automatically unless the caller supplies an Object alias. Snippets run in a separate assembly and can access public APIs. Use explicit reflection for inspection of internals; assembly-access bypass is not supported.

## Session-owned jobs

`unity_open_mcp_jobs` stays local and visible while Unity reloads or disconnects.
You MUST use it only when an operation has an explicit job adapter. Declarations alone do not enable a job. `wait` observes for at most 30 seconds without owning execution.
A lost connection does not establish failure. `orphaned` REQUIRES operation-specific evidence before any retry. A `running` job with lifecycle `disconnected` is still
being observed through a busy or unreachable Editor; keep waiting.
`cancel_requested` is not cancellation confirmation.
Terminal records and idempotency keys survive for 30 minutes in the same server
session, but not across a server restart. You MUST keep routing and agent metadata stable.
While a project job runs, conflicting calls return `job_busy` with `error.job`
(`jobId`, `commandId`, `phase`) and `agentNextSteps`. `execute_csharp` is refused
even with `read_only: true`: the bridge cannot verify a snippet writes nothing.
The starting agent waits with that `jobId` (others get `job_not_found`); use typed
reads meanwhile. After a terminal state, you MUST NOT retry more than once.


For project commands, you MUST describe the exact command before invoke/start. After async start, you MUST call status and bounded wait, then inspect terminal gate results even on failure or cancellation. Scope, deny and dirty-scene policies still apply. You MUST NOT use reflection to bypass an unavailable declaration. Builds/imports/packages/bakes
are unsupported job targets.
