#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.Player;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class CarouselAssetTests
    {
        private const string ROOT = "Assets/_Source/Anomalies/Carousel";
        private const string PREFAB_PATH = ROOT + "/Carousel.prefab";
        private const string PROFILE_PATH = ROOT + "/Src/CarouselInfluence.asset";
        private const string VORTEX_PROFILE_PATH = "Assets/_Source/Anomalies/Vortex/Src/VortexInfluence.asset";

        [Test]
        public void PlayerTriggerUsesSourceCycleAndIgnoresSmallBolts()
        {
            GameObject prefab = Prefab();
            SphereCollider volume = prefab.GetComponent<SphereCollider>();
            Assert.That(volume, Is.Not.Null);
            Assert.That(volume.isTrigger, Is.True);
            Assert.That(volume.radius, Is.EqualTo(5f));
            Assert.That(volume.center, Is.EqualTo(Vector3.zero));
            Rigidbody body = prefab.GetComponent<Rigidbody>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.isKinematic, Is.True);
            Assert.That(body.useGravity, Is.False);
            AnomalyPlayerReaction reaction = prefab.GetComponent<AnomalyPlayerReaction>();
            Assert.That(reaction, Is.Not.Null);
            Assert.That(reaction.InfluenceProfile, Is.SameAs(Profile()));
            var serialized = new SerializedObject(reaction);
            Assert.That(serialized.FindProperty("_awakingTime").floatValue, Is.EqualTo(0.05f).Within(0.000001f));
            Assert.That(serialized.FindProperty("_blowoutTime").floatValue, Is.EqualTo(10f));
            Assert.That(serialized.FindProperty("_accumulateTime").floatValue, Is.EqualTo(4f));
            Assert.That(serialized.FindProperty("_ignoreSmallObjects").boolValue, Is.True);
            Assert.That(serialized.FindProperty("_smallObjectIgnoreDelay").floatValue, Is.EqualTo(-1f));
            Assert.That(serialized.FindProperty("_visual").objectReferenceValue, Is.SameAs(prefab.GetComponent<OverlappingAnomalyVisual>()));
        }

        [Test]
        public void ProfileUsesSharedMincerResponseAndOriginalCameraAssets()
        {
            AnomalyInfluenceProfile profile = Profile();
            var original = AssetDatabase.LoadAssetAtPath<AnomalyInfluenceProfile>(VORTEX_PROFILE_PATH);
            Assert.That(original, Is.Not.Null);
            Assert.That(profile.Kind, Is.EqualTo(AnomalyInfluenceKind.Vortex));
            Assert.That(profile.PullStrength, Is.EqualTo(600f));
            Assert.That(profile.ActorBlowoutRadius, Is.EqualTo(0.6f).Within(0.000001f));
            Assert.That(profile.PulseDelay, Is.EqualTo(5.999f).Within(0.000001f));
            Assert.That(profile.LaunchHeight, Is.Zero);
            Assert.That(profile.InherentPostProcess, Is.SameAs(original.InherentPostProcess));
            Assert.That(profile.Earthquake, Is.SameAs(original.Earthquake));
            Assert.That(profile.ImpactPostProcess, Is.SameAs(original.ImpactPostProcess));
            Assert.That(profile.ImpactAnimations, Is.EqualTo(original.ImpactAnimations));
            Assert.That(profile.ImpactAnimations.Length, Is.EqualTo(3));
            Assert.That(profile.RadiusMin, Is.EqualTo(0.7f).Within(0.000001f));
            Assert.That(profile.RadiusMax, Is.EqualTo(1f));
        }

        [Test]
        public void PrefabUsesNativeParticlesResolvedMaterialsAndNoInventedLight()
        {
            GameObject prefab = Prefab();
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<XRayParticleGroupPlayer>(true), Is.Empty);
            ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystemRenderer[] renderers = prefab.GetComponentsInChildren<ParticleSystemRenderer>(true);
            Assert.That(systems.Length, Is.EqualTo(47));
            Assert.That(renderers.Length, Is.EqualTo(systems.Length));
            foreach (ParticleSystem system in systems)
            {
                Assert.That(system.main.playOnAwake, Is.False, system.name);
            }
            int enabledRenderers = 0;
            foreach (ParticleSystemRenderer renderer in renderers)
            {
                if (renderer.enabled == false)
                {
                    Assert.That(renderer.GetComponent<ParticleSystem>().emission.enabled, Is.False, renderer.name);
                    continue;
                }
                enabledRenderers++;
                Assert.That(renderer.sharedMaterials.Length, Is.GreaterThan(0), renderer.name);
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null, renderer.name);
                    Assert.That(material.shader, Is.Not.Null, material.name);
                    Assert.That(material.shader.name, Does.Not.Contain("InternalErrorShader"), material.name);
                    Assert.That(material.HasProperty("_MainTex"), Is.True, material.name);
                    Assert.That(material.GetTexture("_MainTex"), Is.Not.Null, material.name);
                    int textures = 0;
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture texture = material.GetTexture(property);
                        if (texture != null)
                        {
                            Assert.That(texture.width, Is.GreaterThan(0), material.name + "/" + property);
                            Assert.That(texture.height, Is.GreaterThan(0), material.name + "/" + property);
                            textures++;
                        }
                    }
                    Assert.That(textures, Is.GreaterThan(0), material.name);
                }
            }
            Assert.That(enabledRenderers, Is.EqualTo(40));
        }

        [Test]
        public void PhaseAndContactTemplatesPreserveOriginalSmallAndBigBranches()
        {
            GameObject prefab = Prefab();
            OverlappingAnomalyVisual visual = prefab.GetComponent<OverlappingAnomalyVisual>();
            Assert.That(visual, Is.Not.Null);
            var serialized = new SerializedObject(visual);
            Assert.That(serialized.FindProperty("_keepIdleDuringDischarge").boolValue, Is.True);
            Assert.That(serialized.FindProperty("_dischargeEffectDelay").floatValue, Is.EqualTo(0.05f).Within(0.000001f));
            Assert.That(serialized.FindProperty("_groupDuration").floatValue, Is.EqualTo(10f));
            Assert.That(serialized.FindProperty("_dischargeLight").objectReferenceValue, Is.Null);
            ParticleSystem awake = serialized.FindProperty("_awaking").objectReferenceValue as ParticleSystem;
            Assert.That(awake, Is.Not.Null);
            Assert.That(awake.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(3));
            GameObject discharge = serialized.FindProperty("_dischargeTemplate").objectReferenceValue as GameObject;
            Assert.That(discharge, Is.Not.Null);
            Assert.That(discharge.activeSelf, Is.False);
            Assert.That(discharge.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(23));
            AnomalyContactEffects body = Contact(serialized, "_contactEffects");
            AnomalyContactEffects idle = Contact(serialized, "_idleContactEffects");
            AnomalyContactEffects entrance = Contact(serialized, "_entranceContactEffects");
            AnomalyContactEffects smallEntrance = Contact(serialized, "_smallEntranceContactEffects");
            AnomalyContactEffects smallIdle = Contact(serialized, "_smallIdleContactEffects");
            Assert.That(body, Is.SameAs(idle));
            Assert.That(entrance, Is.Not.SameAs(smallEntrance));
            Assert.That(idle, Is.Not.SameAs(smallIdle));
            foreach (AnomalyContactEffects contact in new[] { body, entrance, smallEntrance, smallIdle })
            {
                var data = new SerializedObject(contact);
                GameObject template = data.FindProperty("_template").objectReferenceValue as GameObject;
                Assert.That(template, Is.Not.Null, contact.name);
                Assert.That(template.activeSelf, Is.False, contact.name);
                Assert.That(template.GetComponentsInChildren<ParticleSystem>(true).Length, Is.GreaterThan(0), contact.name);
            }
            var bodyData = new SerializedObject(body);
            GameObject bodyTemplate = bodyData.FindProperty("_template").objectReferenceValue as GameObject;
            Assert.That(bodyTemplate.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(1));
            Assert.That(bodyTemplate.GetComponentInChildren<ParticleSystem>(true).emission.enabled, Is.False);
        }

        [Test]
        public void AudioChannelsUseFourSourceBanksAndOnlyIdleLoops()
        {
            GameObject prefab = Prefab();
            AnomalyAudio audio = prefab.GetComponent<AnomalyAudio>();
            Assert.That(audio, Is.Not.Null);
            var serialized = new SerializedObject(audio);
            Assert.That(serialized.FindProperty("_blowoutDelay").floatValue, Is.EqualTo(0.1f).Within(0.000001f));
            var clips = new HashSet<AudioClip>();
            foreach (string field in new[] { "_idle", "_blowout", "_awaking", "_accumulate", "_entrance", "_hit" })
            {
                AudioSource source = serialized.FindProperty(field).objectReferenceValue as AudioSource;
                Assert.That(source, Is.Not.Null, field);
                Assert.That(source.clip, Is.Not.Null, field);
                Assert.That(source.loop, Is.EqualTo(field == "_idle"), field);
                Assert.That(source.playOnAwake, Is.False, field);
                Assert.That(source.spatialBlend, Is.EqualTo(1f), field);
                Assert.That(source.clip.channels, Is.EqualTo(1), field);
                Assert.That(source.clip.frequency, Is.EqualTo(44100), field);
                clips.Add(source.clip);
            }
            Assert.That(prefab.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(6));
            Assert.That(clips.Count, Is.EqualTo(4));
            AssertAudio(serialized, "_idle", "gravitational_strong_idle", 2.2f, 50f, 1.35f);
            AssertAudio(serialized, "_blowout", "gravitational_strong_blowout", 5.5f, 75f, 2f);
            AssertAudio(serialized, "_awaking", "gravitational_strong_shield", 4f, 50f, 2f);
            AssertAudio(serialized, "_entrance", "gravitational_strong_shield", 4f, 50f, 2f);
            AssertAudio(serialized, "_hit", "gravitational_strong_shield", 4f, 50f, 2f);
            AssertAudio(serialized, "_accumulate", "gravi_idle01", 3f, 50f, 1f);
        }

        [Test]
        public void ProximityAndParticleSinksCannotExpandTheActivationTrigger()
        {
            GameObject prefab = Prefab();
            Rigidbody body = prefab.GetComponent<Rigidbody>();
            AnomalyPlayerReaction reaction = prefab.GetComponent<AnomalyPlayerReaction>();
            AnomalyProximityZone proximity = prefab.GetComponentInChildren<AnomalyProximityZone>(true);
            Assert.That(proximity, Is.Not.Null);
            SphereCollider proximityVolume = proximity.GetComponent<SphereCollider>();
            Assert.That(proximityVolume, Is.Not.Null);
            Assert.That(proximityVolume.radius, Is.EqualTo(12f));
            var serialized = new SerializedObject(proximity);
            Assert.That(serialized.FindProperty("_source").objectReferenceValue, Is.SameAs(reaction));
            Assert.That(serialized.FindProperty("_sourceVolume").objectReferenceValue, Is.SameAs(prefab.GetComponent<SphereCollider>()));
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (collider.transform == prefab.transform || collider.gameObject == proximity.gameObject)
                {
                    continue;
                }
                Assert.That(collider.isTrigger, Is.True, collider.name);
                Rigidbody sinkBody = collider.GetComponent<Rigidbody>();
                Assert.That(sinkBody, Is.Not.Null, collider.name);
                Assert.That(sinkBody, Is.Not.SameAs(body), collider.name);
                Assert.That(sinkBody.isKinematic, Is.True, collider.name);
                Assert.That(collider.excludeLayers.value, Is.EqualTo(-1), collider.name);
            }
        }

        private static GameObject Prefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert.That(prefab, Is.Not.Null, PREFAB_PATH);
            return prefab;
        }

        private static AnomalyInfluenceProfile Profile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<AnomalyInfluenceProfile>(PROFILE_PATH);
            Assert.That(profile, Is.Not.Null, PROFILE_PATH);
            return profile;
        }

        private static AnomalyContactEffects Contact(SerializedObject serialized, string field)
        {
            AnomalyContactEffects contact = serialized.FindProperty(field).objectReferenceValue as AnomalyContactEffects;
            Assert.That(contact, Is.Not.Null, field);
            return contact;
        }

        private static void AssertAudio(SerializedObject serialized, string field, string name, float minimum, float maximum, float gain)
        {
            AudioSource source = serialized.FindProperty(field).objectReferenceValue as AudioSource;
            Assert.That(source.clip.name, Is.EqualTo(name));
            Assert.That(source.minDistance, Is.EqualTo(minimum).Within(0.00001f));
            Assert.That(source.maxDistance, Is.EqualTo(maximum));
            Assert.That(source.volume, Is.EqualTo(1f));
            AnomalyAudioGain boost = source.GetComponent<AnomalyAudioGain>();
            if (gain > 1f)
            {
                Assert.That(boost, Is.Not.Null, field);
                Assert.That(boost.Gain, Is.EqualTo(gain).Within(0.000001f), field);
            }
            else
            {
                Assert.That(boost, Is.Null, field);
            }
        }
    }
}
#endif

