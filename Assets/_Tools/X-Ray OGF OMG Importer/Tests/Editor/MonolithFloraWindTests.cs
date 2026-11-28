using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class MonolithFloraWindTests
    {
        private const string ANOMALY_ROOT = "Assets/_Source/Levels/Anomaly/1v2_DX11_v_12gb_2.5_168_662acc8d";
        private const int GRID = 5;
        private const int SIZE = 160;
        private const float QUANT = 2048.0f;
        private const float BACKGROUND = 1000.0f;
        private const double POSITION_TOLERANCE = 0.0005;
        private const double MOTION_TOLERANCE = 0.01 / SIZE * 0.5;

        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly List<MonolithFloraWindRig> _fixtureRigs = new List<MonolithFloraWindRig>();
        private readonly List<List<UnityEngine.Object>> _fixtureObjects = new List<List<UnityEngine.Object>>();
        private readonly List<Scene> _fixtureScenes = new List<Scene>();
        private readonly List<Action<ScriptableRenderContext, Camera>> _fixtureSubmissions = new List<Action<ScriptableRenderContext, Camera>>();
        private int _readbacks;
        private float _captureDeltaTime;
        private bool _captureTimeOwned;

        [TearDown]
        public void TearDown()
        {
            RestoreCaptureTime();
            foreach (Action<ScriptableRenderContext, Camera> submission in _fixtureSubmissions) RenderPipelineManager.beginCameraRendering -= submission;
            _fixtureSubmissions.Clear();
            foreach (MonolithFloraWindRig rig in _fixtureRigs) rig.Release();
            _fixtureRigs.Clear();
            foreach (List<UnityEngine.Object> objects in _fixtureObjects)
            {
                for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            }
            _fixtureObjects.Clear();
            foreach (Scene scene in _fixtureScenes) if (scene.IsValid() && scene.isLoaded && EditorSceneManager.IsPreviewScene(scene)) EditorSceneManager.ClosePreviewScene(scene);
            _fixtureScenes.Clear();
            for (int i = _owned.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
            XRayFloraWindState.Clear();
        }

        [Test]
        public void WindStateFollowsEngineEnvironmentAndTreeVisual()
        {
            var state = new XRayFloraWindState { Profile = XRayFloraWindProfile.AnomalySsfx, TreesSetup = new Vector4(11.0f, 0.15f, 0.5f, 0.15f) };
            var weather = new XRayFloraWindWeather(30.0f * Mathf.Deg2Rad, 9.0f, 0.004f);
            state.Reset(weather);
            Assert.AreEqual(state.Anim, state.AnimPrev, "First frame must not create a velocity spike.");
            Assert.AreEqual(Vector4.zero, state.Anim);
            double[] expected = new double[4];
            double[] saved = new double[4];
            float[] deltas = { 0.016f, 0.033f, 0.0f, 0.0f, 0.1f, 0.02f, 0.016f, 0.05f };
            float[] directions = { 30.0f, 30.0f, 30.0f, 200.0f, 200.0f, -45.0f, 90.0f, 90.0f };
            float[] velocities = { 9.0f, 9.0f, 9.0f, 1500.0f, 1500.0f, 0.0f, 400.0f, 4000.0f };
            for (int step = 0; step < deltas.Length; step++)
            {
                weather = new XRayFloraWindWeather(directions[step] * Mathf.Deg2Rad, velocities[step], 0.004f);
                Array.Copy(expected, saved, 4);
                float velocity = Mathf.Max(Mathf.Max(velocities[step], 0.15f * 1000.0f), 200.0f) * 0.001f;
                float direction = -(directions[step] * Mathf.Deg2Rad) + 1.5707964f;
                expected[0] += velocity * Mathf.Cos(direction) * deltas[step];
                expected[1] += velocity * Mathf.Sin(direction) * deltas[step];
                expected[2] += Mathf.Clamp(velocity * 1.33f, 0.0f, 1.0f) * deltas[step];
                expected[3] += 1.0f * deltas[step];
                state.Advance(deltas[step], weather);
                double error = 0;
                for (int c = 0; c < 4; c++)
                {
                    error = Math.Max(error, Math.Abs(state.Anim[c] - expected[c]));
                    Assert.AreEqual(saved[c], state.AnimPrev[c], 1e-5, "wind_anim_prev must be the value of the previous rendered frame (device.cpp: prev = saved; saved = current) at step " + step);
                }
                Assert.LessOrEqual(error, 1e-5, "step " + step);
                if (deltas[step] == 0.0f) Assert.AreEqual(state.AnimPrev, state.Anim, "Paused frames keep a stable animation state.");
            }

            state.Reinitialize();
            Assert.AreEqual(state.Anim, state.AnimPrev, "Teleport or weather reinitialization must not create a motion spike.");
            Assert.AreEqual(0.0f, XRayFloraWindState.EngineDelta(0.016f, true));
            Assert.AreEqual(0.1f, XRayFloraWindState.EngineDelta(0.5f, false));

            var trees = new XRayFloraWindState { Profile = XRayFloraWindProfile.TrueStalkerXRay, TreeRotationPeriod = 10.0f, TreeWaveSpeed = 1.0f, TreeAmplitude = 0.005f, TreeWaveScale = new Vector3(0.1f, 0.01f, 0.11f) };
            trees.Reset(weather);
            double time = 0;
            Vector4 previousWind = trees.TreeWind;
            Vector4 previousWave = trees.TreeWave;
            foreach (float delta in new[] { 0.016f, 0.0f, 0.033f, 0.1f })
            {
                trees.Advance(delta, weather);
                time += delta;
                double rotation = Math.PI * 2.0 * time / 10.0;
                Assert.AreEqual(Math.Sin(rotation) * 0.005, trees.TreeWind.x, 1e-6);
                Assert.AreEqual(Math.Cos(rotation) * 0.005, trees.TreeWind.z, 1e-6);
                Assert.AreEqual(0.1 / (Math.PI * 2.0), trees.TreeWave.x, 1e-7);
                Assert.AreEqual(time * 1.0 / (Math.PI * 2.0), trees.TreeWave.w, 1e-6);
                Assert.AreEqual(previousWind, trees.TreeWindPrev);
                Assert.AreEqual(previousWave, trees.TreeWavePrev);
                previousWind = trees.TreeWind;
                previousWave = trees.TreeWave;
            }

        }

        [Test]
        public void ProviderKeepsUnverifiedTrueStalkerConstantsDisabled()
        {
            var host = new GameObject("Wind provider");
            _owned.Add(host);
            host.SetActive(false);
            XRayFloraWindProvider provider = host.AddComponent<XRayFloraWindProvider>();
            provider.Profile = XRayFloraWindProfile.TrueStalkerXRay;
            host.SetActive(true);
            Assert.AreEqual(XRayFloraWindProfile.Disabled, provider.State.Profile);
            Assert.AreEqual(0.0f, Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY));
            StringAssert.Contains("not confirmed", provider.Diagnostic);
            provider.TrueStalkerDiagnosticConstants = true;
            provider.ResetAnimation();
            Assert.AreEqual(XRayFloraWindProfile.TrueStalkerXRay, provider.State.Profile);
            Assert.AreEqual(2.0f, Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY));
            provider.Profile = XRayFloraWindProfile.AnomalySsfx;
            provider.ResetAnimation();
            Assert.AreEqual(1.0f, Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY));
            host.SetActive(false);
            Assert.AreEqual(0.0f, Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY));
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator MonolithLitAppliesSourceWindInEveryPass()
        {
            HdrpTestWarmup.Ensure();
            Texture2D waves = Own(Waves(false));
            Texture2D flipped = Own(Waves(true));
            string template = File.ReadAllText(MonolithFloraReferenceSource.TEMPLATE);
            var references = new Dictionary<string, Material>();
            foreach ((string key, int group) in new[] { ("Anomaly_Branch", 1), ("Anomaly_Trunk", 2), ("TrueStalker_Branch", 6), ("TrueStalker_Trunk", 7) })
            {
                string source = MonolithFloraReferenceSource.Build(group, template, out _);
                references[key] = Reference(source, key);
            }
            foreach ((string key, bool trueStalker, bool aref) in new[] { ("Anomaly_Branch_Shadow", false, true), ("Anomaly_Trunk_Shadow", false, false), ("TrueStalker_Branch_Shadow", true, true), ("TrueStalker_Trunk_Shadow", true, false) })
            {
                string source = MonolithFloraReferenceSource.BuildShadow(trueStalker, aref, template, out _);
                references[key] = Reference(source, key);
            }

            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            MonolithFloraWindRig rig = null;
            try
            {
                rig = CreateRig(scene, temporary);
                var cases = new[]
                {
                    ("Identity", Matrix4x4.TRS(new Vector3(0.0f, 0.0f, 0.0f), Quaternion.identity, Vector3.one), false),
                    ("RotatedScaled", Matrix4x4.TRS(new Vector3(37.0f, 4.5f, -61.0f), Quaternion.Euler(0.0f, 33.0f, 0.0f), new Vector3(1.3f, 1.3f, 1.3f)), false),
                    ("FlippedWaves", Matrix4x4.TRS(new Vector3(-120.0f, 1.0f, 75.0f), Quaternion.Euler(0.0f, -20.0f, 0.0f), new Vector3(0.8f, 1.1f, 0.8f)), true),
                    ("Mirrored", Matrix4x4.TRS(new Vector3(12.0f, 0.0f, 9.0f), Quaternion.Euler(0.0f, 70.0f, 0.0f), new Vector3(-1.0f, 1.0f, 1.0f)), false)
                };
                foreach (XRayFloraWindProfile profile in new[] { XRayFloraWindProfile.AnomalySsfx, XRayFloraWindProfile.TrueStalkerXRay })
                {
                    foreach (bool trunk in new[] { false, true })
                    {
                        string part = trunk ? "Trunk" : "Branch";
                        string prefix = (profile == XRayFloraWindProfile.AnomalySsfx ? "Anomaly_" : "TrueStalker_") + part;
                        foreach ((string name, Matrix4x4 matrix, bool flip) in cases)
                        {
                            if (flip && profile != XRayFloraWindProfile.AnomalySsfx) continue;
                            int seed = 101 * (int)profile + 13 * (trunk ? 1 : 0) + name.Length;
                            XRayFloraWindState state = State(profile, seed);
                            Mesh mesh = Own(Grid(true));
                            Vector3[] objectPositions = mesh.vertices;
                            Vector2[] uv0 = Uvs(mesh, 0);
                            Vector2[] uv3 = Uvs(mesh, 3);
                            Vector3[] current = Evaluate(references[prefix], objectPositions, uv0, uv3, matrix, state, false, waves);
                            Vector3[] previous = Evaluate(references[prefix], objectPositions, uv0, uv3, matrix, state, true, waves);
                            Vector3[] shadow = Evaluate(references[prefix + "_Shadow"], objectPositions, uv0, uv3, matrix, state, false, waves);
                            float maxDisplacement = 0;
                            for (int v = 0; v < objectPositions.Length; v++)
                            {
                                Vector3 rest = matrix.MultiplyPoint3x4(objectPositions[v]);
                                maxDisplacement = Mathf.Max(maxDisplacement, new Vector2(current[v].x - rest.x, current[v].z - rest.z).magnitude, new Vector2(previous[v].x - rest.x, previous[v].z - rest.z).magnitude);
                            }
                            Bounds rawBounds = GeometryUtility.CalculateBounds(objectPositions, matrix);
                            float margin = state.MaxHorizontalDisplacement(rawBounds.size.y, 1.0f);
                            Assert.LessOrEqual(maxDisplacement, margin + 1e-4f, prefix + " " + name + ": bounds margin must cover the source displacement.");
                            Material wind = Own(WindMaterial((float)profile, trunk ? 1.0f : 0.0f, flip ? flipped : waves, flip));
                            Material baked = Own(WindMaterial(0.0f, trunk ? 1.0f : 0.0f, flip ? flipped : waves, flip));
                            Mesh bakedCurrent = Own(Baked(mesh, current, matrix));
                            Mesh bakedShadow = Own(Baked(mesh, shadow, matrix));
                            state.Apply();
                            string label = prefix + "_" + name;
                            rig.Frame(GeometryUtility.CalculateBounds(current, Matrix4x4.identity));

                            rig.Show(mesh, wind, matrix, ShadowCastingMode.Off);
                            Color[] uvPixels = rig.Debug(35);
                            Color[] positions = rig.Debug(34);
                            float[] depthWind = rig.Depth();
                            rig.Show(bakedCurrent, baked, matrix, ShadowCastingMode.Off);
                            Color[] bakedPositions = rig.Debug(34);
                            float[] depthBaked = rig.Depth();
                            ComparePositions(positions, uvPixels, current, rig.PixelSize, label);
                            CompareImages(positions, bakedPositions, POSITION_TOLERANCE, label + " forward vs baked");
                            CompareDepth(depthWind, depthBaked, label);
                            XRayFloraWindState previousState = State(profile, seed, false);
                            Assert.AreEqual(state.AnimPrev, previousState.Anim);
                            Assert.AreEqual(state.TreeWindPrev, previousState.TreeWind);
                            previousState.Apply();
                            rig.Show(mesh, wind, matrix, ShadowCastingMode.Off);
                            Color[] previousPositions = rig.Debug(34);
                            rig.Show(Own(Baked(mesh, previous, matrix)), baked, matrix, ShadowCastingMode.Off);
                            CompareImages(previousPositions, rig.Debug(34), POSITION_TOLERANCE, label + " previous state vs baked previous");
                            state.Apply();

                            rig.Receiver(true);
                            rig.Show(mesh, wind, matrix, ShadowCastingMode.ShadowsOnly);
                            Color[] shadowWind = rig.Debug(0);
                            rig.Show(bakedShadow, baked, matrix, ShadowCastingMode.ShadowsOnly);
                            Color[] shadowBaked = rig.Debug(0);
                            rig.Receiver(false);
                            (int pixels, int differing, double error) shadowParity = CompareShadow(shadowWind, shadowBaked);
                            Assert.LessOrEqual(shadowParity.differing, Math.Max(2, shadowParity.pixels / 2000), label + ": ShadowCaster must use the source shadow deformation.");

                            rig.Show(mesh, wind, matrix, ShadowCastingMode.Off);
                            var failures = new List<string>();
                            foreach (string scenario in new[] { "Wind", "Paused", "Reset", "MovingCamera" })
                            {
                                XRayFloraWindState frame = State(profile, seed);
                                Vector3[] frameCurrent = current;
                                Vector3[] framePrevious = previous;
                                if (scenario == "Paused")
                                {
                                    frame.Advance(0.0f, frame.Weather);
                                    frameCurrent = Evaluate(references[prefix], objectPositions, uv0, uv3, matrix, frame, false, waves);
                                    framePrevious = frameCurrent;
                                }
                                if (scenario == "Reset")
                                {
                                    frame.Reinitialize();
                                    framePrevious = frameCurrent;
                                }
                                frame.Apply();
                                Vector3 cameraShift = scenario == "MovingCamera" ? new Vector3(0.21f, -0.13f, 0.0f) : Vector3.zero;
                                (Matrix4x4 previousViewProjection, Matrix4x4 viewProjection, Color[] vectors, Color[] uvMotion) = rig.Motion(cameraShift);
                                (int pixels, double error, double maximum) result = CompareMotion(vectors, uvMotion, frameCurrent, framePrevious, viewProjection, previousViewProjection, label + " " + scenario);
                                failures.Add(_motionFailure);
                                if (scenario == "Wind" && maxDisplacement > 0.001f && result.maximum <= 0.0) failures.Add(label + ": wind motion vectors must not be masked with zero.");
                            }
                            failures.RemoveAll(item => item == null);
                            Assert.IsEmpty(failures, string.Join("\n", failures));
                            rig.Hide();
                            yield return null;
                        }
                    }
                }
            }
            finally
            {
                rig?.Release();
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator DisabledWindKeepsLegacyPixels()
        {
            HdrpTestWarmup.Ensure();
            Texture2D waves = Own(Waves(false));
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            MonolithFloraWindRig rig = null;
            try
            {
                rig = CreateRig(scene, temporary);
                Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(5.0f, 0.0f, 3.0f), Quaternion.Euler(0.0f, 25.0f, 0.0f), Vector3.one);
                Mesh legacyMesh = Own(Grid(false));
                Mesh windMesh = Own(Grid(true));
                Material legacy = Own(WindMaterial(0.0f, 0.0f, null, false));
                rig.Frame(GeometryUtility.CalculateBounds(legacyMesh.vertices, matrix));
                XRayFloraWindState.Clear();
                rig.Show(legacyMesh, legacy, matrix, ShadowCastingMode.On);
                Color[] baseline = rig.Debug(34);
                XRayFloraWindState state = State(XRayFloraWindProfile.AnomalySsfx, 3);
                state.Apply();
                var cases = new List<(string, Mesh, Material)>
                {
                    ("MaterialDisabled", windMesh, legacy),
                    ("MeshWithoutWindFactor", legacyMesh, Own(WindMaterial(1.0f, 0.0f, waves, false))),
                    ("ProfileMismatch", windMesh, Own(WindMaterial(2.0f, 0.0f, waves, false)))
                };
                foreach ((string name, Mesh mesh, Material material) in cases)
                {
                    rig.Show(mesh, material, matrix, ShadowCastingMode.On);
                    CompareImages(rig.Debug(34), baseline, 0.0, name);
                }
                XRayFloraWindState.Clear();
                rig.Show(windMesh, Own(WindMaterial(1.0f, 0.0f, waves, false)), matrix, ShadowCastingMode.On);
                CompareImages(rig.Debug(34), baseline, 0.0, "ProviderInactive");
                rig.Hide();
                yield return null;
            }
            finally
            {
                rig?.Release();
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator FutureImportStoresWindFactorAndBindsVerifiedWind()
        {
            var cases = new[]
            {
                ("AnomalyDisabled", false, XRayFloraWindImportMode.Disabled, XRayFloraSourceProfile.Unknown),
                ("AnomalyVerified", false, XRayFloraWindImportMode.EnableVerifiedAnomaly, XRayFloraSourceProfile.AnomalySss),
                ("AnomalySourceWithTrueStalkerMode", false, XRayFloraWindImportMode.EnableVerifiedTrueStalker, XRayFloraSourceProfile.Unknown),
                ("TrueStalkerVerified", true, XRayFloraWindImportMode.EnableVerifiedTrueStalker, XRayFloraSourceProfile.TrueStalker)
            };
            foreach ((string name, bool trueStalker, XRayFloraWindImportMode mode, XRayFloraSourceProfile expected) in cases)
            {
                string root;
                using (var fixture = new XRayFloraLevelFixture(trueStalker, exportReports: false))
                {
                    root = fixture.Root;
                    yield return Import(fixture, mode);
                    XRayLevelImportRecord record = XRayLevelImportJob.LastRecord;
                    XRayInstanceBatch batch = fixture.Batches().Batches[0];
                    Material material = batch.Materials[0];
                    var uv3 = new List<Vector2>();
                    Mesh mesh = batch.Mesh;
                    mesh.GetUVs(3, uv3);
                    int windData = uv3.FindAll(value => value.y > 0.5f).Count;
                    XRayLevelVertexBlock block = null;
                    XRayLevelData level = XRayLevelReader.Read(File.ReadAllBytes(Path.Combine(fixture.Root, fixture.LevelName, "level")), name);
                    using (XRayLevelGeometry geometry = XRayLevelGeometry.Open(Path.Combine(fixture.Root, fixture.LevelName, "level.geom")))
                    {
                        foreach (XRayLevelVisual visual in level.Visuals)
                        {
                            if (!visual.IsTree) continue;
                            block = geometry.ReadVertices(visual.Geometry.VertexBuffer, visual.Geometry.VertexOffset, mesh.vertexCount);
                            break;
                        }
                    }
                    Assert.IsNotNull(block, name + ": the fixture must contain a source tree visual.");
                    var source = new List<float>(block.WindFactor);
                    var stored = uv3.ConvertAll(value => value.x);
                    source.Sort();
                    stored.Sort();
                    int mismatches = 0;
                    bool accepted = expected != XRayFloraSourceProfile.Unknown;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fixture.Root + "/" + fixture.LevelName + "/Prefabs/" + fixture.LevelName + ".prefab");
                    XRayFloraWindProvider[] providers = prefab.GetComponentsInChildren<XRayFloraWindProvider>(true);
                    Assert.AreEqual(accepted ? 1 : 0, providers.Length, name);
                    if (accepted)
                    {
                        Assert.AreEqual(trueStalker ? XRayFloraWindProfile.TrueStalkerXRay : XRayFloraWindProfile.AnomalySsfx, providers[0].Profile);
                        Assert.IsFalse(providers[0].TrueStalkerDiagnosticConstants);
                        Assert.IsFalse(providers[0].PreviewEnabled);
                    }
                    for (int v = 0; v < stored.Count; v++) if (accepted ? stored[v] != source[v] : stored[v] != 0.0f) mismatches++;
                    Texture waves = material.GetTexture(XRayFloraWindBindings.WAVES);
                    Assert.AreEqual(XRayLevelVertexKind.Tree, block.Kind);
                    Assert.AreEqual(0, mismatches, name + ": WindFactor must be stored exactly for verified imports and stay absent otherwise.");
                    Assert.AreEqual(accepted ? mesh.vertexCount : 0, windData, name);
                    if (mode == XRayFloraWindImportMode.Disabled)
                    {
                        Assert.IsTrue(string.IsNullOrEmpty(record.Source.FloraWindProfile), name);
                        Assert.IsEmpty(material.GetTag(XRayFloraWindBindings.TAG, false, string.Empty), name);
                    }
                    if (!accepted)
                    {
                        Assert.AreEqual(0.0f, material.GetFloat(XRayFloraWindBindings.WIND), name);
                        if (mode != XRayFloraWindImportMode.Disabled) Assert.IsTrue(record.Warnings.Exists(warning => warning.Contains("Source flora wind mode")), name);
                        continue;
                    }

                    Assert.AreEqual(expected.ToString(), record.Source.FloraWindProfile, name);
                    Assert.AreEqual(trueStalker ? XRayFloraSourceBinding.TRUE_STALKER_WIND_CONTRACT_HASH : XRayFloraSourceBinding.ANOMALY_WIND_CONTRACT_HASH, record.Source.FloraWindContractHash, name);
                    Assert.IsTrue(record.Dependencies.Exists(dependency => dependency.Role == "Flora wind contract"), name);
                    Assert.AreEqual(trueStalker ? 2.0f : 1.0f, material.GetFloat(XRayFloraWindBindings.WIND), name);
                    Assert.AreEqual(XRayFloraWindBindings.PART_BRANCH, material.GetFloat(XRayFloraWindBindings.PART), name);
                    Assert.IsTrue(material.GetShaderPassEnabled(XRayFloraWindBindings.MOTION_VECTORS_PASS), name);
                    Assert.AreEqual(!trueStalker, waves != null, name);
                    if (!trueStalker) Assert.IsTrue(record.Dependencies.Exists(dependency => dependency.Role == "Wind waves (s_waves)"), name);
                    AssetDatabase.SaveAssets();
                    Dictionary<string, string> before = Snapshot(root);
                    string meshGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh));
                    yield return Import(fixture, mode);
                    AssetDatabase.SaveAssets();
                    Dictionary<string, string> after = Snapshot(root);
                    CollectionAssert.AreEquivalent(before, after, name + ": a repeated wind import must not change saved asset bytes.");
                    Assert.AreEqual(meshGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(fixture.Batches().Batches[0].Mesh)), name);
                    material = fixture.Batches().Batches[0].Materials[0];
                    material.SetFloat(XRayFloraWindBindings.WIND, 0.0f);
                    AssetDatabase.SaveAssetIfDirty(material);
                    yield return Import(fixture, mode);
                    material = fixture.Batches().Batches[0].Materials[0];
                    Assert.AreEqual(0.0f, material.GetFloat(XRayFloraWindBindings.WIND), name + ": a user wind override must survive a repeated import.");
                    StringAssert.StartsWith(XRayFloraWindBindings.VERSION, material.GetTag(XRayFloraWindBindings.TAG, false, string.Empty));
                    material.SetFloat(XRayFloraWindBindings.WIND, trueStalker ? 2.0f : 1.0f);
                    AssetDatabase.SaveAssetIfDirty(material);
                    yield return VerifyRealSourceMotion(fixture.Batches().Batches[0], material, trueStalker);
                }
                Assert.IsFalse(Directory.Exists(root) || File.Exists(root + ".meta"), name);
            }

        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator SavedEscapeTreesWithoutWindFactorStayStatic()
        {
            HdrpTestWarmup.Ensure();
            var set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(ANOMALY_ROOT + "/l01_escape/Meshes/l01_escape_StaticProps.asset");
            Assert.IsNotNull(set, "Saved Anomaly l01_escape static props are required.");
            XRayInstanceBatch selected = null;
            foreach (XRayInstanceBatch batch in set.Batches)
            {
                if (batch.Mesh == null || batch.Materials.Length == 0 || batch.Materials[0] == null || !batch.Materials[0].IsKeywordEnabled("_MONOLITH_LEVEL") || !batch.Materials[0].name.Contains("tree_leaves")) continue;
                if (selected == null || batch.Mesh.vertexCount > selected.Mesh.vertexCount) selected = batch;
            }
            Assert.IsNotNull(selected);
            string meshHash = XRayLevelSourceRefresh.HashFile(AssetDatabase.GetAssetPath(selected.Mesh));
            string materialHash = XRayLevelSourceRefresh.HashFile(AssetDatabase.GetAssetPath(selected.Materials[0]));
            Material copy = Own(new Material(selected.Materials[0]));
            Mesh legacyMesh = Own(UnityEngine.Object.Instantiate(selected.Mesh));
            legacyMesh.SetUVs(3, new Vector2[legacyMesh.vertexCount]);
            copy.SetFloat(XRayFloraWindBindings.WIND, 1.0f);
            copy.SetTexture(XRayFloraWindBindings.WAVES, Own(Waves(false)));
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            MonolithFloraWindRig rig = null;
            try
            {
                rig = CreateRig(scene, temporary);
                Matrix4x4 matrix = selected.Matrices[0];
                rig.Frame(GeometryUtility.CalculateBounds(selected.Mesh.vertices, matrix));
                XRayFloraWindState.Clear();
                rig.Show(legacyMesh, copy, matrix, ShadowCastingMode.On);
                Color[] baseline = rig.Debug(34);
                State(XRayFloraWindProfile.AnomalySsfx, 9).Apply();
                CompareImages(rig.Debug(34), baseline, 0.0, "Saved l01_escape mesh without WindFactor");
                rig.Hide();
                yield return null;
            }
            finally
            {
                rig?.Release();
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Assert.AreEqual(meshHash, XRayLevelSourceRefresh.HashFile(AssetDatabase.GetAssetPath(selected.Mesh)));
            Assert.AreEqual(materialHash, XRayLevelSourceRefresh.HashFile(AssetDatabase.GetAssetPath(selected.Materials[0])));
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator RealProviderMovesTreeAndInstancedGrassInEditMode()
        {
            yield return CaptureProviderVegetation(false);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator RealProviderMovesTreeAndInstancedGrassInPlayMode()
        {
            yield return new EnterPlayMode();
            yield return CaptureProviderVegetation(true);
            TearDown();
            yield return new ExitPlayMode();
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator GameCameraSupportsObjectWindMotionInEditMode()
        {
            yield return CaptureProviderVegetation(false, true);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator NativeSceneViewDisablesObjectMotionDespiteExplicitRequest()
        {
            if (Application.isBatchMode) Assert.Ignore("Requires actual Scene View GUI repaint; run separately in the interactive Editor on both APIs.");
            yield return CaptureProviderVegetation(false, false, true);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator PausedGameCameraHasNoNativeWindMotion()
        {
            yield return CaptureProviderVegetation(false, true, false, true);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator NativeSceneViewsShareOneVegetationPopulation()
        {
            if (Application.isBatchMode) Assert.Ignore("Requires two native Scene View windows; execute separately on both APIs.");
            yield return CaptureProviderVegetation(false, false, true, multipleViews: true);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator RealSourceTreeInstancingMovesLeavesAndTrunk()
        {
            yield return CaptureProviderVegetation(false, true, realTreeInstancing: true);
        }

        private IEnumerator CaptureProviderVegetation(bool playing, bool gameCamera = false, bool nativeSceneView = false, bool pausedAcceptance = false, bool multipleViews = false, bool realTreeInstancing = false)
        {
            HdrpTestWarmup.Ensure();
            var set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(ANOMALY_ROOT + "/l01_escape/Meshes/l01_escape_StaticProps.asset");
            Assert.IsNotNull(set);
            Scene runtimeScene = SceneManager.GetActiveScene();
            Scene scene = playing ? SceneManager.CreateScene("DIAGNOSTIC wind GPU fixture") : EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            var windRoot = new GameObject("DIAGNOSTIC real vegetation wind");
            SceneManager.MoveGameObjectToScene(windRoot, runtimeScene);
            temporary.Add(windRoot);
            bool runInBackground = Application.runInBackground;
            Application.runInBackground = true;
            if (playing)
            {
                _captureDeltaTime = Time.captureDeltaTime;
                _captureTimeOwned = true;
                Time.captureDeltaTime = 1.0f / 30.0f;
            }
            MonolithFloraWindRig rig = null;
            Action<ScriptableRenderContext, Camera> diagnosticSubmission = null;
            Action<ScriptableRenderContext, Camera> automaticSubmission = null;
            try
            {
                XRayFloraWindProvider provider = windRoot.AddComponent<XRayFloraWindProvider>();
                provider.Profile = XRayFloraWindProfile.AnomalySsfx;
                provider.PreviewEnabled = false;
                provider.SetWeather(30.0f, 900.0f, 0.004f);
                provider.ResetAnimation();
                rig = CreateRig(scene, temporary, false, !playing && !gameCamera);
                foreach (string part in new[] { "opaque", "alpha_clipped", "tree_leaves", "tree_trunk" })
                {
                    bool synthetic = part == "opaque" || part == "alpha_clipped";
                    string sourcePart = part == "opaque" ? "tree_trunk" : synthetic ? "tree_leaves" : part;
                    XRayInstanceBatch selected = null;
                    foreach (XRayInstanceBatch batch in set.Batches)
                    {
                        if (batch.Mesh == null || batch.Materials.Length == 0 || batch.Materials[0] == null || !batch.Materials[0].name.Contains(sourcePart)) continue;
                        if (selected == null || batch.Mesh.vertexCount > selected.Mesh.vertexCount) selected = batch;
                    }
                    Assert.IsNotNull(selected, part);
                    Material material = Own(new Material(selected.Materials[0]));
                    Assert.AreEqual(1.0f, material.GetFloat(XRayFloraWindBindings.WIND));
                    Matrix4x4 matrix = synthetic ? Matrix4x4.identity : selected.Matrices[0];
                    Mesh mesh = synthetic ? Own(Grid(true)) : selected.Mesh;
                    rig.Frame(GeometryUtility.CalculateBounds(mesh.vertices, matrix));
                    rig.Show(mesh, material, matrix, ShadowCastingMode.On);
                    GameObject instancedRoot = null;
                    if (realTreeInstancing)
                    {
                        rig.Hide();
                        material.enableInstancing = true;
                        instancedRoot = new GameObject("Real source tree instancing acceptance");
                        temporary.Add(instancedRoot);
                        SceneManager.MoveGameObjectToScene(instancedRoot, scene);
                        var batchSet = Own(ScriptableObject.CreateInstance<XRayInstanceBatchSet>());
                        Vector4 instanceData = selected.Data != null && selected.Data.Length > 0 ? selected.Data[0] : new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
                        batchSet.Initialize("source tree", 32.0f, new List<XRayInstanceBatch> { new XRayInstanceBatch(part, mesh, new[] { material }, GeometryUtility.CalculateBounds(mesh.vertices, matrix), new[] { matrix }, new[] { instanceData }, true) });
                        instancedRoot.AddComponent<XRayLevelInstancedRenderer>().BatchSet = batchSet;
                    }
                    if (nativeSceneView) yield return rig.NativeFrame(34, null);
                    else rig.Debug(34);
                    material.SetFloat(XRayFloraWindBindings.WIND, 0.0f);
                    Color[] off = null;
                    Color[] offNext = null;
                    if (nativeSceneView) yield return rig.NativeFrame(34, value => off = value);
                    else off = rig.Debug(34);
                    provider.Tick(0.1f);
                    if (nativeSceneView) yield return rig.NativeFrame(34, value => offNext = value);
                    else offNext = rig.Debug(34);
                    CollectionAssert.AreEqual(off, offNext, part + ": disabled wind must preserve geometry while animation time advances.");
                    material.SetFloat(XRayFloraWindBindings.WIND, 1.0f);
                    Color[] previous = null;
                    float maximumTreeMotion = 0.0f;
                    for (int frame = 0; frame < 3; frame++)
                    {
                        if (playing) yield return new WaitForSecondsRealtime(0.5f);
                        else provider.Tick(0.1f);
                        Color[] pixels = null;
                        if (nativeSceneView)
                        {
                            yield return rig.NativeFrame(34, value => pixels = value);
                            yield return rig.NativeFrame(0, null);
                            yield return rig.NativeFrame(0, null, true);
                        }
                        else
                        {
                            pixels = rig.Debug(34);
                            rig.Debug(0);
                            Color[] treeMotion = rig.RawMotion();
                            float frameMotion = MaximumMotion(treeMotion);
                            maximumTreeMotion = Mathf.Max(maximumTreeMotion, frameMotion);
                        }
                        float maximum = MaximumGeometryDelta(previous, pixels);
                        if (frame > 0) Assert.Greater(maximum, 0.00001f, part + ": real GPU geometry must move between frames.");
                        previous = pixels;
                    }
                    if (realTreeInstancing) Assert.Greater(maximumTreeMotion, 0.0f, part + ": actual source tree instancing must produce native wind motion across the moving sequence.");
                    provider.Paused = true;
                    if (playing) yield return new WaitForSecondsRealtime(0.1f);
                    else provider.Tick(0.0f);
                    Color[] paused = null;
                    if (nativeSceneView) yield return rig.NativeFrame(34, value => paused = value);
                    else paused = rig.Debug(34);
                    CollectionAssert.AreEqual(previous, paused, part + ": pause must freeze geometry.");
                    if (realTreeInstancing) Assert.LessOrEqual(MaximumMotion(rig.RawMotion()), MOTION_TOLERANCE);
                    provider.Paused = false;
                    rig.Hide();
                    if (instancedRoot != null) UnityEngine.Object.DestroyImmediate(instancedRoot);
                }

                if (realTreeInstancing) yield break;

                var stored = AssetDatabase.LoadAssetAtPath<XRayDetailDatabase>(ANOMALY_ROOT + "/l01_escape/Details/DetailDatabase.asset");
                Assert.IsNotNull(stored);
                Assert.IsTrue(stored.HasRuntimeSource);
                var database = Own(ScriptableObject.CreateInstance<XRayDetailDatabase>());
                var models = new List<XRayDetailModelEntry>();
                foreach (XRayDetailModelEntry model in stored.Models)
                {
                    Material material = Own(new Material(model.Material));
                    material.SetFloat("_MonolithDebug", 34.0f);
                    models.Add(new XRayDetailModelEntry(model.Shader, model.Texture, model.Mesh, material, model.InverseHeight, model.NoWaving, model.MinScale, model.MaxScale, model.InstanceCount));
                }
                database.Initialize(stored.LevelName, stored.ChunkSize, stored.Density, stored.HeightScale, stored.SlotSize, models, new List<XRayDetailChunk>(), stored.Instances, stored.Slots, stored.SwingNormal, stored.SwingFast, stored.InstanceCount, stored.SlotCount);
                database.SetRuntimeSource(stored.SourceDetails, stored.SourceCollision, stored.Passable, stored.SourceProfile);
                var root = new GameObject("DIAGNOSTIC instanced grass");
                temporary.Add(root);
                root.layer = 31;
                SceneManager.MoveGameObjectToScene(root, scene);
                var vegetation = root.AddComponent<XRayDetailVegetation>();
                vegetation.Database = database;
                var defaults = XRayGrassDefaults.Load(Resources.Load<TextAsset>("XRayRvmGrassDefaults").text);
                var settings = new XRayGrassSettings(defaults, null);
                settings.Set(new XRayGrassValues(0.45f, 50, 1.0f, false), false);
                vegetation.PrepareSource(settings);
                XRayDetails details = XRayDetails.Read(database.SourceDetails.bytes, "DIAGNOSTIC real source");
                Vector3 eye = Vector3.zero;
                bool found = false;
                for (int z = details.SizeZ / 2; z < details.SizeZ && !found; z += 16)
                {
                    for (int x = details.SizeX / 2; x < details.SizeX && !found; x += 16)
                    {
                        XRayDetailSlot slot = details.GetSlot(x, z);
                        if (slot.IsEmpty) continue;
                        eye = new Vector3((x - details.OffsetX + 0.5f) * 2.0f, slot.YBase + 1.0f, (z - details.OffsetZ + 0.5f) * 2.0f);
                        vegetation.Population.Stream(eye, 0.1f);
                        found = vegetation.ResidentInstances > 0;
                    }
                }
                Assert.IsTrue(found);
                rig.Frame(new Bounds(eye, Vector3.one * 12.0f));
                rig.Camera.cameraType = playing || gameCamera ? CameraType.Game : CameraType.SceneView;
                rig.Camera.cullingMask = 1 << 31;
                rig.Camera.overrideSceneCullingMask = ulong.MaxValue;
                vegetation.StreamingCamera = rig.Camera;
                if ((gameCamera || nativeSceneView) && !multipleViews)
                {
                    MethodInfo ensureBuffers = typeof(XRayDetailVegetation).GetMethod("EnsureBuffers", BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo renderSource = typeof(XRayDetailVegetation).GetMethod("RenderSource", BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo beginCamera = typeof(XRayDetailVegetation).GetMethod("OnBeginCameraRendering", BindingFlags.Instance | BindingFlags.NonPublic);
                    automaticSubmission = (Action<ScriptableRenderContext, Camera>)Delegate.CreateDelegate(typeof(Action<ScriptableRenderContext, Camera>), vegetation, beginCamera);
                    RenderPipelineManager.beginCameraRendering -= automaticSubmission;
                    diagnosticSubmission = (context, camera) =>
                    {
                        if (camera != rig.Camera || vegetation == null || !vegetation.isActiveAndEnabled) return;
                        if (nativeSceneView || playing)
                        {
                            automaticSubmission(context, camera);
                            return;
                        }
                        ensureBuffers.Invoke(vegetation, null);
                        renderSource.Invoke(vegetation, new object[] { camera });
                    };
                    RenderPipelineManager.beginCameraRendering += diagnosticSubmission;
                    _fixtureSubmissions.Add(diagnosticSubmission);
                }
                if (nativeSceneView) yield return rig.NativeFrame(-1, null);
                for (int i = 0; i < 2000 && (vegetation.Population.NeedsStreaming || vegetation.Population.HasFadingCells); i++) vegetation.Population.Stream(rig.Camera.transform.position, 0.1f);
                if (nativeSceneView)
                {
                    int covered = 0;
                    for (int warmup = 0; warmup < 4 && covered == 0; warmup++)
                    {
                        Color[] warmPixels = null;
                        yield return rig.NativeFrame(-1, value => warmPixels = value);
                        foreach (Color pixel in warmPixels) if (Covered(pixel)) covered++;
                    }
                    Assert.Greater(covered, 0, "Settled native Scene View must contain real grass GPU geometry before the motion sequence.");
                }
                if (multipleViews)
                {
                    vegetation.StreamingCamera = null;
                    vegetation.enabled = false;
                    vegetation.enabled = true;
                    Assert.IsTrue(vegetation.CanStreamFrom(rig.Camera));
                    vegetation.PrepareSource(settings);
                    for (int i = 0; i < 2000 && (vegetation.Population.NeedsStreaming || vegetation.Population.HasFadingCells); i++) vegetation.Population.Stream(rig.Camera.transform.position, 0.1f);
                    yield return rig.NativeFrame(-1, null);
                    XRayDetailPopulation population = vegetation.Population;
                    object buffers = typeof(XRayDetailVegetation).GetField("_sourceBuffers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(vegetation);
                    string signature = PopulationSignature(population);
                    MonolithFloraWindRig other = CreateRig(scene, temporary, false, true);
                    try
                    {
                        other.Hide();
                        other.Frame(new Bounds(rig.Camera.transform.position + new Vector3(1000.0f, 0.0f, 1000.0f), Vector3.one * 12.0f));
                        Assert.IsFalse(vegetation.CanStreamFrom(other.Camera));
                        for (int i = 0; i < 5; i++)
                        {
                            provider.Tick(0.05f);
                            double time = provider.State.Time;
                            Color[] ownerPixels = null;
                            Color[] otherPixels = null;
                            yield return rig.NativeFrame(-1, value => ownerPixels = value);
                            yield return other.NativeFrame(-1, value => otherPixels = value);
                            int ownerCovered = 0;
                            int otherCovered = 0;
                            foreach (Color pixel in ownerPixels) if (Covered(pixel)) ownerCovered++;
                            foreach (Color pixel in otherPixels) if (Covered(pixel)) otherCovered++;
                            Assert.Greater(ownerCovered, 0);
                            Assert.AreEqual(0, otherCovered);
                            Assert.AreSame(population, vegetation.Population);
                            Assert.AreSame(buffers, typeof(XRayDetailVegetation).GetField("_sourceBuffers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(vegetation));
                            Assert.AreEqual(signature, PopulationSignature(population));
                            Assert.AreEqual(time, provider.State.Time);
                        }
                    }
                    finally
                    {
                        other.Release();
                    }
                    yield break;
                }
                Color[] grassPrevious = null;
                for (int frame = 0; frame < 3; frame++)
                {
                    if (playing) yield return new WaitForSecondsRealtime(0.5f);
                    else provider.Tick(0.1f);
                    Color[] pixels = null;
                    if (nativeSceneView) yield return rig.NativeFrame(-1, value => pixels = value);
                    else pixels = rig.Raw();
                    foreach (XRayDetailModelEntry model in models) model.Material.SetFloat("_MonolithDebug", 0.0f);
                    if (nativeSceneView) yield return rig.NativeFrame(-1, null);
                    else rig.Raw();
                    foreach (XRayDetailModelEntry model in models) model.Material.SetFloat("_MonolithDebug", 34.0f);
                    float maximum = MaximumGeometryDelta(grassPrevious, pixels);
                    Assert.Greater(vegetation.LastVisibleInstances, 0);
                    if (frame > 0) Assert.Greater(maximum, 0.00001f, "Actual detail GPU instancing must deform geometry.");
                    grassPrevious = pixels;
                }
                Color[] grassMotion = null;
                if (nativeSceneView) yield return rig.NativeFrame(-1, value => grassMotion = value, true);
                else grassMotion = rig.RawMotion();
                float maximumMotion = 0.0f;
                foreach (Color pixel in grassMotion) maximumMotion = Mathf.Max(maximumMotion, Mathf.Max(Mathf.Abs(pixel.r), Mathf.Abs(pixel.g)));
                if (!playing && !gameCamera)
                {
                    string version = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(HDRenderPipeline).Assembly).version;
                    Assert.IsTrue(version == "17.3.0" || version == "17.6.0", "Review the installed HDRP source and GPU camera contract before accepting a package upgrade.");
                    Assert.IsFalse(HDCamera.GetOrCreate(rig.Camera).frameSettings.IsEnabled(FrameSettingsField.ObjectMotionVectors), "The reviewed HDRP versions forcibly disable object motion for non-Game cameras.");
                    Assert.AreEqual(0.0f, maximumMotion, "The reviewed HDRP SceneView geometry animates without native object motion vectors.");
                }
                else if (!pausedAcceptance) Assert.Greater(maximumMotion, 0.0f, "Actual instanced grass wind must produce object motion vectors with a static camera.");
                provider.Paused = true;
                if (playing) yield return new WaitForSecondsRealtime(0.1f);
                else provider.Tick(0.0f);
                Color[] pausedMotion = null;
                if (nativeSceneView) yield return rig.NativeFrame(-1, value => pausedMotion = value, true);
                else pausedMotion = rig.RawMotion();
                float maximumPaused = 0.0f;
                foreach (Color pixel in pausedMotion) maximumPaused = Mathf.Max(maximumPaused, Mathf.Max(Mathf.Abs(pixel.r), Mathf.Abs(pixel.g)));
                Assert.LessOrEqual(maximumPaused, MOTION_TOLERANCE, "Paused grass must not generate wind motion vectors.");
                if (playing)
                {
                    yield return null;
                    rig.RawMotion();
                    settings.Set(new XRayGrassValues(settings.Values.Density, settings.Values.Radius, 2.0f, settings.Values.Shadows), false);
                    float heightMotion = MaximumMotion(rig.RawMotion());
                    Assert.Greater(heightMotion, 0.0f, "A height edit must retain the previous height across repeated renders of the same Game frame.");
                    yield return null;
                    float settledHeightMotion = MaximumMotion(rig.RawMotion());
                    Assert.LessOrEqual(settledHeightMotion, MOTION_TOLERANCE);
                }
            }
            finally
            {
                if (diagnosticSubmission != null) RenderPipelineManager.beginCameraRendering -= diagnosticSubmission;
                Application.runInBackground = runInBackground;
                RestoreCaptureTime();
                rig?.Release();
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                if (Application.isPlaying) SceneManager.UnloadSceneAsync(scene);
                else EditorSceneManager.ClosePreviewScene(scene);
                XRayFloraWindState.Clear();
            }
        }

        private void RestoreCaptureTime()
        {
            if (!_captureTimeOwned) return;
            Time.captureDeltaTime = _captureDeltaTime;
            _captureTimeOwned = false;
        }

        private static float MaximumGeometryDelta(Color[] previous, Color[] current)
        {
            float maximum = 0.0f;
            if (previous == null)
            {
                return maximum;
            }
            for (int i = 0; i < current.Length; i++)
            {
                if (!Covered(previous[i]) || !Covered(current[i])) continue;
                float difference = Mathf.Max(Mathf.Abs(current[i].r - previous[i].r), Mathf.Max(Mathf.Abs(current[i].g - previous[i].g), Mathf.Abs(current[i].b - previous[i].b)));
                maximum = Mathf.Max(maximum, difference);
            }
            return maximum;
        }

        private static float MaximumMotion(Color[] pixels)
        {
            float result = 0.0f;
            foreach (Color pixel in pixels) result = Mathf.Max(result, Mathf.Abs(pixel.r), Mathf.Abs(pixel.g));
            return result;
        }

        private static string PopulationSignature(XRayDetailPopulation population)
        {
            var cells = new List<string>();
            foreach (XRayDetailCell cell in population.Cells) cells.Add(cell.X + ":" + cell.Z + ":" + cell.Generation + ":" + cell.Instances.Count);
            cells.Sort(StringComparer.Ordinal);
            return string.Join("|", cells);
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator InstancedTreesUseNativePreviousTransforms()
        {
            yield return new EnterPlayMode();
            HdrpTestWarmup.Ensure();
            string original = MonolithFloraReferenceSource.Build(1, File.ReadAllText(MonolithFloraReferenceSource.TEMPLATE), out _);
            Material reference = Reference(original, "InstanceOriginalBranch");
            Scene scene = SceneManager.CreateScene("Tree native history acceptance");
            var temporary = new List<UnityEngine.Object>();
            var failures = new List<string>();
            MonolithFloraWindRig rig = null;
            try
            {
                rig = CreateRig(scene, temporary, false);
                Mesh mesh = Own(Grid(true));
                Texture2D waves = Own(Waves(false));
                Material material = Own(WindMaterial(1.0f, 0.0f, waves, false));
                material.enableInstancing = true;
                var root = new GameObject("Tree submission history acceptance");
                temporary.Add(root);
                SceneManager.MoveGameObjectToScene(root, scene);
                var providerRoot = new GameObject("Tree history wind provider");
                temporary.Add(providerRoot);
                var provider = providerRoot.AddComponent<XRayFloraWindProvider>();
                provider.Profile = XRayFloraWindProfile.AnomalySsfx;
                provider.PreviewEnabled = false;
                provider.SetWeather(30.0f, 900.0f, 0.004f);
                provider.ResetAnimation();
                provider.Paused = true;
                var set = Own(ScriptableObject.CreateInstance<XRayInstanceBatchSet>());
                var matrices = new[] { Matrix4x4.identity };
                set.Initialize("fixture", 32.0f, new List<XRayInstanceBatch> { new XRayInstanceBatch("source tree", mesh, new[] { material }, new Bounds(Vector3.zero, Vector3.one * 20.0f), matrices, new[] { new Vector4(1.0f, 0.0f, 0.0f, 1.0f) }, false) });
                var renderer = root.AddComponent<XRayLevelInstancedRenderer>();
                renderer.BatchSet = set;
                rig.Frame(new Bounds(Vector3.zero, new Vector3(6.0f, 6.0f, 6.0f)));
                rig.Hide();
                rig.InstanceFrame(material);
                foreach (string scenario in new[] { "FirstFrame", "StaticWind", "MovingInstance", "MovingScaledInstance", "MovingParentWorldSpace", "DisabledWind", "MovingInstanceWindOff", "SubsetReplacement", "CameraMotion", "TeleportReset" })
                {
                    Matrix4x4 previousMatrix = matrices[0];
                    Matrix4x4 previousViewProjection = GL.GetGPUProjectionMatrix(rig.Camera.projectionMatrix, true) * rig.Camera.worldToCameraMatrix;
                    yield return null;
                    provider.Paused = false;
                    if (scenario == "StaticWind") provider.Tick(0.1f);
                    else provider.Tick(0.0f);
                    provider.Paused = true;
                    if (scenario == "MovingInstance" || scenario == "MovingInstanceWindOff") matrices[0] = Matrix4x4.Translate(new Vector3(0.25f, 0.0f, 0.0f)) * matrices[0];
                    if (scenario == "MovingScaledInstance") matrices[0] = Matrix4x4.TRS(new Vector3(0.1f, 0.1f, 0.0f), Quaternion.Euler(0.0f, 0.0f, 10.0f), new Vector3(1.1f, 0.9f, 1.0f));
                    if (scenario == "MovingParentWorldSpace") root.transform.position += new Vector3(10.0f, 0.0f, 0.0f);
                    if (scenario == "DisabledWind") material.SetFloat(XRayFloraWindBindings.WIND, 0.0f);
                    if (scenario == "SubsetReplacement")
                    {
                        renderer.BatchSet = set;
                        previousMatrix = matrices[0];
                    }
                    if (scenario == "CameraMotion") rig.Camera.transform.position += new Vector3(0.21f, -0.13f, 0.0f);
                    if (scenario == "FirstFrame" || scenario == "TeleportReset")
                    {
                        if (scenario == "TeleportReset") matrices[0] = Matrix4x4.Translate(new Vector3(-0.75f, 0.0f, 0.0f));
                        if (scenario == "FirstFrame")
                        {
                            renderer.enabled = false;
                            renderer.enabled = true;
                        }
                        else renderer.ResetMotionHistory();
                        previousMatrix = matrices[0];
                        provider.Reinitialize();
                    }
                    bool wind = material.GetFloat(XRayFloraWindBindings.WIND) > 0.5f;
                    Vector3[] current = wind ? Evaluate(reference, mesh.vertices, Uvs(mesh, 0), Uvs(mesh, 3), matrices[0], provider.State, false, waves) : Array.ConvertAll(mesh.vertices, vertex => matrices[0].MultiplyPoint3x4(vertex));
                    Vector3[] previous = wind ? Evaluate(reference, mesh.vertices, Uvs(mesh, 0), Uvs(mesh, 3), previousMatrix, provider.State, true, waves) : Array.ConvertAll(mesh.vertices, vertex => previousMatrix.MultiplyPoint3x4(vertex));
                    (Color[] vectors, Color[] uvs) = rig.InstanceFrame(material);
                    Matrix4x4 viewProjection = GL.GetGPUProjectionMatrix(rig.Camera.projectionMatrix, true) * rig.Camera.worldToCameraMatrix;
                    (int pixels, double error, double maximum) = CompareMotion(vectors, uvs, current, previous, viewProjection, previousViewProjection, scenario);
                    if (_motionFailure != null) failures.Add(_motionFailure);
                    if ((scenario == "StaticWind" || scenario == "MovingInstance" || scenario == "MovingInstanceWindOff" || scenario == "CameraMotion") && maximum <= 0.0) failures.Add(scenario + ": expected nonzero native motion.");
                    if (scenario == "FirstFrame" || scenario == "TeleportReset")
                    {
                        if (maximum > MOTION_TOLERANCE) failures.Add(scenario + ": unexpected motion spike.");
                    }
                    rig.InstanceFrame(material);
                }
                material.SetFloat(XRayFloraWindBindings.WIND, 1.0f);
                provider.Reinitialize();
                var lods = Own(ScriptableObject.CreateInstance<XRayLevelLodSet>());
                var spheres = new[] { new Vector4(10.0f, 0.0f, 0.0f, 0.00001f), new Vector4(0.0f, 0.0f, 0.0f, 100.0f) };
                lods.Initialize(null, spheres, new[] { 1.0f, 1.0f }, Array.Empty<Vector3>(), Array.Empty<Vector2>(), Array.Empty<float>(), Array.Empty<float>(), Array.Empty<Vector3>(), 1.0f, 64.0f, 48.0f, 3.5f);
                var subset = new[] { Matrix4x4.Translate(Vector3.right * 10.0f), Matrix4x4.identity };
                set.Initialize("LOD history", 32.0f, new List<XRayInstanceBatch> { new XRayInstanceBatch("LOD subset", mesh, new[] { material }, new Bounds(Vector3.zero, Vector3.one * 40.0f), subset, new[] { Vector4.one, Vector4.one }, false, new[] { 0, 1 }) }, lods);
                renderer.BatchSet = set;
                renderer.LodImpostors = true;
                rig.InstanceFrame(material);
                foreach (string scenario in new[] { "LodPartialSubset", "LodFullBatch", "LodPartialReturn" })
                {
                    Matrix4x4 previousMatrix = subset[1];
                    yield return null;
                    subset[1] = Matrix4x4.Translate(new Vector3(0.15f, 0.0f, 0.0f)) * subset[1];
                    spheres[0].w = scenario == "LodFullBatch" ? 100.0f : 0.00001f;
                    Vector3[] current = Evaluate(reference, mesh.vertices, Uvs(mesh, 0), Uvs(mesh, 3), subset[1], provider.State, false, waves);
                    Vector3[] previous = Evaluate(reference, mesh.vertices, Uvs(mesh, 0), Uvs(mesh, 3), previousMatrix, provider.State, true, waves);
                    (Color[] vectors, Color[] uvs) = rig.InstanceFrame(material);
                    Matrix4x4 viewProjection = GL.GetGPUProjectionMatrix(rig.Camera.projectionMatrix, true) * rig.Camera.worldToCameraMatrix;
                    (int pixels, double error, double maximum) = CompareMotion(vectors, uvs, current, previous, viewProjection, viewProjection, scenario);
                    Assert.AreEqual(scenario == "LodFullBatch" ? 0 : 1, renderer.LastLodDiscardedInstances);
                    if (_motionFailure != null) failures.Add(_motionFailure);
                }
                var boundaryMatrices = new Matrix4x4[512];
                var boundaryData = new Vector4[512];
                Bounds boundaryBounds = mesh.bounds;
                for (int i = 0; i < boundaryMatrices.Length; i++)
                {
                    Vector3 offset = new Vector3(i % 32 * 8.0f, i / 32 * 18.0f, 0.0f);
                    boundaryMatrices[i] = Matrix4x4.Translate(offset);
                    boundaryData[i] = Vector4.one;
                    boundaryBounds.Encapsulate(new Bounds(mesh.bounds.center + offset, mesh.bounds.size));
                }
                set.Initialize("Native constant buffer boundary", 32.0f, new List<XRayInstanceBatch> { new XRayInstanceBatch("512 source instances", mesh, new[] { material }, boundaryBounds, boundaryMatrices, boundaryData, false) });
                renderer.BatchSet = set;
                material.SetFloat(XRayFloraWindBindings.WIND, 0.0f);
                material.SetFloat("_MonolithDebug", 34.0f);
                rig.Frame(boundaryBounds);
                Color[] boundaryPixels = rig.Raw();
                var visibleIds = new HashSet<int>();
                foreach (Color pixel in boundaryPixels)
                {
                    if (!Covered(pixel)) continue;
                    int x = Mathf.RoundToInt((pixel.r - mesh.bounds.center.x) / 8.0f);
                    int y = Mathf.RoundToInt((pixel.g - mesh.bounds.center.y) / 18.0f);
                    if (x >= 0 && x < 32 && y >= 0 && y < 16) visibleIds.Add(y * 32 + x);
                }
                Assert.AreEqual(512, visibleIds.Count, "GPU geometry must contain every instance across native submission boundaries.");
                yield return null;
                float boundaryMotion = MaximumMotion(rig.RawMotion());
                Assert.LessOrEqual(boundaryMotion, MOTION_TOLERANCE);
                Assert.IsEmpty(failures, string.Join("\n", failures));
            }
            finally
            {
                rig?.Release();
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                SceneManager.UnloadSceneAsync(scene);
            }
            TearDown();
            yield return new ExitPlayMode();
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator RealAnomalyLeafAlphaShadowMatchesSourceDeformedMesh()
        {
            HdrpTestWarmup.Ensure();
            var set = AssetDatabase.LoadAssetAtPath<XRayInstanceBatchSet>(ANOMALY_ROOT + "/l01_escape/Meshes/l01_escape_StaticProps.asset");
            Assert.IsNotNull(set);
            XRayInstanceBatch selected = null;
            foreach (XRayInstanceBatch batch in set.Batches)
            {
                if (batch.Mesh == null || batch.Materials.Length == 0 || batch.Materials[0] == null || !batch.Materials[0].name.Contains("tree_leaves") || !batch.Materials[0].IsKeywordEnabled("_MONOLITH_LEVEL_FLORA")) continue;
                if (selected == null || batch.Mesh.vertexCount > selected.Mesh.vertexCount) selected = batch;
            }
            Assert.IsNotNull(selected);
            Material wind = Own(new Material(selected.Materials[0]));
            Assert.AreEqual(1.0f, wind.GetFloat(XRayFloraWindBindings.WIND));
            Assert.IsTrue(wind.IsKeywordEnabled("_ALPHATEST_ON"));
            Texture2D waves = wind.GetTexture(XRayFloraWindBindings.WAVES) as Texture2D;
            Assert.IsNotNull(waves);
            Texture2D referenceWaves = wind.GetFloat(XRayFloraWindBindings.WAVES_FLIP) > 0.5f ? Own(UnflippedWaves(waves)) : waves;
            string source = MonolithFloraReferenceSource.BuildShadow(false, true, File.ReadAllText(MonolithFloraReferenceSource.TEMPLATE), out _);
            Material reference = Reference(source, "Real_Alpha_Shadow");
            Matrix4x4 matrix = selected.Matrices[0];
            XRayFloraWindState state = State(XRayFloraWindProfile.AnomalySsfx, 941);
            Vector3[] vertices = Evaluate(reference, selected.Mesh.vertices, Uvs(selected.Mesh, 0), Uvs(selected.Mesh, 3), matrix, state, false, referenceWaves);
            Mesh bakedMesh = Own(Baked(selected.Mesh, vertices, matrix));
            Material baked = Own(new Material(wind));
            baked.SetFloat(XRayFloraWindBindings.WIND, 0.0f);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            MonolithFloraWindRig rig = null;
            try
            {
                rig = CreateRig(scene, temporary);
                rig.Frame(GeometryUtility.CalculateBounds(vertices, Matrix4x4.identity));
                state.Apply();
                rig.Receiver(true);
                rig.Show(selected.Mesh, wind, matrix, ShadowCastingMode.ShadowsOnly);
                Color[] actual = rig.Debug(0);
                rig.Show(bakedMesh, baked, matrix, ShadowCastingMode.ShadowsOnly);
                Color[] expected = rig.Debug(0);
                (int pixels, int differing, double error) parity = CompareShadow(actual, expected);
                rig.Hide();
                Color[] unshadowed = rig.Debug(0);
                (int maskPixels, int shadowPixels, double maskError) shadowMask = CompareShadow(actual, unshadowed);
                Assert.Greater(shadowMask.shadowPixels, 10, "The real alpha-clipped leaves must cast a nonempty shadow.");
                Assert.LessOrEqual(parity.differing, Math.Max(2, parity.pixels / 2000), "Alpha-clipped leaf shadow must match original HLSL-deformed geometry.");
                yield return null;
            }
            finally
            {
                rig?.Release();
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.ClosePreviewScene(scene);
                XRayFloraWindState.Clear();
            }
        }

        private static Texture2D UnflippedWaves(Texture2D source)
        {
            RenderTexture target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var texture = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true) { filterMode = source.filterMode, wrapMode = source.wrapMode };
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                texture.Apply();
                Color[] pixels = texture.GetPixels();
                var flipped = new Color[pixels.Length];
                for (int y = 0; y < source.height; y++) Array.Copy(pixels, y * source.width, flipped, (source.height - 1 - y) * source.width, source.width);
                texture.SetPixels(flipped);
                texture.Apply();
                return texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private IEnumerator VerifyRealSourceMotion(XRayInstanceBatch batch, Material stored, bool trueStalker)
        {
            HdrpTestWarmup.Ensure();
            Scene scene = EditorSceneManager.NewPreviewScene();
            var temporary = new List<UnityEngine.Object>();
            MonolithFloraWindRig rig = null;
            try
            {
                rig = CreateRig(scene, temporary);
                Matrix4x4 matrix = batch.Matrices[0];
                rig.Frame(GeometryUtility.CalculateBounds(batch.Mesh.vertices, matrix));
                Material material = Own(new Material(stored));
                XRayFloraWindState.Clear();
                rig.Show(batch.Mesh, material, matrix, ShadowCastingMode.On);
                rig.Debug(34);
                XRayFloraWindProfile profile = trueStalker ? XRayFloraWindProfile.TrueStalkerXRay : XRayFloraWindProfile.AnomalySsfx;
                foreach ((string scenario, float direction, float velocity, float delta) in new[] { ("LowVelocity", 30.0f, 0.0f, 0.033f), ("HighVelocity", 30.0f, 2500.0f, 0.033f), ("DirectionChange", 210.0f, 2500.0f, 0.033f), ("Paused", 210.0f, 2500.0f, 0.0f) })
                {
                    var state = new XRayFloraWindState { Profile = profile, TreeAmplitude = 0.005f };
                    var weather = new XRayFloraWindWeather(direction * Mathf.Deg2Rad, velocity, 0.004f);
                    state.Reset(weather);
                    for (int frame = 0; frame < 3; frame++)
                    {
                        state.Advance(frame == 0 ? 0.5f : delta, weather);
                        state.Apply();
                        rig.Debug(34);
                        var motionFrame = rig.Motion(scenario == "DirectionChange" && frame == 2 ? new Vector3(0.3f, 0.0f, 0.0f) : Vector3.zero);
                        float motion = 0;
                        foreach (Color vector in motionFrame.Item3) motion = Mathf.Max(motion, Mathf.Max(Mathf.Abs(vector.r), Mathf.Abs(vector.g)));
                        if (scenario == "Paused" && frame > 0) Assert.AreEqual(0.0f, motion, "Paused wind must not produce motion vectors.");
                    }
                }
                rig.Hide();
                yield return null;
            }
            finally
            {
                rig?.Release();
                for (int i = temporary.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(temporary[i]);
                EditorSceneManager.ClosePreviewScene(scene);
                XRayFloraWindState.Clear();
            }
        }

        private static IEnumerator Import(XRayFloraLevelFixture fixture, XRayFloraWindImportMode mode)
        {
            Assert.IsNull(XRayLevelImportJob.Current);
            Assert.IsTrue(XRayLevelImportJob.Enqueue(fixture.Request(XRayFloraImportMode.Preserve, mode)));
            double start = EditorApplication.timeSinceStartup;
            while (XRayLevelImportJob.Current != null)
            {
                if (EditorApplication.timeSinceStartup - start > 300)
                {
                    XRayLevelImportJob.CancelCurrent();
                    Assert.Fail("Full wind import exceeded 300 seconds.");
                }
                yield return null;
            }
            Assert.AreEqual(XRayLevelJobState.Completed, XRayLevelImportJob.LastState, string.Join("; ", XRayLevelImportJob.LastRecord.Errors));
            AssetDatabase.SaveAssets();
        }

        private static Dictionary<string, string> Snapshot(string root)
        {
            var result = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) result[file] = XRayLevelSourceRefresh.HashFile(file);
            return result;
        }

        private static XRayFloraWindState State(XRayFloraWindProfile profile, int seed)
        {
            return State(profile, seed, true);
        }

        private static XRayFloraWindState State(XRayFloraWindProfile profile, int seed, bool lastStep)
        {
            var random = new System.Random(seed);
            var state = new XRayFloraWindState { Profile = profile, TreesSetup = new Vector4(11.0f, 0.15f + (float)random.NextDouble() * 0.1f, 0.5f + (float)random.NextDouble(), 0.15f), TreeRotationPeriod = 10.0f, TreeWaveSpeed = 1.0f, TreeAmplitude = 0.02f + (float)random.NextDouble() * 0.02f, TreeWaveScale = new Vector3(0.1f, 0.01f, 0.11f) };
            var weather = new XRayFloraWindWeather((float)(random.NextDouble() * Math.PI * 2.0), 300.0f + (float)random.NextDouble() * 900.0f, 0.004f);
            state.Reset(weather);
            for (int i = 0; i < 40; i++) state.Advance(0.033f, weather);
            float last = 0.016f + (float)random.NextDouble() * 0.05f;
            if (lastStep) state.Advance(last, weather);
            return state;
        }

        private Material Reference(string source, string key)
        {
            Shader shader = Own(ShaderUtil.CreateShaderAsset(source));
            Assert.IsNotNull(shader, key);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), key + "\n" + Messages(shader));
            return Own(new Material(shader));
        }

        private Vector3[] Evaluate(Material reference, Vector3[] positions, Vector2[] uv0, Vector2[] uv3, Matrix4x4 matrix, XRayFloraWindState state, bool previous, Texture2D waves)
        {
            var result = new Vector3[positions.Length];
            reference.SetTexture("_Waves", waves);
            reference.SetMatrix("_Xform", matrix);
            reference.SetVector("_Consts", new Vector4(1.0f / QUANT, 1.0f / QUANT, 0.0f, 0.0f));
            reference.SetVector("_HemiScaleBias", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
            reference.SetVector("_WindParams", state.WindParams());
            reference.SetVector("_WindTrees", state.TreesSetup);
            reference.SetVector("_WindAnim", previous ? state.AnimPrev : state.Anim);
            reference.SetVector("_WindAnimPrev", state.AnimPrev);
            reference.SetVector("_Wind", previous ? state.TreeWindPrev : state.TreeWind);
            reference.SetVector("_Wave", previous ? state.TreeWavePrev : state.TreeWave);
            bool stagePrevious = previous && state.Profile == XRayFloraWindProfile.AnomalySsfx;
            for (int v = 0; v < positions.Length; v++)
            {
                reference.SetVector("_Vertex", positions[v]);
                reference.SetVector("_VertexData", new Vector4(0.5f, uv0[v].x, 1.0f - uv0[v].y, Mathf.Round(uv3[v].x * QUANT)));
                reference.SetFloat("_Stage", reference.shader.name.Contains("Anomaly_TreeTrunk") ? stagePrevious ? 6.0f : 5.0f : reference.shader.name.Contains("Anomaly_TreeBranch") ? stagePrevious ? 4.0f : 3.0f : 15.0f);
                Color pixel = Readback(reference);
                result[v] = new Vector3(pixel.r, pixel.g, pixel.b);
            }

            return result;
        }

        private Color Readback(Material material)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(2, 2, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
            try
            {
                Graphics.Blit(null, target, material, 0);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 2, 2), 0, 0);
                readback.Apply();
                _readbacks++;
                return readback.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        private static Mesh Grid(bool windData)
        {
            var mesh = new Mesh { name = windData ? "WindGrid" : "LegacyGrid" };
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color32>();
            var uv0 = new List<Vector2>();
            var uv3 = new List<Vector2>();
            for (int j = 0; j < GRID; j++)
            {
                for (int i = 0; i < GRID; i++)
                {
                    float u = i / (float)(GRID - 1);
                    float v = j / (float)(GRID - 1);
                    vertices.Add(new Vector3(-2.0f + 4.0f * u, 9.0f * v, 0.0f));
                    normals.Add(Vector3.back);
                    tangents.Add(new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    colors.Add(new Color32(255, 128, 128, 255));
                    uv0.Add(new Vector2(u, v));
                    uv3.Add(windData ? new Vector2(Mathf.Round((i + j) / (2.0f * (GRID - 1)) * QUANT) / QUANT, 1.0f) : Vector2.zero);
                }
            }

            var indices = new List<int>();
            for (int j = 0; j < GRID - 1; j++)
            {
                for (int i = 0; i < GRID - 1; i++)
                {
                    int a = j * GRID + i;
                    indices.AddRange(new[] { a, a + GRID, a + 1, a + 1, a + GRID, a + GRID + 1 });
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, new List<Vector2>(new Vector2[vertices.Count]));
            mesh.SetUVs(2, new List<Vector2>(new Vector2[vertices.Count]));
            mesh.SetUVs(3, uv3);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Baked(Mesh source, Vector3[] world, Matrix4x4 matrix)
        {
            Mesh mesh = UnityEngine.Object.Instantiate(source);
            Matrix4x4 inverse = matrix.inverse;
            var vertices = new Vector3[world.Length];
            for (int v = 0; v < world.Length; v++) vertices[v] = inverse.MultiplyPoint3x4(world[v]);
            mesh.vertices = vertices;
            mesh.SetUVs(3, new List<Vector2>(new Vector2[world.Length]));
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector2[] Uvs(Mesh mesh, int channel)
        {
            var list = new List<Vector2>();
            mesh.GetUVs(channel, list);
            return list.ToArray();
        }

        private static Material WindMaterial(float wind, float part, Texture2D waves, bool flip)
        {
            Material material = new Material(Shader.Find(MonolithMaterialConfigurator.SHADER_NAME));
            var albedo = new Texture2D(4, 4, TextureFormat.RGBA32, false, false);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = new Color(0.6f, 0.7f, 0.5f, 1.0f);
            albedo.SetPixels(pixels);
            albedo.Apply();
            MonolithMaterialConfigurator.Configure(material, null, albedo, null, null, true);
            material.EnableKeyword("_MONOLITH_LEVEL");
            material.SetFloat("_MonolithLevel", 1.0f);
            material.SetFloat("_DoubleSidedEnable", 1.0f);
            material.SetFloat("_CullMode", 0.0f);
            material.SetFloat("_CullModeForward", 0.0f);
            material.EnableKeyword("_DOUBLESIDED_ON");
            material.SetFloat("_MonolithFloraWind", wind);
            material.SetFloat("_MonolithFloraWindPart", part);
            material.SetTexture("_MonolithWindWaves", waves);
            material.SetFloat("_MonolithWindWavesFlipV", flip ? 1.0f : 0.0f);
            if (wind > 0.5f) material.SetShaderPassEnabled(XRayFloraWindBindings.MOTION_VECTORS_PASS, true);
            return material;
        }

        private static Texture2D Waves(bool flipped)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var random = new System.Random(77);
            var pixels = new Color[64];
            for (int i = 0; i < 64; i++) pixels[i] = new Color((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1.0f);
            if (flipped)
            {
                var rows = new Color[64];
                for (int y = 0; y < 8; y++) Array.Copy(pixels, y * 8, rows, (7 - y) * 8, 8);
                pixels = rows;
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static (int, double) ComparePositions(Color[] positions, Color[] uvs, Vector3[] expected, float pixelSize, string label)
        {
            int pixels = 0;
            double maximum = 0;
            string worst = string.Empty;
            for (int p = 0; p < positions.Length; p++)
            {
                if (!Covered(positions[p]) || !Covered(uvs[p])) continue;
                Vector3 interpolated = Interpolate(uvs[p], expected);
                double error = (new Vector3(positions[p].r, positions[p].g, positions[p].b) - interpolated).magnitude - Math.Sqrt(3.0) * HalfError(Math.Max(Math.Abs(interpolated.x), Math.Max(Math.Abs(interpolated.y), Math.Abs(interpolated.z)))) * 2.0;
                if (error > maximum) worst = " pixel " + p + " uv " + uvs[p] + " actual " + positions[p] + " expected " + interpolated.ToString("F4");
                maximum = Math.Max(maximum, error);
                pixels++;
            }

            Assert.Greater(pixels, 500, label + ": the tree must be visible.");
            Assert.LessOrEqual(maximum, pixelSize, label + ": MonolithLit forward positions reconstructed from depth and stored in the RGBA16F color buffer must lie within one pixel of the original source vertex shader surface." + worst);
            return (pixels, maximum);
        }

        private static double MicroMotionError(float actual, float expected, double tolerance)
        {
            double threshold = 0.01 / SIZE * 0.5;
            double kept = Math.Abs(actual - expected);
            double killed = Math.Abs(actual);
            if (Math.Abs(expected) < threshold - tolerance) return killed;
            if (Math.Abs(expected) <= threshold + tolerance) return Math.Min(kept, killed);
            return kept;
        }

        private static bool Covered(Color pixel)
        {
            return pixel.a > 0.5f && pixel.r < BACKGROUND * 0.5f;
        }

        private static Vector3 Interpolate(Color uv, Vector3[] vertices)
        {
            float x = Mathf.Clamp(uv.r, 0.0f, 1.0f) * (GRID - 1);
            float y = Mathf.Clamp(uv.g, 0.0f, 1.0f) * (GRID - 1);
            int i = Mathf.Min((int)x, GRID - 2);
            int j = Mathf.Min((int)y, GRID - 2);
            float fx = x - i;
            float fy = y - j;
            Vector3 a = vertices[j * GRID + i];
            Vector3 b = vertices[(j + 1) * GRID + i];
            Vector3 c = vertices[j * GRID + i + 1];
            Vector3 d = vertices[(j + 1) * GRID + i + 1];
            if (fx + fy <= 1.0f) return a + (c - a) * fx + (b - a) * fy;
            return d + (b - d) * (1.0f - fx) + (c - d) * (1.0f - fy);
        }

        private static (int, double) CompareImages(Color[] actual, Color[] expected, double tolerance, string label)
        {
            int pixels = 0;
            int coverage = 0;
            double maximum = 0;
            for (int p = 0; p < actual.Length; p++)
            {
                bool a = Covered(actual[p]);
                bool e = Covered(expected[p]);
                if (a != e)
                {
                    coverage++;
                    continue;
                }
                if (!a) continue;
                for (int c = 0; c < 3; c++) maximum = Math.Max(maximum, Math.Max(0.0, Math.Abs(actual[p][c] - expected[p][c]) - 2.0 * HalfError(expected[p][c])));
                pixels++;
            }

            Assert.LessOrEqual(coverage, Math.Max(2, pixels / 1000), label + ": coverage differs on " + coverage + " pixels.");
            Assert.LessOrEqual(maximum, tolerance, label);
            return (pixels, maximum);
        }

        private static (int, double) CompareDepth(float[] actual, float[] expected, string label)
        {
            int pixels = 0;
            int differing = 0;
            double maximum = 0;
            for (int p = 0; p < actual.Length; p++)
            {
                if (actual[p] <= 0.0f && expected[p] <= 0.0f) continue;
                double delta = Math.Abs(actual[p] - expected[p]);
                if ((actual[p] <= 0.0f) != (expected[p] <= 0.0f))
                {
                    differing++;
                    continue;
                }
                maximum = Math.Max(maximum, delta);
                pixels++;
            }

            Assert.Greater(pixels, 500, label + ": depth prepass must contain the tree.");
            Assert.LessOrEqual(differing, Math.Max(2, pixels / 1000), label + ": depth coverage differs on " + differing + " pixels.");
            Assert.LessOrEqual(maximum, 1e-5, label + ": depth prepass must use the same deformation as the forward pass.");
            return (pixels, maximum);
        }

        private static (int, int, double) CompareShadow(Color[] actual, Color[] expected)
        {
            int differing = 0;
            double maximum = 0;
            for (int p = 0; p < actual.Length; p++)
            {
                double delta = 0;
                for (int c = 0; c < 3; c++) delta = Math.Max(delta, Math.Abs(actual[p][c] - expected[p][c]));
                maximum = Math.Max(maximum, delta);
                if (delta > 0.002) differing++;
            }

            return (actual.Length, differing, maximum);
        }

        private static string _motionFailure;

        private static (int, double, double) CompareMotion(Color[] vectors, Color[] uvs, Vector3[] current, Vector3[] previous, Matrix4x4 viewProjection, Matrix4x4 previousViewProjection, string label)
        {
            int pixels = 0;
            int failed = 0;
            double maximum = 0;
            double largest = 0;
            string worst = string.Empty;
            for (int p = 0; p < vectors.Length; p++)
            {
                if (!Covered(uvs[p])) continue;
                Vector2 expected = ExpectedMotion(uvs[p], current, previous, viewProjection, previousViewProjection);
                Vector2 du = ExpectedMotion(new Color(uvs[p].r + 0.001f, uvs[p].g, 0.0f, 1.0f), current, previous, viewProjection, previousViewProjection) - expected;
                Vector2 dv = ExpectedMotion(new Color(uvs[p].r, uvs[p].g + 0.001f, 0.0f, 1.0f), current, previous, viewProjection, previousViewProjection) - expected;
                double uvLimit = HalfError(uvs[p].r) * 1000.0;
                double vLimit = HalfError(uvs[p].g) * 1000.0;
                double toleranceX = MOTION_TOLERANCE + Math.Abs(du.x) * uvLimit + Math.Abs(dv.x) * vLimit + HalfError(expected.x);
                double toleranceY = MOTION_TOLERANCE + Math.Abs(du.y) * uvLimit + Math.Abs(dv.y) * vLimit + HalfError(expected.y);
                double errorX = MicroMotionError(vectors[p].r, expected.x, toleranceX);
                double errorY = MicroMotionError(vectors[p].g, expected.y, toleranceY);
                double error = Math.Max(errorX, errorY);
                if (errorX > toleranceX || errorY > toleranceY)
                {
                    failed++;
                    worst = " pixel " + p + " uv " + uvs[p] + " actual " + vectors[p] + " expected (" + expected.x.ToString("R") + ", " + expected.y.ToString("R") + ") tolerance (" + toleranceX.ToString("R") + ", " + toleranceY.ToString("R") + ")";
                }
                maximum = Math.Max(maximum, error);
                largest = Math.Max(largest, Math.Max(Math.Abs(vectors[p].r), Math.Abs(vectors[p].g)));
                pixels++;
            }

            _motionFailure = pixels <= 500 ? label + ": motion vectors must cover the tree." : failed > 0 ? label + ": " + failed + " motion vectors differ from the current and previous source deformation beyond the HDRP micro-motion resolution (1% of a pixel) plus half-float and uv quantization." + worst : null;
            return (pixels, maximum, largest);
        }

        private static Vector2 ExpectedMotion(Color uv, Vector3[] current, Vector3[] previous, Matrix4x4 viewProjection, Matrix4x4 previousViewProjection)
        {
            Vector3 a = Interpolate(uv, current);
            Vector3 b = Interpolate(uv, previous);
            Vector4 ca = viewProjection * new Vector4(a.x, a.y, a.z, 1.0f);
            Vector4 cb = previousViewProjection * new Vector4(b.x, b.y, b.z, 1.0f);
            return new Vector2((ca.x / ca.w - cb.x / cb.w) * 0.5f, -(ca.y / ca.w - cb.y / cb.w) * 0.5f);
        }

        private static double HalfError(double value)
        {
            double magnitude = Math.Abs(value);
            if (magnitude < 6.103515625e-05) return 2.98023223876953125e-08;
            return Math.Pow(2.0, Math.Floor(Math.Log(magnitude, 2.0)) - 11.0);
        }

        private MonolithFloraWindRig CreateRig(Scene scene, List<UnityEngine.Object> temporary, bool shadows = true, bool sceneView = false)
        {
            if (!_fixtureObjects.Contains(temporary)) _fixtureObjects.Add(temporary);
            if (!_fixtureScenes.Contains(scene)) _fixtureScenes.Add(scene);
            var root = new GameObject("Wind rig");
            temporary.Add(root);
            SceneManager.MoveGameObjectToScene(root, scene);
            var rig = new MonolithFloraWindRig(root, scene, temporary, shadows, sceneView, SIZE, BACKGROUND);
            _fixtureRigs.Add(rig);
            return rig;
        }

        private T Own<T>(T item) where T : UnityEngine.Object
        {
            _owned.Add(item);
            return item;
        }

        private static string Messages(Shader shader)
        {
            var builder = new StringBuilder();
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader)) builder.AppendLine(message.message + " line " + message.line);
            return builder.ToString();
        }
    }
}
