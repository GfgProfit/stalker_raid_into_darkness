using System;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyGrassBenderTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _guardRoot;
            private readonly GameObject _providerRoot;
            private readonly GameObject _visualRoot;
            private readonly XRayFloraWindProvider _previousProvider;

            public XRayFloraWindProvider Provider { get; }
            public AnomalyLightTestVisual Visual { get; }
            public AnomalyGrassBender Bender { get; }

            public Rig()
            {
                _previousProvider = XRayFloraWindProvider.Active;
                _scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    _guardRoot = Create("InactiveRenderGuard");
                    _guardRoot.SetActive(false);
                    XRayFloraWindProvider guard = _guardRoot.AddComponent<XRayFloraWindProvider>();
                    SetActiveProvider(guard);
                    _providerRoot = Create("IsolatedRenderOwner");
                    Provider = _providerRoot.AddComponent<XRayFloraWindProvider>();
                    SetActiveProvider(Provider);
                    _visualRoot = Create(nameof(AnomalyGrassBenderTests));
                    Visual = _visualRoot.AddComponent<AnomalyLightTestVisual>();
                    Bender = _visualRoot.AddComponent<AnomalyGrassBender>();
                    typeof(AnomalyGrassBender).GetField("_visual", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Bender, Visual);
                    Invoke(Bender, "OnDisable");
                    Invoke(Bender, "OnEnable");
                    Visual.ShowIdle();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                SetActiveProvider(_previousProvider);
                UnityEngine.Object.DestroyImmediate(_visualRoot);
                UnityEngine.Object.DestroyImmediate(_providerRoot);
                UnityEngine.Object.DestroyImmediate(_guardRoot);
                EditorSceneManager.ClosePreviewScene(_scene);
            }

            private GameObject Create(string name)
            {
                GameObject root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, _scene);
                root.hideFlags = HideFlags.HideAndDontSave;
                return root;
            }
        }

        [Test]
        public void VisualPulsesOverlapAndHideOrDisableReleasesOwnedRenderState()
        {
            XRayFloraWindProvider previous = XRayFloraWindProvider.Active;
            float active = Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY);
            Vector4 animation = Shader.GetGlobalVector(XRayFloraWindState.ANIM_PROPERTY);
            int benders = Shader.GetGlobalInteger(XRayGrassBenderState.COUNT_PROPERTY);
            using (var rig = new Rig())
            {
                rig.Visual.PlayDischarge();
                rig.Provider.GrassBenders.Advance(0.1f);
                rig.Visual.PlayDischarge();
                Assert.That(rig.Provider.GrassBenders.ActiveCount, Is.EqualTo(2));
                rig.Visual.Hide();
                Assert.That(rig.Provider.GrassBenders.ActiveCount, Is.Zero);
                Invoke(rig.Bender, "OnDisable");
                rig.Visual.ShowIdle();
                rig.Visual.PlayDischarge();
                Assert.That(rig.Provider.GrassBenders.ActiveCount, Is.Zero);
                Invoke(rig.Bender, "OnEnable");
                rig.Visual.PlayDischarge();
                Assert.That(rig.Provider.GrassBenders.ActiveCount, Is.EqualTo(1));
                Invoke(rig.Bender, "OnDisable");
                Assert.That(rig.Provider.GrassBenders.ActiveCount, Is.Zero);
            }
            Assert.That(XRayFloraWindProvider.Active, Is.EqualTo(previous));
            Assert.That(Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY), Is.EqualTo(active));
            Assert.That(Shader.GetGlobalVector(XRayFloraWindState.ANIM_PROPERTY), Is.EqualTo(animation));
            Assert.That(Shader.GetGlobalInteger(XRayGrassBenderState.COUNT_PROPERTY), Is.EqualTo(benders));
        }

        [Test]
        public void AnomalyWithoutAnActiveFloraOwnerDoesNotCreateAGlobalGameplayService()
        {
            using (var rig = new Rig())
            {
                SetActiveProvider(null);
                rig.Visual.PlayDischarge();
                Assert.That(rig.Provider.GrassBenders.ActiveCount, Is.Zero);
                Assert.That(XRayFloraWindProvider.Active, Is.Null);
            }
        }

        private static void SetActiveProvider(XRayFloraWindProvider provider)
        {
            FieldInfo field = typeof(XRayFloraWindProvider).GetField("<Active>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(null, provider);
        }

        private static void Invoke(AnomalyGrassBender target, string name)
        {
            MethodInfo method = typeof(AnomalyGrassBender).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, null);
        }
    }
}
