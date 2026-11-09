using System.Collections.Generic;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class SpatialBubbleAssetTests
    {
        private const string ROOT = "Assets/_Source/Anomalies/SpatialBubble";
        private const string PREFAB = ROOT + "/Src/Prefabs/SpatialBubbleSource.prefab";

        [Test]
        public void SourceSphereAndVisualOffsetRemainSeparateFromCandidateDetection()
        {
            GameObject prefab = Load();
            SpatialBubbleTeleport teleport = prefab.GetComponent<SpatialBubbleTeleport>();
            Assert.That(teleport, Is.Not.Null);
            Assert.That(teleport.Destination, Is.Null);
            SphereCollider source = prefab.GetComponent<SphereCollider>();
            Assert.That(teleport.ActivationVolume, Is.SameAs(source));
            Assert.That(source.isTrigger, Is.True);
            Assert.That(source.radius, Is.EqualTo(2f));
            Assert.That(source.center, Is.EqualTo(Vector3.zero));
            SphereCollider detection = prefab.transform.Find("PlayerDetection").GetComponent<SphereCollider>();
            Assert.That(detection.isTrigger, Is.True);
            Assert.That(detection.radius, Is.EqualTo(2.5f));
            Rigidbody body = prefab.GetComponent<Rigidbody>();
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.useGravity, Is.False);
            Transform visuals = prefab.transform.Find("SourceVisualOffset");
            Assert.That(visuals, Is.Not.Null);
            Assert.That(visuals.parent, Is.SameAs(prefab.transform));
            Assert.That(visuals, Is.Not.SameAs(source.transform));
            Assert.That(float.IsNaN(visuals.localPosition.sqrMagnitude) || float.IsInfinity(visuals.localPosition.sqrMagnitude), Is.False);
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<AnomalyPlayerReaction>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<AnomalyProximityZone>(true), Is.Empty);
        }

        [Test]
        public void TeleportUsesOriginalNonloopingPostProcessAndUnattenuatedSound()
        {
            GameObject prefab = Load();
            var data = new SerializedObject(prefab.GetComponent<SpatialBubbleTeleport>());
            var effect = data.FindProperty("_teleportPostProcess").objectReferenceValue as AnomalyPostProcessAsset;
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.SourceFile, Is.EqualTo("anims/teleport.ppe"));
            Assert.That(effect.Duration, Is.EqualTo(3f));
            Assert.That(effect.ColorMap, Is.Null);
            for (int channel = 0; channel < AnomalyPostProcessAsset.CHANNEL_COUNT; channel++)
            {
                foreach (float time in new[] { 0f, 0.9f, effect.Duration })
                {
                    float value = effect.Evaluate(channel, time, false);
                    Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False, channel + "/" + time);
                }
            }
            AudioSource[] sources = prefab.GetComponentsInChildren<AudioSource>(true);
            Assert.That(sources.Length, Is.EqualTo(1));
            AudioSource audio = sources[0];
            Assert.That(data.FindProperty("_teleportAudio").objectReferenceValue, Is.SameAs(audio));
            Assert.That(audio.clip, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(audio.clip), Is.EqualTo(ROOT + "/Src/Audio/tinnitus3a.ogg"));
            Assert.That(audio.clip.samples, Is.GreaterThan(0));
            Assert.That(audio.spatialBlend, Is.Zero);
            Assert.That(audio.volume, Is.EqualTo(1f));
            Assert.That(audio.pitch, Is.EqualTo(1f));
            Assert.That(audio.loop, Is.False);
            Assert.That(audio.playOnAwake, Is.False);
        }

        [Test]
        public void NativeLayersHaveResolvedMaterialsCompatibleCurvesAndNoSourcePlayers()
        {
            GameObject prefab = Load();
            ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(systems.Length, Is.EqualTo(25));
            Assert.That(prefab.GetComponentsInChildren<XRayParticleGroupPlayer>(true), Is.Empty);
            int rendered = 0;
            foreach (ParticleSystem system in systems)
            {
                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.enabled == false)
                {
                    continue;
                }
                rendered++;
                Assert.That(system.main.playOnAwake, Is.True, system.name);
                Assert.That(system.main.loop, Is.True, system.name);
                Assert.That(renderer.sharedMaterial, Is.Not.Null, system.name);
                Assert.That(renderer.sharedMaterial.shader, Is.Not.Null, system.name);
                Assert.That(renderer.sharedMaterial.mainTexture, Is.Not.Null, system.name);
                Assert.That(system.main.maxParticles, Is.GreaterThan(0), system.name);
                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                if (velocity.enabled)
                {
                    AssertTriplet(velocity.x, velocity.y, velocity.z, system.name + "/linear");
                    AssertTriplet(velocity.orbitalX, velocity.orbitalY, velocity.orbitalZ, system.name + "/orbital");
                    AssertTriplet(velocity.orbitalOffsetX, velocity.orbitalOffsetY, velocity.orbitalOffsetZ, system.name + "/offset");
                }
            }
            Assert.That(rendered, Is.EqualTo(24));
            foreach (string dependency in AssetDatabase.GetDependencies(PREFAB, true))
            {
                string extension = System.IO.Path.GetExtension(dependency).ToLowerInvariant();
                Assert.That(extension, Is.Not.EqualTo(".pg").And.Not.EqualTo(".pe").And.Not.EqualTo(".ppe").And.Not.EqualTo(".ltx").And.Not.EqualTo(".xr"), dependency);
            }
        }

        [Test]
        public void FiniteSourceAtlasesUseCustomFramesInsteadOfNativeGridCropping()
        {
            SpatialBubbleParticleFrames[] layers = Load().GetComponentsInChildren<SpatialBubbleParticleFrames>(true);
            Assert.That(layers.Length, Is.EqualTo(12));
            foreach (SpatialBubbleParticleFrames layer in layers)
            {
                ParticleSystem system = layer.GetComponent<ParticleSystem>();
                Assert.That(system.textureSheetAnimation.enabled, Is.False, layer.name);
                var streams = new List<ParticleSystemVertexStream>();
                system.GetComponent<ParticleSystemRenderer>().GetActiveVertexStreams(streams);
                Assert.That(streams, Does.Contain(ParticleSystemVertexStream.Custom1X), layer.name);
            }
        }

        [Test]
        public void PersistentSourceLayersKeepInfiniteLifeWithoutNativeReemission()
        {
            SpatialBubblePersistentParticle[] layers = Load().GetComponentsInChildren<SpatialBubblePersistentParticle>(true);
            Assert.That(layers.Length, Is.EqualTo(7));
            foreach (SpatialBubblePersistentParticle layer in layers)
            {
                ParticleSystem system = layer.GetComponent<ParticleSystem>();
                Assert.That(float.IsPositiveInfinity(system.main.startLifetime.constant), Is.True, layer.name);
                Assert.That(system.emission.enabled, Is.False, layer.name);
                var streams = new List<ParticleSystemVertexStream>();
                system.GetComponent<ParticleSystemRenderer>().GetActiveVertexStreams(streams);
                Assert.That(streams, Does.Contain(ParticleSystemVertexStream.Custom1X), layer.name);
            }
        }

        private static void AssertTriplet(ParticleSystem.MinMaxCurve x, ParticleSystem.MinMaxCurve y, ParticleSystem.MinMaxCurve z, string context)
        {
            Assert.That(y.mode, Is.EqualTo(x.mode), context);
            Assert.That(z.mode, Is.EqualTo(x.mode), context);
        }

        private static GameObject Load()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB);
            Assert.That(prefab, Is.Not.Null);
            return prefab;
        }
    }
}
