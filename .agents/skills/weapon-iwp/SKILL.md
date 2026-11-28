---
name: weapon-iwp
description: REQUIRED for project weapon work involving weapons, attachments, suppressors (silencers), scopes, underbarrel grenade launchers, wpn_* sections, inventory grid items, world/HUD models, hand animations, audio, particles, stats, balance or compatibility. Use clean CoC IWP models, animations, sounds and effects and resolve gameplay data and attachment models from GRA.
---

# Weapon assets from IWP and weapon data from GRA

[AGENTS.md](../../../AGENTS.md) governs project routing and when Unity may run. [gra-research](../gra-research/SKILL.md) covers data sources. [xray-gra-import](../xray-gra-import/SKILL.md) covers importing and wiring assets. This skill governs the sources of weapon and attachment geometry, data, and icons.

Activation includes weapon, firearm, suppressor/silencer, sight/scope, underbarrel launcher, attachment/module, IWP, any `wpn_*` section, and equivalent Russian requests such as “оружие”, “ствол”, “пушка”, “глушитель”, “прицел”, “подствол”, and “модуль/обвес”.

## Detachable suppressor grid rule

**An inventory weapon with a detachable suppressor fitted MUST occupy one additional cell along its length, to the left or right.**

- This rule applies only to detachable suppressors (`silencer_status = 2` in GRA). For integrated suppressors (`1`, such as ВСС, АС «Вал», and ПБ), the suppressor is already included in the base size. The item MUST NOT expand.
- The extra cell follows the barrel axis: width in the default orientation and height for a rotated item.
- The current policy first tries the right side because the muzzle points right in icons, then tries the left. If neither side is free, fitting is rejected for insufficient space. On removal, the item returns the same cell. The user's requirement is expansion to the left or right. The side order and failure behavior are implementation assumptions. You MUST ask the user before changing them.
- Grid size and icon size MUST change together:
  - Grid: You MUST check how `ItemInstance.GetSize` accounts for installed attachments in `InventoryModel` / `ItemInstance`. If support is absent and the task requires it, implement the size change in the inventory model. The UI consumes the resolved size from the state owner.
  - Icon: Use `ItemIconVariantPart._sizeDelta = (1, 0)` on the suppressor part in the visual prefab. `ItemIconVariantEnumeration.CalculateSizeDelta` calculates it.
- When changing fitting, removal, or suppressor footprint, you MUST test fitting to a weapon adjacent to other items in the grid. Check the right side, left side, and grid edges.

## Required sources

| Material | Source |
|---|---|
| Clean world and HUD weapon models and their textures | **IWP**, subject to the clean model rule below |
| Weapon and hand HUD animations (OMF) | **IWP** |
| Weapon sounds for firing, reloading, action cycling, fire mode selection, misfire, and related actions (`sounds/weapons/<weapon>/`) | **IWP** |
| Weapon effects such as muzzle flash, smoke, and cartridge particles (`particles.xr`), plus camera effects | **IWP** |
| Stats and balance, including damage, rate of fire, dispersion, recoil, weight, price, wear, ammo, magazines, upgrades, and grid size | **GRA**, via `gra-research` |
| Attachment compatibility and parameters (`silencer_*`, `scope*`, `grenade_launcher_*`) | **GRA** |
| Attachment models for suppressors, scopes, and underbarrel launchers | **GRA** (`dynamics\weapons\wpn_upgrade\...`) |
| Names and descriptions | **GRA** (`gra.js strings`) |
| Pickup and inventory transfer sound (`snd_on_take`) | **GRA**, through the project's shared inventory sound system |
| Weapon icons | Project icon generator |
| Attachment icons | GRA HD atlases, following the Icons section |

- You MUST preserve GRA description color tags such as `%c[имя]` and `%c[a,r,g,b]`. Convert them to TMP `<color=#RRGGBB>` with `configs/ui/color_defs.xml` (`%c[default]` → `ui_gray_1`). Preserve attachment tooltip dots as in equipment «Особенности», using `d_purple` for the dot and `ui_gray_2` for the text.
- GRA weapon sections identify sound and particle names with `snd_*` and `*_particles`. The files MUST come from IWP. If an IWP file uses another name, search the pack's `sounds/weapons/<weapon>/`. You MUST ask the user if there is no unambiguous match.
- HUD parameters (`hands_position/orientation`, `aim_hud_offset_*`, `item_position`, `fire_point`, `shell_point`) are tied to IWP models and animations. GRA values are calibrated for GRA models and may not fit. If values do not match, you MUST ask the user and MUST NOT guess.
- The user confirmed IWP hands work with the project's GRA `wpn_hand_01`. Changing the hand rig or retargeting is FORBIDDEN.
- You MUST NOT add a weapon unless both its GRA section and its IWP model exist. The same weapon often uses different names in IWP and GRA:
  - `wpn_dvl_10` → `wpn_dvl10`
  - `wpn_fiveseven` → `wpn_fn57`
  - `wpn_m700` → `wpn_remington700` / `wpn_rem700_*`
  - `wpn_rsh12` → `wpn_eft_rsh12`
  - `wpn_saiga12` → `wpn_saiga12s*` (`wpn_saiga` folder)
  - `wpn_scarh` → `wpn_scar*`
  - `wpn_sr2m` → `wpn_sr2_m1/m2/veresk`
  - `wpn_vector` → `wpn_krissvektor` (model in `anomaly_weapons\w_kriss_vektor`)
  - `wpn_sa58` → `wpn_fal_sa58_osw`

  You MUST use `iwpmap` to check the mapping. The `IWP_ALIASES` table is in `iwp.js`. You may add confirmed pairs only when changing the tool is authorized. You MUST check both `iwpmap` and the complete list of GRA model folders before declaring a weapon absent from GRA. The user has used these weapons in their GRA installation.
- An earlier check found no GRA match for `awm`, `b725`, `mdr`, `mp43`, `mp9`, `mts255`, `taurus`, `vpo101`, or the launchers/mines `m320`, `rgm40`, and `pfm1`. You MUST recheck the current profile. If the task needs stats and no match exists, you MUST ask the user for them.

## Clean models only

**When adding a weapon from IWP, you MUST use only the clean base weapon:** world `wpn_<name>.ogf`, HUD `wpn_<name>_hud.ogf`, and its OMF. Importing `wpn_<name>_<scope>[_hud].ogf` variants (`_acog`, `_eot`, `_specter`, `_1p78`, `_ac10632`, and others), suppressed variants, or any other weapon-and-attachment assemblies is FORBIDDEN.

The project attaches modules to the weapon bones at runtime. This avoids a separate file and model for each combination.

- Attachment bones are `wpn_scope`, `wpn_silencer`, and `wpn_launcher`, defined in `WeaponHudComponent`. AUG A3 has `wpn_scope` and `wpn_silencer`. You MUST record the bones found in `WeaponHudComponent._attachmentBones`.
- If the clean model lacks a required attachment bone, you MUST report it to the user. You MUST NOT substitute a preassembled variant.
- Clean IWP files can contain a built-in attachment mesh skinned to an attachment bone. For example, `wpn_aug_a3.ogf` contains a suppressor with texture `wpn_addon_sil_nato`. X-Ray hides it until the attachment is fitted.
- You MUST disable meshes whose weights are all assigned to attachment bones in the item-owned visual prefabs. Leave the source model unchanged. `ItemDefinition._visualPrefab` and `WeaponHudComponent._hudModel` MUST reference these prefabs instead of the `.ogf` files.

## Icons

**Weapon icons MUST come from the project icon generator**, which renders the weapon with its attachments. You MUST NOT import weapon icons or slice `ui_icon_equipment.dds` from either IWP or GRA for them.

**Attachment icons MUST come from GRA HD atlases**, as chosen by the user. You MUST use only this atlas-selection procedure for attachments:

- Atlas: Read `icons_texture` from the attachment section and remove spaces from the value. GRA has the typo `ui\ui_ raid_hd_aattachments`. If the key is absent, use `ui\ui_icon_equipment`.
- Rectangle: Use `inv_grid_x/y/width/height` x 50 px. Measure `y` from the top.
- Use the existing shared item atlas when available. If the selected atlas must be imported, convert it to PNG in the shared item atlas folder defined in [Game Assets](../../../AGENTS.md#game-assets). Create sprites named after the sections with `ISpriteEditorDataProvider`. Reference each sprite through `ItemDefinition._iconOverride` and set `_generateIcon = false`.

The project icon generator is at `Assets/_Source/Scripts/Gameplay/Inventory/IconGeneration`. Its window is `Assets/_Source/Scripts/Editor/Inventory/IconGeneration/ItemIconGeneratorWindow.cs`. Attachment variants use `ItemIconVariantPart` in the item's visual prefab with `_part`, `_group`, `_installedObjects`, `_defaultObjects`, `_requiredParts`, `_incompatibleParts`, and `_sizeDelta`.

Icons for other items, including equipment, MUST follow `xray-gra-import`.

For generated `ItemDefinition` icons, the user requires these `ItemIconDefaults.Apply` settings:

- `LightIntensity = 40`
- `SlotLightIntensity = 40`
- `UseOutline = false`
- Side-on camera `(0, -90, 0)` with the muzzle facing right

The user can adjust an individual item's icon manually. When updating a project file, you MUST change only the necessary fields and preserve manual edits.

## Inventory grid size

- GRA uses its own smaller cells. Example sizes are АК-74 `10x4`, ПМ `4x2`, and the TGP-A suppressor `4x2`.
- The project uses a different scale. Outfits occupy `3x3`, and helmets and backpacks occupy `2x2`. These are not direct GRA dimensions. For example, `army_outfit` is `5x6` in GRA.
- There is no general conversion for weapon size. You MUST ask the user and MUST NOT choose a formula yourself.
- Exception: if a GRA weapon is already at project scale, use its dimensions directly. AUG A3 `5x2` matches the `5x2` «Основное» slot.
- For attachments, the user requires half the GRA dimensions, rounded upward, through `GraWeaponAddonSetup.ToProjectSize`. Examples: `2x2 → 1x1`, `4x2 → 2x1`, `6x2 → 3x1`, and `4x4 → 2x2`.
- The suppressor rule MUST add one cell on the project grid.

## IWP pack layout

`E:\STALKER CoC IWP Unpacked pacth 16.03.2026` is unpacked `gamedata` with no MO2 or DLTX. Encoding is cp1251. Writes to this pack are FORBIDDEN. The IWP configs cover 55 base CoC weapons. You MUST use them only as model and animation references, not as the source of gameplay data.

| Asset | Location |
|---|---|
| World and HUD models | `meshes/dynamics/weapons/wpn_<name>/` contains `wpn_<name>.ogf`, `_hud.ogf`, `_lod.ogf`, and scope variants `wpn_<name>_<scope>[_hud].ogf` |
| Weapon HUD animations | `meshes/dynamics/weapons/wpn_<name>/wpn_<name>_hud_animation.omf`, rigged to the weapon's HUD model |
| Hand HUD animations | `wpn_hand_<name>_hud_animation.omf` may live outside the weapon folder. AUG hand motions (`aug_*`) are in `wpn_fn2000/wpn_hand_fn2000_hud_animation.omf`. Find them with `iwp.js motions <name>`. Use the project's `wpn_hand_01` rig |
| Textures | `textures/wpn/` |
| Sounds | `sounds/weapons/<weapon>/` |
| Particles | `particles.xr` |

The IWP pack contains attachment models in `wpn_upgrade/`. You MUST NOT use them. GRA supplies attachment models.

### Map GRA to IWP

- `node iwp.js iwpmap [regex]` lists matching GRA model folders and section counts for each IWP weapon folder.
- `node iwp.js match [regex]` maps GRA sections to IWP models:
  - `MODEL` means the model path matches.
  - `MOVED` means the filename matches in another folder, such as `wpn_aug_a3` in `wpn_aug/`.
  - `FOLDER` means the weapon folder matches by name or alias and no filename matches.
  - `PREFIX` means the GRA folder name begins with the IWP weapon name, such as `wpn_ak74m` → `wpn_ak74` or `wpn_scar_l_tac` → `wpn_scarh`. This is a candidate only. It may be a different model variant, so you MUST confirm it with the user.
  - `MISSING` means there is no match.
- GRA weapon models are for matching only. The model added to the project MUST come from IWP.
- GRA has separate scope variant sections with their own `visual`, such as `wpn_9a91_kobra` and `wpn_ak74_pso`. IWP model variants are named by IWP sights, including `_1p78`, `_ekp`, `_okp`, `_rakurs`, `_acog`, `_eot`, `_specter`, `_micro-t1`, and `_ac10632`. You MUST map variants explicitly and ask the user when there is no unambiguous match.
- GRA motion names (`anm_*` in `wpn_<name>_hud`) refer to GRA animations. Use `omfnames` for names in an IWP OMF and `motions` to locate a file containing a named motion.

## GRA keys

- Grid: `inv_grid_width`, `inv_grid_height`. See Inventory grid size for conversion.
- Suppressor: `silencer_status` (`0` absent, `1` integrated, `2` detachable), and `silencer_name` for the attachment section, such as `wpn_sil_tgp-a` from `configs/items/weapons/weapon_silencers.ltx`. Suppressed-shot parameters are `silencer_hit_power`, `silencer_hit_impulse`, `silencer_bullet_speed`, and `snd_silncer_shot`. The last key includes the original misspelling.
- Scopes: `scope_status`, `scopes`, and variant sections. Underbarrel launchers: `grenade_launcher_status` and `grenade_launcher_name`.
- Sounds: `snd_*`, often inherited from `*_sounds` sections. Use `gra.js dump`.

## `scripts/iwp.js`

Run `node .agents/skills/weapon-iwp/scripts/iwp.js <command>` from the project root. `STALKER_IWP` overrides the pack path. You MUST use `gra.js` from `gra-research` for GRA data.

| Command | Result |
|---|---|
| `iwpmap [regex]` | Matching GRA model folders and section counts for each IWP weapon folder, or `NO GRA SECTION` |
| `match [regex]` | IWP match status, HUD model, and OMF for each GRA section, plus the GRA grid size and suppressor |
| `meshes [regex]` | Files in `meshes/dynamics/weapons` with sizes |
| `motions <name>...` | IWP OMF containing the motion |
| `omfnames <path-regex>` | Motion names from OMF using null-terminated strings, excluding bones |
| `orphans` | IWP OGF files without references in IWP configs |
| `weapons`, `addons`, `compat`, `sections`, `dump`, `get`, `grep`, `strings`, `sounds` | Equivalent lookups in IWP configs, for reference only |

## Unity import

- Before running a setup, you MUST verify the active Production target, affected assets, and actual side effects, including opening or saving `OutdoorsScene`, recreating groups, and rebaking the icon catalog. `AGENTS.md` governs launch and output locations. For diagnostics, use existing read-only checks or an authorized test path.
- The reference setup is `Assets/_Source/Scripts/Editor/Inventory/IwpWeaponSetup.cs` for AUG A3. Menu `Tools/Inventory/Setup Test Weapon (AUG A3)`, batch `IwpWeaponSetup.SetupBatch`, log tag `[IwpWeapon]`. The setup:
  - Copies clean models and OMF from IWP.
  - Assigns the rig.
  - Generates materials from IWP textures.
  - Checks animation bindings on the HUD model and all hands in `OutfitCatalog`.
  - Builds visual prefabs with built-in attachment meshes disabled.
  - Creates `ItemDefinition` with `WeaponStatsComponent` from GRA and `WeaponHudComponent` from IWP.
  - Adds it to `ItemDatabase` and builds its world prefab.
  - Sets `snd_on_take`, places the weapon in `OutdoorsScene`, and bakes its icon.
- You MUST import models and animations with the existing X-Ray importer as specified in `xray-gra-import`. Copy files into `Assets`. Blender/FBX conversion and custom importers are FORBIDDEN. Weapon models, OMF, sounds, and effects come from IWP. Attachment models come from the GRA winning files (`gra.js winner`).
- Automatic generation after copying an IWP `.ogf` into `Assets` searches GRA MO2 and may miss or select incorrect textures. You MUST run `XRayAssetGenerationCoordinator.Process` after copying:
  - Set `XRaySourceMode.CustomFolder` to the IWP root. For initial IWP material generation, set `PreserveExistingMaterials = false`. For existing assets, you MUST preserve manual edits and check that the run affects only the intended materials.
  - Repeat until `Completed` while deferred texture imports are progressing. If an error occurs or progress stops, you MUST diagnose the cause before another run.
- The importer cannot decode BC7 bump textures (`DX10 DXGI 98`), so the material has no normal map. AUG A3's `aug_a3_bump.dds` is an example. You MUST report the limitation and MUST NOT modify the importer without a user request.
- You MUST run setups and tests through Unity Open MCP. For batch mode, follow `unity-batch-mirror`.
- Weapon and attachment data MUST live in `ItemDefinition` and its components, as with equipment. Weapon rules MUST NOT live in the UI.
- Attachment compatibility MUST come from GRA sections. You MUST NOT infer it from filenames.
- New weapons and attachments MUST use the item layout in [Game Assets](../../../AGENTS.md#game-assets). Verify the actual setup destinations before running a setup and change them only within the authorized task scope.

## GRA attachments

Setup: `Assets/_Source/Scripts/Editor/Inventory/GraWeaponAddonSetup.cs`. Menu `Tools/Inventory/Setup GRA Weapon Addons`, batch `GraWeaponAddonSetup.SetupBatch`, log tag `[WeaponAddons]`.

- A section qualifies as an attachment actually used in the game when its class is `WP_SILEN`, `WP_SCOPE`, or `WP_GLAUN`, it has a `visual`, and a valid reference exists:
  - Weapon `scopes`, `silencer_name`, or `grenade_launcher_name`. You MUST use `XRayLtxDatabase.GetList`, which resolves DLTX `>scopes = ...`. `GetValue` misses the additions and previously caused most scopes to be omitted.
  - MAS scope group from the weapon's `modular_scope_group` to the group `scopes` (`scope_group_*`).
  - Trade and loot lists (`configs/**` with `trade|death|treasure|loot|craft|stash|drop` in the path).
  - You MUST exclude `scope_base`, which has no model, and `placeholder` («MAS инструмент разработчика»). A previous scan found `wpn_addon_scope_mosin` unreferenced. You MUST check the active profile for current results.
  - Conversion kits (`II_FOOD`, including `archangel`, `pn23fore`, and `susatov`) in `scopes` lists do not qualify as attachments. You MUST report them in the log.
  - A previous scan found 146 attachments, including standard, night-vision, and thermal scopes, suppressors, and underbarrel launchers.
- You MUST use the model that mounts to the weapon: `hud = <section>_hud` → `item_visual` (`attachments\scopes\<name>`, Pizza's Ultimate Sight / MAS). Use `visual` only when that file is absent. Some scopes use `visual` for a box (`wpn_case_*`) or a different model. For example, `ekp8_18` uses the ЕКП-8-02 model, and `0kp2` / `rmr` use an untextured box.
- Correct source model defects via the setup's `MESH_ONLY` table, which maps the section to the material to keep. Disable other meshes in the attachment visual prefab. For example, Boomsticks gives `wpn_sil_spec_isg` a full AK world model. Keep only its `wpn_silencer_hexagon12` suppressor mesh. You MUST check every attachment for which `ogftex` shows weapon textures such as `receiver`, `stock`, `magazine`, or `handguard`.
- Attachment textures are generated normally by the importer through GRA MO2.
- Each attachment definition in its item folder includes:
  - `Attachment` category and `Silencer`, `Scope`, or `GrenadeLauncher` tag.
  - `WeaponAddonComponent` with type, price, `*_k` multipliers, and `scope_zoom_factor`.
  - `snd_on_take` sound (`attchsilen`, `attchscope`, `attchscopenv`, `attchgl`, and others).
- When placement of attachments for pickup is in scope, you MUST place them at an open, level spot near the player in `OutdoorsScene` under `WorldItems/Addons` with `WorldItemPrefabBuilder.AppendToScene`. A full setup MUST make every new attachment available for pickup in the world. If scene edits are outside authorized scope, you MUST report that placement remains incomplete. Rerunning the full setup recreates the group. Before running it, you MUST verify scene state and confirm that the operation preserves manual changes and other `WorldItems`.
- `ItemIconBatchBake.BakeAllAsync` rebakes the generated icon catalog. Old generated attachment icons may be removed because attachments use the HD atlas. Before running it, you MUST verify what it overwrites and that it preserves manual icons and entries outside the task.

## First-person weapon implementation

The project owns its firing logic. IWP supplies only the animations, sounds, particles, and camera `.anm` effects for that logic.

- Runtime assembly `RaidIntoDarkness.Weapons` (`Assets/_Source/Scripts/Gameplay/Weapons`):
  - `FirstPersonWeaponAsset` is a ScriptableObject in the weapon's item folder. It contains the HUD prefab, hand offsets, sight calibration, motions by `FirstPersonWeaponMotionKind`, aiming blend `.anm`, RPM, fire modes, magazine, flash and smoke prefabs, recoil, and sounds. `WeaponHudComponent._firstPersonWeapon` references it.
  - `FirstPersonWeaponController` on `FPS_Player` uses Hidden/Drawing/Ready/Reloading/SwitchingMode/Holstering. Slots are Primary (`3`) and Secondary (`2`). Left mouse fires, holding right mouse aims, `R` reloads, and `X` switches fire mode. The controller acquires hands through `FirstPersonRig.TryAcquireHands`.
  - `FirstPersonWeaponGraph` uses Playables with a cross-faded hand layer, a left-hand overlay masked to `l_*` bone subtrees, and a weapon mixer. The `bip01_l_finger*` bones are included as descendants of `l_hand`. `wpn_hand_01` has one bone part named `default`. FDDA pickup `liz_pickup_left_wpn_hands` plays as an overlay when the weapon owns the hands (`IFirstPersonHandsOverlay`).
  - Equipment animations (`EquipmentAnimationQueue`) call `FirstPersonRig.RequestHands`. The weapon holsters and draws again after the hands have been free for 0.5 seconds.
  - `FirstPersonWeaponBinder` maps inventory slots `Primary` and `Secondary` to `SetSlotWeapon`.
  - Ammo behavior is provisional: magazine size comes from GRA `ammo_mag_size` and reserve is 90, keyed by `ItemInstance.InstanceId`. The HUD is `FirstPersonWeaponAmmoView` (“30/90”, TMP, `FPS_Player/WeaponHud/Ammo`).
- Recoil ports `WeaponRecoilManager` / `IRecoil` from BiohazardProtocol. `StalkerCameraRecoil` sits on `CameraRoot` and is applied in `StalkerCameraController.Apply`. Defaults match BiohazardProtocol MDR: `(4, 4, 2)`, snappiness 13, return 9, and half amplitude while crouched.
- **IWP animation HUD offsets:** `hands_position/orientation` MUST be 0 because the hand pose is baked into animations and GRA values displace the weapon. IWP `anm_idle_aim` is not the aiming pose. Aiming uses the hand-space blend `anims/blend/aim_{start,idle,end}_<type>`, as with `player_hud` script layers in modded executables. AUG uses `f2000`. After blending, the `wpn_scope` axis is at eye height with an X offset. Use a screenshot to calibrate the correction. AUG uses `aim offset = (0.0127, -0.006, 0)` in `HandsRoot` space, with the front sight centered in the rear aperture.
- Motions: Hand `aug_*` comes from `wpn_hand_fn2000_hud_animation.omf`. Use A3's OMF first (`idle`, `idle_empty`, `reload`, `reload_empty`, `misfire`). Missing motions (`draw`, `shoot`, `sprint*`, `firemode`) come from `wpn_aug_hud_animation.omf` (A1) with the A3 rig. Camera effects use `anims/camera_effects/weapon/aug_*.anm`.
- Sounds: Pick a random `aug_muzzle_stereo*` shot variant. Use `aug_empty` for an empty magazine, rate limited by `rpm_empty_click`. Play `last_shell_out` on the last round and overlay `mag_shot` at five rounds or fewer. The latter behavior is a project decision based on pack files and has no counterpart in IWP scripts or configs. Resolve GRA `snd_draw/holster` by name in IWP. Fall back to `assault_draw/holster`.
- Setup: `IwpFirstPersonWeaponSetup`, menu `Tools/Inventory/Setup First Person Weapon (AUG A3)`, batch `SetupBatch`, log tag `[FirstPersonWeapon]`. It requires `IwpWeaponSetup` output. Test: `OutdoorsSceneWeaponTests`.

### X-Ray particles

- Import IWP `particles.xr` via `XRayParticleImporter` (menu `Tools/Effects/Import IWP Weapon Particles`, log tag `[XRayParticles]`) and `XRayParticleLibraryReader`. Select `weapons\*` except cartridge particles, including `*shell*`, `weapons\<caliber>`, and `weapons\effects\<caliber>`.
- Outputs are `Assets/_Source/Effects/XRay/Particles/<X-Ray path>.{asset,mat,prefab}`. Textures live at `Effects/XRay/Textures`. DDS uses an sRGB header. Unity cannot import DDS with an incomplete mip chain, so convert those files to PNG.
- Runtime assembly `RaidIntoDarkness.Effects` uses `XRayParticleSimulation`, a CParticleEffect port with a 0.033 s step and Source/KillOld/Move/Target*/Gravity actions, and `XRayParticleGroupPlayer` for the CParticleGroup timeline (t0/t1, deferred stop). It renders with `ParticleSystem.SetParticles` and `RaidIntoDarkness/XRayParticle`, with atlas frames selected in the vertex shader. `xdistort` is not rendered.
- `ALIGN_TO_PATH` MUST follow `CParticleEffect::Render`. Unity Stretch billboards are unsuitable because they stretched the `weapon_shot_invert_*` star backward along the barrel into the fore-end. With speed greater than zero and `FACE_ALIGN` enabled, use a plane perpendicular to velocity and rotate it by `rot.x`. Otherwise use a sprite aligned to velocity or `m_APDefaultRotation` (PED chunk `0x25`), facing the camera. The particles use world-aligned billboards with `rotation3D`.
- Additive materials MUST use `_Intensity = 4` for visible HDRP muzzle flash.
- The flash origin is `FirstPersonWeaponView._muzzle`, a Transform on the item's HUD visual. `MuzzlePoint` is parented to `wpn_silencer`, and its forward axis is the flame direction. Move it manually in the prefab. `IwpFirstPersonWeaponSetup` creates it only when missing. `IwpWeaponSetup` preserves its parent and local pose when rebuilding the visual. Without the component, use `FirstPersonWeaponAsset._muzzleBone`.
- Muzzle effects MUST follow X-Ray orientation (`setup_firedeps` and `generate_orthonormal_basis_normalized`). Z follows the visible barrel direction using `TransformVector` with `HandsRoot` nonuniform scaling under `hud_fov`. Up is world up, independent of weapon roll. A muzzle flash that follows the barrel MUST update its orientation each frame.
- AUG uses `flame_particles = weapons\generic_weapon05` following the `wpn_silencer` bone and `smoke_particles = weapons\generic_shoot_00` at the point of firing. The suppressor's empty `silencer_flame_particles` means no suppressed flash.

## Required verification

For weapon or attachment changes, you MUST verify the items that govern the affected behavior. For reference-only research, you MUST compare source data without running setup.

- Stats match GRA sections. Weapon models, animations, sounds, and effects come from IWP. Attachment models come from GRA.
- The project contains only clean weapon models with no `_<scope>` or other attachment variants. Built-in attachment meshes are disabled in visual prefabs.
- For each weapon with a detachable suppressor, the fitted width is one cell above base width, and the unfitted width equals base width.
- A suppressor expands the inventory item to the right, or to the left when the right is occupied. Fitting is rejected if neither side has room.
- The suppressed icon is one cell wider and matches the item grid size.
- Integrated suppressors do not change item size.
- Weapon icons are generated by the project. Attachment icons use sprites from the selected GRA atlases. Manual changes are preserved.
