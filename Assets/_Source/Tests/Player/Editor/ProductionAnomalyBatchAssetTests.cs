using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests.Editor
{
    public sealed class ProductionAnomalyBatchAssetTests
    {
        private const string ROOT = "Assets/_Source/Anomalies";
        private const string CATALOG = "Assets/_Tools/X-Ray OGF OMG Importer/Editor/Levels/XRayLevelAnomalyCatalog.asset";
        private const string ZHAR_SOUND_SHA256 = "2de2a48f94975d3ea88df9fe606aeef10e7d970d1657fad55eb5625389799930";

        private static readonly Definition[] DEFINITIONS =
        {
            new Definition("AcidicComet", "fireball_acidic_zone", 0.1f, 0.1f, 0.1f, 15f, 15f, true, true, false),
            new Definition("Tesla", "fireball_electric_zone", 0.1f, 0.1f, 0.1f, 15f, 15f, true, true, false),
            new Definition("Comet", "fireball_zone", 0.1f, 0.1f, 0.1f, 15f, 15f, true, true, false),
            new Definition("GeneratorDust", "generator_dust_static", 0.1f, 0.1f, 0.1f, 0f, 30f, false, false, false),
            new Definition("GeneratorElectro", "generator_electra", 0.1f, 0.1f, 0.1f, 0f, 30f, false, false, false),
            new Definition("GeneratorHeat", "generator_torrid", 0.1f, 0.1f, 0.1f, 30f, 30f, true, true, false),
            new Definition("BurningFluff", "zone_burning_fuzz", 0f, 1f, 5f, 0f, 0f, false, false, false),
            new Definition("Impulse", "zone_emi", 0f, 6f, 5f, 0f, 80f, true, true, false),
            new Definition("AcidicFog", "zone_field_acidic_average", 0.1f, 0.3f, 0.1f, 0f, 0f, false, true, true),
            new Definition("PsiField", "zone_field_psychic_average", 0.1f, 0.8f, 0.1f, 0f, 0f, false, true, true),
            new Definition("RadiationField", "zone_field_radioactive_average", 0.1f, 0.1f, 0.1f, 0f, 0f, false, true, true),
            new Definition("ThermalField", "zone_field_thermal_average", 0.1f, 0.3f, 0.1f, 0f, 0f, false, true, true),
            new Definition("Mine", "zone_mine_field_soc", 0f, 1f, 100f, 0f, 30f, true, false, false),
            new Definition("WishGranter", "zone_monolith", 0.001f, 9f, 0.001f, 0f, 0f, true, true, true),
            new Definition("Weightlessness", "zone_no_gravity", 0.8f, 0.5f, 1f, 0f, 0f, false, false, false),
            new Definition("Teleport", "zone_teleport", 0f, 1f, 0.5f, 15f, 30f, false, false, false),
            new Definition("Heat", "zone_zhar", 0f, 0.5f, 0.3f, 8f, 8f, true, true, false)
        };
        private static readonly Dictionary<string, string> EXPECTED_CATALOG_BASES = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "fireball_acidic_zone", "AcidicComet" },
            { "fireball_electric_zone", "Tesla" },
            { "fireball_zone", "Comet" },
            { "generator_dust_static", "GeneratorDust" },
            { "generator_electra", "GeneratorElectro" },
            { "generator_torrid", "GeneratorHeat" },
            { "zone_burning_fuzz", "BurningFluff" },
            { "zone_burning_fuzz_average", "BurningFluff" },
            { "zone_burning_fuzz_strong", "BurningFluff" },
            { "zone_burning_fuzz_weak", "BurningFluff" },
            { "zone_burning_fuzz1", "BurningFluff" },
            { "zone_buzz", "Kisel" },
            { "zone_buzz_average", "Kisel" },
            { "zone_buzz_strong", "Kisel" },
            { "zone_buzz_weak", "Kisel" },
            { "zone_emi", "Impulse" },
            { "zone_field_acidic_average", "AcidicFog" },
            { "zone_field_acidic_strong", "AcidicFog" },
            { "zone_field_acidic_weak", "AcidicFog" },
            { "zone_field_psychic_average", "PsiField" },
            { "zone_field_psychic_strong", "PsiField" },
            { "zone_field_psychic_weak", "PsiField" },
            { "zone_field_radioactive_average", "RadiationField" },
            { "zone_field_radioactive_strong", "RadiationField" },
            { "zone_field_radioactive_weak", "RadiationField" },
            { "zone_field_thermal_average", "ThermalField" },
            { "zone_field_thermal_strong", "ThermalField" },
            { "zone_field_thermal_weak", "ThermalField" },
            { "zone_gravi_zone", "Vortex" },
            { "zone_mine_acidic_average", "Gazirovka" },
            { "zone_mine_acidic_strong", "Gazirovka" },
            { "zone_mine_acidic_weak", "Gazirovka" },
            { "zone_mine_chemical_average", "Kisel" },
            { "zone_mine_chemical_strong", "Kisel" },
            { "zone_mine_chemical_weak", "Kisel" },
            { "zone_mine_electric_average", "Electra" },
            { "zone_mine_electric_strong", "Electra" },
            { "zone_mine_electric_weak", "Electra" },
            { "zone_mine_field_soc", "Mine" },
            { "zone_mine_gravitational_average", "Vortex" },
            { "zone_mine_gravitational_strong", "Carousel" },
            { "zone_mine_gravitational_weak", "Tramplin" },
            { "zone_mine_static_strong", "Electra" },
            { "zone_mine_steam_average", "Zharka" },
            { "zone_mine_steam_weak", "Zharka" },
            { "zone_mine_thermal_average", "Zharka" },
            { "zone_mine_thermal_strong", "Zharka" },
            { "zone_mine_thermal_weak", "Zharka" },
            { "zone_monolith", "WishGranter" },
            { "zone_mosquito_bald", "Tramplin" },
            { "zone_mosquito_bald_average", "Tramplin" },
            { "zone_mosquito_bald_strong", "Tramplin" },
            { "zone_mosquito_bald_weak", "Tramplin" },
            { "zone_no_gravity", "Weightlessness" },
            { "zone_radioactive_average", "RadiationField" },
            { "zone_radioactive_strong", "RadiationField" },
            { "zone_radioactive_weak", "RadiationField" },
            { "zone_teleport", "Teleport" },
            { "zone_witches_galantine", "Electra" },
            { "zone_witches_galantine_average", "Electra" },
            { "zone_witches_galantine_strong", "Electra" },
            { "zone_witches_galantine_weak", "Electra" },
            { "zone_zhar", "Heat" },
            { "zone_zharka_static_average", "Zharka" },
            { "zone_zharka_static_strong", "Zharka" },
            { "zone_zharka_static_weak", "Zharka" },
        };
        private static readonly Dictionary<string, string> EXPECTED_BASE_GUIDS = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "AcidicComet", "6c016ba218b7a554d832769cd1aaae04" },
            { "Tesla", "ab2a2316d20689d4981daf7c13e01daa" },
            { "Comet", "02a237de7d345164cb9cdff100244fcb" },
            { "GeneratorDust", "9b1864c752d37b14ba94ca2d9ce47896" },
            { "GeneratorElectro", "fac5ed82151239f4b8b6981debd0f6d2" },
            { "GeneratorHeat", "0503fcd536acfc34d85804ac2b14e79b" },
            { "BurningFluff", "ceb9b97044f535a43ad1b4d909fd5dac" },
            { "Kisel", "f55d417a9b75a5f469d4d95e71cd516d" },
            { "Impulse", "7986d4e311c9b574d8d6996a9110871f" },
            { "AcidicFog", "5758b0fc117a5d44295534e1c9202a5a" },
            { "PsiField", "bca8d2a43cb61e94ca33142036557a19" },
            { "RadiationField", "ec58e181ecf926a4bb2ed3a93c674e2e" },
            { "ThermalField", "4e106ea0509100c41938071e6c72ea0b" },
            { "Vortex", "57fe2d45e2d10c546ba3c7b47b5b06f8" },
            { "Gazirovka", "edd76411fe76bcb4f9128d7dcf32e3b8" },
            { "Electra", "e4fcbbdd51e69a94fa41cacdf195f1b6" },
            { "Mine", "3bb71be827698994394ac26bc5c21bad" },
            { "Carousel", "7574aac041b53704ebf4b2110384d0d7" },
            { "Tramplin", "bc6367c4932433a4eaa67d191519601b" },
            { "Zharka", "bb71a7a9124edab409ed0f17d98e4634" },
            { "WishGranter", "4c42e546ffdc8894ea2912de0c3612cc" },
            { "Weightlessness", "7d1db57fa0204524d9daac32225403d1" },
            { "Teleport", "f085bcbce6576e249a6ff4ae8a5baef4" },
            { "Heat", "ec76289f1fb1f864883a837abcfc9f14" },
        };

        [Test]
        public void EveryRequestedSourceSectionResolvesToItsReadyPrefabInTheExistingCatalog()
        {
            UnityEngine.Object catalog = AssetDatabase.LoadMainAssetAtPath(CATALOG);
            Assert.That(catalog, Is.Not.Null);
            SerializedProperty entries = new SerializedObject(catalog).FindProperty("_entries");
            Assert.That(entries, Is.Not.Null);
            foreach (Definition definition in DEFINITIONS)
            {
                string difficulty = Difficulty(definition.Section);
                GameObject expected = difficulty.Length == 0 ? Prefab(definition.Name) : Variant(definition.Name, difficulty);
                int matches = 0;
                for (int index = 0; index < entries.arraySize; index++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                    if (entry.FindPropertyRelative("_section").stringValue != definition.Section)
                    {
                        continue;
                    }
                    matches++;
                    Assert.That(entry.FindPropertyRelative("_prefab").objectReferenceValue, Is.EqualTo(expected), definition.Section);
                }
                Assert.That(matches, Is.EqualTo(1), definition.Section);
            }
        }

        [Test]
        public void EveryCatalogEntryUsesItsOriginalFamilyAndEveryDifficultyUsesARealRenamedVariant()
        {
            UnityEngine.Object catalog = AssetDatabase.LoadMainAssetAtPath(CATALOG);
            Assert.That(catalog, Is.Not.Null);
            SerializedProperty entries = new SerializedObject(catalog).FindProperty("_entries");
            Assert.That(entries.arraySize, Is.EqualTo(EXPECTED_CATALOG_BASES.Count));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var uniqueVariants = new HashSet<GameObject>();
            int suffixEntries = 0;
            for (int index = 0; index < entries.arraySize; index++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                string section = entry.FindPropertyRelative("_section").stringValue;
                Assert.That(seen.Add(section), Is.True, section);
                Assert.That(EXPECTED_CATALOG_BASES.TryGetValue(section, out string family), Is.True, section);
                GameObject prefab = entry.FindPropertyRelative("_prefab").objectReferenceValue as GameObject;
                Assert.That(prefab, Is.Not.Null, section);
                GameObject original = Prefab(family);
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)), Is.EqualTo(EXPECTED_BASE_GUIDS[family]), section);
                string difficulty = Difficulty(section);
                if (difficulty.Length == 0)
                {
                    Assert.That(prefab, Is.EqualTo(original), section);
                    continue;
                }
                suffixEntries++;
                uniqueVariants.Add(prefab);
                Assert.That(prefab, Is.EqualTo(Variant(family, difficulty)), section);
                Assert.That(prefab.name, Is.EqualTo(family + difficulty), section);
                Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant), section);
                GameObject inherited = PrefabUtility.GetCorrespondingObjectFromSource(prefab);
                Assert.That(inherited, Is.EqualTo(original), section);
                Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(inherited)), Is.EqualTo(EXPECTED_BASE_GUIDS[family]), section);
                AnomalyPlayerReaction reaction = prefab.GetComponent<AnomalyPlayerReaction>();
                Assert.That(reaction, Is.Not.Null, section);
                Assert.That(reaction.InfluenceProfile, Is.EqualTo(original.GetComponent<AnomalyPlayerReaction>().InfluenceProfile), section);
                string label = entry.FindPropertyRelative("_displayName").stringValue;
                string expectedSuffix = difficulty == "Weak" ? " — слабый вариант" : difficulty == "Average" ? " — средний вариант" : " — сильный вариант";
                Assert.That(label, Does.EndWith(expectedSuffix), section);
                Assert.That(System.Text.RegularExpressions.Regex.Matches(label, " — (слабый|средний|сильный) вариант").Count, Is.EqualTo(1), section);
            }
            Assert.That(suffixEntries, Is.EqualTo(48));
            Assert.That(uniqueVariants.Count, Is.EqualTo(32));
        }

        [Test]
        public void FluffDifficultyVariantsUseThreeOriginalModelsAndInheritTheBaseRuntimeSettings()
        {
            GameObject original = Prefab("BurningFluff");
            Transform baseModel = original.transform.Find("SourceModel");
            Assert.That(baseModel, Is.Not.Null);
            foreach (string difficulty in new[] { "Weak", "Average", "Strong" })
            {
                GameObject variant = Variant("BurningFluff", difficulty);
                Transform model = variant.transform.Find("SourceModel");
                Assert.That(model, Is.Not.Null, difficulty);
                GameObject native = PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
                Assert.That(native, Is.Not.Null, difficulty);
                string expectedFile = difficulty == "Weak" ? "topolinypuh_small.ogf" : difficulty == "Average" ? "topolinypuh_middle.ogf" : "topolinypuh_big.ogf";
                Assert.That(AssetDatabase.GetAssetPath(native), Does.EndWith(expectedFile), difficulty);
                Assert.That(model.localPosition, Is.EqualTo(baseModel.localPosition), difficulty);
                Assert.That(model.localRotation, Is.EqualTo(baseModel.localRotation), difficulty);
                Assert.That(model.localScale, Is.EqualTo(baseModel.localScale), difficulty);
                AnomalyPlayerReaction reaction = variant.GetComponent<AnomalyPlayerReaction>();
                AnomalyPlayerReaction baseReaction = original.GetComponent<AnomalyPlayerReaction>();
                Assert.That(reaction.InfluenceProfile, Is.EqualTo(baseReaction.InfluenceProfile), difficulty);
                var data = new SerializedObject(reaction);
                var baseData = new SerializedObject(baseReaction);
                foreach (string field in new[] { "_awakingTime", "_blowoutTime", "_accumulateTime", "_minimumPlayerSpeed" })
                {
                    Assert.That(data.FindProperty(field).floatValue, Is.EqualTo(baseData.FindProperty(field).floatValue), difficulty + "/" + field);
                }
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.Not.Null, difficulty);
                        Texture diffuse = material.HasProperty("_BaseColorMap") ? material.GetTexture("_BaseColorMap") : material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                        Assert.That(diffuse, Is.Not.Null, difficulty);
                    }
                }
            }
        }

        [Test]
        public void EveryPrefabHasItsOwnProfileActivationAndPresentationWithoutMissingScripts()
        {
            foreach (Definition definition in DEFINITIONS)
            {
                GameObject prefab = Prefab(definition.Name);
                Assert.That(prefab.transform.parent, Is.Null, definition.Name);
                Assert.That(prefab.activeSelf, Is.True, definition.Name);
                Assert.That(prefab.GetComponent<AnomalyVisual>(), Is.Not.Null, definition.Name);
                var visual = new SerializedObject(prefab.GetComponent<AnomalyVisual>());
                Assert.That(visual.FindProperty("_useSourceContactEffects").boolValue, Is.True, definition.Name);
                if (definition.Name == "AcidicComet")
                {
                    Assert.That(visual.FindProperty("_smallContactEffects").objectReferenceValue, Is.Null);
                    Assert.That(visual.FindProperty("_smallEntranceContactEffects").objectReferenceValue, Is.Null);
                    Assert.That(visual.FindProperty("_smallIdleContactEffects").objectReferenceValue, Is.Null);
                }
                AnomalyPlayerReaction reaction = prefab.GetComponent<AnomalyPlayerReaction>();
                Assert.That(reaction, Is.Not.Null, definition.Name);
                Assert.That(reaction.InfluenceProfile, Is.Not.Null, definition.Name);
                Assert.That(AssetDatabase.GetAssetPath(reaction.InfluenceProfile), Does.StartWith(ROOT + "/" + definition.Name + "/Src/"), definition.Name);
                SphereCollider activation = prefab.GetComponent<SphereCollider>();
                Assert.That(activation, Is.Not.Null, definition.Name);
                Assert.That(activation.isTrigger, Is.True, definition.Name);
                Assert.That(activation.radius, Is.GreaterThan(0f), definition.Name);
                foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    Assert.That(behaviour, Is.Not.Null, definition.Name);
                    Assert.That(behaviour.GetType().Name, Is.Not.EqualTo("XRayParticleEffectPlayer"), definition.Name);
                }
            }
        }

        [Test]
        public void SourceCyclesAndPlayerBoltPoliciesRemainDistinctAcrossAllSeventeenAnomalies()
        {
            foreach (Definition definition in DEFINITIONS)
            {
                AnomalyPlayerReaction reaction = Prefab(definition.Name).GetComponent<AnomalyPlayerReaction>();
                var data = new SerializedObject(reaction);
                Assert.That(data.FindProperty("_awakingTime").floatValue, Is.EqualTo(definition.Awaking).Within(0.00001f), definition.Name);
                Assert.That(data.FindProperty("_blowoutTime").floatValue, Is.EqualTo(definition.Blowout).Within(0.00001f), definition.Name);
                Assert.That(data.FindProperty("_accumulateTime").floatValue, Is.EqualTo(definition.Accumulate).Within(0.00001f), definition.Name);
                Assert.That(data.FindProperty("_ignoreSmallObjects").boolValue, Is.EqualTo(definition.IgnoreSmall), definition.Name);
                Assert.That(data.FindProperty("_ignoreNonlivingObjects").boolValue, Is.EqualTo(definition.IgnoreNonliving), definition.Name);
                Assert.That(data.FindProperty("_actorOnly").boolValue, Is.EqualTo(definition.ActorOnly), definition.Name);
                Assert.That(data.FindProperty("_minimumPlayerSpeed").floatValue, Is.EqualTo(definition.Name == "BurningFluff" ? 2f : 0f), definition.Name);
                Assert.That(data.FindProperty("_randomizeRadius").boolValue, Is.False, definition.Name);
                Assert.That(reaction.InfluenceProfile.InterruptSprint, Is.EqualTo(definition.Name == "Mine"), definition.Name);
                Assert.That(reaction.InfluenceProfile.LaunchHeight, Is.Zero, definition.Name);
            }
        }

        [Test]
        public void DynamicLightsExistOnlyWhereEnabledByTheOriginalConfigs()
        {
            foreach (Definition definition in DEFINITIONS)
            {
                GameObject prefab = Prefab(definition.Name);
                SerializedObject visual = new SerializedObject(prefab.GetComponent<AnomalyVisual>());
                Light idle = visual.FindProperty("_idleIllumination").objectReferenceValue as Light;
                SerializedProperty dischargeProperty = visual.FindProperty("_dischargeLight");
                Light discharge = dischargeProperty != null ? dischargeProperty.objectReferenceValue as Light : null;
                Assert.That(idle != null, Is.EqualTo(definition.IdleRange > 0f), definition.Name);
                Assert.That(discharge != null, Is.EqualTo(definition.BlowoutRange > 0f), definition.Name);
                Assert.That(prefab.GetComponentsInChildren<Light>(true).Length, Is.EqualTo((definition.IdleRange > 0f ? 1 : 0) + (definition.BlowoutRange > 0f ? 1 : 0)), definition.Name);
                if (idle != null)
                {
                    AssertLight(idle, definition.IdleRange, definition.Name);
                    Assert.That(idle.enabled, Is.True, definition.Name);
                    Assert.That(visual.FindProperty("_idleLightAnimation").FindPropertyRelative("_frameCount").intValue, Is.GreaterThan(0), definition.Name);
                }
                if (discharge != null)
                {
                    AssertLight(discharge, definition.BlowoutRange, definition.Name);
                    Assert.That(discharge.enabled, Is.False, definition.Name);
                    Assert.That(visual.FindProperty("_dischargeLightDuration").floatValue, Is.GreaterThan(0f), definition.Name);
                }
            }
        }

        [Test]
        public void SourceParticlesHaveResolvedMaterialsAndUniformOrbitalCurveModes()
        {
            foreach (Definition definition in DEFINITIONS)
            {
                GameObject prefab = Prefab(definition.Name);
                ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                if (definition.Name == "RadiationField")
                {
                    Assert.That(systems, Is.Empty, definition.Name);
                }
                else
                {
                    Assert.That(systems.Length, Is.GreaterThan(0), definition.Name);
                }
                foreach (ParticleSystem system in systems)
                {
                    Assert.That(system.main.playOnAwake, Is.False, definition.Name + "/" + system.name);
                    ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                    Assert.That(velocity.orbitalX.mode, Is.EqualTo(velocity.orbitalY.mode), definition.Name + "/" + system.name);
                    Assert.That(velocity.orbitalY.mode, Is.EqualTo(velocity.orbitalZ.mode), definition.Name + "/" + system.name);
                    ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                    if (renderer != null && renderer.enabled)
                    {
                        Assert.That(renderer.sharedMaterial, Is.Not.Null, definition.Name + "/" + system.name);
                        Assert.That(renderer.sharedMaterial.shader, Is.Not.Null, definition.Name + "/" + system.name);
                        Assert.That(renderer.sharedMaterial.shader.name, Does.StartWith("RaidIntoDarkness/"), definition.Name + "/" + system.name);
                    }
                }
            }
        }

        [Test]
        public void AudioSourcesUseImportedClipsAndNeverAutoplayInThePrefab()
        {
            foreach (Definition definition in DEFINITIONS)
            {
                GameObject prefab = Prefab(definition.Name);
                Assert.That(prefab.GetComponent<AnomalyAudio>(), Is.Not.Null, definition.Name);
                foreach (AudioSource source in prefab.GetComponentsInChildren<AudioSource>(true))
                {
                    Assert.That(source.clip, Is.Not.Null, definition.Name + "/" + source.name);
                    Assert.That(source.clip.samples, Is.GreaterThan(0), definition.Name + "/" + source.name);
                    Assert.That(source.playOnAwake, Is.False, definition.Name + "/" + source.name);
                    Assert.That(source.loop, Is.EqualTo(source.name == "Idle"), definition.Name + "/" + source.name);
                    Assert.That(source.maxDistance, Is.GreaterThanOrEqualTo(source.minDistance), definition.Name + "/" + source.name);
                    Assert.That(source.dopplerLevel, Is.Zero, definition.Name + "/" + source.name);
                    Assert.That(source.spatialBlend, Is.EqualTo(source.name == "TransferAudio" || source.clip.channels == 2 ? 0f : 1f), definition.Name + "/" + source.name);
                }
            }
        }

        [Test]
        public void ZharSoundRetainsItsOriginalOneMeterCutoffWithoutDividingByZero()
        {
            AnomalyAudio audio = Prefab("Heat").GetComponent<AnomalyAudio>();
            Assert.That(audio, Is.Not.Null);
            AudioSource source = new SerializedObject(audio).FindProperty("_idle").objectReferenceValue as AudioSource;
            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.Not.Null);
            string clipPath = AssetDatabase.GetAssetPath(source.clip);
            Assert.That(clipPath, Does.EndWith(".ogg"));
            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", clipPath));
            using (SHA256 hash = SHA256.Create())
            {
                string actual = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(absolutePath))).Replace("-", string.Empty).ToLowerInvariant();
                Assert.That(actual, Is.EqualTo(ZHAR_SOUND_SHA256));
            }
            Assert.That(source.minDistance, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(source.maxDistance, Is.EqualTo(1f).Within(0.00001f));
            AnimationCurve curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(1f).Within(0.00001f));
            Assert.That(curve.Evaluate(1f), Is.EqualTo(0f).Within(0.00001f));
        }

        [Test]
        public void WeightlessnessUsesSourceSlowdownWithoutSpringboardLaunchOrFabricatedLight()
        {
            GameObject prefab = Prefab("Weightlessness");
            AnomalyInfluenceProfile profile = prefab.GetComponent<AnomalyPlayerReaction>().InfluenceProfile;
            Assert.That(profile.Kind, Is.EqualTo(AnomalyInfluenceKind.NoGravity));
            Assert.That(new SerializedObject(profile).FindProperty("_runSpeedMultiplier").floatValue, Is.EqualTo(0.6f));
            Assert.That(profile.LaunchHeight, Is.Zero);
            Assert.That(profile.PullStrength, Is.Zero);
            Assert.That(profile.ActorPainEnabled, Is.False);
            Assert.That(profile.InherentPostProcess, Is.Not.Null);
            Assert.That(profile.InherentPostProcess.SourceFile.Replace('\\', '/'), Does.EndWith("alcohol.ppe"));
            Assert.That(prefab.GetComponent<SphereCollider>().radius, Is.EqualTo(3f));
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
        }

        [Test]
        public void TeleportExposesAnExplicitDestinationAndUsesTheOriginalTransferEffects()
        {
            GameObject prefab = Prefab("Teleport");
            SpatialBubbleTeleport teleport = prefab.GetComponent<SpatialBubbleTeleport>();
            Assert.That(teleport, Is.Not.Null);
            Assert.That(teleport.Destination, Is.Null);
            Assert.That(teleport.ActivationVolume, Is.EqualTo(prefab.GetComponent<SphereCollider>()));
            var data = new SerializedObject(teleport);
            AnomalyPostProcessAsset postProcess = data.FindProperty("_teleportPostProcess").objectReferenceValue as AnomalyPostProcessAsset;
            Assert.That(postProcess, Is.Not.Null);
            Assert.That(postProcess.SourceFile.Replace('\\', '/'), Does.EndWith("teleport.ppe"));
            AudioSource audio = data.FindProperty("_teleportAudio").objectReferenceValue as AudioSource;
            Assert.That(audio, Is.Not.Null);
            Assert.That(audio.clip.name, Is.EqualTo("tinnitus3a"));
            Assert.That(audio.spatialBlend, Is.Zero);
            Assert.That(audio.volume, Is.EqualTo(1f));
        }

        [Test]
        public void MovingAnomaliesAndFluffUseNativeImportedAssets()
        {
            foreach (string name in new[] { "AcidicComet", "Tesla", "Comet", "GeneratorHeat" })
            {
                AnomalyMotion motion = Prefab(name).GetComponent<AnomalyMotion>();
                Assert.That(motion, Is.Not.Null, name);
                var data = new SerializedObject(motion);
                UnityEngine.Object asset = data.FindProperty("_motion").objectReferenceValue;
                Assert.That(asset, Is.Not.Null, name);
                Assert.That(AssetDatabase.GetAssetPath(asset), Does.EndWith(".anm"), name);
                Assert.That(data.FindProperty("_relativeToFirstFrame").boolValue, Is.True, name);
                Assert.That(new SerializedObject(Prefab(name).GetComponent<OverlappingAnomalyVisual>()).FindProperty("_freezeDischargeAtStart").boolValue, Is.True, name);
                Assert.That(new SerializedObject(Prefab(name).GetComponent<AnomalyAudio>()).FindProperty("_followZone").boolValue, Is.True, name);
            }
            foreach (string name in new[] { "GeneratorDust", "GeneratorElectro" })
            {
                Assert.That(Prefab(name).GetComponent<AnomalyMotion>(), Is.Null, name);
                Assert.That(new SerializedObject(Prefab(name).GetComponent<OverlappingAnomalyVisual>()).FindProperty("_freezeDischargeAtStart").boolValue, Is.False, name);
                Assert.That(new SerializedObject(Prefab(name).GetComponent<AnomalyAudio>()).FindProperty("_followZone").boolValue, Is.False, name);
            }
            Transform model = Prefab("BurningFluff").transform.Find("SourceModel");
            Assert.That(model, Is.Not.Null);
            GameObject nativeSource = PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject);
            Assert.That(nativeSource, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(nativeSource), Does.EndWith(".ogf"));
            Assert.That(model.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0));
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.Not.Null);
                    Texture texture = material.HasProperty("_BaseColorMap") ? material.GetTexture("_BaseColorMap") : material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    Assert.That(texture, Is.Not.Null, "Native fuzz source texture act/act_puh must resolve through the importer.");
                }
            }
        }

        private static void AssertLight(Light light, float range, string name)
        {
            Assert.That(light.type, Is.EqualTo(LightType.Point), name);
            Assert.That(light.range, Is.EqualTo(range).Within(0.0001f), name);
            Assert.That(light.intensity, Is.GreaterThan(0f), name);
            Component hdrp = light.GetComponent("HDAdditionalLightData");
            Assert.That(hdrp, Is.Not.Null, name);
            System.Reflection.PropertyInfo dimmer = hdrp.GetType().GetProperty("volumetricDimmer");
            Assert.That(dimmer, Is.Not.Null, name);
            Assert.That((float)dimmer.GetValue(hdrp), Is.Zero, name);
        }

        private static string Difficulty(string section)
        {
            if (section.EndsWith("_weak", StringComparison.Ordinal))
            {
                return "Weak";
            }
            if (section.EndsWith("_average", StringComparison.Ordinal))
            {
                return "Average";
            }
            if (section.EndsWith("_strong", StringComparison.Ordinal))
            {
                return "Strong";
            }
            return string.Empty;
        }

        private static GameObject Variant(string family, string difficulty)
        {
            string path = ROOT + "/" + family + "/Src/Variants/" + family + difficulty + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            return prefab;
        }

        private static GameObject Prefab(string name)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "/" + name + "/" + name + ".prefab");
            Assert.That(prefab, Is.Not.Null, name);
            return prefab;
        }

        private sealed class Definition
        {
            public string Name { get; }
            public string Section { get; }
            public float Awaking { get; }
            public float Blowout { get; }
            public float Accumulate { get; }
            public float IdleRange { get; }
            public float BlowoutRange { get; }
            public bool IgnoreSmall { get; }
            public bool IgnoreNonliving { get; }
            public bool ActorOnly { get; }

            public Definition(string name, string section, float awaking, float blowout, float accumulate, float idleRange, float blowoutRange, bool ignoreSmall, bool ignoreNonliving, bool actorOnly)
            {
                Name = name;
                Section = section;
                Awaking = awaking;
                Blowout = blowout;
                Accumulate = accumulate;
                IdleRange = idleRange;
                BlowoutRange = blowoutRange;
                IgnoreSmall = ignoreSmall;
                IgnoreNonliving = ignoreNonliving;
                ActorOnly = actorOnly;
            }
        }
    }
}
