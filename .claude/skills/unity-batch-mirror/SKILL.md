---
name: unity-batch-mirror
description: Use whenever Unity must run in batch mode for this project (executeMethod setup tools, EditMode/PlayMode tests, screenshots/captures, asset generation) or when C# changes need a compile check without Unity. The user's Unity editor is always open on the project, so all batch work goes through a mirror copy. Triggers - "запусти тесты", "прогони сетап", "сделай скриншот из игры", "проверь что компилится", any -executeMethod / -runTests work.
---

# Unity batch через зеркало проекта

Редактор пользователя всегда открыт на `E:\Projects\stalker_raid_into_darkness`. Второй Unity на том же проекте не запускать, процессы рабочего редактора не трогать. Все batch-операции идут в зеркале `CleanProject`, результаты переносятся обратно вместе с `.meta`.

Скрипты лежат в `.claude/skills/unity-batch-mirror/scripts/` и запускаются через Bash (Git Bash). Пути переопределяются переменными `STALKER_PROJECT`, `STALKER_MIRROR`, `STALKER_UNITY`, `STALKER_WORK`. По умолчанию зеркало: `C:/Users/kagami/AppData/Local/Temp/claude/e--Projects-stalker-raid-into-darkness/f9f30669-85fe-4c0e-9402-a46207e682fe/scratchpad/CleanProject`, логи и результаты в соседней папке `batch/`. Если зеркала нет, `sync.sh full` создаст его, но первый импорт Library займёт долго.

## Быстрая проверка компиляции (без Unity)

```sh
node .claude/skills/unity-batch-mirror/scripts/compile.js RaidIntoDarkness.Player.Editor RaidIntoDarkness.Integrations.Inventory.Editor
```

- csproj строится из asmdef: все `.cs` под папкой asmdef (без вложенных asmdef), ссылки на проекты и dll из `Library/ScriptAssemblies` добавляются автоматически. Новые файлы и новые ссылки asmdef (например HDRP) подхватываются сами.
- Нужны сгенерированные Unity `*.csproj` в корне проекта; если csproj для новой сборки нет, попроси пользователя сфокусировать редактор или собери через batch.
- Собирай верхние сборки (Tests/Editor), они тянут зависимости. Это первый шаг после любых правок C#.

## Типовой цикл batch

1. `sh scripts/kill_stale.sh` — только если прошлый batch упал или завис (убивает Unity.exe с путём зеркала в командной строке, снимает `Temp/UnityLockfile`).
2. Синхронизация:
   - `sh scripts/sync.sh code` — только код (`_Source/Scripts`, `_Source/Tests`, `_Source/Shaders`, `_Modules`, X-Ray импортёр) и inputactions; сгенерированные в зеркале ассеты не трогаются;
   - `sh scripts/sync.sh full` — /MIR `Assets`, `Packages`, `ProjectSettings`. Стирает в зеркале всё, чего нет в проекте, поэтому сначала перенеси обратно нужные результаты.
3. `node scripts/meta_check.js --fix` — обязательно перед batch, если появились новые файлы. Открытый редактор сам генерирует `.meta` для новых скриптов; зеркало должно иметь тот же GUID, иначе префабы и сцены, сохранённые в зеркале, получат битые ссылки на скрипты. Источник истины — `.meta` проекта.
4. Запуск:
   - сетап: `sh scripts/run_method.sh Namespace.Class.SetupBatch setup_name [nographics|graphics]`. Метод обязан вызвать `EditorApplication.Exit(0|1)`; логируй с тегом вида `[EquipmentAnimations]` и потом `grep` по нему.
   - тесты: `sh scripts/run_tests.sh EditMode "RaidIntoDarkness.Player.Tests.Editor"` или `sh scripts/run_tests.sh PlayMode "RaidIntoDarkness.Inventory.Tests.PlayMode;RaidIntoDarkness.Player.Tests.PlayMode" [filter]`. Итог печатает `results.js`.
5. Перенос результатов: `node scripts/copy_back.js <Assets/путь>... ` (сухой прогон) → `--apply`. Код (`.cs`, `.asmdef`, `.shader`, `.inputactions`) не копируется никогда: код правится прямо в проекте. Файлы, изменённые в проекте после последней синхронизации, считаются правками пользователя и пропускаются (`CONFLICT`), `--force` только по явной причине. Перезаписанные файлы бэкапятся в `batch/backup/<время>/`.

## Сборки тестов проекта

- EditMode: `RaidIntoDarkness.Player.Tests.Editor`, `XRay.Importer.Tests.Editor`, `Inventory.Tests.Editor`.
- PlayMode (сцена `OutdoorsScene`): `RaidIntoDarkness.Inventory.Tests.PlayMode`, `RaidIntoDarkness.Player.Tests.PlayMode`.
- `OutdoorsSceneFoleyTests.FoleyAndCameraEffectsWorkInOutdoorsScene` падает давно («test pad surface was not detected») — не регрессия, но упоминай в отчёте.
- Capture-тесты пропускаются без переменных `INVENTORY_CAPTURE_DIR` / `STALKER_CAPTURE_DIR`.

## Скриншоты и визуальная проверка

- Для разового визуального контроля пиши временный PlayMode-тест **только в зеркале** (в проект не класть), после проверки удали его из зеркала.
- `[Timeout(600000)]` на длинные тесты (по умолчанию 180 с).
- `WaitForEndOfFrame` в batch зависает — используй `yield return null` и читай `camera.targetTexture` (RenderTexture) через `ReadPixels`.
- `Time.captureFramerate = 30`, иначе большие кадровые шаги дают сильный motion blur HDRP.
- Для PlayMode и рендера не используй `-nographics`; для сетапов без рендера `-nographics` можно.
- Результат смотри через Read на PNG и описывай пользователю, что видно.

## Шум в логах, который не является ошибкой

`DirectoryNotFoundException ... YamlDotNet`, `RenderPipelineGlobalSettingsUtils:TryEnsure`, `Host type is not matching any asset type`, `[Licensing::Module] Error: Access token is unavailable`.

## Перед переносом в проект

- Проверь `git diff --stat` целевых сцен и префабов: пользователь параллельно правит `OutdoorsScene` и объекты UI (например `HudMessage`). Никогда не перезаписывай их целиком вслепую — только если изменения от сетапа ожидаемые; иначе внеси правку в проект напрямую.
- Префабы и сцены после копирования проверь по `git diff`: должны появиться только ожидаемые компоненты и поля.
- Временные бэкапы держи в рабочей папке сессии, не в `Assets`.
