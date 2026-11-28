using System;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Domain
{
    [Serializable]
    public struct ItemIconViewSettings
    {
        public const float DEFAULT_LIGHT_INTENSITY = 20f;

        [SerializeField] private Vector3 _modelEulerAngles;
        [SerializeField] private Vector3 _modelScale;
        [SerializeField] private Vector3 _cameraEulerAngles;
        [SerializeField] [Range(1f, 2f)] private float _padding;
        [SerializeField] private bool _useDirectionalLight;
        [SerializeField] private float _lightIntensity;

        public ItemIconViewSettings(Vector3 modelEulerAngles, Vector3 modelScale, Vector3 cameraEulerAngles, float padding, bool useDirectionalLight, float lightIntensity)
        {
            _modelEulerAngles = modelEulerAngles;
            _modelScale = modelScale;
            _cameraEulerAngles = cameraEulerAngles;
            _padding = padding;
            _useDirectionalLight = useDirectionalLight;
            _lightIntensity = lightIntensity;
        }

        public static ItemIconViewSettings Default => new(Vector3.zero, Vector3.one, new Vector3(25f, -135f, 0f), 1.15f, true, DEFAULT_LIGHT_INTENSITY);

        public Vector3 ModelEulerAngles => _modelEulerAngles;
        public Vector3 ModelScale => _modelScale == Vector3.zero ? Vector3.one : _modelScale;
        public Vector3 CameraEulerAngles => _cameraEulerAngles;
        public float Padding => Mathf.Max(1f, _padding);
        public bool UseDirectionalLight => _useDirectionalLight;
        public float LightIntensity => _lightIntensity;
    }
}
