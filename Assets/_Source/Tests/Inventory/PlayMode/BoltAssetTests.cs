#if UNITY_EDITOR
using NUnit.Framework;
using RaidIntoDarkness.FirstPerson;
using RaidIntoDarkness.Player;
using RaidIntoDarkness.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using XRay.Importer;

namespace RaidIntoDarkness.Inventory.Tests
{
    public sealed class BoltAssetTests
    {
        private const string ROOT = "Assets/_Source/FirstPerson/Bolt";
        private const string DEFINITION_PATH = ROOT + "/BoltDefinition.asset";
        private const string GRA_HANDS = ROOT + "/Animations/dev_hand_bolt_hud_animation.omf";
        private const string GRA_ITEM = ROOT + "/Animations/dev_bolt_hud_animation.omf";
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
            Assert.That(Quaternion.Angle(definition.HandsRotation, Quaternion.identity), Is.LessThan(0.0001f));
            Assert.That(definition.HasDistinctMovement, Is.False);
            Assert.That(definition.DrawCameraEffect, Is.Null);
            Assert.That(definition.Draw.name, Is.EqualTo("dev_bolt_draw"));
            Assert.That(definition.DrawAlternate.name, Is.EqualTo("dev_bolt_draw1"));
            Assert.That(AssetDatabase.GetAssetPath(definition.HudModel), Is.EqualTo(ROOT + "/Models/dev_bolt_hud.ogf"));
            var data = new SerializedObject(definition);
            Assert.That(AssetDatabase.GetAssetPath(data.FindProperty("_handsMotions").objectReferenceValue), Is.EqualTo(GRA_HANDS));
            Assert.That(AssetDatabase.GetAssetPath(data.FindProperty("_itemMotions").objectReferenceValue), Is.EqualTo(GRA_ITEM));
            Assert.That(definition.HudFieldOfView, Is.EqualTo(0.78f));
            Assert.That(definition.TransitionDuration, Is.EqualTo(0.2f).Within(0.000001f));
            Assert.That(Vector3.Distance(definition.HandsPosition, new Vector3(0.033f, -0.155f, -0.15f)), Is.LessThan(0.000001f));
            Assert.That(definition.DrawSound, Is.Not.Null);
            Assert.That(definition.HolsterSound, Is.Not.Null);
        }

        [Test]
        public void GraHudPoseIsAppliedBelowUnshiftedHandsRoot()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PLAYER_PATH);
            Assert.That(player, Is.Not.Null);
            var rig = new SerializedObject(player.GetComponent<FirstPersonRig>());
            Transform handsRoot = rig.FindProperty("_handsRoot").objectReferenceValue as Transform;
            Assert.That(handsRoot, Is.Not.Null);
            Assert.That(handsRoot.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(Quaternion.Angle(handsRoot.localRotation, Quaternion.identity), Is.LessThan(0.0001f));
            Assert.That(handsRoot.localScale, Is.EqualTo(Vector3.one));
            GameObject hud = Definition().HudModel;
            Assert.That(hud.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(Quaternion.Angle(hud.transform.localRotation, Quaternion.identity), Is.LessThan(0.0001f));
        }

        [Test]
        public void ProjectileUsesSourcePhysicsAndHasNoInventoryPickup()
        {
            GameObject prefab = Definition().ProjectilePrefab;
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<BoltProjectile>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<AnomalyProbe>(), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<WorldItem>(true), Is.Empty);
            bool foundWorldMesh = false;
            foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Assert.That(renderer.sharedMesh, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMesh), Is.EqualTo(ROOT + "/Models/dev_bolt.ogf"));
                foundWorldMesh = true;
            }
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Assert.That(filter.sharedMesh, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(filter.sharedMesh), Is.EqualTo(ROOT + "/Models/dev_bolt.ogf"));
                foundWorldMesh = true;
            }
            Assert.That(foundWorldMesh, Is.True);
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
            Assert.That(collider.size.x, Is.GreaterThan(0f));
            Assert.That(collider.size.y, Is.GreaterThan(0f));
            Assert.That(collider.size.z, Is.GreaterThan(0f));
            Assert.That(collider.size.magnitude, Is.LessThan(0.6f));
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

