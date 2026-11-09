#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.Player;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class GazirovkaAssetTests
    {
        private const string ROOT = "Assets/_Source/Anomalies/Gazirovka";
        private const string PREFAB_PATH = ROOT + "/Gazirovka.prefab";
        private const string PROFILE_PATH = ROOT + "/Src/GazirovkaInfluence.asset";
        private const string PPE_PATH = ROOT + "/Src/PostProcess/acidic_mine.asset";

        [Test]
        public void PlayerAndBoltTriggerUsesOriginalCycleAndIntegerSpawnRadius()
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
            Assert.That(data.FindProperty("_randomizeRadius").boolValue, Is.True);
            Assert.That(data.FindProperty("_randomRadiusMin").intValue, Is.EqualTo(2));
            Assert.That(data.FindProperty("_randomRadiusMax").intValue, Is.EqualTo(3));
            Assert.That(data.FindProperty("_visual").objectReferenceValue, Is.SameAs(prefab.GetComponent<OverlappingAnomalyVisual>()));
            AnomalyProximityZone proximity = prefab.GetComponentInChildren<AnomalyProximityZone>(true);
            Assert.That(proximity, Is.Not.Null);
            var proximityData = new SerializedObject(proximity);
            Assert.That(proximityData.FindProperty("_source").objectReferenceValue, Is.SameAs(reaction));
            Assert.That(proximityData.FindProperty("_sourceVolume").objectReferenceValue, Is.SameAs(volume));
            Assert.That(proximity.ProximityRadius, Is.EqualTo(6f));
            Rigidbody proximityBody = proximity.GetComponent<Rigidbody>();
            Assert.That(proximityBody, Is.Not.Null);
            Assert.That(proximityBody.isKinematic, Is.True);
            Assert.That(proximityBody.useGravity, Is.False);
        }

        [Test]
        public void ChemicalProfileUsesOnlyOriginalProximityPostProcessWithoutPhysicalImpact()
        {
            AnomalyInfluenceProfile profile = Profile();
            Assert.That(profile.Kind, Is.EqualTo(AnomalyInfluenceKind.Chemical));
            Assert.That(profile.RadiusMin, Is.EqualTo(0.7f).Within(0.000001f));
            Assert.That(profile.RadiusMax, Is.EqualTo(1f));
            Assert.That(profile.ActorPainEnabled, Is.True);
            Assert.That(profile.LaunchHeight, Is.Zero);
            Assert.That(profile.LaunchAirAcceleration, Is.Zero);
            Assert.That(profile.PullStrength, Is.Zero);
            Assert.That(profile.PulseDelay, Is.Zero);
            Assert.That(profile.Earthquake, Is.Null);
            Assert.That(profile.ElectricMine, Is.Null);
            Assert.That(profile.Electric, Is.Null);
            Assert.That(profile.ImpactPostProcess, Is.Null);
            Assert.That(profile.ImpactAnimations, Is.Null.Or.Empty);
            var postProcess = AssetDatabase.LoadAssetAtPath<AnomalyPostProcessAsset>(PPE_PATH);
            Assert.That(postProcess, Is.Not.Null);
            Assert.That(profile.InherentPostProcess, Is.SameAs(postProcess));
            Assert.That(postProcess.SourceFile, Is.EqualTo("anims/acidic_mine.ppe"));
            Assert.That(postProcess.ColorMap, Is.Not.Null);
            Assert.That(postProcess.ColorMap.width, Is.GreaterThan(0));
            Assert.That(postProcess.ColorMap.height, Is.GreaterThan(0));
        }

        [Test]
        public void NativeParticleBranchesHaveFiniteLifetimesValidMaterialsAndCompatibleVelocityModes()
        {
            GameObject prefab = Prefab();
            Assert.That(prefab.GetComponentsInChildren<XRayParticleGroupPlayer>(true), Is.Empty);
            var expected = new HashSet<string> { "studen_idle_bottom_02", "acidic_mine_smok", "studen_idle_bottom_shader", "anomaly_toxic_particle_test_00", "studen_idle_bottom_glow_02", "studen_idle_bottom_12", "studen_idle_bottom_00", "studen_idle_bottom_01", "zone_acidic_mine_hit_1", "zone_acidic_mine_hit_1_08", "zone_acidic_mine_hit_1_09", "zone_acidic_mine_hit_1_10", "zone_acidic_mine_hit_1_11" };
            int rendered = 0;
            foreach (ParticleSystem system in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                Assert.That(system.main.playOnAwake, Is.False, system.name);
                ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                if (velocity.enabled)
                {
                    AssertTripletMode(velocity.x, velocity.y, velocity.z, system.name + "/linear");
                    AssertTripletMode(velocity.orbitalX, velocity.orbitalY, velocity.orbitalZ, system.name + "/orbital");
                    AssertTripletMode(velocity.orbitalOffsetX, velocity.orbitalOffsetY, velocity.orbitalOffsetZ, system.name + "/offset");
                }
                ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                Assert.That(renderer, Is.Not.Null, system.name);
                if (renderer.enabled == false)
                {
                    Assert.That(system.emission.enabled, Is.False, system.name);
                    continue;
                }
                rendered++;
                string sourceName = system.name.Split(new[] { "__" }, StringSplitOptions.None)[0];
                expected.Remove(sourceName);
                ParticleSystem.MinMaxCurve lifetime = system.main.startLifetime;
                Assert.That(lifetime.mode, Is.EqualTo(ParticleSystemCurveMode.Constant), system.name);
                Assert.That(float.IsNaN(lifetime.constant) || float.IsInfinity(lifetime.constant), Is.False, system.name);
                Assert.That(lifetime.constant, Is.InRange(0.8f, 3f), system.name);
                foreach (Material material in renderer.sharedMaterials)
                {
                    AssertMaterial(material, system.name);
                }
                Assert.That(renderer.sharedMaterials, Is.Not.Empty, system.name);
                if (sourceName.StartsWith("zone_acidic_mine_hit_1", StringComparison.Ordinal) && sourceName != "zone_acidic_mine_hit_1_09")
                {
                    ParticleSystem.TextureSheetAnimationModule frames = system.textureSheetAnimation;
                    Assert.That(frames.enabled, Is.True, system.name);
                    Assert.That(frames.timeMode, Is.EqualTo(ParticleSystemAnimationTimeMode.Lifetime), system.name);
                    Assert.That(frames.frameOverTime.mode, Is.EqualTo(ParticleSystemCurveMode.Constant), system.name);
                    Assert.That(frames.frameOverTime.constant, Is.Zero, system.name);
                    Assert.That(frames.startFrame.mode, Is.EqualTo(ParticleSystemCurveMode.TwoConstants), system.name);
                    Assert.That(frames.startFrame.constantMin, Is.Zero, system.name);
                    int frameCount = frames.numTilesX * frames.numTilesY;
                    Assert.That(frames.startFrame.constantMax, Is.GreaterThan(0f).And.LessThan(1f), system.name);
                    Assert.That(Mathf.FloorToInt(frames.startFrame.constantMax * frameCount), Is.EqualTo(frameCount - 1), system.name);
                }
            }
            ParticleSystem spores = Array.Find(prefab.GetComponentsInChildren<ParticleSystem>(true), system => system.name == "zone_acidic_mine_hit_1_09");
            Assert.That(spores, Is.Not.Null);
            Assert.That(spores.textureSheetAnimation.enabled, Is.False, "The original spore texture is a single frame");
            Assert.That(rendered, Is.GreaterThanOrEqualTo(13));
            Assert.That(expected, Is.Empty, "Missing source particle branches");
        }

        [Test]
        public void SourceLightAnimationAndBlowoutKeepOriginalRangesHeightsAndTiming()
        {
            GameObject prefab = Prefab();
            Assert.That(prefab.GetComponentsInChildren<Light>(true).Length, Is.EqualTo(2));
            var data = new SerializedObject(prefab.GetComponent<OverlappingAnomalyVisual>());
            Light idle = data.FindProperty("_idleIllumination").objectReferenceValue as Light;
            Light discharge = data.FindProperty("_dischargeLight").objectReferenceValue as Light;
            AssertLight(idle, 4f, LightShadows.None);
            AssertLight(discharge, 7f, LightShadows.Soft);
            Assert.That(discharge.color, Is.EqualTo(new Color(1.1f, 1.7f, 1.4f)));
            Assert.That(data.FindProperty("_idleLightRange").floatValue, Is.EqualTo(4f));
            Assert.That(data.FindProperty("_idleLightRangeJitter").floatValue, Is.EqualTo(0.25f));
            Assert.That(data.FindProperty("_dischargeLightRange").floatValue, Is.EqualTo(7f));
            Assert.That(data.FindProperty("_dischargeLightDuration").floatValue, Is.EqualTo(0.35f).Within(0.000001f));
            Assert.That(data.FindProperty("_dischargeLightDelay").floatValue, Is.Zero);
            SerializedProperty animation = data.FindProperty("_idleLightAnimation");
            Assert.That(animation.FindPropertyRelative("_fps").floatValue, Is.EqualTo(10f));
            Assert.That(animation.FindPropertyRelative("_frameCount").intValue, Is.EqualTo(40));
            SerializedProperty frames = animation.FindPropertyRelative("_frames");
            SerializedProperty colors = animation.FindPropertyRelative("_colors");
            int[] expectedFrames = { 0, 10, 13, 17, 30, 39 };
            Color32[] expectedColors = { new Color32(153, 213, 56, 255), new Color32(149, 186, 0, 255), new Color32(153, 213, 56, 255), new Color32(141, 193, 58, 255), new Color32(149, 186, 0, 255), new Color32(153, 213, 56, 255) };
            Assert.That(frames.arraySize, Is.EqualTo(expectedFrames.Length));
            Assert.That(colors.arraySize, Is.EqualTo(expectedColors.Length));
            for (int index = 0; index < expectedFrames.Length; index++)
            {
                Assert.That(frames.GetArrayElementAtIndex(index).intValue, Is.EqualTo(expectedFrames[index]));
                Assert.That((Color32)colors.GetArrayElementAtIndex(index).colorValue, Is.EqualTo(expectedColors[index]));
            }
        }

        [Test]
        public void FourSpatialAudioChannelsUseTwoOriginalBanksAndUnclampedGain()
        {
            GameObject prefab = Prefab();
            AnomalyAudio audio = prefab.GetComponent<AnomalyAudio>();
            Assert.That(audio, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(4));
            var data = new SerializedObject(audio);
            Assert.That(data.FindProperty("_blowoutDelay").floatValue, Is.Zero);
            Assert.That(data.FindProperty("_awaking").objectReferenceValue, Is.Null);
            Assert.That(data.FindProperty("_accumulate").objectReferenceValue, Is.Null);
            var clips = new HashSet<AudioClip>();
            foreach (string field in new[] { "_idle", "_blowout", "_entrance", "_hit" })
            {
                AudioSource source = data.FindProperty(field).objectReferenceValue as AudioSource;
                Assert.That(source, Is.Not.Null, field);
                Assert.That(source.clip, Is.Not.Null, field);
                bool idle = field == "_idle";
                Assert.That(source.clip.name, Is.EqualTo(idle ? "acidic_idle" : "acidic_hit"), field);
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
            foreach (AudioClip clip in clips)
            {
                string expected = clip.name == "acidic_idle" ? "401ccf6ecbd1ea3840700c88eac50fb5fb0b427da2d8520cf281f47fec411516" : "4c30cc79e741ff0aa45192cbf9011573c47b5be0e60f780da1199126a7cdd572";
                using (SHA256 algorithm = SHA256.Create())
                {
                    string hash = BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(AssetDatabase.GetAssetPath(clip)))).Replace("-", string.Empty).ToLowerInvariant();
                    Assert.That(hash, Is.EqualTo(expected), clip.name);
                }
            }
        }

        [Test]
        public void PrefabContainsOnlySharedPresentationAndReactionComponents()
        {
            GameObject prefab = Prefab();
            var allowed = new HashSet<Type> { typeof(AnomalyPlayerReaction), typeof(AnomalyProximityZone), typeof(OverlappingAnomalyVisual), typeof(AnomalyAudio), typeof(AnomalyAudioGain) };
            bool hasGrassBender = false;
            foreach (MonoBehaviour component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                Assert.That(component, Is.Not.Null, "Missing prefab script");
                bool presentation = allowed.Contains(component.GetType()) || component.GetType().FullName == "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData" || component.GetType().FullName == "RaidIntoDarkness.Effects.AnomalyGrassBender";
                Assert.That(presentation, Is.True, component.GetType().FullName);
                if (component.GetType().FullName == "RaidIntoDarkness.Effects.AnomalyGrassBender")
                {
                    hasGrassBender = true;
                    var grass = new SerializedObject(component);
                    Assert.That(grass.FindProperty("_visual").objectReferenceValue, Is.SameAs(prefab.GetComponent<OverlappingAnomalyVisual>()));
                    Assert.That(grass.FindProperty("_fade").floatValue, Is.EqualTo(1.33f).Within(0.000001f));
                    Assert.That(grass.FindProperty("_speed").floatValue, Is.EqualTo(3f));
                    Assert.That(grass.FindProperty("_intensity").floatValue, Is.EqualTo(1f));
                    Assert.That(grass.FindProperty("_radius").floatValue, Is.EqualTo(3f));
                }
            }
            Assert.That(hasGrassBender, Is.True);
            Assert.That(prefab.GetComponentsInChildren<AnomalyContactEffects>(true), Is.Empty);
            var data = new SerializedObject(prefab.GetComponent<OverlappingAnomalyVisual>());
            Assert.That(data.FindProperty("_keepIdleDuringDischarge").boolValue, Is.True);
            Assert.That(data.FindProperty("_playIdleChildren").boolValue, Is.True);
            Assert.That(data.FindProperty("_dischargeEffectDelay").floatValue, Is.Zero);
            Assert.That(data.FindProperty("_idle").objectReferenceValue, Is.Not.Null);
            foreach (string field in new[] { "_awaking", "_contactEffects", "_entranceContactEffects", "_idleContactEffects", "_smallEntranceContactEffects", "_smallIdleContactEffects" })
            {
                Assert.That(data.FindProperty(field).objectReferenceValue, Is.Null, field);
            }
            GameObject discharge = data.FindProperty("_dischargeTemplate").objectReferenceValue as GameObject;
            Assert.That(discharge, Is.Not.Null);
            Assert.That(discharge.activeSelf, Is.False);
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

        private static void AssertTripletMode(ParticleSystem.MinMaxCurve x, ParticleSystem.MinMaxCurve y, ParticleSystem.MinMaxCurve z, string name)
        {
            Assert.That(y.mode, Is.EqualTo(x.mode), name + "/Y");
            Assert.That(z.mode, Is.EqualTo(x.mode), name + "/Z");
        }

        private static void AssertMaterial(Material material, string name)
        {
            Assert.That(material, Is.Not.Null, name);
            Assert.That(material.shader, Is.Not.Null, name);
            Assert.That(material.shader.name, Does.Not.Contain("InternalErrorShader"), name);
            Assert.That(material.GetTexture("_MainTex"), Is.Not.Null, name);
            foreach (string property in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(property);
                if (texture != null)
                {
                    Assert.That(texture.width, Is.GreaterThan(0), name + "/" + property);
                    Assert.That(texture.height, Is.GreaterThan(0), name + "/" + property);
                }
            }
            if (material.HasProperty("_Intensity"))
            {
                Assert.That(material.GetFloat("_Intensity"), Is.EqualTo(1f), name);
                Vector4 frameSize = material.GetVector("_FrameSize");
                Assert.That(frameSize.x, Is.EqualTo(1f), name);
                Assert.That(frameSize.y, Is.EqualTo(1f), name);
                Assert.That(material.GetFloat("_FrameColumns"), Is.EqualTo(1f), name);
            }
        }

        private static void AssertLight(Light light, float range, LightShadows shadows)
        {
            Assert.That(light, Is.Not.Null);
            Assert.That(light.type, Is.EqualTo(LightType.Point));
            Assert.That(light.shadows, Is.EqualTo(shadows));
            Assert.That(light.range, Is.EqualTo(range));
            Assert.That(light.transform.localPosition, Is.EqualTo(new Vector3(0f, 0.75f, 0f)));
            Assert.That(float.IsNaN(light.intensity) || float.IsInfinity(light.intensity), Is.False);
            Assert.That(light.intensity, Is.GreaterThan(0f));
        }
    }
}
#endif
