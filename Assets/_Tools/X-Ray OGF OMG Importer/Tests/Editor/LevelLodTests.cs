using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class LevelLodTests
    {
        [Test]
        public void LodStatesFollowOriginalScreenSpaceThresholds()
        {
            var cameraObject = new GameObject("LOD test camera", typeof(Camera));
            var target = new RenderTexture(1920, 1080, 0);
            var set = ScriptableObject.CreateInstance<XRayLevelLodSet>();
            var renderer = new XRayLevelLodRenderer();
            try
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.targetTexture = target;
                camera.fieldOfView = 67.5f;
                camera.farClipPlane = 2000.0f;
                float[] distances = { 100.0f, 160.0f, 400.0f, 1000.0f };
                int count = distances.Length;
                var normals = new Vector3[count * XRayLevelLodSet.FACES];
                for (int i = 0; i < normals.Length; i++)
                {
                    float angle = (i % XRayLevelLodSet.FACES) * 45.0f * Mathf.Deg2Rad;
                    normals[i] = new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle));
                }

                int vertices = count * XRayLevelLodSet.VERTICES;
                set.Initialize(null, distances.Select(distance => new Vector4(0.0f, 0.0f, distance, 5.0f)).ToArray(), Enumerable.Repeat(1.0f, count).ToArray(), new Vector3[vertices], new Vector2[vertices], new float[vertices], new float[vertices], normals, 0.5f, 64.0f, 48.0f, 3.5f);
                renderer.Evaluate(camera, set);
                CollectionAssert.AreEqual(new[] { XRayLevelLodRenderer.STATE_FULL, XRayLevelLodRenderer.STATE_BOTH, XRayLevelLodRenderer.STATE_IMPOSTOR, XRayLevelLodRenderer.STATE_DISCARD }, renderer.States);
                Assert.AreEqual(2, renderer.LastImpostors);
                Assert.AreEqual(1, renderer.LastDiscarded);

                FieldInfo meshesField = typeof(XRayLevelLodRenderer).GetField("_meshes", BindingFlags.Instance | BindingFlags.NonPublic);
                var meshes = (Dictionary<Camera, Mesh>)meshesField.GetValue(renderer);
                Mesh mesh = meshes[camera];
                Vector3 firstVertex = mesh.vertices[0];
                renderer.Evaluate(camera, set);
                Assert.AreEqual(firstVertex, mesh.vertices[0]);
                camera.transform.position = Vector3.right;
                renderer.Evaluate(camera, set);
                Assert.AreEqual(2, renderer.LastImpostors);
                Assert.AreNotEqual(firstVertex, mesh.vertices[0]);
                camera.transform.position = Vector3.zero;

                set.Spheres[2].x = 1000.0f;
                for (int vertex = 3 * XRayLevelLodSet.VERTICES; vertex < vertices; vertex++) set.Positions[vertex] = new Vector3((vertex & 1) == 0 ? -1.0f : 1.0f, (vertex & 2) == 0 ? -1.0f : 1.0f, distances[3]);
                var owners = new[] { true, false, true, true };
                MethodInfo automatic = typeof(XRayLevelLodRenderer).GetMethod(nameof(XRayLevelLodRenderer.Evaluate), BindingFlags.Instance | BindingFlags.NonPublic);
                automatic.Invoke(renderer, new object[] { camera, set, owners, null, 0.0f, null });
                CollectionAssert.AreEqual(new[] { XRayLevelLodRenderer.STATE_FULL, XRayLevelLodRenderer.STATE_FULL, XRayLevelLodRenderer.STATE_FULL, XRayLevelLodRenderer.STATE_IMPOSTOR }, renderer.States);
                Assert.AreEqual(1, renderer.LastImpostors);
                Assert.AreEqual(0, renderer.LastDiscarded);
            }
            finally
            {
                renderer.Dispose();
                Object.DestroyImmediate(set);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void LodMaterialUsesOriginalAtlasPathWithoutInstancing()
        {
            string albedoPath = "Assets/XRayLevelLodAlbedo_" + Guid.NewGuid().ToString("N") + ".asset";
            string normalPath = "Assets/XRayLevelLodNormal_" + Guid.NewGuid().ToString("N") + ".asset";
            var albedo = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            var normal = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Material material = null;
            try
            {
                AssetDatabase.CreateAsset(albedo, albedoPath);
                AssetDatabase.CreateAsset(normal, normalPath);
                material = new Material(Shader.Find("HDRP/MonolithLit"));
                typeof(XRayLevelMaterialLibrary).GetMethod("ConfigureLod", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { material, albedo, normal });
                Assert.IsTrue(material.IsKeywordEnabled("_MONOLITH_LEVEL_LOD"));
                Assert.IsFalse(material.IsKeywordEnabled("_MONOLITH_LEVEL"));
                Assert.IsFalse(material.IsKeywordEnabled("_MONOLITH_LEVEL_DETAIL"));
                Assert.IsTrue(material.IsKeywordEnabled("_ALPHATEST_ON"));
                Assert.AreEqual(0.0f, material.GetFloat("_AlphaCutoff"));
                Assert.AreEqual(0.0f, material.GetFloat("_MonolithHasBump"));
                Assert.AreEqual(XRayLevelMaterialLibrary.DEF_GLOSS, material.GetFloat("_MonolithFallbackGloss"));
                Assert.AreEqual(albedoPath, AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap")));
                Assert.AreEqual(normalPath, AssetDatabase.GetAssetPath(material.GetTexture("_MonolithBump")));
                Assert.IsFalse(material.enableInstancing);
            }
            finally
            {
                if (material != null)
                {
                    Object.DestroyImmediate(material);
                }

                AssetDatabase.DeleteAsset(albedoPath);
                AssetDatabase.DeleteAsset(normalPath);
            }
        }

        [Test]
        [Category("RealData")]
        public void OriginalEscapeLodFactorsMatchFlodCorrection()
        {
            const string SOURCE = "E:/True Stalker Unpacked Patch 1.5/levels/escape/level";
            Assume.That(File.Exists(SOURCE));
            XRayLevelData level = XRayLevelReader.Read(File.ReadAllBytes(SOURCE), SOURCE);
            MethodInfo factor = typeof(XRayLevelImportJob).GetMethod("LodFactor", BindingFlags.NonPublic | BindingFlags.Static);
            int lods = 0;
            foreach (XRayLevelVisual visual in level.Visuals.Where(visual => visual.LodVertices != null))
            {
                float value = (float)factor.Invoke(null, new object[] { visual });
                Assert.That(value, Is.GreaterThan(0.0f).And.LessThanOrEqualTo(1.0f + 1e-5f), visual.Index.ToString());
                foreach (int child in visual.Children)
                {
                    Assert.IsTrue(level.Visuals[child].IsTree, visual.Index + " -> " + child);
                }

                lods++;
            }

            Assert.AreEqual(4102, lods);
        }
    }
}
