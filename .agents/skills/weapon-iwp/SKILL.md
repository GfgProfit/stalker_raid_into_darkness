---
name: weapon-iwp
description: Use for any weapon work in this project - weapons, attachments/modules (silencers, scopes, grenade launchers), weapon items in the inventory grid, weapon world/HUD models, weapon hand animations, weapon sounds, particles and stats. Only clean weapon models (never per-module variant files - modules attach to weapon bones at runtime), HUD animations, weapon sounds and effects come from the unpacked CoC IWP pack (E:\STALKER CoC IWP Unpacked pacth 16.03.2026); stats, balance, module compatibility and module models come from GRA. Triggers - "оружие", "ствол", "пушка", "глушитель", "прицел", "подствол", "модуль/обвес", "IWP", any wpn_* section.
---

# Оружие: модели, анимации, звуки и эффекты IWP + данные GRA

## Главное правило

**Если на оружие установлен глушитель, предмет в сетке инвентаря становится на одну клетку длиннее: +1 клетка влево или вправо.**

- Правило касается только съёмного глушителя (`silencer_status = 2` в GRA). У встроенного (`1`: ВСС, АС «Вал», ПБ) глушитель уже входит в базовый размер, и прибавки нет.
- Клетка добавляется вдоль длины ствола: в обычном положении это ширина, у повёрнутого предмета — высота.
- Клетка занимается с той стороны, где она свободна; сначала справа (дуло на иконках смотрит вправо), иначе слева. Если свободно ни там, ни там, глушитель не ставится, как при нехватке места. При снятии глушителя предмет сжимается обратно на ту же клетку. Порядок сторон и поведение без места — это мои допущения, правила только «влево или вправо». Если меняешь их, уточни у пользователя.
- Размер в сетке и размер иконки меняются вместе:
  - сетка — через модель инвентаря (`InventoryModel` / `ItemInstance`). Сейчас `ItemInstance.GetSize` берёт только `Definition.Size`, поэтому размер, зависящий от установленных модулей, нужно добавить в модель; в UI это не делается;
  - иконка — через `ItemIconVariantPart._sizeDelta = (1, 0)` на части глушителя в визуальном префабе. Его считает `ItemIconVariantEnumeration.CalculateSizeDelta`.
  - Оба значения обязаны совпадать.
- Обязательный тестовый случай: глушитель ставится на оружие, лежащее в сетке вплотную к другим предметам. Проверь и правую, и левую сторону, и края сетки.

## Что откуда брать

| Что | Источник |
|---|---|
| Мировые и HUD-модели оружия (только чистые, см. ниже), их текстуры | **IWP** |
| HUD-анимации оружия и рук (OMF) | **IWP** |
| Звуки оружия: выстрел, перезарядка, затвор, смена режима, осечка и т.д. (`sounds/weapons/<ствол>/`) | **IWP** |
| Эффекты оружия: партиклы вспышки, дыма, гильз и т.п. (`particles.xr`), камерные эффекты | **IWP** |
| Характеристики и баланс (урон, скорострельность, разброс, отдача, вес, цена, износ, патроны, магазин, апгрейды, размер в сетке) | **GRA** (скилл `gra-research`) |
| Совместимость и параметры модулей: `silencer_*`, `scope*`, `grenade_launcher_*` | **GRA** |
| Модели модулей (глушители, прицелы, подствольники) | **GRA** (`dynamics\weapons\wpn_upgrade\...`) |
| Названия и описания | **GRA** (`gra.js strings`) |
| Звук подбора/переноса в инвентаре (`snd_on_take`) | **GRA** — общая система звуков инвентаря проекта |
| Иконки оружия и модулей | **ни откуда**, их делает генератор иконок |

- Описание из GRA содержит цветовые теги `%c[имя]` / `%c[a,r,g,b]`. Их не вырезать: они переводятся в TMP `<color=#RRGGBB>` по `configs/ui/color_defs.xml` (`%c[default]` → `ui_gray_1`). Так у модулей в тултипе остаются цветные точки, как в «Особенностях» снаряжения: `d_purple` точка, текст `ui_gray_2`.
- В GRA имена звуков и партиклов указаны в секции ствола (`snd_*`, `*_particles`), но сами файлы берутся из IWP. Если у файла IWP другое имя, ищи его в `sounds/weapons/<ствол>/` пака; если однозначной пары нет, спроси пользователя.
- HUD-параметры (`hands_position/orientation`, `aim_hud_offset_*`, `item_position`, `fire_point`, `shell_point`) привязаны к модели и анимациям IWP, а значения GRA рассчитаны на модели GRA и к ним могут не подойти. Если что-то не сходится, спроси пользователя, а не подбирай значения сам.
- Руки IWP совместимы с руками проекта (`wpn_hand_01` из GRA) — подтверждено пользователем. Rig рук не меняй и retarget не делай.
- Ствол берётся в проект только если есть и секция GRA, и модель IWP. Одно и то же оружие в IWP и GRA часто названо по-разному:
  - `wpn_dvl_10` — `wpn_dvl10`;
  - `wpn_fiveseven` — `wpn_fn57`;
  - `wpn_m700` — `wpn_remington700` / `wpn_rem700_*`;
  - `wpn_rsh12` — `wpn_eft_rsh12`;
  - `wpn_saiga12` — `wpn_saiga12s*` (папка `wpn_saiga`);
  - `wpn_scarh` — `wpn_scar*`;
  - `wpn_sr2m` — `wpn_sr2_m1/m2/veresk`;
  - `wpn_vector` — `wpn_krissvektor` (модель в `anomaly_weapons\w_kriss_vektor`);
  - `wpn_sa58` — `wpn_fal_sa58_osw`.

  Поэтому пару ищи через `iwpmap` (таблица синонимов `IWP_ALIASES` лежит в `iwp.js`; найдёшь новую пару — добавь туда). Не объявляй ствол «отсутствующим в GRA», пока не проверил его через `iwpmap` и полный список папок моделей GRA. Пользователь бегал с этими стволами в своей GRA.
- Без пары в GRA сейчас: `awm`, `b725`, `mdr`, `mp43`, `mp9`, `mts255`, `taurus`, `vpo101` и гранатомёты/мины `m320`, `rgm40`, `pfm1`. Для них характеристики не выдумывай, спроси пользователя.

## Только чистая модель

**При добавлении оружия из IWP берётся ТОЛЬКО чистая модель ствола без модулей:**
- мировая `wpn_<name>.ogf`, HUD-модель `wpn_<name>_hud.ogf` и её OMF;
- **никогда** не тяни варианты `wpn_<name>_<прицел>[_hud].ogf` (`_acog`, `_eot`, `_specter`, `_1p78`, `_ac10632`, ...), модели с глушителем и любые другие сборки «ствол + модуль».

Модули ставятся в кость нужного оружия в нашем движке во время игры. Так не будет, как в сталкере, отдельного файла и отдельной модели под каждую вариацию.

- Кости крепления модулей: `wpn_scope`, `wpn_silencer`, `wpn_launcher` (константы в `WeaponHudComponent`). У AUG A3 есть `wpn_scope` и `wpn_silencer`. Найденные кости записываются в `WeaponHudComponent._attachmentBones`.
  - Если у чистой модели нет кости под модуль, не бери вариант с модулем, а сообщи пользователю.
- В «чистых» моделях IWP часто зашит меш модуля, скиннутый на кость модуля. Например, у `wpn_aug_a3.ogf` внутри лежит глушитель с текстурой `wpn_addon_sil_nato`; в X-Ray движок прячет его, пока модуль не установлен.
  - У нас такие меши выключаются в визуальном префабе `Items/Weapons/Visuals/<section>[_hud].prefab`: это меши, все веса которых на костях модулей. Модель при этом не правится.
  - `ItemDefinition._visualPrefab` и `WeaponHudComponent._hudModel` ссылаются на эти префабы, а не на `.ogf`.

## Иконки

**Иконки оружия не импортируются** — их делает генератор иконок проекта (на оружии рендерится само оружие с модулями).

**Иконки модулей — из HD-атласов GRA** (решение пользователя).
- атлас: ключ `icons_texture` секции модуля, пробелы в значении убираются (в GRA есть опечатка `ui\ui_ raid_hd_aattachments`); если ключа нет — `ui\ui_icon_equipment`;
- прямоугольник: `inv_grid_x/y/width/height` × 50 px, `y` отсчитывается сверху;
- атлас конвертируется в PNG в `Assets/_Source/Items/Icons/<имя>.png`, спрайты с именами секций (`ISpriteEditorDataProvider`), ссылка — `ItemDefinition._iconOverride`, `_generateIcon = false`.

Генератор иконок проекта:
- `Assets/_Modules/Inventory/Runtime/IconGeneration`, окно — `Assets/_Modules/Inventory/Editor/IconGeneration/ItemIconGeneratorWindow.cs`;
- варианты с модулями — компонент `ItemIconVariantPart` в визуальном префабе предмета: `_part`, `_group`, `_installedObjects`, `_defaultObjects`, `_requiredParts`, `_incompatibleParts`, `_sizeDelta`.

Не нарезай `ui_icon_equipment.dds` ни из IWP, ни из GRA. Иконки остальных предметов (снаряжение и т.п.) делаются по правилам скилла `xray-gra-import`.

Параметры генерируемых иконок в `ItemDefinition` (требование пользователя) задаёт `ItemIconDefaults.Apply`:
- `LightIntensity = 40`;
- `SlotLightIntensity = 40`;
- `UseOutline = false`;
- камера сбоку `(0, -90, 0)`, дуло вправо.

Пользователь может поправить иконку конкретного предмета вручную. При обновлении проектного файла меняй только нужные поля и сохраняй ручные правки.

## Размер в сетке

- В GRA своя мелкая сетка: АК-74 `10x4`, ПМ `4x2`, глушитель TGP-A `4x2`.
- В проекте размеры свои: костюмы `3x3`, шлемы и рюкзаки `2x2`, и это не прямые значения GRA (у `army_outfit` в GRA `5x6`).
- Как переводить размер оружия из GRA в сетку проекта, в общем случае не решено. Не выбирай формулу сам, спроси пользователя.
  - Исключение: если в GRA размер уже в масштабе проекта (AUG A3 `5x2` = слот «Основное» `5x2`), он берётся как есть.
- **Модули (решение пользователя): половина размера GRA с округлением вверх** — `GraWeaponAddonSetup.ToProjectSize`. Примеры: `2x2 → 1x1`, `4x2 → 2x1`, `6x2 → 3x1`, `4x4 → 2x2`.
- Правило глушителя (+1 клетка) действует в сетке проекта.

## IWP: структура пака

`E:\STALKER CoC IWP Unpacked pacth 16.03.2026` — распакованная `gamedata` без MO2 и DLTX, кодировка cp1251. Файлы там не изменяй. Конфиги IWP (55 стволов базового CoC) — не источник данных; используй их только для справки по моделям и анимациям.

| Что | Где |
|---|---|
| Мировые и HUD-модели | `meshes/dynamics/weapons/wpn_<name>/` — `wpn_<name>.ogf`, `_hud.ogf`, `_lod.ogf`, варианты под прицелы `wpn_<name>_<scope>[_hud].ogf` |
| HUD-анимации оружия | `meshes/dynamics/weapons/wpn_<name>/wpn_<name>_hud_animation.omf` (rig — HUD-модель ствола) |
| HUD-анимации рук | `wpn_hand_<name>_hud_animation.omf`, но не всегда в папке ствола: руки AUG (`aug_*`) лежат в `wpn_fn2000/wpn_hand_fn2000_hud_animation.omf`. Ищи через `iwp.js motions <имя>` (rig — `wpn_hand_01` проекта) |
| Текстуры | `textures/wpn/` |
| Звуки | `sounds/weapons/<ствол>/` |
| Партиклы | `particles.xr` |

Модели модулей в IWP (`wpn_upgrade/`) тоже лежат, но их **не берём** — модели модулей идут из GRA.

### Сопоставление GRA → IWP

- Обзор по стволам IWP: `node iwp.js iwpmap [regex]` — какие папки моделей GRA и сколько секций соответствуют каждой папке IWP.
- По секциям GRA: `node iwp.js match [regex]`. Статусы:
  - `MODEL` — тот же путь модели;
  - `MOVED` — тот же файл в другой папке (`wpn_aug_a3` лежит в `wpn_aug/`);
  - `FOLDER` — папка ствола совпала по имени или синониму, но файла с тем же именем нет;
  - `PREFIX` — имя папки GRA начинается с имени ствола IWP (`wpn_ak74m` → `wpn_ak74`, `wpn_scar_l_tac` → `wpn_scarh`). Это только кандидат: модель может быть другой модификацией, подтверди у пользователя;
  - `MISSING` — пары нет.
- Модели GRA нужны только для сопоставления: в проект идёт модель из IWP.
- В GRA прицелы сделаны отдельными секциями-вариантами со своим `visual` (`wpn_9a91_kobra`, `wpn_ak74_pso` и т.п.). В IWP варианты моделей называются по прицелам IWP (`_1p78`, `_ekp`, `_okp`, `_rakurs`, `_acog`, `_eot`, `_specter`, `_micro-t1`, `_ac10632`). Соответствие вариантов составляй явно; если однозначного нет, спроси.
- Имена моушенов GRA (`anm_*` в `wpn_<name>_hud`) относятся к анимациям GRA. Имена в OMF IWP смотри через `omfnames`, а по имени моушена ищи файл через `motions`.

## Ключи GRA

- Сетка: `inv_grid_width`, `inv_grid_height` (перевод в сетку проекта — см. «Размер в сетке»).
- Глушитель: `silencer_status` (`0` нет, `1` встроен, `2` съёмный), `silencer_name` (секция модуля, например `wpn_sil_tgp-a` из `configs/items/weapons/weapon_silencers.ltx`). Параметры выстрела с глушителем: `silencer_hit_power`, `silencer_hit_impulse`, `silencer_bullet_speed`, `snd_silncer_shot` (опечатка в ключе оригинальная).
- Прицел: `scope_status`, `scopes`, секции-варианты. Подствольник: `grenade_launcher_status`, `grenade_launcher_name`.
- Звуки — `snd_*`, часто через секции `*_sounds` в родителях: смотри `gra.js dump`.

## Утилита `scripts/iwp.js`

Запуск: `node .claude/skills/weapon-iwp/scripts/iwp.js <команда>`. Путь к паку переопределяется переменной `STALKER_IWP`. Данные GRA смотри через `gra.js` из `gra-research`.

| Команда | Что делает |
|---|---|
| `iwpmap [regex]` | для каждой папки оружия IWP — соответствующие папки моделей GRA и число секций (или `NO GRA SECTION`) |
| `match [regex]` | для каждой секции GRA — модель IWP: статус, HUD-модель, OMF, сетка и глушитель из GRA |
| `meshes [regex]` | файлы в `meshes/dynamics/weapons` с размером |
| `motions <name>...` | в каком OMF IWP лежит моушен |
| `omfnames <path-regex>` | имена из OMF (эвристика: строки с нулём на конце, без костей) |
| `orphans` | OGF IWP без ссылок из конфигов IWP |
| `weapons`, `addons`, `compat`, `sections`, `dump`, `get`, `grep`, `strings`, `sounds` | то же по конфигам IWP — только для справки |

## Импорт в Unity

- Образец сетапа — `Assets/_Source/Scripts/Editor/Integrations/Inventory/IwpWeaponSetup.cs` (AUG A3, меню `Tools/Inventory/Setup Test Weapon (AUG A3)`, batch `IwpWeaponSetup.SetupBatch`, лог `[IwpWeapon]`). Он делает следующее:
  - копирует чистые модели и OMF из IWP;
  - назначает rig;
  - генерирует материалы из текстур IWP;
  - проверяет анимации на HUD-модели и на всех руках из `OutfitCatalog`;
  - строит визуальные префабы без встроенных модулей;
  - создаёт `ItemDefinition` с `WeaponStatsComponent` (GRA) и `WeaponHudComponent` (IWP);
  - добавляет его в `ItemDatabase`, строит мировой префаб;
  - назначает звук `snd_on_take`, кладёт ствол в `OutdoorsScene` и запекает иконку.
- Модели и анимации импортируются только нашим X-Ray импортёром, по правилам скилла `xray-gra-import`: копировать файлы в `Assets`, без Blender/FBX и без своих импортёров. Модели оружия, OMF, звуки и эффекты оружия копируются из IWP; модели модулей — из файлов-победителей GRA (`gra.js winner`).
- Текстуры моделей IWP: при копировании `.ogf` в `Assets` автоматическая генерация ищет текстуры в MO2 GRA и может их не найти или взять чужие. Поэтому после копирования вызывай `XRayAssetGenerationCoordinator.Process`:
  - с источником `XRaySourceMode.CustomFolder` = корень IWP и `PreserveExistingMaterials = false`;
  - повторяй до `Completed`, потому что текстуры импортируются отложенно.
- Ограничение импортёра: bump в BC7 (`DX10 DXGI 98`) не декодируется, материал остаётся без карты нормалей. Так у AUG A3 (`aug_a3_bump.dds`). Импортёр без запроса пользователя не правь — сообщи об этом.
- Сетапы и тесты запускай через Unity Open MCP. Для batch-режима следуй `unity-batch-mirror`.
- Данные стволов и модулей храни в `ItemDefinition` и его компонентах, как у снаряжения. Правила оружия не клади в UI.
- Совместимость модулей бери из секций GRA, а не из имён файлов.
- Папки создавай рядом с существующими предметами: `Assets/_Source/Items/Weapons`, `Assets/_Source/Items/Addons`, `Assets/_Source/Items/Definitions/{Weapons,Addons}`.

## Модули GRA

Сетап `Assets/_Source/Scripts/Editor/Integrations/Inventory/GraWeaponAddonSetup.cs`: меню `Tools/Inventory/Setup GRA Weapon Addons`, batch `GraWeaponAddonSetup.SetupBatch`, лог `[WeaponAddons]`.

- **«Реально используется в игре»** — класс `WP_SILEN` / `WP_SCOPE` / `WP_GLAUN`, есть `visual`, и на секцию есть ссылка:
  - в оружии: `scopes`, `silencer_name`, `grenade_launcher_name` через `XRayLtxDatabase.GetList` (учитывает DLTX `>scopes = ...`; `GetValue` добавки НЕ видит — так в первый раз потерялись почти все прицелы);
  - в группе прицелов MAS: `modular_scope_group` оружия → `scopes` группы (`scope_group_*`);
  - в торговых и лут-списках (`configs/**` с `trade|death|treasure|loot|craft|stash|drop` в пути).
  - Исключены `scope_base` (без модели), `placeholder` («MAS инструмент разработчика»). Без ссылок сейчас только `wpn_addon_scope_mosin`.
  - Наборы переделки (`II_FOOD`: `archangel`, `pn23fore`, `susatov`, ...) в списках `scopes` — не модули, пишутся в лог.
  - Сейчас модулей 146 (прицелы, включая обычную оптику, ночные и тепловизоры, глушители, подствольники).
- **Модель модуля** — то, что ставится на оружие: `hud = <секция>_hud` → `item_visual` (`attachments\scopes\<имя>`, мод Pizza's Ultimate Sight / MAS). Если такого файла нет — `visual`.
  - `visual` у части прицелов — коробка (`wpn_case_*`) или чужая модель: у `ekp8_18` это модель ЕКП-8-02, у `0kp2`/`rmr` — коробка без текстуры. Поэтому `visual` — только запасной вариант.
- Ошибки моделей в самой GRA исправляются через таблицу `MESH_ONLY` в сетапе: секция → материал, который нужно оставить, остальные меши выключаются в префабе `Items/Addons/Visuals/<секция>.prefab`. Пример: `wpn_sil_spec_isg` — в моде Boomsticks его мировая модель — целый АК, из него берётся только меш глушителя `wpn_silencer_hexagon12`. Каждый модуль, для которого `ogftex` показывает текстуры оружия (`receiver`, `stock`, `magazine`, `handguard`), проверяй так же.
- Текстуры — через MO2 GRA обычной генерацией импортёра. Всё лежит в `Assets/_Source/Items/Addons/{Models,Prefabs}` (~1,1 ГБ, из них ~540 МБ — нормали, которые импортёр сохраняет несжатыми PNG).
- Определения — `Assets/_Source/Items/Definitions/Addons/<секция>.asset`:
  - категория `Attachment`, тег `Silencer` / `Scope` / `GrenadeLauncher`;
  - компонент `WeaponAddonComponent` с типом, ценой, множителями `*_k` и `scope_zoom_factor`;
  - звук `snd_on_take` (`attchsilen` / `attchscope` / `attchscopenv` / `attchgl` ...).
- В мир модули кладутся в группу `WorldItems/Addons` сцены `OutdoorsScene`: `WorldItemPrefabBuilder.AppendToScene`, свободное ровное место рядом с игроком. Остальные предметы `WorldItems` не трогаются, при повторном запуске группа пересоздаётся. Любой новый предмет должен лежать в мире, иначе его нельзя подобрать.
- После сетапа `ItemIconBatchBake.BakeAllAsync` перепекает каталог: в нём остаются только генерируемые иконки (оружие), старые иконки модулей удаляются.

## Оружие от первого лица (своя логика, не порт X-Ray)

Логика стрельбы своя; из IWP берутся только анимации, звуки, партиклы и камерные anm.

- Рантайм — сборка `RaidIntoDarkness.Weapons` (`Assets/_Source/Scripts/Gameplay/Weapons`):
  - `FirstPersonWeaponAsset` (SO, `Items/Weapons/FirstPerson/<секция>.asset`): HUD-префаб, смещения рук, калибровка прицела, моушены по видам (`FirstPersonWeaponMotionKind`), blend-anm прицеливания, rpm/режимы/магазин, префабы вспышки и дыма, отдача, звуки. Ссылка — `WeaponHudComponent._firstPersonWeapon`.
  - `FirstPersonWeaponController` на `FPS_Player`: состояния Hidden/Drawing/Ready/Reloading/SwitchingMode/Holstering, слоты Primary (`3`) / Secondary (`2`), ЛКМ — огонь, ПКМ (удержание) — прицел, `R` — перезарядка, `X` — режим огня. Держит руки через `FirstPersonRig.TryAcquireHands`.
  - `FirstPersonWeaponGraph` — Playables: слой рук (кроссфейд) + слой-оверлей левой руки с маской на поддеревья костей `l_*` (пальцы `bip01_l_finger*` входят как потомки `l_hand`; у `wpn_hand_01` один bone part `default`) + микшер оружия. Подбор FDDA `liz_pickup_left_wpn_hands` играет оверлеем, если руки держит оружие (`IFirstPersonHandsOverlay`).
  - Анимации экипировки (`EquipmentAnimationQueue`) просят руки через `FirstPersonRig.RequestHands` — оружие убирается и через 0,5 с свободных рук достаётся обратно.
  - Привязка к инвентарю — `FirstPersonWeaponBinder` (слоты `Primary`/`Secondary` → `SetSlotWeapon`).
  - Патроны пока условные: магазин из GRA `ammo_mag_size`, запас 90, по ключу `ItemInstance.InstanceId`; HUD `FirstPersonWeaponAmmoView` («30/90», TMP, `FPS_Player/WeaponHud/Ammo`).
- Отдача — порт `WeaponRecoilManager`/`IRecoil` из BiohazardProtocol (`StalkerCameraRecoil` на `CameraRoot`, применяется в `StalkerCameraController.Apply`). Значения по умолчанию как у MDR из BiohazardProtocol: `(4, 4, 2)`, snappiness 13, return 9; в присяде вдвое меньше.
- **HUD-смещения для анимаций IWP:** `hands_position/orientation` = 0 (поза зашита в анимации; значения GRA сдвигают ствол). `anm_idle_aim` IWP не прицельная поза — прицеливание даёт blend-anm `anims/blend/aim_{start,idle,end}_<тип>` в пространстве рук (как `player_hud` script layers модовых exe). Тип для AUG — `f2000`. После blend ось `wpn_scope` стоит на уровне глаз, но со сдвигом по X; поправка подбирается по скриншоту (у AUG `aim offset = (0.0127, -0.006, 0)` в пространстве `HandsRoot`, мушка в кольце целика по центру).
- Моушены: руки `aug_*` из `wpn_hand_fn2000_hud_animation.omf`, оружие — сначала OMF A3 (`idle`, `idle_empty`, `reload`, `reload_empty`, `misfire`), недостающие (`draw`, `shoot`, `sprint*`, `firemode`) из `wpn_aug_hud_animation.omf` (A1) с rig A3. Камерные anm — `anims/camera_effects/weapon/aug_*.anm`.
- Звуки: выстрел — варианты `aug_muzzle_stereo*` (случайный); пустой магазин — `aug_empty` (не чаще `rpm_empty_click`); на последнем патроне — `last_shell_out`, при ≤5 патронах — `mag_shot` поверх выстрела (в скриптах/конфигах IWP механики нет, это наше решение по файлам пака). `snd_draw/holster` GRA ищутся в IWP по имени, иначе `assault_draw/holster`.
- Сетап: `IwpFirstPersonWeaponSetup` (меню `Tools/Inventory/Setup First Person Weapon (AUG A3)`, batch `SetupBatch`, лог `[FirstPersonWeapon]`), требует результата `IwpWeaponSetup`. Тест — `OutdoorsSceneWeaponTests`.

### Партиклы X-Ray

- Импорт `particles.xr` IWP: `XRayParticleImporter` (меню `Tools/Effects/Import IWP Weapon Particles`, лог `[XRayParticles]`), читалка `XRayParticleLibraryReader`. Берутся `weapons\*`, кроме гильз (`*shell*`, калиберные `weapons\<калибр>` и `weapons\effects\<калибр>`).
- Результат — `Assets/_Source/Effects/XRay/Particles/<путь X-Ray>.{asset,mat,prefab}`, текстуры — `Effects/XRay/Textures` (DDS с sRGB-заголовком; DDS с неполной mip-цепочкой Unity не импортирует — конвертируются в PNG).
- Рантайм `RaidIntoDarkness.Effects`: `XRayParticleSimulation` — порт CParticleEffect (шаг 0,033 с, действия Source/KillOld/Move/Target*/Gravity…), `XRayParticleGroupPlayer` — таймлайн CParticleGroup (t0/t1, deferred stop), отрисовка через `ParticleSystem.SetParticles` и шейдер `RaidIntoDarkness/XRayParticle` (кадры атласа в вершинном шейдере). `xdistort` не рисуется.
- `ALIGN_TO_PATH` рисуется как в `CParticleEffect::Render`, а не Stretch-билбордом Unity (тот вытягивал «звезду» `weapon_shot_invert_*` вдоль ствола назад, в цевьё): при скорости > 0 и `FACE_ALIGN` — плоскость поперёк скорости с поворотом `rot.x`; иначе — спрайт вдоль скорости или `m_APDefaultRotation` (чанк PED `0x25`), развёрнутый к камере. Частицы — World-aligned billboard с `rotation3D`.
- Аддитивные материалы получают `_Intensity = 4` — без этого вспышка в HDRP почти не видна.
- Точка вспышки — Transform `FirstPersonWeaponView._muzzle` на HUD-префабе (`Items/Weapons/Visuals/<секция>_hud.prefab`, объект `MuzzlePoint` под костью `wpn_silencer`, forward = направление пламени). Его двигают в префабе руками; `IwpFirstPersonWeaponSetup` создаёт точку только если её нет, `IwpWeaponSetup` при пересоздании визуала сохраняет её родителя и локальную позу. Без компонента используется кость `FirstPersonWeaponAsset._muzzleBone`.
- Ориентация дульных эффектов как у X-Ray (`setup_firedeps` + `generate_orthonormal_basis_normalized`): ось Z — видимое направление ствола (`TransformVector` с учётом неравномерного масштаба `HandsRoot` под hud_fov), «верх» — мировой, а не крен оружия. Вспышка, которая следует за стволом, переориентируется каждый кадр.
- Вспышка AUG: `flame_particles = weapons\generic_weapon05` (следует за костью `wpn_silencer`), дым `smoke_particles = weapons\generic_shoot_00` (на месте выстрела). У глушителя `silencer_flame_particles` пуст — вспышки нет.

## Проверка перед сдачей

- Характеристики совпадают с секциями GRA; модели оружия, анимации, звуки и эффекты оружия взяты из IWP, модели модулей — из GRA.
- В проект скопированы только чистые модели (без `_<прицел>` и прочих вариантов); встроенные меши модулей выключены в визуальных префабах.
- Для каждого ствола со съёмным глушителем: с глушителем ширина на 1 больше базовой, без него — базовая.
- Глушитель расширяет предмет вправо, а если справа занято — влево. При нехватке места установка отклоняется.
- Иконка варианта с глушителем на клетку шире и совпадает с размером в сетке.
- У встроенных глушителей размер не меняется.
- Нет импортированных иконок оружия и модулей.
