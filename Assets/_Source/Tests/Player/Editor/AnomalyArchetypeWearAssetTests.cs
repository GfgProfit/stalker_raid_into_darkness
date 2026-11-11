using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyArchetypeWearAssetTests
    {
        private const string AUTHORING_PATH = "Assets/_Source/Anomalies/Balance/AnomalyBalance.json";
        private const string ARCHETYPE_ROOT = "Assets/_Source/Anomalies/Balance/Archetypes/";

        [Serializable]
        private sealed class Authoring
        {
            public Family[] families;
        }

        [Serializable]
        private sealed class Family
        {
            public string id;
            public string damageType;
            public float baseDamage;
            public float equipmentWearPerHitPercent;
        }

        [TestCase("AcidicComet", AnomalyDamageType.ChemicalBurn, 180f, 0.32f)]
        [TestCase("Tesla", AnomalyDamageType.Shock, 145f, 0.24f)]
        [TestCase("Comet", AnomalyDamageType.Burn, 610f, 0.4f)]
        [TestCase("GeneratorDust", AnomalyDamageType.Shock, 160f, 0.18f)]
        [TestCase("GeneratorElectro", AnomalyDamageType.Shock, 160f, 0.22f)]
        [TestCase("GeneratorHeat", AnomalyDamageType.Burn, 680f, 0.44f)]
        [TestCase("BurningFluff", AnomalyDamageType.ChemicalBurn, 60f, 0.8f)]
        [TestCase("Kisel", AnomalyDamageType.ChemicalBurn, 50f, 1.2f)]
        [TestCase("Impulse", AnomalyDamageType.Shock, 125f, 1.5f)]
        [TestCase("AcidicFog", AnomalyDamageType.ChemicalBurn, 3f, 0.025f)]
        [TestCase("PsiField", AnomalyDamageType.Telepatic, 11f, 0f)]
        [TestCase("RadiationField", AnomalyDamageType.Radiation, 0.3f, 0f)]
        [TestCase("ThermalField", AnomalyDamageType.Burn, 7f, 0.04f)]
        [TestCase("Vortex", AnomalyDamageType.Strike, 65f, 1.6f)]
        [TestCase("Gazirovka", AnomalyDamageType.ChemicalBurn, 40f, 1f)]
        [TestCase("Electra", AnomalyDamageType.Shock, 55f, 1.1f)]
        [TestCase("Mine", AnomalyDamageType.Explosion, 2275f, 3f)]
        [TestCase("Carousel", AnomalyDamageType.Strike, 55f, 1.8f)]
        [TestCase("Tramplin", AnomalyDamageType.Strike, 130f, 1.3f)]
        [TestCase("Zharka", AnomalyDamageType.Burn, 115f, 1.4f)]
        [TestCase("WishGranter", AnomalyDamageType.Telepatic, 10f, 0f)]
        [TestCase("Weightlessness", AnomalyDamageType.Radiation, 20f, 0f)]
        [TestCase("Teleport", AnomalyDamageType.None, 0f, 0f)]
        [TestCase("Heat", AnomalyDamageType.Burn, 100f, 0.65f)]
        public void EveryArchetypeKeepsItsDamageAndStoresItsOwnExplicitWear(string id, AnomalyDamageType damageType, float baseDamage, float expectedWear)
        {
            Authoring authoring = JsonUtility.FromJson<Authoring>(File.ReadAllText(AUTHORING_PATH));
            Assert.That(authoring.families.Length, Is.EqualTo(24));
            Family family = Array.Find(authoring.families, entry => entry.id == id);
            Assert.That(family, Is.Not.Null, id);
            Assert.That(family.damageType, Is.EqualTo(damageType.ToString()), id);
            Assert.That(family.baseDamage, Is.EqualTo(baseDamage).Within(0.0001f), id);
            Assert.That(family.equipmentWearPerHitPercent, Is.EqualTo(expectedWear).Within(0.000001f), id);
            AnomalyArchetypeConfig archetype = AssetDatabase.LoadAssetAtPath<AnomalyArchetypeConfig>(ARCHETYPE_ROOT + id + ".asset");
            Assert.That(archetype, Is.Not.Null, id);
            Assert.That(archetype.Id, Is.EqualTo(id));
            Assert.That(archetype.DamageType, Is.EqualTo(damageType), id);
            Assert.That(archetype.BaseDamage, Is.EqualTo(baseDamage).Within(0.0001f), id);
            Assert.That(archetype.EquipmentWearPerHitPercent, Is.EqualTo(expectedWear).Within(0.000001f), id);
            Assert.That(archetype.TryValidate(out string diagnostic), Is.True, diagnostic);
            AnomalyBalanceConfig balance = LoadBalance();
            int matchingReferences = 0;
            foreach (AnomalyArchetypeConfig registered in balance.Archetypes)
            {
                if (registered == archetype)
                {
                    matchingReferences++;
                }
            }
            Assert.That(matchingReferences, Is.EqualTo(1), id);
        }

        [TestCase("fireball_acidic_zone", "AcidicComet", AnomalyStrength.Average, 1f, false, 180f)]
        [TestCase("fireball_electric_zone", "Tesla", AnomalyStrength.Average, 1f, false, 145f)]
        [TestCase("fireball_zone", "Comet", AnomalyStrength.Average, 1f, false, 610f)]
        [TestCase("generator_dust_static", "GeneratorDust", AnomalyStrength.Average, 1f, false, 160f)]
        [TestCase("generator_electra", "GeneratorElectro", AnomalyStrength.Average, 1f, false, 160f)]
        [TestCase("generator_torrid", "GeneratorHeat", AnomalyStrength.Average, 1f, false, 680f)]
        [TestCase("zone_burning_fuzz", "BurningFluff", AnomalyStrength.Average, 1f, false, 60f)]
        [TestCase("zone_burning_fuzz_average", "BurningFluff", AnomalyStrength.Average, 1f, false, 60f)]
        [TestCase("zone_burning_fuzz_strong", "BurningFluff", AnomalyStrength.Strong, 1.5f, false, 90f)]
        [TestCase("zone_burning_fuzz_weak", "BurningFluff", AnomalyStrength.Weak, 0.75f, false, 45f)]
        [TestCase("zone_burning_fuzz1", "BurningFluff", AnomalyStrength.Average, 1f, false, 60f)]
        [TestCase("zone_buzz", "Kisel", AnomalyStrength.Average, 1f, false, 50f)]
        [TestCase("zone_buzz_average", "Kisel", AnomalyStrength.Average, 1f, false, 50f)]
        [TestCase("zone_buzz_strong", "Kisel", AnomalyStrength.Strong, 1.5f, false, 75f)]
        [TestCase("zone_buzz_weak", "Kisel", AnomalyStrength.Weak, 0.5f, false, 25f)]
        [TestCase("zone_emi", "Impulse", AnomalyStrength.Average, 1f, false, 125f)]
        [TestCase("zone_field_acidic_average", "AcidicFog", AnomalyStrength.Average, 1f, true, 3f)]
        [TestCase("zone_field_acidic_strong", "AcidicFog", AnomalyStrength.Strong, 1.5f, true, 4.5f)]
        [TestCase("zone_field_acidic_weak", "AcidicFog", AnomalyStrength.Weak, 0.75f, true, 2.25f)]
        [TestCase("zone_field_psychic_average", "PsiField", AnomalyStrength.Average, 1f, true, 11f)]
        [TestCase("zone_field_psychic_strong", "PsiField", AnomalyStrength.Strong, 1.25f, true, 13.75f)]
        [TestCase("zone_field_psychic_weak", "PsiField", AnomalyStrength.Weak, 0.5f, true, 5.5f)]
        [TestCase("zone_field_radioactive_average", "RadiationField", AnomalyStrength.Average, 1f, true, 0.3f)]
        [TestCase("zone_field_radioactive_strong", "RadiationField", AnomalyStrength.Strong, 1.5f, true, 0.45f)]
        [TestCase("zone_field_radioactive_weak", "RadiationField", AnomalyStrength.Weak, 0.75f, true, 0.225f)]
        [TestCase("zone_field_thermal_average", "ThermalField", AnomalyStrength.Average, 1f, true, 7f)]
        [TestCase("zone_field_thermal_strong", "ThermalField", AnomalyStrength.Strong, 1.5f, true, 10.5f)]
        [TestCase("zone_field_thermal_weak", "ThermalField", AnomalyStrength.Weak, 0.75f, true, 5.25f)]
        [TestCase("zone_gravi_zone", "Vortex", AnomalyStrength.Average, 1f, false, 65f)]
        [TestCase("zone_mine_acidic_average", "Gazirovka", AnomalyStrength.Average, 1f, false, 40f)]
        [TestCase("zone_mine_acidic_strong", "Gazirovka", AnomalyStrength.Strong, 1.5f, false, 60f)]
        [TestCase("zone_mine_acidic_weak", "Gazirovka", AnomalyStrength.Weak, 0.75f, false, 30f)]
        [TestCase("zone_mine_chemical_average", "Kisel", AnomalyStrength.Average, 1f, false, 50f)]
        [TestCase("zone_mine_chemical_strong", "Kisel", AnomalyStrength.Strong, 1.5f, false, 75f)]
        [TestCase("zone_mine_chemical_weak", "Kisel", AnomalyStrength.Weak, 0.5f, false, 25f)]
        [TestCase("zone_mine_electric_average", "Electra", AnomalyStrength.Average, 1f, false, 55f)]
        [TestCase("zone_mine_electric_strong", "Electra", AnomalyStrength.Strong, 1.25f, false, 68.75f)]
        [TestCase("zone_mine_electric_weak", "Electra", AnomalyStrength.Weak, 0.75f, false, 41.25f)]
        [TestCase("zone_mine_field_soc", "Mine", AnomalyStrength.Average, 1f, false, 2275f)]
        [TestCase("zone_mine_gravitational_average", "Vortex", AnomalyStrength.Average, 1f, false, 65f)]
        [TestCase("zone_mine_gravitational_strong", "Carousel", AnomalyStrength.Strong, 1.5f, false, 82.5f)]
        [TestCase("zone_mine_gravitational_weak", "Tramplin", AnomalyStrength.Weak, 0.5f, false, 65f)]
        [TestCase("zone_mine_static_strong", "Electra", AnomalyStrength.Strong, 1.25f, false, 68.75f)]
        [TestCase("zone_mine_steam_average", "Zharka", AnomalyStrength.Average, 1f, false, 115f)]
        [TestCase("zone_mine_steam_weak", "Zharka", AnomalyStrength.Weak, 0.75f, false, 86.25f)]
        [TestCase("zone_mine_thermal_average", "Zharka", AnomalyStrength.Average, 1f, false, 115f)]
        [TestCase("zone_mine_thermal_strong", "Zharka", AnomalyStrength.Strong, 1.25f, false, 143.75f)]
        [TestCase("zone_mine_thermal_weak", "Zharka", AnomalyStrength.Weak, 0.75f, false, 86.25f)]
        [TestCase("zone_monolith", "WishGranter", AnomalyStrength.Average, 1f, true, 10f)]
        [TestCase("zone_mosquito_bald", "Tramplin", AnomalyStrength.Average, 1f, false, 130f)]
        [TestCase("zone_mosquito_bald_average", "Tramplin", AnomalyStrength.Average, 1f, false, 130f)]
        [TestCase("zone_mosquito_bald_strong", "Tramplin", AnomalyStrength.Strong, 1.75f, false, 227.5f)]
        [TestCase("zone_mosquito_bald_weak", "Tramplin", AnomalyStrength.Weak, 0.5f, false, 65f)]
        [TestCase("zone_no_gravity", "Weightlessness", AnomalyStrength.Average, 1f, false, 20f)]
        [TestCase("zone_radioactive_average", "RadiationField", AnomalyStrength.Average, 1f, true, 0.3f)]
        [TestCase("zone_radioactive_strong", "RadiationField", AnomalyStrength.Strong, 1.5f, true, 0.45f)]
        [TestCase("zone_radioactive_weak", "RadiationField", AnomalyStrength.Weak, 0.75f, true, 0.225f)]
        [TestCase("zone_teleport", "Teleport", AnomalyStrength.Average, 1f, false, 0f)]
        [TestCase("zone_witches_galantine", "Electra", AnomalyStrength.Average, 1f, false, 55f)]
        [TestCase("zone_witches_galantine_average", "Electra", AnomalyStrength.Average, 1f, false, 55f)]
        [TestCase("zone_witches_galantine_strong", "Electra", AnomalyStrength.Strong, 1.25f, false, 68.75f)]
        [TestCase("zone_witches_galantine_weak", "Electra", AnomalyStrength.Weak, 0.75f, false, 41.25f)]
        [TestCase("zone_zhar", "Heat", AnomalyStrength.Average, 1f, false, 100f)]
        [TestCase("zone_zharka_static_average", "Zharka", AnomalyStrength.Average, 1f, false, 115f)]
        [TestCase("zone_zharka_static_strong", "Zharka", AnomalyStrength.Strong, 1.25f, false, 143.75f)]
        [TestCase("zone_zharka_static_weak", "Zharka", AnomalyStrength.Weak, 0.75f, false, 86.25f)]
        public void EveryVariantRetainsItsPreWearDamageStrengthAndFrequency(string section, string archetypeId, AnomalyStrength strength, float multiplier, bool continuous, float inputDamage)
        {
            AnomalyBalanceConfig balance = LoadBalance();
            Assert.That(balance.Variants.Count, Is.EqualTo(66));
            AnomalyVariantConfig variant = balance.FindVariant(section);
            Assert.That(variant, Is.Not.Null, section);
            Assert.That(variant.SourceSection, Is.EqualTo(section));
            Assert.That(variant.Archetype.Id, Is.EqualTo(archetypeId), section);
            Assert.That(variant.Strength, Is.EqualTo(strength), section);
            Assert.That(variant.DamageMultiplier, Is.EqualTo(multiplier).Within(0.000001f), section);
            Assert.That(variant.InputDamage, Is.EqualTo(inputDamage).Within(0.0001f), section);
            Assert.That(variant.Continuous, Is.EqualTo(continuous), section);
            Assert.That(variant.HitInterval, Is.EqualTo(0.1f).Within(0.000001f), section);
        }

        [TestCase(0f, 0f)]
        [TestCase(25f, 17.5f)]
        [TestCase(50f, 35f)]
        [TestCase(60f, 48.5f)]
        [TestCase(70f, 62f)]
        [TestCase(75f, 70f)]
        [TestCase(80f, 78f)]
        [TestCase(82.5f, 82f)]
        [TestCase(85f, 86f)]
        [TestCase(87.5f, 89.5f)]
        [TestCase(90f, 93f)]
        [TestCase(92.5f, 95f)]
        [TestCase(95f, 97f)]
        [TestCase(97.5f, 98.5f)]
        [TestCase(100f, 100f)]
        public void ConditionEfficiencyRetainsThePreWearCurve(float conditionPercent, float efficiencyPercent)
        {
            Assert.That(LoadBalance().EvaluateConditionEfficiency(conditionPercent / 100f), Is.EqualTo(efficiencyPercent / 100f).Within(0.000001f));
            Assert.That(AnomalyProtectionMath.DefaultEfficiency(conditionPercent / 100f), Is.EqualTo(efficiencyPercent / 100f).Within(0.000001f));
        }

        [Test]
        public void RadiationRatesRemainUnchangedAndAllHelmetProtectionsApply()
        {
            AnomalyBalanceConfig balance = LoadBalance();
            Assert.That(balance.RadiationRecoveryPerHour, Is.EqualTo(4f));
            Assert.That(balance.RadiationDamagePerSecond, Is.EqualTo(0.2f));
            foreach (AnomalyDamageType type in Enum.GetValues(typeof(AnomalyDamageType)))
            {
                Assert.That(balance.HelmetApplies(type), Is.EqualTo(type != AnomalyDamageType.None), type.ToString());
            }
        }

        [TestCase(float.NaN, false)]
        [TestCase(float.PositiveInfinity, false)]
        [TestCase(float.NegativeInfinity, false)]
        [TestCase(-0.001f, false)]
        [TestCase(100.001f, false)]
        [TestCase(0f, true)]
        [TestCase(100f, true)]
        public void WearValidationEnforcesFinitePercentBoundsWithoutChangingTheAsset(float wear, bool valid)
        {
            AnomalyArchetypeConfig asset = AssetDatabase.LoadAssetAtPath<AnomalyArchetypeConfig>(ARCHETYPE_ROOT + "Electra.asset");
            Assert.That(asset, Is.Not.Null);
            string before = EditorJsonUtility.ToJson(asset);
            AnomalyArchetypeConfig instance = UnityEngine.Object.Instantiate(asset);
            try
            {
                var serialized = new SerializedObject(instance);
                serialized.FindProperty("_equipmentWearPerHitPercent").floatValue = wear;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(instance.TryValidate(out _), Is.EqualTo(valid));
                Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(before));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static AnomalyBalanceConfig LoadBalance()
        {
            GameConfig config = GameConfig.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config.AnomalyBalance, Is.Not.Null);
            Assert.That(config.AnomalyBalance.Archetypes.Count, Is.EqualTo(24));
            return config.AnomalyBalance;
        }
    }
}
