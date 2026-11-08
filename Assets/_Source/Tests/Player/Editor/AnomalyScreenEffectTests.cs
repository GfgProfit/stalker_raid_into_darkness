using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyScreenEffectTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _previewScene;
            private readonly GameObject _root;

            public StalkerAnomalyScreenEffects Effects { get; }
            public AnomalyPostProcessAsset Asset { get; }

            public Rig()
            {
                _previewScene = EditorSceneManager.NewPreviewScene();
                _root = new GameObject(nameof(AnomalyScreenEffectTests));
                SceneManager.MoveGameObjectToScene(_root, _previewScene);
                _root.hideFlags = HideFlags.HideAndDontSave;
                Effects = _root.AddComponent<StalkerAnomalyScreenEffects>();
                Effects.Configure(null, null);
                Asset = ScriptableObject.CreateInstance<AnomalyPostProcessAsset>();
                Asset.hideFlags = HideFlags.HideAndDontSave;
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_root);
                UnityEngine.Object.DestroyImmediate(Asset);
                EditorSceneManager.ClosePreviewScene(_previewScene);
            }
        }

        [Test]
        public void OneShotFinishesWithoutLoopingOrWaitingForRemovalFade()
        {
            using (var rig = new Rig())
            {
                rig.Asset.SetImportData("test", 0.25f, new AnimationCurve[AnomalyPostProcessAsset.CHANNEL_COUNT], null);
                rig.Effects.PlayOnce(11, rig.Asset, 1f);
                rig.Effects.Tick(0.24f);
                Assert.That(rig.Effects.ActiveLayerCount, Is.EqualTo(1));
                rig.Effects.Tick(0.02f);
                Assert.That(rig.Effects.ActiveLayerCount, Is.Zero);
            }
        }

        [Test]
        public void NoncyclicEnvelopeClampsItsFinalKeyWhileIdleLayersRepeat()
        {
            using (var rig = new Rig())
            {
                var channels = new AnimationCurve[AnomalyPostProcessAsset.CHANNEL_COUNT];
                channels[AnomalyPostProcessAsset.BLUR] = AnimationCurve.Linear(0f, 1f, 0.25f, 0f);
                rig.Asset.SetImportData("test", 0.25f, channels, null);
                Assert.That(rig.Asset.Evaluate(AnomalyPostProcessAsset.BLUR, 0.5f, false), Is.Zero);
                Assert.That(rig.Asset.Evaluate(AnomalyPostProcessAsset.BLUR, 0.5f), Is.EqualTo(1f));
            }
        }

        [Test]
        public void DifferentIdsKeepIndependentLayerExpiries()
        {
            using (var rig = new Rig())
            {
                rig.Effects.Refresh(11, rig.Asset, 1.0f, 0.1f);
                rig.Effects.Refresh(22, rig.Asset, 1.0f, 5.0f);
                Assert.That(rig.Effects.ActiveLayerCount, Is.EqualTo(2));

                rig.Effects.Tick(1.1f);

                Assert.That(rig.Effects.ActiveLayerCount, Is.EqualTo(1));
                rig.Effects.RemoveImmediately(22);
                Assert.That(rig.Effects.ActiveLayerCount, Is.Zero);
            }
        }

        [Test]
        public void RefreshingTheSameIdExtendsExpiryWithoutAddingALayer()
        {
            using (var rig = new Rig())
            {
                rig.Effects.Refresh(11, rig.Asset, 0.5f, 0.1f);
                rig.Effects.Tick(0.05f);

                rig.Effects.Refresh(11, rig.Asset, 0.5f, 1.0f);
                Assert.That(rig.Effects.ActiveLayerCount, Is.EqualTo(1));
                rig.Effects.Tick(0.75f);
                Assert.That(rig.Effects.ActiveLayerCount, Is.EqualTo(1));
                rig.Effects.Tick(0.6f);

                Assert.That(rig.Effects.ActiveLayerCount, Is.Zero);
            }
        }

        [Test]
        public void RemoveFadesANonunitFactorAtOneUnitPerSecond()
        {
            using (var rig = new Rig())
            {
                rig.Effects.Refresh(11, rig.Asset, 0.75f, 10.0f);

                rig.Effects.Remove(11);
                rig.Effects.Tick(0.74f);
                Assert.That(rig.Effects.ActiveLayerCount, Is.EqualTo(1));
                rig.Effects.Tick(0.02f);

                Assert.That(rig.Effects.ActiveLayerCount, Is.Zero);
            }
        }

        [Test]
        public void RemoveImmediatelyOnlyRemovesTheSpecifiedId()
        {
            using (var rig = new Rig())
            {
                rig.Effects.Refresh(11, rig.Asset, 1.0f, 10.0f);
                rig.Effects.Refresh(22, rig.Asset, 1.0f, 10.0f);

                rig.Effects.RemoveImmediately(11);
                Assert.That(rig.Effects.ActiveLayerCount, Is.EqualTo(1));
                rig.Effects.RemoveImmediately(11);
                Assert.That(rig.Effects.ActiveLayerCount, Is.EqualTo(1));
                rig.Effects.RemoveImmediately(22);

                Assert.That(rig.Effects.ActiveLayerCount, Is.Zero);
            }
        }

        [Test]
        public void ClearRemovesEveryLayer()
        {
            using (var rig = new Rig())
            {
                rig.Effects.Refresh(11, rig.Asset, 1.0f, 10.0f);
                rig.Effects.Refresh(22, rig.Asset, 0.75f, 10.0f);

                rig.Effects.Clear();
                rig.Effects.Tick(0.1f);

                Assert.That(rig.Effects.ActiveLayerCount, Is.Zero);
            }
        }

        [Test]
        public void OnDisableClearsEveryLayer()
        {
            using (var rig = new Rig())
            {
                rig.Effects.Refresh(11, rig.Asset, 1.0f, 10.0f);
                rig.Effects.Refresh(22, rig.Asset, 0.75f, 10.0f);
                MethodInfo onDisable = typeof(StalkerAnomalyScreenEffects).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onDisable, Is.Not.Null);

                onDisable.Invoke(rig.Effects, null);

                Assert.That(rig.Effects.ActiveLayerCount, Is.Zero);
            }
        }
    }
}
