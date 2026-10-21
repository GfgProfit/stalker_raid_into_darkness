---
name: cs-style-check
description: Use before finishing any task that changed C#, shader or helper script code in this project (the CLAUDE.md "Final Verification" step), and before /git-commit-ru. Mechanically checks the project's hard style rules on changed lines - no comments of any kind, conditions/parameters on one line, Allman braces, _privateFields, UPPER_CASE constants, no blank lines between members of the same group.
---

# Проверка стиля проекта

Пользователь не раз ловил нарушения правил CLAUDE.md: комментарии в коде, пустые строки между полями одной группы. Поэтому перед финальным ответом прогоняй проверку механически, а не «на глаз».

```sh
node .claude/skills/cs-style-check/scripts/check.js            # добавленные строки рабочего diff + неотслеживаемые файлы
node .claude/skills/cs-style-check/scripts/check.js --staged   # только staged diff (перед коммитом)
node .claude/skills/cs-style-check/scripts/check.js <файлы>    # файлы целиком
node .claude/skills/cs-style-check/scripts/check.js --all      # весь проект без Plugins/TextMesh Pro (старый код содержит единичные исторические нарушения)
```

По умолчанию проверяются только **добавленные** строки, поэтому старый код не создаёт шума. Код выхода 1 — есть нарушения; строки с `warning:` на код выхода не влияют.

## Что проверяется

| Правило | Пояснение |
|---|---|
| `comment` | `//`, `/* */`, `///` в `.cs`, `.shader`, `.hlsl`, `.js`; `#` в `.sh`/`.ps1` (кроме shebang). Запрет действует для **любого** кода, включая скрипты скиллов и шейдеры. Usage и пояснения пиши в `SKILL.md`/README, не в код. |
| `condition split across lines` | `if/while/for/foreach/switch/using/lock` с незакрытой скобкой в конце строки |
| `parameters split across lines` | строка заканчивается на `(` или `,` при незакрытой скобке |
| `brace must be on its own line` | `{` в конце строки с кодом (кроме `=>`, `=`, `new …`, `(`, `,`) |
| `const must be UPPER_CASE` | |
| `private field must start with _` | `private` и `[SerializeField]`; `static readonly` в UPPER_CASE допустимы как константы |
| `blank line between members of the same group` | группа = доступ + const/static/readonly/SerializeField + поле или свойство |
| `warning: public mutable field` | допустимо только для DTO `JsonUtility`; в остальном — `[SerializeField] private` или свойство |

## Чего чекер не ловит — проверь глазами

- Стиль окружающего кода: expression-bodied члены только там, где они уже используются; порядок членов и lifecycle-методов не меняется без нужды.
- `UnityEditor` в runtime-сборках, editor-скрипты только в папках `Editor`.
- Лишние `Update()`, аллокации и LINQ в горячих путях; подписки R3/событий отписываются.
- Наименьшее безопасное изменение: никаких переписываний рабочего кода «под себя».
- Финальный ответ на русском по разделам CLAUDE.md: кратко, изменённые файлы, детали реализации, риски, ручные шаги в Unity.
