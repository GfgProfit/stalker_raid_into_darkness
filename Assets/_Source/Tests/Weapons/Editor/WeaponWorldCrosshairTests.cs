using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Weapons.Tests
{
    public sealed class WeaponWorldCrosshairTests
    {
        [TestCase(FirstPersonWeaponState.Ready, false, 0f, StalkerMoveFlags.None, false)]
        [TestCase(FirstPersonWeaponState.Ready, false, 0f, StalkerMoveFlags.Forward | StalkerMoveFlags.Accel, false)]
        [TestCase(FirstPersonWeaponState.Ready, false, 0f, StalkerMoveFlags.Forward | StalkerMoveFlags.Crouch, false)]
        [TestCase(FirstPersonWeaponState.Drawing, false, 0f, StalkerMoveFlags.None, false)]
        [TestCase(FirstPersonWeaponState.Holstering, false, 0f, StalkerMoveFlags.None, false)]
        [TestCase(FirstPersonWeaponState.Reloading, false, 0f, StalkerMoveFlags.None, true)]
        [TestCase(FirstPersonWeaponState.Ready, true, 0f, StalkerMoveFlags.None, true)]
        [TestCase(FirstPersonWeaponState.Ready, false, 0.5f, StalkerMoveFlags.None, true)]
        [TestCase(FirstPersonWeaponState.Ready, false, 0f, StalkerMoveFlags.Forward, false)]
        [TestCase(FirstPersonWeaponState.Ready, false, 0f, StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint, true)]
        public void VisibilityPolicyOnlyHidesForReloadSprintAndAim(FirstPersonWeaponState state, bool aiming, float aimFactor, StalkerMoveFlags flags, bool expected)
        {
            var movement = new StalkerMovementState(flags, StalkerEnvironment.OnGround, Vector3.zero, Vector3.zero, 0, false, 0f, 0f);
            MethodInfo method = typeof(WeaponWorldCrosshair).GetMethod("ShouldHide", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { state, aiming, aimFactor, movement }), Is.EqualTo(expected));
        }

        [TestCase(StalkerMoveFlags.Jump, StalkerMovementMode.Airborne)]
        [TestCase(StalkerMoveFlags.Fall, StalkerMovementMode.Airborne)]
        [TestCase(StalkerMoveFlags.Landing, StalkerMovementMode.Landing)]
        public void SprintRemainsHiddenWhenAirborneOrLandingOverridesTheMovementMode(StalkerMoveFlags phase, StalkerMovementMode expectedMode)
        {
            MethodInfo method = typeof(WeaponWorldCrosshair).GetMethod("ShouldHide", BindingFlags.Static | BindingFlags.NonPublic);
            StalkerMoveFlags flags = StalkerMoveFlags.Forward | phase;
            var sprint = new StalkerMovementState(flags | StalkerMoveFlags.Sprint, StalkerEnvironment.OnGround, Vector3.zero, Vector3.zero, 0, false, 0f, 0f);
            var ordinary = new StalkerMovementState(flags, StalkerEnvironment.OnGround, Vector3.zero, Vector3.zero, 0, false, 0f, 0f);
            Assert.That(sprint.Mode, Is.EqualTo(expectedMode));
            Assert.That(method.Invoke(null, new object[] { FirstPersonWeaponState.Ready, false, 0f, sprint }), Is.True);
            Assert.That(ordinary.Mode, Is.EqualTo(expectedMode));
            Assert.That(method.Invoke(null, new object[] { FirstPersonWeaponState.Ready, false, 0f, ordinary }), Is.False);
        }

        [TestCase(2f, 0.05f, 1.95f)]
        [TestCase(2f, 0f, 2f)]
        [TestCase(0.02f, 0.05f, 0f)]
        public void BarrelOffsetPullsTheMarkerBackAlongTheBoreWithoutPassingTheMuzzle(float distance, float offset, float expectedDistance)
        {
            Ray ray = new Ray(new Vector3(1f, 2f, 3f), new Vector3(0.2f, 0.3f, 1f).normalized);
            MethodInfo method = typeof(WeaponWorldCrosshair).GetMethod("GetOffsetPosition", BindingFlags.Static | BindingFlags.NonPublic);
            Vector3 position = (Vector3)method.Invoke(null, new object[] { ray, distance, offset });
            Assert.That(Vector3.Distance(position, ray.GetPoint(expectedDistance)), Is.LessThan(0.00001f));
        }

        [Test]
        public void MuzzleRayMatchesTheScaledHudBoreInsteadOfCameraForward()
        {
            var root = new GameObject("CrosshairRayTest");
            root.SetActive(false);
            try
            {
                FirstPersonWeaponController controller = root.AddComponent<FirstPersonWeaponController>();
                root.transform.localScale = new Vector3(2f, 3f, 1f);
                var muzzle = new GameObject("MuzzlePoint");
                muzzle.transform.SetParent(root.transform, false);
                muzzle.transform.localPosition = new Vector3(0.3f, -0.2f, 0.5f);
                muzzle.transform.localRotation = Quaternion.Euler(15f, 25f, 0f);
                typeof(FirstPersonWeaponController).GetField("_muzzle", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, muzzle.transform);
                typeof(FirstPersonWeaponController).GetField("_muzzleFromView", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, true);
                Assert.That(controller.TryGetMuzzleRay(out Ray ray), Is.True);
                Assert.That(ray.origin, Is.EqualTo(muzzle.transform.position));
                Vector3 expected = muzzle.transform.TransformVector(Vector3.forward).normalized;
                Assert.That(Vector3.Angle(ray.direction, expected), Is.LessThan(0.001f));
                Assert.That(Vector3.Angle(ray.direction, Vector3.forward), Is.GreaterThan(1f));
                Assert.That(Vector3.Angle(ray.direction, muzzle.transform.forward), Is.GreaterThan(1f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
