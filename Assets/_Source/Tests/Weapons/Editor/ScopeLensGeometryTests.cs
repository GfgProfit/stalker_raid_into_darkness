using NUnit.Framework;
using RaidIntoDarkness.Weapons.Optics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Weapons.Tests
{
    public sealed class ScopeLensGeometryTests
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

        [TestCase(1f, 1f, 1f, 0f, 0f, 0f)]
        [TestCase(1.67f, 1.67f, 1f, 0f, 0f, 0f)]
        [TestCase(1.67f, 1.67f, 1f, 25f, 35f, 20f)]
        [TestCase(1.67f, 1.2f, 0.9f, 40f, 20f, 35f)]
        public void TrueOnePowerMatchesEveryTransformedLensCorner(float scaleX, float scaleY, float scaleZ, float pitch, float yaw, float roll)
        {
            Vector2 localHalfSize = new Vector2(0.0158353858f, 0.014f);
            Matrix4x4 matrix = CreateMatrix(new Vector3(scaleX, scaleY, scaleZ), new Vector3(pitch, yaw, roll));
            ScopeLensGeometry geometry = CreateGeometry(matrix, localHalfSize, new Vector3(0.003f, -0.002f, -0.09f), out Vector3 eyeWorld);
            Matrix4x4 projection = geometry.ApplyProjectionShear(ScopeOpticsMath.BuildProjection(_configuration, 1f, geometry.HalfSizeMeters, geometry.ProjectionEyeLocal, 0.01f, 1000f));
            for (int horizontal = -1; horizontal <= 1; horizontal += 2)
            {
                for (int vertical = -1; vertical <= 1; vertical += 2)
                {
                    Vector3 world = matrix.MultiplyPoint3x4(new Vector3(horizontal * localHalfSize.x, vertical * localHalfSize.y, 0f));
                    Vector2 projected = Project(projection, geometry.Rotation, eyeWorld, world);
                    Assert.That(projected.x, Is.EqualTo(horizontal).Within(0.0001f));
                    Assert.That(projected.y, Is.EqualTo(vertical).Within(0.0001f));
                }
            }
        }

        [Test]
        public void HudScaleChangesLensExtentsWithoutScalingPhysicalEyeRelief()
        {
            Vector2 localHalfSize = new Vector2(0.0158353858f, 0.0158353858f);
            Matrix4x4 matrix = Matrix4x4.Scale(new Vector3(1.67f, 1.67f, 1f));
            ScopeLensGeometry geometry = CreateGeometry(matrix, localHalfSize, new Vector3(0.003f, -0.002f, -0.09f), out _);
            Assert.That(geometry.HalfSizeMeters.x, Is.EqualTo(localHalfSize.x * 1.67f).Within(0.000001f));
            Assert.That(geometry.HalfSizeMeters.y, Is.EqualTo(localHalfSize.y * 1.67f).Within(0.000001f));
            Assert.That(geometry.EyeLocal.x, Is.EqualTo(0.003f).Within(0.000001f));
            Assert.That(geometry.EyeLocal.y, Is.EqualTo(-0.002f).Within(0.000001f));
            Assert.That(geometry.EyeLocal.z, Is.EqualTo(-0.09f).Within(0.000001f));
        }

        [Test]
        public void SkewedLensUsesTheTransformedPlaneNormal()
        {
            Matrix4x4 matrix = CreateMatrix(new Vector3(1.67f, 1.2f, 1f), new Vector3(25f, 35f, 20f));
            ScopeLensGeometry geometry = CreateGeometry(matrix, new Vector2(0.015f, 0.015f), new Vector3(0f, 0f, -0.09f), out _);
            Vector3 expectedNormal = matrix.inverse.transpose.MultiplyVector(Vector3.forward).normalized;
            Assert.That(Vector3.Dot(geometry.Rotation * Vector3.forward, expectedNormal), Is.EqualTo(1f).Within(0.000001f));
            Assert.That(Mathf.Abs(geometry.Shear), Is.GreaterThan(0.01f));
            Assert.That(Vector3.Dot(geometry.Rotation * Vector3.right, geometry.Rotation * Vector3.up), Is.EqualTo(0f).Within(0.000001f));
        }

        [Test]
        public void MagnificationScalesTheTargetRelativeToItsOpticalReferenceUnderShear()
        {
            Matrix4x4 matrix = CreateMatrix(new Vector3(1.67f, 1.2f, 1f), new Vector3(25f, 35f, 20f));
            ScopeLensGeometry geometry = CreateGeometry(matrix, new Vector2(0.015f, 0.015f), new Vector3(0.002f, -0.001f, -0.09f), out Vector3 eyeWorld);
            Matrix4x4 onePower = geometry.ApplyProjectionShear(ScopeOpticsMath.BuildProjection(_configuration, 1f, geometry.HalfSizeMeters, geometry.ProjectionEyeLocal, 0.01f, 1000f));
            Matrix4x4 sixPower = geometry.ApplyProjectionShear(ScopeOpticsMath.BuildProjection(_configuration, 6f, geometry.HalfSizeMeters, geometry.ProjectionEyeLocal, 0.01f, 1000f));
            Vector3 center = eyeWorld + geometry.Rotation * new Vector3(0f, 0f, 100f);
            Vector3 target = center + geometry.Rotation * new Vector3(1f, 0.5f, 0f);
            Vector2 onePowerOffset = Project(onePower, geometry.Rotation, eyeWorld, target) - Project(onePower, geometry.Rotation, eyeWorld, center);
            Vector2 sixPowerOffset = Project(sixPower, geometry.Rotation, eyeWorld, target) - Project(sixPower, geometry.Rotation, eyeWorld, center);
            Assert.That(sixPowerOffset.x, Is.EqualTo(onePowerOffset.x * 6f).Within(0.00001f));
            Assert.That(sixPowerOffset.y, Is.EqualTo(onePowerOffset.y * 6f).Within(0.00001f));
        }

        [TestCase(1f)]
        [TestCase(6f)]
        public void FiniteFocusReticleMatchesTheActualSkewedProjection(float magnification)
        {
            Matrix4x4 matrix = CreateMatrix(new Vector3(1.67f, 1.2f, 1f), new Vector3(25f, 35f, 20f));
            ScopeLensGeometry geometry = CreateGeometry(matrix, new Vector2(0.015f, 0.015f), new Vector3(0.002f, -0.001f, -0.09f), out Vector3 eyeWorld);
            Matrix4x4 projection = geometry.ApplyProjectionShear(ScopeOpticsMath.BuildProjection(_configuration, magnification, geometry.HalfSizeMeters, geometry.ProjectionEyeLocal, 0.01f, 1000f));
            Vector3 focusPoint = matrix.MultiplyPoint3x4(Vector3.zero) + geometry.Rotation * Vector3.forward * 100f;
            Vector2 actualOffset = Project(projection, geometry.Rotation, eyeWorld, focusPoint) * 0.5f;
            Vector2 reticleOffset = ScopeOpticsMath.GetReticleParallaxOffset(_configuration, magnification, geometry.HalfSizeMeters, geometry.ProjectionEyeLocal, 100f);
            Assert.That(reticleOffset.x, Is.EqualTo(actualOffset.x).Within(0.00001f));
            Assert.That(reticleOffset.y, Is.EqualTo(actualOffset.y).Within(0.00001f));
        }

        [TestCase(0f, 1f, 1f)]
        [TestCase(1f, 0f, 1f)]
        [TestCase(1f, 1f, 0f)]
        [TestCase(-1f, 1f, 1f)]
        public void DegenerateOrMirroredTransformsAreRejected(float x, float y, float z)
        {
            bool valid = ScopeLensGeometry.TryCreate(Matrix4x4.Scale(new Vector3(x, y, z)), new Vector3(0f, 0f, -0.09f), new Vector2(0.015f, 0.015f), out ScopeLensGeometry geometry);
            Assert.That(valid, Is.False);
            Assert.That(float.IsFinite(geometry.Shear), Is.True);
        }

        [Test]
        public void NonfiniteGeometryIsRejectedWithoutProducingNonfiniteOutput()
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.m00 = float.NaN;
            Assert.That(ScopeLensGeometry.TryCreate(matrix, new Vector3(0f, 0f, -0.09f), new Vector2(0.015f, 0.015f), out ScopeLensGeometry geometry), Is.False);
            Assert.That(float.IsFinite(geometry.HalfSizeMeters.x), Is.True);
            Assert.That(float.IsFinite(geometry.HalfSizeMeters.y), Is.True);
            Assert.That(float.IsFinite(geometry.Shear), Is.True);
        }

        private static Matrix4x4 CreateMatrix(Vector3 scale, Vector3 localRotation)
        {
            return Matrix4x4.TRS(new Vector3(3f, 5f, -2f), Quaternion.Euler(23f, 41f, 17f), scale) * Matrix4x4.Rotate(Quaternion.Euler(localRotation));
        }

        private static ScopeLensGeometry CreateGeometry(Matrix4x4 matrix, Vector2 halfSize, Vector3 eyeLocal, out Vector3 eyeWorld)
        {
            Vector3 origin = matrix.MultiplyPoint3x4(Vector3.zero);
            Assert.That(ScopeLensGeometry.TryCreate(matrix, origin, halfSize, out ScopeLensGeometry initial), Is.True);
            eyeWorld = origin + initial.Rotation * eyeLocal;
            Assert.That(ScopeLensGeometry.TryCreate(matrix, eyeWorld, halfSize, out ScopeLensGeometry geometry), Is.True);
            return geometry;
        }

        private static Vector2 Project(Matrix4x4 projection, Quaternion rotation, Vector3 eyeWorld, Vector3 world)
        {
            Vector3 view = Quaternion.Inverse(rotation) * (world - eyeWorld);
            Vector4 clip = projection * new Vector4(view.x, view.y, -view.z, 1f);
            return new Vector2(clip.x, clip.y) / clip.w;
        }
    }
}
