using RaidIntoDarkness.Player;
using UnityEngine;
using UnityEngine.Rendering;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Weapons
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(250)]
    public sealed class WeaponWorldCrosshair : MonoBehaviour
    {
        private const int HIT_BUFFER_SIZE = 64;
        private static readonly int COLOR = Shader.PropertyToID("_CrosshairColor");
        private static readonly int DIMENSIONS = Shader.PropertyToID("_CrosshairDimensions");

        private readonly RaycastHit[] _hits = new RaycastHit[HIT_BUFFER_SIZE];

        private FirstPersonWeaponController _controller;
        private StalkerPlayerController _player;
        private GameConfig _gameConfig;
        private WeaponCrosshairSettings _settings;
        private Mesh _mesh;
        private MaterialPropertyBlock _properties;
        private float _opacity;
        private float _gap;

        public Vector3 TargetPosition { get; private set; }

        public void Initialize(FirstPersonWeaponController controller, StalkerPlayerController player)
        {
            _controller = controller;
            _player = player;
            _gameConfig = GameConfig.Load();
            _settings = _gameConfig != null ? _gameConfig.WeaponCrosshair : null;
            if (_settings == null || _settings.Enabled && _settings.Material == null)
            {
                Debug.LogWarning("WeaponWorldCrosshair requires GameConfig.WeaponCrosshair and its world crosshair material.", this);
            }
        }

        private void LateUpdate()
        {
            _settings = _gameConfig != null ? _gameConfig.WeaponCrosshair : null;
            if (_controller == null || !_controller.isActiveAndEnabled || _player == null || _settings == null || !_settings.Enabled || _controller.ActiveWeapon == null || !_controller.TryGetMuzzleRay(out Ray ray))
            {
                _opacity = 0f;
                return;
            }

            Camera viewer = _player.CameraController != null ? _player.CameraController.Camera : null;
            if (viewer == null || !viewer.isActiveAndEnabled || viewer.pixelHeight <= 0 || _settings.Material == null)
            {
                return;
            }

            float targetOpacity = ShouldHide(_controller.State, _controller.IsAiming, _controller.AimFactor, _player.State) ? 0f : 1f;
            _opacity = Mathf.MoveTowards(_opacity, targetOpacity, Time.deltaTime / _settings.FadeDurationSeconds);
            if (_opacity <= 0f)
            {
                return;
            }

            float distance = Mathf.Min(_settings.MaximumDistanceMeters, viewer.farClipPlane * 0.95f);
            float shotDistance = _controller.ShotMaximumDistance;
            if (float.IsFinite(shotDistance) && shotDistance > 0f)
            {
                distance = Mathf.Min(distance, shotDistance);
            }

            WeaponBallisticsSettings ballistics = _controller.ActiveWeapon.Ballistics;
            int mask = ballistics != null ? ballistics.BallisticHitMask.value : Physics.DefaultRaycastLayers;
            int count = Physics.RaycastNonAlloc(ray, _hits, distance, mask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = _hits;
            if (count == _hits.Length)
            {
                hits = Physics.RaycastAll(ray, distance, mask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            bool hasHit = false;
            Transform owner = _player.transform.root;
            Transform weapon = _controller.Model != null ? _controller.Model.transform : null;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = hits[index];
                if (hit.collider == null || hit.collider.transform.IsChildOf(owner) || weapon != null && hit.collider.transform.IsChildOf(weapon) || hit.distance >= distance)
                {
                    continue;
                }

                distance = hit.distance;
                hasHit = true;
            }

            TargetPosition = ray.GetPoint(distance);
            Vector3 position = GetOffsetPosition(ray, distance, hasHit ? _settings.BarrelOffsetMeters : 0f);
            if (hasHit)
            {
                position = Vector3.MoveTowards(position, viewer.transform.position, _settings.SurfaceOffsetMeters);
            }
            float depth = Vector3.Dot(position - viewer.transform.position, viewer.transform.forward);
            if (depth <= viewer.nearClipPlane)
            {
                return;
            }

            float unitsPerPixel = 2f * depth * Mathf.Tan(viewer.fieldOfView * 0.5f * Mathf.Deg2Rad) / viewer.pixelHeight;
            float dispersion = _controller.ShotDispersionDegrees;
            dispersion = float.IsFinite(dispersion) ? Mathf.Clamp(dispersion, 0f, 89f) : 0f;
            float spreadPixels = Mathf.Tan(dispersion * Mathf.Deg2Rad) * distance / unitsPerPixel;
            float targetGap = Mathf.Clamp(spreadPixels, _settings.MinimumGapPixels, _settings.MaximumGapPixels);
            _gap = Mathf.Lerp(_gap, targetGap, 1f - Mathf.Exp(-_settings.SpreadResponse * Time.deltaTime));
            float size = 2f * (_gap + _settings.LineLengthPixels + _settings.LineWidthPixels + 2f);
            EnsureMesh();
            Color color = _settings.Color;
            color.a *= _opacity;
            _properties.SetColor(COLOR, color);
            _properties.SetVector(DIMENSIONS, new Vector4(_gap, _settings.LineLengthPixels, _settings.LineWidthPixels * 0.5f, size));
            Matrix4x4 matrix = Matrix4x4.TRS(position, viewer.transform.rotation, Vector3.one * (unitsPerPixel * size));
            var parameters = new RenderParams(_settings.Material)
            {
                camera = viewer,
                layer = 2,
                worldBounds = new Bounds(position, Vector3.one * (unitsPerPixel * size * 2f)),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                matProps = _properties
            };
            Graphics.RenderMesh(parameters, _mesh, 0, matrix);
        }

        private static Vector3 GetOffsetPosition(Ray ray, float distance, float offset)
        {
            return ray.GetPoint(Mathf.Max(0f, distance - Mathf.Max(0f, offset)));
        }

        private static bool ShouldHide(FirstPersonWeaponState state, bool aiming, float aimFactor, StalkerMovementState movement)
        {
            return state == FirstPersonWeaponState.Reloading || aiming || aimFactor > 0.001f || movement.IsSprinting;
        }

        private void EnsureMesh()
        {
            if (_mesh != null)
            {
                return;
            }

            _mesh = new Mesh
            {
                name = "Weapon World Crosshair",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            _properties = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            _opacity = 0f;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_mesh);
                }
                else
                {
                    DestroyImmediate(_mesh);
                }
            }
        }
    }
}
