---
name: xray-gra-import
description: REQUIRED when changing the project X-Ray asset importer, importing GRA/Anomaly or CoC IWP models, animations, effects, textures, audio or icons into Unity, or wiring imported assets into prefabs and ScriptableObjects. Covers OGF, OMF, ANM, PPE, DDS and OGG.
---

# Import GRA assets through the project X-Ray importer

The project importer lives at `Assets/_Tools/X-Ray OGF OMG Importer/`. It imports OGF, OMF and ANM, finds textures through the MO2 VFS, creates HDRP materials, and converts bump textures to normal maps. When importing assets, you MUST copy the winning file into `Assets` and use this importer. Writing another importer or converting through Blender/FBX is FORBIDDEN.

You MUST NOT change these parts of the importer unless the task directly requires it:

- OGF/OMF parsing
- Bone math, bind poses, and skinning
- AnimationClip generation and runtime Playables

## Project asset locations

| Asset | Path |
|---|---|
| Hands (158 OGF models sharing a rig) | `Assets/_Source/FirstPerson/Hands/Models/` |
| Rig for hand OMF | `wpn_hand_01.ogf`, GUID `475a6b96ee587c842bb5357963a962aa` |
| Swm_Visible_Body body | `Assets/_Source/FirstPerson/Body/Models/` |
| Body animations | `Assets/_Source/FirstPerson/Animations/Body/` and `BodyAnimationLibrary.asset` |
| Hand HUD motions (pickup and equip) | `Assets/_Source/FirstPerson/Animations/Hands/` |
| Equipment animations | `Assets/_Source/FirstPerson/Equipment/` (HUD models, item OMF, `.anm`, sounds, and `EquipmentAnimationLibrary.asset`) |
| Outfit-to-hands/body catalog | `Assets/_Source/FirstPerson/Outfits/OutfitCatalog.asset` |
| World item models and prefabs | `Assets/_Source/Items/{Outfits,Helmets,Backpacks}/{Models,Prefabs}` |
| Item definitions | `Assets/_Source/Items/Definitions/{Armor,Head,Backpack}` |
| Icons (weapons and attachments follow `weapon-iwp`) | `Assets/_Source/Items/Icons` uses 2D Sprite slices from `[Интерфейс] HD GRA Иконки`. `ItemDefinition` has an icon override field |
| Camera `.anm` motions | `Assets/_Source/Settings/Player/Camera/Anm/` |

The importer generates `Textures/` and `Materials/` beside the model. You MUST NOT duplicate them manually.

## REQUIRED Editor setup workflow

Follow these steps for a setup in scope.

1. Use a static class in `Assets/_Source/Scripts/Editor/...` with `[MenuItem("Tools/X-Ray/...")]`. A separate `SetupBatch()` entry point MUST call `EditorApplication.Exit(success ? 0 : 1)` only in an authorized, separate batch process. In the interactive Production Editor, you MUST invoke the shared operation without exiting Unity. Tag the logs with `[Name]`.
2. Open sources through `StalkerXRaySources.Open(scope, out error)` or plan with `Mo2VirtualFileSystem.Plan(...)` and a narrow `IndexScope` (`"configs"`, affected `meshes/...`, `anims`, `sounds/actor`), then use `XRayAssetIndex.Build(plan)`. Load system configs with `XRayLtxDatabase.LoadSystem(index)`, which resolves DLTX. Load separate INI files with `XRayLtxDatabase.LoadFile(index, path)`.
3. You MUST use winning files only. Resolve with `index.TryGet(virtualPath, out file)` and read with `File.ReadAllBytes(index.GetFullPath(file.Winner))`. Copy into `Assets` within `AssetDatabase.StartAssetEditing/StopAssetEditing`, then run `AssetDatabase.Refresh(ForceSynchronousImport)`.
4. For OMF, you MUST assign the rig through the importer's `SerializedObject`: `_rigGuid = GUID of the model` and `_autoDetectRig = false`, then `SaveAndReimport()`. Use `wpn_hand_01` for hand motions. For one item OMF targeting multiple model rigs, such as helmets or backpacks, you MUST make a separate copy for each model using `<omf>__<model>.omf` so its bone paths match.
5. For model textures and materials, use `XRayAssetGenerationCoordinator.NeedsGeneration` and `Process(model, options, false, out _)` for up to four passes with `Refresh`.
6. Validation is REQUIRED. Binding paths from `AnimationUtility.GetCurveBindings` MUST exist in every target model, including every model in `OutfitCatalog` for hand clips. Items attached to `lead_gun` require that bone. You MUST count failures and return `failures == 0`.
7. You MUST store data in a ScriptableObject library or catalog. Wire references with `PrefabUtility.LoadPrefabContents` and `SerializedObject`, then save with `SaveAsPrefabAsset`.
8. You MUST run setup through Unity Open MCP in the active Production Editor. For a batch run, you MUST follow `unity-batch-mirror`. You MUST verify the result with a test and capture frames when needed.

## Asset formats

- `.anm`: Standard `XRayAnmImporter` (Engine semantics, scale 1) → `XRayAnmAsset.Evaluate(time, out pos, out rot)`. Play camera effectors through `StalkerCameraEffects`, using `PlayItemEffect` for item animations.
- `.ppe` (post-processing): `u32 version`, then `base color, add color, gray color`, each with `float base` and three R, G, B envelopes. Then read one envelope per `gray, blur, duality h/v, noise i/g/f, cm_influence` parameter. Read envelopes through `XRayAnmReader.ReadEnvelope(reader, warnings)` (`Load_2` format). Apply through `StalkerScreenFade` with add color in gamma space after post-processing and fade-out through `Stop(1)`.
- You MUST copy `.ogg` files as-is into the feature folder.
- Resolve world item models from the section's `visual`, scripted HUD animation models from `item_visual`, hands from `[actor_hud_*] visual`, and bodies from `<section>_legs` → `visual`.
- X-Ray config paths use `\`, omit `meshes/`, and often omit `.ogf`. Sound paths omit `sounds/` and `.ogg`.

## Restrictions

- Importing weapons or their animations is FORBIDDEN unless requested. The weapon route in [AGENTS.md](../../../AGENTS.md) governs weapon asset and icon sources.
- You MUST NOT delete bones, retarget, or change the hand rig.
- You MUST NOT duplicate textures, materials, meshes, or clips. Separate OMF copies for distinct rigs are permitted.
- You MUST NOT overwrite the user's manual material and prefab settings.
- You MUST NOT change files in `D:\GRA2.5`.
