# Inventory

Универсальный модуль инвентаря для Unity 6 + HDRP: сеточные и слотовые контейнеры, атомарные операции, JSON-сохранение, процедурный UI и HDRP-генератор иконок.

Модуль не зависит от игрового кода. Интеграция с конкретной игрой живёт снаружи (в этом проекте — `Assets/_Source/Scripts/Integrations/Inventory`).

## Структура и сборки

| Папка | Сборка | Зависимости |
|---|---|---|
| `Runtime/Core`, `Definitions`, `Instances`, `Grid`, `Equipment`, `Transactions`, `Serialization` | `Inventory` | только UnityEngine |
| `Runtime/UI` | `Inventory.UI` | Core, uGUI, TextMeshPro, Input System |
| `Runtime/IconGeneration` | `Inventory.IconGeneration` | Core, UniTask, HDRP |
| `Editor` | `Inventory.Editor` | всё выше, только Editor |
| `Tests/Editor` | `Inventory.Tests.Editor` | EditMode-тесты модуля |

Данные разделены так:

- `ItemDefinition` (ScriptableObject) — статическое описание предмета: id, размер, вес, стек, категория, теги, prefab, настройки иконки, компоненты (`ConsumableItemComponent`, `EquippableItemComponent`, свои наследники `ItemDefinitionComponent`).
- `ItemInstance` (plain C#) — конкретный экземпляр: `InstanceId`, количество, состояние (`SetState`), визуальный вариант (`SetVisualVariant`), положение.
- `InventoryModel` — владелец контейнеров и предметов. Все изменения идут только через его методы.

## Создать сетку из пустого Image

1. В Canvas создайте `UI → Image` (или `GameObject → UI → Inventory → Inventory Grid`).
2. Добавьте компонент `InventoryGridView`.
3. В инспекторе задайте `Container Id`, `Owner Id`, размер ячейки и режим размера:
   - **Auto Size** — задаёте `Columns`/`Rows`, RectTransform подгоняется под `columns x cellWidth`, `rows x cellHeight`;
   - **Size From Rect** — количество ячеек вычисляется из размера RectTransform. Если размер не кратен ячейке, инспектор показывает предупреждение, остаток не используется.
4. Сетка рисуется процедурно (`InventoryGridGraphic`) и обновляется в edit mode, prefab stage и runtime. Служебные дочерние объекты (`__InventoryGrid`, `__InventoryHighlight`, `__InventoryItems`, `__InventoryLabels`) не сохраняются в сцену и не дублируются; ваши дочерние объекты не трогаются.
5. Цвета, толщина линий и подсветки задаются в `Style`.

Слотовая сетка (экипировка, разгрузка): `InventorySlotGridView` + `InventorySlotLayout` или inline-маска.

## Размеры и маски слотов

Маска — текст, каждый символ — ячейка. Одинаковые символы образуют один прямоугольный слот, `.`, `_`, `-` и пробел — пустые ячейки:

```
HH.AAA
HH.AAA
PPPPP.SSS
aa.bb.c.dd
```

Для каждого символа задаётся `SlotLayoutBinding`: `SlotId`, `Mode` (`Single` — один предмет любого размера занимает весь слот; `Grid` — слот работает как маленькая сетка) и `SlotRule`:

- `AcceptedCategories`, `RequiredTags` (любой из), `ForbiddenTags`;
- `RequiresOccupiedSlots` — слот доступен, только если заняты указанные слоты (карманы разгрузки требуют `Armor`);
- `BlockedByOccupiedSlots` — слот недоступен, пока заняты указанные;
- `StartsLocked` — стартовая блокировка, меняется `model.TrySetSlotLocked(containerId, slotId, locked)`;
- `ValidatorId` — внешнее правило через `model.RegisterSlotValidator(id, ISlotValidator)`.

Нельзя снять предмет, от которого зависят занятые слоты (`InventoryFailure.DependentSlotOccupied`).

## Новый тип предмета

1. `Create → Inventory → Item Definition`.
2. Заполните размер, вес, `Max Stack`, категорию, теги, `Visual Prefab` (для иконки) и настройки `Icon`.
3. `DefinitionId` назначается автоматически при импорте (GUID). Проверка: `Tools → Inventory → Item IDs → Validate Definition IDs`.
4. Добавьте определение в `ItemDatabase` — это `IItemDefinitionResolver` для загрузки сохранений.
5. Поведение добавляется компонентами: наследник `ItemDefinitionComponent` с `[Serializable]`, чтение через `definition.GetComponent<T>()`.

## Добавить предметы из кода

```csharp
var model = new InventoryModel(itemDatabase);
model.AddContainer(new GridContainer("Backpack", 10, 8, "Player"));

ItemInstance ammo = model.CreateItem(ammoDefinition, 30);
InventoryResult result = model.TryAddAuto(ammo, "Backpack");

if (result.Success == false)
{
    Debug.LogWarning(result.Failure);
}

model.TryAdd(model.CreateItem(rifleDefinition), "Backpack", new Vector2Int(0, 2), rotated: false);
model.TryMove(ammo.InstanceId, "Stash", Vector2Int.zero, false);
model.TrySplit(ammo.InstanceId, 10, "Backpack", new Vector2Int(5, 5), false);
model.TryTransfer(ammo.InstanceId, "Stash", amount: 5);
model.TryEquip(rifle.InstanceId, "Equipment");
```

Каждая операция атомарна: при ошибке модель не меняется и возвращается `InventoryResult` с причиной. События `Changed`, `ItemEquipped`, `ItemUnequipped` вызываются после завершения операции.

Сортировка сетки: `model.TryArrange(containerId, order, groups)` раскладывает все предметы контейнера в порядке `order` построчно; каждая новая группа (`groups[i]`) начинается со строки ниже предыдущей, если места нет — ищется с начала сетки, затем с поворотом. Пример постоянной сортировки по типам (как SortingPlus из Anomaly) — `InventorySortingPlus` в интеграции проекта. Ограничение веса — `container.MaxWeight`, своё правило размещения — `model.AddPlacementValidator(...)`.

Сохранение:

```csharp
string json = InventorySerializer.ToJson(model);
InventoryLoadReport report = InventorySerializer.FromJson(model, json);
```

Неизвестные `DefinitionId` не теряются: они попадают в `report.UnknownDefinitions`, хранятся в `model.UnresolvedItems` и записываются обратно при следующем сохранении.

## UI-окно

`InventoryWindow` связывает модель и сетки: `window.Bind(model, iconProvider)` создаёт недостающие контейнеры по сеткам, `Open`/`Close`/`Toggle` управляют показом. Правила взаимодействия (как в GRA):

- перетаскивание внутри одной и той же сетки запрещено; перенести предмет можно в слот или в другую сетку. В другой сетке предмет встаёт под курсор (поворот — `RotateItem`), а если там занято — раскладывается автоматически, одинаковые стопки сливаются;
- бросок на занятый слот меняет предметы местами, вытесненный уходит в основную сетку;
- двойной клик: снятый предмет из слота уходит в рюкзак; при открытой второй сетке предмет переносится в неё; расходник используется; экипируемый предмет надевается в предпочтительный свободный слот (или заменяет занятый);
- при открытой второй сетке наведение на предмет с зажатым `TransferModifier` (Left Shift) переносит его.

Деление стека — через пункт контекстного меню. Весь ввод идёт через `InputActionReference` в `InventoryInputBindings`, поэтому переназначается в меню настроек.

Внешний вид задаётся без изменения кода модуля:

- `InventoryTheme` (ScriptableObject) — шрифты TMP, цвета, фон всплывающих панелей, оверлеи предметов (`x17`, вес, состояние, короткое имя для выбранных категорий, тень текста) и словарь подписей (`Label(key, fallback)`: действия, статусы, ошибки `failure_<InventoryFailure>`, подсказки).
- `InventorySlotGridView.SetSlotVisuals(...)` — произвольные прямоугольники слотов поверх логической маски, положение подписи, спрайт подсветки совместимого слота и спрайт блокировки.
- `InventoryStatusPanel`, `InventoryCharacterHeader`, `InventoryFilterBar` — полосы состояния, шапка персонажа и вкладки фильтра (неподходящие предметы затемняются).
- `InventoryWindow.SetSecondaryVisible(bool)` показывает или прячет вторую панель (тайник, труп, торговец).
- `InventoryWindow.ItemDecorator` — функция `ItemInstance → InventoryItemDecoration` (тонировка фона и значок справа), после смены внешних отметок вызывается `RefreshItems()`.

Контекстное меню расширяется через `window.Actions.Register(new InventoryDelegateAction(id, order, label, execute, visible, enabled))`. Встроенные действия: Use, Equip/Unequip, Split, Move, Inspect, Drop (`InventoryBuiltInActions`).

## Генерация иконок

Иконки рендерятся HDRP-камерой в изолированной сцене (слой рендера 31, отдельный Volume и свет, сцена на время рендера лишается своего освещения и после рендера восстанавливается), читаются через AsyncGPUReadback и проходят пост-обработку (обводка, тень, размытие, смещение, альфа).

- Глобальные настройки: `ItemIconGeneratorSettings` в `Resources/ItemIconGeneratorSettings`.
- Настройки предмета: блок `Icon` в `ItemDefinition` (камера, поворот модели, padding, свет, обводка, тень, отдельный вид для слотов).
- Варианты (обвес): компонент `ItemIconVariantPart` на дочернем объекте prefab — ссылка на определение детали, группа, объекты «установлено» и «по умолчанию», зависимости (достаточно любой из перечисленных деталей) и несовместимости, `Size Delta` — изменение размера иконки в ячейках (глушитель удлиняет оружие на клетку).
- Окно: `Tools → Inventory → Item Icon Generator` — превью, запекание одного предмета, вариантов, всех предметов, прогресс, отмена, инкрементальный режим.

### Batch bake

- `Tools → Inventory → Item Icons → Bake All (Incremental)` — перепекает только изменившиеся иконки.
- `Tools → Inventory → Item Icons → Rebake All` — полная перегенерация.
- `Tools → Inventory → Item Icons → Validate Catalog` — проверка каталога.
- Командная строка: `Unity.exe -batchmode -projectPath <path> -executeMethod Inventory.Editor.ItemIconBatchBake.BakeAllBatch` (без `-quit`: метод сам завершает редактор после запекания, код выхода 0 — успех).

Результат — PNG в `Resources/Generated/ItemIcons` и `BakedItemIconCatalog` в `Resources`. Ключ иконки включает id предмета, вариант, размер, тип профиля и хэш визуальных настроек, поэтому изменение настроек автоматически инвалидирует иконку.

### Кэш

`ItemIconCacheProvider` (реализация `IItemIconProvider` для UI) сначала ищет запечённую иконку в каталоге, затем рендерит в runtime и кэширует результат. `ItemIconCache.PrewarmAsync(...)` прогревает кэш с бюджетом на кадр, `ItemIconCache.Clear()` освобождает текстуры. Без `Visual Prefab` используется `Fallback Icon`.

## Перенос в другой проект

1. Скопируйте `Assets/_Modules/Inventory` целиком.
2. Нужны пакеты: uGUI + TextMeshPro, Input System, HDRP (только для `IconGeneration`), UniTask.
3. Если HDRP не используется, удалите `Runtime/IconGeneration` и `Editor/IconGeneration` и передайте в `InventoryWindow.Bind` свой `IItemIconProvider`.
4. Создайте `ItemIconGeneratorSettings` в любой папке `Resources`, свои `ItemDefinition`, `ItemDatabase` и сетки из Image.
5. Свяжите окно со своим вводом и камерой во внешнем адаптере по примеру `InventoryWindowController`: открытие и закрытие, курсор, блокировка управления персонажем.

## Тесты

- `Inventory.Tests.Editor` — модель, атомарность, слоты, сериализация, генерация сеток, пост-обработка, ключи кэша, рендер иконок (изоляция от освещения сцены, альфа, варианты, очистка, отмена).
- `RaidIntoDarkness.Inventory.Tests.PlayMode` (вне модуля) — интеграция с OutdoorsScene.
