using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyLightTestVisual : AnomalyVisual
    {
        private bool _isDischarging;

        public bool AllowDischarge { get; set; } = true;
        public override bool IsDischarging => _isDischarging;

        protected override bool HasDischarge => true;

        public void DisableVisual()
        {
            enabled = false;
            base.OnDisable();
        }

        public void EnableVisual()
        {
            enabled = true;
            base.OnEnable();
        }

        protected override bool StartDischarge()
        {
            _isDischarging = AllowDischarge;
            return AllowDischarge;
        }

        protected override void StopDischarge()
        {
            _isDischarging = false;
        }

        protected override void TickVisual(float deltaTime)
        {
        }
    }

    public sealed class AnomalyVisualLightTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _root;
            private readonly UnityEngine.Random.State _randomState;

            public AnomalyLightTestVisual Visual { get; }
            public Light Light { get; }

            public Rig()
            {
                _randomState = UnityEngine.Random.state;
                _scene = EditorSceneManager.NewPreviewScene();
                _root = new GameObject(nameof(AnomalyVisualLightTests));
                SceneManager.MoveGameObjectToScene(_root, _scene);
                _root.hideFlags = HideFlags.HideAndDontSave;
                Light = _root.AddComponent<Light>();
                Light.enabled = false;
                Light.intensity = 137f;
                Light.range = 9f;
                Visual = _root.AddComponent<AnomalyLightTestVisual>();
                SetField(Visual, "_idleIllumination", Light);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_root);
                EditorSceneManager.ClosePreviewScene(_scene);
                UnityEngine.Random.state = _randomState;
            }
        }

        [Test]
        public void IdleLightFollowsVisibilityDischargeAndComponentLifecycle()
        {
            using (var rig = new Rig())
            {
                rig.Visual.ShowIdle();
                Assert.That(rig.Light.enabled, Is.True);
                rig.Visual.PlayDischarge();
                Assert.That(rig.Visual.IsDischarging, Is.True);
                Assert.That(rig.Light.enabled, Is.False);
                rig.Visual.ShowIdle();
                Assert.That(rig.Light.enabled, Is.True);
                rig.Visual.Hide();
                Assert.That(rig.Light.enabled, Is.False);
                rig.Visual.ShowIdle();
                rig.Visual.DisableVisual();
                Assert.That(rig.Light.enabled, Is.False);
                rig.Visual.ShowIdle();
                Assert.That(rig.Light.enabled, Is.False);
                rig.Visual.EnableVisual();
                Assert.That(rig.Light.enabled, Is.True);
            }
        }

        [Test]
        public void RetainedIdleLightContinuesDuringDischargeAndStopsOnHide()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Visual, "_keepIdleDuringDischarge", true);
                rig.Visual.ShowIdle();
                rig.Visual.PlayDischarge();
                Assert.That(rig.Visual.IsDischarging, Is.True);
                Assert.That(rig.Light.enabled, Is.True);
                rig.Visual.Hide();
                Assert.That(rig.Light.enabled, Is.False);
            }
        }

        [Test]
        public void FailedDischargeRestoresPreviousIdleVisibility()
        {
            using (var rig = new Rig())
            {
                rig.Visual.AllowDischarge = false;
                rig.Visual.ShowIdle();
                rig.Visual.PlayDischarge();
                Assert.That(rig.Light.enabled, Is.True);
                rig.Visual.Hide();
                rig.Visual.PlayDischarge();
                Assert.That(rig.Visual.IsVisible, Is.False);
                Assert.That(rig.Light.enabled, Is.False);
            }
        }

        [Test]
        public void RuntimeVisualTypesHaveUniqueSerializedFieldNamesAcrossInheritance()
        {
            foreach (Type type in typeof(AnomalyVisual).Assembly.GetTypes())
            {
                if (type.IsSubclassOf(typeof(AnomalyVisual)) == false)
                {
                    continue;
                }
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (Type current = type; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
                {
                    foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (field.IsDefined(typeof(NonSerializedAttribute), false) || field.IsPublic == false && field.IsDefined(typeof(SerializeField), false) == false && field.IsDefined(typeof(SerializeReference), false) == false)
                        {
                            continue;
                        }
                        Assert.That(names.Add(field.Name), Is.True, type.FullName + ": " + field.Name);
                    }
                }
            }
        }

        [Test]
        public void NativeLightAnimationUsesIntegerFramesQuantizedRgbAndGlobalPhase()
        {
            AnomalyLightAnimation animation = GreenAnimation();
            Color firstFrame = animation.Evaluate(0.15f);
            Assert.That(firstFrame.r, Is.EqualTo(152f / 255f).Within(0.0000001f));
            Assert.That(firstFrame.g, Is.EqualTo(210f / 255f).Within(0.0000001f));
            Assert.That(firstFrame.b, Is.EqualTo(50f / 255f).Within(0.0000001f));
            Assert.That(firstFrame.a, Is.EqualTo(1f));
            Assert.That(animation.Evaluate(0.19f), Is.EqualTo(firstFrame));
            Assert.That(animation.Evaluate(4.15f), Is.EqualTo(firstFrame));
            Assert.That(GreenAnimation().Evaluate(8.15f), Is.EqualTo(firstFrame));
            Color key = animation.Evaluate(1.05f);
            Assert.That(key.r, Is.EqualTo(149f / 255f).Within(0.0000001f));
            Assert.That(key.g, Is.EqualTo(186f / 255f).Within(0.0000001f));
            Assert.That(key.b, Is.Zero);
        }

        [Test]
        public void IdleAnimationKeepsAuthoredIntensityAndUsesSourceRangeJitter()
        {
            using (var rig = new Rig())
            {
                SetField(rig.Visual, "_idleLightAnimation", GreenAnimation());
                SetField(rig.Visual, "_idleLightRange", 4f);
                SetField(rig.Visual, "_idleLightRangeJitter", 0.25f);
                rig.Visual.ShowIdle();
                MethodInfo update = typeof(AnomalyVisual).GetMethod("UpdateIdleLight", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(update, Is.Not.Null);
                for (int index = 0; index < 20; index++)
                {
                    update.Invoke(rig.Visual, new object[] { 1.05f });
                    Assert.That(rig.Light.color, Is.EqualTo(GreenAnimation().Evaluate(1.05f)));
                    Assert.That(rig.Light.intensity, Is.EqualTo(137f));
                    Assert.That(rig.Light.range, Is.InRange(3.75f, 4.25f));
                }
                rig.Visual.Hide();
                Color color = rig.Light.color;
                float range = rig.Light.range;
                update.Invoke(rig.Visual, new object[] { 0.15f });
                Assert.That(rig.Light.enabled, Is.False);
                Assert.That(rig.Light.color, Is.EqualTo(color));
                Assert.That(rig.Light.range, Is.EqualTo(range));
            }
        }

        private static AnomalyLightAnimation GreenAnimation()
        {
            return new AnomalyLightAnimation(10f, 40, new[] { 0, 10, 13, 17, 30, 39 }, new[] { new Color32(153, 213, 56, 0), new Color32(149, 186, 0, 0), new Color32(153, 213, 56, 0), new Color32(141, 193, 58, 0), new Color32(149, 186, 0, 0), new Color32(153, 213, 56, 0) });
        }

        private static void SetField(AnomalyVisual target, string name, object value)
        {
            FieldInfo field = typeof(AnomalyVisual).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
