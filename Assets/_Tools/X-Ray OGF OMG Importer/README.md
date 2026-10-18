# X-Ray OGF/OMF Importer

Импорт моделей (`*.ogf`) и анимаций (`*.omf`) S.T.A.L.K.E.R. в Unity 6000.3 / HDRP 17.3 и автоматическая сборка текстур и HDRP-материалов из распакованной Anomaly и модпака Mod Organizer 2.

## Сборки

| Сборка | Платформа | Назначение |
| --- | --- | --- |
| `XRay.Importer.Core` | все, без UnityEngine | Парсинг OGF/OMF/ANM, математика скелета, вычисление envelope ANM |
| `XRay.Importer.Runtime` | все | `XRayModel`, `XRayMotionLibrary`, `XRayRigPlayer`, `XRayHudAnimator`, `XRayAnmAsset`, `XRayAnmPlayer` |
| `XRay.Importer.Sources` | Editor, без UnityEngine | MO2, виртуальная файловая система, индекс, `textures.ltx`/`.thm`, DDS, правила шейдеров |
| `XRay.Importer.Editor` | Editor | `ScriptedImporter`, генерация ассетов, окна и инспекторы |
| `XRay.Importer.Tests.Editor` | Editor | EditMode-тесты |

## Быстрый старт

1. `Tools > X-Ray > Asset Sources`.
2. **Base Anomaly Folder** — распакованная `gamedata` (папка, в которой лежит `textures`), например `D:\GRA2.5\Anomaly 1.5.3 up\tools\_unpacked`.
3. **MO2 Root Folder** — папка Mod Organizer 2, например `D:\GRA2.5\MO2`. Нажмите **Detect MO2**.
4. **MO2 Profile** — `<Active MO2 profile>` (берётся `selected_profile` из `ModOrganizer.ini`) или конкретный профиль.
5. Перетащите `.ogf` в проект. Рядом с моделью появятся `Textures/` и `Materials/`, материалы назначатся мешам автоматически.

Пути и профиль хранятся в `UserSettings/XRayAssetSources.asset` (не попадает в Git). Режим источников и опции импорта по умолчанию — в `ProjectSettings/XRayAssetSources.asset`; каждый пользователь может переопределить их галочками **Override Source Mode** / **Override For Me**.

## Поиск ресурсов MO2

`Mo2InstallationReader`:

* Portable: `ModOrganizer.ini` лежит в указанной папке. Instance: если в папке только `ModOrganizer.exe`, ищется `%LOCALAPPDATA%\ModOrganizer\<instance>\ModOrganizer.ini` (при нескольких — выбор в окне).
* Значения QSettings декодируются полностью: `@ByteArray(...)` с `\xHH` UTF-8 (кириллические имена профилей), `%20` в ключах, кавычки, `\\`.
* `[Settings] base_directory / mod_directory / profiles_directory / overwrite_directory` поддерживают `%BASE_DIR%`, абсолютные и относительные пути; по умолчанию `<base>/mods`, `<base>/profiles`, `<base>/overwrite`.
* `skip_file_suffixes` (по умолчанию `.mohidden`) и `skip_directories` (`.git`) применяются при индексации.

`Mo2ProfileReader` читает `profiles/<profile>/modlist.txt`:

* `+name` включён, `-name` выключен (игнорируется), `*name` неуправляемый (игнорируется, его файлы лежат в папке игры), `*_separator` — разделитель.
* MO2 записывает список в обратном порядке: **первая строка — наивысший приоритет**. Приоритет считается как в левой панели MO2: `priority = count - 1 - index` (разделители занимают слоты, как в MO2). Проверено на реальном профиле: базовые моды (`[Основа] GRA`, приоритет 4) внизу файла проигрывают модам выше (например, `GRA Bolt` 197).

`Mo2VirtualFileSystem` строит упорядоченный список источников:

1. `overwrite` (выше всех модов);
2. включённые моды по убыванию приоритета;
3. loose-файлы `<gamePath>/gamedata` (опция **Include Game Gamedata**);
4. Base Anomaly (содержимое `.db`-архивов игры).

Плагин MO2 для STALKER Anomaly отображает корень мода в корень игры, поэтому учитывается только `mods/<Mod>/gamedata/...`. Моды с `textures/` прямо в корне считаются невидимыми для игры и попадают в диагностику; опция **Treat Mod Root As Gamedata** нужна только для плагинов, которые отображают корень мода в `gamedata`.

## Индекс

`XRayAssetIndex` сканирует `textures/` и `textures.ltx` каждого источника. Ключ — нормализованный путь (нижний регистр, `/`). Для каждого файла хранятся все кандидаты в порядке источников: первый — победитель, остальные — проигравшие. Два файла одного источника, отличающиеся только регистром, помечаются как ambiguous; выбирается ординально меньший путь.

* Кэш: в памяти и в `Library/XRayImporter/AssetIndex.bin` (данные игры и MO2 не изменяются).
* Инвалидация по отпечатку: настройки, профиль, `ModOrganizer.ini`, `modlist.txt`, время изменения папок модов, их `gamedata` и `gamedata/textures`.
* Добавление файла глубоко внутри мода может не изменить отпечаток — используйте **Rebuild Index**.
* Реальный профиль GRA (172 активных мода): 33 655 файлов, 6 136 конфликтов, построение около 0,2 с при прогретом файловом кэше ОС.

## Текстуры

`XRayTextureResolver`:

* Диффуз: `textures/<name>.dds`.
* Bump: как в движке, `.thm` (`THM_CHUNK_BUMP`, режимы `use` / `use_parallax`) имеет приоритет над `textures.ltx [specification] bump_mode[use:...]`. Для bump ищутся `<bump>.dds` и `<bump>#.dds`. Bump по соглашению об именах (`_bump`) не выдумывается.
* `detail` из `.thm` / `[association]` выводится в отчёт, но не применяется (формат HDRP detail map другой).

DDS (`XRay.Importer.Sources.Dds`):

| Формат | Цветовая карта | Технические карты |
| --- | --- | --- |
| DXT1 / BC1, DXT5 / BC3, BC7 | копия DDS; legacy-заголовок переписывается в DX10 `*_UNORM_SRGB` без изменения блоков | декодируется |
| ATI1/BC4, ATI2/BC5, BC6H | копия DDS | BC4/BC5 декодируются |
| DXT2/DXT3 (BC2), несжатые RGBA/BGRA/BGRX/BGR/565/5551/4444/L8/A8L8/A8 | декодируется и сохраняется как PNG | декодируется |
| кубмапы, volume, массивы, неизвестные FourCC | `Unsupported` в отчёте | `Unsupported` |

Проверено тестами: Unity импортирует DX10 `BC1_UNORM_SRGB` как `RGBA_DXT1_SRGB`, а ориентация скопированного DDS совпадает с конвертированным PNG.

X-Ray bump не является обычной normal map. Конвертация по шейдерам движка:

```
Ne = (bump.a, bump.b, bump.g) - 0.5 + (bump#.rgb - 0.5)   // bump# сэмплируется с тем же UV (билинейно, wrap)
N  = normalize(Ne)
```

Результат пишется как PNG с типом `Normal Map` (Linear). Опция **Flip Normal Green (Y)** включена по умолчанию: импортёр переворачивает V (`1 - v`), поэтому бинормаль Unity противоположна бинормали X-Ray. Gloss (`bump.r`) и height (`bump#.a`) не используются: это не данные HDRP smoothness и mask map.

## Материалы

`XRayShaderRules` — отдельный расширяемый модуль: `Add` / `Insert` своих `XRayShaderRule`.

| Шейдер OGF | Поверхность HDRP/Lit |
| --- | --- |
| `models\model`, `models\weapons`, `model_pn`, `objects`, `vehicles`, ... | Opaque |
| `*aref*` | Opaque + Alpha Clipping 0.5 |
| `pautina`, `model_fur` | Alpha Clipping, двусторонний |
| `transparent`, `window`, `*glass*`, `*lens*` | Transparent, Alpha blend |
| `collimator`, `crosshair`, `lfo_light_dot*` | Transparent Additive + emission по диффузу, 9 EV100 (аппроксимация) |
| `selflight*` | Opaque + emission 6 EV100 (аппроксимация) |
| `lightplanes` | Transparent Additive, двусторонний |
| `xdistort*`, `xanomaly`, `xmonolith` | Transparent (искажение не воспроизводится) |
| неизвестный | Opaque, отметка `unknown` в отчёте |

Имя материала берётся из имени диффуза; для поверхностей, отличных от обычной Opaque, добавляется суффикс шейдера (`lens_transparent.mat`). Материал хранит ключ `texture|shader` в `userData` своего `.meta`.

## Порядок работы с AssetDatabase

1. `XRayOgfImporter.OnImportAsset` строит модель и не создаёт внешних ассетов. Для каждого слота `texture|shader` используется материал из External Object Map (`AddRemap`); если его ещё нет — встроенный fallback-материал.
2. `XRayAssetPostprocessor.OnPostprocessAllAssets` вызывает `XRayAssetGenerationCoordinator` для импортированных `.ogf`, у которых не все слоты назначены.
3. Координатор разрешает текстуры, копирует или конвертирует файлы в `Textures/` и запускает их импорт. Настройки текстурного импортёра и маркер источника (`userData`) применяются в `OnPreprocessAsset` при первом импорте.
4. Если текстуры ещё не импортированы (импорт отложен до следующей итерации refresh), модель записывается в `Library/XRayImporter/PendingGeneration.json` и обрабатывается, когда эти текстуры появятся в `importedAssets` или после domain reload.
5. Создаются или переиспользуются `.mat`, выполняется `AddRemap` + `SaveAndReimport` модели только при изменении назначений. Следующий импорт модели не находит работы — цикл завершён.

Защита от циклов: максимум 4 попытки ожидания текстур, счётчик генераций в `SessionState`, флаг повторного входа. Работает в batch mode (проверено `-runTests`).

## Повторное использование и обновление

* Ассеты идентифицируются ключом в `userData` (`xray-asset:{...}`): виртуальный путь + роль, источник, путь и отметка исходного файла.
* Сценарий A: два OGF в одной папке с одной текстурой используют одну текстуру и один материал.
* Сценарий B: разные виртуальные пути с одинаковым именем файла получают разные имена (`body_diff.dds`, `ak74m_body_diff.dds`, далее полный путь и номер). Существующий ассет сохраняет своё имя и GUID.
* Файлы без маркера считаются пользовательскими: они не перезаписываются и не удаляются.
* Автоматический реимпорт не перезаписывает текстуры. **Resolve Resources** в инспекторе модели или **Refresh Resources Of All Models** в окне перекопируют текстуры, у которых изменился победитель или файл (тот же путь и GUID). Опция **Overwrite Existing Textures** включает такое поведение и для авто-импорта.
* **Preserve Existing Materials** (включена): существующие `.mat` не изменяются. Если выключить, при обновлении переназначаются текстуры и настройки поверхности.
* Ничего не удаляется автоматически.

## Диагностика

* Инспектор `.ogf` → **X-Ray Resource Resolution**: число материалов, разрешённых и отсутствующих текстур. По каждому слоту показываются шейдер и правило, источник (мод и приоритет), проигравшие моды, полный путь, статус (`Imported` / `Reused` / `Updated` / `Converted` / `Missing` / `Unsupported`), ассеты, заметки.
* Отчёты: `Library/XRayImporter/Reports/<guid>.json`.
* Окно Asset Sources: состояние MO2, статистика индекса, **Validate Sources**, **Show Missing Resources**, **Clear Cache**.
* Архивы `.db`/`.xdb` в модах не индексируются. Отсутствующая текстура получает заметку, если такие архивы есть (в реальном профиле — 3 источника).

## Анимации объектов `.anm`

`XRayAnmImporter` (`ScriptedImporter`, расширение `anm`) импортирует X-Ray object motions: камерные эффекты, сценарные камеры, пути объектов. Импортёр не связан с OMF и скелетами.

```text
camera_shoot.anm
├── camera_shoot (GameObject, main: Transform + Animator + XRayAnmPlayer [+ дочерняя Camera])
├── camera_shoot (AnimationClip: m_LocalPosition + m_LocalRotation)
└── camera_shoot (XRayAnmAsset: заголовок, статистика каналов, исходные кривые)
```

Без **Create Preview Object** главным ассетом становится `XRayAnmAsset`.

### Формат (Core, `Runtime/Core/Anm`, без UnityEngine)

* Chunk `0x1100`: `name` (stringZ cp1251), `frame_start` и `frame_end` (`int32`, поэтому `0xFFFFFFFF` читается как `-1`), `fps` (`f32`), `version` (`u16`).
* Шесть envelope: behaviour pre/post (`u8`×2 в v4/v5, `int32`×2 в v3) и число ключей (`u16` / `u32`). Ключ: `value` (`f32`), `time` (`f32`, секунды) и `shape` (`u8` / `u32 & 0xFF`). В v4/v5 дальше идут 7 значений `q16` в диапазоне [-32, 32] (`tension`, `continuity`, `bias`, `param[0..3]`), у Stepped их нет. В v3 это 7 × `f32`.
* Порядок каналов, как в `COMotion::Load` движка: v3 и v5 — `X, Y, Z, H, P, B`; v4 — `X, Y, Z, P, H, B`. blender-xray читает все версии в порядке v5, поэтому v4-файлы в Blender получают перепутанные H и P.
* Проверяются границы данных, число ключей относительно размера буфера, версия, FPS и наличие chunk. Ошибка указывает файл, chunk и смещение.

### Кривые

`XRayAnmEnvelope` — порт `evalEnvelope` из OpenXRay (`xrCore/Animation/interp.cpp`):

* Формы: TCB (Kochanek–Bartels), Hermite, Bezier 1D, Bezier 2D, Linear, Stepped. Форма сегмента берётся по конечному ключу, исходящая касательная — по начальному.
* Behaviours: Reset, Constant, Repeat, Oscillate (нечётный цикл отражается по формуле OpenXRay), Offset Repeat, Linear.
* Bezier 2D: время ищется бисекцией до 1e-9 с. В движке допуск 1e-4 с, что даёт шум до ~1e-4 по значению; более точное решение кривую не меняет.

| `XRayAnmSemantics` | Поведение |
| --- | --- |
| `Engine` (по умолчанию) | Как в движке |
| `BlenderXRay` | Как в blender-xray 2.39: TCB на краях `0.5·(a+b)·d` вместо `b·d` / `a·d`, исходящая касательная Linear-ключа равна 0, временные ручки Bezier 2D трактуются в кадрах, v4 в порядке v5, периодические behaviours заменяются на Constant |

### Координаты

X-Ray model space совпадает с пространством Unity: левая система, Y вверх, Z вперёд. Поэтому `localPosition = (X, Y, Z) · scale`, `localRotation = Ry(H) · Rx(P) · Rz(B)` = `Quaternion.Euler(P°, H°, B°)`.

* Совпадает с матрицей движка `setHPB(-H, -P, -B)` (`setXYZi` в `CObjectAnimator`), что проверено тестом на 200 случайных углах.
* Совпадает с blender-xray: Blender хранит `location = (X, Z, Y)`, `rotation_euler = (-P, -B, -H)` в режиме `YXZ`. Сопряжение матрицы Blender перестановкой Y и Z даёт ту же матрицу.
* Связанная камера blender-xray повёрнута на `(90°, 0, 0)`, потому что камера Blender смотрит по −Z. Камера Unity смотрит по +Z, поэтому поправка не нужна: Camera — дочерний объект с единичной трансформацией. FOV камеры предпросмотра (67.5°, `SOC_LEVEL_FOV` blender-xray) — настройка импортёра, ANM его не содержит.

### AnimationClip

* **Exact**, позиции: ключи Unity ставятся в моменты ключей ANM. TCB, Hermite и Bezier 1D задаются аналитическими касательными (`out/Δt`, `in/Δt`), поэтому кубика Эрмита Unity совпадает с кубикой X-Ray. Linear задаётся наклоном, Stepped — касательными `Constant`, Bezier 2D — weighted-касательными с контрольными точками X-Ray. Участки без точного представления (Bezier 2D, обрезанный диапазоном кадров или с весами вне [0, 1], периодические behaviours) семплируются адаптивно.
* Вращение: композицию трёх эйлеровых кривых нельзя точно записать ключами Unity, а Euler-кривые Unity при импорте пересэмплирует (~0.04°). Поэтому пишутся кривые `m_LocalRotation`:
  * ключи ставятся в моменты ключей ANM;
  * интервалы делятся, пока ошибка угла не станет ≤ 0.001°;
  * знаки кватернионов выравниваются, поэтому переходы через ±180° и многократные обороты непрерывны;
  * ступенчатые скачки переносятся парой ключей с интервалом 10 мкс и `Constant`-касательной.
* **Adaptive**: всё семплируется с проверкой ошибки, позиции с допуском 1e-5 · scale. **Fixed FPS**: один ключ на кадр ANM, наименее точный вариант.
* Время клипа `τ = t − frame_start / fps`, длина `(frame_end − frame_start) / fps`, `frameRate` берётся из ANM. Галочки `Loop` и `Loop Pose` задают `loopTime` и `loopBlend`.

### Runtime

* `XRayAnmAsset.Evaluate(clipTime)` вычисляет позу точно, через Core, по сохранённым исходным кривым (**Preserve Original Curves**).
* `XRayAnmPlayer` (без PlayableGraph):
  * управление: `Play` / `Stop` / `Pause` / `Resume` / `Seek`, `Speed`, `Loop`, `Weight`, смена `Asset`, старт с заданного времени;
  * `Tick(deltaTime)` (при `DriveSelf` = false) для ручного или синхронного обновления;
  * после остановки восстанавливает исходную локальную позу цели.
* Режимы:
  * **Absolute**: локальная поза цели = поза ANM; `Weight` интерполирует от исходной позы.
  * **Additive**: поза = исходная · Δ, где Δ — движение относительно первого кадра (`RelativeToFirstFrame`), смешанное с единицей по `Weight`.
  * `PlayEffect` добавляет одновременные additive-эффекты (recoil, shake, bob); они перемножаются по порядку.
* FPS-камера: `Player → CameraRoot (мышь) → CameraEffectsPivot (XRayAnmPlayer, Additive) → MainCamera`. Эффект меняет только pivot, мышь управляет `CameraRoot`, после остановки pivot возвращается в исходное положение.
* AnimationClip — обычный generic-клип для Animator, Timeline и Playables, в том числе для `XRayRigPlayer.Play(clip)` на объекте с Animator.

### Проверка по blender-xray

`Tools~/blender_export_anm_reference.py` (Blender 3.6, blender-xray 2.39, `camera_animation=True`) выгружает fcurves, `matrix_basis`, `matrix_world` объекта и связанной камеры на каждом кадре и в долях кадра 0.25 / 0.5 / 0.75. Тесты `AnmValidationTests` (категория `RealData`) берут JSON из `XRAY_ANM_REFERENCE_DIR` и ANM из `XRAY_ANM_FILES_DIR`.

blender-xray запекает нелинейные формы только в целых кадрах, а между ними Blender интерполирует свои ключи сам. Поэтому точное сравнение возможно только в целых кадрах.

## Тесты

`Window > General > Test Runner > EditMode`, или:

```
Unity.exe -batchmode -projectPath <project> -runTests -testPlatform EditMode -testResults results.xml
```

Синтетические MO2-структуры создаются во временной папке ОС, тестовые модели — в `Assets/__XRayTests_tmp` и удаляются после теста. Категория `RealData` читает настоящие `D:\GRA2.5\...` только на чтение и пропускается, если папок нет. Реальный AK-74 копируется в тестовую папку проекта.

## Ограничения

* Архивы игры и модов (`.db`, `.xdb`) не читаются; эквивалентность MO2 VFS — только для loose-файлов.
* Collimator, self-light и distortion — аппроксимации HDRP, а не точное воспроизведение шейдеров X-Ray.
* Направление зелёного канала normal map выведено из конвенций UV и не сверено визуально с игрой; при необходимости отключите **Flip Normal Green**.
* Smoothness / Mask Map не генерируются: X-Ray gloss не является HDRP smoothness.
* Изменение файла глубоко в моде без изменения времени папок требует **Rebuild Index**.
* Текстуры и материалы создаются отдельно для каждой папки с моделями (структура «рядом с OGF»).
* ANM: shape вне диапазона 0..5 вычисляется как 0 (как в движке) с предупреждением; Reset-скачок на границе ключей в клипе сглаживается адаптивной выборкой. ANM не содержит FOV и других параметров камеры.
