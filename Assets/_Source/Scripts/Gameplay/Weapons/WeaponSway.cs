using RaidIntoDarkness.Player;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public class WeaponSway : MonoBehaviour
{
    [SerializeField] private float _swayAmount = 4.0f;
    [SerializeField, Range(0f, 45f)] private float _maxSwayX = 2.0f;
    [SerializeField, Range(0f, 45f)] private float _maxSwayY = 1.5f;
    [SerializeField] private float _smoothTime = 10.0f;
    [SerializeField] private float _swayOffset;
    [SerializeField] private StalkerPlayerInput _playerInput;
    [SerializeField] private StalkerCameraController _cameraController;

    private StalkerPlayerController _player;
    private Transform _swayRoot;
    private Vector3 _basePosition;
    private Quaternion _baseRotation;
    private Quaternion _swayRotation = Quaternion.identity;

    private void Awake()
    {
        if (_playerInput == null)
        {
            _playerInput = GetComponentInParent<StalkerPlayerInput>();
        }

        if (_playerInput == null)
        {
            Debug.LogError($"{nameof(WeaponSway)} on '{name}': no {nameof(StalkerPlayerInput)} assigned or found in parents.", this);
            enabled = false;
            return;
        }

        _player = _playerInput.GetComponent<StalkerPlayerController>();
        if (_cameraController == null)
        {
            _cameraController = _player != null ? _player.CameraController : _playerInput.GetComponent<StalkerCameraController>();
        }

        _swayRoot = _cameraController != null ? _cameraController.HudRoot : null;
        if (_swayRoot == null || _cameraController.CameraRoot == null || _swayRoot == _cameraController.CameraRoot || !_swayRoot.IsChildOf(_cameraController.CameraRoot))
        {
            _swayRoot = null;
            Debug.LogError($"{nameof(WeaponSway)} on '{name}': the player camera requires a HUD root below CameraRoot.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (_swayRoot != null)
        {
            _basePosition = _swayRoot.localPosition;
            _baseRotation = _swayRoot.localRotation;
            _swayRotation = Quaternion.identity;
        }
    }

    private void OnDisable()
    {
        if (_swayRoot != null)
        {
            _swayRoot.localPosition = _basePosition;
            _swayRoot.localRotation = _baseRotation;
        }
    }

    private void LateUpdate()
    {
        if (_swayRoot == null)
        {
            return;
        }

        Vector2 mouseDelta = _playerInput != null && _playerInput.isActiveAndEnabled && Cursor.lockState == CursorLockMode.Locked && (_player == null || !_player.GameplayInputBlocked) ? _playerInput.LookDelta : Vector2.zero;
        float maxSwayX = Mathf.Max(0f, _maxSwayX);
        float maxSwayY = Mathf.Max(0f, _maxSwayY);
        var mouseX = Mathf.Clamp(mouseDelta.x * _swayAmount, -maxSwayX, maxSwayX);
        var mouseY = Mathf.Clamp(mouseDelta.y * _swayAmount, -maxSwayY, maxSwayY);

        var rotationX = Quaternion.AngleAxis(mouseY, Vector3.right);
        var rotationY = Quaternion.AngleAxis(mouseX, Vector3.up);

        var targetRotation = rotationX * rotationY;

        _swayRotation = Quaternion.Slerp(_swayRotation, targetRotation, _smoothTime * Time.deltaTime);
        Transform viewer = _cameraController.Camera != null ? _cameraController.Camera.transform : _cameraController.CameraRoot;
        Vector3 pivotOffset = _swayRoot.parent.InverseTransformVector(viewer.forward * _swayOffset);
        Quaternion rotation = _baseRotation * _swayRotation * Quaternion.Inverse(_baseRotation);
        _swayRoot.localPosition = _basePosition + pivotOffset - rotation * pivotOffset;
        _swayRoot.localRotation = _baseRotation * _swayRotation;
    }
}
