---
name: gra-research
description: REQUIRED whenever a task needs original S.T.A.L.K.E.R. Anomaly 1.5.3 behavior, data, assets, settings or formulas from the GRA 2.5 MO2 modpack. Covers mod ownership, winning files, configs, DLTX-aware LTX, localization, Lua, models, textures, sounds, animations, player/MCM settings and xray-monolith engine sources.
---

# Original GRA research

For GRA behavior, the source is the user's Anomaly 1.5.3 installation with the GRA 2.5 modpack in MO2. You MUST identify the winning file and name its source mod in the response. You MUST NOT invent values or formulas or use tutorials or OpenXRay as the default source. For Raid Into Darkness implementation work, you MUST apply the code routes in [AGENTS.md](../../../AGENTS.md). GRA evidence establishes original behavior. Unity verification establishes project behavior.

Activation examples include “как в ГРА”, “как в оригинале”, “найди в модпаке/в моде”, “какой мод отвечает за”, and “посмотри мои настройки в игре/MCM”.

## Source locations

- Unpacked base game (`gamedata`) is at `D:\GRA2.5\Anomaly 1.5.3 up\tools\_unpacked`.
- MO2 is at `D:\GRA2.5\MO2` (`mods`, `profiles`, `overwrite`). `selected_profile` in `ModOrganizer.ini` identifies the active profile. Before resolving files, you MUST recheck the selected profile and enabled mod count using `profile`. If the user names another profile, you MUST inspect it and report which profile you used.
- Player console settings (`fov`, `hud_fov`, `mouse_sens`, `g_nearwall`, and others) are in `D:\GRA2.5\Anomaly 1.5.3 up\appdata\user.ltx`.
- MCM settings (`fddar/...`, `swm_legs/...`, `video/player/...`) are in `D:\GRA2.5\MO2\overwrite\gamedata\configs\axr_options.ltx`.
- `bin\AnomalyDX11.exe` and its `.pdb` are from **xray-monolith** (themrdemonized). You can inspect symbols in the executable and PDB with `grep -a`.
- The resolver does not index `.db0` archives. If you unpack one, you MUST run `"D:\GRA2.5\Anomaly 1.5.3 up\tools\converter.exe" -unpack -xdb <file>` in a dedicated task directory outside the source installation. For Unity tasks, you MUST use the canonical Archive in `AGENTS.md`. Base `material_pairs.ltx`, for example, exists only in `db/mods/00_modded_exes_gamedata.db0`.

## MO2 resolution rules

- In `modlist.txt`, `+` enables a mod and `-` disables it. **The first line has the highest priority.**
- The winning file comes from `overwrite`, then the mods in `modlist.txt` order, then the base game. Disabled mods are ignored.
- A file qualifies as used in the game only if it wins resolution and a config or script actually references it. Shadowed copies do not qualify.

## LTX and DLTX

- The root is `configs/system.ltx`. Resolve `#include` relative to the including file's directory. The `*` wildcard is supported.
- `configs/mod_system_*.ltx` files are merged after `system.ltx` in filename order.
- Section directives:
  - `![sec]` modifies an existing section and skips absent sections.
  - `@[sec]` creates or modifies a section.
  - `!![sec]` and `[sec]` replace a section.
- Key directives: `!key` deletes a key, `>key` appends to a list, and `<key` removes from a list.
- With `[sec]:parent1,parent2` inheritance, the last parent has key lookup priority. Encoding is cp1251. The comment markers are `;` and `//`.
- Separate `ini_file(...)` inputs, such as `items\items\headgear_blacklist.ltx`, are outside `system.ltx`. You MUST read them directly.

## `scripts/gra.js`

When using the CLI, you MUST run `node .agents/skills/gra-research/scripts/gra.js <command>` from the project root. Commands operate on winning files.

| Command | Result |
|---|---|
| `profile` | Profile and mod count |
| `winner <rel>...` | All candidates with MD5 and size, with the winner marked |
| `find <regex> [subdir] [.ext]` | Virtual paths among winning files |
| `grep <regex> <subdir> [.ext]` | Search winning files, such as `grep "add_pp_effector" scripts .script` |
| `get <section> <key>` | Resolved value with DLTX and inheritance |
| `dump <section>...` | All section keys, parents, and definition file |
| `sections <regex>` | Sections matching a pattern |
| `strings <id> \| <prefix*>` | Russian strings from `configs/text/rus` |
| `motions <name>...` | OMF containing the motion for the HUD, hands, or body |
| `ogftex <rel\|path>...` | OGF textures and shaders from nested chunks |
| `settings <regex>` | Matching lines from `user.ltx` and `axr_options.ltx` |

The module exports `candidates`, `winner`, `winners`, `ltx`, `strings`, `motions`, and `ogfTextures`. For a one-off script outside Production, you MUST `require` this module and reuse its logic.

## Engine sources

- `sh .agents/skills/gra-research/scripts/monolith.sh src/xrGame/HudItem.cpp` downloads the file into a cache and prints its path. For a Unity task, you MUST set `MONOLITH_CACHE` to an absolute cache path inside the task's Archive directory before running it.
- `sh .agents/skills/gra-research/scripts/monolith.sh search <symbol>` runs `gh search code` against the repository.
- You MUST verify engine behavior against Monolith. For example, Monolith uses `hud_fov * 83°` independently of `fov` when rendering the HUD. Items can also define per-section `hud_fov` and nearwall settings.

## Known mod map

You MUST recheck the winning files. Mod priority can change.

- `[Анимации] FDDA Redone 1.4.1` provides HUD animations for item pickup and for equipping outfits, helmets, gas masks, backpacks, patches, and plates. Relevant entries include the `lam2` queue, the `zzzz_liz_fdda_redone_ui_inventory` inventory hooks, `*_headgear_hud`, `outfit_animation_*`, and the `deimos_short.ppe` darkening effect.
- `[Модели] Swm_Visible_Body` provides the first-person body (`<outfit>_legs`, `swm_legs.script`).
- `[Руки] T.H.A.P. Rework` and other hand mods use `player_hud_section` → `[actor_hud_*] visual`. GRA can use different colors for the hands and the body. The mismatch can originate in the modpack.
- `[Интерфейс] HD GRA Иконки` provides equipment icons (`textures/ui`).
- SortingPlus.34 provides inventory sorting. `context-menu-overhaul-1.4.2` provides the right-click menu.
- Better Stats Bars and Momopate's Detailed Weapon Stats provide the statistics and status-bar formulas in tooltips.
- `[Звук] Dark_Signal_Soundscape` wins most actor sounds.

## Scripted animation HUD sections

- `anm_* = <hand motion>, <item motion>`.
- `item_visual` attaches to the `lead_gun` hand bone (`ancor_0` in `[actor_hud]`). The item root bone is forced to identity.
- Hand offsets are `hands_position_16x9` and `hands_orientation_16x9` (HPB, degrees).
- X-Ray vectors transfer to Unity 1:1, as in `XRayUnitySpace`.

## Reporting

- You MUST name the winning mod, file path, and key values. If the user says “в оригинале так же”, you MUST treat it as their in-game verification.
- For bulk comparisons, you MUST build temporary Node.js scripts on `gra.js` and deliver a table or CSV with its absolute path. For Unity tasks, scripts and exports MUST live in the task's Archive directory. You MUST distinguish profile, file and configuration findings from behavior verified in the game.
- Writes to `D:\GRA2.5` are FORBIDDEN. The installation is read-only for this workflow.
