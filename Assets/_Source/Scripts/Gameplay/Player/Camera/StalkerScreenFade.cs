using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerScreenFade : MonoBehaviour
    {
        public const string ADD_COLOR_PROPERTY = "_AddColor";

        private const float MIN_FACTOR = 0.0001f;

        [Tooltip("deimos_short.ppe add color (r = g = b) over effector time in seconds.")]
        [SerializeField] private AnimationCurve _addColor = new AnimationCurve();
        [Tooltip("level.remove_pp_effector calls CPostprocessAnimator::Stop(1.0): the factor drops by this value per second.")]
        [SerializeField] private float _stopSpeed = 1.0f;
        [SerializeField] private Material _material;

        private CustomPassVolume _volume;
        private Material _instance;
        private float _time;
        private float _factor;
        private bool _stopping;

        public bool IsPlaying { get; private set; }
        public float Factor => _factor;
        public float Value { get; private set; }
        public float Length => _addColor != null && _addColor.length > 0 ? _addColor[_addColor.length - 1].time : 0.0f;
        public AnimationCurve AddColor => _addColor;

        public void Configure(AnimationCurve addColor, Material material)
        {
            _addColor = addColor;
            _material = material;
        }

        public void Play()
        {
            IsPlaying = true;
            _time = 0.0f;
            _factor = 1.0f;
            _stopping = false;
            Value = Evaluate();
            Apply();
        }

        public void Stop()
        {
            if (IsPlaying)
            {
                _stopping = true;
            }
        }

        public void Tick(float deltaTime)
        {
            if (!IsPlaying)
            {
                return;
            }

            _time += deltaTime;
            if (_stopping)
            {
                _factor = Mathf.Clamp(_factor - deltaTime * _stopSpeed, MIN_FACTOR, 1.0f);
            }

            if ((_stopping && _factor <= MIN_FACTOR) || _time > Length)
            {
                IsPlaying = false;
                Value = 0.0f;
                Apply();
                return;
            }

            Value = Evaluate();
            Apply();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnDisable()
        {
            IsPlaying = false;
            Value = 0.0f;
            Apply();
        }

        private void OnDestroy()
        {
            if (_instance != null)
            {
                Destroy(_instance);
            }
        }

        private float Evaluate()
        {
            return _addColor != null ? _addColor.Evaluate(_time) * _factor : 0.0f;
        }

        private void Apply()
        {
            bool active = IsPlaying && !Mathf.Approximately(Value, 0.0f);
            if (!active)
            {
                if (_volume != null)
                {
                    _volume.enabled = false;
                }

                return;
            }

            if (!EnsureVolume())
            {
                return;
            }

            _instance.SetFloat(ADD_COLOR_PROPERTY, Value);
            _volume.enabled = true;
        }

        private bool EnsureVolume()
        {
            if (_volume != null)
            {
                return true;
            }

            if (_material == null)
            {
                return false;
            }

            _instance = new Material(_material);
            _volume = gameObject.AddComponent<CustomPassVolume>();
            _volume.isGlobal = true;
            _volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            var pass = (StalkerScreenAddColorPass)_volume.AddPassOfType(typeof(StalkerScreenAddColorPass));
            pass.name = nameof(StalkerScreenFade);
            pass.Material = _instance;
            return true;
        }
    }
}
