using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.Weapons.Tests
{
    public sealed class WeaponSwayLeanTests
    {
        private const float FRAME_TIME = 1.0f / 60.0f;
        private const float HUD_SCALE = 1.6f;

        [TestCase(-1.0f, -0.07668651f, -0.04234925f, 13.485915f)]
        [TestCase(1.0f, -0.02837801f, -0.02467653f, 7.251639f)]
        public void LeanBlendsTheAsymmetricWeaponPoseAndFadesDuringAim(float intent, float x, float y, float roll)
        {
            var camera = new GameObject("Camera");
            camera.SetActive(false);
            try
            {
                camera.transform.SetPositionAndRotation(new Vector3(2.0f, 1.6f, 3.0f), Quaternion.AngleAxis(12.0f * -intent, Vector3.forward));
                Vector3 cameraPosition = camera.transform.position;
                Quaternion cameraRotation = camera.transform.rotation;
                var sway = camera.AddComponent<WeaponSway>();
                Transform hud = new GameObject("HandsRoot").transform;
                hud.SetParent(camera.transform, false);
                var restPosition = new Vector3(0.01f, -0.02f, 0.03f);
                hud.localScale = new Vector3(HUD_SCALE, HUD_SCALE, 1.0f);
                typeof(WeaponSway).GetField("_swayRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sway, hud);
                typeof(WeaponSway).GetField("_basePosition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sway, restPosition);
                typeof(WeaponSway).GetField("_baseRotation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sway, Quaternion.identity);
                MethodInfo update = typeof(WeaponSway).GetMethod("UpdateLean", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo apply = typeof(WeaponSway).GetMethod("ApplyHudPose", BindingFlags.Instance | BindingFlags.NonPublic);

                var targetOffset = new Vector3(x, y, 0.0f) * HUD_SCALE;
                var pivot = new object[] { Vector3.zero };
                var holding = new object[] { intent, false, FRAME_TIME };
                update.Invoke(sway, holding);
                apply.Invoke(sway, pivot);
                Assert.That(Vector3.Distance(hud.localPosition, restPosition + targetOffset / 12.0f), Is.LessThan(0.00001f));

                for (int i = 0; i < 120; i++)
                {
                    update.Invoke(sway, holding);
                }

                apply.Invoke(sway, pivot);
                Assert.That(Vector3.Distance(hud.localPosition, restPosition + targetOffset), Is.LessThan(0.00001f));
                Assert.That(hud.localEulerAngles.z, Is.EqualTo(roll).Within(0.01f));
                Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
                Assert.That(camera.transform.rotation, Is.EqualTo(cameraRotation));

                var aiming = new object[] { intent, true, FRAME_TIME };
                update.Invoke(sway, aiming);
                apply.Invoke(sway, pivot);
                Assert.That(Vector3.Distance(hud.localPosition, restPosition + targetOffset * 0.85f), Is.LessThan(0.00001f));

                for (int i = 0; i < 90; i++)
                {
                    update.Invoke(sway, aiming);
                }

                apply.Invoke(sway, pivot);
                Assert.That(hud.localPosition, Is.EqualTo(restPosition));
                Assert.That(hud.localRotation, Is.EqualTo(Quaternion.identity));
            }
            finally
            {
                Object.DestroyImmediate(camera);
            }
        }
    }
}
