using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class PunctualShadowDistance : MonoBehaviour
    {
        private static readonly HashSet<Scene> _registeredScenes = new HashSet<Scene>();

        private GameConfig _configuration;
        private System.Threading.SynchronizationContext _synchronizationContext;
        private Light _light;
        private HDAdditionalLightData _data;
        private float _originalFadeDistance;
        private float _lastAppliedFadeDistance;
        private int _configurationChangeQueued;
        private bool _hasBaseline;
        private bool _ownsFadeDistance;
        private bool _subscribed;

        public GameConfig Configuration => _configuration;

        public void ApplyConfiguration(GameConfig configuration)
        {
            if (configuration != null)
            {
                _synchronizationContext = System.Threading.SynchronizationContext.Current ?? throw new System.InvalidOperationException("Punctual shadow configuration requires a Unity main-thread synchronization context.");
            }
            if (_configuration != configuration)
            {
                Unsubscribe();
                _configuration = configuration;
            }
            Refresh();
        }

        public void Refresh()
        {
            if (!gameObject.scene.IsValid())
            {
                Unsubscribe();
                return;
            }
            UpdateSubscription();
            if (_light == null) _light = GetComponent<Light>();
            HDAdditionalLightData data = GetComponent<HDAdditionalLightData>();
            if (_data != data)
            {
                RestoreFadeDistance();
                _data = data;
                _hasBaseline = false;
            }
            if (_data == null) return;
            float current = _data.shadowFadeDistance;
            if (!_hasBaseline || !_ownsFadeDistance || current != _lastAppliedFadeDistance)
            {
                _originalFadeDistance = current;
                _hasBaseline = true;
                _ownsFadeDistance = false;
            }
            float distance = XRayVideoOverrides.PunctualShadowDistance ?? (_configuration != null ? _configuration.PunctualShadowDistance : 0.0f);
            if (_light == null || _light.type != LightType.Point && _light.type != LightType.Spot || _configuration == null || !_configuration.PunctualShadowDistanceEnabled || !float.IsFinite(distance) || distance <= 0.0f || !float.IsFinite(_originalFadeDistance) || _originalFadeDistance < 0.0f)
            {
                RestoreFadeDistance();
                return;
            }
            float target = Mathf.Min(_originalFadeDistance, distance);
            if (current != target) _data.shadowFadeDistance = target;
            _lastAppliedFadeDistance = _data.shadowFadeDistance;
            _ownsFadeDistance = true;
        }

        public void Release()
        {
            Unsubscribe();
            RestoreFadeDistance();
            _configuration = null;
            _synchronizationContext = null;
            _light = null;
            _data = null;
            _hasBaseline = false;
        }

        public static PunctualShadowDistance Register(Light light)
        {
            return Register(light, Application.isPlaying ? GameConfig.Load() : null);
        }

        public static void RegisterHierarchy(Component component)
        {
            if (component != null) RegisterHierarchy(component.gameObject);
        }

        public static void RegisterHierarchy(GameObject root)
        {
            if (root == null || !root.scene.IsValid()) return;
            GameConfig configuration = Application.isPlaying ? GameConfig.Load() : null;
            Light[] lights = root.GetComponentsInChildren<Light>(true);
            foreach (Light light in lights) Register(light, configuration);
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
            {
                ParticleSystem.LightsModule module = system.lights;
                if (module.light != null) Register(module.light, configuration);
            }
        }

        private static PunctualShadowDistance Register(Light light, GameConfig configuration)
        {
            if (light == null || !light.gameObject.scene.IsValid()) return null;
            XRayVideoLightShadows.Register(light);
            PunctualShadowDistance policy = light.GetComponent<PunctualShadowDistance>();
            if (policy != null && !policy.enabled) return policy;
            if (light.type != LightType.Point && light.type != LightType.Spot)
            {
                if (policy != null) policy.Refresh();
                return policy;
            }
            if (policy == null) policy = light.gameObject.AddComponent<PunctualShadowDistance>();
            if (Application.isPlaying) policy.ApplyConfiguration(configuration);
            return policy;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            ApplyConfiguration(_configuration != null ? _configuration : GameConfig.Load());
            XRayVideoLightShadows.Register(_light);
        }

        private void OnDisable()
        {
            Unsubscribe();
            RestoreFadeDistance();
            _light = null;
            _data = null;
            _hasBaseline = false;
        }

        private void OnDestroy()
        {
            Release();
        }

        private void UpdateSubscription()
        {
            if (_configuration == null || !isActiveAndEnabled)
            {
                Unsubscribe();
                return;
            }
            if (_subscribed) return;
            _configuration.Changed += OnConfigurationChanged;
            XRayVideoOverrides.PunctualShadowDistanceChanged += Refresh;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_subscribed)
            {
                if (_configuration != null) _configuration.Changed -= OnConfigurationChanged;
                XRayVideoOverrides.PunctualShadowDistanceChanged -= Refresh;
            }
            _subscribed = false;
        }

        private void OnConfigurationChanged()
        {
            System.Threading.SynchronizationContext context = _synchronizationContext;
            if (context == null || System.Threading.Interlocked.Exchange(ref _configurationChangeQueued, 1) != 0) return;
            context.Post(ApplyConfigurationChange, null);
        }

        private void ApplyConfigurationChange(object state)
        {
            System.Threading.Interlocked.Exchange(ref _configurationChangeQueued, 0);
            if (this == null)
            {
                Unsubscribe();
                return;
            }
            if (_subscribed && isActiveAndEnabled) Refresh();
        }

        private void RestoreFadeDistance()
        {
            if (_ownsFadeDistance && _data != null && _data.gameObject.scene.IsValid() && _data.shadowFadeDistance == _lastAppliedFadeDistance) _data.shadowFadeDistance = _originalFadeDistance;
            _ownsFadeDistance = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistration()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _registeredScenes.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeRegistration()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InitializeVideoDefaults()
        {
            XRayVideoOverrides.SetPunctualShadowDefaultsProvider(GetVideoDefaults);
        }

        private static float? GetVideoDefaults()
        {
            GameConfig configuration = GameConfig.Load();
            return configuration != null && configuration.PunctualShadowDistanceEnabled ? configuration.PunctualShadowDistance : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterLoadedScenes()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++) RegisterScene(SceneManager.GetSceneAt(index));
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RegisterScene(scene);
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            _registeredScenes.Remove(scene);
        }

        private static void RegisterScene(Scene scene)
        {
            if (!Application.isPlaying || !scene.IsValid() || !scene.isLoaded || !_registeredScenes.Add(scene)) return;
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (GameObject root in roots) RegisterHierarchy(root);
        }
    }
}
