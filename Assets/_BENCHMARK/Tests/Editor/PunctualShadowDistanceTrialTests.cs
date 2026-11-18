using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rvm.CleanReleaseRoute07;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Rvm.Benchmark.Tests
{
    public sealed class PunctualShadowDistanceTrialTests
    {
        private const float ORIGINAL_DIMMER = 0.64f;
        private const float ORIGINAL_VOLUMETRIC_DIMMER = 0.36f;
        private const float TOLERANCE = 0.00001f;

        [TestCase("A")]
        [TestCase("B")]
        public void ModesChangeOnlyTheirShadowFieldsAndRestoreOriginalValues(string mode)
        {
            using (var fixture = new Fixture())
            {
                var light = fixture.CreateLight(70f);
                var additional = light.GetComponent<HDAdditionalLightData>();
                var original = new LightSnapshot(light);
                var trial = fixture.Attach(mode, new[] { light });
                Assert.That(trial.Mode, Is.EqualTo(mode));
                Assert.That(trial.TrackedCount, Is.EqualTo(1));
                Assert.That(trial.EligibleCount, Is.EqualTo(1));
                Assert.That(trial.WorldDiscoveryPasses, Is.Zero);
                Assert.That(additional.shadowFadeDistance, Is.EqualTo(mode == "B" ? 75f : 140f));
                AssertDimmers(additional, 1f);
                original.AssertLightUnchanged();
                fixture.Camera.transform.position = Vector3.back * 20f;
                trial.RefreshTargetsAtPose(new[] { light });
                Assert.That(additional.shadowFadeDistance, Is.EqualTo(mode == "B" ? 75f : 140f));
                AssertDimmers(additional, 1f);
                original.AssertLightUnchanged();
                trial.Restore();
                Assert.That(trial.Restored, Is.True);
                original.AssertAllUnchanged();
            }
        }

        [TestCase("A")]
        [TestCase("B")]
        public void DisabledVolumetricsPreserveHiddenOriginalShadowDimmerThroughApplyAndRestore(string mode)
        {
            using (var fixture = new Fixture())
            {
                var light = fixture.CreateLight(100f);
                var additional = light.GetComponent<HDAdditionalLightData>();
                additional.affectsVolumetric = false;
                var original = new LightSnapshot(light);
                var trial = fixture.Attach(mode, new[] { light });
                Assert.That(additional.affectsVolumetric, Is.False);
                Assert.That(additional.volumetricShadowDimmer, Is.Zero);
                Assert.That(additional.shadowDimmer, Is.EqualTo(ORIGINAL_DIMMER));
                trial.Restore();
                original.AssertAllUnchanged();
                additional.affectsVolumetric = true;
                Assert.That(additional.volumetricShadowDimmer, Is.EqualTo(ORIGINAL_VOLUMETRIC_DIMMER));
            }
        }

        [Test]
        public void ProtectedLightTypesAndInvalidOriginalShadowSettingsRemainUntouched()
        {
            using (var fixture = new Fixture())
            {
                var targets = new List<Light>();
                targets.Add(fixture.CreateLight(100f));
                targets[0].shadows = LightShadows.None;
                targets.Add(fixture.CreateLight(100f, LightType.Directional));
                targets.Add(fixture.CreateLight(100f, LightType.Rectangle));
                targets.Add(fixture.CreateLight(100f, LightType.Point, false));
                targets.Add(fixture.CreateLight(100f));
                targets[4].GetComponent<HDAdditionalLightData>().shadowDimmer = 0f;
                targets.Add(fixture.CreateLight(100f));
                targets[5].GetComponent<HDAdditionalLightData>().shadowFadeDistance = 0f;
                targets.Add(fixture.CreateLight(100f));
                targets[6].GetComponent<HDAdditionalLightData>().shadowFadeDistance = float.NaN;
                var originals = new List<LightSnapshot>();
                foreach (var light in targets)
                {
                    originals.Add(new LightSnapshot(light));
                }
                var trial = fixture.Attach("B", targets);
                Assert.That(trial.EligibleCount, Is.Zero);
                trial.RefreshTargetsAtPose(targets);
                trial.Restore();
                foreach (var original in originals)
                {
                    original.AssertAllUnchanged();
                }
            }
        }

        [Test]
        public void NativeFadeNeverExtendsAnExistingShorterShadowDistance()
        {
            using (var fixture = new Fixture())
            {
                var light = fixture.CreateLight(50f, LightType.Spot);
                light.GetComponent<HDAdditionalLightData>().shadowFadeDistance = 60f;
                var original = new LightSnapshot(light);
                var trial = fixture.Attach("B", new[] { light });
                original.AssertAllUnchanged();
                trial.Restore();
                original.AssertAllUnchanged();
            }
        }

        [Test]
        public void RefreshCapsNewSourcesWithoutReplacingOriginalSnapshotsOrDuplicatingTargets()
        {
            using (var fixture = new Fixture())
            {
                var first = fixture.CreateLight(100f);
                var originalFirst = new LightSnapshot(first);
                var trial = fixture.Attach("B", new[] { first });
                var added = fixture.CreateLight(150f, LightType.Spot);
                var originalAdded = new LightSnapshot(added);
                trial.RefreshTargetsAtPose(new[] { first, first, added, added });
                Assert.That(trial.TrackedCount, Is.EqualTo(2));
                Assert.That(trial.EligibleCount, Is.EqualTo(2));
                Assert.That(trial.DiscoveryPasses, Is.EqualTo(2));
                Assert.That(first.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(75f));
                Assert.That(added.GetComponent<HDAdditionalLightData>().shadowFadeDistance, Is.EqualTo(75f));
                trial.Restore();
                originalFirst.AssertAllUnchanged();
                originalAdded.AssertAllUnchanged();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeFadeDoesNotRequireOrTrackAPrimaryCamera(bool destroyAfterAttach)
        {
            using (var fixture = new Fixture())
            {
                var light = fixture.CreateLight(100f);
                var additional = light.GetComponent<HDAdditionalLightData>();
                var original = new LightSnapshot(light);
                var trial = fixture.Attach("B", new[] { light }, destroyAfterAttach ? fixture.Camera : null);
                if (destroyAfterAttach) UnityEngine.Object.DestroyImmediate(fixture.Camera.gameObject);
                trial.RefreshTargetsAtPose(new[] { light });
                Assert.That(additional.shadowFadeDistance, Is.EqualTo(75f));
                Assert.That(trial.Restored, Is.False);
                AssertDimmers(additional, 1f);
                trial.Restore();
                original.AssertAllUnchanged();
            }
        }

        [Test]
        public void DestroyedTargetsArePrunedWithinBoundedRouteDiscoveryBudget()
        {
            using (var fixture = new Fixture())
            {
                var trial = fixture.Attach("B", Array.Empty<Light>());
                for (int i = 0; i < 2; i++)
                {
                    var light = fixture.CreateLight(100f);
                    trial.RefreshTargetsAtPose(new[] { light });
                    Assert.That(trial.TrackedCount, Is.EqualTo(1));
                    UnityEngine.Object.DestroyImmediate(light.gameObject);
                    Assert.DoesNotThrow(() => trial.RefreshTargetsAtPose(Array.Empty<Light>()));
                    Assert.That(trial.TrackedCount, Is.Zero);
                    Assert.That(trial.EligibleCount, Is.Zero);
                }
                Assert.Throws<InvalidOperationException>(() => trial.RefreshTargetsAtPose(Array.Empty<Light>()));
                Assert.That(trial.WorldDiscoveryPasses, Is.Zero);
                Assert.DoesNotThrow(trial.Restore);
                Assert.DoesNotThrow(trial.Dispose);
            }
        }

        [Test]
        public void NativeRestorePreservesDimmersItDoesNotOwn()
        {
            using (var fixture = new Fixture())
            {
                var light = fixture.CreateLight(100f);
                var additional = light.GetComponent<HDAdditionalLightData>();
                var trial = fixture.Attach("B", new[] { light });
                additional.shadowDimmer = 0.2f;
                additional.volumetricShadowDimmer = 0.3f;
                trial.Restore();
                Assert.That(additional.shadowFadeDistance, Is.EqualTo(140f));
                Assert.That(additional.shadowDimmer, Is.EqualTo(0.2f));
                Assert.That(additional.volumetricShadowDimmer, Is.EqualTo(0.3f));
            }
        }

        [Test]
        public void RestoreAndDisposeAreIdempotentAndDoNotOverwriteSubsequentChanges()
        {
            using (var fixture = new Fixture())
            {
                var light = fixture.CreateLight(100f);
                var additional = light.GetComponent<HDAdditionalLightData>();
                var original = new LightSnapshot(light);
                var trial = fixture.Attach("B", new[] { light });
                trial.Restore();
                original.AssertAllUnchanged();
                additional.shadowFadeDistance = 23f;
                additional.shadowDimmer = 0.2f;
                additional.volumetricShadowDimmer = 0.3f;
                var afterRestore = new LightSnapshot(light);
                trial.Restore();
                trial.Dispose();
                trial.Dispose();
                trial.RefreshTargetsAtPose(new[] { light });
                afterRestore.AssertAllUnchanged();
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("unknown")]
        [TestCase("C")]
        public void InvalidModeThrowsBeforeChangingTargets(string mode)
        {
            using (var fixture = new Fixture())
            {
                var light = fixture.CreateLight(100f);
                var original = new LightSnapshot(light);
                Assert.Throws<ArgumentException>(() => PunctualShadowDistanceTrial.Attach(fixture.Camera, mode, new[] { light }));
                original.AssertAllUnchanged();
            }
        }

        [Test]
        public void FailedInitialDiscoveryRestoresAlreadyModifiedTargets()
        {
            using (var fixture = new Fixture())
            {
                var light = fixture.CreateLight(100f);
                var original = new LightSnapshot(light);
                Assert.Throws<InvalidOperationException>(() => PunctualShadowDistanceTrial.Attach(fixture.Camera, "B", new ThrowingTargets(light)));
                original.AssertAllUnchanged();
            }
        }

        [TestCase("A")]
        [TestCase("B")]
        public void ShadowArgumentsAcceptOnlyExplicitModesWithFixedGroupingAndNativeSubmission(string mode)
        {
            Assert.That(ParseMode(new[] { "-punctual-shadow-mode", mode, "-location-batching", "U", "-instance-native-submission", "A" }), Is.EqualTo(mode));
        }

        [TestCase("-location-batching", "G1")]
        [TestCase("-instance-native-submission", "B")]
        [TestCase("-punctual-shadow-mode", "C")]
        [TestCase("-anomaly-visual", "A")]
        [TestCase("-shadow-only-mode", "B")]
        [TestCase("-benchmark-pix-diagnostic", "")]
        [TestCase("-punctual-shadow-capture", "")]
        public void ShadowArgumentsRejectIncompatibleExperimentsAndUnqualifiedCapture(string option, string value)
        {
            var arguments = new List<string> { "-punctual-shadow-mode", "A", "-location-batching", "U", "-instance-native-submission", "A" };
            int index = arguments.IndexOf(option);
            if (index >= 0)
            {
                arguments[index + 1] = value;
            }
            else
            {
                arguments.Add(option);
                if (value.Length > 0) arguments.Add(value);
            }
            var error = Assert.Throws<TargetInvocationException>(() => ParseMode(arguments.ToArray()));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
        }

        [TestCase(null)]
        [TestCase("-punctual-shadow-mode")]
        [TestCase("-punctual-shadow-technical")]
        [TestCase("-punctual-shadow-capture")]
        public void OrdinaryRouteKeepsNoShadowModeAndOrphanDiagnosticsAreRejected(string option)
        {
            if (option == null)
            {
                Assert.That(ParseMode(Array.Empty<string>()), Is.Null);
                return;
            }
            var error = Assert.Throws<TargetInvocationException>(() => ParseMode(new[] { option }));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
        }

        private static string ParseMode(string[] arguments)
        {
            var method = typeof(CleanReleaseRouteController).GetMethod("ParsePunctualShadowMode", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (string)method.Invoke(null, new object[] { arguments });
        }

        private static void AssertDimmers(HDAdditionalLightData additional, float factor)
        {
            Assert.That(additional.shadowDimmer, Is.EqualTo(ORIGINAL_DIMMER * factor).Within(TOLERANCE));
            Assert.That(additional.volumetricShadowDimmer, Is.EqualTo(ORIGINAL_VOLUMETRIC_DIMMER * factor).Within(TOLERANCE));
        }

        private sealed class ThrowingTargets : IReadOnlyList<Light>
        {
            private readonly Light _light;

            public int Count { get; } = 2;
            public Light this[int index]
            {
                get
                {
                    if (index == 0) return _light;
                    throw new InvalidOperationException("Fixture discovery failure.");
                }
            }

            public ThrowingTargets(Light light)
            {
                _light = light;
            }

            public IEnumerator<Light> GetEnumerator()
            {
                yield return _light;
                throw new InvalidOperationException("Fixture discovery failure.");
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _root;
            private readonly List<PunctualShadowDistanceTrial> _trials = new List<PunctualShadowDistanceTrial>();

            public Camera Camera { get; }

            public Fixture()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                _root = new GameObject(nameof(PunctualShadowDistanceTrialTests));
                SceneManager.MoveGameObjectToScene(_root, _scene);
                _root.hideFlags = HideFlags.HideAndDontSave;
                var cameraObject = new GameObject("Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, _scene);
                cameraObject.transform.SetParent(_root.transform, false);
                Camera = cameraObject.AddComponent<Camera>();
                Camera.enabled = false;
            }

            public Light CreateLight(float distance, LightType type = LightType.Point, bool includeAdditional = true)
            {
                var lightObject = new GameObject("Fixture light");
                SceneManager.MoveGameObjectToScene(lightObject, _scene);
                lightObject.transform.SetParent(_root.transform, false);
                lightObject.transform.localPosition = Vector3.forward * distance;
                var light = lightObject.AddComponent<Light>();
                light.type = type;
                var additional = light.GetComponent<HDAdditionalLightData>();
                if (includeAdditional && additional == null)
                {
                    additional = lightObject.AddComponent<HDAdditionalLightData>();
                }
                if (!includeAdditional && additional != null)
                {
                    UnityEngine.Object.DestroyImmediate(additional);
                }
                if (includeAdditional)
                {
                    additional.affectsVolumetric = true;
                    additional.shadowFadeDistance = 140f;
                    additional.shadowDimmer = ORIGINAL_DIMMER;
                    additional.volumetricShadowDimmer = ORIGINAL_VOLUMETRIC_DIMMER;
                }
                light.enabled = true;
                light.intensity = 9.5f;
                light.range = 13f;
                light.color = new Color(0.25f, 0.5f, 0.75f, 1f);
                light.shadows = LightShadows.Soft;
                light.cullingMask = 0x1234;
                light.renderingLayerMask = 0x15;
                return light;
            }

            public PunctualShadowDistanceTrial Attach(string mode, IReadOnlyList<Light> targets)
            {
                return Attach(mode, targets, Camera);
            }

            public PunctualShadowDistanceTrial Attach(string mode, IReadOnlyList<Light> targets, Camera camera)
            {
                var trial = PunctualShadowDistanceTrial.Attach(camera, mode, targets);
                _trials.Add(trial);
                return trial;
            }

            public void Dispose()
            {
                foreach (var trial in _trials)
                {
                    trial.Dispose();
                }
                UnityEngine.Object.DestroyImmediate(_root);
                EditorSceneManager.ClosePreviewScene(_scene);
            }
        }

        private sealed class LightSnapshot
        {
            private readonly Light _light;
            private readonly HDAdditionalLightData _additional;
            private readonly bool _enabled;
            private readonly float _intensity;
            private readonly float _range;
            private readonly Color _color;
            private readonly LightShadows _shadows;
            private readonly LightType _type;
            private readonly int _cullingMask;
            private readonly int _renderingLayerMask;
            private readonly float _shadowFadeDistance;
            private readonly float _shadowDimmer;
            private readonly float _volumetricShadowDimmer;

            public LightSnapshot(Light light)
            {
                _light = light;
                _additional = light.GetComponent<HDAdditionalLightData>();
                _enabled = light.enabled;
                _intensity = light.intensity;
                _range = light.range;
                _color = light.color;
                _shadows = light.shadows;
                _type = light.type;
                _cullingMask = light.cullingMask;
                _renderingLayerMask = light.renderingLayerMask;
                if (_additional != null)
                {
                    _shadowFadeDistance = _additional.shadowFadeDistance;
                    _shadowDimmer = _additional.shadowDimmer;
                    _volumetricShadowDimmer = _additional.volumetricShadowDimmer;
                }
            }

            public void AssertLightUnchanged()
            {
                Assert.That(_light.enabled, Is.EqualTo(_enabled));
                Assert.That(_light.intensity, Is.EqualTo(_intensity));
                Assert.That(_light.range, Is.EqualTo(_range));
                Assert.That(_light.color, Is.EqualTo(_color));
                Assert.That(_light.shadows, Is.EqualTo(_shadows));
                Assert.That(_light.type, Is.EqualTo(_type));
                Assert.That(_light.cullingMask, Is.EqualTo(_cullingMask));
                Assert.That(_light.renderingLayerMask, Is.EqualTo(_renderingLayerMask));
            }

            public void AssertAllUnchanged()
            {
                AssertLightUnchanged();
                Assert.That(_light.GetComponent<HDAdditionalLightData>(), Is.EqualTo(_additional));
                if (_additional != null)
                {
                    Assert.That(_additional.shadowFadeDistance, Is.EqualTo(_shadowFadeDistance));
                    Assert.That(_additional.shadowDimmer, Is.EqualTo(_shadowDimmer));
                    Assert.That(_additional.volumetricShadowDimmer, Is.EqualTo(_volumetricShadowDimmer));
                }
            }
        }
    }
}
