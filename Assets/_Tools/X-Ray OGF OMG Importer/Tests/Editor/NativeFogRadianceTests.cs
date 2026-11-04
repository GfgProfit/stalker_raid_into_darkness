using System;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class NativeFogRadianceTests
    {
        private const float TOLERANCE = 0.000002f;

        [Test]
        public void BlackSourceFogKeepsNativeSkyColorNeutral()
        {
            Assert.AreEqual(Color.white, XRayWeatherFogMath.ToSkyTint(Color.black));
            Assert.AreEqual(Color.white, XRayWeatherFogMath.ToSkyTint(Color.clear));
            Assert.AreEqual(Color.white, XRayWeatherFogMath.ToSkyTint(new Color(-1.0f, -2.0f, -3.0f, 0.25f)));
        }

        [TestCase(0.0f)]
        [TestCase(0.0001f)]
        [TestCase(0.05f)]
        [TestCase(0.5f)]
        [TestCase(1.0f)]
        [TestCase(10.0f)]
        [TestCase(float.MaxValue)]
        public void AchromaticSourceFogDoesNotChangeNativeSkyHue(float value)
        {
            Assert.AreEqual(Color.white, XRayWeatherFogMath.ToSkyTint(new Color(value, value, value, 0.25f)));
        }

        [Test]
        public void UnitAndHdrSourceColorsPreserveNormalizedGammaHue()
        {
            Color unit = XRayWeatherFogMath.ToSkyTint(new Color(0.25f, 0.5f, 1.0f, 0.25f));
            Color hdr = XRayWeatherFogMath.ToSkyTint(new Color(2.5f, 5.0f, 10.0f, 0.75f));
            Assert.That(unit.r, Is.EqualTo(Math.Pow(0.25, 2.2)).Within(TOLERANCE));
            Assert.That(unit.g, Is.EqualTo(Math.Pow(0.5, 2.2)).Within(TOLERANCE));
            Assert.AreEqual(1.0f, unit.b);
            Assert.AreEqual(1.0f, unit.a);
            for (int channel = 0; channel < 4; channel++) Assert.That(hdr[channel], Is.EqualTo(unit[channel]).Within(TOLERANCE));
        }

        [Test]
        public void DarkSourceFogDesaturatesWithoutChangingItsSourceValues()
        {
            var source = new Color(0.02f, 0.04f, 0.1f, 0.3f);
            Color original = source;
            Color tint = XRayWeatherFogMath.ToSkyTint(source);
            Assert.That(tint.r, Is.EqualTo(0.9 + 0.1 * Math.Pow(0.2, 2.2)).Within(TOLERANCE));
            Assert.That(tint.g, Is.EqualTo(0.9 + 0.1 * Math.Pow(0.4, 2.2)).Within(TOLERANCE));
            Assert.AreEqual(1.0f, tint.b);
            Assert.AreEqual(original, source);
            AssertValidTint(tint);
        }

        [TestCase(0.01f)]
        [TestCase(0.0001f)]
        [TestCase(0.00000001f)]
        [TestCase(float.Epsilon)]
        public void NearBlackColorsConvergeContinuouslyToNeutral(float value)
        {
            Color tint = XRayWeatherFogMath.ToSkyTint(new Color(0.0f, value, 0.0f, 0.0f));
            Assert.That(tint.r, Is.EqualTo(1.0f - value).Within(TOLERANCE));
            Assert.AreEqual(1.0f, tint.g);
            Assert.That(tint.b, Is.EqualTo(1.0f - value).Within(TOLERANCE));
            AssertValidTint(tint);
        }

        [TestCase(0.0f)]
        [TestCase(0.000001f)]
        [TestCase(0.0001f)]
        [TestCase(0.25f)]
        [TestCase(0.5f)]
        [TestCase(1.0f)]
        public void BlackToColoredWeatherCrossfadeHasNoTintDiscontinuity(float weight)
        {
            var endpoint = new Color(0.1f, 0.2f, 0.8f, 1.0f);
            Color actual = XRayWeatherFogMath.ToSkyTint(Color.LerpUnclamped(Color.black, endpoint, weight));
            Color expected = Color.LerpUnclamped(Color.white, XRayWeatherFogMath.ToSkyTint(endpoint), weight);
            for (int channel = 0; channel < 4; channel++) Assert.That(actual[channel], Is.EqualTo(expected[channel]).Within(TOLERANCE));
            AssertValidTint(actual);
        }

        [Test]
        public void TintIsContinuousWhenTheDominantWeatherChannelChanges()
        {
            var first = new Color(0.2f, 0.4f, 0.3f, 1.0f);
            var second = new Color(0.4f, 0.2f, 0.3f, 1.0f);
            Color before = XRayWeatherFogMath.ToSkyTint(Color.LerpUnclamped(first, second, 0.4999f));
            Color after = XRayWeatherFogMath.ToSkyTint(Color.LerpUnclamped(first, second, 0.5001f));
            for (int channel = 0; channel < 3; channel++) Assert.That(Mathf.Abs(before[channel] - after[channel]), Is.LessThan(0.0002f));
            AssertValidTint(before);
            AssertValidTint(after);
        }

        [Test]
        public void NegativeSourceComponentsClampBeforeHueNormalization()
        {
            Color tint = XRayWeatherFogMath.ToSkyTint(new Color(-1.0f, 0.25f, 0.5f, 1.0f));
            Assert.That(tint.r, Is.EqualTo(0.5f).Within(TOLERANCE));
            Assert.That(tint.g, Is.EqualTo(0.5 + 0.5 * Math.Pow(0.5, 2.2)).Within(TOLERANCE));
            Assert.AreEqual(1.0f, tint.b);
            AssertValidTint(tint);
        }

        [TestCase(float.Epsilon)]
        [TestCase(0.000000000000000000000000000001f)]
        [TestCase(float.MaxValue)]
        [TestCase(-float.MaxValue)]
        public void ExtremeFiniteSourceColorsKeepFiniteBoundedTint(float value)
        {
            AssertValidTint(XRayWeatherFogMath.ToSkyTint(new Color(value, value * 0.5f, -value, 1.0f)));
        }

        [TestCase(0, float.NaN)]
        [TestCase(1, float.NaN)]
        [TestCase(2, float.NaN)]
        [TestCase(0, float.PositiveInfinity)]
        [TestCase(1, float.PositiveInfinity)]
        [TestCase(2, float.PositiveInfinity)]
        [TestCase(0, float.NegativeInfinity)]
        [TestCase(1, float.NegativeInfinity)]
        [TestCase(2, float.NegativeInfinity)]
        public void NonfiniteSourceRgbCannotReachNativeFog(int channel, float value)
        {
            Color source = Color.gray;
            source[channel] = value;
            Assert.Throws<ArgumentOutOfRangeException>(() => XRayWeatherFogMath.ToSkyTint(source));
        }

        [Test]
        public void NativeFogUsesWeatherSkyTintAndRestoresAuthoredColorMode()
        {
            if (XRayWeatherController.Active != null) Assert.Ignore("This isolated native Fog fixture preserves an existing weather owner.");
            Scene scene = EditorSceneManager.NewPreviewScene();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Fog authored = profile.Add<Fog>(false);
            var authoredTint = new Color(0.13f, 0.23f, 0.37f, 1.0f);
            authored.tint.Override(authoredTint);
            authored.colorMode.Override(FogColorMode.ConstantColor);
            XRayWeatherFogRenderer renderer = null;
            try
            {
                var root = new GameObject("Native fog sky tint fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                Volume volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = float.MaxValue;
                volume.weight = 1.0f;
                volume.sharedProfile = profile;
                renderer = root.AddComponent<XRayWeatherFogRenderer>();
                renderer.Configure(volume);
                XRayWeatherSnapshot snapshot = Snapshot(new Color(0.1f, 0.2f, 0.3f, 1.0f));
                Assert.IsTrue(renderer.TrySetWeather(renderer, snapshot), renderer.Diagnostic);
                Assert.IsTrue(volume.profile.TryGet(out Fog fog));
                Assert.AreEqual(FogColorMode.SkyColor, fog.colorMode.value);
                Assert.IsTrue(fog.tint.overrideState);
                Assert.AreEqual(XRayWeatherFogMath.ToSkyTint(snapshot.FogColor), fog.tint.value);
                float meanFreePath = fog.meanFreePath.value;
                Assert.IsTrue(renderer.TrySetWeather(renderer, Snapshot(Color.black)), renderer.Diagnostic);
                Assert.AreEqual(Color.white, fog.tint.value);
                Assert.AreEqual(meanFreePath, fog.meanFreePath.value);
                Assert.AreEqual(new Color(0.1f, 0.2f, 0.3f, 1.0f), snapshot.FogColor);
                Assert.AreEqual(authoredTint, authored.tint.value);
                Assert.AreEqual(FogColorMode.ConstantColor, authored.colorMode.value);
                renderer.ReleaseWeather(renderer);
                Assert.IsFalse(renderer.HasWeather);
                Assert.IsFalse(volume.HasInstantiatedProfile());
                Assert.AreSame(profile, volume.sharedProfile);
                Assert.AreEqual(authoredTint, authored.tint.value);
                Assert.IsTrue(authored.tint.overrideState);
                Assert.AreEqual(FogColorMode.ConstantColor, authored.colorMode.value);
                Assert.IsTrue(authored.colorMode.overrideState);
                Assert.IsFalse(authored.enabled.overrideState);
            }
            finally
            {
                if (renderer != null) renderer.ReleaseWeather(renderer);
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (VolumeComponent component in profile.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(profile);
            }
        }

        private static void AssertValidTint(Color tint)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                Assert.IsFalse(float.IsNaN(tint[channel]) || float.IsInfinity(tint[channel]));
                Assert.That(tint[channel], Is.InRange(0.0f, 1.0f));
            }
            Assert.AreEqual(1.0f, tint.maxColorComponent);
            Assert.AreEqual(1.0f, tint.a);
        }

        private static XRayWeatherSnapshot Snapshot(Color color)
        {
            return new XRayWeatherSnapshot("00:00:00", 0.0, Color.white, Color.gray, Color.gray, 0.0f, 0.0f, 0.01f, 0.0f, Color.white, color, 0.2f, 300.0f, 400.0f, Color.gray, Color.gray, 0.0f, default, default);
        }
    }
}
