using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Inventory;
using Inventory.UI;
using RaidIntoDarkness.Integrations.Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RaidIntoDarkness.UI
{
    [DisallowMultipleComponent]
    public sealed class SpawnMenuView : MonoBehaviour
    {
        private const int DEFINITIONS_PER_FRAME = 64;
        private const float TOOLTIP_DELAY = 0.3f;
        private const double DOUBLE_CLICK_SECONDS = 0.3d;

        [SerializeField] private RectTransform _categoryContent;
        [SerializeField] private SpawnMenuCategoryRow _categoryTemplate;
        [SerializeField] private SpawnMenuGrid _grid;
        [SerializeField] private TMP_InputField _search;
        [SerializeField] private Button _sortName;
        [SerializeField] private Button _sortWeight;
        [SerializeField] private Button _sortPrice;
        [SerializeField] private Button _sortDirection;
        [SerializeField] private TMP_Text _directionLabel;
        [SerializeField] private TMP_Text _catalogMessage;
        [SerializeField] private GameObject _details;
        [SerializeField] private TMP_Text _emptyDetails;
        [SerializeField] private TMP_Text _itemName;
        [SerializeField] private InventoryItemView _preview;
        [SerializeField] private Image _previewIcon;
        [SerializeField] private TMP_Text _weight;
        [SerializeField] private TMP_Text _quantityLabel;
        [SerializeField] private TMP_InputField _quantity;
        [SerializeField] private Button _decrease;
        [SerializeField] private Button _increase;
        [SerializeField] private GameObject _conditionBlock;
        [SerializeField] private Slider _condition;
        [SerializeField] private TMP_Text _conditionValue;
        [SerializeField] private Button _grant;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private InventoryTooltip _tooltip;

        private readonly List<SpawnMenuCategoryRow> _rows = new();
        private readonly List<SpawnMenuCatalog.Entry> _results = new();
        private readonly List<InventoryItemStat> _tooltipStats = new();
        private PlayerInventoryRuntime _inventory;
        private InventoryWindow _inventoryWindow;
        private EquipmentStatsFormatter _statsFormatter;
        private InputActionReference _fullStatsModifier;
        private IReadOnlyList<ItemDefinition> _definitions;
        private SpawnMenuCatalog _catalog;
        private Coroutine _load;
        private SpawnMenuCatalog.Category _category;
        private SpawnMenuCatalog.Entry _selected;
        private SpawnMenuCatalog.Entry _lastClicked;
        private SpawnMenuSort _sort;
        private SpawnMenuItemCell _hoveredCell;
        private SpawnMenuCatalog.Entry _hoveredEntry;
        private Camera _hoverCamera;
        private Vector2 _hoverPosition;
        private float _hoverStart;
        private int _tooltipCondition;
        private int _tooltipAmount;
        private double _lastClick;
        private int _lastPointer;
        private bool _descending;
        private bool _tooltipFullCondition;

        internal void Bind(PlayerInventoryRuntime inventory, InventoryWindow window, EquipmentStatsFormatter statsFormatter, InputActionReference fullStatsModifier)
        {
            _inventory = inventory;
            _inventoryWindow = window;
            _statsFormatter = statsFormatter;
            _fullStatsModifier = fullStatsModifier;
            if (isActiveAndEnabled)
            {
                EnsureCatalog();
            }
        }

        private void Awake()
        {
            _search.onValueChanged.AddListener(SearchChanged);
            _sortName.onClick.AddListener(SortByName);
            _sortWeight.onClick.AddListener(SortByWeight);
            _sortPrice.onClick.AddListener(SortByPrice);
            _sortDirection.onClick.AddListener(ToggleDirection);
            _quantity.onValueChanged.AddListener(PreviewQuantity);
            _quantity.onEndEdit.AddListener(CommitQuantity);
            _decrease.onClick.AddListener(DecreaseQuantity);
            _increase.onClick.AddListener(IncreaseQuantity);
            _condition.onValueChanged.AddListener(ChangeCondition);
            _grant.onClick.AddListener(GrantSelected);
        }

        private void OnEnable()
        {
            RefreshSort();
            RefreshDetails();
            EnsureCatalog();
        }

        private void OnDisable()
        {
            if (_load != null)
            {
                StopCoroutine(_load);
                _load = null;
            }
            _lastClicked = null;
            LeaveItem(_hoveredCell);
        }

        private void LateUpdate()
        {
            if (_hoveredEntry == null || Time.unscaledTime - _hoverStart < TOOLTIP_DELAY)
            {
                return;
            }

            Vector2 pointer = Pointer.current != null ? Pointer.current.position.ReadValue() : _hoverPosition;
            bool fullCondition = _fullStatsModifier != null && _fullStatsModifier.action != null && _fullStatsModifier.action.IsPressed();
            int condition = _hoveredEntry.Condition;
            int amount = _hoveredEntry.GetUnitCount(_hoveredEntry.Quantity);
            if (_tooltip.IsVisible && condition == _tooltipCondition && fullCondition == _tooltipFullCondition && amount == _tooltipAmount)
            {
                _tooltip.Follow(pointer, _hoverCamera);
                return;
            }

            _tooltipCondition = condition;
            _tooltipAmount = amount;
            _tooltipFullCondition = fullCondition;
            ItemDefinition definition = _hoveredEntry.Definition;
            float normalizedCondition = condition / 100f;
            _tooltipStats.Clear();
            _statsFormatter.CollectDefinition(definition, normalizedCondition, fullCondition, _tooltipStats);
            _tooltip.ShowDefinition(definition, pointer, _hoverCamera, _tooltipStats, _statsFormatter.DescribeDefinition(definition, normalizedCondition, amount));
        }

        internal void SelectCategory(SpawnMenuCatalog.Category category)
        {
            if (_category == category)
            {
                return;
            }

            _category = category;
            foreach (SpawnMenuCategoryRow row in _rows)
            {
                row.SetSelected(category);
            }
            RefreshResults();
        }

        internal void ToggleCategory(SpawnMenuCatalog.Category category)
        {
            category.Expanded = !category.Expanded;
            if (!category.Expanded && _category.BelongsTo(category))
            {
                SelectCategory(category);
            }
            RefreshTree();
        }

        internal void ClickItem(SpawnMenuCatalog.Entry entry, PointerEventData eventData)
        {
            double now = Time.unscaledTimeAsDouble;
            bool doubleClick = _lastClicked == entry && _lastPointer == eventData.pointerId && eventData.clickCount > 1 && now - _lastClick <= DOUBLE_CLICK_SECONDS;
            _lastClicked = doubleClick ? null : entry;
            _lastClick = now;
            _lastPointer = eventData.pointerId;
            SelectItem(entry);
            if (doubleClick)
            {
                GrantSelected();
            }
        }

        internal void HoverItem(SpawnMenuItemCell cell, SpawnMenuCatalog.Entry entry, PointerEventData eventData)
        {
            _hoveredCell = cell;
            _hoveredEntry = entry;
            _hoverPosition = eventData.position;
            _hoverCamera = eventData.enterEventCamera;
            _hoverStart = Time.unscaledTime;
            _tooltip.Hide();
        }

        internal void LeaveItem(SpawnMenuItemCell cell)
        {
            if (cell == _hoveredCell)
            {
                _hoveredCell = null;
                _hoveredEntry = null;
                _hoverCamera = null;
                _tooltip.Hide();
            }
        }

        private void EnsureCatalog()
        {
            IReadOnlyList<ItemDefinition> definitions = _inventory != null && _inventory.Database != null && _inventoryWindow != null && _inventoryWindow.Model != null ? _inventory.Database.Definitions : null;
            if (_catalog != null && ReferenceEquals(_definitions, definitions))
            {
                return;
            }
            if (_load != null)
            {
                StopCoroutine(_load);
                _load = null;
            }
            _definitions = definitions;
            _catalog = null;
            _category = null;
            RefreshTree();
            RefreshResults();
            if (_definitions != null)
            {
                _tooltip.Theme = _inventoryWindow.Theme;
                _load = StartCoroutine(IndexCatalog(_definitions));
            }
        }

        private IEnumerator IndexCatalog(IReadOnlyList<ItemDefinition> definitions)
        {
            var catalog = new SpawnMenuCatalog();
            for (int i = 0; i < definitions.Count; i++)
            {
                if (i % DEFINITIONS_PER_FRAME == 0)
                {
                    yield return null;
                }
                catalog.Add(definitions[i]);
            }
            if (definitions.Count == 0)
            {
                yield return null;
            }
            _catalog = catalog;
            _load = null;
            _category = catalog.Root;
            RefreshTree();
            RefreshResults();
        }

        private void RefreshTree()
        {
            int index = 0;
            if (_catalog != null && _catalog.Entries.Count > 0)
            {
                ShowCategory(_catalog.Root, ref index);
            }
            for (int i = index; i < _rows.Count; i++)
            {
                _rows[i].gameObject.SetActive(false);
            }
        }

        private void ShowCategory(SpawnMenuCatalog.Category category, ref int index)
        {
            if (index == _rows.Count)
            {
                _rows.Add(Instantiate(_categoryTemplate, _categoryContent));
            }
            SpawnMenuCategoryRow row = _rows[index++];
            row.Bind(this, category, category == _category);
            row.gameObject.SetActive(true);
            if (category.Expanded)
            {
                foreach (SpawnMenuCatalog.Category child in category.Children)
                {
                    ShowCategory(child, ref index);
                }
            }
        }

        private void RefreshResults()
        {
            _results.Clear();
            if (_catalog != null)
            {
                _catalog.Filter(_results, _category, _search.text.Trim(), _sort, _descending);
            }
            if (_selected != null && !_results.Contains(_selected))
            {
                SelectItem(null);
            }

            _lastClicked = null;
            LeaveItem(_hoveredCell);
            var backpack = _inventoryWindow != null ? _inventoryWindow.FindGrid(_inventoryWindow.PrimaryContainerId) : null;
            Vector2 cellSize = backpack != null ? backpack.CellSize : new Vector2(52f, 52f);
            _grid.Show(this, _results, _inventoryWindow, cellSize, _catalog != null ? _catalog.MaxWidth : 1);
            _catalogMessage.text = _catalog == null ? (_definitions == null ? "Инвентарь недоступен" : "Загрузка предметов…") : _results.Count == 0 ? "Предметы не найдены" : string.Empty;
            _catalogMessage.gameObject.SetActive(_catalogMessage.text.Length > 0);
        }

        private void SelectItem(SpawnMenuCatalog.Entry entry)
        {
            if (_selected == entry)
            {
                return;
            }

            _selected = entry;
            _status.text = string.Empty;
            _grid.SetSelected(entry);
            RefreshDetails();
        }

        private void RefreshDetails()
        {
            bool selected = _selected != null;
            _details.SetActive(selected);
            _emptyDetails.gameObject.SetActive(!selected);
            if (!selected)
            {
                return;
            }

            ItemDefinition definition = _selected.Definition;
            _itemName.text = definition.DisplayName;
            _preview.BindDefinition(definition, _previewIcon, _inventoryWindow != null ? _inventoryWindow.IconProvider : null);
            _quantityLabel.text = _selected.PackSize > 1 ? $"Пачки (по {_selected.PackSize} патр.)" : "Количество";
            SetQuantity(_selected.Quantity);
            _conditionBlock.SetActive(_selected.SupportsCondition);
            _condition.SetValueWithoutNotify(_selected.Condition);
            _conditionValue.SetText("Прочность {0}%", _selected.Condition);
        }

        private void SearchChanged(string value) => RefreshResults();

        private void SortByName() => SetSort(SpawnMenuSort.Name);

        private void SortByWeight() => SetSort(SpawnMenuSort.Weight);

        private void SortByPrice() => SetSort(SpawnMenuSort.Price);

        private void SetSort(SpawnMenuSort sort)
        {
            _sort = sort;
            RefreshSort();
            RefreshResults();
        }

        private void ToggleDirection()
        {
            _descending = !_descending;
            RefreshSort();
            RefreshResults();
        }

        private void RefreshSort()
        {
            _sortName.interactable = _sort != SpawnMenuSort.Name;
            _sortWeight.interactable = _sort != SpawnMenuSort.Weight;
            _sortPrice.interactable = _sort != SpawnMenuSort.Price;
            _directionLabel.text = _descending ? "Убыв." : "Возр.";
        }

        private void PreviewQuantity(string text)
        {
            if (_selected != null)
            {
                RefreshWeight(ReadQuantity(text));
            }
        }

        private void CommitQuantity(string text)
        {
            if (_selected != null)
            {
                SetQuantity(ReadQuantity(text));
            }
        }

        private int ReadQuantity(string text)
        {
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) && quantity > 0 && quantity <= _selected.MaxQuantity ? quantity : _selected.Quantity;
        }

        private void DecreaseQuantity()
        {
            CommitQuantity(_quantity.text);
            if (_selected != null && _selected.Quantity > 1)
            {
                SetQuantity(_selected.Quantity - 1);
            }
        }

        private void IncreaseQuantity()
        {
            CommitQuantity(_quantity.text);
            if (_selected != null && _selected.Quantity < _selected.MaxQuantity)
            {
                SetQuantity(_selected.Quantity + 1);
            }
        }

        private void SetQuantity(int quantity)
        {
            _selected.Quantity = quantity;
            _quantity.SetTextWithoutNotify(quantity.ToString(CultureInfo.InvariantCulture));
            _decrease.interactable = quantity > 1;
            _increase.interactable = quantity < _selected.MaxQuantity;
            RefreshWeight(quantity);
        }

        private void RefreshWeight(int quantity)
        {
            float unitWeight = _selected.Definition.Weight;
            float totalWeight = unitWeight * _selected.GetUnitCount(quantity);
            InventoryTheme theme = _inventoryWindow != null ? _inventoryWindow.Theme : null;
            string unit = theme != null ? theme.FormatWeight(unitWeight) : InventoryUIFactory.FormatWeight(unitWeight) + " кг";
            string total = theme != null ? theme.FormatWeight(totalWeight) : InventoryUIFactory.FormatWeight(totalWeight) + " кг";
            _weight.text = unit + "\n" + total;
        }

        private void ChangeCondition(float condition)
        {
            if (_selected != null)
            {
                _selected.Condition = Mathf.RoundToInt(condition);
                _conditionValue.SetText("Прочность {0}%", _selected.Condition);
            }
        }

        private void GrantSelected()
        {
            if (_selected == null)
            {
                return;
            }

            CommitQuantity(_quantity.text);
            int packSize = _selected.PackSize;
            int quantity = _selected.GetUnitCount(_selected.Quantity);
            int condition = _selected.SupportsCondition ? _selected.Condition : 100;
            int added = InventorySpawn.Grant(_inventory, _inventoryWindow, _selected.Definition, quantity, condition, out InventoryFailure failure);
            if (added == quantity)
            {
                _status.text = packSize > 1 ? $"Выдано пачек: {_selected.Quantity} ({added} патр.)" : $"Выдано: {added}";
                return;
            }

            string reason = failure switch
            {
                InventoryFailure.NoSpace => "В рюкзаке нет места",
                InventoryFailure.ContainerNotFound => "Инвентарь недоступен",
                _ => "Не удалось выдать предмет"
            };
            string unit = packSize > 1 ? " патр." : string.Empty;
            _status.text = added == 0 ? reason : $"Выдано: {added} из {quantity}{unit}. {reason}.";
        }
    }
}
