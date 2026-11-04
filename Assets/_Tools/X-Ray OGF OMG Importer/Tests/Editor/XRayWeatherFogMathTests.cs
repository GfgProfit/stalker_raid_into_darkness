using System;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherFogMathTests
    {
        private const float TOLERANCE = 0.00002f;

        [TestCase(0.0f, 100.0f)]
        [TestCase(0.2f, 300.0f)]
        [TestCase(1.0f, 100.0f)]
        [TestCase(1.2f, 400.0f)]
        [TestCase(-0.1f, 100.0f)]
        public void ParametersMatchSourceConstantsWithoutDensityClamping(float density, float distance)
        {
            XRayWeatherSnapshot snapshot = Snapshot("00:00:00", 0.0, density, distance, 1000.0f, Color.gray);
            Assert.IsTrue(XRayWeatherFogMath.TryGetParameters(snapshot, out Vector4 parameters));
            double near = (1.0 - density) * 0.85 * distance;
            double far = 0.99 * distance;
            double reciprocal = 1.0 / (far - near);
            Assert.That(parameters.x, Is.EqualTo(-near * reciprocal).Within(TOLERANCE * Math.Max(1.0, Math.Abs(near * reciprocal))));
            Assert.That(parameters.y, Is.EqualTo(near).Within(TOLERANCE * Math.Max(1.0, Math.Abs(near))));
            Assert.That(parameters.z, Is.EqualTo(far).Within(TOLERANCE * Math.Max(1.0, far)));
            Assert.That(parameters.w, Is.EqualTo(reciprocal).Within(TOLERANCE));
            Assert.AreEqual(density, snapshot.FogDensity);
            AssertFinite(parameters);
        }

        [Test]
        public void SourceFactorUsesRadialDistanceAndLinearSaturatedRamp()
        {
            XRayWeatherSnapshot snapshot = Snapshot("00:00:00", 0.0, 0.2f, 300.0f, 1000.0f, Color.gray);
            Assert.IsTrue(XRayWeatherFogMath.TryGetParameters(snapshot, out Vector4 parameters));
            Assert.That(XRayWeatherFogMath.EvaluateFactor(0.0f, parameters), Is.EqualTo(0.0f).Within(TOLERANCE));
            Assert.That(XRayWeatherFogMath.EvaluateFactor(204.0f, parameters), Is.EqualTo(0.0f).Within(TOLERANCE));
            Assert.That(XRayWeatherFogMath.EvaluateFactor(227.25f, parameters), Is.EqualTo(0.25f).Within(TOLERANCE));
            Assert.That(XRayWeatherFogMath.EvaluateFactor(250.5f, parameters), Is.EqualTo(0.5f).Within(TOLERANCE));
            Assert.That(XRayWeatherFogMath.EvaluateFactor(273.75f, parameters), Is.EqualTo(0.75f).Within(TOLERANCE));
            Assert.That(XRayWeatherFogMath.EvaluateFactor(297.0f, parameters), Is.EqualTo(1.0f).Within(TOLERANCE));
            Assert.That(XRayWeatherFogMath.EvaluateFactor(float.MaxValue, parameters), Is.EqualTo(1.0f));
        }

        [Test]
        public void DensityAboveOneKeepsNegativeNearAndNonzeroFogAtCamera()
        {
            XRayWeatherSnapshot snapshot = Snapshot("00:00:00", 0.0, 1.2f, 100.0f, 1000.0f, Color.gray);
            Assert.That(snapshot.FogNear, Is.EqualTo(-17.0f).Within(TOLERANCE));
            Assert.IsTrue(XRayWeatherFogMath.TryGetParameters(snapshot, out Vector4 parameters));
            Assert.That(XRayWeatherFogMath.EvaluateFactor(0.0f, parameters), Is.EqualTo(17.0f / 116.0f).Within(TOLERANCE));
            Assert.That(XRayWeatherFogMath.EvaluateFactor(41.0f, parameters), Is.EqualTo(0.5f).Within(TOLERANCE));
        }

        [TestCase(0.0f, 100.0f)]
        [TestCase(0.2f, 300.0f)]
        [TestCase(1.2f, 400.0f)]
        [TestCase(100.0f, 400.0f)]
        public void NativeAttenuationMatchesSourceAtOneOpticalDepthAnchor(float density, float distance)
        {
            XRayWeatherSnapshot snapshot = Snapshot("00:00:00", 0.0, density, distance, 1000.0f, Color.gray);
            Assert.IsTrue(XRayWeatherFogMath.TryGetMeanFreePath(snapshot, out float meanFreePath));
            double near = (1.0 - density) * 0.85 * distance;
            double far = 0.99 * distance;
            double anchorFactor = 1.0 - Math.Exp(-1.0);
            double anchorDistance = near + anchorFactor * (far - near);
            Assert.That(meanFreePath, Is.EqualTo(Math.Max(1.0, anchorDistance)).Within(TOLERANCE * Math.Max(1.0, Math.Abs(anchorDistance))));
            Assert.AreEqual(density, snapshot.FogDensity);
            if (anchorDistance >= 1.0)
            {
                Assert.IsTrue(XRayWeatherFogMath.TryGetParameters(snapshot, out Vector4 parameters));
                Assert.That(XRayWeatherFogMath.EvaluateFactor(meanFreePath, parameters), Is.EqualTo(anchorFactor).Within(TOLERANCE));
            }
        }

        [Test]
        public void NativeColorUsesSourceGamma22BeforeLinearHdrpFog()
        {
            Color converted = XRayWeatherFogMath.ToLinearColor(new Color(0.1f, 0.4f, 0.8f, 0.3f));
            Assert.That(converted.r, Is.EqualTo(Math.Pow(0.1, 2.2)).Within(TOLERANCE));
            Assert.That(converted.g, Is.EqualTo(Math.Pow(0.4, 2.2)).Within(TOLERANCE));
            Assert.That(converted.b, Is.EqualTo(Math.Pow(0.8, 2.2)).Within(TOLERANCE));
        }

        [TestCase(float.NaN, 100.0f)]
        [TestCase(float.PositiveInfinity, 100.0f)]
        [TestCase(float.NegativeInfinity, 100.0f)]
        [TestCase(0.2f, float.NaN)]
        [TestCase(0.2f, float.PositiveInfinity)]
        [TestCase(0.2f, float.NegativeInfinity)]
        [TestCase(0.2f, 0.0f)]
        [TestCase(0.2f, -100.0f)]
        [TestCase(-1.0f, 100.0f)]
        [TestCase(float.MaxValue, 100.0f)]
        public void InvalidOrInvertedRangesCannotPublishNonfiniteConstants(float density, float distance)
        {
            XRayWeatherSnapshot snapshot = Snapshot("00:00:00", 0.0, density, distance, 1000.0f, Color.gray);
            Assert.IsFalse(XRayWeatherFogMath.TryGetParameters(snapshot, out Vector4 parameters));
            AssertFinite(parameters);
        }

        [Test]
        public void SamplingInterpolatesBeforeVisibilityAndDistanceClamp()
        {
            Color firstColor = new Color(0.1f, 0.2f, 0.3f, 1.0f);
            Color secondColor = new Color(0.7f, 0.6f, 0.5f, 1.0f);
            XRayWeatherProfile profile = Profile(firstColor, secondColor);
            try
            {
                Assert.IsTrue(profile.TryEvaluate(21600.0, out XRayWeatherSnapshot snapshot, out string diagnostic), diagnostic);
                Assert.That(snapshot.FarPlane, Is.EqualTo(400.0f).Within(TOLERANCE));
                Assert.That(snapshot.FogDistance, Is.EqualTo(390.0f).Within(TOLERANCE));
                Assert.That(snapshot.FogDensity, Is.EqualTo(0.7f).Within(TOLERANCE));
                Assert.That(snapshot.FogNear, Is.EqualTo(99.45f).Within(TOLERANCE));
                Assert.That(snapshot.FogFar, Is.EqualTo(386.1f).Within(TOLERANCE * 2.0f));
                Assert.That(snapshot.FogColor.r, Is.EqualTo(0.4f).Within(TOLERANCE));
                Assert.That(snapshot.FogColor.g, Is.EqualTo(0.4f).Within(TOLERANCE));
                Assert.That(snapshot.FogColor.b, Is.EqualTo(0.4f).Within(TOLERANCE));
                Assert.IsTrue(XRayWeatherFogMath.TryGetParameters(snapshot, out Vector4 parameters));
                Assert.That(parameters.z, Is.EqualTo(386.1f).Within(TOLERANCE * 2.0f));
                Assert.AreEqual(600.0f, profile.GetKeyframe(0).FarPlane);
                Assert.AreEqual(900.0f, profile.GetKeyframe(0).FogDistance);
                Assert.IsTrue(profile.TryEvaluate(21600.0, out XRayWeatherSnapshot repeated, out diagnostic), diagnostic);
                Assert.AreEqual(snapshot.FarPlane, repeated.FarPlane);
                Assert.AreEqual(snapshot.FogDistance, repeated.FogDistance);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ClearRainSnapshotCrossfadeDoesNotApplyVisibilityAgain()
        {
            XRayWeatherProfile profile = Profile(Color.black, Color.white);
            try
            {
                Assert.IsTrue(profile.TryEvaluate(0.0, out XRayWeatherSnapshot clear, out string diagnostic), diagnostic);
                Assert.IsTrue(profile.TryEvaluate(43200.0, out XRayWeatherSnapshot rain, out diagnostic), diagnostic);
                XRayWeatherSnapshot blended = XRayWeatherSnapshot.Blend(clear, rain, 0.25f);
                Assert.That(blended.FarPlane, Is.EqualTo(350.0f).Within(TOLERANCE));
                Assert.That(blended.FogDistance, Is.EqualTo(292.5f).Within(TOLERANCE));
                Assert.That(blended.FogDensity, Is.EqualTo(0.45f).Within(TOLERANCE));
                Assert.That(blended.FogNear, Is.EqualTo(136.74375f).Within(TOLERANCE * 2.0f));
                Assert.IsTrue(XRayWeatherFogMath.TryGetParameters(blended, out Vector4 parameters));
                AssertFinite(parameters);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void MidnightWrapKeepsContinuousFogWithoutMutatingDescriptors()
        {
            XRayWeatherProfile profile = Profile(Color.black, Color.white);
            try
            {
                Assert.IsTrue(profile.TryEvaluate(86399.5, out XRayWeatherSnapshot before, out string diagnostic), diagnostic);
                Assert.IsTrue(profile.TryEvaluate(86400.0, out XRayWeatherSnapshot midnight, out diagnostic), diagnostic);
                Assert.IsTrue(profile.TryEvaluate(86400.5, out XRayWeatherSnapshot after, out diagnostic), diagnostic);
                Assert.IsTrue(profile.TryEvaluate(-0.5, out XRayWeatherSnapshot negative, out diagnostic), diagnostic);
                Assert.AreEqual(before.FogDistance, negative.FogDistance);
                Assert.AreEqual(before.FogDensity, negative.FogDensity);
                Assert.That(before.FogDensity, Is.EqualTo(after.FogDensity).Within(TOLERANCE));
                Assert.That(before.FogDistance, Is.EqualTo(after.FogDistance).Within(TOLERANCE * 2.0f));
                Assert.That(midnight.FogDistance, Is.EqualTo(290.0f).Within(TOLERANCE));
                Assert.That(midnight.FogDensity, Is.EqualTo(0.2f).Within(TOLERANCE));
                Assert.IsTrue(XRayWeatherFogMath.TryGetParameters(before, out Vector4 beforeParameters));
                Assert.IsTrue(XRayWeatherFogMath.TryGetParameters(after, out Vector4 afterParameters));
                AssertFinite(beforeParameters);
                AssertFinite(afterParameters);
                Assert.That(XRayWeatherFogMath.EvaluateFactor(250.0f, beforeParameters), Is.EqualTo(XRayWeatherFogMath.EvaluateFactor(250.0f, afterParameters)).Within(TOLERANCE));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static XRayWeatherProfile Profile(Color firstColor, Color secondColor)
        {
            var profile = ScriptableObject.CreateInstance<XRayWeatherProfile>();
            XRayWeatherSnapshot first = Snapshot("00:00:00", 0.0, 0.2f, 900.0f, 600.0f, firstColor);
            XRayWeatherSnapshot second = Snapshot("12:00:00", 43200.0, 1.2f, 300.0f, 1000.0f, secondColor);
            var positions = new Vector2[24];
            profile.Initialize("fog_numeric", "analytic_fog_numeric", "independent source fog interpolation fixture", new[] { first, second }, positions, null, null, null, null, 0.5f);
            return profile;
        }

        private static XRayWeatherSnapshot Snapshot(string key, double seconds, float density, float distance, float farPlane, Color color)
        {
            return new XRayWeatherSnapshot(key, seconds, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.01f, 0.0f, Color.white, color, density, distance, farPlane, Color.gray, Color.gray, 0.0f, default, default);
        }

        private static void AssertFinite(Vector4 parameters)
        {
            for (int i = 0; i < 4; i++) Assert.IsFalse(float.IsNaN(parameters[i]) || float.IsInfinity(parameters[i]), "Fog constant " + i);
        }
    }
}
