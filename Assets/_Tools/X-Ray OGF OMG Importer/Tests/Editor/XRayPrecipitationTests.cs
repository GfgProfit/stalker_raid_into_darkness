using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class XRayPrecipitationTests
    {
        private const float TOLERANCE = 0.00001f;
        private const string COMPUTE_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Runtime/Shaders/XRayPrecipitation.compute";

        [TestCase(0.0f, 2500, 0)]
        [TestCase(0.00009f, 2500, 0)]
        [TestCase(0.0001f, 2500, 25)]
        [TestCase(0.5f, 2500, 1262)]
        [TestCase(1.0f, 2500, 2500)]
        [TestCase(2.0f, 2500, 2500)]
        [TestCase(1.0f, 0, 0)]
        public void SourceDropBudgetsUseTheVerifiedDensityEquation(float density, int budget, int expected)
        {
            Assert.AreEqual(expected, XRayPrecipitationMath.DropCount(density, budget));
            Assert.AreEqual(0, XRayPrecipitationMath.DropCount(float.NaN, budget));
            Assert.AreEqual(0, XRayPrecipitationMath.DropCount(float.PositiveInfinity, budget));
        }

        [Test]
        public void VerifiedWindAnglesAndSpeedUseWorldSpaceSourceUnits()
        {
            Vector3 calm = XRayPrecipitationMath.FallDirection(0.0f, 0.0f, 0.0f, 1.0f);
            Assert.That(calm.x, Is.EqualTo(0.0f).Within(TOLERANCE));
            Assert.That(calm.y, Is.EqualTo(-1.0f).Within(TOLERANCE));
            Vector3 wind = XRayPrecipitationMath.FallDirection(Mathf.PI * 0.5f, 500.0f, 0.0f, 1.0f);
            double angle = Math.PI * 17.5 / 180.0;
            Assert.That(wind.x, Is.EqualTo(Math.Sin(angle)).Within(TOLERANCE));
            Assert.That(wind.y, Is.EqualTo(-Math.Cos(angle)).Within(TOLERANCE));
            Assert.That(wind.z, Is.EqualTo(0.0f).Within(TOLERANCE));
            Assert.That(wind.magnitude, Is.EqualTo(1.0f).Within(TOLERANCE));
            Assert.AreEqual(20.0f, XRayPrecipitationMath.SpeedFactor(0.0f, 40.0f));
            Assert.AreEqual(45.0f, XRayPrecipitationMath.SpeedFactor(500.0f, 60.0f));
            Assert.AreEqual(80.0f, XRayPrecipitationMath.SpeedFactor(1000.0f, 80.0f));
        }

        [TestCase(5.0f, false)]
        [TestCase(35.0f, true)]
        public void ActualGpuRainRespectsKnownRoofHeightAndCreatesOnlySurfaceSplashes(float roofHeight, bool ceilingAboveSpawn)
        {
            ComputeShader compute = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(COMPUTE_PATH));
            var heights = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            heights.SetPixels(new[] { new Color(roofHeight, 0.0f, 0.0f), new Color(roofHeight, 0.0f, 0.0f), new Color(roofHeight, 0.0f, 0.0f), new Color(roofHeight, 0.0f, 0.0f) });
            heights.Apply(false, false);
            var buffer = new ComputeBuffer(2500, 64, ComputeBufferType.Structured);
            try
            {
                int initialize = compute.FindKernel("InitializeDrops");
                int simulate = compute.FindKernel("SimulateDrops");
                ConfigureGpu(compute, initialize, buffer, heights, new Vector3(0.0f, -1.0f, 0.0f), 0.3f);
                compute.Dispatch(initialize, 40, 1, 1);
                ConfigureGpu(compute, simulate, buffer, heights, new Vector3(0.0f, -1.0f, 0.0f), 0.3f);
                compute.SetFloat("_XRayRainDelta", 0.05f);
                int visible = 0;
                int splashes = 0;
                var drops = new XRayRainDropState[2500];
                for (int step = 0; step < 40; step++)
                {
                    compute.Dispatch(simulate, 40, 1, 1);
                    buffer.GetData(drops);
                    foreach (XRayRainDropState drop in drops)
                    {
                        if (drop.Size > 0.0f)
                        {
                            visible++;
                            Assert.That(drop.Position.y, Is.GreaterThanOrEqualTo(roofHeight - 0.0001f));
                        }
                        if (drop.SplashAge >= 0.3f) continue;
                        splashes++;
                        Assert.That(drop.SplashPosition.y, Is.EqualTo(roofHeight + 0.015f).Within(TOLERANCE));
                    }
                }
                Assert.AreEqual(ceilingAboveSpawn, visible == 0);
                Assert.AreEqual(ceilingAboveSpawn, splashes == 0);
            }
            finally
            {
                buffer.Dispose();
                Object.DestroyImmediate(heights);
                Object.DestroyImmediate(compute);
            }
        }

        [Test]
        public void ActualGpuDropVelocityUsesIndependentSourceSpeedWindAndPhysicalDelta()
        {
            ComputeShader compute = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(COMPUTE_PATH));
            var heights = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            heights.SetPixels(new[] { new Color(-1000.0f, -1000.0f, 0.0f), new Color(-1000.0f, -1000.0f, 0.0f), new Color(-1000.0f, -1000.0f, 0.0f), new Color(-1000.0f, -1000.0f, 0.0f) });
            heights.Apply(false, false);
            var buffer = new ComputeBuffer(2500, 64, ComputeBufferType.Structured);
            try
            {
                int initialize = compute.FindKernel("InitializeDrops");
                int simulate = compute.FindKernel("SimulateDrops");
                double angle = Math.PI * 17.5 / 180.0;
                Vector3 direction = new Vector3((float)Math.Sin(angle), -(float)Math.Cos(angle), 0.0f);
                ConfigureGpu(compute, initialize, buffer, heights, direction, 0.45f);
                compute.Dispatch(initialize, 40, 1, 1);
                var first = new XRayRainDropState[2500];
                var frozen = new XRayRainDropState[2500];
                var next = new XRayRainDropState[2500];
                buffer.GetData(first);
                ConfigureGpu(compute, simulate, buffer, heights, direction, 0.45f);
                compute.SetFloat("_XRayRainDelta", 0.0f);
                compute.Dispatch(simulate, 40, 1, 1);
                buffer.GetData(frozen);
                Vector3 mean = Vector3.zero;
                foreach (XRayRainDropState drop in first)
                {
                    Assert.That(drop.Velocity.magnitude, Is.InRange(17.9999f, 36.0001f));
                    mean += drop.Velocity.normalized;
                }
                Assert.That(Vector3.Angle(mean.normalized, direction), Is.LessThan(1.0f));
                compute.SetFloat("_XRayRainDelta", 0.01f);
                compute.Dispatch(simulate, 40, 1, 1);
                buffer.GetData(next);
                int unchangedTrajectory = 0;
                for (int index = 0; index < first.Length; index++)
                {
                    Assert.AreEqual(first[index].Position, frozen[index].Position);
                    Assert.AreEqual(first[index].Age, frozen[index].Age);
                    if (next[index].Seed != first[index].Seed || next[index].Age < first[index].Age) continue;
                    unchangedTrajectory++;
                    Assert.That(Vector3.Distance(first[index].Position + first[index].Velocity * 0.01f, next[index].Position), Is.LessThan(0.00001f));
                }
                Assert.That(unchangedTrajectory, Is.GreaterThan(2000));
            }
            finally
            {
                buffer.Dispose();
                Object.DestroyImmediate(heights);
                Object.DestroyImmediate(compute);
            }
        }

        private static void ConfigureGpu(ComputeShader compute, int kernel, ComputeBuffer buffer, Texture heights, Vector3 direction, float speed)
        {
            compute.SetBuffer(kernel, "_XRayRainDrops", buffer);
            compute.SetTexture(kernel, "_XRayRainHeights", heights);
            compute.SetVector("_XRayRainWorldMapping", new Vector4(-100.0f, -100.0f, 0.005f, 0.005f));
            compute.SetVector("_XRayRainSource", new Vector4(0.0f, 0.0f, 0.0f, 15.0f));
            compute.SetVector("_XRayRainForward", new Vector4(0.0f, 0.0f, 1.0f, 0.5f));
            compute.SetVector("_XRayRainMotion", new Vector4(direction.x, direction.y, direction.z, speed));
            compute.SetInt("_XRayRainDropCount", 2500);
            compute.SetInt("_XRayRainActiveCount", 2500);
            compute.SetInt("_XRayRainSplashBudget", 1000);
            compute.SetFloat("_XRayRainDelta", 0.0f);
        }

        [Test]
        public void AllOrdinaryAndFxFramesProvideFinitePrecipitationWithoutNameInference()
        {
            GameConfig game = GameConfig.Load();
            Assert.IsNotNull(game);
            int ordinaryKeys = 0;
            int ordinaryRain = 0;
            int fxKeys = 0;
            int fxRain = 0;
            Assert.AreEqual(21, game.OrdinaryWeatherCatalog.Profiles.Count);
            Assert.AreEqual(7, game.WeatherFxCatalog.Effects.Count);
            foreach (XRayWeatherProfile profile in game.OrdinaryWeatherCatalog.Profiles)
            {
                for (int index = 0; index < profile.KeyframeCount; index++)
                {
                    XRayWeatherSnapshot frame = profile.GetKeyframe(index);
                    CheckFrame(frame);
                    ordinaryKeys++;
                    if (frame.RainDensity > 0.0f) ordinaryRain++;
                }
            }
            foreach (XRayWeatherFxProfile profile in game.WeatherFxCatalog.Effects)
            {
                for (int index = 0; index < profile.KeyframeCount; index++)
                {
                    XRayWeatherSnapshot frame = profile.GetKeyframe(index);
                    CheckFrame(frame);
                    fxKeys++;
                    if (frame.RainDensity > 0.0f) fxRain++;
                    if (profile.EffectName == "fx_psi_storm_night" || profile.EffectName == "fx_surge_day_3_stancia") Assert.AreEqual(0.0f, frame.RainDensity);
                    if (profile.EffectName == "fx_super_blowout_day" && frame.Key0 == "00:46:00") Assert.AreEqual(1.0f, frame.RainDensity);
                }
            }
            Assert.AreEqual(600, ordinaryKeys);
            Assert.AreEqual(206, ordinaryRain);
            Assert.AreEqual(112, fxKeys);
            Assert.AreEqual(35, fxRain);
        }

        [Test]
        public void ConfigurationQualityAndExplicitLevelInheritanceStayReadOnly()
        {
            GameConfig game = GameConfig.Load();
            Assert.IsNotNull(game);
            PrecipitationConfig canonical = game.Precipitation;
            Assert.IsNotNull(canonical);
            Assert.IsTrue(canonical.TryValidate(out string diagnostic), diagnostic);
            string rootBefore = JsonUtility.ToJson(game);
            string configBefore = JsonUtility.ToJson(canonical);
            PrecipitationConfig local = Object.Instantiate(canonical);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var settings = new SerializedObject(local);
                settings.FindProperty("_maxDrops").intValue = 2500;
                settings.FindProperty("_quality").enumValueIndex = 0;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(625, local.DropBudget);
                Assert.AreEqual(256, local.AtlasResolution);
                settings.FindProperty("_quality").enumValueIndex = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(1250, local.DropBudget);
                Assert.AreEqual(512, local.AtlasResolution);
                settings.FindProperty("_quality").enumValueIndex = 2;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(2500, local.DropBudget);
                Assert.AreEqual(1024, local.AtlasResolution);
                var firstRoot = new GameObject("First precipitation config consumer");
                var secondRoot = new GameObject("Second precipitation config consumer");
                SceneManager.MoveGameObjectToScene(firstRoot, scene);
                SceneManager.MoveGameObjectToScene(secondRoot, scene);
                XRayWeatherController first = firstRoot.AddComponent<XRayWeatherController>();
                XRayWeatherController second = secondRoot.AddComponent<XRayWeatherController>();
                Assert.AreSame(canonical, first.EffectivePrecipitationConfig);
                Assert.AreSame(canonical, second.EffectivePrecipitationConfig);
                var level = new SerializedObject(first);
                level.FindProperty("_precipitationConfig").objectReferenceValue = local;
                level.FindProperty("_levelOverrides").intValue |= (int)XRayWeatherLevelOverrides.Precipitation;
                level.ApplyModifiedPropertiesWithoutUndo();
                first.ApplySettings();
                Assert.AreSame(local, first.EffectivePrecipitationConfig);
                Assert.AreSame(canonical, second.EffectivePrecipitationConfig);
                level.FindProperty("_levelOverrides").intValue &= ~(int)XRayWeatherLevelOverrides.Precipitation;
                level.ApplyModifiedPropertiesWithoutUndo();
                first.ApplySettings();
                Assert.AreSame(canonical, first.EffectivePrecipitationConfig);
                Assert.AreEqual(rootBefore, JsonUtility.ToJson(game));
                Assert.AreEqual(configBefore, JsonUtility.ToJson(canonical));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(local);
            }
        }

        [TestCase(0.0f)]
        [TestCase(1.0f)]
        [TestCase(7.0f)]
        public void PhysicalRainUsesRealSecondsWhileWeatherUsesTheWorldClock(float scale)
        {
            GameConfig game = GameConfig.Load();
            XRayWeatherProfile profile = FindRain(game.OrdinaryWeatherCatalog, out double time);
            Assert.IsNotNull(profile);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Precipitation clock regression");
                SceneManager.MoveGameObjectToScene(root, scene);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                Assert.IsTrue(controller.BeginPreview(time, profile), controller.Diagnostic);
                controller.SetPaused(false);
                controller.SetTimeScale(scale);
                Assert.IsNotNull(controller.Precipitation);
                double worldBefore = controller.GameTimeSeconds;
                double realBefore = controller.Precipitation.RealTime;
                controller.TickPreview(1.0f);
                Assert.That(controller.GameTimeSeconds, Is.EqualTo((worldBefore + scale) % 86400.0).Within(TOLERANCE));
                Assert.That(controller.Precipitation.RealTime, Is.EqualTo(realBefore + 1.0).Within(TOLERANCE));
                Assert.That(controller.Precipitation.RainDensity, Is.EqualTo(Mathf.Clamp01(controller.Snapshot.RainDensity * game.Precipitation.IntensityMultiplier)).Within(TOLERANCE));
                controller.SetPaused(true);
                worldBefore = controller.GameTimeSeconds;
                realBefore = controller.Precipitation.RealTime;
                controller.TickPreview(2.0f);
                Assert.AreEqual(worldBefore, controller.GameTimeSeconds);
                Assert.AreEqual(realBefore, controller.Precipitation.RealTime);
                controller.Seek(86399.0);
                Assert.AreEqual(realBefore, controller.Precipitation.RealTime);
                controller.SetPaused(false);
                controller.TickPreview(1.0f);
                Assert.That(controller.GameTimeSeconds, Is.EqualTo((86399.0 + scale) % 86400.0).Within(TOLERANCE));
                Assert.That(controller.Precipitation.RealTime, Is.EqualTo(realBefore + 1.0).Within(TOLERANCE));
                controller.EndPreview();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void SessionPrecipitationOverrideCanEnableDisabledDefaultsAndClearsAtLifecycleBoundaries()
        {
            GameConfig game = GameConfig.Load();
            PrecipitationConfig local = Object.Instantiate(game.Precipitation);
            var config = new SerializedObject(local);
            config.FindProperty("_enabled").boolValue = false;
            config.ApplyModifiedPropertiesWithoutUndo();
            string before = JsonUtility.ToJson(local);
            string canonicalBefore = JsonUtility.ToJson(game.Precipitation);
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Precipitation override lifetime regression");
                SceneManager.MoveGameObjectToScene(root, scene);
                XRayWeatherProfile profile = FindRain(game.OrdinaryWeatherCatalog, out double seconds);
                XRayWeatherController controller = XRayLevelSceneBuilder.ConfigureWeather(root, profile);
                var level = new SerializedObject(controller);
                level.FindProperty("_precipitationConfig").objectReferenceValue = local;
                level.FindProperty("_levelOverrides").intValue |= (int)XRayWeatherLevelOverrides.Precipitation;
                level.ApplyModifiedPropertiesWithoutUndo();
                controller.ApplySettings();
                Assert.IsTrue(controller.BeginPreview(seconds, profile), controller.Diagnostic);
                Assert.IsFalse(controller.PrecipitationEnabled);
                Assert.IsFalse(controller.Precipitation.Enabled);
                controller.SetPrecipitationOverride(true);
                Assert.IsTrue(controller.PrecipitationEnabled);
                Assert.IsTrue(controller.Precipitation.Enabled);
                Assert.IsFalse(local.Enabled);
                controller.SetPrecipitationOverride(null);
                Assert.IsFalse(controller.Precipitation.Enabled);
                controller.SetPrecipitationOverride(false);
                Assert.IsFalse(controller.Precipitation.Enabled);
                controller.SetPrecipitationOverride(true);
                controller.SetRainVisualOverride(false);
                controller.EndPreview();
                Assert.IsFalse(controller.PrecipitationOverride.HasValue);
                Assert.IsFalse(controller.RainVisualOverride.HasValue);
                Assert.IsFalse(controller.Precipitation.Enabled);
                Assert.IsTrue(controller.BeginPreview(seconds, profile), controller.Diagnostic);
                Assert.IsFalse(controller.Precipitation.Enabled);
                controller.SetPrecipitationOverride(true);
                controller.SetRainVisualOverride(false);
                controller.enabled = false;
                Assert.IsFalse(controller.PrecipitationOverride.HasValue);
                Assert.IsFalse(controller.RainVisualOverride.HasValue);
                Assert.IsFalse(controller.Precipitation.Enabled);
                Assert.AreEqual(before, JsonUtility.ToJson(local));
                Assert.AreEqual(canonicalBefore, JsonUtility.ToJson(game.Precipitation));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(local);
            }
        }

        [Test]
        public void HeightAtlasTracksRendererVisibilityAndMeshReplacementWithoutHierarchyChanges()
        {
            PrecipitationConfig config = Object.Instantiate(GameConfig.Load().Precipitation);
            var serialized = new SerializedObject(config);
            serialized.FindProperty("_quality").enumValueIndex = 0;
            serialized.FindProperty("_geometryRefreshSeconds").floatValue = 0.1f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Scene scene = EditorSceneManager.NewPreviewScene();
            var geometry = new XRayPrecipitationGeometry(scene);
            Material material = new Material(Shader.Find("HDRP/MonolithLit"));
            var quad = new Mesh();
            try
            {
                quad.vertices = new[] { new Vector3(-0.5f, 0.0f, -0.5f), new Vector3(0.5f, 0.0f, -0.5f), new Vector3(-0.5f, 0.0f, 0.5f), new Vector3(0.5f, 0.0f, 0.5f) };
                quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                quad.RecalculateBounds();
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.transform.localScale = new Vector3(5.0f, 1.0f, 5.0f);
                MeshRenderer renderer = floor.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                floor.SetActive(false);
                geometry.Refresh(config, 0.0f, true);
                Assert.Less(HeightCenter(geometry.Heights).r, HeightCenter(geometry.Heights).g);
                int captures = geometry.CaptureCount;
                floor.SetActive(true);
                geometry.Refresh(config, 0.2f);
                Assert.Greater(geometry.CaptureCount, captures);
                Color cube = HeightCenter(geometry.Heights);
                Assert.That(cube.r, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(cube.g, Is.EqualTo(-0.5f).Within(0.0001f));
                captures = geometry.CaptureCount;
                renderer.enabled = false;
                geometry.Refresh(config, 0.2f);
                Assert.Greater(geometry.CaptureCount, captures);
                Assert.Less(HeightCenter(geometry.Heights).r, HeightCenter(geometry.Heights).g);
                captures = geometry.CaptureCount;
                renderer.enabled = true;
                geometry.Refresh(config, 0.2f);
                Assert.Greater(geometry.CaptureCount, captures);
                Assert.That(HeightCenter(geometry.Heights).r, Is.EqualTo(0.5f).Within(0.0001f));
                captures = geometry.CaptureCount;
                floor.GetComponent<MeshFilter>().sharedMesh = quad;
                geometry.Refresh(config, 0.2f);
                Assert.Greater(geometry.CaptureCount, captures);
                Color plane = HeightCenter(geometry.Heights);
                Assert.That(plane.r, Is.EqualTo(0.0f).Within(0.0001f));
                Assert.That(plane.g, Is.EqualTo(0.0f).Within(0.0001f));
                captures = geometry.CaptureCount;
                floor.SetActive(false);
                geometry.Refresh(config, 0.2f);
                floor.SetActive(true);
                geometry.Refresh(config, 0.2f);
                Assert.Greater(geometry.CaptureCount, captures);
                Assert.That(HeightCenter(geometry.Heights).r, Is.EqualTo(0.0f).Within(0.0001f));
            }
            finally
            {
                geometry.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(quad);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(config);
            }
        }

        private static Color HeightCenter(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            var texture = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(target.width / 2, target.height / 2, 1, 1), 0, 0);
                texture.Apply(false, false);
                return texture.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
            }
        }

        private static void CheckFrame(XRayWeatherSnapshot frame)
        {
            Assert.IsTrue(float.IsFinite(frame.RainDensity));
            Assert.That(frame.RainDensity, Is.InRange(0.0f, 1.0f));
            Assert.IsTrue(float.IsFinite(frame.RainColor.r) && float.IsFinite(frame.RainColor.g) && float.IsFinite(frame.RainColor.b));
            Assert.IsTrue(float.IsFinite(frame.WindVelocity) && float.IsFinite(frame.WindDirectionRadians) && float.IsFinite(frame.WaterIntensity));
        }

        private static XRayWeatherProfile FindRain(XRayWeatherCatalog catalog, out double time)
        {
            foreach (XRayWeatherProfile profile in catalog.Profiles)
            {
                for (int index = 0; index < profile.KeyframeCount; index++)
                {
                    XRayWeatherSnapshot frame = profile.GetKeyframe(index);
                    if (frame.RainDensity <= 0.0f) continue;
                    time = frame.GameSeconds;
                    return profile;
                }
            }
            time = 0.0;
            return null;
        }
    }
}
