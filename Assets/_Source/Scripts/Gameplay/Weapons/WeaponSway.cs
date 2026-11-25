using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public class WeaponSway : MonoBehaviour
{
    private const float LEAN_RESPONSE = 5.0f;
    private const float LEAN_OVERRIDE_RESPONSE = 9.0f;
    private const float LEAN_SETTLE_THRESHOLD = 0.0001f;
    private const float LEFT_LEAN_ROLL_TANGENT = 0.23981877f;
    private const float RIGHT_LEAN_ROLL_TANGENT = 0.12724514f;

    private static readonly Vector3 LEFT_LEAN_POSITION = new Vector3(-0.07668651f, -0.04234925f, 0.0f);
    private static readonly Vector3 RIGHT_LEAN_POSITION = new Vector3(-0.02837801f, -0.02467653f, 0.0f);

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
    private Vector2 _leanWeights;

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
            _leanWeights = Vector2.zero;
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

        float deltaTime = Time.deltaTime;
        _swayRotation = Quaternion.Slerp(_swayRotation, targetRotation, _smoothTime * deltaTime);
        TickWeaponLean(deltaTime);
        Transform viewer = _cameraController.Camera != null ? _cameraController.Camera.transform : _cameraController.CameraRoot;
        Vector3 pivotOffset = _swayRoot.parent.InverseTransformVector(viewer.forward * _swayOffset);
        ApplyHudPose(pivotOffset);
    }

    private void TickWeaponLean(float deltaTime)
    {
        FirstPersonWeaponController weapon = _player != null && _player.Rig != null ? _player.Rig.HandsOwner as FirstPersonWeaponController : null;
        FirstPersonWeaponAsset asset = weapon != null ? weapon.ActiveWeapon : null;
        if (asset == null || asset.IsMelee || asset.IsHandGrenade || weapon.Model == null)
        {
            _leanWeights = Vector2.zero;
            return;
        }

        bool lowered = weapon.State == FirstPersonWeaponState.Holstering || weapon.CurrentMotion is FirstPersonWeaponMotionKind.SprintStart or FirstPersonWeaponMotionKind.Sprint or FirstPersonWeaponMotionKind.SprintEnd;
        UpdateLean(_player.LeanIntent, weapon.IsAiming || lowered, deltaTime);
    }

    private void UpdateLean(float intent, bool suppressed, float deltaTime)
    {
        Vector2 target = suppressed ? Vector2.zero : new Vector2(Mathf.Max(-intent, 0.0f), Mathf.Max(intent, 0.0f));
        float response = suppressed ? LEAN_OVERRIDE_RESPONSE : LEAN_RESPONSE;
        _leanWeights = Vector2.Lerp(_leanWeights, target, response * deltaTime);
        if ((_leanWeights - target).sqrMagnitude <= LEAN_SETTLE_THRESHOLD * LEAN_SETTLE_THRESHOLD)
        {
            _leanWeights = target;
        }
    }

    private void ApplyHudPose(Vector3 pivotOffset)
    {
        Quaternion rotation = _baseRotation * _swayRotation;
        Quaternion sway = rotation * Quaternion.Inverse(_baseRotation);
        Vector3 position = _basePosition + pivotOffset - sway * pivotOffset;
        if (_leanWeights != Vector2.zero)
        {
            float leftRoll = Mathf.Atan(LEFT_LEAN_ROLL_TANGENT * _leanWeights.x) * Mathf.Rad2Deg;
            float rightRoll = Mathf.Atan(RIGHT_LEAN_ROLL_TANGENT * _leanWeights.y) * Mathf.Rad2Deg;
            Quaternion leftRotation = Quaternion.AngleAxis(leftRoll, Vector3.forward);
            Vector3 offset = LEFT_LEAN_POSITION * _leanWeights.x + leftRotation * (RIGHT_LEAN_POSITION * _leanWeights.y);
            position += rotation * Vector3.Scale(offset, _swayRoot.localScale);
            rotation *= Quaternion.AngleAxis(leftRoll + rightRoll, Vector3.forward);
        }

        _swayRoot.SetLocalPositionAndRotation(position, rotation);
    }
}
