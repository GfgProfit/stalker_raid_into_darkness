using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Weapons.Optics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Weapons.Tests
{
    public sealed class ScopeOpticsMathTests
    {
        private ScopeOpticsConfig _configuration;

        [SetUp]
        public void SetUp()
        {
            _configuration = ScriptableObject.CreateInstance<ScopeOpticsConfig>();
            _configuration.hideFlags = HideFlags.HideAndDontSave;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_configuration);
        }

        [Test]
        public void MagnificationScalesAngularImageThroughTangentHalfAngle()
        {
            float onePower = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 1f, 0.014f, 0.08f);
            float sixPower = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 6f, 0.014f, 0.08f);
            float angularScale = Mathf.Tan(onePower * Mathf.Deg2Rad * 0.5f) / Mathf.Tan(sixPower * Mathf.Deg2Rad * 0.5f);
            Assert.That(angularScale, Is.EqualTo(6f).Within(0.0001f));
            Assert.That(sixPower, Is.Not.EqualTo(onePower / 6f).Within(0.00001f));
        }

        [Test]
        public void CalibratedOnePowerUsesSixtyDegreeFieldAtFractionalMagnification()
        {
            SetField(_configuration, "_projectionMode", ScopeProjectionMode.CalibratedFieldOfView);
            SetField(_configuration, "_calibrationMagnification", 1f);
            SetField(_configuration, "_calibrationVerticalFov", 60f);
            float one = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 1f, 0.014f, 0.08f);
            float fractional = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 2.35f, 0.014f, 0.08f);
            Assert.That(one, Is.EqualTo(60f).Within(0.0001f));
            Assert.That(Mathf.Tan(one * Mathf.Deg2Rad * 0.5f) / Mathf.Tan(fractional * Mathf.Deg2Rad * 0.5f), Is.EqualTo(2.35f).Within(0.0001f));
        }

        [Test]
        public void TrueOnePowerUsesPhysicalLensAngularSize()
        {
            float fieldOfView = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 1f, 0.014f, 0.08f);
            Assert.That(Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f), Is.EqualTo(0.014f / 0.08f).Within(0.000001f));
        }

        [Test]
        public void TrueOnePowerCalibrationChangesAngularScaleWithoutMovingTheReticleCenter()
        {
            SetField(_configuration, "_trueOnePowerCalibration", 1.1f);
            float fieldOfView = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 1f, 0.014f, 0.08f);
            Assert.That(Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f), Is.EqualTo(0.014f / 0.08f / 1.1f).Within(0.000001f));
            Matrix4x4 projection = ScopeOpticsMath.BuildProjection(_configuration, 1f, new Vector2(0.014f, 0.014f), new Vector3(0f, 0f, -0.08f), 0.01f, 1000f);
            Vector4 center = projection * new Vector4(0f, 0f, -10f, 1f);
            Assert.That(center.x / center.w, Is.EqualTo(0f).Within(0.000001f));
            Assert.That(center.y / center.w, Is.EqualTo(0f).Within(0.000001f));
        }

        [Test]
        public void FixedPowerClampsBothEndsToTheSameMagnification()
        {
            Assert.That(ScopeOpticsMath.ClampMagnification(1f, 4f, 4f), Is.EqualTo(4f));
            Assert.That(ScopeOpticsMath.ClampMagnification(25f, 4f, 4f), Is.EqualTo(4f));
        }

        [TestCase(-20f, 1f)]
        [TestCase(0f, 1f)]
        [TestCase(1f, 1f)]
        [TestCase(6f, 6f)]
        [TestCase(25f, 6f)]
        public void VariablePowerClampsAtConfiguredBounds(float input, float expected)
        {
            Assert.That(ScopeOpticsMath.ClampMagnification(input, 1f, 6f), Is.EqualTo(expected));
        }

        [Test]
        public void ProjectionKeepsCenteredOpticalAxisCenteredWithANonsquareTarget()
        {
            Matrix4x4 projection = ScopeOpticsMath.BuildProjection(_configuration, 6f, new Vector2(0.02f, 0.01f), new Vector3(0f, 0f, -0.08f), 0.01f, 1000f);
            Vector4 center = projection * new Vector4(0f, 0f, -100f, 1f);
            Assert.That(center.x / center.w, Is.EqualTo(0f).Within(0.000001f));
            Assert.That(center.y / center.w, Is.EqualTo(0f).Within(0.000001f));
            Assert.That(projection.m11 / projection.m00, Is.EqualTo(2f).Within(0.00001f));
        }

        [Test]
        public void TrueOnePowerOffAxisProjectionMatchesTheRayThroughEachLensCorner()
        {
            Vector2 halfSize = new Vector2(0.014f, 0.012f);
            Vector3 eye = new Vector3(0.003f, -0.002f, -0.08f);
            Matrix4x4 projection = ScopeOpticsMath.BuildProjection(_configuration, 1f, halfSize, eye, 0.01f, 1000f);
            for (int horizontal = -1; horizontal <= 1; horizontal += 2)
            {
                for (int vertical = -1; vertical <= 1; vertical += 2)
                {
                    Vector4 lensRay = new Vector4(horizontal * halfSize.x - eye.x, vertical * halfSize.y - eye.y, eye.z, 1f);
                    Vector4 clip = projection * lensRay;
                    Assert.That(clip.x / clip.w, Is.EqualTo(horizontal).Within(0.00001f));
                    Assert.That(clip.y / clip.w, Is.EqualTo(vertical).Within(0.00001f));
                }
            }
        }

        [Test]
        public void CalibratedFieldOfViewUsesTheAuthoredReferenceRatherThanTheEyeDistance()
        {
            SetField(_configuration, "_projectionMode", ScopeProjectionMode.CalibratedFieldOfView);
            SetField(_configuration, "_calibrationMagnification", 4f);
            SetField(_configuration, "_calibrationVerticalFov", 6f);
            float nearEye = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 4f, 0.014f, 0.05f);
            float farEye = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 4f, 0.014f, 0.15f);
            Assert.That(nearEye, Is.EqualTo(6f).Within(0.000001f));
            Assert.That(farEye, Is.EqualTo(nearEye));
        }

        [Test]
        public void FirstFocalPlaneTracksTargetScaleAndSecondFocalPlaneKeepsItsVisibleSize()
        {
            float onePowerFov = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 1f, 0.014f, 0.08f);
            float sixPowerFov = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 6f, 0.014f, 0.08f);
            float targetScale = Mathf.Tan(onePowerFov * Mathf.Deg2Rad * 0.5f) / Mathf.Tan(sixPowerFov * Mathf.Deg2Rad * 0.5f);
            float onePowerReticle = ScopeOpticsMath.GetReticleScale(_configuration, 1f, 0.014f, 0.08f);
            float sixPowerReticle = ScopeOpticsMath.GetReticleScale(_configuration, 6f, 0.014f, 0.08f);
            Assert.That(sixPowerReticle / onePowerReticle, Is.EqualTo(targetScale).Within(0.0001f));
            FieldInfo plane = typeof(ScopeOpticsConfig).GetField("_reticlePlane", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(plane, Is.Not.Null);
            plane.SetValue(_configuration, System.Enum.Parse(plane.FieldType, "SecondFocalPlane"));
            Assert.That(ScopeOpticsMath.GetReticleScale(_configuration, 1f, 0.014f, 0.08f), Is.EqualTo(ScopeOpticsMath.GetReticleScale(_configuration, 6f, 0.014f, 0.08f)).Within(0.000001f));
        }

        [Test]
        public void FirstFocalPlaneTracksACalibratedNonlinearOpticalScale()
        {
            SetField(_configuration, "_fieldOfViewMultiplier", AnimationCurve.Linear(1f, 1f, 6f, 0.8f));
            float onePowerFov = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 1f, 0.014f, 0.09f);
            float sixPowerFov = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, 6f, 0.014f, 0.09f);
            float targetScale = Mathf.Tan(onePowerFov * Mathf.Deg2Rad * 0.5f) / Mathf.Tan(sixPowerFov * Mathf.Deg2Rad * 0.5f);
            float onePowerReticle = ScopeOpticsMath.GetReticleScale(_configuration, 1f, 0.014f, 0.09f);
            float sixPowerReticle = ScopeOpticsMath.GetReticleScale(_configuration, 6f, 0.014f, 0.09f);
            Assert.That(targetScale, Is.EqualTo(7.5f).Within(0.0001f));
            Assert.That(sixPowerReticle / onePowerReticle, Is.EqualTo(targetScale).Within(0.0001f));
        }

        [Test]
        public void SecondFocalPlaneKeepsMaterialReticleSizeAcrossEyeDistances()
        {
            SetField(_configuration, "_reticlePlane", ScopeReticlePlane.SecondFocalPlane);
            float nearEye = ScopeOpticsMath.GetReticleScale(_configuration, 1f, 0.014f, 0.07f);
            float farEye = ScopeOpticsMath.GetReticleScale(_configuration, 6f, 0.014f, 0.11f);
            Assert.That(nearEye, Is.EqualTo(1f));
            Assert.That(farEye, Is.EqualTo(1f));
        }

        [TestCase(1f, -0.001f)]
        [TestCase(1f, 0.001f)]
        [TestCase(6f, -0.001f)]
        [TestCase(6f, 0.001f)]
        public void ReticleParallaxOffsetMatchesTheFocusedTargetProjection(float magnification, float horizontalEyeOffset)
        {
            Vector2 halfSize = new Vector2(0.014f, 0.012f);
            Vector3 eye = new Vector3(horizontalEyeOffset, 0.0005f, -0.09f);
            Matrix4x4 projection = ScopeOpticsMath.BuildProjection(_configuration, magnification, halfSize, eye, 0.01f, 1000f);
            Vector4 clip = projection * new Vector4(-eye.x, -eye.y, -(100f - eye.z), 1f);
            Vector2 targetOffset = new Vector2(clip.x, clip.y) / (2f * clip.w);
            Vector2 reticleOffset = ScopeOpticsMath.GetReticleParallaxOffset(_configuration, magnification, halfSize, eye, 100f);
            Assert.That(Vector2.Distance(targetOffset, reticleOffset), Is.LessThan(0.000001f));
        }

        [TestCase(1f)]
        [TestCase(6f)]
        public void AfocalPupilTranslationPreservesTheLensRelativeReticleOffsetAtEveryPower(float magnification)
        {
            Vector2 halfSize = new Vector2(0.014f, 0.012f);
            Vector3 eye = new Vector3(0.001f, -0.0005f, -0.09f);
            Vector2 offset = ScopeOpticsMath.GetReticleParallaxOffset(_configuration, magnification, halfSize, eye, 1000000f);
            Assert.That(offset.x, Is.EqualTo(eye.x / (2f * halfSize.x)).Within(0.000001f));
            Assert.That(offset.y, Is.EqualTo(eye.y / (2f * halfSize.y)).Within(0.000001f));
        }

        [Test]
        public void ReticleParallaxResidualChangesSignAcrossTheFocusedDistance()
        {
            Vector2 halfSize = new Vector2(0.014f, 0.014f);
            Vector3 eye = new Vector3(0.002f, 0f, -0.09f);
            Vector2 reticle = ScopeOpticsMath.GetReticleParallaxOffset(_configuration, 6f, halfSize, eye, 100f);
            Vector2 near = ScopeOpticsMath.GetReticleParallaxOffset(_configuration, 6f, halfSize, eye, 50f) - reticle;
            Vector2 far = ScopeOpticsMath.GetReticleParallaxOffset(_configuration, 6f, halfSize, eye, 200f) - reticle;
            Assert.That(near.sqrMagnitude, Is.GreaterThan(0f));
            Assert.That(far.sqrMagnitude, Is.GreaterThan(0f));
            Assert.That(Vector2.Dot(near, far), Is.LessThan(0f));
        }

        [Test]
        public void ParallaxErrorVanishesOnlyWhenTargetDistanceMatchesFocusOrEyeIsCentered()
        {
            Vector2 offset = new Vector2(0.002f, -0.001f);
            Assert.That(ScopeOpticsMath.GetParallaxAngularError(offset, 100f, 100f), Is.EqualTo(Vector2.zero));
            Assert.That(ScopeOpticsMath.GetParallaxAngularError(Vector2.zero, 50f, 100f), Is.EqualTo(Vector2.zero));
            Vector2 near = ScopeOpticsMath.GetParallaxAngularError(offset, 50f, 100f);
            Vector2 far = ScopeOpticsMath.GetParallaxAngularError(offset, 200f, 100f);
            Assert.That(Vector2.Dot(near, far), Is.LessThan(0f));
            Assert.That(near.magnitude / far.magnitude, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void AngularCorrectionUnitsHaveExplicitPhysicalConversions()
        {
            Assert.That(ScopeOpticsMath.DegreesToMilliradians(1f), Is.EqualTo(Mathf.PI / 180f * 1000f).Within(0.00001f));
            Assert.That(ScopeOpticsMath.MilliradiansToDegrees(1000f), Is.EqualTo(Mathf.Rad2Deg).Within(0.00001f));
            Assert.That(ScopeOpticsMath.MinutesOfAngleToMilliradians(1f), Is.EqualTo(Mathf.PI / 10800f * 1000f).Within(0.000001f));
        }

        [Test]
        public void ProjectionAndFieldOfViewStayFiniteAcrossNormalGeometryAndPowerRanges()
        {
            float[] distances = { 0.025f, 0.08f, 0.2f };
            float[] powers = { 1f, 3f, 6f };
            foreach (float distance in distances)
            {
                foreach (float power in powers)
                {
                    float fieldOfView = ScopeOpticsMath.GetVerticalFieldOfView(_configuration, power, 0.014f, distance);
                    Assert.That(float.IsFinite(fieldOfView), Is.True);
                    Assert.That(fieldOfView, Is.InRange(0.001f, 179f));
                    Matrix4x4 projection = ScopeOpticsMath.BuildProjection(_configuration, power, new Vector2(0.014f, 0.012f), new Vector3(0.005f, -0.003f, -distance), 0.01f, 1000f);
                    for (int index = 0; index < 16; index++)
                    {
                        Assert.That(float.IsFinite(projection[index]), Is.True);
                    }
                }
            }
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(instance, value);
        }
    }
}
