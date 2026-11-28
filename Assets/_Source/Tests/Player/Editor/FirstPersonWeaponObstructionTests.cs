using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.FirstPerson;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class FirstPersonWeaponObstructionTests
    {
        [TestCase(0.65f, true)]
        [TestCase(0.25f, true)]
        [TestCase(0.65f, false)]
        public void CalibratedBoundsDriveBothPosesAndPreserveTheGrips(float barrelLength, bool useMuzzleAxis)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            object obstruction = null;
            MethodInfo dispose = null;
            try
            {
                Vector3 origin = new Vector3(3000f, 0f, 3000f);
                Transform actor = Create(scene, "Actor", null, origin);
                Transform eye = Create(scene, "Eye", actor, Vector3.zero);
                Transform hands = Create(scene, "Hands", actor, Vector3.zero);
                hands.localScale = new Vector3(1.1f, 1.2f, 1f);
                Transform pivot = Create(scene, FirstPersonHudAnimator.ITEM_ANCHOR_BONE, hands, Vector3.zero);
                Transform weapon = Create(scene, "Weapon", pivot, Vector3.zero);
                Transform muzzle = Create(scene, "Muzzle", weapon, Vector3.forward * barrelLength);
                if (!useMuzzleAxis)
                {
                    muzzle.localRotation = Quaternion.Euler(0f, 90f, 0f);
                }

                float gripDepthScale = barrelLength < 0.33f ? 0.5f : 1f;
                Transform leftUpper = Create(scene, "l_upperarm", hands, new Vector3(-0.34f, -0.13f, 0.1f * gripDepthScale));
                Transform leftForearm = Create(scene, "l_forearm", leftUpper, new Vector3(0.08f, 0.01f, 0.12f * gripDepthScale));
                Transform rightUpper = Create(scene, "r_upperarm", hands, new Vector3(0.25f, -0.11f, 0.02f));
                Transform rightForearm = Create(scene, "r_forearm", rightUpper, new Vector3(-0.05f, 0.02f, 0.01f));
                Transform left = Create(scene, "l_hand", leftForearm, new Vector3(0.17f, 0.07f, 0.06f * gripDepthScale));
                Transform right = Create(scene, "r_hand", rightForearm, new Vector3(-0.12f, 0.05f, 0.01f));
                left.localRotation = Quaternion.Euler(5f, 12f, 0f);
                right.localRotation = Quaternion.Euler(16f, 0f, -7f);
                var bones = new[] { leftUpper, leftForearm, left, rightUpper, rightForearm, right, pivot };
                var handModel = hands.gameObject.AddComponent<XRayModel>();
                handModel.SetImportData(string.Empty, 0, string.Empty, 1f, bones, Array.ConvertAll(bones, bone => bone.name), Array.Empty<string>(), string.Empty, string.Empty, null, 0, 0, 0, Array.Empty<string>());
                AddHeldBox(scene, weapon, barrelLength, barrelLength * 0.5f, true, out Mesh heldMesh);
                Transform extension = AddHeldBox(scene, weapon, 0.20f, barrelLength + 0.1f, false, out _);
                Vector3 leftGrip = pivot.InverseTransformPoint(left.position);
                Vector3 rightGrip = pivot.InverseTransformPoint(right.position);
                Quaternion leftRotation = Quaternion.Inverse(pivot.rotation) * left.rotation;
                Quaternion rightRotation = Quaternion.Inverse(pivot.rotation) * right.rotation;

                AddObstacle(scene, actor, Vector3.forward * 0.1f);
                Transform wall = AddObstacle(scene, null, origin + Vector3.forward * (barrelLength + 0.5f));
                Type type = typeof(FirstPersonRig).Assembly.GetType("RaidIntoDarkness.FirstPerson.FirstPersonWeaponObstruction", true);
                FirstPersonContactCatalog calibration = BuildCalibration(handModel, heldMesh);
                obstruction = Activator.CreateInstance(type, actor, eye, -1, calibration, new Dictionary<Collider, float>());
                dispose = type.GetMethod("Dispose");
                MethodInfo bind = type.GetMethod("Bind");
                MethodInfo tick = type.GetMethod("Tick");
                PropertyInfo blocksAim = type.GetProperty("BlocksAim");
                var arguments = new object[] { hands.gameObject, scene.GetPhysicsScene(), 1f / 60f };
                bind.Invoke(obstruction, new object[] { weapon.gameObject, muzzle, useMuzzleAxis });

                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 60);
                Assert.That(hands.localPosition, Is.EqualTo(Vector3.zero));

                wall.position = origin + Vector3.forward * (barrelLength + 0.045f);
                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 60);
                Assert.That(hands.localPosition.z, Is.LessThan(-0.01f));
                Assert.That(Quaternion.Angle(hands.localRotation, Quaternion.identity), Is.LessThan(5f));
                Assert.That(blocksAim.GetValue(obstruction), Is.False);

                wall.position = origin + Vector3.forward * (barrelLength + 0.031f);
                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 60);
                Assert.That(hands.localPosition.z, Is.LessThan(-0.01f));
                Assert.That(blocksAim.GetValue(obstruction), Is.False, "Light overlap must allow ADS.");

                wall.position = origin + Vector3.forward * (barrelLength + 0.022f);
                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 3);
                Assert.That(blocksAim.GetValue(obstruction), Is.True, "A contact just above the 2.5 cm threshold must block ADS.");

                wall.position = origin + Vector3.forward * (barrelLength + 0.028f);
                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 3);
                Assert.That(blocksAim.GetValue(obstruction), Is.True, "The release threshold must keep ADS blocked between the thresholds.");

                wall.position = origin + Vector3.forward * (barrelLength * 0.29f);
                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 3);
                Assert.That(blocksAim.GetValue(obstruction), Is.True);
                Assert.That(Quaternion.Angle(hands.localRotation, Quaternion.identity), Is.LessThan(5f));
                RunFrames(tick, obstruction, arguments, 50);
                float pitch = Mathf.DeltaAngle(0f, hands.localEulerAngles.x);
                Assert.That(barrelLength < 0.33f ? -pitch : pitch, Is.GreaterThan(12f));
                Assert.That(Vector3.Distance(pivot.InverseTransformPoint(left.position), leftGrip), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(pivot.InverseTransformPoint(right.position), rightGrip), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(Quaternion.Inverse(pivot.rotation) * left.rotation, leftRotation), Is.LessThan(0.1f));
                Assert.That(Quaternion.Angle(Quaternion.Inverse(pivot.rotation) * right.rotation, rightRotation), Is.LessThan(0.1f));

                wall.position += Vector3.right;
                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 1);
                Assert.That(Mathf.DeltaAngle(pitch, hands.localEulerAngles.x), Is.EqualTo(0f).Within(0.01f));
                RunFrames(tick, obstruction, arguments, 70);
                Assert.That(hands.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(hands.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(blocksAim.GetValue(obstruction), Is.False);

                wall.position = origin + Vector3.forward * (barrelLength + 0.18f);
                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 60);
                Assert.That(hands.localPosition, Is.EqualTo(Vector3.zero));

                Transform extendedMuzzle = Create(scene, "Extended Muzzle", weapon, Vector3.forward * (barrelLength + 0.2f));
                extension.gameObject.SetActive(true);
                bind.Invoke(obstruction, new object[] { weapon.gameObject, extendedMuzzle, true });
                RunFrames(tick, obstruction, arguments, 60);
                Assert.That(hands.localPosition.z, Is.LessThan(-0.01f));

                wall.position = origin + Vector3.right * 2.0f;
                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 70);
                Assert.That(hands.localPosition, Is.EqualTo(Vector3.zero));

                Vector3[] ignoredContacts =
                {
                    leftUpper.position + Vector3.left * 0.065f,
                    leftForearm.position + Vector3.left * 0.065f,
                    rightUpper.position + Vector3.right * 0.065f,
                    rightForearm.position + Vector3.right * 0.065f,
                    right.position + Vector3.right * 0.085f
                };
                foreach (Vector3 ignoredContact in ignoredContacts)
                {
                    wall.position = ignoredContact;
                    Physics.SyncTransforms();
                    RunFrames(tick, obstruction, arguments, 20);
                    Assert.That(hands.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(hands.localRotation, Is.EqualTo(Quaternion.identity));
                }

                wall.position = left.position + Vector3.left * 0.065f;
                Physics.SyncTransforms();
                RunFrames(tick, obstruction, arguments, 60);
                Assert.That(hands.localPosition.z, Is.LessThan(-0.01f), "Off-axis left-hand contact should move the shared rig.");
                Assert.That(Vector3.Distance(pivot.InverseTransformPoint(left.position), leftGrip), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(pivot.InverseTransformPoint(right.position), rightGrip), Is.LessThan(0.001f));
                bind.Invoke(obstruction, new object[] { null, null, false });
                Assert.That(hands.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(hands.localRotation, Is.EqualTo(Quaternion.identity));
            }
            finally
            {
                dispose?.Invoke(obstruction, null);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static FirstPersonContactCatalog BuildCalibration(XRayModel hands, Mesh mesh)
        {
            using var data = new MemoryStream();
            using var writer = new BinaryWriter(data);
            writer.Write(FirstPersonContactCatalog.VERSION);
            writer.Write(2);
            writer.Write(FirstPersonContactCatalog.HandsKey(hands));
            writer.Write(6);
            for (int i = 0; i < 6; i++)
            {
                WriteBox(writer, i, new Bounds(Vector3.zero, Vector3.one * 0.11f));
            }

            writer.Write(FirstPersonContactCatalog.MeshKey(null, mesh));
            writer.Write(4);
            for (int i = 0; i < 4; i++)
            {
                WriteBox(writer, -1, new Bounds(new Vector3(0f, 0f, -0.375f + i * 0.25f), new Vector3(1f, 1f, 0.25f)));
            }

            writer.Flush();
            return (FirstPersonContactCatalog)Activator.CreateInstance(typeof(FirstPersonContactCatalog), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { data.ToArray() }, null);
        }

        private static void WriteBox(BinaryWriter writer, int bone, Bounds bounds)
        {
            writer.Write(bone);
            for (int axis = 0; axis < 3; axis++)
            {
                writer.Write(bounds.center[axis]);
            }

            for (int axis = 0; axis < 3; axis++)
            {
                writer.Write(bounds.size[axis]);
            }
        }

        private static Transform AddHeldBox(Scene scene, Transform pivot, float length, float depth, bool active, out Mesh mesh)
        {
            GameObject held = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(held, scene);
            Transform shape = held.transform;
            shape.SetParent(pivot, false);
            shape.localPosition = Vector3.forward * depth;
            shape.localScale = new Vector3(0.13f, 0.13f, length);
            mesh = held.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(held.GetComponent<Collider>());
            held.SetActive(active);
            return shape;
        }

        private static void RunFrames(MethodInfo tick, object obstruction, object[] arguments, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                tick.Invoke(obstruction, arguments);
            }
        }

        private static Transform AddObstacle(Scene scene, Transform parent, Vector3 position)
        {
            Transform obstacle = Create(scene, "Obstacle", parent, position);
            var box = obstacle.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.12f, 0.12f, 0.04f);
            return obstacle;
        }

        private static Transform Create(Scene scene, string name, Transform parent, Vector3 position)
        {
            var gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = position;
            return gameObject.transform;
        }
    }
}
