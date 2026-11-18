using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class PunctualShadowDistanceTests
    {
        private SynchronizationContext _previousContext;
        private QueuedSynchronizationContext _context;

        [SetUp]
        public void SetUp()
        {
            _previousContext = SynchronizationContext.Current;
            _context = new QueuedSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_context);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _context.Drain();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(_previousContext);
                _context = null;
                _previousContext = null;
            }
        }

        [Test]
        public void SharedConfigurationDefaultsToEnabledSeventyFiveMeters()
        {
            using (var fixture = new Fixture())
            {
                GameConfig configuration = fixture.CreateConfiguration();
                Assert.That(configuration.PunctualShadowDistanceEnabled, Is.True);
                Assert.That(configuration.PunctualShadowDistance, Is.EqualTo(75.0f));
                GameConfig canonical = GameConfig.Load();
                Assert.That(canonical, Is.Not.Null);
                Assert.That(canonical.PunctualShadowDistanceEnabled, Is.True);
                Assert.That(canonical.PunctualShadowDistance, Is.EqualTo(75.0f));
                Assert.That(canonical.InstanceNativeSubmissionEnabled, Is.True);
            }
        }

        [TestCase(LightType.Point)]
        [TestCase(LightType.Spot)]
        public void PunctualLightsOnlyChangeNativeFadeAndRestoreAuthoredFields(LightType type)
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight(type);
                var original = new LightSnapshot(light);
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig configuration = fixture.CreateConfiguration();
                string configurationBefore = EditorJsonUtility.ToJson(configuration);
                policy.ApplyConfiguration(configuration);
                Assert.That(policy.Configuration, Is.SameAs(configuration));
                Assert.That(light.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(75.0f));
                original.AssertExceptFade();
                Assert.That(EditorJsonUtility.ToJson(configuration), Is.EqualTo(configurationBefore));
                policy.Release();
                original.AssertAll();
            }
        }

        [TestCase(LightShadows.None, false)]
        [TestCase(LightShadows.None, true)]
        [TestCase(LightShadows.Soft, false)]
        public void ShadowlessAndDisabledLightsArePreparedWithoutBeingEnabled(LightShadows shadows, bool enabled)
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                light.shadows = shadows;
                light.enabled = enabled;
                var original = new LightSnapshot(light);
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                policy.ApplyConfiguration(fixture.CreateConfiguration());
                Assert.That(light.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(75.0f));
                original.AssertExceptFade();
                light.enabled = true;
                light.shadows = LightShadows.Hard;
                Assert.That(light.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(75.0f));
                policy.Release();
                Assert.That(light.enabled, Is.True);
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Hard));
                Assert.That(light.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(original.Fade));
            }
        }

        [TestCase(0.0f)]
        [TestCase(30.0f)]
        public void ExistingShorterAuthoredFadeIsNeverExtended(float authored)
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                data.shadowFadeDistance = authored;
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig configuration = fixture.CreateConfiguration();
                policy.ApplyConfiguration(configuration);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(authored));
                SetDistance(configuration, 150.0f);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(authored));
                policy.Release();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(authored));
            }
        }

        [Test]
        public void SharedChangesRaiseAndDisableTheCapWithoutErasingAuthoredLimits()
        {
            using (var fixture = new Fixture())
            {
                Light first = fixture.CreateLight();
                Light second = fixture.CreateLight(LightType.Spot);
                HDAdditionalLightData firstData = first.GetComponent<HDAdditionalLightData>();
                HDAdditionalLightData secondData = second.GetComponent<HDAdditionalLightData>();
                firstData.shadowFadeDistance = 180.0f;
                secondData.shadowFadeDistance = 30.0f;
                PunctualShadowDistance firstPolicy = fixture.AddPolicy(first);
                PunctualShadowDistance secondPolicy = fixture.AddPolicy(second);
                GameConfig configuration = fixture.CreateConfiguration();
                firstPolicy.ApplyConfiguration(configuration);
                secondPolicy.ApplyConfiguration(configuration);
                Assert.That(firstData.shadowFadeDistance, Is.EqualTo(75.0f));
                Assert.That(secondData.shadowFadeDistance, Is.EqualTo(30.0f));
                SetDistance(configuration, 120.0f);
                Assert.That(firstData.shadowFadeDistance, Is.EqualTo(120.0f));
                Assert.That(secondData.shadowFadeDistance, Is.EqualTo(30.0f));
                SetEnabled(configuration, false);
                Assert.That(firstData.shadowFadeDistance, Is.EqualTo(180.0f));
                Assert.That(secondData.shadowFadeDistance, Is.EqualTo(30.0f));
                SetDistance(configuration, 240.0f);
                SetEnabled(configuration, true);
                Assert.That(firstData.shadowFadeDistance, Is.EqualTo(180.0f));
                Assert.That(secondData.shadowFadeDistance, Is.EqualTo(30.0f));
                SetDistance(configuration, 75.0f);
                Assert.That(firstData.shadowFadeDistance, Is.EqualTo(75.0f));
                Assert.That(secondData.shadowFadeDistance, Is.EqualTo(30.0f));
            }
        }

        [Test]
        public void ReplacingConfigurationReleasesThePreviousSubscriptionAndKeepsAuthoredFade()
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig first = fixture.CreateConfiguration();
                GameConfig second = fixture.CreateConfiguration();
                SetDistance(second, 50.0f);
                policy.ApplyConfiguration(first);
                policy.ApplyConfiguration(second);
                Assert.That(policy.Configuration, Is.SameAs(second));
                Assert.That(data.shadowFadeDistance, Is.EqualTo(50.0f));
                SetDistance(first, 10.0f);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(50.0f));
                SetDistance(second, 90.0f);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(90.0f));
                policy.Release();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(140.0f));
                SetDistance(second, 20.0f);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(140.0f));
            }
        }

        [Test]
        public void NullConfigurationAndRepeatedApplyReleaseDoNotBakeTheCapIntoTheBaseline()
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig configuration = fixture.CreateConfiguration();
                policy.ApplyConfiguration(configuration);
                policy.ApplyConfiguration(configuration);
                policy.Refresh();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(75.0f));
                policy.ApplyConfiguration(null);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(140.0f));
                policy.Release();
                policy.Release();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(140.0f));
                policy.ApplyConfiguration(configuration);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(75.0f));
                policy.Release();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(140.0f));
            }
        }

        [Test]
        public void ConcurrentLightAnimationAndDimmersRemainOwnedByTheirSource()
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig configuration = fixture.CreateConfiguration();
                policy.ApplyConfiguration(configuration);
                light.enabled = false;
                light.color = Color.magenta;
                light.intensity = 19.0f;
                light.range = 31.0f;
                light.shadows = LightShadows.None;
                data.shadowDimmer = 0.27f;
                data.volumetricShadowDimmer = 0.58f;
                var animated = new LightSnapshot(light);
                SetDistance(configuration, 100.0f);
                policy.Refresh();
                animated.AssertExceptFade();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(100.0f));
                policy.Release();
                animated.AssertExceptFade();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(140.0f));
            }
        }

        [Test]
        public void ReleasePreservesALaterExternalFadeWrite()
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                policy.ApplyConfiguration(fixture.CreateConfiguration());
                data.shadowFadeDistance = 42.0f;
                policy.Release();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(42.0f));
            }
        }

        [TestCase(40.0f)]
        [TestCase(90.0f)]
        public void RefreshAdoptsANewExternalAuthoredLimit(float external)
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig configuration = fixture.CreateConfiguration();
                policy.ApplyConfiguration(configuration);
                data.shadowFadeDistance = external;
                policy.Refresh();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(Mathf.Min(external, 75.0f)));
                SetDistance(configuration, 120.0f);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(external));
                policy.Release();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(external));
            }
        }

        [TestCase(LightType.Directional)]
        [TestCase(LightType.Rectangle)]
        public void NonPunctualTypesRemainUnchanged(LightType type)
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight(type);
                var original = new LightSnapshot(light);
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig configuration = fixture.CreateConfiguration();
                policy.ApplyConfiguration(configuration);
                SetDistance(configuration, 25.0f);
                policy.Refresh();
                original.AssertAll();
                policy.Release();
                original.AssertAll();
            }
        }

        [Test]
        public void AdditionalDataAddedOrReplacedIsPreparedFromItsOwnAuthoredValue()
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight(LightType.Point, false);
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                policy.ApplyConfiguration(fixture.CreateConfiguration());
                Assert.That(light.GetComponent<HDAdditionalLightData>(), Is.Null);
                HDAdditionalLightData first = light.gameObject.AddComponent<HDAdditionalLightData>();
                first.shadowFadeDistance = 160.0f;
                policy.Refresh();
                Assert.That(first.shadowFadeDistance, Is.EqualTo(75.0f));
                Object.DestroyImmediate(first);
                HDAdditionalLightData second = light.gameObject.AddComponent<HDAdditionalLightData>();
                second.shadowFadeDistance = 210.0f;
                policy.Refresh();
                Assert.That(second.shadowFadeDistance, Is.EqualTo(75.0f));
                policy.Release();
                Assert.That(second.shadowFadeDistance, Is.EqualTo(210.0f));
            }
        }

        [Test]
        public void TypeChangesRestoreDirectionalStateAndRecapturePunctualState()
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                policy.ApplyConfiguration(fixture.CreateConfiguration());
                light.type = LightType.Directional;
                policy.Refresh();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(140.0f));
                light.type = LightType.Spot;
                policy.Refresh();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(75.0f));
                policy.Release();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(140.0f));
            }
        }

        [Test]
        public void HierarchyRegistrationIsIdempotentAndIncludesInactivePunctualChildren()
        {
            using (var fixture = new Fixture())
            {
                GameObject root = fixture.CreateObject("Dynamic light hierarchy");
                Light point = fixture.CreateLight();
                Light spot = fixture.CreateLight(LightType.Spot);
                Light sun = fixture.CreateLight(LightType.Directional);
                point.transform.SetParent(root.transform);
                spot.transform.SetParent(root.transform);
                sun.transform.SetParent(root.transform);
                spot.gameObject.SetActive(false);
                PunctualShadowDistance.RegisterHierarchy(root);
                PunctualShadowDistance.RegisterHierarchy(root.transform);
                Assert.That(point.GetComponents<PunctualShadowDistance>().Length, Is.EqualTo(1));
                Assert.That(spot.GetComponents<PunctualShadowDistance>().Length, Is.EqualTo(1));
                Assert.That(sun.GetComponent<PunctualShadowDistance>(), Is.Null);
                GameConfig configuration = fixture.CreateConfiguration();
                point.GetComponent<PunctualShadowDistance>().ApplyConfiguration(configuration);
                spot.GetComponent<PunctualShadowDistance>().ApplyConfiguration(configuration);
                Assert.That(point.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(75.0f));
                Assert.That(spot.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(75.0f));
                Assert.That(spot.gameObject.activeSelf, Is.False);
                Assert.That(sun.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(140.0f));
            }
        }

        [Test]
        public void DestroyingAConsumerDoesNotBreakOtherSharedConfigurationConsumers()
        {
            using (var fixture = new Fixture())
            {
                Light destroyed = fixture.CreateLight();
                Light surviving = fixture.CreateLight();
                GameConfig configuration = fixture.CreateConfiguration();
                fixture.AddPolicy(destroyed).ApplyConfiguration(configuration);
                fixture.AddPolicy(surviving).ApplyConfiguration(configuration);
                Object.DestroyImmediate(destroyed.gameObject);
                Assert.DoesNotThrow(() => SetDistance(configuration, 110.0f));
                Assert.That(surviving.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(110.0f));
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1.0f)]
        [TestCase(0.0f)]
        public void InvalidGlobalDistancesAreRejectedAndReleaseOwnedFade(float distance)
        {
            using (var fixture = new Fixture())
            {
                GameConfig canonical = GameConfig.Load();
                Assert.That(canonical, Is.Not.Null);
                GameConfig configuration = fixture.CloneConfiguration(canonical);
                Light light = fixture.CreateLight();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                policy.ApplyConfiguration(configuration);
                SetDistance(configuration, distance);
                Assert.That(configuration.TryValidate(out string diagnostic), Is.False);
                Assert.That(diagnostic, Does.Contain("PunctualShadowDistance"));
                Assert.That(light.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(140.0f));
            }
        }

        [Test]
        public void ConfigurationValidationOnlyAppliesAfterMainContextDispatch()
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig configuration = fixture.CreateConfiguration();
                policy.ApplyConfiguration(configuration);
                SetDistancePending(configuration, 120.0f);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(75.0f));
                Assert.That(_context.PendingCount, Is.GreaterThan(0));
                _context.Drain();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(120.0f));
            }
        }

        [Test]
        public void WorkerConfigurationValidationQueuesWithoutTouchingUnityUntilMainDispatch()
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig configuration = fixture.CreateConfiguration();
                policy.ApplyConfiguration(configuration);
                typeof(GameConfig).GetField("_punctualShadowDistance", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(configuration, 110.0f);
                Exception failure = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        NotifyConfiguration(configuration);
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                });
                worker.IsBackground = true;
                worker.Start();
                Assert.That(worker.Join(1000), Is.True);
                Assert.That(failure, Is.Null);
                Assert.That(data.shadowFadeDistance, Is.EqualTo(75.0f));
                Assert.That(_context.PendingCount, Is.GreaterThan(0));
                _context.Drain();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(110.0f));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingConfigurationCannotReapplyAfterDisableOrRelease(bool release)
        {
            using (var fixture = new Fixture())
            {
                Light light = fixture.CreateLight();
                HDAdditionalLightData data = light.GetComponent<HDAdditionalLightData>();
                PunctualShadowDistance policy = fixture.AddPolicy(light);
                GameConfig configuration = fixture.CreateConfiguration();
                policy.ApplyConfiguration(configuration);
                SetDistancePending(configuration, 120.0f);
                Assert.That(_context.PendingCount, Is.GreaterThan(0));
                if (release) policy.Release();
                else policy.enabled = false;
                float beforeDispatch = data.shadowFadeDistance;
                _context.Drain();
                Assert.That(data.shadowFadeDistance, Is.EqualTo(beforeDispatch));
                Assert.That(data.shadowFadeDistance, Is.Not.EqualTo(120.0f));
                if (release) Assert.That(data.shadowFadeDistance, Is.EqualTo(140.0f));
            }
        }

        private void SetDistance(GameConfig configuration, float distance)
        {
            SetDistancePending(configuration, distance);
            _context.Drain();
        }

        private static void SetDistancePending(GameConfig configuration, float distance)
        {
            typeof(GameConfig).GetField("_punctualShadowDistance", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(configuration, distance);
            NotifyConfiguration(configuration);
        }

        private void SetEnabled(GameConfig configuration, bool enabled)
        {
            typeof(GameConfig).GetField("_punctualShadowDistanceEnabled", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(configuration, enabled);
            NotifyConfiguration(configuration);
            _context.Drain();
        }

        private static void NotifyConfiguration(GameConfig configuration)
        {
            typeof(GameConfig).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(configuration, null);
        }

        private sealed class QueuedSynchronizationContext : SynchronizationContext
        {
            private readonly Queue<PendingWork> _pending = new Queue<PendingWork>();

            public int PendingCount
            {
                get
                {
                    lock (_pending)
                    {
                        return _pending.Count;
                    }
                }
            }

            public override void Post(SendOrPostCallback callback, object state)
            {
                lock (_pending)
                {
                    _pending.Enqueue(new PendingWork(callback, state));
                }
            }

            public override SynchronizationContext CreateCopy()
            {
                return this;
            }

            public void Drain()
            {
                while (true)
                {
                    PendingWork work;
                    lock (_pending)
                    {
                        if (_pending.Count == 0) return;
                        work = _pending.Dequeue();
                    }
                    work.Invoke();
                }
            }
        }

        private readonly struct PendingWork
        {
            private readonly SendOrPostCallback _callback;
            private readonly object _state;

            public PendingWork(SendOrPostCallback callback, object state)
            {
                _callback = callback;
                _state = state;
            }

            public void Invoke()
            {
                _callback(_state);
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene _scene = EditorSceneManager.NewPreviewScene();
            private readonly List<GameConfig> _configurations = new List<GameConfig>();

            public GameConfig CreateConfiguration()
            {
                GameConfig configuration = ScriptableObject.CreateInstance<GameConfig>();
                _configurations.Add(configuration);
                return configuration;
            }

            public GameConfig CloneConfiguration(GameConfig source)
            {
                GameConfig configuration = Object.Instantiate(source);
                _configurations.Add(configuration);
                return configuration;
            }

            public GameObject CreateObject(string name)
            {
                var gameObject = new GameObject(name);
                SceneManager.MoveGameObjectToScene(gameObject, _scene);
                return gameObject;
            }

            public Light CreateLight(LightType type = LightType.Point, bool additionalData = true)
            {
                GameObject gameObject = CreateObject(type + " policy source");
                Light light = gameObject.AddComponent<Light>();
                light.type = type;
                light.color = new Color(0.17f, 0.28f, 0.39f);
                light.intensity = 42.0f;
                light.range = 23.0f;
                light.shadows = LightShadows.Soft;
                if (additionalData)
                {
                    HDAdditionalLightData data = gameObject.AddComponent<HDAdditionalLightData>();
                    data.affectsVolumetric = true;
                    data.shadowFadeDistance = 140.0f;
                    data.shadowDimmer = 0.64f;
                    data.volumetricShadowDimmer = 0.36f;
                }
                return light;
            }

            public PunctualShadowDistance AddPolicy(Light light)
            {
                return light.gameObject.AddComponent<PunctualShadowDistance>();
            }

            public void Dispose()
            {
                foreach (GameObject root in _scene.GetRootGameObjects())
                {
                    foreach (PunctualShadowDistance policy in root.GetComponentsInChildren<PunctualShadowDistance>(true))
                    {
                        policy.Release();
                    }
                }
                EditorSceneManager.ClosePreviewScene(_scene);
                foreach (GameConfig configuration in _configurations)
                {
                    Object.DestroyImmediate(configuration);
                }
            }
        }

        private sealed class LightSnapshot
        {
            private readonly Light _light;
            private readonly HDAdditionalLightData _additional;
            private readonly bool _enabled;
            private readonly LightType _type;
            private readonly LightShadows _shadows;
            private readonly Color _color;
            private readonly float _intensity;
            private readonly float _range;
            private readonly float _shadowDimmer;
            private readonly float _volumetricShadowDimmer;
            private readonly bool _affectsVolumetric;
            private readonly float _fade;

            public float Fade => _fade;

            public LightSnapshot(Light light)
            {
                _light = light;
                _additional = light.GetComponent<HDAdditionalLightData>();
                _enabled = light.enabled;
                _type = light.type;
                _shadows = light.shadows;
                _color = light.color;
                _intensity = light.intensity;
                _range = light.range;
                _shadowDimmer = _additional.shadowDimmer;
                _volumetricShadowDimmer = _additional.volumetricShadowDimmer;
                _affectsVolumetric = _additional.affectsVolumetric;
                _fade = _additional.shadowFadeDistance;
            }

            public void AssertExceptFade()
            {
                Assert.That(_light.enabled, Is.EqualTo(_enabled));
                Assert.That(_light.type, Is.EqualTo(_type));
                Assert.That(_light.shadows, Is.EqualTo(_shadows));
                Assert.That(_light.color, Is.EqualTo(_color));
                Assert.That(_light.intensity, Is.EqualTo(_intensity));
                Assert.That(_light.range, Is.EqualTo(_range));
                Assert.That(_additional.shadowDimmer, Is.EqualTo(_shadowDimmer));
                Assert.That(_additional.volumetricShadowDimmer, Is.EqualTo(_volumetricShadowDimmer));
                Assert.That(_additional.affectsVolumetric, Is.EqualTo(_affectsVolumetric));
            }

            public void AssertAll()
            {
                AssertExceptFade();
                Assert.That(_additional.shadowFadeDistance, Is.EqualTo(_fade));
            }
        }
    }
}
