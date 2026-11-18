---
name: unity-batch-mirror
description: Use for Unity batch-mode setup, tests, captures, or compile checks. Prefer the active Production Editor through Unity Open MCP and start a separate batch process only on an available, authorized project.
---

# Проверка и запуск Unity

- Для компиляции, сетапов и тестов используй Unity Open MCP в открытом Production Editor. До запуска проверь абсолютный путь проекта и состояние сцены.
- Для batch-режима сначала убедись, что целевой проект свободен. Если Production открыт, используй открытый редактор или явно разрешённый изолированный проект. Не закрывай редактор пользователя и не запускай второй Unity на том же проекте.
- Указывай проверенный абсолютный путь в `-projectPath` и сохраняй `-logFile`, результаты тестов и captures в отдельной папке Archive согласно AGENTS.md.
- Скрипты в `scripts/` относятся к прежнему workflow. Не запускай их для штатной проверки. Создание, синхронизация и перенос данных из зеркала требуют отдельного разрешения для конкретного проекта.
- Для графических тестов оставляй графику включённой. В batch не используй `WaitForEndOfFrame`. Для чтения кадра используй `RenderTexture` и `ReadPixels`.
