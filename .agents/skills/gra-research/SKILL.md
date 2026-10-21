---
name: gra-research
description: Use whenever a task needs data, formulas or behaviour from the original game - S.T.A.L.K.E.R. Anomaly 1.5.3 with the GRA 2.5 modpack (MO2) - "как в ГРА", "как в оригинале", "найди в модпаке/в моде", "какой мод отвечает за", "посмотри мои настройки в игре/MCM", configs, Lua scripts, models, textures, sounds, animations, engine formulas (xray-monolith). Gives MO2 winner resolution, DLTX-aware LTX lookup, string tables, user settings and engine source access.
---

# Исследование оригинальной GRA

Источник истины для геймплея, UI и данных — реальная игра пользователя: Anomaly 1.5.3 + модпак GRA 2.5 в MO2. Не выдумывай значения и формулы и не бери их из туториалов или OpenXRay по умолчанию. Всегда находи файл-победитель и называй мод-источник в ответе.

## Где что лежит

- Базовая игра (распакованная, = `gamedata`): `D:\GRA2.5\Anomaly 1.5.3 up\tools\_unpacked`.
- MO2: `D:\GRA2.5\MO2` (`mods`, `profiles`, `overwrite`). Выбранный профиль хранится в `ModOrganizer.ini` → `selected_profile`; сейчас это `1v2(DX11) (v_12gb) 2.5, Все включено (168)`, 174 включённых мода.
- Консольные настройки игрока (`fov`, `hud_fov`, `mouse_sens`, `g_nearwall` и т.д.): `D:\GRA2.5\Anomaly 1.5.3 up\appdata\user.ltx`.
- Настройки MCM: `D:\GRA2.5\MO2\overwrite\gamedata\configs\axr_options.ltx` (`fddar/...`, `swm_legs/...`, `video/player/...`).
- Движок: `bin\AnomalyDX11.exe` (+ `.pdb`) — это **xray-monolith** (themrdemonized). В exe и pdb можно искать символы через `grep -a`.
- Архивы `.db0` резолвер не индексирует. Распаковка: `"D:\GRA2.5\Anomaly 1.5.3 up\tools\converter.exe" -unpack -xdb <файл>` в отдельную папку. Например базовый `material_pairs.ltx` есть только в `db/mods/00_modded_exes_gamedata.db0`.

## Правила разрешения MO2

- `modlist.txt`: строки с `+` включены, с `-` выключены; **первая строка — наивысший приоритет**.
- Победитель файла: `overwrite` → моды по порядку `modlist.txt` → базовая игра. Выключенные моды игнорируются.
- «Реально используется в игре» = файл-победитель плюс то, что на него действительно ссылаются конфиги или скрипты. Копии, перекрытые приоритетом, не считаются.

## LTX / DLTX

- Корень `configs/system.ltx`, `#include` относительно папки файла, поддерживаются маски `*`.
- После system подмешиваются `configs/mod_system_*.ltx` (по имени файла).
- Заголовки:
  - `![sec]` — изменить существующую секцию (пропуск, если её нет);
  - `@[sec]` — создать или изменить;
  - `!![sec]` и `[sec]` — заменить.
- Ключи: `!key` удаляет, `>key` дописывает в список, `<key` удаляет из списка.
- Наследование `[sec]:parent1,parent2`: при поиске ключа приоритет у последнего родителя. Кодировка cp1251, комментарии `;` и `//`.
- Отдельные `ini_file(...)` (например `items\items\headgear_blacklist.ltx`) в system не входят — читай напрямую.

## Утилита `scripts/gra.js`

Запуск: `node .claude/skills/gra-research/scripts/gra.js <команда>`. Все команды работают только с файлами-победителями.

| Команда | Что делает |
|---|---|
| `profile` | профиль и число модов |
| `winner <rel>...` | все кандидаты файла с md5/размером, отмечен победитель |
| `find <regex> [subdir] [.ext]` | поиск виртуальных путей среди победителей |
| `grep <regex> <subdir> [.ext]` | grep только по победителям (например `grep "add_pp_effector" scripts .script`) |
| `get <section> <key>` | значение с учётом DLTX и наследования |
| `dump <section>...` | все ключи секции, родители, файл определения |
| `sections <regex>` | список секций по шаблону |
| `strings <id> \| <prefix*>` | русские строки из `configs/text/rus` |
| `motions <name>...` | в каком OMF лежит моушен (HUD, руки, тело) |
| `ogftex <rel\|path>...` | текстуры и шейдеры OGF (по вложенным чанкам) |
| `settings <regex>` | строки из `user.ltx` и `axr_options.ltx` |

Модуль экспортирует `candidates`, `winner`, `winners`, `ltx`, `strings`, `motions`, `ogfTextures` — для одноразовых скриптов в scratchpad делай `require` этого файла, а не копию логики.

## Исходники движка

- `sh .claude/skills/gra-research/scripts/monolith.sh src/xrGame/HudItem.cpp` — скачивает файл в кэш и печатает путь.
- `sh .../monolith.sh search <символ>` — `gh search code` по репозиторию.
- Monolith отличается от OpenXRay. Например, HUD рисуется с FOV `hud_fov * 83°` независимо от `fov`, а у предметов есть per-section `hud_fov` и nearwall. Проверяй именно monolith.

## Известная карта модов (проверяй победителей заново, порядок может меняться)

- `[Анимации] FDDA Redone 1.4.1` — HUD-анимации:
  - подбор предмета;
  - костюм, шлемы и противогазы, рюкзак, нашивки, пластины;
  - очередь `lam2`, хуки инвентаря `zzzz_liz_fdda_redone_ui_inventory`;
  - секции `*_headgear_hud`, `outfit_animation_*`, затемнение `deimos_short.ppe`.
- `[Модели] Swm_Visible_Body` — тело от первого лица (`<outfit>_legs`, `swm_legs.script`).
- `[Руки] T.H.A.P. Rework` и другие — руки (`player_hud_section` → `[actor_hud_*] visual`). Цвета рук и тела в GRA иногда не совпадают — это особенность модпака, не ошибка импорта.
- `[Интерфейс] HD GRA Иконки` — иконки снаряжения (`textures/ui`).
- SortingPlus.34 — сортировка инвентаря; `context-menu-overhaul-1.4.2` — ПКМ-меню.
- Better Stats Bars и Momopate's Detailed Weapon Stats — формулы характеристик и статус-баров в тултипах.
- `[Звук] Dark_Signal_Soundscape` — победитель большинства звуков actor.

## HUD-секции скриптовых анимаций (движок)

- `anm_* = <моушен рук>, <моушен предмета>`.
- `item_visual` крепится к кости рук `lead_gun` (`ancor_0` в `[actor_hud]`); корневая кость предмета принудительно identity.
- Смещение рук: `hands_position_16x9` / `hands_orientation_16x9` (HPB, градусы).
- Векторы X-Ray переносятся в Unity 1:1 (как `XRayUnitySpace`).

## Как отвечать

- Указывай мод-победитель, путь файла и ключевые значения. Если пользователь пишет «в оригинале так же», это проверка в игре — прими как факт.
- Для массовых сверок пиши node-скрипты в scratchpad поверх `gra.js` и выдавай таблицу или CSV в scratchpad с полным путём.
- Ничего не меняй в `D:\GRA2.5` — только чтение.
