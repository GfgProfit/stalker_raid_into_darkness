using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerStatTests
    {
        [Test]
        public void EveryNewStatStartsAtTheHundredPointValueAndMaximum()
        {
            var stat = new StalkerStat();
            Assert.That(stat.Value, Is.EqualTo(100f));
            Assert.That(stat.Maximum, Is.EqualTo(100f));
            Assert.That(stat.Capacity, Is.EqualTo(100f));
        }

        [Test]
        public void LoweringTheMaximumClampsTheValueAndRaisingItDoesNotHeal()
        {
            var stat = new StalkerStat();
            stat.SetMaximum(60f);
            Assert.That(stat.Value, Is.EqualTo(60f));
            stat.SetMaximum(100f);
            Assert.That(stat.Value, Is.EqualTo(60f));
            stat.ChangeValue(15f);
            Assert.That(stat.Value, Is.EqualTo(75f));
        }

        [Test]
        public void ValuesAndMaximumCannotLeaveTheirCapacityBounds()
        {
            var stat = new StalkerStat();
            stat.SetValue(-10f);
            Assert.That(stat.Value, Is.Zero);
            stat.SetMaximum(200f);
            stat.SetValue(200f);
            Assert.That(stat.Value, Is.EqualTo(100f));
            stat.SetMaximum(-1f);
            Assert.That(stat.Value, Is.Zero);
            Assert.That(stat.Maximum, Is.Zero);
            stat.ChangeValue(100f);
            Assert.That(stat.Value, Is.Zero);
        }

        [Test]
        public void CapacityChangesPreserveCurrentAndMaximumFractions()
        {
            var stat = new StalkerStat();
            stat.SetMaximum(60f);
            stat.SetValue(40f);
            stat.SetCapacity(200f);
            Assert.That(stat.Value, Is.EqualTo(80f));
            Assert.That(stat.Maximum, Is.EqualTo(120f));
            Assert.That(stat.NormalizedValue, Is.EqualTo(0.4f));
            Assert.That(stat.NormalizedMaximum, Is.EqualTo(0.6f));
        }

        [Test]
        public void InvalidNumbersAreRejectedWithoutChangingState()
        {
            var stat = new StalkerStat();
            Assert.Throws<ArgumentOutOfRangeException>(() => stat.SetValue(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => stat.SetMaximum(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => stat.SetCapacity(0f));
            Assert.That(stat.Value, Is.EqualTo(100f));
            Assert.That(stat.Maximum, Is.EqualTo(100f));
            Assert.That(stat.Capacity, Is.EqualTo(100f));
        }

        [Test]
        public void SixConditionStatsHaveIndependentHundredPointState()
        {
            WithConditions((first, second, profile) =>
            {
                foreach (StalkerStatKind kind in Enum.GetValues(typeof(StalkerStatKind)))
                {
                    Assert.That(first.GetStat(kind).Value, Is.EqualTo(100f), kind.ToString());
                    Assert.That(first.GetStat(kind).Maximum, Is.EqualTo(100f), kind.ToString());
                    Assert.That(first.GetStat(kind).Capacity, Is.EqualTo(100f), kind.ToString());
                }
                first.SetHealth(40f);
                first.SetPsyHealth(30f);
                first.SetPower(20f);
                first.SetHunger(10f);
                first.SetThirst(50f);
                first.SetSleep(60f);
                Assert.That(first.Health, Is.EqualTo(40f));
                Assert.That(first.PsyHealth, Is.EqualTo(30f));
                Assert.That(first.Power, Is.EqualTo(20f));
                Assert.That(first.Hunger, Is.EqualTo(10f));
                Assert.That(first.Thirst, Is.EqualTo(50f));
                Assert.That(first.Sleep, Is.EqualTo(60f));
                foreach (StalkerStatKind kind in Enum.GetValues(typeof(StalkerStatKind)))
                {
                    Assert.That(second.GetStat(kind).Value, Is.EqualTo(100f), kind.ToString());
                }
            });
        }

        [Test]
        public void ProfileReinitializationDoesNotRestoreRuntimeStats()
        {
            WithConditions((first, second, profile) =>
            {
                first.SetMaxHealth(60f);
                first.SetHealth(40f);
                first.SetPsyHealth(20f);
                first.SetPower(30f);
                first.Initialize(profile);
                Assert.That(first.Health, Is.EqualTo(40f));
                Assert.That(first.MaxHealth, Is.EqualTo(60f));
                Assert.That(first.PsyHealth, Is.EqualTo(20f));
                Assert.That(first.Power, Is.EqualTo(30f));
            });
        }

        [Test]
        public void GameConfigCapacityReachesAllSixStatsWithoutMutatingTheConfig()
        {
            WithConditions((first, second, profile) =>
            {
                var configuration = ScriptableObject.CreateInstance<GameConfig>();
                try
                {
                    var serialized = new SerializedObject(configuration);
                    serialized.FindProperty("_defaultPlayerStatCapacity").floatValue = 200f;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    first.SetMaxHealth(60f);
                    first.SetHealth(40f);
                    first.ApplyConfiguration(configuration);
                    Assert.That(first.Health, Is.EqualTo(80f));
                    Assert.That(first.MaxHealth, Is.EqualTo(120f));
                    foreach (StalkerStatKind kind in Enum.GetValues(typeof(StalkerStatKind)))
                    {
                        Assert.That(first.GetStat(kind).Capacity, Is.EqualTo(200f), kind.ToString());
                    }
                    first.SetPower(50f);
                    Assert.That(configuration.DefaultPlayerStatCapacity, Is.EqualTo(200f));
                    Assert.That(second.PowerCapacity, Is.EqualTo(100f));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(configuration);
                }
            });
        }

        [Test]
        public void SharedConfigurationEditsReachBothPlayersWithoutResettingRuntimeFractions()
        {
            WithConditions((first, second, profile) =>
            {
                var configuration = ScriptableObject.CreateInstance<GameConfig>();
                try
                {
                    first.ApplyConfiguration(configuration);
                    second.ApplyConfiguration(configuration);
                    first.SetMaxHealth(60f);
                    first.SetHealth(40f);
                    int previousRevision = configuration.Revision;
                    var serialized = new SerializedObject(configuration);
                    serialized.FindProperty("_defaultPlayerStatCapacity").floatValue = 200f;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Assert.That(configuration.Revision, Is.GreaterThan(previousRevision));
                    first.Tick(StalkerMoveFlags.None, false, 0f, 0f);
                    second.Tick(StalkerMoveFlags.None, false, 0f, 0f);
                    Assert.That(first.Health, Is.EqualTo(80f));
                    Assert.That(first.MaxHealth, Is.EqualTo(120f));
                    Assert.That(second.Health, Is.EqualTo(200f));
                    Assert.That(second.PowerCapacity, Is.EqualTo(200f));
                    Assert.That(configuration.DefaultPlayerStatCapacity, Is.EqualTo(200f));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(configuration);
                }
            });
        }

        [Test]
        public void ExistingMovementThresholdsUseNormalizedStaminaAfterTheUnitChange()
        {
            WithConditions((first, second, profile) =>
            {
                first.SetPower(5f);
                Assert.That(first.IsCantSprint(), Is.True);
                first.SetPower(9f);
                Assert.That(first.IsCantSprint(), Is.True);
                first.SetPower(11f);
                Assert.That(first.IsCantSprint(), Is.False);
                first.SetHealth(8f);
                Assert.That(first.IsLimping(), Is.True);
                first.SetHealth(13f);
                first.SetPower(30f);
                Assert.That(first.IsLimping(), Is.False);
            });
        }

        private static void WithConditions(Action<StalkerActorCondition, StalkerActorCondition, StalkerMovementProfile> test)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(nameof(StalkerStatTests));
            SceneManager.MoveGameObjectToScene(root, scene);
            root.SetActive(false);
            var child = new GameObject("SecondPlayer");
            child.transform.SetParent(root.transform, false);
            var profile = ScriptableObject.CreateInstance<StalkerMovementProfile>();
            try
            {
                StalkerActorCondition first = root.AddComponent<StalkerActorCondition>();
                StalkerActorCondition second = child.AddComponent<StalkerActorCondition>();
                first.Initialize(profile);
                second.Initialize(profile);
                test(first, second, profile);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(profile);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
