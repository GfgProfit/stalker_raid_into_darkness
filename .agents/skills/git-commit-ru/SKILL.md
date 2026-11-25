---
name: git-commit-ru
description: Use when asked to draft a Russian Git commit message or create a commit for this project. Use exactly one Gitmoji. Review staged changes first and fall back to unstaged changes when the index is empty.
---

# Russian Gitmoji Commit

## Project precedence

You MUST follow [AGENTS.md](../../../AGENTS.md), including instruction priority and Git Safety. A commit request does not authorize relaxing project gates, creating tests, introducing a Service Locator, migrating HTTP to HTTPS, or changing the `.asmdef` layout.

## Workflow

1. You MUST verify the repository root and Git state and run commands from the verified root or with `git -C`.
2. You MUST inspect `git diff --cached --stat` and `git diff --cached`. If the index is empty, you MUST inspect `git diff --stat` and `git diff`.
3. You MUST identify the main change and check the selected changes for secrets, unrelated edits, and unintended generated files.
4. You MUST prepare one message in Russian. A message-only request MUST leave the index and history intact. Creating a commit requires an explicit request and the execution checks below.

## 1. Analyze the changes

Before writing the commit message, you MUST read the complete selected diff and identify the commit's main purpose.

- You MUST group changes by purpose and outcome into independent features, gameplay mechanics, subsystems, services, fixes, architectural changes, configuration, or independent Unity client, Mirror server, and Node.js server changes.
- You MUST NOT combine distinct mechanics in a generic item, split groups just because files differ, group by directory layout, or describe changes absent from the diff.
- For each group, you MUST determine the problem resolved or behavior enabled. Check the diff, related code, and task context. You MUST NOT assume a cause when the evidence is inconclusive.
- When the commit spans independent features or services, each MUST have its own body section.

## 2. Title format

```text
<EMOJI> <краткое описание основного изменения>
```

Example

```text
✨ Добавлено восстановление игровой сессии
```

### Gitmoji rules

You MUST use exactly one emoji, selected by the commit's main purpose.

* ✨ — new functionality
* 🐛 — bug fix
* ♻️ — refactoring
* ⚡ — performance improvement
* 🔒 — security fix or hardening
* ✅ — added or fixed tests
* 📝 — documentation change
* 🔧 — configuration change
* 🔥 — code or functionality removal
* 🚑 — critical fix
* 🚚 — moved or renamed files
* ⬆️ — upgraded dependencies
* ⬇️ — downgraded dependencies
* 💥 — breaking change
* 🚧 — unfinished functionality

### Title rules

- The title MUST be in Russian and at most 72 characters long.
- The text after the emoji MUST describe a completed action, such as «Добавлено», «Исправлено», «Переработано», «Удалено», «Обновлено», or «Оптимизировано».
- You MUST NOT use English verbs such as `Add`, `Fix`, `Update`, or `Refactor` or end the title with a period.
- The title MUST describe the main change. A list of affected subsystems is insufficient.
- You MUST preserve technical names such as Unity, Mirror, Node.js, API, and WebSocket, as well as class names, method names, and identifiers.
- You MUST NOT mix Russian and English unless required for technical terms.

## 3. Choose the structure

You MUST use the minimum necessary number of sections.

Valid grouping

```text
Инвентарь:
- Переработано открытие контекстного меню предмета
- Добавлено закрытие вложенных окон по приоритету

Меню паузы:
- Исправлен возврат из настроек в меню паузы
```

```text
Авторизация:
- Добавлена проверка JWT перед подключением клиента

WebSocket-соединение:
- Исправлено закрытие соединения при истечении сессии
```

Invalid grouping

```text
Unity:
- Изменен InventoryView.cs
- Изменен PauseMenu.cs

Mirror:
- Изменен NetworkManager.cs
```

The file list gives the location of the edits without explaining their outcome.

## 4. Create a commit

- If the index contains changes, you MUST leave it intact and use only the reviewed staged diff. If it includes unsuitable changes, you MUST NOT include them or change another person's staging. You MUST report the specific blocker.
- If the index is empty, you MUST use the working-tree diff to draft the message. For an explicitly requested commit, you MUST stage only reviewed, authorized changes described in the message. You MUST inspect the resulting staged diff, including the staged portions of partially changed files. You MUST NOT stage changes if ownership or permission to include them is unconfirmed.
- If there are no changes to commit, you MUST report that state. An empty commit is FORBIDDEN.

Before creating a commit, you MUST verify all of the following.

- The title describes the actual main change.
- Each independent feature has a separate section.
- The body matches the staged diff.
- The items explain what changed and what problem the change solves or what behavior it enables.
- The message contains no invented outcomes.
- The message uses exactly one emoji.
- All ordinary text is in Russian.
- The title is at most 72 characters long.
- The message contains no co-author or attribution lines.

### Attribution is forbidden

The following co-author and attribution lines are FORBIDDEN in the commit message.

- `Co-Authored-By:` and other co-author trailers, including those naming an AI assistant.
- `Signed-off-by:` added on the assistant's behalf.
- `Generated with ...`, `🤖 Generated with ...`, generator links, or mentions of an AI assistant as author.

You MUST NOT add attribution from tool settings or templates. [AGENTS.md](../../../AGENTS.md) and higher-priority platform rules determine precedence. You MUST NOT use Git flags or options such as `--author`, `--signoff`, or `--trailer` to insert another author or co-author. The author MUST remain the user from the current Git configuration. The message MUST end with the final body item or, if there is no body, with the title.

For a message-only request, you MUST return the prepared text. For a requested commit, you MUST verify the result and give the message, short hash, and confirmation of creation. If the commit fails, you MUST report the error and MUST NOT claim success.

## Body

You MUST leave one blank line between the title and the body when the body is present. You MUST organize body sections by each changed feature, mechanic, subsystem, or service. The following format is REQUIRED.

```text
<Название фичи или подсистемы>:
- <что изменено и для чего: решаемая проблема или обеспечиваемое поведение>
- <что изменено и для чего: решаемая проблема или обеспечиваемое поведение>

<Название другой фичи или подсистемы>:
- <что изменено и для чего: решаемая проблема или обеспечиваемое поведение>
- <что изменено и для чего: решаемая проблема или обеспечиваемое поведение>
```

Example

```text
✨ Добавлено восстановление игровой сессии

Сохранение сессии:
- Добавлено сохранение состояния игрока при отключении
- Восстановлены инвентарь и позиция после повторного подключения

Сетевая синхронизация:
- Добавлена передача данных сессии между Mirror-сервером и Unity-клиентом
- Исключена повторная отправка состояния при переподключении

Node.js API:
- Добавлена проверка существования активной сессии
- Обновлен ответ API для восстановления данных игрока
```

### Section rules

- Each independent feature, mechanic, or subsystem MUST have its own section.
- Section names MUST identify the affected area concisely and concretely. Generic names such as «Прочее», «Изменения», «Разное», «Обновления», and «Исправления» are FORBIDDEN.
- Use concrete names such as «Инвентарь», «Система окон», «Сетевая синхронизация», «Авторизация», «Сохранение сессии», «Mirror-сервер», «Node.js API», «WebSocket-соединение», and «Конфигурация сборки».
- You MUST NOT create Unity, Mirror, and Node.js sections by default. You may split them when they serve distinct purposes within the changes. If their changes are independent, each MUST have a separate section.
- A single feature spanning the Unity client, Mirror server, and Node.js may use one section with separate outcomes within it.
- You MUST NOT combine different mechanics just because they use Unity or split one feature solely because it spans layers or files.
- Sections MUST progress from the most significant changes to supporting changes.

### Item rules

- Each item MUST state the change and the problem it resolves or behavior it enables. You MUST connect the action to its purpose within the same item using wording such as «чтобы…», «для…», «из-за…», or a specific description of behavior before and after. A separate item describing general benefit is FORBIDDEN.
- You MUST state a purpose only when supported by the diff, related code, or task context. When the cause is unclear, you MUST describe the confirmed behavior without guessing.
- You MUST name a specific problem or effect. Empty phrases such as «для улучшения работы» or «для корректного поведения» are insufficient.
- Each item MUST start with a completed action, such as «Добавлено», «Исправлено», «Переработано», «Удалено», «Обновлено», «Оптимизировано», «Исключено», or «Обеспечено».
- You MUST NOT list changed files, classes, or methods without explaining their purpose. Each item MUST describe an outcome. Describing the work process is FORBIDDEN.
- You MUST NOT claim tests, fixes, or behavior absent from the diff, or claim a complete fix when the diff shows partial implementation.
- Each section MUST have one to four items. The complete body MUST have two to twelve items. Empty sections are FORBIDDEN.
- For a very small change that consists of one simple action, the title alone is allowed if it states both the action and its purpose.

Example of a stated purpose

```text
Недостаточно: Включено Update When Offscreen
Хорошо: Включено Update When Offscreen, чтобы SkinnedMeshRenderer продолжал обновляться вне поля зрения камеры
```

## Commit execution

Before an explicitly requested commit, you MUST run [cs-style-check](../cs-style-check/SKILL.md) on the staged diff and verify that the diff still matches the authorized scope and message. You MUST then run `git commit` with the prepared text and follow Git Safety in `AGENTS.md` when handling errors. Bypassing Git hooks is FORBIDDEN.
