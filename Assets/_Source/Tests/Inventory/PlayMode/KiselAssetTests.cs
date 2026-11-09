#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.Player;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class KiselAssetTests
    {
        private const string ROOT = "Assets/_Source/Anomalies/Kisel";
        private const string PREFAB_PATH = ROOT + "/Kisel.prefab";
        private const string PROFILE_PATH = ROOT + "/Src/KiselInfluence.asset";
        private const string GAZIROVKA_ROOT = "Assets/_Source/Anomalies/Gazirovka";

        [Test]
        public void ChemicalPrefabKeepsDistinctIdentityAndSharesGazirovkaResources()
        {
            GameObject prefab = Prefab();
            var gazirovka = AssetDatabase.LoadAssetAtPath<GameObject>(GAZIROVKA_ROOT + "/Gazirovka.prefab");
            Assert.That(gazirovka, Is.Not.Null);
            Assert.That(prefab, Is.Not.SameAs(gazirovka));
            Assert.That(AssetDatabase.AssetPathToGUID(PREFAB_PATH), Is.Not.EqualTo(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(gazirovka))));
            AnomalyInfluenceProfile profile = Profile();
            var gasProfile = AssetDatabase.LoadAssetAtPath<AnomalyInfluenceProfile>(GAZIROVKA_ROOT + "/Src/GazirovkaInfluence.asset");
            Assert.That(gasProfile, Is.Not.Null);
            Assert.That(profile, Is.Not.SameAs(gasProfile));
            Assert.That(profile.Kind, Is.EqualTo(AnomalyInfluenceKind.Chemical));
            Assert.That(profile.InherentPostProcess, Is.SameAs(gasProfile.InherentPostProcess));
            Assert.That(AssetDatabase.GetAssetPath(profile.InherentPostProcess), Is.EqualTo(GAZIROVKA_ROOT + "/Src/PostProcess/acidic_mine.asset"));
            Assert.That(profile.RadiusMin, Is.EqualTo(0.7f).Within(0.000001f));
            Assert.That(profile.RadiusMax, Is.EqualTo(1f));
            Assert.That(profile.ActorPainEnabled, Is.True);
            Assert.That(profile.Electric, Is.Null);
            Assert.That(profile.ElectricMine, Is.Null);
            Assert.That(profile.Earthquake, Is.Null);
            Assert.That(profile.ImpactPostProcess, Is.Null);
            Assert.That(profile.ImpactAnimations, Is.Null.Or.Empty);
        }

        [Test]
        public void PlayerAndBoltUseSourceCycleWithoutRandomRadiusOrPhysicalImpact()
        {
            GameObject prefab = Prefab();
            SphereCollider volume = prefab.GetComponent<SphereCollider>();
            Assert.That(volume, Is.Not.Null);
            Assert.That(volume.isTrigger, Is.True);
            Assert.That(volume.center, Is.EqualTo(Vector3.zero));
            Assert.That(volume.radius, Is.EqualTo(2f));
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Rigidbody body = prefab.GetComponent<Rigidbody>();
            if (body != null)
            {
                Assert.That(body.isKinematic, Is.True);
                Assert.That(body.useGravity, Is.False);
            }
            AnomalyPlayerReaction reaction = prefab.GetComponent<AnomalyPlayerReaction>();
            Assert.That(reaction, Is.Not.Null);
            Assert.That(reaction.InfluenceProfile, Is.SameAs(Profile()));
            var data = new SerializedObject(reaction);
            Assert.That(data.FindProperty("_awakingTime").floatValue, Is.EqualTo(0.1f).Within(0.000001f));
            Assert.That(data.FindProperty("_blowoutTime").floatValue, Is.EqualTo(1.4f).Within(0.000001f));
            Assert.That(data.FindProperty("_accumulateTime").floatValue, Is.EqualTo(0.3f).Within(0.000001f));
            Assert.That(data.FindProperty("_ignoreSmallObjects").boolValue, Is.False);
            Assert.That(data.FindProperty("_smallObjectIgnoreDelay").floatValue, Is.EqualTo(-1f));
            Assert.That(data.FindProperty("_nonlivingIdleStopDelay").floatValue, Is.EqualTo(50f));
            Assert.That(data.FindProperty("_repeatNonlivingIdleStop").boolValue, Is.True);
            Assert.That(data.FindProperty("_randomizeRadius").boolValue, Is.False);
            Assert.That(data.FindProperty("_visual").objectReferenceValue, Is.SameAs(prefab.GetComponent<OverlappingAnomalyVisual>()));
            AnomalyInfluenceProfile profile = Profile();
            Assert.That(profile.LaunchHeight, Is.Zero);
            Assert.That(profile.LaunchAirAcceleration, Is.Zero);
            Assert.That(profile.PullStrength, Is.Zero);
            Assert.That(profile.PulseDelay, Is.Zero);
            AnomalyProximityZone proximity = prefab.GetComponentInChildren<AnomalyProximityZone>(true);
            Assert.That(proximity, Is.Not.Null);
            var proximityData = new SerializedObject(proximity);
            Assert.That(proximityData.FindProperty("_source").objectReferenceValue, Is.SameAs(reaction));
            Assert.That(proximityData.FindProperty("_sourceVolume").objectReferenceValue, Is.SameAs(volume));
            Assert.That(proximity.ProximityRadius, Is.EqualTo(6f));
        }

        [Test]
        public void SharedContactGroupUsesRootBoneAndPersistentSourceLifecycle()
        {
            GameObject prefab = Prefab();
            var visual = new SerializedObject(prefab.GetComponent<OverlappingAnomalyVisual>());
            AnomalyContactEffects contact = visual.FindProperty("_contactEffects").objectReferenceValue as AnomalyContactEffects;
            Assert.That(contact, Is.Not.Null);
            Assert.That(visual.FindProperty("_idleContactEffects").objectReferenceValue, Is.SameAs(contact));
            Assert.That(prefab.GetComponentsInChildren<AnomalyContactEffects>(true).Length, Is.EqualTo(1));
            foreach (string field in new[] { "_awaking", "_entranceContactEffects", "_smallEntranceContactEffects", "_smallIdleContactEffects" })
            {
                Assert.That(visual.FindProperty(field).objectReferenceValue, Is.Null, field);
            }
            var data = new SerializedObject(contact);
            Assert.That(data.FindProperty("_useRootBone").boolValue, Is.True);
            Assert.That(data.FindProperty("_sourceEffectKey").stringValue.Replace('\\', '/'), Is.EqualTo("anomaly2/studen_idle_bottom_00"));
            Assert.That(data.FindProperty("_entranceEnabled").boolValue, Is.False);
            Assert.That(data.FindProperty("_groupDuration").floatValue, Is.Zero);
            GameObject template = data.FindProperty("_template").objectReferenceValue as GameObject;
            Assert.That(template, Is.Not.Null);
            Assert.That(template.activeSelf, Is.False);
            Assert.That(template.GetComponentsInChildren<ParticleSystem>(true), Is.Not.Empty);
            Assert.That(visual.FindProperty("_keepIdleDuringDischarge").boolValue, Is.True);
            Assert.That(visual.FindProperty("_dischargeEffectDelay").floatValue, Is.Zero);
            Assert.That(visual.FindProperty("_groupDuration").floatValue, Is.EqualTo(5f));
        }

        [Test]
        public void NativeWorldAndContactParticlesKeepSourceBranchesAndResolvedMaterials()
        {
            GameObject prefab = Prefab();
            Assert.That(prefab.GetComponentsInChildren<XRayParticleGroupPlayer>(true), Is.Empty);
            ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(systems.Length, Is.EqualTo(43));
            var expected = new HashSet<string> { "studen_idle_bottom_02", "studen_idle_bottom_08", "studen_idle_bottom_05", "studen_idle_bottom_07", "studen_idle_glow_02", "studen_idle_bottom_07_00", "studen_idle_bottom_shader", "studen_idle_top_heat_00", "anomaly_toxic_particle_test", "studen_idle_bottom_03", "studen_idle_bottom_20", "studen_idle_bottom_00", "studen_idle_bottom_12" };
            int rendered = 0;
            foreach (ParticleSystem system in systems)
            {
                Assert.That(system.main.playOnAwake, Is.False, system.name);
                string sourceName = system.name.Split(new[] { "__" }, StringSplitOptions.None)[0];
                expected.Remove(sourceName);
                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                if (velocity.enabled)
                {
                    AssertTriplet(velocity.x, velocity.y, velocity.z, system.name + "/linear");
                    AssertTriplet(velocity.orbitalX, velocity.orbitalY, velocity.orbitalZ, system.name + "/orbital");
                    AssertTriplet(velocity.orbitalOffsetX, velocity.orbitalOffsetY, velocity.orbitalOffsetZ, system.name + "/offset");
                }
                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                Assert.That(renderer, Is.Not.Null, system.name);
                if (renderer.enabled == false)
                {
                    continue;
                }
                rendered++;
                Assert.That(renderer.sharedMaterials, Is.Not.Empty, system.name);
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null, system.name);
                    Assert.That(material.shader, Is.Not.Null, system.name);
                    Assert.That(material.shader.name, Does.Not.Contain("InternalErrorShader"), system.name);
                    Assert.That(material.GetTexture("_MainTex"), Is.Not.Null, material.name);
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture texture = material.GetTexture(property);
                        if (texture != null)
                        {
                            Assert.That(texture.width, Is.GreaterThan(0), material.name + "/" + property);
                            Assert.That(texture.height, Is.GreaterThan(0), material.name + "/" + property);
                        }
                    }
                }
            }
            Assert.That(expected, Is.Empty, "Missing original studen idle/contact particle branches");
            Assert.That(rendered, Is.EqualTo(40));
            AnomalyGrassBender bender = prefab.GetComponent<AnomalyGrassBender>();
            Assert.That(bender, Is.Not.Null);
            var grass = new SerializedObject(bender);
            Assert.That(grass.FindProperty("_visual").objectReferenceValue, Is.SameAs(prefab.GetComponent<OverlappingAnomalyVisual>()));
            Assert.That(grass.FindProperty("_fade").floatValue, Is.EqualTo(1.33f).Within(0.000001f));
            Assert.That(grass.FindProperty("_speed").floatValue, Is.EqualTo(3f));
            Assert.That(grass.FindProperty("_intensity").floatValue, Is.EqualTo(1f));
            Assert.That(grass.FindProperty("_radius").floatValue, Is.EqualTo(3f));
            var allowed = new HashSet<Type> { typeof(AnomalyPlayerReaction), typeof(AnomalyProximityZone), typeof(OverlappingAnomalyVisual), typeof(AnomalyContactEffects), typeof(AnomalyGrassBender), typeof(AnomalyAudio), typeof(AnomalyAudioGain) };
            foreach (MonoBehaviour component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Assert.That(component, Is.Not.Null, "Missing prefab script");
                bool presentation = allowed.Contains(component.GetType()) || component.GetType().FullName == "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData";
                Assert.That(presentation, Is.True, component.GetType().FullName);
            }
        }

        [Test]
        public void IdleAndBlowoutLightsUseOriginalShadowAndAnimationSettings()
        {
            GameObject prefab = Prefab();
            Assert.That(prefab.GetComponentsInChildren<Light>(true).Length, Is.EqualTo(2));
            var data = new SerializedObject(prefab.GetComponent<OverlappingAnomalyVisual>());
            Light idle = data.FindProperty("_idleIllumination").objectReferenceValue as Light;
            Light discharge = data.FindProperty("_dischargeLight").objectReferenceValue as Light;
            AssertLight(idle, 4f);
            AssertLight(discharge, 7f);
            Assert.That(discharge.color, Is.EqualTo(new Color(1.1f, 1.7f, 1.4f)));
            Assert.That(data.FindProperty("_idleLightRange").floatValue, Is.EqualTo(4f));
            Assert.That(data.FindProperty("_idleLightRangeJitter").floatValue, Is.EqualTo(0.25f));
            Assert.That(data.FindProperty("_dischargeLightRange").floatValue, Is.EqualTo(7f));
            Assert.That(data.FindProperty("_dischargeLightDuration").floatValue, Is.EqualTo(0.35f).Within(0.000001f));
            Assert.That(data.FindProperty("_dischargeLightDelay").floatValue, Is.Zero);
            var gasData = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(GAZIROVKA_ROOT + "/Gazirovka.prefab").GetComponent<OverlappingAnomalyVisual>());
            SerializedProperty animation = data.FindProperty("_idleLightAnimation");
            SerializedProperty original = gasData.FindProperty("_idleLightAnimation");
            Assert.That(animation.FindPropertyRelative("_fps").floatValue, Is.EqualTo(10f));
            Assert.That(animation.FindPropertyRelative("_frameCount").intValue, Is.EqualTo(40));
            foreach (string field in new[] { "_frames", "_colors" })
            {
                SerializedProperty keys = animation.FindPropertyRelative(field);
                SerializedProperty sourceKeys = original.FindPropertyRelative(field);
                Assert.That(keys.arraySize, Is.EqualTo(6));
                Assert.That(keys.arraySize, Is.EqualTo(sourceKeys.arraySize));
                for (int index = 0; index < keys.arraySize; index++)
                {
                    SerializedProperty key = keys.GetArrayElementAtIndex(index);
                    SerializedProperty sourceKey = sourceKeys.GetArrayElementAtIndex(index);
                    if (field == "_frames")
                    {
                        Assert.That(key.intValue, Is.EqualTo(sourceKey.intValue));
                    }
                    else
                    {
                        Assert.That(key.colorValue, Is.EqualTo(sourceKey.colorValue));
                    }
                }
            }
        }

        [Test]
        public void FourAudioChannelsReuseOriginalGazirovkaClipsAndGain()
        {
            GameObject prefab = Prefab();
            var data = new SerializedObject(prefab.GetComponent<AnomalyAudio>());
            Assert.That(prefab.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(4));
            Assert.That(data.FindProperty("_blowoutDelay").floatValue, Is.Zero);
            Assert.That(data.FindProperty("_awaking").objectReferenceValue, Is.Null);
            Assert.That(data.FindProperty("_accumulate").objectReferenceValue, Is.Null);
            var clips = new HashSet<AudioClip>();
            foreach (string field in new[] { "_idle", "_blowout", "_entrance", "_hit" })
            {
                AudioSource source = data.FindProperty(field).objectReferenceValue as AudioSource;
                Assert.That(source, Is.Not.Null, field);
                bool idle = field == "_idle";
                string path = GAZIROVKA_ROOT + "/Src/Audio/" + (idle ? "acidic_idle.ogg" : "acidic_hit.ogg");
                Assert.That(source.clip, Is.SameAs(AssetDatabase.LoadAssetAtPath<AudioClip>(path)), field);
                Assert.That(source.clip, Is.Not.Null, path);
                Assert.That(source.loop, Is.EqualTo(idle), field);
                Assert.That(source.playOnAwake, Is.False, field);
                Assert.That(source.spatialBlend, Is.EqualTo(1f), field);
                Assert.That(source.clip.channels, Is.EqualTo(1), field);
                Assert.That(source.clip.frequency, Is.EqualTo(44100), field);
                Assert.That(source.minDistance, Is.EqualTo(idle ? 2.5f : 2f), field);
                Assert.That(source.maxDistance, Is.EqualTo(25f), field);
                Assert.That(source.volume, Is.EqualTo(1f), field);
                AnomalyAudioGain gain = source.GetComponent<AnomalyAudioGain>();
                Assert.That(gain, Is.Not.Null, field);
                Assert.That(gain.Gain, Is.EqualTo(1.5f), field);
                clips.Add(source.clip);
            }
            Assert.That(clips.Count, Is.EqualTo(2));
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

        private static void AssertTriplet(ParticleSystem.MinMaxCurve x, ParticleSystem.MinMaxCurve y, ParticleSystem.MinMaxCurve z, string name)
        {
            Assert.That(y.mode, Is.EqualTo(x.mode), name + "/Y");
            Assert.That(z.mode, Is.EqualTo(x.mode), name + "/Z");
        }

        private static void AssertLight(Light light, float range)
        {
            Assert.That(light, Is.Not.Null);
            Assert.That(light.type, Is.EqualTo(LightType.Point));
            Assert.That(light.shadows, Is.EqualTo(LightShadows.Soft));
            Assert.That(light.range, Is.EqualTo(range));
            Assert.That(light.transform.localPosition, Is.EqualTo(new Vector3(0f, 0.75f, 0f)));
            Assert.That(float.IsNaN(light.intensity) || float.IsInfinity(light.intensity), Is.False);
            Assert.That(light.intensity, Is.GreaterThan(0f));
        }
    }
}
#endif
