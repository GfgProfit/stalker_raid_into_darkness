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

        private readonly List<InventoryContextMenuEntry> _menuEntries = new();
        private readonly InventoryItemHover _itemHover = new();

        private IDisposable _modelBinding;
        private IDisposable _commandBinding;
        private InventoryWindowViewModel _commands;
        private InventoryDragInteraction _dragInteraction;
        private InventoryWeaponAttachments _itemDropHandler;
        private float _statusUntil;
        private string _hint = string.Empty;
        private bool _initialized;
        private AudioSource _audio;
        private float _lastSoundTime = float.MinValue;

        public InventoryModel Model { get; private set; }
        public IItemIconProvider IconProvider { get; set; }
        public InventoryActionRegistry Actions { get; private set; }
        public InventoryInputBindings Input => _input;
        public bool IsOpen { get; private set; }
        public bool IsDragging => _dragInteraction?.State != null;
        public InventoryDragState Drag => _dragInteraction?.State;
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
        public Action<ItemInstance, List<InventoryItemStat>> ItemStatsProvider
        {
            get => _itemHover.StatsProvider;
            set => _itemHover.StatsProvider = value;
        }

        public Func<ItemInstance, string> ItemDescriptionProvider
        {
            get => _itemHover.DescriptionProvider;
            set => _itemHover.DescriptionProvider = value;
        }
        public Func<ItemInstance, string> ItemShortNameProvider { get; set; }
        public Func<ItemInstance, float?> ItemConditionProvider { get; set; }
        public Func<InventoryActionContext, IInventoryItemAction, InventoryActionStyle> ActionStyleProvider { get; set; }
        public ItemInstance HoveredItem => _itemHover.Item;

        public InventoryWeaponAttachments ItemDropHandler
        {
            get => _itemDropHandler;
            set
            {
                _itemDropHandler = value;
                if (_dragInteraction != null)
                {
                    _dragInteraction.Attachments = value;
                }
            }
        }

        private bool CanPointAtItem => IsOpen && isActiveAndEnabled && IsDragging == false && HasModalWidget == false;

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
            _itemHover.Configure(_tooltip, _tooltipDelay, _theme);
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
            _itemHover.Configure(_tooltip, _tooltipDelay, _theme);
        }

        public string Label(string key, string fallback) => _theme != null ? _theme.Label(key, fallback) : fallback;

        public string FormatLabel(string key, string fallback, params object[] arguments) => string.Format(System.Globalization.CultureInfo.InvariantCulture, Label(key, fallback), arguments);

        public void SetSecondaryVisible(bool visible)
        {
            if (_secondaryRoot == null || _secondaryRoot.gameObject.activeSelf == visible)
            {
                return;
            }

            _dragInteraction?.Suspend();
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
            IEquipmentAnimationRequests equipment = _commands?.Equipment;
            Func<ItemInstance, InventoryResult> worldDrop = _commands?.WorldDrop;
            Unbind();
            Model = model;
            IconProvider = iconProvider;
            _commands = new InventoryWindowViewModel(model, _equipmentContainerId, _primaryContainerId)
            {
                Equipment = equipment,
                WorldDrop = worldDrop
            };
            InventoryWindowViewModel commands = _commands;
            _commandBinding = commands.Completed.Subscribe(ReportCommand);
            _dragInteraction = new InventoryDragInteraction(model, _grids, transform, _dragLayer, _dropItemArea, _secondaryRoot, _primaryContainerId, _equipmentContainerId, () => commands.CanDropInWorld, item => commands.Drop(item, true))
            {
                Attachments = _itemDropHandler,
                Equipment = equipment
            };
            _dragInteraction.Starting += HideTransientWidgets;

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

            _itemHover.Bind(model);
            _modelBinding = Observable.FromEvent<InventoryChange>(handler => model.Changed += handler, handler => model.Changed -= handler)
                .Subscribe(_ => UpdateWeight());
            UpdateWeight();
        }

        public void Unbind()
        {
            _dragInteraction?.Dispose();
            _dragInteraction = null;
            _itemHover.Unbind();
            HideTransientWidgets();

            _commandBinding?.Dispose();
            _commandBinding = null;
            _commands?.Dispose();
            _commands = null;
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

            IsOpen = false;
            _dragInteraction?.Suspend();
            HideTransientWidgets();

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
            _itemHover.Clear();
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
            if (CanPointAtItem == false || _itemHover.Enter(view, eventData) == false)
            {
                return;
            }

            if (_input.TransferHeld && CanHoverTransfer(view.Item))
            {
                TransferHoveredItem();
            }
        }

        public void HandleItemPointerExit(InventoryItemView view, PointerEventData eventData) => _itemHover.Exit(view);

        internal void HandleItemViewDetached(InventoryItemView view)
        {
            _dragInteraction?.ResetSource(view);
            _itemHover.Exit(view);
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
            if (IsOpen && isActiveAndEnabled)
            {
                _dragInteraction?.BeginPointer(view, eventData, _theme, IconProvider);
            }
        }

        public void HandleDrag(PointerEventData eventData) => UpdateDrag(eventData.position, eventData.pressEventCamera);

        internal void HandleItemDrag(InventoryItemView view, PointerEventData eventData)
        {
            if (_dragInteraction?.MatchesPointer(view, eventData.pointerId) == true)
            {
                HandleDrag(eventData);
            }
        }

        public void HandleEndDrag(PointerEventData eventData)
        {
            if (_dragInteraction?.TrackPointer(eventData.position, eventData.pressEventCamera) == true)
            {
                CompleteDrag();
            }
        }

        internal void HandleItemEndDrag(InventoryItemView view, PointerEventData eventData)
        {
            if (_dragInteraction?.MatchesPointer(view, eventData.pointerId) == true)
            {
                HandleEndDrag(eventData);
            }
        }

        public bool BeginDrag(ItemInstance item, InventoryGridViewBase sourceGrid, int amount, Vector2 screenPosition, Camera eventCamera)
        {
            return IsOpen && isActiveAndEnabled && _dragInteraction?.Begin(item, sourceGrid, amount, screenPosition, eventCamera, _theme, IconProvider, null) == true;
        }

        public void UpdateDrag(Vector2 screenPosition, Camera eventCamera) => _dragInteraction?.Update(screenPosition, eventCamera);

        public void RotateDrag() => _dragInteraction?.Rotate();

        public InventoryResult CompleteDrag()
        {
            if (_dragInteraction == null)
            {
                return InventoryResult.Fail(InventoryFailure.InvalidItem);
            }

            var completed = _dragInteraction.Complete();
            if (completed.AttachmentSource != null)
            {
                ReportAttachmentResult(completed.Result, completed.AttachmentSource);
            }
            else if (completed.ReportFailure)
            {
                ShowStatus(DescribeFailure(completed.Result.Failure));
            }
            else if (completed.Sound is InventorySound sound)
            {
                PlaySound(sound);
            }

            return completed.Result;
        }

        public void CancelDrag() => _dragInteraction?.Cancel();

        public void OpenContextMenu(ItemInstance item, InventoryGridViewBase grid, Vector2 screenPosition, Camera eventCamera)
        {
            if (_contextMenu == null || item == null)
            {
                return;
            }

            _itemHover.Clear();
            var context = new InventoryActionContext(this, item, grid);
            Actions.BuildMenu(context, ActionStyleProvider, _menuEntries);
            _contextMenu.Show(screenPosition, eventCamera, _menuEntries);
            ApplyThemeTo(_contextMenu);

            if (_menuEntries.Count > 0)
            {
                PlaySound(InventorySound.ContextMenu);
            }
        }

        public bool CanUseItem(ItemInstance item) => InventoryItemOperations.CanUse(item);

        public void UseItem(ItemInstance item) => _commands?.Use(item);

        public bool IsEquipped(ItemInstance item) => Model != null && EquipmentInventoryCommands.IsEquipped(Model, item, _equipmentContainerId);

        public bool HasEquipmentSlotFor(ItemInstance item) => Model?.HasEquipmentSlotFor(item, _equipmentContainerId) == true;

        public bool CanEquipItem(ItemInstance item) => Model?.CanEquipItem(item, _equipmentContainerId, _primaryContainerId) == true;

        public void EquipItem(ItemInstance item) => _commands?.Equip(item, null, true);

        public void EquipItemNow(ItemInstance item, SlotDefinition slot) => _commands?.Equip(item, slot, false);

        public void UnequipItem(ItemInstance item)
        {
            InventoryUnequipTarget target = InventoryUnequipTarget.Auto(_primaryContainerId);

            _commands?.Unequip(item, target, true, true);
        }

        public InventoryResult UnequipItemNow(ItemInstance item, InventoryUnequipTarget target) => UnequipItemNow(item, target, true);

        public InventoryResult UnequipItemNow(ItemInstance item, InventoryUnequipTarget target, bool playSound)
        {
            return _commands?.Unequip(item, target, false, playSound) ?? InventoryResult.Fail(InventoryFailure.ItemNotFound);
        }

        public void OpenSplitDialog(ItemInstance item)
        {
            if (_splitDialog == null || _commands?.CanSplit(item) != true)
            {
                return;
            }

            _itemHover.Clear();
            _splitDialog.Show(Model, item, amount => _commands?.Split(item, amount));
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

        public void QuickTransfer(ItemInstance item) => _commands?.Transfer(item, GetQuickTransferTarget(item));

        public void InspectItem(ItemInstance item)
        {
            if (_inspectPanel == null)
            {
                return;
            }

            _itemHover.Clear();
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

        public void DropItemNow(ItemInstance item) => _commands?.Drop(item, false);

        private InventoryResult TryDropItem(ItemInstance item) => _commands?.Drop(item, true) ?? InventoryResult.Fail(InventoryFailure.ItemNotFound);

        internal void BindEquipmentRequests(IEquipmentAnimationRequests equipment)
        {
            if (_commands != null)
            {
                _commands.Equipment = equipment;
            }

            if (_dragInteraction != null)
            {
                _dragInteraction.Equipment = equipment;
            }
        }

        internal void BindWorldDrop(Func<ItemInstance, InventoryResult> drop)
        {
            if (_commands != null)
            {
                _commands.WorldDrop = drop;
            }
        }

        private void ReportCommand((InventoryWindowCommand Kind, ItemInstance Item, InventoryResult Result, bool PlaySound, int Amount, InventoryContainer Target) command)
        {
            switch (command.Kind)
            {
                case InventoryWindowCommand.Use:
                    if (Report(command.Result, FormatLabel("status_used", "Used {0}", command.Item.Definition.DisplayName)))
                    {
                        PlaySound(InventorySound.Use);
                        ItemUsed?.Invoke(command.Item);
                    }
                    break;
                case InventoryWindowCommand.Equip:
                    ReportEquippedItem(command.Item, command.Result);
                    break;
                case InventoryWindowCommand.Unequip:
                    ReportUnequippedItem(command.Item, command.Result, command.PlaySound);
                    break;
                case InventoryWindowCommand.Split:
                    Report(command.Result, FormatLabel("status_split", "Split {0} x {1}", command.Amount, command.Item.Definition.DisplayName));
                    break;
                case InventoryWindowCommand.Transfer:
                    if (Report(command.Result, FormatLabel("status_moved", "Moved to {0}", command.Target.DisplayName)) && command.PlaySound)
                    {
                        PlaySound(InventorySound.ToBag);
                    }
                    break;
                case InventoryWindowCommand.Drop:
                    ReportDroppedItem(command.Item, command.Result);
                    break;
            }
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

        public IReadOnlyList<InventoryItemStat> CollectStats(ItemInstance item) => _itemHover.CollectStats(item);

        public string CollectDescription(ItemInstance item) => _itemHover.CollectDescription(item);

        public bool ShowTooltip(string title, string description, Vector2 screenPosition, Camera eventCamera)
        {
            return CanPointAtItem && _itemHover.ShowExternal(title, description, screenPosition, eventCamera);
        }

        public void HideTooltip() => _itemHover.HideExternal();

        internal void MoveExternalTooltip(Vector2 screenPosition, Camera eventCamera) => _itemHover.FollowExternal(screenPosition, eventCamera);

        public void RefreshTooltip()
        {
            if (CanPointAtItem)
            {
                _itemHover.Refresh();
            }
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

            _itemHover.Configure(_tooltip, _tooltipDelay, _theme);

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

        private void OnDisable()
        {
            _dragInteraction?.Suspend();
            _itemHover.Clear();
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

            _dragInteraction?.Tick();
            if (IsDragging && _input.RotatePressed)
            {
                RotateDrag();
            }

            if (CanPointAtItem && _input.TransferPressed)
            {
                TransferHoveredItem();
            }
        }

        private void LateUpdate() => _itemHover.Tick(CanPointAtItem);

        private void TransferHoveredItem()
        {
            ItemInstance item = _itemHover.Item;
            if (CanHoverTransfer(item))
            {
                _itemHover.Clear();
                QuickTransfer(item);
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
    }
}
