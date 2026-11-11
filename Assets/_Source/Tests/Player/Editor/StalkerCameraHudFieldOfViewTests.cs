using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerCameraHudFieldOfViewTests
    {
        [Test]
        public void WeaponHudFieldOfViewBlendsWithoutChangingDepthMainFieldOfViewOrBaseSettings()
        {
            var root = new GameObject("HudFieldOfViewTest");
            try
            {
                StalkerCameraController controller = root.AddComponent<StalkerCameraController>();
                Camera camera = new GameObject("Camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                Transform hud = new GameObject("Hud").transform;
                hud.SetParent(root.transform, false);
                hud.localPosition = new Vector3(0.1f, 0.2f, 0.4f);
                var settings = new SerializedObject(controller);
                settings.FindProperty("_camera").objectReferenceValue = camera;
                settings.FindProperty("_hudRoot").objectReferenceValue = hud;
                settings.ApplyModifiedPropertiesWithoutUndo();
                StalkerMovementProfile profile = AssetDatabase.LoadAssetAtPath<StalkerMovementProfile>("Assets/_Source/Settings/Player/StalkerMovementProfile.asset");
                Assert.That(profile, Is.Not.Null);
                controller.Initialize(profile);
                Vector3 initialPosition = hud.localPosition;
                Vector3 initialScale = hud.localScale;
                float initialFieldOfView = camera.fieldOfView;
                float initialHud = controller.HudFieldOfView;
                float target = initialHud * 0.75f;
                controller.SetWeaponHudFieldOfView(target, 1f);
                float aimedScale = hud.localScale.x;
                Assert.That(aimedScale, Is.GreaterThan(initialScale.x));
                Assert.That(hud.localScale.z, Is.EqualTo(1f));
                Assert.That(hud.localPosition, Is.EqualTo(initialPosition));
                Assert.That(camera.fieldOfView, Is.EqualTo(initialFieldOfView));
                Assert.That(controller.HudFieldOfView, Is.EqualTo(initialHud));
                controller.SetWeaponHudFieldOfView(target, 0.5f);
                Assert.That(hud.localScale.x, Is.GreaterThan(initialScale.x).And.LessThan(aimedScale));
                controller.SetWeaponHudFieldOfView(0f, 0f);
                Assert.That(hud.localScale.x, Is.EqualTo(initialScale.x).Within(0.000001f));
                Assert.That(hud.localPosition, Is.EqualTo(initialPosition));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
