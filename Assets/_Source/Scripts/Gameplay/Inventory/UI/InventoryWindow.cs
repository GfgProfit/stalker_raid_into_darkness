using RaidIntoDarkness.Inventory.Domain;
using RaidIntoDarkness.Inventory.Application;
using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RaidIntoDarkness.Inventory.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Inventory/Inventory Window")]
    public sealed class InventoryWindow : MonoBehaviour
    {
        private const float STATUS_DURATION = 2.5f;
        private const float DROP_AREA_FADE_DURATION = 0.15f;

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
        [SerializeField] private CanvasGroup _dropItemArea;
        [SerializeField] private string _weightFormat = "Weight: {0} / {1} kg";
        [SerializeField] private string _weightFormatUnlimited = "Weight: {0} kg";
        [SerializeField] private Color _overweightColor = new(1f, 0.55f, 0.2f, 1f);

        private readonly List<RaycastResult> _raycastResults = new();
        private readonly List<InventoryItemStat> _tooltipStats = new();
        private readonly List<InventoryContextMenuEntry> _menuEntries = new();
        private readonly List<RectInt> _compatibleAreas = new();
        private InventoryDropAreaViewModel _dropAreaViewModel;
        private IDisposable _dropAreaBinding;
        private IDisposable _modelBinding;
        private float _dropAreaTargetAlpha;
        private InventoryDragState _drag;
        private InventoryItemView _hovered;
        private float _hoverStart;
        private Vector2 _hoverPosition;
        private Camera _hoverCamera;
        private PointerEventData _hoverPointer;
        private EventSystem _hoverEventSystem;
        private float _statusUntil;
        private string _hint = string.Empty;
        private bool _initialized;
        private bool _externalTooltip;
        private AudioSource _audio;
        private float _lastSoundTime = float.MinValue;

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
        public Func<ItemInstance, string> ItemShortNameProvider { get; set; }
        public Func<ItemInstance, float?> ItemConditionProvider { get; set; }
        public Func<InventoryActionContext, IInventoryItemAction, InventoryActionStyle> ActionStyleProvider { get; set; }
        public IEquipmentAnimationRequests EquipmentGate { get; set; }
        public InventoryWeaponAttachments ItemDropHandler { get; set; }
        public Func<ItemInstance, InventoryResult> DropToWorld { get; set; }
        public ItemInstance HoveredItem => _hovered != null ? _hovered.Item : null;

        public event Action<InventoryWindow> Opened;
        public event Action<InventoryWindow> Closed;
        public event Action<ItemInstance> ItemUsed;
        public event Action<ItemInstance> ItemDropped;
        public event Action<ItemInstance> ItemInspected;
        public event Action<InventorySound> SoundPlayed;

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
            ResetDropArea();
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

        internal void RefreshItem(ItemInstance item)
        {
            if (item != null)
            {
                InventoryGridViewBase grid = FindGrid(item.ContainerId);
                grid?.GetItemView(item)?.Refresh();
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

                InventoryContainer container = model.GetContainer(grid.ContainerId);

                if (container == null)
                {
                    Debug.LogError($"Inventory grid '{grid.name}' references unconfigured container '{grid.ContainerId}'.", grid);
                    continue;
                }

                grid.Bind(this, container);
            }

            _modelBinding = Observable.FromEvent<InventoryChange>(handler => model.Changed += handler, handler => model.Changed -= handler)
                .Subscribe(OnModelChanged);
            UpdateWeight();
        }

        public void Unbind()
        {
            CancelDrag();
            ResetDropArea();

            if (_splitDialog != null)
            {
                _splitDialog.Hide();
            }

            _modelBinding?.Dispose();
            _modelBinding = null;

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

            UpdateWeight();
            PlaySound(InventorySound.Open);
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
            ResetDropArea();
            IsOpen = false;

            if (_content != null)
            {
                _content.gameObject.SetActive(false);
            }

            PlaySound(InventorySound.Close);
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
            _externalTooltip = false;
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
            if (IsCurrentItemView(view) == false)
            {
                return;
            }

            if (_hovered != view)
            {
                _tooltip?.Hide();
            }

            _externalTooltip = false;
            _hovered = view;
            _hoverStart = Time.unscaledTime;
            _hoverPosition = eventData.position;
            _hoverCamera = eventData.enterEventCamera;

            if (_drag == null && HasModalWidget == false && _input.TransferHeld && CanHoverTransfer(view.Item))
            {
                _hovered = null;
                _tooltip?.Hide();
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
                _drag.LastScreenPosition = eventData.position;
                _drag.EventCamera = eventData.pressEventCamera;
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
            _drag.Ghost = InventoryItemDragGhost.Create(layer, item, sourceGrid.CellSize, _drag.Rotated, Theme, IconProvider);
            _drag.Ghost.transform.SetAsLastSibling();

            if (_drag.IsSplit == false)
            {
                _drag.SourceView?.SetDragSource(true);
            }

            ShowCompatibleSlots(item);
            _dropAreaViewModel?.SetVisible(_drag.IsSplit == false && CanUseDropArea());
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
            RefreshDragTarget();
        }

        private void RefreshDragTarget()
        {
            Vector2 screenPosition = _drag.LastScreenPosition;
            Camera eventCamera = _drag.EventCamera;
            InventoryGridViewBase previousTarget = _drag.TargetGrid;
            InventoryGridViewBase target = IsOverDropArea(screenPosition, eventCamera) ? null : FindGridAt(screenPosition);

            if (previousTarget != null && previousTarget != target)
            {
                previousTarget.SetPlacementHighlight(default, Color.clear);
            }

            _drag.TargetGrid = target;
            _drag.Kind = InventoryDropKind.None;
            _drag.Failure = InventoryFailure.None;
            _drag.AutoPlace = false;
            _drag.TargetItem = null;

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
            _drag.Ghost.SetRotation(_drag.Rotated, _drag.SourceGrid.CellSize);
            UpdateDrag(_drag.LastScreenPosition, _drag.EventCamera);
        }

        public InventoryResult CompleteDrag()
        {
            if (_drag == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            RefreshDragTarget();
            InventoryDragState drag = _drag;
            bool dropInWorld = IsOverDropArea(drag.LastScreenPosition, drag.EventCamera);
            InventoryResult result = InventoryResult.Fail(InventoryFailure.NoSlot);
            string sourceContainerId = drag.Item != null ? drag.Item.ContainerId : null;
            EndDrag();

            if (dropInWorld)
            {
                return _dropAreaViewModel.Drop(drag.Item);
            }

            if (drag.TargetGrid != null && drag.TargetGrid.Container != null && drag.Kind != InventoryDropKind.None)
            {
                InventoryContainer target = drag.TargetGrid.Container;

                var applied = InventoryDragOperations.Apply(Model, ItemDropHandler, EquipmentGate, drag.Item, target, drag.TargetItem, drag.TargetOrigin, drag.Rotated, drag.IsSplit, drag.Amount, drag.AutoPlace, drag.Kind != InventoryDropKind.Invalid, _equipmentContainerId, _primaryContainerId);
                result = applied.Result;
                if (applied.Attachment)
                {
                    ReportAttachmentResult(result, drag.Item);
                    return result;
                }

                if (applied.Queued)
                {
                    return result;
                }

                if (result.Success == false)
                {
                    ShowStatus(DescribeFailure(result.Failure));
                }
                else if (target.Id == _equipmentContainerId)
                {
                    PlaySound(InventorySound.ToSlot);
                }
                else if (sourceContainerId == _equipmentContainerId)
                {
                    PlaySound(InventorySound.ToBag);
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
            Actions.BuildMenu(context, ActionStyleProvider, _menuEntries);
            _contextMenu.Show(screenPosition, eventCamera, _menuEntries);
            ApplyThemeTo(_contextMenu);

            if (_menuEntries.Count > 0)
            {
                PlaySound(InventorySound.ContextMenu);
            }
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
            PlaySound(InventorySound.Use);
            ItemUsed?.Invoke(item);
        }

        public bool IsEquipped(ItemInstance item) => item != null && item.ContainerId == _equipmentContainerId && Model?.GetContainer(_equipmentContainerId) is SlottedContainer;

        public bool HasEquipmentSlotFor(ItemInstance item) => Model?.HasEquipmentSlotFor(item, _equipmentContainerId) == true;

        public bool CanEquipItem(ItemInstance item) => Model?.CanEquipItem(item, _equipmentContainerId, _primaryContainerId) == true;

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

            InventoryResult result = Model.TryEquipItem(item, _equipmentContainerId, slot?.Id ?? SlotId.None, _primaryContainerId);
            ReportEquippedItem(item, result);
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

        public InventoryResult UnequipItemNow(ItemInstance item, InventoryUnequipTarget target) => UnequipItemNow(item, target, true);

        public InventoryResult UnequipItemNow(ItemInstance item, InventoryUnequipTarget target, bool playSound)
        {
            if (item == null || Model == null || Model.ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            InventoryResult result = Model.TryUnequipItem(item, target.ContainerId, target.Origin, target.Rotated, target.AutoPlace);
            ReportUnequippedItem(item, result, playSound);
            return result;
        }

        public void OpenSplitDialog(ItemInstance item)
        {
            if (_splitDialog == null || Model == null || Model.ContainsItem(item) == false || item.Amount < 2)
            {
                return;
            }

            _splitDialog.Show(Model, item, amount => Report(Model.TrySplitAuto(item.InstanceId, amount), FormatLabel("status_split", "Split {0} x {1}", amount, item.Definition.DisplayName)));
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

            bool equipped = IsEquipped(item);

            if (target != null && Report(Model.TryTransfer(item.InstanceId, target.Id), FormatLabel("status_moved", "Moved to {0}", target.DisplayName)) && equipped)
            {
                PlaySound(InventorySound.ToBag);
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

        public void DropItem(ItemInstance item) => TryDropItem(item);

        public void DropItemNow(ItemInstance item) => TryDropItemNow(item);

        private InventoryResult TryDropItem(ItemInstance item)
        {
            if (Model != null && IsEquipped(item) && EquipmentGate != null && EquipmentGate.HandleDrop(item))
            {
                return InventoryResult.Ok(item, item.Amount);
            }

            return TryDropItemNow(item);
        }

        private InventoryResult TryDropItemNow(ItemInstance item)
        {
            if (item == null || Model == null || Model.ContainsItem(item) == false)
            {
                return InventoryResult.Fail(InventoryFailure.ItemNotFound);
            }

            InventoryResult result = DropToWorld != null ? DropToWorld(item) : Model.TryRemove(item.InstanceId);
            ReportDroppedItem(item, result);
            return result;
        }

        internal void ReportEquippedItem(ItemInstance item, InventoryResult result)
        {
            if (Report(result, FormatLabel("status_equipped", "Equipped {0}", item.Definition.DisplayName)))
            {
                PlaySound(InventorySound.ToSlot);
            }
        }

        internal void ReportUnequippedItem(ItemInstance item, InventoryResult result, bool playSound)
        {
            if (Report(result, FormatLabel("status_unequipped", "Unequipped {0}", item.Definition.DisplayName)) && playSound)
            {
                PlaySound(InventorySound.ToBag);
            }
        }

        internal void ReportDroppedItem(ItemInstance item, InventoryResult result)
        {
            if (Report(result, FormatLabel("status_dropped", "Dropped {0}", item.Definition.DisplayName)))
            {
                PlaySound(InventorySound.Drop);
                ItemDropped?.Invoke(result.Item);
            }
        }

        internal void ReportAttachmentResult(InventoryResult result, ItemInstance source)
        {
            if (result.Success == false)
            {
                ShowStatus(DescribeFailure(result.Failure));
            }
            else if (source != null)
            {
                PlayClip(source.Definition.GetComponent<WeaponAddonComponent>().AttachSound, 1f);
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

        public bool ShowTooltip(string title, string description, Vector2 screenPosition, Camera eventCamera)
        {
            if (IsOpen == false || _tooltip == null || IsDragging || HasModalWidget)
            {
                return false;
            }
            _hovered = null;
            _externalTooltip = true;
            _hoverPosition = screenPosition;
            _hoverCamera = eventCamera;
            _tooltip.Show(title, description, screenPosition, eventCamera);
            ApplyThemeTo(_tooltip);
            return true;
        }

        public void HideTooltip()
        {
            if (_externalTooltip)
            {
                _externalTooltip = false;
                _tooltip?.Hide();
            }
        }

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

        public void PlaySound(InventorySound sound)
        {
            InventorySoundSet sounds = _theme != null ? _theme.Sounds : null;
            AudioClip clip = sounds != null ? sounds.Get(sound) : null;

            if (clip == null)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (now - _lastSoundTime < sounds.MinInterval)
            {
                return;
            }

            _lastSoundTime = now;
            PlayClip(clip, sounds.Volume);
            SoundPlayed?.Invoke(sound);
        }

        public void PlayClip(AudioClip clip, float volume)
        {
            if (clip == null)
            {
                return;
            }

            if (_audio == null)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 0f;
                _audio.ignoreListenerPause = true;
            }

            _audio.PlayOneShot(clip, volume);
        }

        public string DescribeFailure(InventoryFailure failure) => Label("failure_" + failure, InventoryUIFactory.Describe(failure));

        private void ApplyThemeTo(Component widget)
        {
            if (_theme != null && widget != null)
            {
                _theme.ApplyTo(widget.transform, widget is InventoryTooltip == false);
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

        private void OnEnable()
        {
            if (_dropItemArea == null)
            {
                return;
            }

            _dropItemArea.interactable = false;
            _dropItemArea.blocksRaycasts = false;
            ResetDropArea();
            _dropAreaViewModel = new InventoryDropAreaViewModel(TryDropItem);
            _dropAreaBinding = _dropAreaViewModel.Visibility.Subscribe(visible => _dropAreaTargetAlpha = visible ? 1f : 0f);
        }

        private void OnDisable()
        {
            CancelDrag();
            _dropAreaBinding?.Dispose();
            _dropAreaViewModel?.Dispose();
            _dropAreaBinding = null;
            _dropAreaViewModel = null;
            ResetDropArea();
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

            if (_dropItemArea != null && _dropItemArea.alpha != _dropAreaTargetAlpha)
            {
                _dropItemArea.alpha = Mathf.MoveTowards(_dropItemArea.alpha, _dropAreaTargetAlpha, Time.unscaledDeltaTime / DROP_AREA_FADE_DURATION);
            }

            if (_drag != null && _input.RotatePressed)
            {
                RotateDrag();
            }

            if (_hovered != null && _drag == null && HasModalWidget == false && _input.TransferPressed && Model != null && Model.ContainsItem(_hovered.Item) && CanHoverTransfer(_hovered.Item))
            {
                ItemInstance item = _hovered.Item;
                _hovered = null;

                if (_tooltip != null)
                {
                    _tooltip.Hide();
                }

                QuickTransfer(item);
            }
        }

        private void LateUpdate()
        {
            if (IsOpen == false || _drag != null || HasModalWidget || _tooltip == null)
            {
                _externalTooltip = false;
                _tooltip?.Hide();
                return;
            }

            Vector2 pointer = UnityEngine.InputSystem.Pointer.current != null ? UnityEngine.InputSystem.Pointer.current.position.ReadValue() : _hoverPosition;

            if (_externalTooltip)
            {
                _tooltip.Follow(pointer, _hoverCamera);
                return;
            }
            if (_hovered == null)
            {
                _tooltip.Hide();
                return;
            }

            if (IsHoveredItemAt(pointer) == false)
            {
                _hovered = null;
                _tooltip.Hide();
                return;
            }

            if (Time.unscaledTime - _hoverStart < _tooltipDelay)
            {
                return;
            }

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

        private bool IsHoveredItemAt(Vector2 screenPosition)
        {
            if (_hovered == null || _hovered.isActiveAndEnabled == false)
            {
                return false;
            }

            EventSystem eventSystem = EventSystem.current;

            if (eventSystem == null)
            {
                return false;
            }

            if (_hoverPointer == null || _hoverEventSystem != eventSystem)
            {
                _hoverEventSystem = eventSystem;
                _hoverPointer = new PointerEventData(eventSystem);
            }

            _hoverPointer.Reset();
            _hoverPointer.position = screenPosition;
            _raycastResults.Clear();
            eventSystem.RaycastAll(_hoverPointer, _raycastResults);
            return _raycastResults.Count > 0 && _raycastResults[0].gameObject != null && _raycastResults[0].gameObject.GetComponentInParent<InventoryItemView>() == _hovered;
        }

        private bool IsCurrentItemView(InventoryItemView view)
        {
            return view != null && view.isActiveAndEnabled && view.Item != null && Model != null && Model.ContainsItem(view.Item) && view.Grid != null && view.Grid.Container != null && view.Grid.Container.Contains(view.Item);
        }

        private void OnModelChanged(InventoryChange change)
        {
            UpdateWeight();

            InventoryItemView hovered = _hovered;
            if (ReferenceEquals(hovered, null) == false && (change.Kind == InventoryChangeKind.Cleared || change.Item == hovered.Item) && IsCurrentItemView(hovered) == false)
            {
                HandleItemPointerExit(hovered, null);
            }

            if (_drag == null)
            {
                return;
            }

            if (Model.ContainsItem(_drag.Item) == false || _drag.SourceGrid == null || _drag.SourceGrid.Container == null || _drag.SourceGrid.Container.Contains(_drag.Item) == false)
            {
                CancelDrag();
                return;
            }

            InventoryGridViewBase target = _drag.TargetGrid;

            if (change.Item == _drag.Item || change.Affects(_drag.Item.ContainerId) || (target != null && change.Affects(target.ContainerId)))
            {
                RefreshDragTarget();
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
            _dropAreaViewModel?.SetVisible(false);

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

        private bool CanUseDropArea()
        {
            return IsOpen && _dropItemArea != null && _dropItemArea.isActiveAndEnabled && DropToWorld != null && (_secondaryRoot == null || _secondaryRoot.gameObject.activeSelf == false);
        }

        private bool IsOverDropArea(Vector2 screenPosition, Camera eventCamera)
        {
            return _dropAreaViewModel?.IsVisible == true && CanUseDropArea() && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_dropItemArea.transform, screenPosition, eventCamera);
        }

        private void ResetDropArea()
        {
            _dropAreaViewModel?.SetVisible(false);
            _dropAreaTargetAlpha = 0f;
            if (_dropItemArea != null)
            {
                _dropItemArea.alpha = 0f;
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
                    return grid.Container != null && Model.GetContainer(grid.ContainerId) == grid.Container ? grid : null;
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
            origin = default;
            area = default;
            failure = InventoryFailure.None;

            if (target.TryGetCellAtScreen(screenPosition, eventCamera, out Vector2Int targetCell))
            {
                ItemInstance targetItem = container.GetItemAt(targetCell);
                if (InventoryDragOperations.TryPreviewItemTarget(Model, ItemDropHandler, item, targetItem, _drag.IsSplit, _drag.Amount, out kind, out failure))
                {
                    _drag.TargetItem = targetItem;
                    origin = targetItem.Location.Position;
                    area = container.GetItemArea(targetItem);
                    return;
                }
            }

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

            var preview = InventoryDragOperations.PreviewPlacement(Model, item, container, origin, _drag.Rotated, _drag.IsSplit, _drag.Amount, _primaryContainerId, area);
            area = preview.Area;
            kind = preview.Kind;
            failure = preview.Failure;
            _drag.AutoPlace = preview.AutoPlace;
            if (kind == InventoryDropKind.Invalid)
            {
                ClipToContainer(ref area, container);
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
