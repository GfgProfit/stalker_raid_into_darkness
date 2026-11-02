using UnityEngine;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class XRayFloraWindProvider : MonoBehaviour
    {
        [SerializeField] private XRayFloraWindProfile _profile;
        [SerializeField] private float _windDirectionDegrees;
        [SerializeField] private float _windVelocity;
        [SerializeField] private float _treeAmplitudeIntensity;
        [SerializeField] private Vector4 _ssfxWindTrees = new Vector4(11.0f, 0.15f, 0.5f, 0.15f);
        [SerializeField] private bool _trueStalkerDiagnosticConstants;
        [SerializeField] private float _treeRotationPeriod = 10.0f;
        [SerializeField] private float _treeWaveSpeed = 1.0f;
        [SerializeField] private float _treeAmplitude = 0.005f;
        [SerializeField] private Vector3 _treeWaveScale = new Vector3(0.1f, 0.01f, 0.11f);
        [SerializeField] private bool _paused;
        [SerializeField, InspectorName("DIAGNOSTIC Scene View wind preview")] private bool _previewEnabled;

        private readonly XRayFloraWindState _state = new XRayFloraWindState();
        private int _lastFrame = -1;

        public static XRayFloraWindProvider Active { get; private set; }

        public XRayFloraWindState State => _state;
        public bool InputsComplete => _profile == XRayFloraWindProfile.AnomalySsfx || _profile == XRayFloraWindProfile.TrueStalkerXRay && _trueStalkerDiagnosticConstants;
        public string Diagnostic { get; private set; }

        public bool PreviewEnabled
        {
            get => _previewEnabled;
            set => _previewEnabled = value;
        }

        public XRayFloraWindProfile Profile
        {
            get => _profile;
            set => _profile = value;
        }

        public bool Paused
        {
            get => _paused;
            set => _paused = value;
        }

        public bool TrueStalkerDiagnosticConstants
        {
            get => _trueStalkerDiagnosticConstants;
            set => _trueStalkerDiagnosticConstants = value;
        }

        public void SetWeather(float directionDegrees, float velocity, float treeAmplitudeIntensity)
        {
            _windDirectionDegrees = directionDegrees;
            _windVelocity = velocity;
            _treeAmplitudeIntensity = treeAmplitudeIntensity;
        }

        public void SetSsfxWindTrees(Vector4 setup)
        {
            _ssfxWindTrees = setup;
        }

        public void ResetAnimation()
        {
            Configure();
            if (_state.Initialized)
            {
                _state.Advance(0.0f, Weather());
                _state.Reinitialize();
            }
            else
            {
                _state.Reset(Weather());
            }
            Publish();
        }

        public void Reinitialize()
        {
            _state.Reinitialize();
            Publish();
        }

        public void Tick(float deltaTime)
        {
            Configure();
            _state.Advance(XRayFloraWindState.EngineDelta(deltaTime, _paused), Weather());
            Publish();
        }

        private void OnEnable()
        {
            _lastFrame = -1;
            if (Active == null) Active = this;
            ResetAnimation();
        }

        private void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
                foreach (XRayFloraWindProvider provider in Resources.FindObjectsOfTypeAll<XRayFloraWindProvider>())
                {
                    if (provider == this || !provider.isActiveAndEnabled || !provider.gameObject.scene.IsValid() || Application.isPlaying && !Application.IsPlaying(provider.gameObject)) continue;
                    Active = provider;
                    provider.Reinitialize();
                    break;
                }
                if (Active == null) XRayFloraWindState.Clear();
            }
        }

        private void Update()
        {
            if (!Application.IsPlaying(gameObject) || Active != this || Time.frameCount == _lastFrame)
            {
                return;
            }

            _lastFrame = Time.frameCount;
            Tick(Time.deltaTime);
        }

        private void Configure()
        {
            _state.Profile = InputsComplete ? _profile : XRayFloraWindProfile.Disabled;
            _state.TreesSetup = _ssfxWindTrees;
            _state.TreeRotationPeriod = Mathf.Max(0.01f, _treeRotationPeriod);
            _state.TreeWaveSpeed = _treeWaveSpeed;
            _state.TreeAmplitude = _treeAmplitude;
            _state.TreeWaveScale = _treeWaveScale;
            Diagnostic = _profile == XRayFloraWindProfile.TrueStalkerXRay && !_trueStalkerDiagnosticConstants ? "True Stalker tree wind constants (ps_r__Tree_w_rot, ps_r__Tree_w_speed, ps_r__Tree_w_amp, ps_r__Tree_Wave) are not confirmed for xrRender.dll; wind stays disabled until diagnostic constants are explicitly enabled." : null;
        }

        private XRayFloraWindWeather Weather()
        {
            return new XRayFloraWindWeather(_windDirectionDegrees * Mathf.Deg2Rad, _windVelocity, _treeAmplitudeIntensity);
        }

        private void Publish()
        {
            if (Active == this || Active == null)
            {
                _state.Apply();
            }
        }
    }
}
