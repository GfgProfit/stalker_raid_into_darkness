using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class XRayWeatherSkyRenderer : MonoBehaviour
    {
        private static readonly Dictionary<Volume, XRayWeatherSkyRenderer> _owners = new Dictionary<Volume, XRayWeatherSkyRenderer>();

        [SerializeField] private Volume _volume;
        [SerializeField] private Shader _shader;

        private Object _weatherOwner;
        private Volume _ownedVolume;
        private VolumeProfile _savedProfile;
        private VolumeProfile _runtimeProfile;
        private XRayWeatherSkySettings _settings;

        public string Diagnostic { get; private set; }
        public bool HasWeather => _weatherOwner != null;
        public double SourceTime { get; private set; }
        public XRayWeatherSkySettings Settings => _settings;

        public void Configure(Volume volume, Shader shader)
        {
            Restore();
            _volume = volume;
            _shader = shader;
        }

        public bool TrySetWeather(Object owner, XRayWeatherSnapshot snapshot, double renderSeconds)
        {
            if (owner == null || !isActiveAndEnabled || _weatherOwner != null && _weatherOwner != owner) return false;
            if (_volume != null && _owners.TryGetValue(_volume, out XRayWeatherSkyRenderer currentOwner) && currentOwner != null && currentOwner != this)
            {
                Diagnostic = "Weather sky Volume is already owned by another renderer.";
                return false;
            }
            if (_volume == null || !_volume.isActiveAndEnabled || !_volume.isGlobal || _shader == null || !_shader.isSupported)
            {
                Diagnostic = "Weather sky requires an enabled global HDRP Volume and a serialized supported X-Ray sky shader.";
                return false;
            }
            Vector4 weights = snapshot.SkyResources.Weights;
            float total = 0.0f;
            for (int i = 0; i < 4; i++)
            {
                if (weights[i] <= 0.0f) continue;
                Texture texture = snapshot.SkyResources.Texture(i);
                if (texture == null || texture.dimension != TextureDimension.Cube)
                {
                    Diagnostic = "INPUT BLOCKED: weather sky cubemap reference is missing for " + snapshot.SkyResources.Name(i) + ".";
                    return false;
                }
                total += weights[i];
            }
            if (Mathf.Abs(total - 1.0f) > 0.0001f)
            {
                Diagnostic = "Weather sky resource weights must sum to one.";
                return false;
            }
            if (_ownedVolume != null && _ownedVolume != _volume) Restore();
            if (_runtimeProfile == null) Acquire(owner);
            if (!_ownedVolume.HasInstantiatedProfile() || _ownedVolume.profile != _runtimeProfile)
            {
                Diagnostic = "Weather sky Volume ownership changed.";
                Restore();
                return false;
            }
            _settings.SetWeather(snapshot, _shader);
            SourceTime = renderSeconds;
            Diagnostic = snapshot.CloudsResources.Weights == Vector4.zero || snapshot.CloudsColor.a == 0.0f ? null : "INPUT BLOCKED: the effective standalone cloud shader writes zero alpha; visible separate cloud layers are not implemented.";
            return true;
        }

        public void ReleaseWeather(Object owner)
        {
            if (_weatherOwner == owner) Restore();
        }

        private void Acquire(Object owner)
        {
            _ownedVolume = _volume;
            _savedProfile = _volume.HasInstantiatedProfile() ? _volume.profile : null;
            VolumeProfile authored = _savedProfile != null ? _savedProfile : _volume.sharedProfile;
            _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _runtimeProfile.name = "XRayWeatherSkyRuntime";
            _runtimeProfile.hideFlags = HideFlags.HideAndDontSave;
            if (authored != null)
            {
                for (int i = 0; i < authored.components.Count; i++)
                {
                    VolumeComponent component = Instantiate(authored.components[i]);
                    component.hideFlags = HideFlags.HideAndDontSave;
                    _runtimeProfile.components.Add(component);
                }
            }
            if (!_runtimeProfile.TryGet(out _settings)) _settings = _runtimeProfile.Add<XRayWeatherSkySettings>(true);
            if (!_runtimeProfile.TryGet(out VisualEnvironment environment)) environment = _runtimeProfile.Add<VisualEnvironment>(true);
            environment.active = true;
            environment.skyType.Override(XRayWeatherSkySettings.SKY_ID);
            _settings.active = true;
            _ownedVolume.profile = _runtimeProfile;
            _weatherOwner = owner;
            _owners[_ownedVolume] = this;
        }

        private void Restore()
        {
            if (_ownedVolume != null && _ownedVolume.HasInstantiatedProfile() && _ownedVolume.profile == _runtimeProfile) _ownedVolume.profile = _savedProfile;
            if (!ReferenceEquals(_ownedVolume, null) && _owners.TryGetValue(_ownedVolume, out XRayWeatherSkyRenderer currentOwner) && currentOwner == this) _owners.Remove(_ownedVolume);
            if (_runtimeProfile != null)
            {
                for (int i = 0; i < _runtimeProfile.components.Count; i++) CoreUtils.Destroy(_runtimeProfile.components[i]);
                CoreUtils.Destroy(_runtimeProfile);
            }
            _weatherOwner = null;
            _ownedVolume = null;
            _savedProfile = null;
            _runtimeProfile = null;
            _settings = null;
        }

        private void OnDisable()
        {
            Restore();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOwners()
        {
            _owners.Clear();
        }
    }
}
