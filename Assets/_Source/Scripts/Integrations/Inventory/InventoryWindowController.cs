using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Inventory;
using Inventory.IconGeneration;
using Inventory.UI;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Integrations.Inventory
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

        private static readonly Color _compatibleAmmoTint = new(0f, 1f, 0.35f, 0.22f);

        [SerializeField] private InventoryWindow _window;
        [SerializeField] private PlayerInventoryRuntime _inventory;
        [SerializeField] private StalkerPlayerController _player;
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

        private InventorySortingPlus _sorting;
        private EquipmentStatsFormatter _statsFormatter;
        private EquipmentStatusPresenter _statusPresenter;
        private EquipmentStatusDetailsFormatter _statusDetails;
        private InventoryStatusPanel _hoverStatusPanel;
        private FirstPersonOutfitBinder _outfitBinder;
        private FirstPersonWeaponBinder _weaponBinder;
        private InventoryWeaponAttachments _weaponAttachments;
        private OutfitSlotRules _outfitSlotRules;
        private EquipmentAnimationQueue _equipmentAnimations;
        private InventoryItemSounds _itemSounds;
        private ItemInstance _statsHovered;
        private ItemInstance _compatibilityHovered;
        private int _restoreFrame = -1;
        private bool _gameplayBlocked;
        private bool _bound;
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

        public void Configure(InventoryWindow window, PlayerInventoryRuntime inventory, StalkerPlayerController player, InputActionReference toggleInventory, InputActionReference closeInventory)
        {
            if (_inventory != null && _inventory != inventory)
            {
                _inventory.BindDamageReceiver(null);
            }
            _window = window;
            _inventory = inventory;
            _player = player;
            _toggleInventory = toggleInventory;
            _closeInventory = closeInventory;
            BindDamageReceiver();
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
            EnsureBound();

            if (_sorting != null)
            {
                _sorting.ArrangeAll();
            }

            if (IsOpen)
            {
                return;
            }

            RefreshPresentation();
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
            Enable(_toggleInventory);
            Enable(_closeInventory);
            Enable(_toggleStash);
            Enable(_toggleFavorite);
            Enable(_toggleJunk);
            Enable(_fullStatsModifier);
        }

        private void Start()
        {
            BindDamageReceiver();
            EnsureBound();

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

            if (_equipmentAnimations != null)
            {
                _equipmentAnimations.Tick(Time.deltaTime);
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
                RefreshConditionStatus();
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
            if (_header != null)
            {
                _header.SetCharacter(_characterName, _community, _portrait, _rank);
                _header.SetMoney(_money, Utils.FormatNumber(_money, EquipmentStatsFormatter.NUMBER_SEPARATOR));
            }

            _statsDirty = true;

            if (_status == null)
            {
                return;
            }

            RefreshConditionStatus();
        }

        private void RefreshConditionStatus()
        {
            if (_status == null)
            {
                return;
            }
            StalkerActorCondition condition = _player != null && _player.Motor != null ? _player.Motor.Condition : null;
            if (condition == null)
            {
                return;
            }
            SetConditionStatus(STATUS_HEALTH, condition.Health, condition.MaxHealth, condition.HealthCapacity);
            SetConditionStatus(STATUS_POWER, condition.Power, condition.MaxPower, condition.PowerCapacity);
            SetConditionStatus(STATUS_HUNGER, condition.Hunger, condition.MaxHunger, condition.HungerCapacity);
            SetConditionStatus(STATUS_THIRST, condition.Thirst, condition.MaxThirst, condition.ThirstCapacity);
            SetConditionStatus(STATUS_SLEEP, condition.Sleep, condition.MaxSleep, condition.SleepCapacity);
        }

        private void SetConditionStatus(string id, float value, float maximum, float capacity)
        {
            if (float.IsFinite(capacity) && capacity > 0f)
            {
                _status.SetValue(id, value / capacity, maximum / capacity);
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
            if (_gameplayBlocked)
            {
                RestoreGameplay();
            }

            if (_sorting != null)
            {
                _sorting.Changed -= OnSortingChanged;
                _sorting = null;
            }

            if (_bound && _inventory != null)
            {
                _inventory.Model.Changed -= OnModelChanged;
            }

            if (_outfitBinder != null)
            {
                _outfitBinder.Dispose();
                _outfitBinder = null;
            }

            if (_weaponBinder != null)
            {
                _weaponBinder.Dispose();
                _weaponBinder = null;
            }

            _weaponAttachments?.Dispose();

            if (_outfitSlotRules != null)
            {
                _outfitSlotRules.Dispose();
                _outfitSlotRules = null;
            }

            if (_equipmentAnimations != null)
            {
                _equipmentAnimations.Dispose();
                _equipmentAnimations = null;
            }

            if (_itemSounds != null)
            {
                _itemSounds.Dispose();
                _itemSounds = null;
            }
        }

        private void BindDamageReceiver()
        {
            if (_inventory != null)
            {
                _inventory.BindDamageReceiver(_player != null ? _player.GetComponent<AnomalyDamageReceiver>() : null);
            }
        }

        private void EnsureBound()
        {
            if (_bound || _window == null || _inventory == null)
            {
                return;
            }

            _bound = true;
            _window.ItemConditionProvider = GetItemCondition;
            _window.Bind(_inventory.Model, new ItemIconCacheProvider());
            BindBackpackLockSprite();
            _inventory.SeedOnce();
            BindSorting();
            _statsFormatter = new EquipmentStatsFormatter(_window);
            _window.ItemStatsProvider = _statsFormatter.Collect;
            _window.ItemDescriptionProvider = _statsFormatter.Describe;
            _window.ItemShortNameProvider = FormatItemShortName;
            _statusPresenter = _status != null ? new EquipmentStatusPresenter(_window, _status) : null;
            _statusDetails = new EquipmentStatusDetailsFormatter(_window);
            _inventory.Model.Changed += OnModelChanged;

            _itemSounds = new InventoryItemSounds(_window);
            _weaponAttachments = new InventoryWeaponAttachments(_window);
            _outfitSlotRules = new OutfitSlotRules(_window, _inventory);
            _outfitSlotRules.Refresh();

            if (_player != null && _player.Rig != null)
            {
                _outfitBinder = new FirstPersonOutfitBinder(_window, _player);
                _outfitBinder.Refresh();
            }

            FirstPersonWeaponController weapons = _player != null ? _player.GetComponentInChildren<FirstPersonWeaponController>() : null;
            if (weapons != null)
            {
                _weaponBinder = new FirstPersonWeaponBinder(_window, weapons);
                _weaponBinder.Refresh();
            }

            if (weapons != null || (_player != null && _player.HudAnimator != null && _player.HudAnimator.Library != null))
            {
                _equipmentAnimations = new EquipmentAnimationQueue(_window, _player, weapons);
            }

            if (_player != null && _player.Motor != null && _player.Motor.Condition != null)
            {
                _player.Motor.Condition.SetCarryWeightSource(_inventory);
                _player.Motor.Condition.SetSprintSource(_inventory);
            }

            UpdateMaxCarryWeight();
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

        private void BindBackpackLockSprite()
        {
            if (_window.FindGrid(_window.EquipmentContainerId) is InventoryEquipmentPanel panel)
            {
                InventoryEquipmentSlot backpack = panel.GetSlotView(OutfitSlotRules.BACKPACK_SLOT);
                if (backpack != null && backpack.LockedSprite == null)
                {
                    backpack.SetLockedSprite(panel.GetSlotView(OutfitSlotRules.HELMET_SLOT)?.LockedSprite);
                }
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

        private static string FormatItemShortName(ItemInstance item)
        {
            string armorType = item?.Definition.GetComponent<EquipmentStatsComponent>()?.ArmorTypeLabel;

            if (armorType != null)
            {
                return FormatShortNameLabel(armorType);
            }

            if (item?.Definition.GetComponent<AmmoStatsComponent>() == null)
            {
                return null;
            }

            string name = item.Definition.ShortName;
            int unit = name.IndexOf("мм", System.StringComparison.OrdinalIgnoreCase);
            unit = unit >= 0 ? unit : name.IndexOf("mm", System.StringComparison.OrdinalIgnoreCase);
            string label = unit >= 0 ? name.Substring(unit + 2).Trim() : name;

            if (unit < 0 && name.Length > 0 && (char.IsDigit(name[0]) || name[0] == '.'))
            {
                int separator = name.IndexOf(' ');
                label = separator >= 0 ? name.Substring(separator + 1).Trim() : name;
            }

            if (label.EndsWith(" FMJ", System.StringComparison.Ordinal))
            {
                label = "FMJ";
            }

            return FormatShortNameLabel(label);
        }

        private static string FormatShortNameLabel(string label) => "<size=16><color=#FFFFFF>" + label + "</color></size>";

        private static float? GetItemCondition(ItemInstance item)
        {
            return item != null && (item.Definition.GetComponent<EquipmentStatsComponent>() != null || item.Definition.GetComponent<WeaponStatsComponent>() != null) ? EquipmentStatsFormatter.GetCondition(item) : null;
        }

        private InventoryItemDecoration DecorateItem(ItemInstance item)
        {
            InventoryItemDecoration decoration = _sorting != null ? _sorting.Decorate(item) : InventoryItemDecoration.None;
            EquipmentStatsComponent equipment = item?.Definition.GetComponent<EquipmentStatsComponent>();
            decoration = new InventoryItemDecoration(decoration.Tint, decoration.Badge, equipment?.CommunityIcon, equipment != null ? equipment.CommunityIconSize : Vector2.zero);

            if (_compatibilityHovered == null || item == null || item == _compatibilityHovered)
            {
                return decoration;
            }

            WeaponStatsComponent weapon = _compatibilityHovered.Definition.GetComponent<WeaponStatsComponent>();
            AmmoStatsComponent ammo = item.Definition.GetComponent<AmmoStatsComponent>();

            if (weapon == null || ammo == null)
            {
                weapon = item.Definition.GetComponent<WeaponStatsComponent>();
                ammo = _compatibilityHovered.Definition.GetComponent<AmmoStatsComponent>();
            }

            bool compatible = weapon != null && ammo != null && weapon.SupportsAmmo(ammo.Section);
            compatible |= _weaponAttachments != null && (_weaponAttachments.Handles(item, _compatibilityHovered) || _weaponAttachments.Handles(_compatibilityHovered, item));
            return compatible ? new InventoryItemDecoration(_compatibleAmmoTint, decoration.Badge, decoration.CommunityIcon, decoration.CommunityIconSize) : decoration;
        }

        private void UpdateCompatibilityHighlight()
        {
            ItemInstance hovered = IsOpen && _window.IsDragging == false && _window.HasModalWidget == false ? _window.HoveredItem : null;

            if (hovered != null)
            {
                InventoryGridViewBase grid = _window.FindGrid(hovered.ContainerId);
                InventoryItemView view = grid != null ? grid.GetItemView(hovered) : null;

                if (_window.Model.ContainsItem(hovered) == false || view == null || view.gameObject.activeInHierarchy == false)
                {
                    hovered = null;
                }
            }

            if (_compatibilityHovered == hovered)
            {
                return;
            }

            _compatibilityHovered = hovered;
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
