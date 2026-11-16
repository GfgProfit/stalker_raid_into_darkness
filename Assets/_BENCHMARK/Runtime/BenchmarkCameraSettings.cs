using System;
using UnityEngine;

namespace Rvm.Benchmark
{
    [Serializable]
    public sealed class BenchmarkCameraSettings
    {
        [SerializeField] private float _fieldOfView;
        [SerializeField] private float _near;
        [SerializeField] private float _far;
        [SerializeField] private float _aspect;
        [SerializeField] private Rect _rect;
        [SerializeField] private bool _orthographic;
        [SerializeField] private float _size;
        [SerializeField] private int _mask;
        [SerializeField] private bool _hdr;
        [SerializeField] private bool _msaa;
        [SerializeField] private bool _dynamic;
        [SerializeField] private bool _physical;
        [SerializeField] private float _focalLength;
        [SerializeField] private Vector2 _sensor;
        [SerializeField] private Vector2 _shift;
        [SerializeField] private bool _occlusion;

        public float FieldOfView => _fieldOfView;
        public float Near => _near;
        public float Far => _far;

        public static BenchmarkCameraSettings Capture(Camera camera)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            return new BenchmarkCameraSettings { _fieldOfView = camera.fieldOfView, _near = camera.nearClipPlane, _far = camera.farClipPlane, _aspect = camera.aspect, _rect = camera.rect, _orthographic = camera.orthographic, _size = camera.orthographicSize, _mask = camera.cullingMask, _hdr = camera.allowHDR, _msaa = camera.allowMSAA, _dynamic = camera.allowDynamicResolution, _physical = camera.usePhysicalProperties, _focalLength = camera.focalLength, _sensor = camera.sensorSize, _shift = camera.lensShift, _occlusion = camera.useOcclusionCulling };
        }

        public void SetGameplayLens(float fieldOfView, float near)
        {
            _fieldOfView = fieldOfView;
            _near = near;
        }

        public string Validate()
        {
            if (!Finite(_fieldOfView) || _fieldOfView <= 0 || _fieldOfView >= 180 || !Finite(_near) || _near <= 0 || !Finite(_far) || _far <= _near || !Finite(_aspect) || _aspect <= 0 || !Finite(_size) || _size <= 0 || !Finite(_focalLength) || _focalLength <= 0 || !Finite(_sensor.x) || !Finite(_sensor.y) || _sensor.x <= 0 || _sensor.y <= 0 || !Finite(_shift.x) || !Finite(_shift.y) || !Finite(_rect.x) || !Finite(_rect.y) || !Finite(_rect.width) || !Finite(_rect.height) || _rect.width <= 0 || _rect.height <= 0) return "Invalid route camera settings.";
            return null;
        }

        public void Apply(Camera camera)
        {
            string error = Validate();
            if (error != null) throw new InvalidOperationException(error);
            camera.usePhysicalProperties = _physical;
            camera.sensorSize = _sensor;
            camera.lensShift = _shift;
            camera.focalLength = _focalLength;
            camera.fieldOfView = _fieldOfView;
            camera.nearClipPlane = _near;
            camera.farClipPlane = _far;
            camera.aspect = _aspect;
            camera.rect = _rect;
            camera.orthographic = _orthographic;
            camera.orthographicSize = _size;
            camera.cullingMask = _mask;
            camera.allowHDR = _hdr;
            camera.allowMSAA = _msaa;
            camera.allowDynamicResolution = _dynamic;
            camera.useOcclusionCulling = _occlusion;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class BenchmarkCameraState : IDisposable
    {
        private Camera _camera;
        private readonly BenchmarkCameraSettings _settings;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly Vector3 _scale;

        public BenchmarkCameraState(Camera camera)
        {
            _camera = camera;
            _settings = BenchmarkCameraSettings.Capture(camera);
            _position = camera.transform.localPosition;
            _rotation = camera.transform.localRotation;
            _scale = camera.transform.localScale;
        }

        public void Dispose()
        {
            if (_camera == null) return;
            _settings.Apply(_camera);
            _camera.transform.SetLocalPositionAndRotation(_position, _rotation);
            _camera.transform.localScale = _scale;
            _camera = null;
        }
    }
}
