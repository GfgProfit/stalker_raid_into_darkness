# First Person Import Report

Created (UTC): 2026-10-18 08:59:49Z  
MO2 profile: 1v2(DX11) (v_12gb) 2.5, Все включено (168), active mods: 172, indexed files: 7312  
LTX sections: 21271, DLTX files: 436, rus strings: 35161  
swm_legs script: [Модели] Swm_Visible_Body :: scripts/swm_legs.script, use_hd_models: False

## Counts

| Item | Value |
|---|---|
| Catalog entries (single player outfits + actor without outfit) | 162 |
| Single player outfits | 161 |
| Multiplayer outfits excluded (configs/mp) | 3 |
| Exoskeleton outfits (repair_type = outfit_exo) | 27 |
| Unique hands models / imported | 158 / 158 |
| Unique body models / imported | 119 / 119 |
| Body OMF imported | 2 |
| AnimationClips in body OMF | 162 |
| Motion names present in both OMF | 0 |
| Library bindings / missing clips | 206 / 0 |
| Import errors / warnings logged | 0 / 599 |
| Camera height | 1.656 ([actor] camera_height_factor = 0.92, ph_box0_size = 0.30, 0.90, 0.30) |
| Body offset (swm_legs/legs_offset) | -0.65 |

Resolution: Resolved: 162

Excluded multiplayer sections: mp_exo_outfit, mp_military_stalker_outfit, mp_scientific_outfit

## Model problems

None.

## Skeleton compatibility (body vs body OMF)

Fully compatible bodies: 119 / 119

- 13 bones, missing animated 0, not animated 0, missing curve paths 0: 119 bodies

## SampleAnimation

| Body | Motion | Time | Driven | Non-finite | Max rot from rest | Max pos from rest | Max rot diff to first body |
|---|---|---|---|---|---|---|---|
| no_outfit | lancew_legs_idle | 0.8167 | 13 | 0 | 180 | 0.9999 | 0 |
| no_outfit | lancew_legs_sprint | 0.3333 | 13 | 0 | 180 | 0.0898 | 0 |
| no_outfit | leg_crouch_walk_fwd | 0.9 | 10 | 0 | 180 | 0.987 | 0 |
| no_outfit | leg_ladder_up | 0.4167 | 13 | 0 | 180 | 0.9965 | 0 |
| army_basic | lancew_legs_idle | 0.8167 | 12 | 0 | 42.679 | 0.9999 | 0 |
| army_basic | lancew_legs_sprint | 0.3333 | 12 | 0 | 111.6316 | 0.0898 | 0 |
| army_basic | leg_crouch_walk_fwd | 0.9 | 9 | 0 | 113.442 | 0.987 | 0 |
| army_basic | leg_ladder_up | 0.4167 | 12 | 0 | 104.9923 | 0.9932 | 0 |
| exo_army_outfit | lancew_legs_idle | 0.8167 | 12 | 0 | 42.6791 | 0.9999 | 0 |
| exo_army_outfit | lancew_legs_sprint | 0.3333 | 12 | 0 | 111.6316 | 0.0898 | 0 |
| exo_army_outfit | leg_crouch_walk_fwd | 0.9 | 9 | 0 | 113.4421 | 0.987 | 0 |
| exo_army_outfit | leg_ladder_up | 0.4167 | 12 | 0 | 104.9924 | 0.9932 | 0 |

## Missing body motions

None.

## Errors (0)


## Warnings (19)

- Missing include 'configs/items/weapons/sound_layers/wpn_ar_sounds.ltx'.
- Missing include 'configs/items/weapons/sound_layers/wpn_sd_sounds.ltx'.
- Missing include 'configs/items/weapons/sound_layers/wpn_explosion_sounds.ltx'.
- Missing include 'configs/items/weapons/sound_layers/wpn_pistol_sounds.ltx'.
- Missing include 'configs/items/weapons/sound_layers/wpn_rifle_sounds.ltx'.
- Missing include 'configs/items/weapons/sound_layers/wpn_shotgun_sounds.ltx'.
- Missing include 'configs/items/weapons/sound_layers/wpn_smg_sounds.ltx'.
- Missing include 'configs/items/weapons/sound_layers/wpn_lmg_sounds.ltx'.
- Missing include 'configs/items/weapons/sound_layers/wpn_sniper_sounds.ltx'.
- Missing include 'configs/items/weapons/sound_layers/wpn_sd_upgraded_sounds.ltx'.
- AnimTable_Basic[144] is assigned twice; 'lancew_legs_jump_start' overrides 'lancew_legs_jump_start'.
- AnimTable_Basic[272] is assigned twice; 'lancew_legs_jump_idle_c' overrides 'lancew_legs_jump_idle_c'.
- AnimTable_Basic[528] is assigned twice; 'lancew_legs_jump_end' overrides 'lancew_legs_jump_end'.
- AnimTable_Basic[160] is assigned twice; 'lancew_legs_jump_start' overrides 'lancew_legs_jump_start'.
- AnimTable_Basic[288] is assigned twice; 'lancew_legs_jump_idle_c' overrides 'lancew_legs_jump_idle_c'.
- AnimTable_Basic[544] is assigned twice; 'lancew_legs_jump_end' overrides 'lancew_legs_jump_end'.
- AnimTable_Basic[176] is assigned twice; 'lancew_legs_jump_start_sc' overrides 'lancew_legs_jump_start_sc'.
- AnimTable_Basic[304] is assigned twice; 'lancew_legs_jump_idle_sc' overrides 'lancew_legs_jump_idle_sc'.
- AnimTable_Basic[560] is assigned twice; 'lancew_legs_jump_end' overrides 'lancew_legs_jump_end'.

