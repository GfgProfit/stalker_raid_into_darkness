using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyBalanceAssetTests
    {
        private const string AUTHORING_PATH = "Assets/_Source/Anomalies/Balance/AnomalyBalance.json";
        private const string BALANCE_ROOT = "Assets/_Source/Anomalies/Balance/";
        private const string CATALOG_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Editor/Levels/XRayLevelAnomalyCatalog.asset";
        private const string PLAYER_PATH = "Assets/_Source/FirstPerson/FPS_Player.prefab";

        [Serializable]
        private sealed class Authoring
        {
            public int schemaVersion;
            public float radiationRecoveryPerHour;
            public float radiationDamagePerSecond;
            public Family[] families;
            public Variant[] variants;
            public Binding[] prefabBindings;
            public EfficiencyPoint[] protectionEfficiencyCurve;
        }

        [Serializable]
        private sealed class Family
        {
            public string id;
            public string damageType;
            public float baseDamage;
            public string unit;
        }

        [Serializable]
        private sealed class Variant
        {
            public string section;
            public string familyId;
            public string prefabPath;
            public string strength;
            public float multiplier;
            public bool continuous;
            public float interval;
        }

        [Serializable]
        private sealed class Binding
        {
            public string assetPath;
            public string section;
            public bool isBase;
        }

        [Serializable]
        private sealed class EfficiencyPoint
        {
            public float conditionPercent;
            public float efficiencyPercent;
        }

        [Test]
        public void GlobalBalanceContainsEveryAuthoredFamilyAndVariantWithHumanScaleDamage()
        {
            Authoring authoring = LoadAuthoring();
            AnomalyBalanceConfig balance = LoadBalance();
            Assert.That(authoring.schemaVersion, Is.EqualTo(1));
            Assert.That(authoring.families.Length, Is.EqualTo(24));
            Assert.That(authoring.variants.Length, Is.EqualTo(66));
            Assert.That(balance.Archetypes.Count, Is.EqualTo(authoring.families.Length));
            Assert.That(balance.Variants.Count, Is.EqualTo(authoring.variants.Length));
            Assert.That(balance.TryValidate(out string diagnostic), Is.True, diagnostic);
            var families = new Dictionary<string, AnomalyArchetypeConfig>(StringComparer.Ordinal);
            foreach (AnomalyArchetypeConfig archetype in balance.Archetypes)
            {
                Assert.That(families.TryAdd(archetype.Id, archetype), Is.True, archetype.Id);
                AssertProductionAsset(archetype);
            }
            foreach (Family expected in authoring.families)
            {
                Assert.That(families.TryGetValue(expected.id, out AnomalyArchetypeConfig actual), Is.True, expected.id);
                Assert.That(actual.BaseDamage, Is.EqualTo(expected.baseDamage).Within(0.00001f), expected.id);
                Assert.That(actual.DamageType, Is.EqualTo(Enum.Parse<AnomalyDamageType>(expected.damageType)), expected.id);
                Assert.That(expected.unit, Is.Not.Empty, expected.id);
            }
            var sections = new HashSet<string>(StringComparer.Ordinal);
            foreach (Variant expected in authoring.variants)
            {
                Assert.That(sections.Add(expected.section), Is.True, expected.section);
                AnomalyVariantConfig actual = balance.FindVariant(expected.section);
                Assert.That(actual, Is.Not.Null, expected.section);
                Assert.That(actual.Archetype, Is.SameAs(families[expected.familyId]), expected.section);
                Assert.That(actual.Strength, Is.EqualTo(Enum.Parse<AnomalyStrength>(expected.strength)), expected.section);
                Assert.That(actual.DamageMultiplier, Is.EqualTo(expected.multiplier).Within(0.00001f), expected.section);
                Assert.That(actual.InputDamage, Is.EqualTo(actual.Archetype.BaseDamage * expected.multiplier).Within(0.0001f), expected.section);
                Assert.That(actual.Continuous, Is.EqualTo(expected.continuous), expected.section);
                Assert.That(actual.HitInterval, Is.EqualTo(expected.interval).Within(0.00001f), expected.section);
                Assert.That(expected.multiplier * 4f, Is.EqualTo(Mathf.Round(expected.multiplier * 4f)), expected.section);
                Assert.That(actual.TryValidate(out diagnostic), Is.True, diagnostic);
                AssertProductionAsset(actual);
            }
            Assert.That(balance.FindVariant("unregistered_anomaly"), Is.Null);
        }

        [Test]
        public void EveryAuthoredPrefabBindsItsCanonicalProfileAndSerializedSourceSection()
        {
            Authoring authoring = LoadAuthoring();
            AnomalyBalanceConfig balance = LoadBalance();
            Assert.That(authoring.prefabBindings.Length, Is.EqualTo(56));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int bases = 0;
            foreach (Binding binding in authoring.prefabBindings)
            {
                Assert.That(seen.Add(binding.assetPath), Is.True, binding.assetPath);
                GameObject prefab = LoadPrefab(binding.assetPath);
                AnomalyDamageSource[] sources = prefab.GetComponents<AnomalyDamageSource>();
                Assert.That(sources.Length, Is.EqualTo(1), binding.assetPath);
                AnomalyVariantConfig expected = balance.FindVariant(binding.section);
                Assert.That(expected, Is.Not.Null, binding.section);
                Assert.That(sources[0].Variant, Is.SameAs(expected), binding.assetPath);
                SerializedProperty section = new SerializedObject(sources[0]).FindProperty("_sourceSection");
                Assert.That(section, Is.Not.Null, binding.assetPath);
                Assert.That(section.stringValue, Is.EqualTo(binding.section), binding.assetPath);
                if (binding.isBase)
                {
                    bases++;
                }
            }
            Assert.That(bases, Is.EqualTo(24));
        }

        [Test]
        public void EveryCatalogAliasResolvesToTheSameFamilyStrengthAndDamageAsItsBoundPrefab()
        {
            Authoring authoring = LoadAuthoring();
            AnomalyBalanceConfig balance = LoadBalance();
            UnityEngine.Object catalog = AssetDatabase.LoadMainAssetAtPath(CATALOG_PATH);
            Assert.That(catalog, Is.Not.Null);
            SerializedProperty entries = new SerializedObject(catalog).FindProperty("_entries");
            Assert.That(entries, Is.Not.Null);
            var catalogPaths = new HashSet<string>(StringComparer.Ordinal);
            var catalogSections = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                string section = entry.FindPropertyRelative("_section").stringValue;
                Assert.That(catalogSections.Add(section), Is.True, section);
                var prefab = entry.FindPropertyRelative("_prefab").objectReferenceValue as GameObject;
                Assert.That(prefab, Is.Not.Null, section);
                catalogPaths.Add(AssetDatabase.GetAssetPath(prefab));
                AnomalyVariantConfig expected = balance.FindVariant(section);
                Assert.That(expected, Is.Not.Null, section);
                AnomalyDamageSource source = prefab.GetComponent<AnomalyDamageSource>();
                Assert.That(source, Is.Not.Null, section);
                AssertEquivalentProfiles(source.Variant, expected, section);
            }
            Assert.That(catalogSections.Count, Is.EqualTo(authoring.variants.Length));
            Assert.That(catalogPaths.Count, Is.EqualTo(49));
            var authoredPaths = new HashSet<string>(StringComparer.Ordinal);
            int additionalBases = 0;
            foreach (Binding binding in authoring.prefabBindings)
            {
                authoredPaths.Add(binding.assetPath);
                if (!catalogPaths.Contains(binding.assetPath))
                {
                    Assert.That(binding.isBase, Is.True, binding.assetPath);
                    additionalBases++;
                }
            }
            Assert.That(additionalBases, Is.EqualTo(7));
            Assert.That(authoredPaths.IsSupersetOf(catalogPaths), Is.True);
            foreach (Variant variant in authoring.variants)
            {
                Assert.That(catalogPaths.Contains(variant.prefabPath), Is.True, variant.section);
            }
        }

        [Test]
        public void PlayerReceiverUsesTheActualConditionAndAllowsExternalProtectionBinding()
        {
            GameObject player = LoadPrefab(PLAYER_PATH);
            AnomalyDamageReceiver[] receivers = player.GetComponentsInChildren<AnomalyDamageReceiver>(true);
            Assert.That(receivers.Length, Is.EqualTo(1));
            AnomalyDamageReceiver receiver = receivers[0];
            StalkerActorCondition condition = receiver.GetComponent<StalkerActorCondition>();
            Assert.That(condition, Is.Not.Null);
            Assert.That(new SerializedObject(receiver).FindProperty("_condition").objectReferenceValue, Is.SameAs(condition));
            SerializedProperty sources = new SerializedObject(receiver).FindProperty("_protectionSources");
            Assert.That(sources, Is.Not.Null);
            for (int i = 0; i < sources.arraySize; i++)
            {
                Assert.That(sources.GetArrayElementAtIndex(i).objectReferenceValue, Is.InstanceOf<IAnomalyProtectionSource>());
            }
        }

        [Test]
        public void SharedEfficiencyCurveAndRadiationRatesMatchAuthoringWithoutMutatingAssets()
        {
            Authoring authoring = LoadAuthoring();
            AnomalyBalanceConfig balance = LoadBalance();
            string before = EditorJsonUtility.ToJson(balance);
            Assert.That(balance.RadiationRecoveryPerHour, Is.EqualTo(authoring.radiationRecoveryPerHour));
            Assert.That(balance.RadiationDamagePerSecond, Is.EqualTo(authoring.radiationDamagePerSecond));
            foreach (EfficiencyPoint point in authoring.protectionEfficiencyCurve)
            {
                Assert.That(balance.EvaluateConditionEfficiency(point.conditionPercent / 100f), Is.EqualTo(point.efficiencyPercent / 100f).Within(0.00001f));
            }
            float previous = 0f;
            for (int i = 0; i <= 1000; i++)
            {
                float efficiency = balance.EvaluateConditionEfficiency(i / 1000f);
                Assert.That(float.IsFinite(efficiency), Is.True);
                Assert.That(efficiency, Is.InRange(previous - 0.00001f, 1f));
                previous = efficiency;
            }
            Assert.That(balance.EvaluateConditionEfficiency(1f), Is.GreaterThan(balance.EvaluateConditionEfficiency(0.95f)));
            Assert.That(balance.EvaluateConditionEfficiency(0.95f), Is.GreaterThan(balance.EvaluateConditionEfficiency(0.9f)));
            Assert.That(EditorJsonUtility.ToJson(balance), Is.EqualTo(before));
        }

        [Test]
        public void InvalidNumericDataIsRejectedWithoutEditingProductionAssets()
        {
            AnomalyBalanceConfig balance = LoadBalance();
            AnomalyArchetypeConfig archetype = UnityEngine.Object.Instantiate(balance.Archetypes[0]);
            AnomalyVariantConfig variant = UnityEngine.Object.Instantiate(balance.Variants[0]);
            AnomalyBalanceConfig invalidBalance = UnityEngine.Object.Instantiate(balance);
            try
            {
                SetField(archetype, "_baseDamage", float.NaN);
                Assert.That(archetype.TryValidate(out _), Is.False);
                SetField(archetype, "_baseDamage", float.PositiveInfinity);
                Assert.That(archetype.TryValidate(out _), Is.False);
                SetField(variant, "_damageMultiplier", float.NaN);
                Assert.That(variant.TryValidate(out _), Is.False);
                SetField(variant, "_damageMultiplier", float.PositiveInfinity);
                Assert.That(variant.TryValidate(out _), Is.False);
                SetField(invalidBalance, "_radiationDamagePerSecond", float.NaN);
                Assert.That(invalidBalance.TryValidate(out _), Is.False);
                SetField(invalidBalance, "_radiationDamagePerSecond", float.PositiveInfinity);
                Assert.That(invalidBalance.TryValidate(out _), Is.False);
                SetField(invalidBalance, "_radiationDamagePerSecond", 0.2f);
                SetField(invalidBalance, "_conditionEfficiencyCurve", new AnimationCurve(new Keyframe(0f, 0f, float.PositiveInfinity, float.PositiveInfinity), new Keyframe(100f, 100f, float.PositiveInfinity, float.PositiveInfinity)));
                Assert.That(invalidBalance.TryValidate(out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(archetype);
                UnityEngine.Object.DestroyImmediate(variant);
                UnityEngine.Object.DestroyImmediate(invalidBalance);
            }
        }

        private static Authoring LoadAuthoring()
        {
            Assert.That(File.Exists(AUTHORING_PATH), Is.True);
            Authoring authoring = JsonUtility.FromJson<Authoring>(File.ReadAllText(AUTHORING_PATH));
            Assert.That(authoring, Is.Not.Null);
            return authoring;
        }

        private static AnomalyBalanceConfig LoadBalance()
        {
            GameConfig config = GameConfig.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.AnomalyBalance, Is.Not.Null);
            AssertProductionAsset(config.AnomalyBalance);
            return config.AnomalyBalance;
        }

        private static GameObject LoadPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            return prefab;
        }

        private static void AssertProductionAsset(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            Assert.That(path.StartsWith(BALANCE_ROOT, StringComparison.Ordinal), Is.True, path);
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.Not.Empty, path);
        }

        private static void AssertEquivalentProfiles(AnomalyVariantConfig actual, AnomalyVariantConfig expected, string context)
        {
            Assert.That(actual, Is.Not.Null, context);
            Assert.That(actual.Archetype, Is.SameAs(expected.Archetype), context);
            Assert.That(actual.Strength, Is.EqualTo(expected.Strength), context);
            Assert.That(actual.DamageMultiplier, Is.EqualTo(expected.DamageMultiplier), context);
            Assert.That(actual.InputDamage, Is.EqualTo(expected.InputDamage), context);
            Assert.That(actual.Continuous, Is.EqualTo(expected.Continuous), context);
            Assert.That(actual.HitInterval, Is.EqualTo(expected.HitInterval), context);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
