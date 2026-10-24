using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using XRay.Importer.Editor;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class LevelRendererLifecycleTests
    {
        private const int LAYER = 31;
        private const int WIDTH = 96;
        private const int HEIGHT = 48;
        private const float VERTEX_HEMI = 0.37f;
        private const string LOD_MESH_NAME = "X-Ray LOD impostors";
        private const string ENTRY_HEADER = "stage,cameraId,cameraName,cameraType,cameraAlive,cameraActive,testCamera,meshId,meshAlive,rendererId";

        private static readonly Color BACKGROUND = Color.magenta;
        private static readonly Vector4[] INSTANCE_DATA = { new Vector4(0.5f, 0.1f, 0.0f, 0.0f), new Vector4(1.5f, 0.05f, 0.0f, 0.0f) };

        [UnityTest]
        [Category("Graphics")]
        public IEnumerator StaticHemisphereIsIdenticalForSrpBatcherAndInstancing()
        {
            LogAssert.ignoreFailingMessages = true;
            var rig = new RenderRig(new Vector3(0.0f, 0.0f, -3.0f));
            Material material = CreateLevelMaterial();
            Mesh mesh = CreateQuad(VERTEX_HEMI);
            var objects = new List<GameObject>();
            bool batching = GraphicsSettings.useScriptableRenderPipelineBatching;
            var rows = new List<string> { "srpBatcher,instancingFlagLeft,instancingFlagRight,hemiLeft,hemiRight,instanceDataLeft" };
            try
            {
                foreach (float x in new[] { -0.6f, 0.6f })
                {
                    var go = new GameObject("Static hemisphere probe");
                    SceneManager.MoveGameObjectToScene(go, rig.Scene);
                    go.layer = LAYER;
                    go.transform.position = new Vector3(x, 0.0f, 0.0f);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = material;
                    objects.Add(go);
                }

                var hemi = new float[2][];
                var flags = new float[2][];
                for (int mode = 0; mode < 2; mode++)
                {
                    GraphicsSettings.useScriptableRenderPipelineBatching = mode == 0;
                    material.SetFloat("_MonolithDebug", 36.0f);
                    yield return rig.Render(4);
                    rig.Save("Static_" + mode);
                    hemi[mode] = new[] { rig.Pixel(40, 24).r, rig.Pixel(56, 24).r };
                    material.SetFloat("_MonolithDebug", 38.0f);
                    yield return rig.Render(4);
                    flags[mode] = new[] { rig.Pixel(40, 24).r, rig.Pixel(56, 24).r };
                    material.SetFloat("_MonolithDebug", 37.0f);
                    yield return rig.Render(4);
                    rows.Add($"{mode == 0},{flags[mode][0].ToString("R", CultureInfo.InvariantCulture)},{flags[mode][1].ToString("R", CultureInfo.InvariantCulture)},{hemi[mode][0].ToString("R", CultureInfo.InvariantCulture)},{hemi[mode][1].ToString("R", CultureInfo.InvariantCulture)},{rig.Pixel(40, 24).r.ToString("R", CultureInfo.InvariantCulture)}");
                }

                WriteRows("StaticHemisphere", rows);
                Assert.AreEqual(1.0f, flags[1][0], "SRP Batcher off must exercise the INSTANCING_ON variant");
                Assert.AreEqual(1.0f, flags[1][1], "SRP Batcher off must exercise the INSTANCING_ON variant");
                for (int mode = 0; mode < 2; mode++)
                {
                    Assert.AreEqual(VERTEX_HEMI, hemi[mode][0], 1e-4f, "hemisphere mode " + mode);
                    Assert.AreEqual(VERTEX_HEMI, hemi[mode][1], 1e-4f, "hemisphere mode " + mode);
                }

                Assert.AreEqual(hemi[0][0], hemi[1][0], 1e-6f, "SRP Batcher and instancing hemisphere must agree within one float ulp");
                Assert.AreEqual(hemi[0][1], hemi[1][1], 1e-6f, "SRP Batcher and instancing hemisphere must agree within one float ulp");
            }
            finally
            {
                GraphicsSettings.useScriptableRenderPipelineBatching = batching;
                foreach (GameObject go in objects)
                {
                    Object.DestroyImmediate(go);
                }

                rig.Dispose();
                DestroyLevelMaterial(material);
                Object.DestroyImmediate(mesh);
            }
        }

        [UnityTest]
        [Category("Graphics")]
        public IEnumerator InstancedHemisphereSurvivesDisableEnableAndBatchReassignment()
        {
            LogAssert.ignoreFailingMessages = true;
            var rig = new RenderRig(new Vector3(0.0f, 0.0f, -3.0f));
            Material material = CreateLevelMaterial();
            Mesh mesh = CreateQuad(VERTEX_HEMI);
            XRayInstanceBatchSet set = CreateBatchSet(mesh, material, null);
            var go = new GameObject("Instanced hemisphere renderer");
            var rows = new List<string> { "stage,hemiLeft,hemiRight,visibleBatches,visibleInstances" };
            try
            {
                SceneManager.MoveGameObjectToScene(go, rig.Scene);
                go.layer = LAYER;
                var renderer = go.AddComponent<XRayLevelInstancedRenderer>();
                renderer.BatchSet = set;
                material.SetFloat("_MonolithDebug", 36.0f);
                var stages = new List<float[]>();
                foreach (string stage in new[] { "created", "disabled", "enabled", "reassigned", "toggled" })
                {
                    if (stage == "disabled")
                    {
                        renderer.enabled = false;
                    }
                    else if (stage == "enabled")
                    {
                        renderer.enabled = true;
                    }
                    else if (stage == "reassigned")
                    {
                        renderer.BatchSet = set;
                    }
                    else if (stage == "toggled")
                    {
                        renderer.enabled = false;
                        renderer.enabled = true;
                    }

                    yield return rig.Render(4);
                    rig.Save("Instanced_" + stage);
                    float[] values = { rig.Pixel(40, 24).r, rig.Pixel(56, 24).r };
                    stages.Add(values);
                    rows.Add($"{stage},{values[0].ToString("R", CultureInfo.InvariantCulture)},{values[1].ToString("R", CultureInfo.InvariantCulture)},{renderer.LastVisibleBatches},{renderer.LastVisibleInstances}");
                    if (stage == "disabled")
                    {
                        Assert.AreEqual(BACKGROUND.r, values[0], 1e-5f, "disabled renderer must not submit draws");
                        Assert.AreEqual(BACKGROUND.r, values[1], 1e-5f, "disabled renderer must not submit draws");
                    }
                    else
                    {
                        Assert.AreEqual(1, renderer.LastVisibleBatches, stage);
                        Assert.AreEqual(2, renderer.LastVisibleInstances, stage);
                    }
                }

                WriteRows("InstancedHemisphere", rows);
                Assert.AreEqual(VERTEX_HEMI * INSTANCE_DATA[0].x + INSTANCE_DATA[0].y, stages[0][0], 1e-4f);
                Assert.AreEqual(VERTEX_HEMI * INSTANCE_DATA[1].x + INSTANCE_DATA[1].y, stages[0][1], 1e-4f);
                foreach (int stage in new[] { 2, 3, 4 })
                {
                    Assert.AreEqual(stages[0][0], stages[stage][0], "left instance after stage " + stage);
                    Assert.AreEqual(stages[0][1], stages[stage][1], "right instance after stage " + stage);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                rig.Dispose();
                Object.DestroyImmediate(set);
                DestroyLevelMaterial(material);
                Object.DestroyImmediate(mesh);
            }
        }

        [UnityTest]
        [Category("Graphics")]
        public IEnumerator LodImpostorIsDrawnOnFirstFrameAndAfterReinitialization()
        {
            LogAssert.ignoreFailingMessages = true;
            var rig = new RenderRig(new Vector3(0.0f, 0.0f, -5.0f));
            Texture2D albedo = CreateTexture(new Color(0.2f, 0.6f, 0.3f, 1.0f));
            Texture2D normal = CreateTexture(new Color(0.5f, 0.5f, 0.0f, 1.0f));
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            XRayLevelLodSet lods = null;
            XRayInstanceBatchSet set = null;
            var go = new GameObject("LOD lifecycle renderer");
            var rows = new List<string> { "stage,r,g,b,impostors,lodMeshes" };
            var entries = new List<string> { ENTRY_HEADER };
            try
            {
                typeof(XRayLevelMaterialLibrary).GetMethod("ConfigureLod", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { material, albedo, normal });
                material.SetFloat("_MonolithDebug", 9.0f);
                lods = CreateLodSet(material);
                set = CreateBatchSet(null, null, lods);
                HashSet<int> baseline = LodMeshIds();
                SceneManager.MoveGameObjectToScene(go, rig.Scene);
                go.layer = LAYER;
                var renderer = go.AddComponent<XRayLevelInstancedRenderer>();
                renderer.BatchSet = set;
                yield return rig.Render(1);
                Color background = rig.Pixel(WIDTH / 2, HEIGHT / 2);
                rows.Add($"lodOff,{background.r.ToString("R", CultureInfo.InvariantCulture)},{background.g.ToString("R", CultureInfo.InvariantCulture)},{background.b.ToString("R", CultureInfo.InvariantCulture)},{renderer.LastImpostors},{renderer.LodMeshCount}");
                AssertColor(BACKGROUND, background, 1e-5f, "nothing but the background is expected without MT_LOD");
                AssertOwnedMeshes("lodOff", renderer, baseline, entries, rig);
                Assert.AreEqual(0, renderer.LodMeshCount, "no impostor mesh may exist while MT_LOD is off");
                var colors = new List<Color>();
                foreach (string stage in new[] { "firstFrame", "reenabled", "lodToggled" })
                {
                    if (stage == "reenabled")
                    {
                        renderer.enabled = false;
                        Assert.AreEqual(0, renderer.LodMeshCount, "disabling the renderer must release impostor meshes immediately");
                        AssertOwnedMeshes("disabled", renderer, baseline, entries, rig);
                        yield return rig.Render(1);
                        AssertColor(BACKGROUND, rig.Pixel(WIDTH / 2, HEIGHT / 2), 1e-5f, "a disabled renderer must not submit impostors");
                        renderer.enabled = true;
                    }
                    else if (stage == "lodToggled")
                    {
                        renderer.LodImpostors = false;
                        yield return rig.Render(1);
                        Assert.AreEqual(0, renderer.LodMeshCount, "disabling MT_LOD must release impostor meshes");
                        AssertOwnedMeshes("lodOffAgain", renderer, baseline, entries, rig);
                    }

                    renderer.LodImpostors = true;
                    Assert.IsFalse(ShaderUtil.anythingCompiling, "shader variants must be ready before the first frame is judged");
                    yield return rig.Render(1);
                    rig.Save("Lod_" + stage);
                    Color color = rig.Pixel(WIDTH / 2, HEIGHT / 2);
                    colors.Add(color);
                    rows.Add($"{stage},{color.r.ToString("R", CultureInfo.InvariantCulture)},{color.g.ToString("R", CultureInfo.InvariantCulture)},{color.b.ToString("R", CultureInfo.InvariantCulture)},{renderer.LastImpostors},{renderer.LodMeshCount}");
                    Assert.AreEqual(1, renderer.LastImpostors, stage);
                    Dictionary<Camera, Mesh> owned = AssertOwnedMeshes(stage, renderer, baseline, entries, rig);
                    Assert.IsTrue(owned.ContainsKey(rig.Camera), stage + ": the test camera must own exactly one impostor mesh");
                }

                WriteRows("LodFirstFrame", rows);
                for (int i = 0; i < colors.Count; i++)
                {
                    AssertColor(new Color(0.2f, 0.6f, 0.3f), colors[i], 0.01f, "impostor albedo " + i);
                    AssertColor(colors[0], colors[i], 1e-6f, "impostor must match after reinitialization " + i);
                }
            }
            finally
            {
                WriteRows("LodFirstFrame_Entries", entries);
                Object.DestroyImmediate(go);
                rig.Dispose();
                if (set != null)
                {
                    Object.DestroyImmediate(set);
                }

                if (lods != null)
                {
                    Object.DestroyImmediate(lods);
                }

                Object.DestroyImmediate(material);
                Object.DestroyImmediate(albedo);
                Object.DestroyImmediate(normal);
            }
        }

        [UnityTest]
        [Category("Graphics")]
        public IEnumerator LodMeshesOfDestroyedCamerasAndDisabledRendererAreReleased()
        {
            LogAssert.ignoreFailingMessages = true;
            var rig = new RenderRig(new Vector3(0.0f, 0.0f, -5.0f));
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            Texture2D albedo = CreateTexture(new Color(0.2f, 0.6f, 0.3f, 1.0f));
            Texture2D normal = CreateTexture(new Color(0.5f, 0.5f, 0.0f, 1.0f));
            XRayLevelLodSet lods = null;
            XRayInstanceBatchSet set = null;
            var go = new GameObject("LOD resource renderer");
            RenderRig second = null;
            RenderRig third = null;
            var entries = new List<string> { ENTRY_HEADER };
            try
            {
                typeof(XRayLevelMaterialLibrary).GetMethod("ConfigureLod", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { material, albedo, normal });
                lods = CreateLodSet(material);
                set = CreateBatchSet(null, null, lods);
                HashSet<int> baseline = LodMeshIds();
                SceneManager.MoveGameObjectToScene(go, rig.Scene);
                go.layer = LAYER;
                var renderer = go.AddComponent<XRayLevelInstancedRenderer>();
                renderer.BatchSet = set;
                renderer.LodImpostors = true;
                yield return rig.Render(1);
                Assert.IsTrue(AssertOwnedMeshes("first", renderer, baseline, entries, rig).ContainsKey(rig.Camera), "the first test camera must own an impostor mesh");
                second = new RenderRig(new Vector3(0.0f, 0.0f, -5.0f));
                yield return second.Render(1);
                Dictionary<Camera, Mesh> owned = AssertOwnedMeshes("second", renderer, baseline, entries, rig, second);
                Assert.IsTrue(owned.ContainsKey(second.Camera), "the second test camera must own an impostor mesh");
                Assert.AreNotEqual(owned[rig.Camera], owned[second.Camera], "each camera needs its own impostor mesh");
                Mesh released = owned[second.Camera];
                Camera destroyed = second.Camera;
                second.Dispose();
                second = null;
                third = new RenderRig(new Vector3(0.0f, 0.0f, -5.0f));
                yield return third.Render(1);
                owned = AssertOwnedMeshes("third", renderer, baseline, entries, rig, third);
                Assert.IsTrue(owned.ContainsKey(third.Camera), "the third test camera must own an impostor mesh");
                Assert.IsFalse(owned.ContainsKey(destroyed), "the destroyed camera entry must be removed when the next camera registers");
                Assert.IsTrue(released == null, "the mesh of the destroyed camera must be destroyed");
                renderer.LodImpostors = false;
                yield return rig.Render(1);
                Assert.AreEqual(0, renderer.LodMeshCount);
                Assert.IsEmpty(AssertOwnedMeshes("lodOff", renderer, baseline, entries, rig, third), "disabling MT_LOD must release every impostor mesh");
                renderer.LodImpostors = true;
                yield return rig.Render(1);
                Assert.IsTrue(AssertOwnedMeshes("lodOn", renderer, baseline, entries, rig, third).ContainsKey(rig.Camera), "re-enabling MT_LOD must recreate the camera mesh");
                renderer.enabled = false;
                Assert.AreEqual(0, renderer.LodMeshCount);
                Assert.IsEmpty(AssertOwnedMeshes("disabled", renderer, baseline, entries, rig, third), "disabling the renderer must release every impostor mesh");
                renderer.enabled = true;
                yield return rig.Render(1);
                Assert.IsTrue(AssertOwnedMeshes("enabled", renderer, baseline, entries, rig, third).ContainsKey(rig.Camera), "re-enabling the renderer must recreate the camera mesh");
                Object.DestroyImmediate(go);
                HashSet<int> remaining = LodMeshIds();
                remaining.ExceptWith(baseline);
                entries.Add($"destroyed,summary,,,,,,0,{remaining.Count},0");
                Assert.IsEmpty(remaining, "destroying the renderer must release every impostor mesh it created");
            }
            finally
            {
                WriteRows("LodRelease_Entries", entries);
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
                second?.Dispose();
                third?.Dispose();
                rig.Dispose();
                if (set != null)
                {
                    Object.DestroyImmediate(set);
                }

                if (lods != null)
                {
                    Object.DestroyImmediate(lods);
                }

                Object.DestroyImmediate(material);
                Object.DestroyImmediate(albedo);
                Object.DestroyImmediate(normal);
            }
        }

        [Test]
        public void LodRendererDropsDestroyedCameras()
        {
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            XRayLevelLodSet set = CreateLodSet(material);
            var renderer = new XRayLevelLodRenderer();
            var cameras = new List<GameObject>();
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var cameraObject = new GameObject("LOD cleanup camera " + i, typeof(Camera));
                    cameras.Add(cameraObject);
                    Camera camera = cameraObject.GetComponent<Camera>();
                    camera.transform.position = new Vector3(0.0f, 0.0f, -5.0f);
                    renderer.Evaluate(camera, set);
                    if (i == 0)
                    {
                        Object.DestroyImmediate(cameraObject);
                    }
                }

                Assert.AreEqual(2, renderer.MeshCount);
                renderer.Dispose();
                Assert.AreEqual(0, renderer.MeshCount);
            }
            finally
            {
                renderer.Dispose();
                foreach (GameObject cameraObject in cameras)
                {
                    if (cameraObject != null)
                    {
                        Object.DestroyImmediate(cameraObject);
                    }
                }

                Object.DestroyImmediate(set);
                Object.DestroyImmediate(material);
            }
        }

        private static Material CreateLevelMaterial()
        {
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            MonolithMaterialConfigurator.Configure(material, null, CreateTexture(new Color(0.5f, 0.5f, 0.5f, 1.0f)), null, null, false);
            material.EnableKeyword("_MONOLITH_LEVEL");
            material.SetFloat("_MonolithLevel", 1.0f);
            material.SetFloat("_MonolithLevelHemiMode", 2.0f);
            material.enableInstancing = true;
            return material;
        }

        private static void DestroyLevelMaterial(Material material)
        {
            Texture texture = material.GetTexture("_BaseColorMap");
            Object.DestroyImmediate(material);
            if (texture != null)
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static void AssertColor(Color expected, Color actual, float tolerance, string message)
        {
            Assert.AreEqual(expected.r, actual.r, tolerance, message + " r");
            Assert.AreEqual(expected.g, actual.g, tolerance, message + " g");
            Assert.AreEqual(expected.b, actual.b, tolerance, message + " b");
        }

        private static Mesh CreateQuad(float hemi)
        {
            var mesh = new Mesh { name = "Lifecycle quad" };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0.0f), new Vector3(0.5f, -0.5f, 0.0f), new Vector3(-0.5f, 0.5f, 0.0f), new Vector3(0.5f, 0.5f, 0.0f) });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetTangents(new[] { new Vector4(1.0f, 0.0f, 0.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f) });
            mesh.SetUVs(0, new[] { new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.0f), new Vector2(0.0f, 1.0f), new Vector2(1.0f, 1.0f) });
            mesh.SetUVs(2, new[] { new Vector2(0.0f, hemi), new Vector2(0.0f, hemi), new Vector2(0.0f, hemi), new Vector2(0.0f, hemi) });
            mesh.SetUVs(3, new[] { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D CreateTexture(Color color)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static XRayInstanceBatchSet CreateBatchSet(Mesh mesh, Material material, XRayLevelLodSet lods)
        {
            var batches = new List<XRayInstanceBatch>();
            if (mesh != null)
            {
                Matrix4x4[] matrices = { Matrix4x4.Translate(new Vector3(-0.6f, 0.0f, 0.0f)), Matrix4x4.Translate(new Vector3(0.6f, 0.0f, 0.0f)) };
                batches.Add(new XRayInstanceBatch("Lifecycle batch", mesh, new[] { material }, new Bounds(Vector3.zero, new Vector3(3.0f, 2.0f, 2.0f)), matrices, (Vector4[])INSTANCE_DATA.Clone(), false));
            }

            var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            set.Initialize("lifecycle", 64.0f, batches, lods);
            return set;
        }

        private static XRayLevelLodSet CreateLodSet(Material material)
        {
            var normals = new Vector3[XRayLevelLodSet.FACES];
            for (int face = 0; face < normals.Length; face++)
            {
                float angle = face * 45.0f * Mathf.Deg2Rad;
                normals[face] = new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle));
            }

            Vector3[] corners = { new Vector3(-1.0f, -1.0f, 0.0f), new Vector3(1.0f, -1.0f, 0.0f), new Vector3(1.0f, 1.0f, 0.0f), new Vector3(-1.0f, 1.0f, 0.0f) };
            Vector2[] uvs = { new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.0f), new Vector2(1.0f, 1.0f), new Vector2(0.0f, 1.0f) };
            var positions = new Vector3[XRayLevelLodSet.VERTICES];
            var coordinates = new Vector2[XRayLevelLodSet.VERTICES];
            var hemi = new float[XRayLevelLodSet.VERTICES];
            var sun = new float[XRayLevelLodSet.VERTICES];
            for (int vertex = 0; vertex < XRayLevelLodSet.VERTICES; vertex++)
            {
                positions[vertex] = corners[vertex % 4];
                coordinates[vertex] = uvs[vertex % 4];
                hemi[vertex] = 1.0f / XRayLevelLodRenderer.HEMI_SCALE;
                sun[vertex] = 1.0f;
            }

            var set = ScriptableObject.CreateInstance<XRayLevelLodSet>();
            set.Initialize(material, new[] { new Vector4(0.0f, 0.0f, 0.0f, 1.0f) }, new[] { 1.0f }, positions, coordinates, hemi, sun, normals, 0.1f, 64.0f, 48.0f, 3.5f);
            return set;
        }

        private static Mesh[] FindLodMeshes()
        {
            return Array.FindAll(Resources.FindObjectsOfTypeAll<Mesh>(), mesh => mesh.name == LOD_MESH_NAME);
        }

        private static HashSet<int> LodMeshIds()
        {
            var ids = new HashSet<int>();
            foreach (Mesh mesh in FindLodMeshes())
            {
                ids.Add(mesh.GetInstanceID());
            }

            return ids;
        }

        private static Dictionary<Camera, Mesh> LodEntries(XRayLevelInstancedRenderer renderer)
        {
            object lodRenderer = typeof(XRayLevelInstancedRenderer).GetField("_lodRenderer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(renderer);
            if (lodRenderer == null)
            {
                return new Dictionary<Camera, Mesh>();
            }

            var meshes = (Dictionary<Camera, Mesh>)typeof(XRayLevelLodRenderer).GetField("_meshes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lodRenderer);
            return new Dictionary<Camera, Mesh>(meshes);
        }

        private static Dictionary<Camera, Mesh> AssertOwnedMeshes(string stage, XRayLevelInstancedRenderer renderer, HashSet<int> baseline, List<string> rows, params RenderRig[] rigs)
        {
            Dictionary<Camera, Mesh> entries = LodEntries(renderer);
            var owned = new HashSet<int>();
            foreach (XRayLevelInstancedRenderer other in Resources.FindObjectsOfTypeAll<XRayLevelInstancedRenderer>())
            {
                foreach (Mesh mesh in LodEntries(other).Values)
                {
                    if (mesh != null)
                    {
                        owned.Add(mesh.GetInstanceID());
                    }
                }
            }

            var testCameras = new HashSet<Camera>();
            foreach (RenderRig rig in rigs)
            {
                testCameras.Add(rig.Camera);
            }

            var meshIds = new HashSet<int>();
            foreach (KeyValuePair<Camera, Mesh> entry in entries)
            {
                bool alive = entry.Key != null;
                string type = alive ? entry.Key.cameraType.ToString() : "destroyed";
                string name = alive ? entry.Key.name : "destroyed";
                rows.Add($"{stage},{entry.Key.GetInstanceID()},{name},{type},{alive},{(alive && entry.Key.isActiveAndEnabled)},{testCameras.Contains(entry.Key)},{(entry.Value != null ? entry.Value.GetInstanceID() : 0)},{entry.Value != null},{renderer.GetInstanceID()}");
                Assert.IsTrue(alive, $"{stage}: impostor mesh is kept for a destroyed camera {entry.Key.GetInstanceID()}");
                Assert.IsTrue(entry.Key.cameraType != CameraType.Preview && entry.Key.cameraType != CameraType.Reflection, $"{stage}: {type} camera '{name}' must not own impostor meshes");
                Assert.IsTrue(entry.Value != null, $"{stage}: camera '{name}' references a destroyed impostor mesh");
                Assert.IsTrue(meshIds.Add(entry.Value.GetInstanceID()), $"{stage}: impostor mesh {entry.Value.GetInstanceID()} is shared by several cameras");
            }

            foreach (int id in LodMeshIds())
            {
                if (!baseline.Contains(id))
                {
                    Assert.IsTrue(owned.Contains(id), $"{stage}: impostor mesh {id} created during the test is not owned by any renderer (leak)");
                }
            }

            rows.Add($"{stage},summary,,,,,,{entries.Count},{LodMeshIds().Count - baseline.Count},{renderer.GetInstanceID()}");
            return entries;
        }

        private static void WriteRows(string name, List<string> rows)
        {
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "XRayRendererLifecycle_" + name + ".csv"), rows);
        }

        private sealed class RenderRig : IDisposable
        {
            private readonly GameObject _cameraObject;
            private readonly RenderTexture _target;
            private readonly RenderTexture _aov;
            private readonly RTHandle _buffer;
            private readonly Texture2D _readback;
            private readonly bool _asyncCompilation;
            private readonly Camera _camera;

            public RenderRig(Vector3 position)
            {
                Scene = SceneManager.GetActiveScene();
                HdrpTestWarmup.Ensure();
                _asyncCompilation = ShaderUtil.allowAsyncCompilation;
                ShaderUtil.allowAsyncCompilation = false;
                _target = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                _aov = new RenderTexture(WIDTH, HEIGHT, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                _buffer = RTHandles.Alloc(WIDTH, HEIGHT, colorFormat: GraphicsFormat.R32G32B32A32_SFloat);
                _readback = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBAFloat, false, true);
                _cameraObject = new GameObject("Lifecycle camera");
                SceneManager.MoveGameObjectToScene(_cameraObject, Scene);
                _camera = _cameraObject.AddComponent<Camera>();
                _camera.scene = Scene;
                _camera.enabled = false;
                _camera.cullingMask = 1 << LAYER;
                _camera.fieldOfView = 60.0f;
                _camera.nearClipPlane = 0.1f;
                _camera.farClipPlane = 100.0f;
                _camera.transform.position = position;
                _camera.targetTexture = _target;
                HDAdditionalCameraData data = _cameraObject.AddComponent<HDAdditionalCameraData>();
                data.volumeLayerMask = 0;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                data.backgroundColorHDR = BACKGROUND;
                data.customRenderingSettings = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
                AOVRequest request = AOVRequest.NewDefault();
                request.SetOverrideRenderFormat(true);
                data.SetAOVRequests(new AOVRequestBuilder().Add(request, id => _buffer, null, new[] { AOVBuffers.Color }, (cmd, textures, properties) => cmd.Blit(textures[0], _aov)).Build());
            }

            public Scene Scene { get; }
            public Camera Camera => _camera;

            public IEnumerator Render(int frames)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    yield return null;
                    _camera.Render();
                }

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _aov;
                _readback.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
                _readback.Apply();
                RenderTexture.active = previous;
            }

            public void Save(string name)
            {
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "XRayRendererLifecycle_" + name + ".exr"), _readback.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            }

            public Color Pixel(int x, int y)
            {
                return _readback.GetPixel(x, y);
            }

            public void Dispose()
            {
                ShaderUtil.allowAsyncCompilation = _asyncCompilation;
                if (_cameraObject != null)
                {
                    _cameraObject.GetComponent<HDAdditionalCameraData>().SetAOVRequests(null);
                    _camera.targetTexture = null;
                    Object.DestroyImmediate(_cameraObject);
                }

                _buffer.Release();
                _target.Release();
                _aov.Release();
                Object.DestroyImmediate(_target);
                Object.DestroyImmediate(_aov);
                Object.DestroyImmediate(_readback);
            }
        }
    }
}
