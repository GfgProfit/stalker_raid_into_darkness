using NUnit.Framework;
using RaidIntoDarkness.Weapons.Optics;
using UnityEngine;

namespace RaidIntoDarkness.Weapons.Tests
{
    public sealed class ScopeProProjectionTests
    {
        [TestCase(0.014f, 0.014f, 0.09f, 1f)]
        [TestCase(0.02645f, 0.02645f, 0.12f, 0.6f)]
        [TestCase(0.021f, 0.009f, 0.3f, 2f)]
        public void CaptureScaleMatchesTheNormalizedViewRayDerivative(float halfWidth, float halfHeight, float eyeDistance, float renderSize)
        {
            Vector2 halfSize = new Vector2(halfWidth, halfHeight);
            Vector2 scale = ScopeProProjection.GetCaptureProjectionScale(halfSize, eyeDistance, renderSize);
            float step = 0.001f;
            Vector2 horizontalDerivative = (SampleSourceUv(new Vector2(0.5f + step, 0.5f), halfSize, eyeDistance, renderSize) - SampleSourceUv(new Vector2(0.5f - step, 0.5f), halfSize, eyeDistance, renderSize)) / (2f * step);
            Vector2 verticalDerivative = (SampleSourceUv(new Vector2(0.5f, 0.5f + step), halfSize, eyeDistance, renderSize) - SampleSourceUv(new Vector2(0.5f, 0.5f - step), halfSize, eyeDistance, renderSize)) / (2f * step);
            Assert.That(scale.x, Is.EqualTo(horizontalDerivative.x).Within(0.0001f));
            Assert.That(scale.y, Is.EqualTo(verticalDerivative.y).Within(0.0001f));
            Assert.That(horizontalDerivative.y, Is.EqualTo(0f).Within(0.000001f));
            Assert.That(verticalDerivative.x, Is.EqualTo(0f).Within(0.000001f));
        }

        [TestCase(1f)]
        [TestCase(0.6f)]
        [TestCase(2f)]
        public void OnePowerKeepsTheNearCenterWorldTargetAtItsUnmagnifiedFraming(float renderSize)
        {
            Vector2 halfSize = new Vector2(0.021f, 0.009f);
            float distance = 0.12f;
            float angle = 0.0001f;
            Matrix4x4 projection = CreateProjection(halfSize, distance, renderSize, 1f);
            Vector2 captureUv = ProjectTarget(projection, angle, angle);
            Vector2 displayedUv = new Vector2(FindLensCoordinate(captureUv.x, halfSize, distance, renderSize, true), FindLensCoordinate(captureUv.y, halfSize, distance, renderSize, false));
            Vector2 outsideUv = new Vector2(0.5f + distance * Mathf.Tan(angle) / (2f * halfSize.x), 0.5f + distance * Mathf.Tan(angle) / (2f * halfSize.y));
            Assert.That(displayedUv.x, Is.EqualTo(outsideUv.x).Within(0.000001f));
            Assert.That(displayedUv.y, Is.EqualTo(outsideUv.y).Within(0.000001f));
        }

        [Test]
        public void EightPowerRetainsItsMagnificationAfterSourceUvCompensation()
        {
            Vector2 halfSize = new Vector2(0.02645f, 0.02645f);
            float distance = 0.12f;
            float renderSize = 1f;
            float angle = 0.0002f;
            Vector2 oneCapture = ProjectTarget(CreateProjection(halfSize, distance, renderSize, 1f), angle, 0f);
            Vector2 eightCapture = ProjectTarget(CreateProjection(halfSize, distance, renderSize, 8f), angle, 0f);
            float onePosition = FindLensCoordinate(oneCapture.x, halfSize, distance, renderSize, true) - 0.5f;
            float eightPosition = FindLensCoordinate(eightCapture.x, halfSize, distance, renderSize, true) - 0.5f;
            Assert.That(eightPosition / onePosition, Is.EqualTo(8f).Within(0.002f));
        }

        [Test]
        public void NonsquareLensCompensatesEachAxisIndependently()
        {
            Vector2 halfSize = new Vector2(0.03f, 0.01f);
            Vector2 scale = ScopeProProjection.GetCaptureProjectionScale(halfSize, 0.1f, 1f);
            Assert.That(scale.x, Is.GreaterThan(scale.y));
            Matrix4x4 projection = CreateProjection(halfSize, 0.1f, 1f, 1f);
            Vector2 captureUv = ProjectTarget(projection, 0.0001f, 0.0001f);
            float horizontal = FindLensCoordinate(captureUv.x, halfSize, 0.1f, 1f, true) - 0.5f;
            float vertical = FindLensCoordinate(captureUv.y, halfSize, 0.1f, 1f, false) - 0.5f;
            Assert.That(vertical / horizontal, Is.EqualTo(3f).Within(0.002f));
        }

        [TestCase(0f, 0f, 0f)]
        [TestCase(-1f, -1f, -1f)]
        [TestCase(float.NaN, float.PositiveInfinity, float.NaN)]
        [TestCase(float.PositiveInfinity, float.NaN, float.PositiveInfinity)]
        public void InvalidValuesKeepTheCaptureProjectionFiniteAndPositive(float halfSize, float eyeDistance, float renderSize)
        {
            Vector2 scale = ScopeProProjection.GetCaptureProjectionScale(new Vector2(halfSize, -halfSize), eyeDistance, renderSize);
            Assert.That(float.IsFinite(scale.x), Is.True);
            Assert.That(float.IsFinite(scale.y), Is.True);
            Assert.That(scale.x, Is.GreaterThan(0f));
            Assert.That(scale.y, Is.GreaterThan(0f));
        }

        private static Matrix4x4 CreateProjection(Vector2 halfSize, float eyeDistance, float renderSize, float magnification)
        {
            float fieldOfView = ScopeOpticsMath.GetVerticalFieldOfView(null, magnification, halfSize.y, eyeDistance);
            Matrix4x4 projection = Matrix4x4.Perspective(fieldOfView, halfSize.x / halfSize.y, 0.01f, 1000f);
            Vector2 scale = ScopeProProjection.GetCaptureProjectionScale(halfSize, eyeDistance, renderSize);
            projection.m00 *= scale.x;
            projection.m11 *= scale.y;
            return projection;
        }

        private static Vector2 ProjectTarget(Matrix4x4 projection, float horizontalAngle, float verticalAngle)
        {
            Vector4 clip = projection * new Vector4(Mathf.Tan(horizontalAngle) * 100f, Mathf.Tan(verticalAngle) * 100f, -100f, 1f);
            return new Vector2(clip.x, clip.y) / clip.w * 0.5f + Vector2.one * 0.5f;
        }

        private static float FindLensCoordinate(float capturedCoordinate, Vector2 halfSize, float eyeDistance, float renderSize, bool horizontal)
        {
            float lower = 0.25f;
            float upper = 0.75f;
            for (int iteration = 0; iteration < 32; iteration++)
            {
                float midpoint = (lower + upper) * 0.5f;
                Vector2 coordinate = horizontal ? new Vector2(midpoint, 0.5f) : new Vector2(0.5f, midpoint);
                Vector2 sampled = SampleSourceUv(coordinate, halfSize, eyeDistance, renderSize);
                float value = horizontal ? sampled.x : sampled.y;
                if (value < capturedCoordinate)
                {
                    lower = midpoint;
                }
                else
                {
                    upper = midpoint;
                }
            }

            return (lower + upper) * 0.5f;
        }

        private static Vector2 SampleSourceUv(Vector2 uv, Vector2 halfSize, float eyeDistance, float renderSize)
        {
            Vector2 centered = uv - Vector2.one * 0.5f;
            Vector3 surfaceToEye = new Vector3(-centered.x * 2f * halfSize.x, -centered.y * 2f * halfSize.y, -eyeDistance).normalized;
            return (centered - new Vector2(surfaceToEye.x, surfaceToEye.y)) * renderSize + Vector2.one * 0.5f;
        }
    }
}
