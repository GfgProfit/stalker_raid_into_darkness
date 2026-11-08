#if UNITY_EDITOR
using NUnit.Framework;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class BoltAssetTests
    {
        private const string ROOT = "Assets/_Source/FirstPerson/Bolt";
        private const string DEFINITION_PATH = ROOT + "/BoltDefinition.asset";
        private const string PLAYER_PATH = "Assets/_Source/FirstPerson/FPS_Player.prefab";
        private const string INPUT_PATH = "Assets/_Source/Settings/Input/StalkerControls.inputactions";
        private const string MATERIAL = "objects\\small_metal_trash";

        [Test]
        public void PlayerReferencesBoltAndKeyboardSixAction()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PATH);
            Assert.That(player, Is.Not.Null);
            FirstPersonBoltController bolt = player.GetComponent<FirstPersonBoltController>();
            Assert.That(bolt, Is.Not.Null);
            var serialized = new SerializedObject(bolt);
            Assert.That(serialized.FindProperty("_definition").objectReferenceValue, Is.SameAs(Definition()));
            Assert.That(serialized.FindProperty("_player").objectReferenceValue, Is.SameAs(player.GetComponent<StalkerPlayerController>()));
            Assert.That(serialized.FindProperty("_weapons").objectReferenceValue, Is.SameAs(player.GetComponent<FirstPersonWeaponController>()));
            Assert.That(serialized.FindProperty("_input").objectReferenceValue, Is.SameAs(player.GetComponent<StalkerPlayerInput>()));
            AudioSource audio = serialized.FindProperty("_audio").objectReferenceValue as AudioSource;
            Assert.That(audio, Is.Not.Null);
            Assert.That(audio.spatialBlend, Is.Zero);
            Assert.That(audio.playOnAwake, Is.False);
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(INPUT_PATH);
            Assert.That(actions, Is.Not.Null);
            InputAction action = actions.FindAction("Player/Bolt", true);
            bool found = false;
            foreach (InputBinding binding in action.bindings)
            {
                found |= binding.path == "<Keyboard>/6";
            }
            Assert.That(found, Is.True);
            var input = new SerializedObject(player.GetComponent<StalkerPlayerInput>());
            Assert.That(input.FindProperty("_actions").objectReferenceValue, Is.SameAs(actions));
        }

        [Test]
        public void ImportedMotionMetadataPreservesGraSpeedsAndReleaseMark()
        {
            BoltDefinition definition = Definition();
            foreach (AnimationClip clip in new[] { definition.Draw, definition.DrawAlternate, definition.Holster, definition.Idle, definition.Moving, definition.Sprint, definition.Bore, definition.ThrowBegin, definition.ThrowIdle, definition.ThrowEnd, definition.ItemIdle, definition.ItemFallback, definition.ItemDraw, definition.ItemDrawAlternate, definition.ItemBore })
            {
                Assert.That(clip, Is.Not.Null);
            }
            Assert.That(definition.GetMotionSpeed(definition.Idle), Is.EqualTo(0.25f).Within(0.000001f));
            Assert.That(definition.GetMotionSpeed(definition.ThrowIdle), Is.EqualTo(0.25f).Within(0.000001f));
            Assert.That(definition.GetMotionSpeed(definition.Moving), Is.EqualTo(0.9f).Within(0.000001f));
            Assert.That(definition.GetMotionSpeed(definition.Sprint), Is.EqualTo(1.2f).Within(0.000001f));
            Assert.That(definition.GetMotionSpeed(definition.ThrowEnd), Is.EqualTo(1f));
            Assert.That(definition.ThrowReleaseTime, Is.EqualTo(0.2446666956f).Within(0.000001f));
            Assert.That(definition.HudFieldOfView, Is.EqualTo(0.78f));
            Assert.That(definition.TransitionDuration, Is.EqualTo(0.2f).Within(0.000001f));
            Assert.That(Vector3.Distance(definition.HandsPosition, new Vector3(0.033f, -0.155f, -0.15f)), Is.LessThan(0.000001f));
            Assert.That(definition.DrawSound, Is.Not.Null);
            Assert.That(definition.HolsterSound, Is.Not.Null);
        }

        [Test]
        public void ProjectileUsesSourcePhysicsAndHasNoInventoryPickup()
        {
            GameObject prefab = Definition().ProjectilePrefab;
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<BoltProjectile>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<AnomalyProbe>(), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<WorldItem>(true), Is.Empty);
            Rigidbody body = prefab.GetComponent<Rigidbody>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.mass, Is.EqualTo(1f));
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.useGravity, Is.False);
            Assert.That(body.linearDamping, Is.EqualTo(0.0001f).Within(0.000001f));
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
            BoxCollider collider = prefab.GetComponent<BoxCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(Vector3.Distance(collider.center, new Vector3(0.0000715283677f, -0.0049078334123f, -0.0000623427331f)), Is.LessThan(0.000001f));
            Assert.That(Vector3.Distance(collider.size, new Vector3(0.03337807208f, 0.07814133168f, 0.03294686228f)), Is.LessThan(0.000001f));
            WorldItemImpactSound impact = prefab.GetComponent<WorldItemImpactSound>();
            Assert.That(impact, Is.Not.Null);
            Assert.That(impact.Material, Is.EqualTo(MATERIAL));
            Assert.That(impact.Mass, Is.EqualTo(1f));
        }

        [Test]
        public void ImpactBanksKeepSourceSurfaceSilenceAndSharedAudio()
        {
            var sounds = AssetDatabase.LoadAssetAtPath<StalkerCollideSoundLibrary>(ROOT + "/BoltCollideSounds.asset");
            Assert.That(sounds, Is.Not.Null);
            Assert.That(sounds.Entries.Count, Is.EqualTo(StalkerSurfaceTypes.Count));
            Assert.That(sounds.Find(MATERIAL, StalkerSurfaceType.Metal).Count, Is.EqualTo(4));
            Assert.That(sounds.Find(MATERIAL, StalkerSurfaceType.Concrete).Count, Is.EqualTo(4));
            Assert.That(sounds.Find(MATERIAL, StalkerSurfaceType.Bush).Count, Is.EqualTo(2));
            Assert.That(sounds.Find(MATERIAL, StalkerSurfaceType.Water).Count, Is.EqualTo(1));
            Assert.That(sounds.Find(MATERIAL, StalkerSurfaceType.Earth), Is.Empty);
            Assert.That(sounds.Find(MATERIAL, StalkerSurfaceType.Grass), Is.Empty);
            foreach (StalkerCollideSounds entry in sounds.Entries)
            {
                Assert.That(entry.ObjectMaterial, Is.EqualTo(MATERIAL));
                foreach (AudioClip clip in entry.Clips)
                {
                    Assert.That(clip, Is.Not.Null);
                }
            }
            var impact = new SerializedObject(Definition().ProjectilePrefab.GetComponent<WorldItemImpactSound>());
            Assert.That(impact.FindProperty("_sounds").objectReferenceValue, Is.SameAs(sounds));
            Assert.That(impact.FindProperty("_surfaces").objectReferenceValue, Is.Not.Null);
            Assert.That(impact.FindProperty("_audio").objectReferenceValue, Is.Not.Null);
        }

        [Test]
        public void HudAndProjectileShareResolvedMonolithMaterial()
        {
            BoltDefinition definition = Definition();
            Material expected = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Models/Materials/wpn_bolt_monolith.mat");
            Assert.That(expected, Is.Not.Null);
            Assert.That(expected.shader, Is.Not.Null);
            Assert.That(expected.shader.name, Does.Not.Contain("InternalErrorShader"));
            Assert.That(expected.GetTexture("_BaseColorMap"), Is.Not.Null);
            Assert.That(expected.GetTexture("_MonolithBump"), Is.Not.Null);
            Assert.That(expected.GetTexture("_MonolithBumpCorrection"), Is.Not.Null);
            foreach (GameObject model in new[] { definition.HudModel, definition.ProjectilePrefab })
            {
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThan(0));
                foreach (Renderer renderer in renderers)
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Assert.That(material, Is.SameAs(expected));
                    }
                }
            }
        }

        [Test]
        public void AnomaliesUseSourceSmallObjectPolicies()
        {
            foreach (string name in new[] { "Electra", "Zharka", "Tramplin", "Vortex" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Source/Anomalies/" + name + "/" + name + ".prefab");
                Assert.That(prefab, Is.Not.Null);
                AnomalyPlayerReaction reaction = prefab.GetComponentInChildren<AnomalyPlayerReaction>(true);
                Assert.That(reaction, Is.Not.Null);
                var serialized = new SerializedObject(reaction);
                Assert.That(serialized.FindProperty("_ignoreSmallObjects").boolValue, Is.EqualTo(name == "Vortex"));
                Assert.That(serialized.FindProperty("_smallObjectIgnoreDelay").floatValue, Is.EqualTo(name == "Zharka" ? -1f : 0.5f));
            }
        }

        private static BoltDefinition Definition()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BoltDefinition>(DEFINITION_PATH);
            Assert.That(definition, Is.Not.Null);
            return definition;
        }
    }
}
#endif
