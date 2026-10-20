using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Inventory.UI
{
    public enum InventoryDropKind
    {
        None,
        Valid,
        Merge,
        Invalid
    }

    public sealed class InventoryDragState
    {
        public ItemInstance Item;
        public InventoryGridViewBase SourceGrid;
        public InventoryItemView SourceView;
        public InventoryItemView Ghost;
        public bool Rotated;
        public int Amount;
        public bool IsSplit;
        public Vector2 LastScreenPosition;
        public Camera EventCamera;
        public InventoryGridViewBase TargetGrid;
        public Vector2Int TargetOrigin;
        public InventoryDropKind Kind;
        public InventoryFailure Failure;
        public bool AutoPlace;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Inventory/Inventory Window")]
    public sealed class InventoryWindow : MonoBehaviour
    {
        private const float STATUS_DURATION = 2.5f;

        [SerializeField] private RectTransform _content;
        [SerializeField] private List<InventoryGridViewBase> _grids = new();
        [SerializeField] private RectTransform _dragLayer;
        [SerializeField] private InventoryTooltip _tooltip;
        [SerializeField] private InventoryContextMenu _contextMenu;
        [SerializeField] private InventorySplitDialog _splitDialog;
        [SerializeField] private InventoryInspectPanel _inspectPanel;
        [SerializeField] private TMP_Text _weightText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private string _primaryContainerId = "Backpack";
        [SerializeField] private string _secondaryContainerId = "Stash";
        [SerializeField] private string _equipmentContainerId = "Equipment";
        [SerializeField] private string _weightOwnerId = "Player";
        [SerializeField] [Min(0f)] private float _maxCarryWeight;
        [SerializeField] [Min(0f)] private float _tooltipDelay = 0.3f;
        [SerializeField] private bool _startOpen;
        [SerializeField] private InventoryInputBindings _input = new();
        [SerializeField] private InventoryTheme _theme;
        [SerializeField] private RectTransform _secondaryRoot;
        [SerializeField] private string _weightFormat = "Weight: {0} / {1} kg";
        [SerializeField] private string _weightFormatUnlimited = "Weight: {0} kg";
        [SerializeField] private Color _overweightColor = new(1f, 0.55f, 0.2f, 1f);

        private readonly List<RaycastResult> _raycastResults = new();
        private readonly List<IInventoryItemAction> _visibleActions = new();
        private readonly List<InventoryItemStat> _tooltipStats = new();
        private readonly List<InventoryContextMenu.Entry> _menuEntries = new();
        private readonly List<RectInt> _compatibleAreas = new();
        private InventoryDragState _drag;
        private InventoryItemView _hovered;
        private float _hoverStart;
        private Vector2 _hoverPosition;
        private Camera _hoverCamera;
        private float _statusUntil;
        private string _hint = string.Empty;
        private bool _initialized;

        public InventoryModel Model { get; private set; }
        public IItemIconProvider IconProvider { get; set; }
        public InventoryActionRegistry Actions { get; private set; }
        public InventoryInputBindings Input => _input;
        public bool IsOpen { get; private set; }
        public bool IsDragging => _drag != null;
        public InventoryDragState Drag => _drag;
        public IReadOnlyList<InventoryGridViewBase> Grids => _grids;
        public RectTransform Content => _content;
        public InventoryTooltip Tooltip => _tooltip;
        public InventoryContextMenu ContextMenu => _contextMenu;
        public InventorySplitDialog SplitDialog => _splitDialog;
        public InventoryInspectPanel InspectPanel => _inspectPanel;
        public string PrimaryContainerId => _primaryContainerId;
        public string SecondaryContainerId => _secondaryContainerId;
        public string EquipmentContainerId => _equipmentContainerId;
        public string LastStatus { get; private set; } = string.Empty;
        public string Hint => _hint;
        public InventoryTheme Theme => _theme;
        public bool SecondaryVisible => _secondaryRoot == null || _secondaryRoot.gameObject.activeSelf;
        public bool IsSecondaryOpen => _secondaryRoot != null && _secondaryRoot.gameObject.activeSelf && Model != null && Model.GetContainer(_secondaryContainerId) != null;
        public Func<ItemInstance, bool> ItemFilter { get; private set; }
        public Func<ItemInstance, InventoryItemDecoration> ItemDecorator { get; set; }
        public Action<ItemInstance, List<InventoryItemStat>> ItemStatsProvider { get; set; }
        public Func<ItemInstance, string> ItemDescriptionProvider { get; set; }
        public Func<InventoryActionContext, IInventoryItemAction, InventoryActionStyle> ActionStyleProvider { get; set; }
        public IInventoryEquipmentGate EquipmentGate { get; set; }
        public ItemInstance HoveredItem => _hovered != null ? _hovered.Item : null;

        public event Action<InventoryWindow> Opened;
        public event Action<InventoryWindow> Closed;
        public event Action<ItemInstance> ItemUsed;
        public event Action<ItemInstance> ItemDropped;
        public event Action<ItemInstance> ItemInspected;

        public void Configure(RectTransform content, RectTransform dragLayer, InventoryTooltip tooltip, InventoryContextMenu contextMenu, InventorySplitDialog splitDialog, InventoryInspectPanel inspectPanel, TMP_Text weightText, TMP_Text statusText, IEnumerable<InventoryGridViewBase> grids)
        {
            _content = content;
            _dragLayer = dragLayer;
            _tooltip = tooltip;
            _contextMenu = contextMenu;
            _splitDialog = splitDialog;
            _inspectPanel = inspectPanel;
            _weightText = weightText;
            _statusText = statusText;
            _grids = new List<InventoryGridViewBase>(grids);
        }

        public void ConfigureContainers(string primaryContainerId, string secondaryContainerId, string equipmentContainerId, string weightOwnerId, float maxCarryWeight)
        {
            _primaryContainerId = primaryContainerId;
            _secondaryContainerId = secondaryContainerId;
            _equipmentContainerId = equipmentContainerId;
            _weightOwnerId = weightOwnerId;
            _maxCarryWeight = maxCarryWeight;
        }

        public void SetMaxCarryWeight(float maxCarryWeight)
        {
            _maxCarryWeight = Mathf.Max(0f, maxCarryWeight);
            UpdateWeight();
        }

        public void ConfigurePresentation(InventoryTheme theme, RectTransform secondaryRoot, string weightFormat, string weightFormatUnlimited, Color overweightColor)
        {
            _theme = theme;
            _secondaryRoot = secondaryRoot;
            _weightFormat = weightFormat;
            _weightFormatUnlimited = weightFormatUnlimited;
            _overweightColor = overweightColor;
        }

        public string Label(string key, string fallback) => _theme != null ? _theme.Label(key, fallback) : fallback;

        public string FormatLabel(string key, string fallback, params object[] arguments) => string.Format(System.Globalization.CultureInfo.InvariantCulture, Label(key, fallback), arguments);

        public void SetSecondaryVisible(bool visible)
        {
            if (_secondaryRoot == null || _secondaryRoot.gameObject.activeSelf == visible)
            {
                return;
            }

            CancelDrag();
            HideTransientWidgets();
            _secondaryRoot.gameObject.SetActive(visible);
        }

        public void RefreshItems()
        {
            foreach (InventoryGridViewBase grid in _grids)
            {
                if (grid == null)
                {
                    continue;
                }

                foreach (InventoryItemView view in grid.ItemViews.Values)
                {
                    if (view != null)
                    {
                        view.Refresh();
                    }
                }
            }
        }

        public void SetItemFilter(Func<ItemInstance, bool> filter)
        {
            ItemFilter = filter;

            foreach (InventoryGridViewBase grid in _grids)
            {
                if (grid != null)
                {
                    grid.ApplyFilter();
                }
            }
        }

        public InventoryGridViewBase FindGrid(string containerId) => _grids.Find(grid => grid != null && grid.ContainerId == containerId);

        public void Bind(InventoryModel model, IItemIconProvider iconProvider)
        {
            Initialize();
            Unbind();
            Model = model;
            IconProvider = iconProvider;

            foreach (InventoryGridViewBase grid in _grids)
            {
                if (grid == null)
                {
                    continue;
                }

                InventoryContainer container = model.GetContainer(grid.ContainerId) ?? model.AddContainer(grid.CreateContainer());
                grid.Bind(this, container);
            }

            Model.Changed += OnModelChanged;
            UpdateWeight();
        }

        public void Unbind()
        {
            CancelDrag();

            if (Model != null)
            {
                Model.Changed -= OnModelChanged;
            }

            foreach (InventoryGridViewBase grid in _grids)
            {
                if (grid != null)
                {
                    grid.Unbind();
                }
            }

            Model = null;
        }

        public void Open()
        {
            Initialize();

            if (IsOpen)
            {
                return;
            }

            IsOpen = true;
            _input.Enable();

            if (_content != null)
            {
                _content.gameObject.SetActive(true);
            }

            foreach (InventoryGridViewBase grid in _grids)
            {
                if (grid != null && grid.Container != null)
                {
                    grid.SyncItems();
                }
            }

            UpdateWeight();
            Opened?.Invoke(this);
        }

        public void Close()
        {
            Initialize();

            if (IsOpen == false)
            {
                return;
            }

            CancelDrag();
            HideTransientWidgets();
            IsOpen = false;

            if (_content != null)
            {
                _content.gameObject.SetActive(false);
            }

            Closed?.Invoke(this);
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void HideTransientWidgets()
        {
            _hovered = null;
            _tooltip?.Hide();
            _contextMenu?.Hide();
            _splitDialog?.Hide();
            _inspectPanel?.Hide();
        }

        public bool HasModalWidget => (_contextMenu != null && _contextMenu.IsVisible) || (_splitDialog != null && _splitDialog.IsVisible) || (_inspectPanel != null && _inspectPanel.IsVisible);

        public bool CloseTopWidget()
        {
            if (IsDragging)
            {
                CancelDrag();
                return true;
            }

            if (_contextMenu != null && _contextMenu.IsVisible)
            {
                _contextMenu.Hide();
                return true;
            }

            if (_splitDialog != null && _splitDialog.IsVisible)
            {
                _splitDialog.Hide();
                return true;
            }

            if (_inspectPanel != null && _inspectPanel.IsVisible)
            {
                _inspectPanel.Hide();
                return true;
            }

            return false;
        }

        public void HandleItemPointerEnter(InventoryItemView view, PointerEventData eventData)
        {
            if (view.IsGhost)
            {
                return;
            }

            _hovered = view;
            _hoverStart = Time.unscaledTime;
            _hoverPosition = eventData.position;
            _hoverCamera = eventData.enterEventCamera;

            if (_drag == null && HasModalWidget == false && _input.TransferHeld && CanHoverTransfer(view.Item))
            {
                _hovered = null;
                QuickTransfer(view.Item);
            }
        }

        public void HandleItemPointerExit(InventoryItemView view, PointerEventData eventData)
        {
            if (_hovered == view)
            {
                _hovered = null;
                _tooltip?.Hide();
            }
        }

        public void HandleItemClick(InventoryItemView view, PointerEventData eventData)
        {
            if (IsDragging || view.Item == null || Model == null)
            {
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                OpenContextMenu(view.Item, view.Grid, eventData.position, eventData.pressEventCamera);
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (_input.TransferHeld && CanHoverTransfer(view.Item))
            {
                QuickTransfer(view.Item);
            }
            else if (eventData.clickCount >= 2)
            {
                ActivateItem(view.Item);
            }
        }

        public bool CanHoverTransfer(ItemInstance item) => item != null && IsSecondaryOpen && (item.ContainerId == _primaryContainerId || item.ContainerId == _secondaryContainerId);

        public void ActivateItem(ItemInstance item)
        {
            if (item == null || Model == null || Model.ContainsItem(item) == false)
            {
                return;
            }

            if (IsEquipped(item))
            {
                UnequipItem(item);
            }
            else if (IsSecondaryOpen && GetQuickTransferTarget(item) != null)
            {
                QuickTransfer(item);
            }
            else if (item.Definition.TryGetComponent(out ConsumableItemComponent _))
            {
                UseItem(item);
            }
            else if (HasEquipmentSlotFor(item))
            {
                EquipItem(item);
            }
        }

        public void HandleBeginDrag(InventoryItemView view, PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || view.Item == null)
            {
                return;
            }

            BeginDrag(view.Item, view.Grid, view.Item.Amount, eventData.position, eventData.pressEventCamera);
        }

        public void HandleDrag(PointerEventData eventData)
        {
            if (_drag != null)
            {
                UpdateDrag(eventData.position, eventData.pressEventCamera);
            }
        }

        public void HandleEndDrag(PointerEventData eventData)
        {
            if (_drag != null)
            {
                UpdateDrag(eventData.position, eventData.pressEventCamera);
                CompleteDrag();
            }
        }

        public bool BeginDrag(ItemInstance item, InventoryGridViewBase sourceGrid, int amount, Vector2 screenPosition, Camera eventCamera)
        {
            if (IsOpen == false || item == null || sourceGrid == null || Model == null || Model.ContainsItem(item) == false)
            {
                return false;
            }

            CancelDrag();
            HideTransientWidgets();
            int resolvedAmount = Mathf.Clamp(amount, 1, item.Amount);
            bool slotItem = sourceGrid.UsesSlotIcon(item, out _);
            _drag = new InventoryDragState
            {
                Item = item,
                SourceGrid = sourceGrid,
                SourceView = sourceGrid.GetItemView(item),
                Rotated = slotItem == false && item.Location.Rotated,
                Amount = resolvedAmount,
                IsSplit = resolvedAmount < item.Amount,
                EventCamera = eventCamera
            };

            RectTransform layer = _dragLayer != null ? _dragLayer : (RectTransform)transform;
            _drag.Ghost = InventoryItemView.Create(layer, HideFlags.DontSave);
            _drag.Ghost.name = "Dragged Item";
            _drag.Ghost.BindGhost(item, sourceGrid, sourceGrid.CellSize, _drag.Rotated, resolvedAmount);
            _drag.Ghost.transform.SetAsLastSibling();

            if (_drag.IsSplit == false)
            {
                _drag.SourceView?.SetDragSource(true);
            }

            ShowCompatibleSlots(item);
            UpdateDrag(screenPosition, eventCamera);
            return true;
        }

        public void UpdateDrag(Vector2 screenPosition, Camera eventCamera)
        {
            if (_drag == null)
            {
                return;
            }

            _drag.LastScreenPosition = screenPosition;
            _drag.EventCamera = eventCamera;
            RectTransform ghostRect = _drag.Ghost.RectTransform;
            var ghostParent = (RectTransform)ghostRect.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(ghostParent, screenPosition, eventCamera, out Vector2 local);
            ghostRect.anchorMin = new Vector2(0.5f, 0.5f);
            ghostRect.anchorMax = new Vector2(0.5f, 0.5f);
            ghostRect.localPosition = local;
            InventoryGridViewBase previousTarget = _drag.TargetGrid;
            InventoryGridViewBase target = FindGridAt(screenPosition);

            if (previousTarget != null && previousTarget != target)
            {
                previousTarget.SetPlacementHighlight(default, Color.clear);
            }

            _drag.TargetGrid = target;
            _drag.Kind = InventoryDropKind.None;

            if (target == null)
            {
                return;
            }

            Evaluate(target, screenPosition, eventCamera, out Vector2Int origin, out RectInt area, out InventoryDropKind kind, out InventoryFailure failure);
            _drag.TargetOrigin = origin;
            _drag.Kind = kind;
            _drag.Failure = failure;
            Color highlight = kind switch
            {
                InventoryDropKind.Valid => target.Style.ValidPlacementColor,
                InventoryDropKind.Merge => target.Style.MergePlacementColor,
                _ => target.Style.InvalidPlacementColor
            };

            target.SetPlacementHighlight(area, highlight);
        }

        public void RotateDrag()
        {
            if (_drag == null || _drag.Item.Definition.AllowRotation == false)
            {
                return;
            }

            _drag.Rotated = _drag.Rotated == false;
            _drag.Ghost.SetGhostRotation(_drag.Rotated, _drag.SourceGrid.CellSize);
            UpdateDrag(_drag.LastScreenPosition, _drag.EventCamera);
        }

        public InventoryResult CompleteDrag()
        {
            if (_drag == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            InventoryDragState drag = _drag;
            InventoryResult result = InventoryResult.Fail(InventoryFailure.NoSlot);
            EndDrag();

            if (drag.TargetGrid != null && drag.TargetGrid.Container != null && drag.Kind != InventoryDropKind.None)
            {
                InventoryContainer target = drag.TargetGrid.Container;

                if (TryGateDrag(drag, target))
                {
                    return InventoryResult.Ok(drag.Item, drag.Item.Amount);
                }

                if (drag.AutoPlace)
                {
                    result = Model.TryTransfer(drag.Item.InstanceId, target.Id);
                }
                else
                {
                    result = drag.IsSplit ? Model.TrySplit(drag.Item.InstanceId, drag.Amount, target.Id, drag.TargetOrigin, drag.Rotated) : Model.TryMove(drag.Item.InstanceId, target.Id, drag.TargetOrigin, drag.Rotated);

                    if (result.Success == false && drag.IsSplit == false && target is SlottedContainer slotted && CanSwapIntoSlot(drag.Item, slotted, drag.TargetOrigin))
                    {
                        result = TrySwapIntoSlot(drag.Item, slotted, slotted.GetSlotAt(drag.TargetOrigin));
                    }
                }

                if (result.Success == false)
                {
                    ShowStatus(DescribeFailure(result.Failure));
                }
            }

            return result;
        }

        public void CancelDrag()
        {
            if (_drag != null)
            {
                EndDrag();
            }
        }

        public void OpenContextMenu(ItemInstance item, InventoryGridViewBase grid, Vector2 screenPosition, Camera eventCamera)
        {
            if (_contextMenu == null || item == null)
            {
                return;
            }

            _tooltip?.Hide();
            var context = new InventoryActionContext(this, item, grid);
            Actions.CollectVisible(context, _visibleActions);
            _menuEntries.Clear();

            foreach (IInventoryItemAction action in _visibleActions)
            {
                IInventoryItemAction captured = action;
                InventoryActionStyle style = ActionStyleProvider != null ? ActionStyleProvider(context, action) : default;
                _menuEntries.Add(new InventoryContextMenu.Entry(action.GetLabel(context), action.IsEnabled(context), () =>
                {
                    if (Model != null && Model.ContainsItem(item))
                    {
                        captured.Execute(context);
                    }
                }, style.Icon, style.Group));
            }

            _contextMenu.Show(screenPosition, eventCamera, _menuEntries);
            ApplyThemeTo(_contextMenu);
        }

        public bool CanUseItem(ItemInstance item) => item != null && item.Definition.GetComponent<ConsumableItemComponent>() != null;

        public void UseItem(ItemInstance item)
        {
            if (item.Definition.TryGetComponent(out ConsumableItemComponent consumable))
            {
                InventoryResult result = Model.TryRemove(item.InstanceId, consumable.AmountPerUse);

                if (result.Success == false)
                {
                    ShowStatus(DescribeFailure(result.Failure));
                    return;
                }
            }

            ShowStatus(FormatLabel("status_used", "Used {0}", item.Definition.DisplayName));
            ItemUsed?.Invoke(item);
        }

        public bool IsEquipped(ItemInstance item) => item != null && item.ContainerId == _equipmentContainerId && Model.GetContainer(_equipmentContainerId) is SlottedContainer;

        public bool HasEquipmentSlotFor(ItemInstance item)
        {
            if (item == null || Model == null || Model.TryGetContainer(_equipmentContainerId, out SlottedContainer equipment) == false)
            {
                return false;
            }

            foreach (SlotDefinition slot in equipment.Slots)
            {
                if (slot.Mode == SlotMode.Single && slot.Rule.AcceptsDefinition(item.Definition))
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanEquipItem(ItemInstance item)
        {
            if (HasEquipmentSlotFor(item) == false)
            {
                return false;
            }

            SlottedContainer equipment = (SlottedContainer)Model.GetContainer(_equipmentContainerId);

            foreach (SlotDefinition slot in equipment.Slots)
            {
                if (slot.Mode == SlotMode.Single && Model.CanMove(item.InstanceId, equipment.Id, slot.Bounds.position, false, false).Success)
                {
                    return true;
                }
            }

            foreach (SlotDefinition slot in EquipCandidates(item, equipment))
            {
                if (CanSwapIntoSlot(item, equipment, slot.Bounds.position))
                {
                    return true;
                }
            }

            return false;
        }

        public void EquipItem(ItemInstance item)
        {
            if (EquipmentGate != null && EquipmentGate.HandleEquip(item, null))
            {
                return;
            }

            EquipItemNow(item, null);
        }

        public void EquipItemNow(ItemInstance item, SlotDefinition slot)
        {
            if (item == null || Model == null || Model.ContainsItem(item) == false)
            {
                return;
            }

            if (slot != null && Model.TryGetContainer(_equipmentContainerId, out SlottedContainer target))
            {
                InventoryResult moved = Model.TryMove(item.InstanceId, target.Id, slot.Bounds.position, false, false);

                if (moved.Success == false && CanSwapIntoSlot(item, target, slot.Bounds.position))
                {
                    moved = TrySwapIntoSlot(item, target, slot);
                }

                Report(moved, FormatLabel("status_equipped", "Equipped {0}", item.Definition.DisplayName));
                return;
            }

            InventoryResult result = Model.TryEquip(item.InstanceId, _equipmentContainerId);

            if (result.Success == false && Model.TryGetContainer(_equipmentContainerId, out SlottedContainer equipment))
            {
                foreach (SlotDefinition candidate in EquipCandidates(item, equipment))
                {
                    if (CanSwapIntoSlot(item, equipment, candidate.Bounds.position))
                    {
                        result = TrySwapIntoSlot(item, equipment, candidate);
                        break;
                    }
                }
            }

            Report(result, FormatLabel("status_equipped", "Equipped {0}", item.Definition.DisplayName));
        }

        public void UnequipItem(ItemInstance item)
        {
            InventoryUnequipTarget target = InventoryUnequipTarget.Auto(_primaryContainerId);

            if (EquipmentGate != null && EquipmentGate.HandleUnequip(item, target))
            {
                return;
            }

            UnequipItemNow(item, target);
        }

        public InventoryResult UnequipItemNow(ItemInstance item, InventoryUnequipTarget target)
        {
            if (item == null || Model == null || Model.ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            InventoryResult result = target.AutoPlace ? Model.TryTransfer(item.InstanceId, target.ContainerId) : Model.TryMove(item.InstanceId, target.ContainerId, target.Origin, target.Rotated);

            if (result.Success == false && target.AutoPlace == false)
            {
                result = Model.TryTransfer(item.InstanceId, target.ContainerId);
            }

            Report(result, FormatLabel("status_unequipped", "Unequipped {0}", item.Definition.DisplayName));
            return result;
        }

        public void OpenSplitDialog(ItemInstance item)
        {
            if (_splitDialog == null || item.Amount < 2)
            {
                return;
            }

            _splitDialog.Show(item, amount => Report(Model.TrySplitAuto(item.InstanceId, amount), FormatLabel("status_split", "Split {0} x {1}", amount, item.Definition.DisplayName)));
            ApplyThemeTo(_splitDialog);
        }

        public InventoryContainer GetQuickTransferTarget(ItemInstance item)
        {
            if (item == null || Model == null)
            {
                return null;
            }

            string targetId = item.ContainerId == _primaryContainerId ? _secondaryContainerId : _primaryContainerId;
            InventoryContainer target = Model.GetContainer(targetId);
            if (target == null || target.Id == item.ContainerId || FindGrid(target.Id) == null)
            {
                return null;
            }

            return target.Id == _secondaryContainerId && SecondaryVisible == false ? null : target;
        }

        public string GetQuickTransferTargetName(ItemInstance item) => GetQuickTransferTarget(item)?.DisplayName ?? string.Empty;

        public void QuickTransfer(ItemInstance item)
        {
            InventoryContainer target = GetQuickTransferTarget(item);

            if (target != null && IsEquipped(item) && EquipmentGate != null && EquipmentGate.HandleUnequip(item, InventoryUnequipTarget.Auto(target.Id)))
            {
                return;
            }

            if (target != null)
            {
                Report(Model.TryTransfer(item.InstanceId, target.Id), FormatLabel("status_moved", "Moved to {0}", target.DisplayName));
            }
        }

        public void InspectItem(ItemInstance item)
        {
            if (_inspectPanel == null)
            {
                return;
            }

            var request = ItemIconRequest.ForItem(item);
            Sprite icon = null;

            if (IconProvider != null && IconProvider.TryGetIcon(request, out icon) == false)
            {
                IconProvider.RequestIcon(request, sprite =>
                {
                    if (_inspectPanel != null && _inspectPanel.Item == item)
                    {
                        _inspectPanel.SetIcon(sprite);
                    }
                }, default);
            }

            _inspectPanel.Show(item, icon != null ? icon : item.Definition.FallbackIcon);
            ApplyThemeTo(_inspectPanel);
            ItemInspected?.Invoke(item);
        }

        public void DropItem(ItemInstance item)
        {
            if (IsEquipped(item) && EquipmentGate != null && EquipmentGate.HandleDrop(item))
            {
                return;
            }

            DropItemNow(item);
        }

        public void DropItemNow(ItemInstance item)
        {
            if (item == null || Model == null || Model.ContainsItem(item) == false)
            {
                return;
            }

            InventoryResult result = Model.TryRemove(item.InstanceId);

            if (Report(result, FormatLabel("status_dropped", "Dropped {0}", item.Definition.DisplayName)))
            {
                ItemDropped?.Invoke(result.Item);
            }
        }

        public void ShowStatus(string message)
        {
            LastStatus = message ?? string.Empty;

            if (_statusText != null)
            {
                _statusText.text = LastStatus;
            }

            _statusUntil = Time.unscaledTime + STATUS_DURATION;
        }

        public void ShowHint(string hint)
        {
            string value = hint ?? string.Empty;

            if (_hint == value)
            {
                return;
            }

            _hint = value;

            if (_statusText != null && _statusUntil <= 0f)
            {
                _statusText.text = _hint;
            }
        }

        public void ClearHint() => ShowHint(string.Empty);

        public IReadOnlyList<InventoryItemStat> CollectStats(ItemInstance item)
        {
            _tooltipStats.Clear();

            if (item != null && ItemStatsProvider != null)
            {
                ItemStatsProvider(item, _tooltipStats);
            }

            return _tooltipStats;
        }

        public string CollectDescription(ItemInstance item) => item != null && ItemDescriptionProvider != null ? ItemDescriptionProvider(item) : null;

        public void RefreshTooltip()
        {
            if (_tooltip == null || _tooltip.IsVisible == false || _hovered == null || _hovered.Item == null)
            {
                return;
            }

            Vector2 pointer = UnityEngine.InputSystem.Pointer.current != null ? UnityEngine.InputSystem.Pointer.current.position.ReadValue() : _hoverPosition;
            _tooltip.Show(_hovered.Item, pointer, _hoverCamera, CollectStats(_hovered.Item), CollectDescription(_hovered.Item));
            ApplyThemeTo(_tooltip);
        }

        private string DescribeFailure(InventoryFailure failure) => Label("failure_" + failure, InventoryUIFactory.Describe(failure));

        private void ApplyThemeTo(Component widget)
        {
            if (_theme != null && widget != null)
            {
                _theme.ApplyTo(widget.transform, true);
            }
        }

        private bool Report(InventoryResult result, string successMessage)
        {
            ShowStatus(result.Success ? successMessage : DescribeFailure(result.Failure));
            return result.Success;
        }

        private void Awake()
        {
            Initialize();

            if (_startOpen == false && _content != null)
            {
                _content.gameObject.SetActive(false);
            }

            IsOpen = _startOpen;
        }

        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            Actions = new InventoryActionRegistry();
            InventoryBuiltInActions.RegisterDefaults(Actions);

            if (_tooltip != null)
            {
                _tooltip.Theme = _theme;
            }

            if (_contextMenu != null)
            {
                _contextMenu.Theme = _theme;
            }

            if (_splitDialog != null)
            {
                _splitDialog.Theme = _theme;
            }

            if (_inspectPanel != null)
            {
                _inspectPanel.Theme = _theme;
            }

            if (_grids.Count == 0)
            {
                _grids.AddRange(GetComponentsInChildren<InventoryGridViewBase>(true));
            }
        }

        private void OnDestroy() => Unbind();

        private void Update()
        {
            if (_statusText != null && _statusUntil > 0f && Time.unscaledTime > _statusUntil)
            {
                _statusUntil = 0f;
                _statusText.text = _hint;
            }

            if (IsOpen == false)
            {
                return;
            }

            if (_drag != null && _input.RotatePressed)
            {
                RotateDrag();
            }

            if (_hovered != null && _drag == null && HasModalWidget == false && _tooltip != null)
            {
                if (_hovered.Item == null || Model == null || Model.ContainsItem(_hovered.Item) == false)
                {
                    _hovered = null;
                    _tooltip.Hide();
                }
                else if (Time.unscaledTime - _hoverStart >= _tooltipDelay)
                {
                    Vector2 pointer = UnityEngine.InputSystem.Pointer.current != null ? UnityEngine.InputSystem.Pointer.current.position.ReadValue() : _hoverPosition;

                    if (_tooltip.IsVisible == false)
                    {
                        _tooltip.Show(_hovered.Item, pointer, _hoverCamera, CollectStats(_hovered.Item), CollectDescription(_hovered.Item));
                        ApplyThemeTo(_tooltip);
                    }
                    else
                    {
                        _tooltip.Follow(pointer, _hoverCamera);
                    }
                }
            }
        }

        private void OnModelChanged(InventoryChange change)
        {
            UpdateWeight();

            if (_drag != null && Model.ContainsItem(_drag.Item) == false)
            {
                CancelDrag();
            }
        }

        private void UpdateWeight()
        {
            if (_weightText == null || Model == null)
            {
                return;
            }

            float weight = Model.GetOwnerWeight(_weightOwnerId);
            bool overweight = _maxCarryWeight > 0f && weight > _maxCarryWeight;
            Color weightColor = overweight ? _overweightColor : _theme != null ? _theme.TextColor : InventoryUIFactory.TextColor;
            string format = _maxCarryWeight > 0f ? _weightFormat : _weightFormatUnlimited;
            _weightText.text = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, InventoryUIFactory.FormatWeight(weight), InventoryUIFactory.FormatWeight(_maxCarryWeight), ColorUtility.ToHtmlStringRGB(weightColor));
            _weightText.color = format.Contains("{2}") ? Color.white : weightColor;
        }

        private void EndDrag()
        {
            InventoryDragState drag = _drag;
            _drag = null;

            if (drag.Ghost != null)
            {
                InventoryUIFactory.Destroy(drag.Ghost.gameObject);
            }

            if (drag.SourceView != null)
            {
                drag.SourceView.SetDragSource(false);
            }

            foreach (InventoryGridViewBase grid in _grids)
            {
                if (grid != null)
                {
                    grid.ClearHighlights();
                }
            }
        }

        private void ShowCompatibleSlots(ItemInstance item)
        {
            foreach (InventoryGridViewBase grid in _grids)
            {
                if (grid is not InventorySlotGridView slotGrid || slotGrid.Container is not SlottedContainer container)
                {
                    continue;
                }

                _compatibleAreas.Clear();

                foreach (SlotDefinition slot in container.Slots)
                {
                    ItemInstance occupant = container.GetSlotOccupant(slot.Id);

                    if (slot.Rule.AcceptsDefinition(item.Definition) && container.IsSlotAvailable(slot, item) && (slot.Mode == SlotMode.Grid || occupant == null || occupant == item))
                    {
                        _compatibleAreas.Add(slot.Bounds);
                    }
                }

                grid.SetCompatibleAreas(_compatibleAreas);
            }
        }

        private InventoryGridViewBase FindGridAt(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;

            if (eventSystem == null)
            {
                return null;
            }

            var pointer = new PointerEventData(eventSystem) { position = screenPosition };
            _raycastResults.Clear();
            eventSystem.RaycastAll(pointer, _raycastResults);

            foreach (RaycastResult result in _raycastResults)
            {
                if (result.gameObject == null)
                {
                    continue;
                }

                InventoryGridViewBase grid = result.gameObject.GetComponentInParent<InventoryGridViewBase>();

                if (grid != null && _grids.Contains(grid))
                {
                    return grid;
                }

                if (result.gameObject.GetComponentInParent<InventoryWindow>() == this)
                {
                    return null;
                }
            }

            return null;
        }

        private void Evaluate(InventoryGridViewBase target, Vector2 screenPosition, Camera eventCamera, out Vector2Int origin, out RectInt area, out InventoryDropKind kind, out InventoryFailure failure)
        {
            InventoryContainer container = target.Container;
            ItemInstance item = _drag.Item;
            bool slotTarget = container is SlottedContainer;
            _drag.AutoPlace = false;
            origin = default;
            area = default;
            failure = InventoryFailure.None;

            if (target == _drag.SourceGrid && slotTarget == false)
            {
                kind = InventoryDropKind.None;
                return;
            }

            Vector2 pointer = target.ScreenToTopLeftLocal(screenPosition, eventCamera);
            Vector2Int size = item.GetSize(_drag.Rotated);
            origin = target.GetPlacementOrigin(pointer, size);
            area = new RectInt(origin, size);

            if (container.TryResolvePlacement(item, origin, _drag.Rotated, out ItemPlacement placement, out failure))
            {
                area = placement.Area;
            }

            InventoryResult result = _drag.IsSplit ? Model.CanSplit(item.InstanceId, _drag.Amount, container.Id, origin, _drag.Rotated) : Model.CanMove(item.InstanceId, container.Id, origin, _drag.Rotated);

            if (result.Success)
            {
                failure = InventoryFailure.None;
                ItemInstance occupant = container.GetItemAt(origin);

                if (result.Item != null && result.Item != item && result.Item.CanStackWith(item))
                {
                    kind = InventoryDropKind.Merge;
                    area = container.GetItemArea(result.Item);
                }
                else if (occupant != null && occupant != item && occupant.CanStackWith(item))
                {
                    kind = InventoryDropKind.Merge;
                    area = container.GetItemArea(occupant);
                }
                else
                {
                    kind = InventoryDropKind.Valid;
                }

                return;
            }

            if (slotTarget && _drag.IsSplit == false && CanSwapIntoSlot(item, (SlottedContainer)container, origin))
            {
                kind = InventoryDropKind.Valid;
                failure = InventoryFailure.None;
                return;
            }

            if (slotTarget == false && _drag.IsSplit == false && TryFindAutoPlacement(item, container, out RectInt autoArea, out bool merge))
            {
                _drag.AutoPlace = true;
                area = autoArea;
                kind = merge ? InventoryDropKind.Merge : InventoryDropKind.Valid;
                failure = InventoryFailure.None;
                return;
            }

            kind = InventoryDropKind.Invalid;
            failure = result.Failure;
            ClipToContainer(ref area, container);
        }

        private static bool TryFindAutoPlacement(ItemInstance item, InventoryContainer container, out RectInt area, out bool merge)
        {
            foreach (ItemInstance existing in container.Items)
            {
                if (existing != item && existing.CanStackWith(item) && existing.FreeStackSpace > 0)
                {
                    area = container.GetItemArea(existing);
                    merge = true;
                    return true;
                }
            }

            merge = false;

            if (container.TryFindSpace(item, true, out ItemPlacement placement))
            {
                area = placement.Area;
                return true;
            }

            area = default;
            return false;
        }

        private bool TryGateDrag(InventoryDragState drag, InventoryContainer target)
        {
            if (EquipmentGate == null || drag.IsSplit || drag.Item == null)
            {
                return false;
            }

            bool fromEquipment = drag.Item.ContainerId == _equipmentContainerId;
            bool toEquipment = target.Id == _equipmentContainerId;

            if (toEquipment && fromEquipment == false && target is SlottedContainer slotted)
            {
                SlotDefinition slot = slotted.GetSlotAt(drag.TargetOrigin);
                return slot != null && slot.Mode == SlotMode.Single && slot.Rule.AcceptsDefinition(drag.Item.Definition) && EquipmentGate.HandleEquip(drag.Item, slot);
            }

            return fromEquipment && toEquipment == false && EquipmentGate.HandleUnequip(drag.Item, new InventoryUnequipTarget(target.Id, drag.TargetOrigin, drag.Rotated, drag.AutoPlace));
        }

        private bool CanSwapIntoSlot(ItemInstance item, SlottedContainer container, Vector2Int cell)
        {
            SlotDefinition slot = container.GetSlotAt(cell);

            if (slot == null || slot.Mode != SlotMode.Single || slot.Rule.AcceptsDefinition(item.Definition) == false)
            {
                return false;
            }

            ItemInstance occupant = container.GetSlotOccupant(slot.Id);
            return occupant != null && occupant != item && container.IsSlotAvailable(slot, occupant) && Model.GetContainer(_primaryContainerId) != null;
        }

        private InventoryResult TrySwapIntoSlot(ItemInstance item, SlottedContainer container, SlotDefinition slot)
        {
            ItemInstance occupant = container.GetSlotOccupant(slot.Id);
            bool sameContainer = item.ContainerId == container.Id;
            Vector2Int previousPosition = item.Location.Position;
            InventoryResult parked = Model.TryTransfer(occupant.InstanceId, _primaryContainerId, int.MaxValue, false);

            if (parked.Success == false)
            {
                return parked;
            }

            InventoryResult moved = Model.TryMove(item.InstanceId, container.Id, slot.Bounds.position, false, false);

            if (moved.Success == false)
            {
                Model.TryMove(occupant.InstanceId, container.Id, slot.Bounds.position, false, false);
                return moved;
            }

            if (sameContainer)
            {
                Model.TryMove(occupant.InstanceId, container.Id, previousPosition, false, false);
            }

            return moved;
        }

        private IEnumerable<SlotDefinition> EquipCandidates(ItemInstance item, SlottedContainer equipment)
        {
            var yielded = new HashSet<string>(StringComparer.Ordinal);

            if (item.Definition.TryGetComponent(out EquippableItemComponent equippable))
            {
                foreach (string slotId in equippable.PreferredSlotIds)
                {
                    SlotDefinition slot = equipment.GetSlot(slotId);

                    if (slot != null && slot.Mode == SlotMode.Single && yielded.Add(slot.Id))
                    {
                        yield return slot;
                    }
                }
            }

            foreach (SlotDefinition slot in equipment.Slots)
            {
                if (slot.Mode == SlotMode.Single && slot.Rule.AcceptsDefinition(item.Definition) && yielded.Add(slot.Id))
                {
                    yield return slot;
                }
            }
        }

        private static void ClipToContainer(ref RectInt area, InventoryContainer container)
        {
            int xMin = Mathf.Clamp(area.xMin, 0, container.Width);
            int yMin = Mathf.Clamp(area.yMin, 0, container.Height);
            int xMax = Mathf.Clamp(area.xMax, 0, container.Width);
            int yMax = Mathf.Clamp(area.yMax, 0, container.Height);
            area = new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
        }
    }
}
