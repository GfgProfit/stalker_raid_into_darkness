using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerVegetationConditionTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _root;

            public StalkerMovementProfile Profile { get; }
            public StalkerActorCondition Condition { get; }

            public Rig(bool materialDrain = true)
            {
                _scene = EditorSceneManager.NewPreviewScene();
                _root = new GameObject(nameof(StalkerVegetationConditionTests));
                _root.hideFlags = HideFlags.HideAndDontSave;
                _root.SetActive(false);
                SceneManager.MoveGameObjectToScene(_root, _scene);
                Profile = ScriptableObject.CreateInstance<StalkerMovementProfile>();
                Profile.hideFlags = HideFlags.HideAndDontSave;
                var serialized = new SerializedObject(Profile);
                serialized.FindProperty("_staminaMod._enableMaterialDrain").boolValue = materialDrain;
                serialized.FindProperty("_staminaMod._enableUphillDrain").boolValue = false;
                serialized.FindProperty("_staminaMod._enableUphillSpeedModifier").boolValue = false;
                serialized.FindProperty("_staminaMod._enableStaminaSpeedModifier").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Condition = _root.AddComponent<StalkerActorCondition>();
                Condition.Initialize(Profile);
                Condition.SetPower(0.8f);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_root);
                UnityEngine.Object.DestroyImmediate(Profile);
                EditorSceneManager.ClosePreviewScene(_scene);
            }
        }

        [TestCase(StalkerSurfaceType.Bush)]
        [TestCase(StalkerSurfaceType.BushSux)]
        public void VegetationFootstepReducesRunCoefficientsAndKeepsSprintAvailable(StalkerSurfaceType surface)
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetFootstepSurface(surface);

                Assert.That(rig.Condition.RunCoef, Is.EqualTo(rig.Profile.Actor.RunCoef * 0.85f).Within(0.000001f));
                Assert.That(rig.Condition.RunBackCoef, Is.EqualTo(rig.Profile.Actor.RunBackCoef * 0.85f).Within(0.000001f));
                Assert.That(rig.Condition.SprintKoef, Is.EqualTo(rig.Profile.Actor.SprintKoef));
                Assert.That(rig.Condition.IsCantSprint(), Is.False);
                Assert.That(rig.Condition.SprintAllowed, Is.True);
            }
        }

        [Test]
        public void VegetationModifierPersistsUntilTheNextSurfaceFootstep()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetFootstepSurface(StalkerSurfaceType.Bush);
                rig.Condition.Tick(StalkerMoveFlags.None, false, 0.0f, 0.06f);
                Assert.That(rig.Condition.RunCoef, Is.EqualTo(rig.Profile.Actor.RunCoef * 0.85f).Within(0.000001f));

                rig.Condition.SetFootstepSurface(StalkerSurfaceType.Default);

                Assert.That(rig.Condition.RunCoef, Is.EqualTo(rig.Profile.Actor.RunCoef));
                Assert.That(rig.Condition.RunBackCoef, Is.EqualTo(rig.Profile.Actor.RunBackCoef));
            }
        }

        [TestCase(StalkerSurfaceType.Earth, 0.98f, 0.0f)]
        [TestCase(StalkerSurfaceType.Grass, 0.97f, -0.02f)]
        [TestCase(StalkerSurfaceType.Asphalt, 1.055f, 0.035f)]
        [TestCase(StalkerSurfaceType.Concrete, 1.04f, 0.0275f)]
        [TestCase(StalkerSurfaceType.Stucco, 1.04f, 0.0275f)]
        [TestCase(StalkerSurfaceType.Metal, 1.025f, 0.015f)]
        [TestCase(StalkerSurfaceType.MetalPipe, 1.025f, 0.015f)]
        [TestCase(StalkerSurfaceType.MetalPlate, 1.025f, 0.015f)]
        [TestCase(StalkerSurfaceType.Wood, 1.025f, 0.015f)]
        [TestCase(StalkerSurfaceType.WoodenBoard, 1.025f, 0.015f)]
        [TestCase(StalkerSurfaceType.FlooringTile, 1.025f, 0.015f)]
        [TestCase(StalkerSurfaceType.Water, 0.8f, -0.15f)]
        [TestCase(StalkerSurfaceType.TreeTrunk, 1.0f, 0.0f)]
        public void MaterialCoefficientsMatchTheActiveStaminaScript(StalkerSurfaceType surface, float speed, float stamina)
        {
            using (var material = new Rig())
            using (var neutral = new Rig())
            {
                material.Condition.SetFootstepSurface(surface);
                material.Condition.Tick(StalkerMoveFlags.Forward, false, 0.0f, 0.06f);
                neutral.Condition.Tick(StalkerMoveFlags.Forward, false, 0.0f, 0.06f);

                Assert.That(material.Condition.RunCoef, Is.EqualTo(material.Profile.Actor.RunCoef * speed).Within(0.000001f));
                Assert.That(material.Condition.Power - neutral.Condition.Power, Is.EqualTo(stamina * 0.005f * 0.8f).Within(0.000001f));
            }
        }

        [TestCase(StalkerSurfaceType.Bush)]
        [TestCase(StalkerSurfaceType.BushSux)]
        public void VegetationConsumesConfiguredPowerAtTheSixtyMillisecondTick(StalkerSurfaceType surface)
        {
            using (var vegetation = new Rig())
            using (var earth = new Rig())
            {
                vegetation.Condition.SetFootstepSurface(surface);
                earth.Condition.SetFootstepSurface(StalkerSurfaceType.Earth);
                vegetation.Condition.Tick(StalkerMoveFlags.Forward, false, 0.0f, 0.03f);
                earth.Condition.Tick(StalkerMoveFlags.Forward, false, 0.0f, 0.03f);
                Assert.That(vegetation.Condition.Power, Is.EqualTo(earth.Condition.Power));

                vegetation.Condition.Tick(StalkerMoveFlags.Forward, false, 0.0f, 0.03f);
                earth.Condition.Tick(StalkerMoveFlags.Forward, false, 0.0f, 0.03f);

                Assert.That(earth.Condition.Power - vegetation.Condition.Power, Is.EqualTo(0.00048f).Within(0.000001f));
            }
        }

        [Test]
        public void StationaryVegetationContactDoesNotAddPowerDrain()
        {
            using (var vegetation = new Rig())
            using (var earth = new Rig())
            {
                vegetation.Condition.SetFootstepSurface(StalkerSurfaceType.Bush);
                vegetation.Condition.Tick(StalkerMoveFlags.None, false, 0.0f, 0.06f);
                earth.Condition.Tick(StalkerMoveFlags.None, false, 0.0f, 0.06f);

                Assert.That(vegetation.Condition.Power, Is.EqualTo(earth.Condition.Power));
            }
        }

        [Test]
        public void DisabledMaterialDrainLeavesSpeedAndPowerUnchangedByVegetation()
        {
            using (var vegetation = new Rig(false))
            using (var earth = new Rig(false))
            {
                vegetation.Condition.SetFootstepSurface(StalkerSurfaceType.Bush);
                vegetation.Condition.Tick(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint, false, 0.0f, 0.06f);
                earth.Condition.Tick(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint, false, 0.0f, 0.06f);

                Assert.That(vegetation.Condition.RunCoef, Is.EqualTo(earth.Condition.RunCoef));
                Assert.That(vegetation.Condition.RunBackCoef, Is.EqualTo(earth.Condition.RunBackCoef));
                Assert.That(vegetation.Condition.Power, Is.EqualTo(earth.Condition.Power));
            }
        }
    }
}
