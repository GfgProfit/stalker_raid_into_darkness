using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Inventory;
using Inventory.IconGeneration;
using Inventory.UI;
using RaidIntoDarkness.Player;
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

        private InventorySortingPlus _sorting;
        private int _restoreFrame = -1;
        private bool _gameplayBlocked;
        private bool _bound;

        public InventoryWindow Window => _window;
        public PlayerInventoryRuntime Inventory => _inventory;
        public StalkerPlayerController Player => _player;
        public bool IsOpen => _window != null && _window.IsOpen;
        public bool GameplayBlocked => _gameplayBlocked;
        public bool IsStashVisible => IsOpen && _window.SecondaryVisible;
        public InventorySortingPlus Sorting => _sorting;

        public void Configure(InventoryWindow window, PlayerInventoryRuntime inventory, StalkerPlayerController player, InputActionReference toggleInventory, InputActionReference closeInventory)
        {
            _window = window;
            _inventory = inventory;
            _player = player;
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
        }

        public void ConfigureSorting(InputActionReference toggleFavorite, InputActionReference toggleJunk, Sprite favoriteBadge, Sprite junkBadge)
        {
            _toggleFavorite = toggleFavorite;
            _toggleJunk = toggleJunk;
            _favoriteBadge = favoriteBadge;
            _junkBadge = junkBadge;
        }

        public void Open() => Open(false);

        public void Open(bool showStash)
        {
            if (_window == null)
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
            Enable(_toggleInventory);
            Enable(_closeInventory);
            Enable(_toggleStash);
            Enable(_toggleFavorite);
            Enable(_toggleJunk);
        }

        private void Start()
        {
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
                _header.SetMoney(_money);
            }

            if (_status == null)
            {
                return;
            }

            _status.SetValue(STATUS_HEALTH, 1f);
            _status.SetValue(STATUS_POWER, 1f);
            _status.SetValue(STATUS_HUNGER, 1f);
            _status.SetValue(STATUS_THIRST, 1f);
            _status.SetValue(STATUS_SLEEP, 1f);
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
            if (_gameplayBlocked)
            {
                RestoreGameplay();
            }

            if (_sorting != null)
            {
                _sorting.Changed -= OnSortingChanged;
                _sorting = null;
            }
        }

        private void EnsureBound()
        {
            if (_bound || _window == null || _inventory == null)
            {
                return;
            }

            _bound = true;
            _window.Bind(_inventory.Model, new ItemIconCacheProvider());
            _inventory.SeedOnce();
            BindSorting();
        }

        private void BindSorting()
        {
            _sorting = new InventorySortingPlus(_inventory.Model, new[] { _window.PrimaryContainerId, _window.SecondaryContainerId }, _favoriteBadge, _junkBadge);
            _sorting.Changed += OnSortingChanged;
            _window.ItemDecorator = _sorting.Decorate;
            _window.Actions.Register(new InventoryDelegateAction(FAVORITE_ACTION, 45, GetFavoriteLabel, context => _sorting.ToggleFavorite(context.Item.Definition), context => _sorting.IsSorted(context.Item.ContainerId)));
            _window.Actions.Register(new InventoryDelegateAction(JUNK_ACTION, 46, GetJunkLabel, context => _sorting.ToggleJunk(context.Item.Definition), context => _sorting.IsSorted(context.Item.ContainerId)));
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
    }
}
