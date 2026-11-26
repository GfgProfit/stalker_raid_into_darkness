using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Weapons
{
    [AddComponentMenu("Debug/Bullet Impact Debug")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FirstPersonWeaponController))]
    public sealed class BulletImpactDebug : MonoBehaviour
    {
        private const int MAX_MARKERS = 128;
        private const string MARKER_NAME = "Bullet Impact Marker";

        private static readonly int _unlitColorId = Shader.PropertyToID("_UnlitColor");

        [SerializeField] [Min(0.001f)] private float _diameter = 0.035f;
        [SerializeField] [Min(0.01f)] private float _lifetime = 5f;

        private readonly Transform[] _markers = new Transform[MAX_MARKERS];
        private readonly float[] _expiresAt = new float[MAX_MARKERS];

        private FirstPersonWeaponController _weapon;
        private Mesh _sphere;
        private Material _material;
        private int _nextMarker;
        private int _markerCount;

        private void OnEnable()
        {
            _weapon = GetComponent<FirstPersonWeaponController>();
            _sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            Shader shader = Shader.Find("HDRP/Unlit");
            if (_sphere == null || shader == null || !shader.isSupported)
            {
                Debug.LogError("Bullet impact debug requires the built-in sphere and the HDRP/Unlit shader.", this);
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.DontSave };
            _material.SetColor(_unlitColorId, Color.red);
            _weapon.BulletImpacted += MarkImpact;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _markerCount; i++)
            {
                Transform marker = _markers[i];
                if (marker != null && marker.gameObject.activeSelf && now >= _expiresAt[i])
                {
                    marker.gameObject.SetActive(false);
                }
            }
        }

        private void OnDisable()
        {
            if (_weapon != null)
            {
                _weapon.BulletImpacted -= MarkImpact;
                _weapon = null;
            }

            for (int i = 0; i < _markerCount; i++)
            {
                Transform marker = _markers[i];
                if (marker != null)
                {
                    marker.gameObject.SetActive(false);
                    Destroy(marker.gameObject);
                }

                _markers[i] = null;
            }

            _nextMarker = 0;
            _markerCount = 0;
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void MarkImpact(Vector3 position)
        {
            int index = _nextMarker;
            Transform marker = _markers[index];
            if (marker == null)
            {
                var visual = new GameObject(MARKER_NAME) { hideFlags = HideFlags.DontSave };
                SceneManager.MoveGameObjectToScene(visual, gameObject.scene);
                visual.AddComponent<MeshFilter>().sharedMesh = _sphere;
                var renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                marker = visual.transform;
                _markers[index] = marker;
                _markerCount = Mathf.Max(_markerCount, index + 1);
            }

            marker.position = position;
            marker.localScale = Vector3.one * _diameter;
            marker.gameObject.SetActive(true);
            _expiresAt[index] = Time.unscaledTime + _lifetime;
            _nextMarker = (index + 1) % MAX_MARKERS;
        }
    }
}
