---
name: unity-open-mcp
description: Use for Unity Editor work, asset inspection or edits, Unity validation, and live Editor recovery in Raid Into Darkness. Covers Unity Open MCP, scoped gates, offline asset reads, tests, and authorized batch fallback.
---
# Unity Open MCP

For routine work and validation, you MUST use Unity Open MCP in the Production Editor. You MUST follow the canonical project, authorization, dependency and Archive rules in [AGENTS.md](../../../AGENTS.md), including its task-specific skill routes.

## Select and protect the target

- Before using Unity tools, you MUST verify the absolute project path, Unity version, live Editor and scene state. You MUST preserve the current project and user work, and keep scene, prefab, material, shader and settings changes within the requested scope. Experimental tests that may affect important Production assets REQUIRE an explicitly authorized isolated project at the user-specified path.
- You SHOULD prefer targeted read-only investigation and typed operations. Use only the needed tool groups, compact output and scoped validation. Editor tools MUST stay in Editor assemblies and support Undo where appropriate.
- When the live bridge is unavailable, you MUST diagnose it with bridge status, offline compile diagnostics and matching process evidence. When the MCP client cannot access the tools, use safe reads of the process, current Editor log or authoritative files as applicable. Neither condition proves the target project free. You MUST continue independent read-only or static checks and report any unavailable compile, test or live verification with the concrete blocker.
- For a needed headless route, you MUST follow [unity-batch-mirror](../unity-batch-mirror/SKILL.md) and verify that the target is free before any call that might launch headless Unity. You MUST NOT close the user's Editor for a routine check or create or synchronize a project copy for routine validation.

## Core workflow

1. Discover a tool with `unity_open_mcp_capabilities(tool_name: "unity_open_mcp_component_modify")`, or browse with `query` and `page_size`. Exact lookup returns one complete schema and example. You MUST NOT request the full unpaged catalog.
2. Activate the needed tools with `unity_open_mcp_manage_tools(action: "activate_for", intent: "…")`. If list refresh is unavailable, use its `invoke` action with `tool_name` and `arguments`.
3. You MUST confirm the selected Editor with `ping` / `bridge_status` and use the instance lock’s port. A hardcoded port is FORBIDDEN. Offline reads and `read_compile_errors` do not require a live bridge.
4. You SHOULD prefer typed tools. You MUST use canonical `asset_path`, `game_object_path`, `component_type`, and `property_path` from the exact schema. Start compact and page reads; expand only the relevant slice.
5. You MUST scope mutations with nonempty `paths_hint`, inspect `gate.delta` and `agentNextSteps`, and validate and test the affected scope. Mutation success does not establish project safety.
6. After a lost response, socket reset, or timeout, you MUST inspect post-state before retrying a mutation, even if the bridge restarted. If `delta` returns `validation_incomplete`, you MUST resolve `error.rulesFailed` and create a fresh checkpoint. When applying a fix, you MUST copy the scan's `issueId` unchanged into `apply_fix.issue_id`. Use `profile: "full"` to retrieve issues.

## Non-negotiable safety

- You MUST NOT launch a second Unity instance for the same project. A live PID with an unavailable bridge REQUIRES recovery.
- You MUST preserve dirty scenes and lifecycle guards. Destructive MCP actions and guard bypasses REQUIRE explicit authorization. Pure `read_only` snippets MUST NOT write, refresh, compile, switch scenes, or change play state. The flag asserts caller behavior and provides no sandbox.
- While the Editor is reachable, you SHOULD prefer typed live edits. You MUST NOT directly edit the YAML of an open or dirty scene or asset. If direct file repair is necessary, you MUST identify the offline-file route, preserve GUID/fileID links, check the intended scene, reimport or reserialize, then run scoped validation.
- You MUST run one test request at a time. `run_id` identifies the started run. You MUST NOT use it as a poll handle, retry an unresolved run, or put server-polled tests or recovery tools in a live batch.
- If a top-level fix reports `rollback_unavailable` because backup failed, you MUST resolve the temporary-directory space or file-permission problem before retrying. You MUST NOT bypass the refusal.
- Batch arguments are validated before step zero. All-read batches need no mutation scope; mixed batches REQUIRE aggregate scope. A failed mutation with a skipped gate has not passed validation.

## Required references by action

You MUST read only the references that apply to the current action.

- Before browsing or activating unfamiliar tools: [Discovery and groups](references/discovery-and-groups.md).
- On compile errors, reloads, Safe Mode, or a live PID with no bridge: [Compile and Safe Mode](references/compile-and-safe-mode.md).
- Before scene switches, play transitions, snippets, or choosing live/batch/offline routes: [Routing and lifecycle](references/routing-and-lifecycle.md).
- Before batching, interpreting a failed gate, or applying fixes: [Batch and gates](references/batch-and-gates.md).
- Before file-level repair, YAML edits, or offline asset investigation: [YAML and offline work](references/yaml-and-offline-work.md).
- Before testing, compiling for validation, capturing results, or integrating an isolated result: [Senses and tests](references/senses-and-tests.md).

### Project-owned commands

Use `unity_open_mcp_project_commands(action="list", query="…", limit=20)` to
find project commands, paging with `nextOffset`. You MUST describe the exact catalog id
with `action="describe", id="project.owner.command"` for its schema, safety
metadata and availability diagnostics. This tool stays visible regardless of
active groups and reads fresh metadata after recompilation. Invoke with `action="invoke", command_id="project.owner.command", args={...}`.
The server revalidates a fresh schema; optionally pin `schema_version` from describe.
You MUST supply `paths_hint` for mutations without declared paths and keep gate and dirty-scene options outside `args`. Check `mutation.output.result` and the normal gate and lifecycle envelope. After a timeout or reload, you MUST inspect post-state before retrying. For an `async` command, you MUST use jobs with the exact id and invocation envelope in job `args`. You MUST NOT bypass unavailable commands with reflection or nest project commands in `batch_execute`.

## Asynchronous job observation

Use the always-visible `unity_open_mcp_jobs` for explicitly supported asynchronous
operations. `start` REQUIRES `tool_or_command`, adapter `args`, and an
`idempotency_key`. Supported targets are `unity_senses_run_tests` (ordinary test
filters, no caller `run_id`, cancellation unsupported) and available async project
commands (`args: {args: {...}, gate, paths_hint}`). You MUST read terminal project-job gate results even on failure or cancellation. You MUST NOT retry an unsupported target as an arbitrary background tool.

After start, you MUST call `status`, use bounded `wait` calls until the job is terminal, then inspect the retained result and gate. A queued acknowledgement records acceptance only. You MUST keep the job id and the same project/port/agent identity for `status`, `wait`, `cancel` and `list`. A wait timeout leaves execution running. You MUST reuse the same key and arguments after a start response is lost. Cancellation is cooperative and may be unsupported. `orphaned` is an unknown outcome. You MUST inspect operation evidence before retrying. Records and keys expire 30 minutes after completion and are lost
on server restart. `job_busy` means a project job owns the Editor: mutations, Editor-state
writes, test starts and every `execute_csharp` (even `read_only: true`) are refused.
The job owner MUST wait on `error.job.jobId` via jobs. Use typed reads while waiting. After the job is terminal, you MUST NOT retry more than once. See [Routing and lifecycle](references/routing-and-lifecycle.md).
