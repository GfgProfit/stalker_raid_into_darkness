# Changelog

## 2.1.0 — 2026-10-18

### Added

- Импорт X-Ray object motions `.anm` (v3, v4, v5): `XRayAnmImporter`, Core-парсер и вычислитель envelope (TCB, Hermite, Bezier 1D/2D, Linear, Stepped, все behaviours) с режимами `Engine` и `BlenderXRay`, AnimationClip с аналитическими позиционными и адаптивными кватернионными кривыми, `XRayAnmAsset` с исходными кривыми, объект предпросмотра и опциональная камера.
- `XRayAnmPlayer`: точное runtime-воспроизведение, режимы Absolute и Additive, одновременные эффекты, восстановление позы.
- Инспектор `.anm` со статистикой каналов и кнопкой **Create Preview In Scene**.
- `Tools~/blender_export_anm_reference.py` и тесты сравнения с blender-xray.

### Changed

- `XRay.Importer.Runtime` ссылается на `XRay.Importer.Core`.

## 2.0.0 — 2026-10-18

### Added

- Сборка `XRay.Importer.Sources` (Editor, без UnityEngine): чтение `ModOrganizer.ini` (portable и instance, QSettings `@ByteArray`, `%BASE_DIR%`), профилей и `modlist.txt`, виртуальная файловая система MO2 (overwrite > моды по приоритету > `<game>/gamedata` > Base Anomaly), индекс с кэшем в `Library/XRayImporter`, разбор `textures.ltx` и `.thm`, DDS-парсер и декодер (BC1–BC5, несжатые форматы), конвертер X-Ray bump/bump# в tangent-space normal map, расширяемые правила шейдеров.
- Автоматическое создание `Textures/` и `Materials/` рядом с `.ogf`, копирование DDS (с явным sRGB DX10-заголовком для цветовых карт), конвертация неподдерживаемых Unity форматов и bump в PNG, генерация HDRP/Lit материалов и назначение их мешам через External Object Map.
- `XRayAssetPostprocessor` и `XRayAssetGenerationCoordinator`: генерация ассетов вне `OnImportAsset`, отложенная обработка до импорта текстур, защита от циклов, работа в batch mode.
- Окно `Tools > X-Ray > Asset Sources`: источники, профиль MO2, режим, опции импорта, Detect MO2, Select Profile, Rebuild Index, Validate Sources, Show Missing Resources, Clear Cache, Refresh Resources Of All Models.
- Раздел **X-Ray Resource Resolution** в инспекторе `.ogf`: источник каждой текстуры, проигравшие моды, пути, статусы, кнопка **Resolve Resources**.
- Настройки: `ProjectSettings/XRayAssetSources.asset` (проектные) и `UserSettings/XRayAssetSources.asset` (пути и переопределения пользователя).
- EditMode-тесты (`XRay.Importer.Tests.Editor`): MO2, профили, приоритеты, VFS, индекс, текстуры, DDS, bump, правила шейдеров, генерация и назначение материалов, сохранение правок, отсутствие дубликатов и циклов, смена профиля, стабильность реимпорта, реальные данные Anomaly/GRA (только чтение).

### Changed

- `XRayOgfImporter`: версия импортёра 2; новая настройка `Resolve X-Ray Resources`; назначенные внешние материалы имеют приоритет над встроенными fallback-материалами.
