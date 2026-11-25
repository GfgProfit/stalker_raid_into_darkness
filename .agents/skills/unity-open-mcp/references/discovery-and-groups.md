# Discovery and groups

Use `capabilities(tool_name: "unity_open_mcp_component_modify")` for one full schema, example and lifecycle contract. Browse with `query`, `group`, `tag`, `available`, `active`, `route`, or `mutating`, plus `page_size` and `cursor`. Availability is null when the live inventory is unknown; batch-route tools instead report whether an editor for the project's Unity version resolves for a headless spawn.

Use `manage_tools(action: "activate_for", intent: "…")`; its `tools` list contains schemas for newly activated tools. If your client ignores list changes, use `manage_tools(action: "invoke", tool_name: "…", arguments: {...})`. This uses the target tool’s normal route and protections; you MUST NOT put recovery calls in a live batch.

Canonical locators are `asset_path`, `game_object_path`, `component_type`, and patch `property_path`. Deprecated aliases produce notes. You MUST NOT mix an alias and its canonical key. You MUST inspect the exact schema before relying on a key.

## Version drift and updates

Use `unity-open-mcp status` to compare the running server and bridge versions.
`unity-open-mcp update --check` checks the MCP npm release only: exit `0` means
current, `10` means an update is available, and `11` means npm and the GitHub
fallback were unreachable or invalid. `unity-open-mcp update` can update a
global or project-local npm install; under `npx` it prints the pin to change.
It intentionally does **not** edit Unity package pins, client configuration, or
project prose. An update is a separate operation from diagnosis. Server, tooling, package or client changes REQUIRE explicit request or authorization. For an authorized Unity package upgrade, you MUST move the Bridge and Verify packages together through the bridge Updates UI or the setup/manual package path, restart the MCP client and confirm with `ping` / `status`.

For an already-installed open project, activate `typed-editor` and call
`unity_open_mcp_upgrade` with its default `dry_run: true`. You MUST review every file and skip reason. Repeat with `dry_run: false` only when requested. Home configs
are rewritten only when their ownership markers belong exclusively to this
project; embedded/`file:` bridge installs preserve their package pins. The tool
returns before a possible UPM reload, so poll `bridge_status` and re-ping after
Unity settles, then remind the operator to restart the MCP client.


For a human Search handoff, use `manage_tools(action: "editor_search", search_text: "Player", asset_type: "Prefab")`. It returns the query without UI changes; you MUST use `open_ui: true` only when opening or focusing the Search window is explicitly wanted (during a project job it returns `opened: false`, `reason: "job_busy"`; hand over the query instead). Keep structured investigation on `search_assets` / `find_references`.
