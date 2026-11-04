using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Ltx;
using XRay.Importer.Sources.Vfs;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayWeatherFxTests
    {
        private const double MIDNIGHT = 86400.0;
        private const double NOON = 43200.0;
        private const float TOLERANCE = 0.0001f;

        [Test]
        public void SavedEffectFramesAndResourcesMatchEffectiveSource()
        {
            XRayWeatherFxCatalog catalog = Catalog();
            XRaySourceConfiguration configuration = XRayLevelSourceCache.CreateMo2Configuration();
            configuration.IndexScope.Clear();
            configuration.IndexScope.AddRange(new[] { "configs", "scripts", "textures/sky" });
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(configuration);
            if (!plan.IsValid) Assert.Ignore("The local RVM source is unavailable.");
            XRayAssetIndex index = XRayAssetIndex.Build(plan);
            var source = new XRayEffectiveGamedata(index);
            var textureGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int frames = 0;
            foreach (XRayWeatherFxProfile profile in catalog.Effects)
            {
                Assert.IsTrue(AssetDatabase.Contains(profile), profile.EffectName);
                Assert.IsTrue(AssetDatabase.Contains(profile.SourceProfile), profile.EffectName);
                Assert.AreEqual(plan.ProfileName + "|" + plan.ModOrderFingerprint, profile.SourceIdentity, profile.EffectName);
                string path = "configs/environment/weather_effects/" + profile.EffectName + ".ltx";
                XRayEffectiveFile file = source.Resolve(path);
                Assert.IsNotNull(file, path);
                string hash;
                using (SHA256 sha256 = SHA256.Create()) hash = BitConverter.ToString(sha256.ComputeHash(source.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant();
                Assert.That(profile.Provenance, Does.Contain(path + "|" + file.SourceName + "|" + file.Location + "|sha256=" + hash), profile.EffectName);
                XRayLtxDatabase descriptors = XRayLtxDatabase.LoadFile(index, path);
                var keys = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string key in descriptors.Sections.Keys)
                {
                    if (TimeSpan.TryParseExact(key, "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out _)) keys.Add(key);
                }
                Assert.AreEqual(keys.Count, profile.KeyframeCount, profile.EffectName);
                int frameIndex = 0;
                foreach (string key in keys)
                {
                    Color? sourceSun = null;
                    if (profile.EffectName == "fx_super_blowout_night" && key == "00:07:12")
                    {
                        Assert.AreEqual("w, 0, 0", descriptors.GetValue(key, "sun_color"));
                        sourceSun = Color.black;
                    }
                    XRayWeatherCatalogTests.CheckFrame(descriptors, key, profile.GetKeyframe(frameIndex++), textureGuids, sourceSun);
                    frames++;
                }
            }
            Assert.AreEqual(112, frames);
            TestContext.WriteLine($"{catalog.Effects.Count} FX, {frames} source keys, {textureGuids.Count} unique textures.");
        }

        [TestCase(XRayWeatherFxKind.Blowout, "fx_blowout_day", "fx_blowout_night")]
        [TestCase(XRayWeatherFxKind.SuperBlowout, "fx_super_blowout_day", "fx_super_blowout_night")]
        [TestCase(XRayWeatherFxKind.PsiStorm, "fx_psi_storm_day", "fx_psi_storm_night")]
        public void VariantChoiceUsesSourceThresholdsAndWraps(XRayWeatherFxKind kind, string day, string night)
        {
            XRayWeatherFxCatalog catalog = Catalog();
            Assert.AreEqual(night, catalog.FindVariant(kind, 17999.0).EffectName);
            Assert.AreEqual(day, catalog.FindVariant(kind, 18000.0).EffectName);
            Assert.AreEqual(day, catalog.FindVariant(kind, 75599.0).EffectName);
            Assert.AreEqual(night, catalog.FindVariant(kind, 75600.0).EffectName);
            Assert.AreEqual(night, catalog.FindVariant(kind, MIDNIGHT + 17999.0).EffectName);
        }

        [TestCase("fx_blowout_day", 21, 180.0, 3000.0)]
        [TestCase("fx_blowout_night", 21, 180.0, 3000.0)]
        [TestCase("fx_super_blowout_day", 21, 180.0, 3000.0)]
        [TestCase("fx_super_blowout_night", 21, 180.0, 3000.0)]
        [TestCase("fx_psi_storm_day", 11, 240.0, 2760.0)]
        [TestCase("fx_psi_storm_night", 12, 60.0, 1080.0)]
        [TestCase("fx_surge_day_3_stancia", 5, 60.0, 2400.0)]
        public void AllEffectsPlayAuthoredAnchorsAndReturnToTheCurrentOrdinaryClock(string effectName, int count, double firstOffset, double lastOffset)
        {
            XRayWeatherFxCatalog catalog = Catalog();
            XRayWeatherFxProfile effect = FindEffect(catalog, effectName);
            XRayWeatherProfile ordinary = FindOrdinary(catalog, "w_clear1");
            var root = new GameObject("Weather FX lifecycle regression");
            try
            {
                XRayWeatherController controller = Begin(root, catalog, ordinary, MIDNIGHT - 1.0);
                Assert.AreEqual(count, effect.KeyframeCount);
                Assert.AreEqual(0.0, effect.GetKeyframe(0).GameSeconds);
                Assert.AreEqual(firstOffset, effect.GetKeyframe(1).GameSeconds);
                Assert.AreEqual(lastOffset, effect.AuthoredDurationSeconds);
                controller.SetTimeScale(2.0f);
                Assert.IsTrue(controller.StartFx(effect), controller.Diagnostic);
                Assert.AreSame(effect, controller.ActiveFx);
                Assert.AreSame(ordinary, controller.ActiveProfile);
                Assert.AreEqual(XRayWeatherFxPhase.Entry, controller.FxPhase);
                Assert.AreEqual(10.0, controller.FxEntrySeconds);
                Assert.AreEqual(lastOffset + 20.0, controller.FxDurationSeconds);
                Assert.IsTrue(ordinary.TryEvaluate(MIDNIGHT - 1.0, out XRayWeatherSnapshot start, out string diagnostic), diagnostic);
                Assert.That(Vector4.Distance(start.SunColor, controller.Snapshot.SunColor), Is.LessThan(TOLERANCE));
                controller.TickPreview(1.0f);
                Assert.AreEqual(1.0, controller.GameTimeSeconds);
                Assert.AreEqual(2.0, controller.FxTimeSeconds);
                double entry = controller.FxEntrySeconds;
                for (int i = 1; i < effect.KeyframeCount; i++)
                {
                    XRayWeatherSnapshot key = effect.GetKeyframe(i);
                    controller.SeekFx(entry + key.GameSeconds);
                    Assert.AreSame(effect, controller.ActiveFx);
                    Assert.AreEqual(key.SunDefinition, controller.Snapshot.SunDefinition, key.Key0);
                    Assert.AreEqual(string.IsNullOrEmpty(key.SunDefinition) ? null : key.SunDefinition, controller.Celestial.FlareDefinition?.Name, key.Key0);
                    Assert.That(Vector4.Distance(key.SunColor, controller.Snapshot.SunColor), Is.LessThan(TOLERANCE), key.Key0);
                    Assert.That(controller.Snapshot.RainDensity, Is.EqualTo(key.RainDensity).Within(TOLERANCE), key.Key0);
                    Assert.IsInstanceOf<Cubemap>(controller.Snapshot.SkyResources.Texture(0), key.Key0);
                    Assert.IsInstanceOf<Cubemap>(controller.Snapshot.EnvironmentResources.Texture(0), key.Key0);
                    Assert.That(Vector3.Distance(ordinary.SunDirection(controller.GameTimeSeconds), controller.Snapshot.SunDirection), Is.LessThan(TOLERANCE));
                }
                controller.SeekFx(entry + firstOffset);
                Assert.AreEqual(XRayWeatherFxPhase.Effect, controller.FxPhase);
                controller.SeekFx(entry + lastOffset + 5.0);
                Assert.AreEqual(XRayWeatherFxPhase.Return, controller.FxPhase);
                controller.TickPreview(2.5f);
                Assert.IsNull(controller.ActiveFx);
                Assert.AreEqual(XRayWeatherFxPhase.None, controller.FxPhase);
                Assert.AreSame(ordinary, controller.ActiveProfile);
                CheckOrdinarySample(controller, ordinary);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PauseZeroFactorAndWorldSeekPreserveEffectClock()
        {
            XRayWeatherFxCatalog catalog = Catalog();
            XRayWeatherFxProfile effect = FindEffect(catalog, "fx_blowout_day");
            XRayWeatherProfile ordinary = FindOrdinary(catalog, "w_clear1");
            var root = new GameObject("Weather FX clock regression");
            try
            {
                XRayWeatherController controller = Begin(root, catalog, ordinary, NOON);
                controller.SetTimeScale(0.0f);
                Assert.IsTrue(controller.StartFx(effect, 215.0), controller.Diagnostic);
                Assert.AreEqual(5.0, controller.FxEntrySeconds);
                Assert.AreEqual(215.0, controller.FxTimeSeconds);
                Assert.AreEqual(NOON, controller.GameTimeSeconds);
                controller.TickPreview(10.0f);
                Assert.AreEqual(215.0, controller.FxTimeSeconds);
                Assert.AreEqual(NOON, controller.GameTimeSeconds);
                controller.SetPaused(true);
                controller.SetTimeScale(4.0f);
                controller.TickPreview(10.0f);
                Assert.AreEqual(215.0, controller.FxTimeSeconds);
                Assert.AreEqual(NOON, controller.GameTimeSeconds);
                controller.Seek(MIDNIGHT - 2.0);
                Assert.AreEqual(215.0, controller.FxTimeSeconds);
                controller.SeekFx(218.0);
                Assert.AreEqual(1.0, controller.GameTimeSeconds);
                controller.SeekFx(214.0);
                Assert.AreEqual(MIDNIGHT - 3.0, controller.GameTimeSeconds);
                controller.SetPaused(false);
                controller.TickPreview(2.0f);
                Assert.AreEqual(222.0, controller.FxTimeSeconds);
                Assert.AreEqual(5.0, controller.GameTimeSeconds);
                Assert.AreEqual(5.0, controller.FxEntrySeconds);
                controller.StopFx();
                Assert.IsNull(controller.ActiveFx);
                CheckOrdinarySample(controller, ordinary);
                controller.Seek(NOON);
                Assert.IsTrue(controller.StartFxFromRemaining(effect, 100.0), controller.Diagnostic);
                Assert.AreEqual(controller.FxDurationSeconds - 100.0, controller.FxTimeSeconds);
                Assert.AreEqual(NOON, controller.GameTimeSeconds);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OnlySuperCanInterruptBlowoutAndTheOrdinaryDestinationSurvives()
        {
            XRayWeatherFxCatalog catalog = Catalog();
            XRayWeatherFxProfile blowout = FindEffect(catalog, "fx_blowout_day");
            XRayWeatherFxProfile super = FindEffect(catalog, "fx_super_blowout_day");
            XRayWeatherFxProfile psi = FindEffect(catalog, "fx_psi_storm_day");
            XRayWeatherProfile clear = FindOrdinary(catalog, "w_clear1");
            XRayWeatherProfile rain = FindOrdinary(catalog, "w_rain1");
            var root = new GameObject("Weather FX interruption regression");
            try
            {
                XRayWeatherController controller = Begin(root, catalog, clear, NOON);
                Assert.IsTrue(controller.TransitionTo(rain, 8.0f), controller.Diagnostic);
                controller.TickPreview(1.0f);
                Assert.IsTrue(controller.StartFx(blowout), controller.Diagnostic);
                Assert.IsFalse(controller.IsTransitioning);
                Assert.AreSame(rain, controller.ActiveProfile);
                Assert.IsFalse(controller.StartFx(psi));
                Assert.IsFalse(controller.StartFx(blowout));
                Assert.IsFalse(controller.TransitionTo(clear, 0.0f));
                controller.SetProfile(clear);
                Assert.AreSame(rain, controller.ActiveProfile);
                Assert.IsTrue(controller.StartFx(super), controller.Diagnostic);
                Assert.AreSame(super, controller.ActiveFx);
                Assert.AreSame(rain, controller.ActiveProfile);
                Assert.IsFalse(controller.StartFx(blowout));
                Assert.IsFalse(controller.StartFx(psi));
                controller.StopFx();
                Assert.IsNull(controller.ActiveFx);
                Assert.AreSame(rain, controller.ActiveProfile);
                CheckOrdinarySample(controller, rain);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MalformedSuperNightKeyKeepsNativeFogAtExactZeroLux()
        {
            XRayWeatherFxCatalog catalog = Catalog();
            XRayWeatherFxProfile effect = FindEffect(catalog, "fx_super_blowout_night");
            XRayWeatherProfile ordinary = FindOrdinary(catalog, "w_clear1");
            var root = new GameObject("Weather FX zero source regression");
            try
            {
                XRayWeatherController controller = Begin(root, catalog, ordinary, 0.0);
                Assert.IsTrue(controller.StartFx(effect), controller.Diagnostic);
                double elapsed = controller.FxEntrySeconds + 432.0;
                Light sun = root.GetComponentInChildren<Light>();
                XRayWeatherFogRenderer renderer = root.GetComponentInChildren<XRayWeatherFogRenderer>();
                Assert.IsTrue(renderer.Volume.profile.TryGet(out Fog fog));
                controller.SeekFx(elapsed - 1.0);
                Assert.Greater(sun.intensity, 0.0f);
                Assert.IsTrue(fog.enableVolumetricFog.value);
                controller.SeekFx(elapsed);
                Assert.AreEqual(Vector3.zero, controller.SourceLightColor);
                Assert.AreEqual(0.0f, sun.intensity);
                Assert.IsTrue(fog.enableVolumetricFog.value);
                controller.SetCelestialEffectOverrides(false, true);
                Assert.AreEqual(0.0f, sun.intensity);
                Assert.IsFalse(fog.enableVolumetricFog.value);
                controller.SetCelestialEffectOverrides(true, true);
                controller.SeekFx(elapsed + 1.0);
                Assert.AreEqual(0.0f, sun.intensity);
                Assert.IsTrue(fog.enableVolumetricFog.value);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static XRayWeatherFxCatalog Catalog()
        {
            XRayWeatherFxCatalog catalog = null;
            try
            {
                catalog = XRayWeatherImporter.LoadPreparedFxCatalog();
            }
            catch (InvalidDataException exception) when (exception.Message.StartsWith("Cannot resolve current weather source:", StringComparison.Ordinal))
            {
                Assert.Ignore("The local RVM source is unavailable.");
            }
            if (catalog == null) Assert.Ignore("Prepare the generated RVM Weather FX catalog in Production.");
            Assert.AreEqual(7, catalog.Effects.Count);
            Assert.AreEqual(21, catalog.OrdinaryCatalog.Profiles.Count);
            return catalog;
        }

        private static XRayWeatherController Begin(GameObject root, XRayWeatherFxCatalog catalog, XRayWeatherProfile ordinary, double time)
        {
            XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, ordinary);
            controller.ConfigureCatalog(catalog.OrdinaryCatalog);
            controller.ConfigureFxCatalog(catalog);
            Assert.IsTrue(controller.BeginPreview(time, ordinary), controller.Diagnostic);
            controller.SetTimeScale(1.0f);
            controller.SetPaused(false);
            controller.SetCelestialEffectOverrides(true, true);
            return controller;
        }

        private static void CheckOrdinarySample(XRayWeatherController controller, XRayWeatherProfile ordinary)
        {
            Assert.IsTrue(ordinary.TryEvaluate(controller.GameTimeSeconds, out XRayWeatherSnapshot expected, out string diagnostic), diagnostic);
            Assert.AreEqual(expected.Key0, controller.Snapshot.Key0);
            Assert.AreEqual(expected.Key1, controller.Snapshot.Key1);
            Assert.AreEqual(expected.SunColor, controller.Snapshot.SunColor);
            Assert.AreEqual(expected.SunDefinition, controller.Snapshot.SunDefinition);
            Assert.AreEqual(string.IsNullOrEmpty(expected.SunDefinition) ? null : expected.SunDefinition, controller.Celestial.FlareDefinition?.Name);
            Assert.AreEqual(expected.SkyResources.Texture(0), controller.Snapshot.SkyResources.Texture(0));
        }

        private static XRayWeatherFxProfile FindEffect(XRayWeatherFxCatalog catalog, string name)
        {
            foreach (XRayWeatherFxProfile effect in catalog.Effects)
            {
                if (effect.EffectName == name) return effect;
            }
            Assert.Fail("Missing weather effect " + name);
            return null;
        }

        private static XRayWeatherProfile FindOrdinary(XRayWeatherFxCatalog catalog, string name)
        {
            foreach (XRayWeatherProfile profile in catalog.OrdinaryCatalog.Profiles)
            {
                if (profile.CycleName == name) return profile;
            }
            Assert.Fail("Missing ordinary weather " + name);
            return null;
        }
    }
}
