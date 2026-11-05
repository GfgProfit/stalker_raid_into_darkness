using System;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayPrecipitationWeatherTests
    {
        private const float TOLERANCE = 0.00001f;

        [Test]
        public void AllOrdinaryKeysReachTheRainSystemWithoutChangingSharedAssets()
        {
            using (var rig = new Rig())
            {
                int checkedKeys = 0;
                foreach (XRayWeatherProfile profile in rig.Game.OrdinaryWeatherCatalog.Profiles)
                {
                    rig.Controller.SetProfile(profile);
                    for (int index = 0; index < profile.KeyframeCount; index++)
                    {
                        XRayWeatherSnapshot key = profile.GetKeyframe(index);
                        rig.Controller.Seek(key.GameSeconds);
                        float expected = Mathf.Min(1.0f, Mathf.Max(0.0f, key.RainDensity * rig.Game.Precipitation.IntensityMultiplier));
                        Assert.That(rig.Controller.Precipitation.RainDensity, Is.EqualTo(expected).Within(TOLERANCE), profile.name + "/" + key.Key0);
                        checkedKeys++;
                    }
                }
                Assert.AreEqual(600, checkedKeys);
                rig.CheckSharedAssets();
            }
        }

        [Test]
        public void AllFxPlayableKeysReachRainAndCompletionOrCancellationRestoresOrdinaryRain()
        {
            using (var rig = new Rig())
            {
                int checkedKeys = 0;
                rig.Controller.SetTimeScale(7.0f);
                foreach (XRayWeatherFxProfile effect in rig.Game.WeatherFxCatalog.Effects)
                {
                    Assert.IsTrue(rig.Controller.StartFx(effect), rig.Controller.Diagnostic);
                    double entry = rig.Controller.FxEntrySeconds;
                    for (int index = 1; index < effect.KeyframeCount; index++)
                    {
                        XRayWeatherSnapshot key = effect.GetKeyframe(index);
                        Assert.IsTrue(rig.Controller.SeekFx(entry + key.GameSeconds));
                        float expected = Mathf.Min(1.0f, Mathf.Max(0.0f, key.RainDensity * rig.Game.Precipitation.IntensityMultiplier));
                        Assert.That(rig.Controller.Precipitation.RainDensity, Is.EqualTo(expected).Within(TOLERANCE), effect.EffectName + "/" + key.Key0);
                        checkedKeys++;
                    }
                    Assert.IsTrue(rig.Controller.SeekFx(rig.Controller.FxDurationSeconds));
                    CheckOrdinaryRain(rig);
                    Assert.IsTrue(rig.Controller.StartFx(effect), rig.Controller.Diagnostic);
                    Assert.IsTrue(rig.Controller.SeekFx(rig.Controller.FxEntrySeconds + effect.GetKeyframe(1).GameSeconds));
                    Assert.IsTrue(rig.Controller.StopFx());
                    CheckOrdinaryRain(rig);
                }
                Assert.AreEqual(105, checkedKeys);
                rig.CheckSharedAssets();
            }
        }

        [Test]
        public void ClearRainStormTransitionsBlendIntensityInRealSecondsAtAStoppedWorldClock()
        {
            using (var rig = new Rig())
            {
                XRayWeatherProfile clear = Find(rig.Game.OrdinaryWeatherCatalog, "w_clear1");
                XRayWeatherProfile rain = Find(rig.Game.OrdinaryWeatherCatalog, "w_rain1");
                XRayWeatherProfile storm = Find(rig.Game.OrdinaryWeatherCatalog, "w_storm1");
                rig.Controller.SetProfile(clear);
                rig.Controller.Seek(43200.0);
                rig.Controller.SetTimeScale(0.0f);
                XRayWeatherProfile previous = clear;
                foreach (XRayWeatherProfile next in new[] { rain, storm, clear })
                {
                    Assert.IsTrue(previous.TryEvaluate(43200.0, out XRayWeatherSnapshot from, out string fromDiagnostic), fromDiagnostic);
                    Assert.IsTrue(next.TryEvaluate(43200.0, out XRayWeatherSnapshot to, out string toDiagnostic), toDiagnostic);
                    Assert.IsTrue(rig.Controller.TransitionTo(next, 4.0f), rig.Controller.Diagnostic);
                    float realBefore = rig.Controller.Precipitation.RealTime;
                    for (int step = 1; step <= 4; step++)
                    {
                        rig.Controller.TickPreview(1.0f);
                        float expected = Mathf.Min(1.0f, Mathf.Max(0.0f, (from.RainDensity + (to.RainDensity - from.RainDensity) * step * 0.25f) * rig.Game.Precipitation.IntensityMultiplier));
                        Assert.That(rig.Controller.Precipitation.RainDensity, Is.EqualTo(expected).Within(TOLERANCE));
                        Assert.AreEqual(43200.0, rig.Controller.GameTimeSeconds);
                        Assert.That(rig.Controller.Precipitation.RealTime, Is.EqualTo(realBefore + step).Within(TOLERANCE));
                    }
                    Assert.IsFalse(rig.Controller.IsTransitioning);
                    previous = next;
                }
                rig.CheckSharedAssets();
            }
        }

        private static void CheckOrdinaryRain(Rig rig)
        {
            Assert.IsNull(rig.Controller.ActiveFx);
            Assert.IsTrue(rig.Controller.ActiveProfile.TryEvaluate(rig.Controller.GameTimeSeconds, out XRayWeatherSnapshot ordinary, out string diagnostic), diagnostic);
            float expected = Mathf.Min(1.0f, Mathf.Max(0.0f, ordinary.RainDensity * rig.Game.Precipitation.IntensityMultiplier));
            Assert.That(rig.Controller.Precipitation.RainDensity, Is.EqualTo(expected).Within(TOLERANCE));
        }

        private static XRayWeatherProfile Find(XRayWeatherCatalog catalog, string cycle)
        {
            foreach (XRayWeatherProfile profile in catalog.Profiles)
            {
                if (profile.CycleName == cycle) return profile;
            }
            Assert.Fail("Missing weather cycle " + cycle);
            return null;
        }

        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly string _gameBefore;
            private readonly string _precipitationBefore;

            public GameConfig Game { get; }
            public XRayWeatherController Controller { get; }

            public Rig()
            {
                Game = GameConfig.Load();
                Assert.IsNotNull(Game);
                _gameBefore = JsonUtility.ToJson(Game);
                _precipitationBefore = JsonUtility.ToJson(Game.Precipitation);
                _scene = EditorSceneManager.NewPreviewScene();
                var root = new GameObject("Precipitation weather integration regression");
                SceneManager.MoveGameObjectToScene(root, _scene);
                Controller = XRayLevelSceneBuilder.ConfigureWeather(root, Game.DefaultWeather);
                Assert.IsTrue(Controller.BeginPreview(43200.0, Game.DefaultWeather), Controller.Diagnostic);
                Controller.SetPaused(false);
            }

            public void CheckSharedAssets()
            {
                Assert.AreEqual(_gameBefore, JsonUtility.ToJson(Game));
                Assert.AreEqual(_precipitationBefore, JsonUtility.ToJson(Game.Precipitation));
            }

            public void Dispose()
            {
                Controller.EndPreview();
                EditorSceneManager.ClosePreviewScene(_scene);
            }
        }
    }
}
