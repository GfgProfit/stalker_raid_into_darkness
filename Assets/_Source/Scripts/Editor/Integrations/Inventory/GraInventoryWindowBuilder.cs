using System.Collections.Generic;
using System.IO;
using Inventory.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class GraInventoryWindowBuilder
    {
        public const string ROOT = "Assets/_Source/UI/Inventory/GRA";
        public const string SPRITES_FOLDER = ROOT + "/Sprites";
        public const string CONTEXT_MENU_FOLDER = ROOT + "/ContextMenu";
        public const string FONT_PATH = "Assets/_Source/Fonts/Bender/bender_regular SDF.asset";
        public const string THEME_PATH = ROOT + "/GraInventoryTheme.asset";
        public const string PORTRAIT_SPRITE = "gra_portrait";
        public const string RANK_SPRITE = "gra_rank";
        public const string FAVORITE_SPRITE = "gra_sort_favourite";
        public const string JUNK_SPRITE = "gra_sort_junk";
        public const string STASH_PANEL = "StashPanel";
        public const string SLOT_SUFFIX = " Slot";
        public const string SLOT_ICON = "Icon";
        public const string EQUIPMENT_TITLE = "Экипировка";
        public const float CELL = 41f * SCALE_Y;

        private const float SCALE_X = 1920f / 1024f;
        private const float SCALE_Y = 1080f / 768f;
        private const int BAG_COLUMNS = 11;
        private const int BAG_ROWS = 14;
        private const string WEIGHT_FORMAT = "<color=#808080>Общий вес:</color>  <color=#{2}>{0} кг</color>  <color=#808080>(max {1} кг)</color>";
        private const string WEIGHT_FORMAT_UNLIMITED = "<color=#808080>Общий вес:</color>  <color=#{2}>{0} кг</color>";
        private const string PROTECTION_BAR_SPRITE = "gra_bar_progress";
        private const string HELMET_BLOCKER_SPRITE = "gra_blocker_big";

        private static readonly (string Name, string SlotId)[] _slotAliases =
        {
            ("Helmet", "Head"),
            ("PDA", "Pda"),
            ("Melee", "Knife")
        };

        private static readonly (string Key, string Text)[] _labels =
        {
            ("use", "Использовать"),
            ("equip", "Экипировать"),
            ("unequip", "Снять"),
            ("split", "Разделить"),
            ("move", "Переместить: {0}"),
            ("inspect", "Описание"),
            ("drop", "Выбросить"),
            ("favorite_add", "Отметить как избранное"),
            ("favorite_remove", "Снять отметку избранного"),
            ("junk_add", "Отметить как мусор"),
            ("junk_remove", "Снять отметку мусора"),
            ("status_used", "Использовано: {0}"),
            ("status_equipped", "Экипировано: {0}"),
            ("status_unequipped", "Снято: {0}"),
            ("status_split", "Отделено {0} шт.: {1}"),
            ("status_moved", "Перемещено: {0}"),
            ("status_dropped", "Выброшено: {0}"),
            ("status_picked", "Подобрано: {0}"),
            ("hint_pick_up", "[{0}] Подобрать"),
            ("stat_fire_wound", "Баллист. защита"),
            ("stat_fire_wound_helmet", "Балл. защ. Шлема"),
            ("stat_speed", "Множ. подвижности"),
            ("stat_durability_physical", "Физ. прочность"),
            ("stat_durability_anomaly", "Аном. прочность"),
            ("stat_unit_kg", "кг"),
            ("stat_knife_weak_damage", "Урон слабой атаки"),
            ("stat_knife_strong_damage", "Урон сильной атаки"),
            ("stat_knife_reach", "Дальность удара"),
            ("stat_unit_m", "м"),
            ("stat_grenade_type", "Тип гранаты"),
            ("stat_grenade_blast_radius", "Радиус взрыва"),
            ("stat_grenade_blast_damage", "Урон взрыва"),
            ("stat_grenade_fragment_count", "Количество осколков"),
            ("stat_grenade_fragment_damage", "Урон осколка"),
            ("stat_grenade_fragment_range", "Дальность осколков"),
            ("stat_grenade_detonation", "Время детонации"),
            ("grenade_type_hand", "Ручная"),
            ("grenade_type_hand_impact", "Ручная контактная"),
            ("grenade_type_launcher", "Подствольная"),
            ("grenade_type_launcher_bounce", "Подствольная с подскоком"),
            ("grenade_detonation_limit", "До {0}"),
            ("stat_unit_s", "с"),
            ("stat_burn", "Термозащита"),
            ("stat_shock", "Электрозащита"),
            ("stat_chemical_burn", "Химзащита"),
            ("stat_radiation", "Радиозащита"),
            ("stat_telepatic", "Пси-защита"),
            ("stat_wound", "Защ. от разрыва"),
            ("stat_strike", "Гашение удара"),
            ("stat_explosion", "Защ. от взрыва"),
            ("stat_artefact_count", "Кол-во слотов"),
            ("stat_additional_weight", "Переносимый вес"),
            ("tooltip_cost", "Стоимость:"),
            ("unit_currency", "руб."),
            ("tooltip_condition", "Состояние"),
            ("tooltip_features", "ОСОБЕННОСТИ"),
            ("feature_community", "группировка"),
            ("feature_helmet", "головной убор"),
            ("feature_helmet_none", "отсутствует"),
            ("feature_helmet_builtin", "встроенный"),
            ("feature_sprint", "передвижение бегом:"),
            ("feature_possible", "возможно"),
            ("feature_not_possible", "невозможно"),
            ("community_unknown", "Неизвестный"),
            ("community_stalker", "Одиночки"),
            ("community_dolg", "«Долг»"),
            ("community_freedom", "«Свобода»"),
            ("community_csky", "«Чистое небо»"),
            ("community_ecolog", "Учёные"),
            ("community_killer", "Наёмники"),
            ("community_army", "Военные"),
            ("community_bandit", "Бандиты"),
            ("community_monolith", "«Монолит»"),
            ("community_renegade", "Ренегаты"),
            ("community_greh", "«Грех»"),
            ("community_isg", "«Интернациональная исследовательская группа»"),
            ("failure_InvalidItem", "Неверный предмет"),
            ("failure_ItemNotFound", "Предмет не найден"),
            ("failure_ContainerNotFound", "Контейнер не найден"),
            ("failure_OutOfBounds", "Не помещается"),
            ("failure_Occupied", "Место занято"),
            ("failure_NoSlot", "Нет подходящего слота"),
            ("failure_SlotUnavailable", "Слот недоступен"),
            ("failure_SlotRejected", "Предмет не подходит для слота"),
            ("failure_ValidatorRejected", "Сюда нельзя положить"),
            ("failure_RotationNotAllowed", "Нельзя повернуть"),
            ("failure_NotStackable", "Не складывается"),
            ("failure_StackFull", "Стопка заполнена"),
            ("failure_InvalidAmount", "Неверное количество"),
            ("failure_NoSpace", "Нет места"),
            ("failure_AlreadyInInventory", "Уже в инвентаре"),
            ("failure_DependentSlotOccupied", "Сначала освободите зависимые слоты"),
            ("failure_Duplicate", "Предмет уже здесь"),
            ("tooltip_weight", "Вес: "),
            ("unit_kg", " кг"),
            ("split_title", "Разделить: "),
            ("button_split", "Разделить"),
            ("button_cancel", "Отмена"),
            ("button_close", "Закрыть"),
            ("inspect_body", "{0}\n\nКатегория: {1}\nРазмер: {2}x{3}\nВес: {4} кг\nКоличество: {5}/{6}"),
            ("category_Weapon", "Оружие"),
            ("category_Ammo", "Патроны"),
            ("category_Armor", "Броня"),
            ("category_Head", "Шлем"),
            ("category_Accessory", "Устройство"),
            ("category_Attachment", "Модуль"),
            ("category_Consumable", "Расходники"),
            ("category_Artifact", "Артефакт"),
            ("category_Misc", "Разное")
        };
        private static readonly Color _labelColor = new(1f, 1f, 1f, 230f / 255f);
        private static readonly Color _mutedColor = new(128f / 255f, 128f / 255f, 128f / 255f, 1f);
        private static readonly Color _accentColor = new(240f / 255f, 217f / 255f, 182f / 255f, 1f);
        private static readonly Color _itemInfoBackground = new(0f, 0f, 0f, 0.75f);
        private static readonly Color _barBackgroundColor = new(26f / 255f, 26f / 255f, 26f / 255f, 1f);
        private static readonly Color _healthColor = new(219f / 255f, 56f / 255f, 51f / 255f, 1f);
        private static readonly Color _protectionColor = new(219f / 255f, 176f / 255f, 51f / 255f, 1f);
        private static readonly Color _needsColor = new(219f / 255f, 176f / 255f, 51f / 255f, 1f);

        public sealed class Result
        {
            public InventoryWindow Window;
            public InventorySlotGridView Equipment;
            public InventoryGridView Backpack;
            public InventoryGridView Stash;
            public RectTransform StashPanel;
            public InventoryCharacterHeader Header;
            public InventoryStatusPanel Status;
            public InventoryTheme Theme;
        }

        public static TMP_FontAsset ImportAssets()
        {
            ConfigureSprites();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);

            if (font == null)
            {
                Debug.LogError("[Inventory] Font asset was not found: " + FONT_PATH);
            }

            return font;
        }

        public static Sprite LoadSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{SPRITES_FOLDER}/{name}.png");

        public static InventoryTheme BuildTheme(TMP_FontAsset font)
        {
            var theme = AssetDatabase.LoadAssetAtPath<InventoryTheme>(THEME_PATH);
            bool created = theme == null;

            if (created)
            {
                theme = ScriptableObject.CreateInstance<InventoryTheme>();
                AssetDatabase.CreateAsset(theme, THEME_PATH);
            }

            var serialized = new SerializedObject(theme);

            if (created)
            {
                serialized.FindProperty("_bodyFont").objectReferenceValue = font;
                serialized.FindProperty("_headerFont").objectReferenceValue = font;
                serialized.FindProperty("_accentFont").objectReferenceValue = font;
                serialized.FindProperty("_smallFont").objectReferenceValue = font;
                serialized.FindProperty("_textColor").colorValue = new Color(170f / 255f, 170f / 255f, 170f / 255f, 1f);
                serialized.FindProperty("_mutedTextColor").colorValue = _mutedColor;
                serialized.FindProperty("_accentColor").colorValue = _accentColor;
                serialized.FindProperty("_negativeColor").colorValue = new Color(1f, 50f / 255f, 50f / 255f, 1f);
                serialized.FindProperty("_panelSprite").objectReferenceValue = LoadSprite("gra_hint_window");
                serialized.FindProperty("_panelColor").colorValue = Color.white;
                serialized.FindProperty("_showItemBackground").boolValue = false;
                serialized.FindProperty("_showItemWeight").boolValue = true;
                serialized.FindProperty("_showItemCount").boolValue = true;
                serialized.FindProperty("_itemTextShadow").boolValue = true;
                serialized.FindProperty("_countFormat").stringValue = "x{0}";
                serialized.FindProperty("_weightUnit").stringValue = "кг";
                serialized.FindProperty("_itemTextSize").floatValue = 15f;
                serialized.FindProperty("_itemNameSize").floatValue = 14f;
                serialized.FindProperty("_itemWeightSize").floatValue = 16f;
                serialized.FindProperty("_itemWeightColor").colorValue = _accentColor;
                serialized.FindProperty("_itemInfoBackground").colorValue = _itemInfoBackground;
                serialized.FindProperty("_itemTextColor").colorValue = Color.white;
                serialized.FindProperty("_itemShadowColor").colorValue = new Color(0f, 0f, 0f, 0.9f);
                serialized.FindProperty("_conditionColor").colorValue = new Color(100f / 255f, 1f, 150f / 255f, 1f);
                SerializedProperty categories = serialized.FindProperty("_shortNameCategories");
                categories.arraySize = 1;
                categories.GetArrayElementAtIndex(0).stringValue = "Weapon";
            }

            AssignIfMissing(serialized.FindProperty("_contextMenuFrame"), CONTEXT_MENU_FOLDER + "/gra_context_frame.png");
            AssignIfMissing(serialized.FindProperty("_contextMenuHighlight"), CONTEXT_MENU_FOLDER + "/gra_context_highlight.png");
            AssignIfMissing(serialized.FindProperty("_contextMenuDivider"), CONTEXT_MENU_FOLDER + "/gra_context_divider.png");
            AddMissingLabels(serialized.FindProperty("_labels"));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            return theme;
        }

        private static void AssignIfMissing(SerializedProperty property, string spritePath)
        {
            if (property.objectReferenceValue == null)
            {
                property.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            }
        }

        private static void AddMissingLabels(SerializedProperty labels)
        {
            var existing = new HashSet<string>(System.StringComparer.Ordinal);

            for (int i = 0; i < labels.arraySize; i++)
            {
                existing.Add(labels.GetArrayElementAtIndex(i).FindPropertyRelative("_key").stringValue);
            }

            foreach ((string Key, string Text) entry in _labels)
            {
                if (existing.Add(entry.Key) == false)
                {
                    continue;
                }

                labels.arraySize++;
                SerializedProperty label = labels.GetArrayElementAtIndex(labels.arraySize - 1);
                label.FindPropertyRelative("_key").stringValue = entry.Key;
                label.FindPropertyRelative("_text").stringValue = entry.Text;
            }
        }

        public static Result Build(GameObject root, InventoryTheme theme, TMP_FontAsset font, string stashTitle)
        {
            var result = new Result { Theme = theme };
            var rootRect = (RectTransform)root.transform;
            InventoryUIFactory.Stretch(rootRect);
            RectTransform content = InventoryUIFactory.Stretch(InventoryUIFactory.CreateRect("Content", rootRect));
            RectTransform screen = InventoryUIFactory.CreateRect("Screen", content);
            screen.anchorMin = new Vector2(0.5f, 0.5f);
            screen.anchorMax = new Vector2(0.5f, 0.5f);
            screen.pivot = new Vector2(0.5f, 0.5f);
            screen.sizeDelta = new Vector2(1920f, 1080f);

            RectTransform stashPanel = Place(screen, STASH_PANEL, U(0f, 0f, 382f, 768f));
            Background(stashPanel, "gra_side_panel");
            Header(stashPanel, font, stashTitle, string.Empty, false);
            result.Stash = BagGrid(stashPanel, "SecondaryInventory", InventoryDemoContentBuilder.STASH, "World", stashTitle);
            result.StashPanel = stashPanel;

            RectTransform equipmentPanel = Place(screen, "EquipmentPanel", U(382f, 0f, 260.5f, 768f));
            Background(equipmentPanel, "gra_equipment_back");
            result.Equipment = EquipmentGrid(equipmentPanel, font);
            result.Status = StatusPanel(equipmentPanel);

            RectTransform playerPanel = Place(screen, "PlayerPanel", U(642.5f, 0f, 382f, 768f));
            Background(playerPanel, "gra_side_panel");
            result.Header = Header(playerPanel, font, string.Empty, string.Empty, true);
            result.Backpack = BagGrid(playerPanel, "PlayerInventory", InventoryDemoContentBuilder.BACKPACK, InventoryDemoContentBuilder.OWNER, "Рюкзак");
            TextMeshProUGUI weight = Label(playerPanel, "Weight", U(100f, 739f, 272f, 18f), string.Empty, font, 20f, Color.white, TextAlignmentOptions.MidlineRight);
            TextMeshProUGUI status = HudMessage(rootRect, font);

            InventoryTooltip tooltip = Widget<InventoryTooltip>(content, "ItemTooltip");
            InventoryContextMenu contextMenu = Widget<InventoryContextMenu>(content, "ContextMenu");
            InventorySplitDialog splitDialog = Widget<InventorySplitDialog>(content, "SplitDialog");
            InventoryInspectPanel inspectPanel = Widget<InventoryInspectPanel>(content, "InspectPanel");
            RectTransform dragLayer = InventoryUIFactory.Stretch(InventoryUIFactory.CreateRect("DraggedItemLayer", content));
            CanvasGroup dragGroup = dragLayer.gameObject.AddComponent<CanvasGroup>();
            dragGroup.blocksRaycasts = false;
            dragGroup.interactable = false;

            InventoryWindow window = root.AddComponent<InventoryWindow>();
            window.Configure(content, dragLayer, tooltip, contextMenu, splitDialog, inspectPanel, weight, status, new InventoryGridViewBase[] { result.Equipment, result.Backpack, result.Stash });
            window.ConfigurePresentation(theme, stashPanel, WEIGHT_FORMAT, WEIGHT_FORMAT_UNLIMITED, new Color(1f, 50f / 255f, 50f / 255f, 1f));
            Sorter(equipmentPanel, window, font);
            result.Window = window;
            content.gameObject.SetActive(false);
            return result;
        }

        private static InventorySlotGridView EquipmentGrid(RectTransform panel, TMP_FontAsset font)
        {
            InventorySlotGridView grid = Grid<InventorySlotGridView>(panel, "EquipmentSlots", Vector2.zero);
            grid.Configure(InventoryDemoContentBuilder.EQUIPMENT, InventoryDemoContentBuilder.OWNER, "Equipment", new Vector2(CELL, CELL));
            Sprite helmet = LoadSprite("gra_hl_helmet");
            Sprite armor = LoadSprite("gra_hl_armor");
            Sprite small = LoadSprite("gra_hl_detector");
            Sprite weapon = LoadSprite("gra_hl_weapon");
            Sprite quick = LoadSprite("gra_hl_quick");
            Sprite blocker = LoadSprite("gra_blocker_small");
            Sprite equipmentLock = LoadSprite(HELMET_BLOCKER_SPRITE);
            var visuals = new List<InventorySlotVisual>
            {
                Slot(OutfitSlotRules.HELMET_SLOT, 12f, 9f, 75f, 96f, "ШЛЕМ", helmet, equipmentLock),
                Slot("Device", 93f, 9f, 75f, 42f, "УСТРОЙСТВО", small),
                Slot("Pda", 174f, 9f, 75f, 42f, "ПДА", small),
                Slot("Outfit", 93f, 63f, 75f, 150f, "КОСТЮМ", armor),
                Slot("Detector", 174f, 63f, 75f, 42f, "ДЕТЕКТОР", small),
                Slot(OutfitSlotRules.BACKPACK_SLOT, 12f, 117f, 75f, 96f, "РЮКЗАК", helmet, equipmentLock),
                Slot("Binocular", 174f, 117f, 75f, 42f, "БИНОКЛЬ", small),
                Slot("Knife", 174f, 171f, 75f, 42f, "ХОЛОДНОЕ", small),
                Slot("Primary", 12f, 225f, 237f, 96f, "ОСНОВНОЕ", weapon),
                Slot("Secondary", 12f, 333f, 237f, 96f, "ВТОРИЧНОЕ", weapon)
            };

            float[] quickX = { 2f, 42f, 83f, 123f };

            for (int i = 0; i < quickX.Length; i++)
            {
                visuals.Add(new InventorySlotVisual($"QuickSlot{i + 1}", U(11f + quickX[i], 442f, 33f, 44f), InventorySlotLabelAnchor.TopLeft, $"F{i + 1}", quick));
            }

            float[] beltX = { 6.375f, 55.125f, 103.875f, 152.625f, 201.5f };
            var lockedRect = new Rect(-6.375f * SCALE_X, -8.5f * SCALE_Y, 43.5f * SCALE_X, 58f * SCALE_Y);

            for (int i = 0; i < beltX.Length; i++)
            {
                visuals.Add(new InventorySlotVisual($"Belt{i + 1}", U(11f + beltX[i], 518.5f, 30.75f, 41f), InventorySlotLabelAnchor.None, null, quick, blocker, lockedRect));
            }

            grid.SetSlotVisuals(visuals);
            grid.SetLabelStyle(true, font, 15f, _labelColor, new Vector2(4f, 1f));
            grid.SetLayout(AssetDatabase.LoadAssetAtPath<global::Inventory.InventorySlotLayout>(InventoryDemoContentBuilder.EQUIPMENT_LAYOUT_PATH), 0f, false, true);
            ApplyTransparentStyle(grid);
            return grid;
        }

        private static InventorySlotVisual Slot(string id, float x, float y, float width, float height, string label, Sprite highlight, Sprite lockedSprite = null)
        {
            return new InventorySlotVisual(id, U(x, y, width, height), InventorySlotLabelAnchor.TopRight, label, highlight, lockedSprite);
        }

        private static InventoryGridView BagGrid(RectTransform panel, string name, string containerId, string ownerId, string displayName)
        {
            InventoryGridView grid = Grid<InventoryGridView>(panel, name, U(9.5f, 121f, 0f, 0f).position);
            grid.Configure(containerId, ownerId, displayName, new Vector2(CELL, CELL));
            grid.SetSizeMode(InventoryGridSizeMode.AutoSize);
            grid.SetGridSize(BAG_COLUMNS, BAG_ROWS);
            ApplyTransparentStyle(grid);
            return grid;
        }

        private static TextMeshProUGUI HudMessage(RectTransform root, TMP_FontAsset font)
        {
            RectTransform rect = InventoryUIFactory.CreateRect("HudMessage", root);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 20f);
            rect.sizeDelta = new Vector2(1200f, 40f);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();

            if (font != null)
            {
                label.font = font;
            }

            label.fontSize = 26f;
            label.color = _accentColor;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.text = string.Empty;
            return label;
        }

        private static InventoryStatusPanel StatusPanel(RectTransform panel)
        {
            RectTransform statusRect = Place(panel, "StatusPanel", U(11f, 576f, 239f, 148f));
            Image back = statusRect.gameObject.AddComponent<Image>();
            back.sprite = LoadSprite("gra_picker_back");
            back.raycastTarget = false;
            var bars = new List<InventoryStatusBar>
            {
                Bar(statusRect, InventoryWindowController.STATUS_HEALTH, "gra_state_health", 0f, 0f, 211.5f, "gra_bar_health", _healthColor),
                Bar(statusRect, EquipmentStatusPresenter.STATUS_RADIATION, "gra_state_radia", 0f, 24.25f, 91.5f, PROTECTION_BAR_SPRITE, _protectionColor),
                Bar(statusRect, EquipmentStatusPresenter.STATUS_CHEMICAL, "gra_state_acid", 0f, 48f, 91.5f, PROTECTION_BAR_SPRITE, _protectionColor),
                Bar(statusRect, EquipmentStatusPresenter.STATUS_SHOCK, "gra_state_shock", 0f, 72f, 91.5f, PROTECTION_BAR_SPRITE, _protectionColor),
                Bar(statusRect, EquipmentStatusPresenter.STATUS_FIRE, "gra_state_fire", 0f, 96f, 91.5f, PROTECTION_BAR_SPRITE, _protectionColor),
                Bar(statusRect, EquipmentStatusPresenter.STATUS_PSI, "gra_state_psi", 120f, 24.25f, 91.5f, PROTECTION_BAR_SPRITE, _protectionColor),
                Bar(statusRect, EquipmentStatusPresenter.STATUS_WOUND, "gra_state_wound", 120f, 48f, 91.5f, PROTECTION_BAR_SPRITE, _protectionColor),
                Bar(statusRect, EquipmentStatusPresenter.STATUS_FIRE_WOUND, "gra_state_fire_wound", 120f, 72f, 91.5f, PROTECTION_BAR_SPRITE, _protectionColor),
                Bar(statusRect, InventoryWindowController.STATUS_POWER, "gra_state_power", 120f, 96f, 91.5f, PROTECTION_BAR_SPRITE, _protectionColor),
                Bar(statusRect, InventoryWindowController.STATUS_HUNGER, "gra_state_hunger", 0f, 120.25f, 46.5f, "gra_bar_hts", _needsColor),
                Bar(statusRect, InventoryWindowController.STATUS_THIRST, "gra_state_thirst", 82.5f, 120.25f, 46.5f, "gra_bar_hts", _needsColor),
                Bar(statusRect, InventoryWindowController.STATUS_SLEEP, "gra_state_sleep", 165f, 120.25f, 46.5f, "gra_bar_hts", _needsColor)
            };

            InventoryStatusPanel status = statusRect.gameObject.AddComponent<InventoryStatusPanel>();
            status.Configure(bars);
            return status;
        }

        private static InventoryStatusBar Bar(RectTransform parent, string id, string iconSprite, float x, float y, float width, string barSprite, Color color)
        {
            Image icon = Img(parent, id + "Icon", U(x, y, 21f, 28f), LoadSprite(iconSprite), new Color(0.75f, 0.75f, 0.75f, 1f));
            icon.preserveAspect = true;
            Img(parent, id + "Back", U(x + 21f, y + 5f, width, 18f), LoadSprite(barSprite + "_bg"), _barBackgroundColor);
            Image fill = Img(parent, id + "Fill", U(x + 21f, y + 5f, width, 18f), LoadSprite(barSprite), color);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            return new InventoryStatusBar(id, fill, icon, BarGradient(color, barSprite == PROTECTION_BAR_SPRITE));
        }

        private static Gradient BarGradient(Color full, bool constant)
        {
            Color empty = constant ? full : new Color(0.8f, 0.2f, 0.15f, 1f);
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(empty, 0f), new GradientColorKey(full, constant ? 1f : 0.5f), new GradientColorKey(full, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        private static void Sorter(RectTransform panel, InventoryWindow window, TMP_FontAsset font)
        {
            RectTransform sorterRect = Place(panel, "Sorter", U(11f, 732f, 239f, 28f));
            (string Sprite, string[] Categories)[] tabs =
            {
                ("all", new string[0]),
                ("weapon", new[] { "Weapon" }),
                ("outfit", new[] { "Armor", "Head" }),
                ("ammo", new[] { "Ammo" }),
                ("arty", new[] { "Artifact" }),
                ("utility", new[] { "Accessory", "Attachment", "Device", "Pda", "Binocular" }),
                ("food", new[] { "Consumable" }),
                ("misc", new[] { "Misc", "Quest" })
            };

            float[] positions = { 22.125f, 46.875f, 71.625f, 96.375f, 121.125f, 145.875f, 170.625f, 195.375f };
            var filterTabs = new List<InventoryFilterTab>();

            for (int i = 0; i < tabs.Length; i++)
            {
                RectTransform buttonRect = Place(sorterRect, "Tab_" + tabs[i].Sprite, U(positions[i], 0f, 21f, 28f));
                Image image = buttonRect.gameObject.AddComponent<Image>();
                image.sprite = LoadSprite("gra_sort_" + tabs[i].Sprite + "_h");
                image.preserveAspect = true;
                Button button = buttonRect.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.ColorTint;
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                colors.selectedColor = Color.white;
                colors.colorMultiplier = 1.5f;
                colors.fadeDuration = 0.05f;
                button.colors = colors;
                Navigation navigation = button.navigation;
                navigation.mode = Navigation.Mode.None;
                button.navigation = navigation;
                TextMeshProUGUI number = Label(buttonRect, "Number", new Rect(0f, 0f, 21f * SCALE_X, 28f * SCALE_Y), (i + 1).ToString(), font, 15f, new Color(1f, 1f, 1f, 0.5f), TextAlignmentOptions.BottomRight);
                number.raycastTarget = false;
                filterTabs.Add(new InventoryFilterTab(tabs[i].Sprite, button, tabs[i].Categories));
            }

            InventoryFilterBar bar = sorterRect.gameObject.AddComponent<InventoryFilterBar>();
            bar.Configure(window, filterTabs);
        }

        private static InventoryCharacterHeader Header(RectTransform panel, TMP_FontAsset font, string characterName, string community, bool withMoney)
        {
            Img(panel, "CommunityBackground", U(6f, 8f, 370f, 102.25f), LoadSprite("gra_faction_stalker"), Color.white).raycastTarget = false;
            Image portrait = Img(panel, "Portrait", U(7f, 9.25f, 106f, 99.75f), LoadSprite(PORTRAIT_SPRITE), Color.white);
            portrait.enabled = withMoney;
            TextMeshProUGUI nameText = Label(panel, "Name", U(122f, 18f, 246f, 18f), characterName, font, 21f, Color.white, TextAlignmentOptions.MidlineLeft);
            TextMeshProUGUI communityText = Label(panel, "Community", U(122f, 51f, 109f, 18f), community, font, 20f, _mutedColor, TextAlignmentOptions.MidlineLeft);
            Image rank = Img(panel, "Rank", U(222f, 28f, 46f, 62f), withMoney ? LoadSprite(RANK_SPRITE) : null, Color.white);
            rank.preserveAspect = true;
            rank.enabled = withMoney;
            TextMeshProUGUI money = null;

            if (withMoney)
            {
                Img(panel, "MoneyIcon", U(121.75f, 83.5f, 13f, 16.5f), LoadSprite("gra_roubles"), Color.white).preserveAspect = true;
                money = Label(panel, "Money", U(140f, 83f, 227f, 20f), string.Empty, font, 28f, Color.white, TextAlignmentOptions.MidlineLeft);
            }

            InventoryCharacterHeader header = panel.gameObject.AddComponent<InventoryCharacterHeader>();
            header.Configure(portrait, rank, nameText, communityText, money);
            return header;
        }

        private static void Background(RectTransform panel, string sprite)
        {
            Image image = panel.gameObject.AddComponent<Image>();
            image.sprite = LoadSprite(sprite);
            image.raycastTarget = true;
        }

        public static InventoryGridViewBase BindEquipmentPanel(InventoryWindow window, Vector2 cellSize)
        {
            RectTransform panel = FindSlotPanel(window.transform);

            if (panel == null)
            {
                return window.GetComponentInChildren<InventorySlotGridView>(true);
            }

            var slots = new List<InventoryEquipmentSlot>();

            for (int i = 0; i < panel.childCount; i++)
            {
                Transform child = panel.GetChild(i);

                if (child.name.EndsWith(SLOT_SUFFIX, System.StringComparison.Ordinal) == false)
                {
                    continue;
                }

                InventoryEquipmentSlot slot = child.GetComponent<InventoryEquipmentSlot>();

                if (slot == null)
                {
                    slot = child.gameObject.AddComponent<InventoryEquipmentSlot>();
                    Transform icon = child.Find(SLOT_ICON);
                    slot.Configure(SlotId(child.name.Substring(0, child.name.Length - SLOT_SUFFIX.Length)), icon != null ? icon.GetComponent<Image>() : null);
                    EditorUtility.SetDirty(slot);
                }

                if ((slot.SlotId == OutfitSlotRules.HELMET_SLOT || slot.SlotId == OutfitSlotRules.BACKPACK_SLOT) && slot.LockedSprite == null)
                {
                    slot.SetLockedSprite(LoadSprite(HELMET_BLOCKER_SPRITE));
                    EditorUtility.SetDirty(slot);
                }

                slots.Add(slot);
            }

            InventoryEquipmentPanel equipment = panel.GetComponent<InventoryEquipmentPanel>();

            if (equipment == null)
            {
                equipment = panel.gameObject.AddComponent<InventoryEquipmentPanel>();
            }

            equipment.Configure(InventoryDemoContentBuilder.EQUIPMENT, InventoryDemoContentBuilder.OWNER, EQUIPMENT_TITLE, cellSize);
            equipment.Configure(AssetDatabase.LoadAssetAtPath<global::Inventory.InventorySlotLayout>(InventoryDemoContentBuilder.EQUIPMENT_LAYOUT_PATH), slots);
            ApplyTransparentStyle(equipment);
            var serialized = new SerializedObject(equipment);
            serialized.FindProperty("_applyBackgroundToImage").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(equipment);
            return equipment;
        }

        private static RectTransform FindSlotPanel(Transform root)
        {
            foreach (RectTransform candidate in root.GetComponentsInChildren<RectTransform>(true))
            {
                for (int i = 0; i < candidate.childCount; i++)
                {
                    if (candidate.GetChild(i).name.EndsWith(SLOT_SUFFIX, System.StringComparison.Ordinal))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static string SlotId(string slotName)
        {
            foreach ((string Name, string SlotId) alias in _slotAliases)
            {
                if (string.Equals(alias.Name, slotName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return alias.SlotId;
                }
            }

            return slotName;
        }

        private static void ApplyTransparentStyle(InventoryGridViewBase grid)
        {
            var serialized = new SerializedObject(grid);
            SerializedProperty style = serialized.FindProperty("_style");
            style.FindPropertyRelative("_gridBackground").colorValue = Color.clear;
            style.FindPropertyRelative("_cellColor").colorValue = Color.clear;
            style.FindPropertyRelative("_showGrid").boolValue = false;
            style.FindPropertyRelative("_showBorder").boolValue = false;
            style.FindPropertyRelative("_validPlacementColor").colorValue = Color.clear;
            style.FindPropertyRelative("_invalidPlacementColor").colorValue = Color.clear;
            style.FindPropertyRelative("_mergePlacementColor").colorValue = new Color(1f, 0.85f, 0.3f, 0.28f);
            style.FindPropertyRelative("_compatibleSlotColor").colorValue = new Color(1f, 1f, 1f, 0.1f);
            style.FindPropertyRelative("_lockedSlotColor").colorValue = new Color(0f, 0f, 0f, 0.5f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            grid.Rebuild();
        }

        private static T Grid<T>(RectTransform parent, string name, Vector2 topLeft) where T : InventoryGridViewBase
        {
            RectTransform rect = InventoryUIFactory.CreateRect(name, parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = Color.clear;
            return rect.gameObject.AddComponent<T>();
        }

        private static T Widget<T>(RectTransform parent, string name) where T : Component
        {
            RectTransform rect = InventoryUIFactory.CreateRect(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            T widget = rect.gameObject.AddComponent<T>();
            rect.gameObject.SetActive(false);
            return widget;
        }

        private static RectTransform Place(RectTransform parent, string name, Rect rect)
        {
            RectTransform child = InventoryUIFactory.CreateRect(name, parent);
            child.anchorMin = new Vector2(0f, 1f);
            child.anchorMax = new Vector2(0f, 1f);
            child.pivot = new Vector2(0f, 1f);
            child.anchoredPosition = new Vector2(rect.x, -rect.y);
            child.sizeDelta = rect.size;
            return child;
        }

        private static Image Img(RectTransform parent, string name, Rect rect, Sprite sprite, Color color)
        {
            Image image = Place(parent, name, rect).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Label(RectTransform parent, string name, Rect rect, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions alignment)
        {
            TextMeshProUGUI label = Place(parent, name, rect).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;

            if (font != null)
            {
                label.font = font;
            }

            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = true;
            return label;
        }

        private static Rect U(float x, float y, float width, float height) => new(x * SCALE_X, y * SCALE_Y, width * SCALE_X, height * SCALE_Y);

        private static void ConfigureSprites()
        {
            foreach (string path in Directory.GetFiles(SPRITES_FOLDER, "*.png"))
            {
                string assetPath = path.Replace('\\', '/');

                if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;

                if (Path.GetFileNameWithoutExtension(assetPath) == "gra_hint_window")
                {
                    importer.spriteBorder = new Vector4(60f, 85f, 65f, 150f);
                    importer.spritePixelsPerUnit = 200f;
                }

                importer.SaveAndReimport();
            }
        }
    }
}
