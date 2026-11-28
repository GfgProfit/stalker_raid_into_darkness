using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RaidIntoDarkness.Inventory;
using RaidIntoDarkness.Inventory.IconGeneration;
using RaidIntoDarkness.Inventory.UI;
using R3;
using RaidIntoDarkness.DI;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Settings;
using RaidIntoDarkness.UI;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Inventory
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class InventoryWindowController : MonoBehaviour
    {
        public const string STATUS_HEALTH = "health";
        public const string STATUS_POWER = "power";
        public const string STATUS_HUNGER = "hunger";
        public const string STATUS_THIRST = "thirst";
        public const string STATUS_SLEEP = "sleep";
        public const string FAVORITE_ACTION = "favorite";
        public const string JUNK_ACTION = "junk";
        public const string OUTFIT_PATCH_ACTION = "outfit_patch";
        public const string UNLOAD_WEAPON_ACTION = "unload_weapon";

        private static readonly Color _compatibleAmmoTint = new(0f, 1f, 0.35f, 0.22f);

        [SerializeField] private InventoryWindow _window;
        [SerializeField] private PlayerInventoryRuntime _inventory;
        [SerializeField] private StalkerPlayerController _player;
        [SerializeField] private SpawnMenuView _spawnMenu;
        [SerializeField] private InputActionReference _toggleInventory;
        [SerializeField] private InputActionReference _closeInventory;
        [SerializeField] private InputActionReference _toggleStash;
        [SerializeField] private InventoryCharacterHeader _header;
        [SerializeField] private InventoryStatusPanel _status;
        [SerializeField] private string _characterName = "Сталкер";
        [SerializeField] private string _community = "Одиночки";
        [SerializeField] private long _money = 5000;
        [SerializeField] private Sprite _portrait;
        [SerializeField] private Sprite _rank;
        [SerializeField] private InputActionReference _toggleFavorite;
        [SerializeField] private InputActionReference _toggleJunk;
        [SerializeField] private Sprite _favoriteBadge;
        [SerializeField] private Sprite _junkBadge;
        [SerializeField] private bool _prewarmIcons = true;
        [SerializeField] private InputActionReference _fullStatsModifier;

        [Inject] private IGameSettings _gameSettings;
        private InventorySortingPlus _sorting;
        private InventoryBackpackLayoutViewModel _backpackLayout;
        private IDisposable _backpackRowsBinding;
        private EquipmentStatsFormatter _statsFormatter;
        private IDisposable _descriptionBinding;
        private IDisposable _conditionBinding;
        private EquipmentStatusPresenter _statusPresenter;
        private EquipmentStatusDetailsFormatter _statusDetails;
        private InventoryStatusPanel _hoverStatusPanel;
        private FirstPersonWeaponController _weapons;
        private InventoryWeaponAmmoSource _weaponAmmo;
        private InventoryWeaponAttachments _weaponAttachments;
        private InventoryAttachmentActions _attachmentActions;
        private EquipmentAnimationQueue _equipmentAnimations;
        private InventoryItemSounds _itemSounds;
        private ItemInstance _statsHovered;
        private ItemInstance _compatibilityHovered;
        private string _compatibilityLauncherId;
        private bool _compatibilityDirty;
        private int _restoreFrame = -1;
        private bool _gameplayBlocked;
        private bool _bound;
        private bool _showItemDescriptions = GameSettingsStore.DEFAULT_SHOW_ITEM_DESCRIPTIONS;
        private bool _fullCondition;
        private bool _statsDirty = true;
        private string _statusHovered;
        private Vector2 _statusHoverPosition;
        private Camera _statusHoverCamera;
        private float _statusTooltipValue;
        private float _statusTooltipMaximum;
        private bool _statusTooltipDirty;

        public InventoryWindow Window => _window;
        public PlayerInventoryRuntime Inventory => _inventory;
        public StalkerPlayerController Player => _player;
        public bool IsOpen => _window != null && _window.IsOpen;
        public bool GameplayBlocked => _gameplayBlocked;
        public bool IsStashVisible => IsOpen && _window.SecondaryVisible;
        public InventorySortingPlus Sorting => _sorting;
        public EquipmentAnimationQueue EquipmentAnimations => _equipmentAnimations;
        public InventoryItemSounds ItemSounds => _itemSounds;
        public InventoryWeaponAttachments WeaponAttachments => _weaponAttachments;

        public void Configure(InventoryWindow window, PlayerInventoryRuntime inventory, StalkerPlayerController player, InputActionReference toggleInventory, InputActionReference closeInventory, SpawnMenuView spawnMenu = null)
        {
            if (_bound && (_window != window || _inventory != inventory || _player != player))
            {
                Close();
                RestoreGameplay();
                Unbind();
            }
            _window = window;
            _inventory = inventory;
            _player = player;
            _spawnMenu = spawnMenu;
            _toggleInventory = toggleInventory;
            _closeInventory = closeInventory;
        }

        public void ConfigurePresentation(InputActionReference toggleStash, InventoryCharacterHeader header, InventoryStatusPanel status, string characterName, string community, long money, Sprite portrait, Sprite rank)
        {
            _toggleStash = toggleStash;
            _header = header;
            _status = status;
            _characterName = characterName;
            _community = community;
            _money = money;
            _portrait = portrait;
            _rank = rank;
            if (isActiveAndEnabled)
            {
                BindStatusHover();
            }
        }

        public void ConfigureSorting(InputActionReference toggleFavorite, InputActionReference toggleJunk, Sprite favoriteBadge, Sprite junkBadge)
        {
            _toggleFavorite = toggleFavorite;
            _toggleJunk = toggleJunk;
            _favoriteBadge = favoriteBadge;
            _junkBadge = junkBadge;
        }

        public void ConfigureStats(InputActionReference fullStatsModifier)
        {
            _fullStatsModifier = fullStatsModifier;
        }

        public void Open() => Open(false);

        public void Open(bool showStash)
        {
            if (_window == null || _player != null && _player.SimulationPaused)
            {
                return;
            }

            _window.SetSecondaryVisible(showStash);
            if (EnsureBound() == false)
            {
                return;
            }

            if (_sorting != null)
            {
                _sorting.ArrangeAll();
            }

            if (IsOpen)
            {
                return;
            }

            RefreshHeader();
            _window.Open();
            _restoreFrame = -1;
            BlockGameplay();
        }

        public void Close()
        {
            if (_window == null || IsOpen == false)
            {
                return;
            }

            _window.Close();
            _restoreFrame = Time.frameCount;
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

        private void OnEnable()
        {
            BindStatusHover();
            if (_bound && IsOpen)
            {
                BindBackpackLayout();
                BindConditionStatus();
            }

            Enable(_toggleInventory);
            Enable(_closeInventory);
            Enable(_toggleStash);
            Enable(_toggleFavorite);
            Enable(_toggleJunk);
            Enable(_fullStatsModifier);
        }

        private void Start()
        {
            if (EnsureBound() && _spawnMenu != null)
            {
                _spawnMenu.Bind(_inventory, _window, _statsFormatter, _fullStatsModifier, _gameSettings);
            }

            if (_window != null)
            {
                _window.Close();
            }

            if (_prewarmIcons && _inventory != null)
            {
                PrewarmAsync(_inventory.CollectDefinitionsInUse()).Forget();
            }
        }

        private void Update()
        {
            if (_player != null && _player.SimulationPaused)
            {
                return;
            }
            if (_restoreFrame >= 0 && Time.frameCount > _restoreFrame)
            {
                RestoreGameplay();
            }

            if (_window == null)
            {
                return;
            }

            if (WasPressed(_toggleInventory))
            {
                Toggle();
            }
            else if (WasPressed(_toggleStash))
            {
                ToggleStash();
            }
            else if (IsOpen && WasPressed(_closeInventory) && _window.CloseTopWidget() == false)
            {
                Close();
            }
            else if (IsOpen && WasPressed(_toggleFavorite))
            {
                ToggleMark(_window.HoveredItem, false);
            }
            else if (IsOpen && WasPressed(_toggleJunk))
            {
                ToggleMark(_window.HoveredItem, true);
            }

            UpdateCompatibilityHighlight();

            if (IsOpen)
            {
                UpdateStats();
                RefreshStatusTooltip();
            }

            if (IsOpen && _gameplayBlocked && Cursor.lockState != CursorLockMode.None)
            {
                ReleaseCursor();
            }
        }

        public void ToggleStash()
        {
            if (IsStashVisible)
            {
                Close();
            }
            else
            {
                Open(true);
            }
        }

        public void RefreshPresentation()
        {
            RefreshHeader();
            RefreshConditionStatus();
        }

        private void RefreshHeader()
        {
            if (_header != null)
            {
                _header.SetCharacter(_characterName, _community, _portrait, _rank);
                _header.SetMoney(_money, Utils.FormatNumber(_money, EquipmentStatsFormatter.NUMBER_SEPARATOR));
            }

            _statsDirty = true;
        }

        private void RefreshConditionStatus()
        {
            StalkerActorCondition condition = GetCondition();
            if (_status == null || condition == null)
            {
                return;
            }

            SetConditionStatus(InventoryConditionStatusViewModel.Project(StalkerStatKind.Health, condition.ReadStat(StalkerStatKind.Health)));
            SetConditionStatus(InventoryConditionStatusViewModel.Project(StalkerStatKind.Stamina, condition.ReadStat(StalkerStatKind.Stamina)));
            SetConditionStatus(InventoryConditionStatusViewModel.Project(StalkerStatKind.Hunger, condition.ReadStat(StalkerStatKind.Hunger)));
            SetConditionStatus(InventoryConditionStatusViewModel.Project(StalkerStatKind.Thirst, condition.ReadStat(StalkerStatKind.Thirst)));
            SetConditionStatus(InventoryConditionStatusViewModel.Project(StalkerStatKind.Sleep, condition.ReadStat(StalkerStatKind.Sleep)));
        }

        private StalkerActorCondition GetCondition() => _player != null && _player.Motor != null ? _player.Motor.Condition : null;

        private void BindConditionStatus()
        {
            StalkerActorCondition condition = GetCondition();
            if (_conditionBinding != null || _status == null || condition == null || isActiveAndEnabled == false)
            {
                return;
            }

            var changes = Observable.FromEvent<StalkerStatKind>(handler => condition.StatChanged += handler, handler => condition.StatChanged -= handler);
            var viewModel = new InventoryConditionStatusViewModel(changes, condition.ReadStat);
            _conditionBinding = viewModel.Values.Subscribe(SetConditionStatus);
            RefreshConditionStatus();
        }

        private void UnbindConditionStatus()
        {
            _conditionBinding?.Dispose();
            _conditionBinding = null;
        }

        private void SetConditionStatus((string Id, float Current, float Maximum) value)
        {
            if (value.Id == null || _status == null)
            {
                return;
            }

            _status.SetValue(value.Id, value.Current, value.Maximum);
            if (_statusHovered == value.Id)
            {
                _statusTooltipDirty = true;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && IsOpen)
            {
                ReleaseCursor();
            }
        }

        private void OnDisable()
        {
            UnbindBackpackLayout();
            UnbindConditionStatus();
            UnbindStatusHover();
            if (_gameplayBlocked)
            {
                if (_window != null)
                {
                    _window.Close();
                }

                RestoreGameplay();
            }
        }

        private void OnDestroy()
        {
            UnbindStatusHover();
            RestoreGameplay();
            Unbind();
        }

        private void Unbind()
        {
            if (_bound == false)
            {
                return;
            }

            _bound = false;
            UnbindBackpackLayout();
            UnbindConditionStatus();
            _descriptionBinding?.Dispose();
            _descriptionBinding = null;
            if (_sorting != null)
            {
                _sorting.Changed -= OnSortingChanged;
                _sorting = null;
            }

            if (_inventory != null)
            {
                _inventory.Model.Changed -= OnModelChanged;
                _inventory.ForcedDropCompleted -= OnForcedDropCompleted;
            }

            if (_weapons != null && _window != null)
            {
                _weapons.FireModeChanged -= _window.RefreshTooltip;
            }

            if (_equipmentAnimations != null)
            {
                _equipmentAnimations.ItemUseChanged -= OnEquipmentItemUseChanged;
                _equipmentAnimations.OperationCompleted -= OnEquipmentOperationCompleted;
            }

            _attachmentActions?.Dispose();
            _itemSounds?.Dispose();
            if (_window != null)
            {
                _window.Opened -= OnInventoryOpened;
                _window.Closed -= OnInventoryClosed;
                _window.ItemConditionProvider = null;
                _window.ItemStatsProvider = null;
                _window.ItemDescriptionProvider = null;
                _window.ItemShortNameProvider = null;
                _window.ItemDecorator = null;
                _window.ActionStyleProvider = null;
                _window.EquipmentGate = null;
                _window.Actions.Unregister(FAVORITE_ACTION);
                _window.Actions.Unregister(JUNK_ACTION);
                _window.Actions.Unregister(UNLOAD_WEAPON_ACTION);
                _window.Actions.Unregister(OUTFIT_PATCH_ACTION);
                _window.Unbind();
            }

            _attachmentActions = null;
            _weaponAmmo = null;
            _weaponAttachments = null;
            _weapons = null;
            _equipmentAnimations = null;
            _itemSounds = null;
            _statsFormatter = null;
            _statusPresenter = null;
            _statusDetails = null;
            _statsHovered = null;
            _compatibilityHovered = null;
            _compatibilityLauncherId = null;
            _compatibilityDirty = false;
        }

        private bool EnsureBound()
        {
            if (_bound)
            {
                return true;
            }

            if (_window == null || _inventory == null || _inventory.EnsureInitialized() == false)
            {
                return false;
            }

            _bound = true;
            _window.ItemConditionProvider = GetItemCondition;
            if (_window.FindGrid(_window.PrimaryContainerId) is InventoryGridView backpackGrid)
            {
                InventoryBackpackViewport.Ensure(backpackGrid);
            }

            _window.Bind(_inventory.Model, new ItemIconCacheProvider());
            _window.Opened += OnInventoryOpened;
            _window.Closed += OnInventoryClosed;
            if (_window.IsOpen)
            {
                BindBackpackLayout();
                BindConditionStatus();
            }

            BindSorting();
            _weapons = _inventory.Weapons;
            _weaponAmmo = _inventory.WeaponAmmo;
            _weaponAttachments = _inventory.WeaponAttachments;
            _equipmentAnimations = _inventory.EquipmentAnimations;
            _statsFormatter = new EquipmentStatsFormatter(_window, weaponAmmo: _weaponAmmo, weapons: _weapons);
            _window.ItemStatsProvider = _statsFormatter.Collect;
            BindItemDescriptions();
            _window.ItemShortNameProvider = FormatItemShortName;
            _statusPresenter = _status != null ? new EquipmentStatusPresenter(_window, _status) : null;
            _statusDetails = new EquipmentStatusDetailsFormatter(_window);
            _inventory.Model.Changed += OnModelChanged;
            _inventory.ForcedDropCompleted += OnForcedDropCompleted;
            _itemSounds = new InventoryItemSounds(_window);
            _attachmentActions = new InventoryAttachmentActions(_window, _weaponAttachments);
            _window.Actions.Register(new InventoryDelegateAction(UNLOAD_WEAPON_ACTION, 24, context => _window.Label(UNLOAD_WEAPON_ACTION, "Разрядить"), UnloadWeapon, context => _weaponAmmo.HasLoadedAmmo(context.Item), CanUnloadWeapon));

            if (_weapons != null)
            {
                _weapons.FireModeChanged += _window.RefreshTooltip;
            }

            if (_equipmentAnimations != null)
            {
                _window.EquipmentGate = _equipmentAnimations;
                _equipmentAnimations.ItemUseChanged += OnEquipmentItemUseChanged;
                _equipmentAnimations.OperationCompleted += OnEquipmentOperationCompleted;
                _window.Actions.Register(new InventoryDelegateAction(OUTFIT_PATCH_ACTION, 12, context => _window.Label("outfit_patch_remove", "Снять нашивку"), RemoveOutfitPatch, context => _equipmentAnimations.HasPatchMotion(context.Item), context => _equipmentAnimations.IsInUse(context.Item) == false));
            }

            UpdateMaxCarryWeight();
            _window.RefreshItems();
            return true;
        }

        private void OnEquipmentItemUseChanged(ItemInstance item)
        {
            if (_window != null)
            {
                _window.RefreshItem(item);
            }
        }

        private void OnForcedDropCompleted(ItemInstance item, InventoryResult result)
        {
            if (_window != null)
            {
                _window.ReportDroppedItem(item, result);
            }
        }

        private void OnEquipmentOperationCompleted(EquipmentAnimationRequestKind operation, ItemInstance item, InventoryResult result, bool playSound)
        {
            if (_window == null)
            {
                return;
            }

            switch (operation)
            {
                case EquipmentAnimationRequestKind.Equip:
                    _window.ReportEquippedItem(item, result);
                    break;
                case EquipmentAnimationRequestKind.Unequip:
                    _window.ReportUnequippedItem(item, result, playSound);
                    break;
                case EquipmentAnimationRequestKind.Drop:
                    _window.ReportDroppedItem(item, result);
                    break;
                case EquipmentAnimationRequestKind.Patch:
                    if (result.Success == false)
                    {
                        _window.ShowStatus(_window.DescribeFailure(result.Failure));
                    }
                    break;
            }
        }

        private void BindBackpackLayout()
        {
            if (isActiveAndEnabled && _backpackLayout == null && _window.FindGrid(_window.PrimaryContainerId) is InventoryGridView grid && grid.Container is GridContainer { RowGrowth: > 0 } backpack)
            {
                _backpackLayout = new InventoryBackpackLayoutViewModel(backpack);
                _backpackRowsBinding = _backpackLayout.Rows.Subscribe(grid.SetContentRows);
            }
        }

        private void UnbindBackpackLayout()
        {
            _backpackRowsBinding?.Dispose();
            _backpackLayout?.Dispose();
            _backpackRowsBinding = null;
            _backpackLayout = null;
        }

        private void OnInventoryOpened(InventoryWindow window)
        {
            BindBackpackLayout();
            BindConditionStatus();
        }

        private void OnInventoryClosed(InventoryWindow window)
        {
            UnbindBackpackLayout();
            UnbindConditionStatus();
        }

        private void BindItemDescriptions()
        {
            _window.ItemDescriptionProvider = item => _statsFormatter.Describe(item, _showItemDescriptions);
            _descriptionBinding = _gameSettings?.ShowItemDescriptions.Subscribe(SetItemDescriptions);
        }

        private void SetItemDescriptions(bool value)
        {
            _showItemDescriptions = value;
            _window.RefreshTooltip();
        }

        private bool CanUnloadWeapon(InventoryActionContext context) => _inventory.WeaponBinder?.IsReloading(context.Item) != true && _weaponAmmo.Unload(context.Item, false).Success;

        private void UnloadWeapon(InventoryActionContext context)
        {
            if (_inventory.WeaponBinder?.IsReloading(context.Item) == true)
            {
                return;
            }

            InventoryResult result = _weaponAmmo.Unload(context.Item);
            if (!result.Success)
            {
                _window.ShowStatus(_window.DescribeFailure(result.Failure));
            }
        }

        private void BindSorting()
        {
            _sorting = new InventorySortingPlus(_inventory.Model, new[] { _window.PrimaryContainerId, _window.SecondaryContainerId }, _favoriteBadge, _junkBadge);
            _sorting.Changed += OnSortingChanged;
            _window.ItemDecorator = DecorateItem;
            _window.ActionStyleProvider = new ContextMenuOverhaulStyle(_window, _sorting).Resolve;
            _window.Actions.Unregister(InventoryBuiltInActions.INSPECT);
            _window.Actions.Register(new InventoryDelegateAction(FAVORITE_ACTION, 45, GetFavoriteLabel, context => _sorting.ToggleFavorite(context.Item.Definition), context => _sorting.IsSorted(context.Item.ContainerId)));
            _window.Actions.Register(new InventoryDelegateAction(JUNK_ACTION, 46, GetJunkLabel, context => _sorting.ToggleJunk(context.Item.Definition), context => _sorting.IsSorted(context.Item.ContainerId)));
        }

        private void RemoveOutfitPatch(InventoryActionContext context)
        {
            if (_equipmentAnimations.TryQueuePatch(context.Item))
            {
                Close();
            }
        }

        private string GetFavoriteLabel(InventoryActionContext context)
        {
            return _sorting.IsFavorite(context.Item.Definition) ? context.Window.Label("favorite_remove", "Unmark favorite") : context.Window.Label("favorite_add", "Mark as favorite");
        }

        private string GetJunkLabel(InventoryActionContext context)
        {
            return _sorting.IsJunk(context.Item.Definition) ? context.Window.Label("junk_remove", "Unmark junk") : context.Window.Label("junk_add", "Mark as junk");
        }

        private void ToggleMark(ItemInstance item, bool junk)
        {
            if (_sorting == null || item == null || _sorting.IsSorted(item.ContainerId) == false)
            {
                return;
            }

            if (junk)
            {
                _sorting.ToggleJunk(item.Definition);
            }
            else
            {
                _sorting.ToggleFavorite(item.Definition);
            }
        }

        private string FormatItemShortName(ItemInstance item)
        {
            if (item != null && item.ContainerId == _window.EquipmentContainerId)
            {
                switch (item.Location.SlotId)
                {
                    case SlotId.Knife:
                    case SlotId.Binocular:
                    case SlotId.Detector:
                    case SlotId.Device:
                    case SlotId.Pda:
                        return string.Empty;
                    case SlotId.Head:
                    case SlotId.Outfit:
                    case SlotId.Backpack:
                        return item.Definition.ShortName;
                }
            }

            return FormatAmmoShortName(item?.Definition);
        }

        internal static string FormatAmmoShortName(ItemDefinition definition)
        {
            if (definition == null || definition.GetComponent<AmmoStatsComponent>() == null || string.IsNullOrEmpty(definition.ShortName))
            {
                return null;
            }

            return "<size=16><color=#FFFFFF>" + definition.ShortName + "</color></size>";
        }

        private static float? GetItemCondition(ItemInstance item)
        {
            return EquipmentStatsFormatter.SupportsCondition(item?.Definition) ? EquipmentStatsFormatter.GetCondition(item) : null;
        }

        private InventoryItemDecoration DecorateItem(ItemInstance item)
        {
            InventoryItemDecoration decoration = _sorting != null ? _sorting.Decorate(item) : InventoryItemDecoration.None;
            if (!EquipmentAnimationQueue.IsPatchRemoved(item))
            {
                decoration = AddCommunityIcon(item?.Definition, decoration);
            }

            if (_equipmentAnimations?.IsInUse(item) == true)
            {
                return new InventoryItemDecoration(EquipmentAnimationQueue.IN_USE_TINT, decoration.Badge, decoration.CommunityIcon, decoration.CommunityIconSize);
            }

            if (_compatibilityHovered == null || item == null || item == _compatibilityHovered)
            {
                return decoration;
            }

            ItemInstance weaponItem = _compatibilityHovered;
            ItemDefinition ammoDefinition = item.Definition;
            WeaponStatsComponent weapon = weaponItem.Definition.GetComponent<WeaponStatsComponent>();
            AmmoStatsComponent ammo = ammoDefinition.GetComponent<AmmoStatsComponent>();

            if (weapon == null || ammo == null)
            {
                weaponItem = item;
                ammoDefinition = _compatibilityHovered.Definition;
                weapon = weaponItem.Definition.GetComponent<WeaponStatsComponent>();
                ammo = ammoDefinition.GetComponent<AmmoStatsComponent>();
            }

            bool compatible = weapon != null && ammo != null && (weapon.SupportsAmmo(ammo.Section) || weapon.GetGrenadeLauncher(weaponItem)?.GetComponent<WeaponAddonComponent>()?.SupportsGrenadeAmmo(ammoDefinition) == true);
            compatible |= _weaponAttachments != null && (_weaponAttachments.Handles(item, _compatibilityHovered) || _weaponAttachments.Handles(_compatibilityHovered, item));
            return compatible ? new InventoryItemDecoration(_compatibleAmmoTint, decoration.Badge, decoration.CommunityIcon, decoration.CommunityIconSize) : decoration;
        }

        internal static InventoryItemDecoration AddCommunityIcon(ItemDefinition definition, InventoryItemDecoration decoration)
        {
            EquipmentStatsComponent equipment = definition != null ? definition.GetComponent<EquipmentStatsComponent>() : null;
            return equipment != null ? new InventoryItemDecoration(decoration.Tint, decoration.Badge, equipment.CommunityIcon, equipment.CommunityIconSize) : decoration;
        }

        private void UpdateCompatibilityHighlight()
        {
            ItemInstance hovered = IsOpen && _window.IsDragging == false && _window.HasModalWidget == false ? _window.HoveredItem : null;

            if (_compatibilityHovered == hovered && _compatibilityDirty == false)
            {
                return;
            }

            _compatibilityDirty = false;
            string launcherId = hovered?.GetMountedWeaponAttachmentDefinition(WeaponAddonKind.GrenadeLauncher)?.DefinitionId;
            if (_compatibilityHovered == hovered && _compatibilityLauncherId == launcherId)
            {
                return;
            }

            _compatibilityHovered = hovered;
            _compatibilityLauncherId = launcherId;
            _window.RefreshItems();
        }

        private void UpdateStats()
        {
            bool fullCondition = IsHeld(_fullStatsModifier);
            ItemInstance hovered = _window.HoveredItem;

            if (fullCondition == _fullCondition && hovered == _statsHovered && _statsDirty == false)
            {
                return;
            }

            bool refreshTooltip = fullCondition != _fullCondition || _statsDirty;
            _fullCondition = fullCondition;
            _statsHovered = hovered;
            _statsDirty = false;
            _statusTooltipDirty = true;

            if (_statsFormatter != null)
            {
                _statsFormatter.FullCondition = fullCondition;
            }

            if (_statusPresenter != null)
            {
                _statusPresenter.Refresh(hovered, fullCondition);
            }

            if (refreshTooltip)
            {
                _window.RefreshTooltip();
            }
        }

        private void BindStatusHover()
        {
            if (_hoverStatusPanel == _status)
            {
                return;
            }
            UnbindStatusHover();
            _hoverStatusPanel = _status;
            if (_hoverStatusPanel != null)
            {
                _hoverStatusPanel.PointerEntered += OnStatusPointerEnter;
                _hoverStatusPanel.PointerMoved += OnStatusPointerMove;
                _hoverStatusPanel.PointerExited += OnStatusPointerExit;
            }
        }

        private void UnbindStatusHover()
        {
            if (_hoverStatusPanel != null)
            {
                _hoverStatusPanel.PointerEntered -= OnStatusPointerEnter;
                _hoverStatusPanel.PointerMoved -= OnStatusPointerMove;
                _hoverStatusPanel.PointerExited -= OnStatusPointerExit;
            }
            _hoverStatusPanel = null;
            OnStatusPointerExit();
        }

        private void OnStatusPointerEnter(string id, Vector2 position, Camera eventCamera)
        {
            _statusHovered = id;
            _statusHoverPosition = position;
            _statusHoverCamera = eventCamera;
            _statusTooltipDirty = true;
            RefreshStatusTooltip();
        }

        private void OnStatusPointerMove(Vector2 position, Camera eventCamera)
        {
            _statusHoverPosition = position;
            _statusHoverCamera = eventCamera;
            if (string.IsNullOrEmpty(_statusHovered) == false && _window != null && _window.Tooltip != null)
            {
                _window.Tooltip.Follow(position, eventCamera);
            }
        }

        private void OnStatusPointerExit()
        {
            _statusHovered = null;
            _statusTooltipDirty = false;
            if (_window != null)
            {
                _window.HideTooltip();
            }
        }

        private void RefreshStatusTooltip()
        {
            if (string.IsNullOrEmpty(_statusHovered) || _window == null || _status == null)
            {
                return;
            }
            if (IsOpen == false || _window.IsDragging || _window.HasModalWidget)
            {
                OnStatusPointerExit();
                return;
            }
            float value = _status.GetValue(_statusHovered);
            float maximum = _status.GetMaximum(_statusHovered);
            if (_statusTooltipDirty == false && value == _statusTooltipValue && maximum == _statusTooltipMaximum)
            {
                return;
            }
            _statusDetails ??= new EquipmentStatusDetailsFormatter(_window);
            StalkerActorCondition condition = _player != null && _player.Motor != null ? _player.Motor.Condition : null;
            if (_statusDetails.TryDescribe(_statusHovered, _fullCondition, out string title, out string description) || _statusDetails.TryDescribeCondition(_statusHovered, condition, out title, out description))
            {
                _window.ShowTooltip(title, description, _statusHoverPosition, _statusHoverCamera);
            }
            _statusTooltipValue = value;
            _statusTooltipMaximum = maximum;
            _statusTooltipDirty = false;
        }

        private void OnModelChanged(InventoryChange change)
        {
            _statsDirty = true;
            if (change.Kind == InventoryChangeKind.ItemStateChanged && change.Item == _compatibilityHovered)
            {
                _compatibilityDirty = true;
            }

            UpdateMaxCarryWeight();
        }

        private void UpdateMaxCarryWeight()
        {
            if (_player != null && _player.Motor != null && _player.Motor.Condition != null && _player.Profile != null)
            {
                _window.SetMaxCarryWeight(_player.Motor.Condition.MaxCarryWeight);
            }
        }

        private void OnSortingChanged()
        {
            if (_window != null)
            {
                _window.RefreshItems();
            }
        }

        private void BlockGameplay()
        {
            _gameplayBlocked = true;

            if (_player != null)
            {
                _player.GameplayInputBlocked = true;
            }

            ReleaseCursor();
        }

        private void RestoreGameplay()
        {
            _restoreFrame = -1;

            if (_gameplayBlocked == false)
            {
                return;
            }

            _gameplayBlocked = false;

            if (_player != null)
            {
                _player.GameplayInputBlocked = false;

                if (_player.CameraController != null)
                {
                    _player.CameraController.SetCursorLocked(true);
                }
            }
        }

        private void ReleaseCursor()
        {
            if (_player != null && _player.CameraController != null)
            {
                _player.CameraController.SetCursorLocked(false);
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private async UniTaskVoid PrewarmAsync(IReadOnlyList<ItemDefinition> definitions)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, destroyCancellationToken);
            ItemIconGeneratorSettings settings = ItemIconGeneratorSettings.LoadDefault();

            try
            {
                await ItemIconCache.PrewarmAsync(definitions, settings.PrewarmSlotProfiles, null, destroyCancellationToken);
            }
            catch (System.OperationCanceledException)
            {
            }
        }

        private static void Enable(InputActionReference reference)
        {
            if (reference != null && reference.action != null && reference.action.enabled == false)
            {
                reference.action.Enable();
            }
        }

        private static bool WasPressed(InputActionReference reference) => reference != null && reference.action != null && reference.action.WasPressedThisFrame();

        private static bool IsHeld(InputActionReference reference) => reference != null && reference.action != null && reference.action.IsPressed();
    }
}
