using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class XRayWeatherFogRenderer : MonoBehaviour
    {
        private const float SHAFTS_BASE_HEIGHT = 16.0f;
        private const float SHAFTS_MAXIMUM_HEIGHT = 100.0f;
        private const float SHAFTS_DEPTH_EXTENT = 96.0f;
        private const float SHAFTS_DAY_SKY_DISTANCE = 42.0f;
        private const float SHAFTS_NIGHT_SKY_DISTANCE = 60.0f;
        private const float SHAFTS_DAY_ANISOTROPY = 0.22f;
        private const float SHAFTS_NIGHT_ANISOTROPY = 0.53f;
        private const float SHAFTS_AMBIENT = 0.04f;

        private static readonly Dictionary<Volume, XRayWeatherFogRenderer> _owners = new Dictionary<Volume, XRayWeatherFogRenderer>();
        private static XRayWeatherFogRenderer _active;

        [SerializeField] private Volume _volume;

        private Object _weatherOwner;
        private Volume _ownedVolume;
        private Volume _fallbackVolume;
        private VolumeProfile _fallbackProfile;
        private VolumeProfile _savedProfile;
        private VolumeProfile _runtimeProfile;
        private Fog _fog;
        private Fog _authoredFog;
        private float _shaftsSkyDistance;
        private bool _nativeShafts;

        public string Diagnostic { get; private set; }
        public bool HasWeather => _weatherOwner != null;
        public Vector4 Parameters { get; private set; }
        public float MeanFreePath { get; private set; }
        public Volume Volume => _volume != null ? _volume : _fallbackVolume;

        public void Configure(Volume volume)
        {
            if (_volume == volume) return;
            Restore();
            _volume = volume;
        }

        public bool TrySetWeather(Object owner, XRayWeatherSnapshot snapshot)
        {
            if (owner == null || !isActiveAndEnabled || _weatherOwner != null && _weatherOwner != owner || _active != null && _active != this) return false;
            if (!XRayWeatherFogMath.TryGetParameters(snapshot, out Vector4 parameters) || !XRayWeatherFogMath.TryGetMeanFreePath(snapshot, out float meanFreePath))
            {
                Diagnostic = "Weather fog requires finite, increasing source fog boundaries.";
                return false;
            }
            if (!ReferenceEquals(_volume, null) && _volume == null)
            {
                Diagnostic = "The authored weather Fog Volume was destroyed.";
                return false;
            }
            Volume target = _volume != null ? _volume : EnsureFallbackVolume();
            if (!target.isActiveAndEnabled || !target.isGlobal || target.weight != 1.0f)
            {
                Diagnostic = "Weather fog requires an enabled global Fog Volume with full weight.";
                return false;
            }
            if (_owners.TryGetValue(target, out XRayWeatherFogRenderer currentOwner) && currentOwner != null && currentOwner != this) return false;
            if (_runtimeProfile == null)
            {
                VolumeProfile authored = target.HasInstantiatedProfile() ? target.profile : target.sharedProfile;
                if (authored == null || !authored.TryGet(out Fog authoredFog))
                {
                    Diagnostic = "Connect a dedicated Volume profile containing HDRP Fog.";
                    return false;
                }
                Acquire(owner, target, authored);
            }
            if (_ownedVolume != target || !_ownedVolume.HasInstantiatedProfile() || _ownedVolume.profile != _runtimeProfile)
            {
                Diagnostic = "Weather Fog Volume ownership changed.";
                Restore();
                return false;
            }
            _fog.active = true;
            _fog.enabled.Override(true);
            _fog.colorMode.Override(FogColorMode.SkyColor);
            _fog.tint.Override(XRayWeatherFogMath.ToSkyTint(snapshot.FogColor));
            _fog.meanFreePath.Override(meanFreePath);
            ApplyFogMode();
            _fog.multipleScatteringIntensity.Override(0.0f);
            Parameters = parameters;
            MeanFreePath = meanFreePath;
            Diagnostic = null;
            return true;
        }

        internal void SetNativeShafts(Object owner, bool active, float daylight)
        {
            if (_weatherOwner != owner || _fog == null) return;
            if (active)
            {
                _shaftsSkyDistance = Mathf.Lerp(SHAFTS_NIGHT_SKY_DISTANCE, SHAFTS_DAY_SKY_DISTANCE, daylight);
                _fog.maxFogDistance.Override(_shaftsSkyDistance);
                _fog.anisotropy.Override(Mathf.Lerp(SHAFTS_NIGHT_ANISOTROPY, SHAFTS_DAY_ANISOTROPY, daylight));
                int quality = XRayVideoOverrides.FogQuality ?? (int)ScalableSettingLevelParameter.Level.High;
                if (!_fog.quality.overrideState || _fog.quality.value != quality) _fog.quality.Override(quality);
            }
            if (_nativeShafts == active) return;
            _nativeShafts = active;
            ApplyFogMode();
            if (active)
            {
                _fog.depthExtent.Override(SHAFTS_DEPTH_EXTENT);
                _fog.globalLightProbeDimmer.Override(SHAFTS_AMBIENT);
                _fog.directionalLightsOnly.Override(true);
                _fog.denoisingMode.Override(FogDenoisingMode.Gaussian);
            }
            else if (_authoredFog != null)
            {
                RestoreParameter(_fog.depthExtent, _authoredFog.depthExtent);
                RestoreParameter(_fog.anisotropy, _authoredFog.anisotropy);
                RestoreParameter(_fog.globalLightProbeDimmer, _authoredFog.globalLightProbeDimmer);
                RestoreParameter(_fog.directionalLightsOnly, _authoredFog.directionalLightsOnly);
                RestoreParameter(_fog.denoisingMode, _authoredFog.denoisingMode);
                RestoreParameter(_fog.quality, _authoredFog.quality);
            }
        }

        private void ApplyFogMode()
        {
            _fog.baseHeight.Override(_nativeShafts ? SHAFTS_BASE_HEIGHT : XRayWeatherFogMath.HOMOGENEOUS_BASE_HEIGHT);
            _fog.maximumHeight.Override(_nativeShafts ? SHAFTS_MAXIMUM_HEIGHT : XRayWeatherFogMath.HOMOGENEOUS_MAXIMUM_HEIGHT);
            _fog.maxFogDistance.Override(_nativeShafts ? _shaftsSkyDistance : 0.0f);
            _fog.enableVolumetricFog.Override(_nativeShafts);
        }

        private static void RestoreParameter(VolumeParameter parameter, VolumeParameter authored)
        {
            parameter.SetValue(authored);
            parameter.overrideState = authored.overrideState;
        }

        public void ReleaseWeather(Object owner)
        {
            if (_weatherOwner == owner) Restore();
        }

        public static int GetVolumeLayer(GameObject owner)
        {
            if (!owner.scene.IsValid() || !owner.scene.isLoaded) return owner.layer;
            int selectedMask = 0;
            int selectedRank = -1;
            foreach (GameObject root in owner.scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (!camera.gameObject.activeInHierarchy || !camera.TryGetComponent(out HDAdditionalCameraData data) || data.volumeLayerMask.value == 0) continue;
                    int rank = (camera.enabled ? 2 : 0) + (camera.CompareTag("MainCamera") ? 1 : 0);
                    if (rank <= selectedRank) continue;
                    selectedMask = data.volumeLayerMask.value;
                    selectedRank = rank;
                }
            }
            for (int layer = 0; layer < 32; layer++)
            {
                if ((selectedMask & (1 << layer)) != 0) return layer;
            }
            return owner.layer;
        }

        private Volume EnsureFallbackVolume()
        {
            if (_fallbackVolume != null) return _fallbackVolume;
            var volumeObject = new GameObject("Source Weather Fog (Runtime)");
            volumeObject.hideFlags = HideFlags.HideAndDontSave;
            volumeObject.layer = GetVolumeLayer(gameObject);
            volumeObject.transform.SetParent(transform, false);
            _fallbackProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _fallbackProfile.name = "WeatherFogRuntimeDefaults";
            _fallbackProfile.hideFlags = HideFlags.HideAndDontSave;
            Fog fog = _fallbackProfile.Add<Fog>(false);
            fog.hideFlags = HideFlags.HideAndDontSave;
            _fallbackVolume = volumeObject.AddComponent<Volume>();
            _fallbackVolume.isGlobal = true;
            _fallbackVolume.priority = float.MaxValue;
            _fallbackVolume.sharedProfile = _fallbackProfile;
            return _fallbackVolume;
        }

        private void Acquire(Object owner, Volume volume, VolumeProfile authored)
        {
            _ownedVolume = volume;
            _savedProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
            _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _runtimeProfile.name = "XRayWeatherFogRuntime";
            _runtimeProfile.hideFlags = HideFlags.HideAndDontSave;
            foreach (VolumeComponent component in authored.components)
            {
                VolumeComponent clone = Instantiate(component);
                clone.hideFlags = HideFlags.HideAndDontSave;
                _runtimeProfile.components.Add(clone);
            }
            _runtimeProfile.TryGet(out _fog);
            authored.TryGet(out _authoredFog);
            volume.profile = _runtimeProfile;
            _weatherOwner = owner;
            _owners[volume] = this;
            _active = this;
        }

        private void Restore()
        {
            if (_ownedVolume != null && _ownedVolume.HasInstantiatedProfile() && _ownedVolume.profile == _runtimeProfile) _ownedVolume.profile = _savedProfile;
            if (!ReferenceEquals(_ownedVolume, null) && _owners.TryGetValue(_ownedVolume, out XRayWeatherFogRenderer currentOwner) && currentOwner == this) _owners.Remove(_ownedVolume);
            DestroyProfile(_runtimeProfile);
            _runtimeProfile = null;
            _savedProfile = null;
            _ownedVolume = null;
            _fog = null;
            _authoredFog = null;
            _nativeShafts = false;
            _weatherOwner = null;
            if (_fallbackVolume != null) CoreUtils.Destroy(_fallbackVolume.gameObject);
            DestroyProfile(_fallbackProfile);
            _fallbackVolume = null;
            _fallbackProfile = null;
            if (_active == this) _active = null;
        }

        private static void DestroyProfile(VolumeProfile profile)
        {
            if (profile == null) return;
            foreach (VolumeComponent component in profile.components) CoreUtils.Destroy(component);
            CoreUtils.Destroy(profile);
        }

        private void OnDisable()
        {
            Restore();
        }

        private void OnDestroy()
        {
            Restore();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOwner()
        {
            if (_active != null) _active.Restore();
            _active = null;
            _owners.Clear();
        }
    }
}
