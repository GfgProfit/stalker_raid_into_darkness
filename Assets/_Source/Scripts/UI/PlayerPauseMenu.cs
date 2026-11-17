using Cysharp.Threading.Tasks;
using RaidIntoDarkness.GraphicsOptions;
using RaidIntoDarkness.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RaidIntoDarkness.UI
{
    [DisallowMultipleComponent]
    public sealed class PlayerPauseMenu : MonoBehaviour
    {
        private const string PAUSE_ACTION = "Player/ReleaseCursor";
        private const string MAIN_MENU_SCENE = "Assets/MainMenu.unity";

        [SerializeField] private StalkerPlayerController _player;
        [SerializeField] private StalkerPlayerInput _input;
        [SerializeField] private GameObject _pauseMenu;
        [SerializeField] private GraphicsMenu _graphics;
        [SerializeField] private Button _continue;
        [SerializeField] private Button _save;
        [SerializeField] private Button _load;
        [SerializeField] private Button _stopGame;
        [SerializeField] private Button _quit;

        private InputAction _pauseAction;
        private GameObject _ownedEventSystem;
        private float _previousTimeScale;
        private bool _previousAudioPause;
        private bool _restoreCursorLock;
        private bool _returningToMainMenu;

        public bool IsPaused { get; private set; }

        private void OnEnable()
        {
            _pauseAction = _input.Actions?.FindAction(PAUSE_ACTION);
            if (_pauseAction != null)
            {
                _pauseAction.performed += OnPauseInput;
            }
            _continue.onClick.AddListener(Resume);
            _stopGame.onClick.AddListener(ReturnToMainMenu);
            _quit.onClick.AddListener(Application.Quit);
            _save.interactable = false;
            _load.interactable = false;
            _stopGame.interactable = SceneLoader.CanLoad(MAIN_MENU_SCENE);
        }

        private void OnDisable()
        {
            if (_pauseAction != null)
            {
                _pauseAction.performed -= OnPauseInput;
                _pauseAction = null;
            }
            _continue.onClick.RemoveListener(Resume);
            _stopGame.onClick.RemoveListener(ReturnToMainMenu);
            _quit.onClick.RemoveListener(Application.Quit);
            EndPause(!_returningToMainMenu);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused && IsPaused && _player.CameraController != null)
            {
                _player.CameraController.SetCursorLocked(false);
            }
        }

        private void OnPauseInput(InputAction.CallbackContext context)
        {
            if (_returningToMainMenu)
            {
                return;
            }
            if (IsPaused)
            {
                if (!_graphics.HandleCancel())
                {
                    Resume();
                }
                return;
            }
            if (!_player.GameplayInputBlocked)
            {
                BeginPause();
            }
        }

        private void BeginPause()
        {
            _previousTimeScale = Time.timeScale;
            _previousAudioPause = AudioListener.pause;
            var camera = _player.CameraController;
            _restoreCursorLock = camera != null && !camera.CursorReleased;
            _player.SetSimulationPaused(true);
            IsPaused = true;
            Time.timeScale = 0.0f;
            AudioListener.pause = true;
            if (camera != null)
            {
                camera.SetCursorLocked(false);
            }
            EnsureEventSystem();
            _pauseMenu.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(_continue.gameObject);
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            if (_ownedEventSystem == null)
            {
                _ownedEventSystem = new GameObject("Pause EventSystem");
                _ownedEventSystem.transform.SetParent(transform, false);
                _ownedEventSystem.AddComponent<EventSystem>();
                _ownedEventSystem.AddComponent<InputSystemUIInputModule>();
            }
            else
            {
                _ownedEventSystem.SetActive(true);
            }
        }

        private void Resume()
        {
            if (!_returningToMainMenu)
            {
                EndPause(true);
            }
        }

        private void ReturnToMainMenu()
        {
            ReturnToMainMenuAsync().Forget();
        }

        private async UniTask ReturnToMainMenuAsync()
        {
            if (!isActiveAndEnabled || !IsPaused || _returningToMainMenu || SceneLoader.IsLoading)
            {
                return;
            }
            if (!_pauseMenu.TryGetComponent(out CanvasGroup controls))
            {
                controls = _pauseMenu.AddComponent<CanvasGroup>();
            }
            bool wasInteractable = controls.interactable;
            _returningToMainMenu = true;
            controls.interactable = false;
            try
            {
                await SceneLoader.LoadAsync(MAIN_MENU_SCENE, destroyCancellationToken);
            }
            finally
            {
                _returningToMainMenu = false;
                if (controls != null)
                {
                    controls.interactable = wasInteractable;
                }
            }
        }

        private void EndPause(bool restoreCursor)
        {
            if (!IsPaused)
            {
                return;
            }
            IsPaused = false;
            if (_graphics != null)
            {
                _graphics.CloseForOwner();
            }
            if (_pauseMenu != null)
            {
                _pauseMenu.SetActive(false);
            }
            if (_ownedEventSystem != null)
            {
                _ownedEventSystem.SetActive(false);
            }
            Time.timeScale = _previousTimeScale;
            AudioListener.pause = _previousAudioPause;
            if (_player == null)
            {
                return;
            }
            _player.SetSimulationPaused(false);
            var camera = _player.CameraController;
            if (camera != null)
            {
                camera.SetCursorLocked(restoreCursor && _restoreCursorLock);
            }
        }
    }
}
