using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerCameraLeanTests
    {
        private const float FRAME_TIME = 1.0f / 60.0f;

        [TestCase(-1.0f)]
        [TestCase(1.0f)]
        public void LeanKeepsAimDirectionAndStopsAtObstacles(float side)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject player = CreateObject(scene, "Player");
                player.transform.position = new Vector3(5000.0f, 0.0f, 5000.0f);
                var ownCollider = player.AddComponent<BoxCollider>();
                ownCollider.center = new Vector3(0.0f, 1.5f, 0.0f);
                ownCollider.size = new Vector3(0.5f, 0.5f, 0.5f);
                Transform pivot = CreateObject(scene, "Camera Pivot").transform;
                pivot.SetParent(player.transform, false);
                var camera = player.AddComponent<StalkerCameraController>();
                var serialized = new SerializedObject(camera);
                serialized.FindProperty("_cameraRoot").objectReferenceValue = pivot;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                StalkerMovementProfile profile = AssetDatabase.LoadAssetAtPath<StalkerMovementProfile>("Assets/_Source/Settings/Player/StalkerMovementProfile.asset");
                Assert.That(profile, Is.Not.Null);
                camera.Initialize(profile);
                camera.SetLook(25.0f, -18.0f);
                camera.Apply(1.5f);
                Vector3 neutralDirection = camera.Direction;
                MethodInfo applyLean = typeof(StalkerCameraController).GetMethod("ApplyLean", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(applyLean, Is.Not.Null);
                Physics.SyncTransforms();

                AdvanceLean(applyLean, camera, side, 1);
                float firstStep = pivot.localPosition.x * side;
                AdvanceLean(applyLean, camera, side, 3);
                float beforeFastStep = pivot.localPosition.x * side;
                AdvanceLean(applyLean, camera, side, 1);
                float fastStep = pivot.localPosition.x * side - beforeFastStep;
                AdvanceLean(applyLean, camera, side, 12);
                float beforeSlowStep = pivot.localPosition.x * side;
                AdvanceLean(applyLean, camera, side, 1);
                float slowStep = pivot.localPosition.x * side - beforeSlowStep;
                Assert.That(firstStep, Is.GreaterThan(0.0f).And.LessThan(fastStep));
                Assert.That(slowStep, Is.GreaterThan(0.0f).And.LessThan(fastStep * 0.5f));

                AdvanceLean(applyLean, camera, side, 30);
                float openOffset = pivot.localPosition.x * side;
                Assert.That(openOffset, Is.GreaterThan(0.25f));
                Assert.That(Vector3.Dot(pivot.up, player.transform.right) * side, Is.GreaterThan(0.15f));
                Assert.That(Vector3.Angle(neutralDirection, camera.Direction), Is.LessThan(0.01f));

                GameObject wall = CreateObject(scene, "Wall");
                wall.transform.SetPositionAndRotation(player.transform.TransformPoint(new Vector3(side * 0.2f, 1.5f, 0.0f)), player.transform.rotation);
                var wallCollider = wall.AddComponent<BoxCollider>();
                wallCollider.size = new Vector3(0.05f, 1.0f, 1.0f);
                Physics.SyncTransforms();
                AdvanceLean(applyLean, camera, side, 1);
                float blockedOffset = pivot.localPosition.x * side;
                Assert.That(blockedOffset, Is.InRange(0.01f, 0.11f));
                camera.Apply(1.5f);
                AdvanceLean(applyLean, camera, side, 6);
                Assert.That(pivot.localPosition.x * side, Is.GreaterThan(0.0f).And.LessThan(blockedOffset * 0.9f));
                AdvanceLean(applyLean, camera, side, 36);
                Assert.That(pivot.localPosition.x * side, Is.EqualTo(blockedOffset).Within(0.001f));

                wallCollider.enabled = false;
                Physics.SyncTransforms();
                AdvanceLean(applyLean, camera, side, 6);
                Assert.That(pivot.localPosition.x * side, Is.GreaterThan(blockedOffset).And.LessThan(openOffset));
                AdvanceLean(applyLean, camera, side, 36);
                AdvanceLean(applyLean, camera, 0.0f, 6);
                Assert.That(pivot.localPosition.x * side, Is.GreaterThan(0.0f).And.LessThan(openOffset));
                AdvanceLean(applyLean, camera, 0.0f, 42);
                Assert.That(pivot.localPosition.x, Is.Zero);
                Assert.That(Vector3.Angle(neutralDirection, camera.Direction), Is.LessThan(0.01f));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void AdvanceLean(MethodInfo applyLean, StalkerCameraController camera, float side, int frames)
        {
            var arguments = new object[] { 1.5f, side, FRAME_TIME };
            for (int i = 0; i < frames; i++)
            {
                applyLean.Invoke(camera, arguments);
            }
        }

        private static GameObject CreateObject(Scene scene, string name)
        {
            var result = new GameObject(name);
            SceneManager.MoveGameObjectToScene(result, scene);
            return result;
        }
    }
}
