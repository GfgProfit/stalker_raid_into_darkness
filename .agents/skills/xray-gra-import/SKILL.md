---
name: xray-gra-import
description: Use when GRA/Anomaly assets must be brought into the Unity project - OGF models (world, HUD, hands, body), OMF animations, ANM camera effects, PPE post-process effects, DDS textures, OGG sounds, icons - or when building editor setup tools that wire them into prefabs/ScriptableObjects. Enforces using the project's own X-Ray OGF/OMF/ANM importer instead of writing new importers or converting via Blender/FBX.
---

# Импорт ассетов GRA через наш X-Ray импортёр

В проекте есть свой рабочий импортёр: `Assets/_Tools/X-Ray OGF OMG Importer/`. Он сам импортирует OGF, OMF и ANM, находит текстуры через MO2 VFS, создаёт HDRP-материалы, конвертирует bump в normal. Пользователь жёстко реагирует на попытки писать «свой импортёр снаряжения» — **не пиши новый импортёр и не конвертируй через Blender/FBX**. Скопируй файл-победитель в `Assets` — импортёр сделает остальное.

Не трогай без прямой необходимости:
- парсинг OGF/OMF;
- математику костей, bind poses, скиннинг;
- генерацию AnimationClip и runtime Playables импортёра.

## Где что лежит в проекте

| Что | Путь |
|---|---|
| Руки (158 OGF, общий rig) | `Assets/_Source/FirstPerson/Hands/Models/` |
| Rig для OMF рук | `wpn_hand_01.ogf`, GUID `475a6b96ee587c842bb5357963a962aa` |
| Тело Swm_Visible_Body | `Assets/_Source/FirstPerson/Body/Models/` |
| Анимации тела | `Assets/_Source/FirstPerson/Animations/Body/` (+ `BodyAnimationLibrary.asset`) |
| HUD-моушены рук (подбор, экипировка) | `Assets/_Source/FirstPerson/Animations/Hands/` |
| Анимации экипировки | `Assets/_Source/FirstPerson/Equipment/` (HUD-модели, OMF предметов, `.anm`, звуки, `EquipmentAnimationLibrary.asset`) |
| Каталог костюмов → руки/тело | `Assets/_Source/FirstPerson/Outfits/OutfitCatalog.asset` |
| Мировые модели и префабы предметов | `Assets/_Source/Items/{Outfits,Helmets,Backpacks}/{Models,Prefabs}` |
| Определения предметов | `Assets/_Source/Items/Definitions/{Armor,Head,Backpack}` |
| Иконки (кроме оружия и модулей — их делает генератор) | `Assets/_Source/Items/Icons` — из `[Интерфейс] HD GRA Иконки`, нарезка через 2D Sprite; у `ItemDefinition` есть поле-override иконки |
| Камерные `.anm` движения | `Assets/_Source/Settings/Player/Camera/Anm/` |

Импортёр сам кладёт `Textures/` и `Materials/` рядом с моделью — не дублируй их вручную.

## Шаблон editor-сетапа

1. Статический класс в `Assets/_Source/Scripts/Editor/...` с `[MenuItem("Tools/X-Ray/...")]` и `SetupBatch()` → `EditorApplication.Exit(success ? 0 : 1)`. Логи с тегом `[Имя]`.
2. Источники:
   - `StalkerXRaySources.Open(scope, out error)` или `Mo2VirtualFileSystem.Plan(...)` с узким `IndexScope` (`"configs"`, нужные `meshes/...`, `anims`, `sounds/actor`), затем `XRayAssetIndex.Build(plan)`;
   - конфиги: `XRayLtxDatabase.LoadSystem(index)` (DLTX учтён), отдельные ini — `XRayLtxDatabase.LoadFile(index, path)`.
3. Только победители: `index.TryGet(virtualPath, out file)` → `File.ReadAllBytes(index.GetFullPath(file.Winner))`. Копируй в `Assets` внутри `AssetDatabase.StartAssetEditing/StopAssetEditing`, потом `AssetDatabase.Refresh(ForceSynchronousImport)`.
4. OMF: задай rig через `SerializedObject` импортёра — `_rigGuid = GUID модели`, `_autoDetectRig = false`, затем `SaveAndReimport()`.
   - Моушены рук — rig `wpn_hand_01`.
   - Один OMF предмета на несколько разных моделей (шлемы, рюкзаки) копируй отдельно на каждую модель: `<omf>__<model>.omf`, иначе пути костей не совпадут.
5. Текстуры и материалы моделей: прогон `XRayAssetGenerationCoordinator.NeedsGeneration` / `Process(model, options, false, out _)` до 4 проходов с `Refresh`.
6. Валидация обязательна:
   - пути биндингов клипов (`AnimationUtility.GetCurveBindings`) существуют в каждой целевой модели;
   - для рук — во всех моделях из `OutfitCatalog`;
   - кость `lead_gun` есть, если к ней крепится предмет.
   Считай ошибки и возвращай `failures == 0`.
7. Данные — в ScriptableObject (библиотека или каталог), связи — через `PrefabUtility.LoadPrefabContents` + `SerializedObject`, сохранение `SaveAsPrefabAsset`.
8. Запускай через Unity Open MCP в открытом Production Editor. Для batch-режима следуй `unity-batch-mirror`. Результат проверь тестом и при необходимости кадрами.

## Форматы

- `.anm`: стандартный `XRayAnmImporter` (Engine semantics, scale 1) → `XRayAnmAsset.Evaluate(time, out pos, out rot)`. Камерные эффекторы играются через `StalkerCameraEffects` (`PlayItemEffect` для анимаций предметов).
- `.ppe` (postprocess): `u32 version`, затем параметры в порядке `base color, add color, gray color` (каждый: `float base` + 3 огибающие R, G, B), потом `gray, blur, duality h/v, noise i/g/f, cm_influence` (по огибающей). Огибающие читаются `XRayAnmReader.ReadEnvelope(reader, warnings)` (формат `Load_2`). Применение — `StalkerScreenFade` (add color в гамма-пространстве после пост-обработки, затухание `Stop(1)`).
- `.ogg` копируются как есть в папку фичи.
- Мировые модели предметов — ключ `visual` секции, HUD-модель скриптовой анимации — `item_visual`, руки — `[actor_hud_*] visual`, тело — `<section>_legs` → `visual`.
- Пути в конфигах X-Ray: `\`, без `meshes/` и часто без `.ogf`; звуки — без `sounds/` и `.ogg`.

## Чего не делать

- Не импортировать оружие и его анимации, пока об этом не попросили. Для работы с оружием и модулями используй скилл `weapon-iwp` (модели оружия и анимации — из CoC IWP, характеристики и модели модулей — из GRA, иконки оружия и модулей делает генератор).
- Не удалять кости, не делать retarget, не менять rig рук.
- Не плодить дубликаты текстур, материалов, мешей и клипов (кроме копий OMF под разные rig).
- Не перезаписывать ручные настройки материалов и префабов пользователя.
- Не изменять файлы в `D:\GRA2.5`.
