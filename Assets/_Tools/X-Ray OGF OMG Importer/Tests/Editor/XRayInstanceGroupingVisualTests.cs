using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstanceGroupingVisualTests
    {
        private const int SIZE = 256;

        [Test]
        public void OffscreenCastersKeepVisibleShadowsAcrossCameraChanges()
        {
            HdrpTestWarmup.Ensure();
            string output = Environment.GetEnvironmentVariable("RVM_BATCHING_VALIDATION_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = @"E:\Projects\stalker_raid_into_darkness_Archive\InstanceGroupingValidation";
            output = Path.Combine(Path.GetFullPath(output), DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));
            Assert.That(output, Does.StartWith("E:\\Projects\\stalker_raid_into_darkness_Archive\\"));
            Directory.CreateDirectory(output);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var owned = new List<UnityEngine.Object>();
            RenderTexture initial = RenderTexture.active;
            try
            {
                var root = new GameObject("Grouping shadow fixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                var camera = new GameObject("Grouping shadow camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform);
                camera.scene = scene;
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 3f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 100f;
                camera.aspect = 1f;
                camera.useOcclusionCulling = false;
                var data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                data.backgroundColorHDR = Color.black;
                data.customRenderingSettings = true;
                foreach (var field in new[] { FrameSettingsField.SSR, FrameSettingsField.SSAO, FrameSettingsField.SSGI, FrameSettingsField.Volumetrics, FrameSettingsField.Postprocess, FrameSettingsField.ExposureControl })
                {
                    data.renderingPathCustomFrameSettings.SetEnabled(field, false);
                    data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)field] = true;
                }
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.ShadowMaps, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.ShadowMaps] = true;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                owned.Add(profile);
                profile.Add<Fog>(true).enabled.Override(false);
                var exposure = profile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(0f);
                var volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000;
                volume.sharedProfile = profile;
                var light = new GameObject("Grouping shadow sun").AddComponent<Light>();
                light.transform.SetParent(root.transform);
                light.type = LightType.Directional;
                light.shadows = LightShadows.Hard;
                light.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, -1f, 0f));
                light.gameObject.AddComponent<HDAdditionalLightData>().SetIntensity(3f, LightUnit.Lux);
                var material = new Material(Shader.Find("HDRP/Lit")) { enableInstancing = true };
                owned.Add(material);
                material.SetColor("_BaseColor", Color.gray);
                var receiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(receiver, scene);
                receiver.transform.SetParent(root.transform);
                receiver.transform.position = new Vector3(0f, -0.05f, 0f);
                receiver.transform.localScale = new Vector3(12f, 0.1f, 12f);
                receiver.GetComponent<MeshRenderer>().sharedMaterial = material;
                var prototype = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh mesh = prototype.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(prototype);
                var batches = new List<XRayInstanceBatch>();
                foreach (float x in new[] { 4f, 4.5f })
                {
                    Vector3 position = new Vector3(x, 3f, 0f);
                    batches.Add(new XRayInstanceBatch("caster", mesh, new[] { material }, new Bounds(position, Vector3.one), new[] { Matrix4x4.Translate(position) }, new[] { Vector4.one }, true));
                }
                var prepared = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
                owned.Add(prepared);
                prepared.Initialize("shadow fixture", 128f, batches);
                XRayInstancePreparation.Ensure(prepared);
                Assert.That(prepared.SubmissionGroups.Count, Is.EqualTo(1));
                var legacy = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
                owned.Add(legacy);
                legacy.Initialize("legacy", 128f, batches);
                legacy.PrepareMatrices("legacy");
                var renderer = root.AddComponent<XRayLevelInstancedRenderer>();
                var target = new RenderTexture(SIZE, SIZE, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                owned.Add(target);
                target.Create();
                camera.targetTexture = target;
                var pixels = new Texture2D(SIZE, SIZE, TextureFormat.RGBAFloat, false, true);
                owned.Add(pixels);
                for (int view = 0; view < 2; view++)
                {
                    camera.transform.position = new Vector3(view * 0.25f, 8f, -8f);
                    camera.transform.LookAt(new Vector3(0f, 0f, view * 0.5f));
                    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
                    foreach (var batch in batches) Assert.That(GeometryUtility.TestPlanesAABB(planes, batch.Bounds), Is.False);
                    renderer.BatchSet = legacy;
                    Color[] before = Capture(camera, target, pixels, Path.Combine(output, "view" + view + "_A.png"));
                    Assert.That(renderer.LastSubmittedCalls, Is.EqualTo(2));
                    renderer.BatchSet = prepared;
                    Color[] after = Capture(camera, target, pixels, Path.Combine(output, "view" + view + "_B.png"));
                    Assert.That(renderer.LastSubmittedCalls, Is.EqualTo(1));
                    renderer.enabled = false;
                    Color[] clear = Capture(camera, target, pixels, null);
                    renderer.enabled = true;
                    int changed = 0;
                    int shadowPixels = 0;
                    float maximum = 0f;
                    for (int index = 0; index < before.Length; index++)
                    {
                        float error = Error(before[index], after[index]);
                        maximum = Mathf.Max(maximum, error);
                        if (error > 0.001f) changed++;
                        if (Error(before[index], clear[index]) > 0.01f) shadowPixels++;
                    }
                    File.WriteAllText(Path.Combine(output, "view" + view + ".json"), "{\"changedPixels\":" + changed + ",\"shadowPixels\":" + shadowPixels + ",\"maximumError\":" + maximum.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ",\"pixels\":" + before.Length + "}");
                    Assert.That(shadowPixels, Is.GreaterThan(100));
                    Assert.That(changed, Is.LessThan(before.Length * 0.005f));
                    Assert.That(maximum, Is.LessThan(0.02f));
                }
            }
            finally
            {
                RenderTexture.active = initial;
                EditorSceneManager.ClosePreviewScene(scene);
                for (int index = owned.Count - 1; index >= 0; index--) UnityEngine.Object.DestroyImmediate(owned[index]);
            }
        }

        private static Color[] Capture(Camera camera, RenderTexture target, Texture2D pixels, string path)
        {
            var renderCamera = UnityEngine.Object.Instantiate(camera.gameObject, camera.transform.parent).GetComponent<Camera>();
            renderCamera.scene = camera.scene;
            renderCamera.targetTexture = target;
            try
            {
                renderCamera.Render();
                renderCamera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
                pixels.Apply();
                RenderTexture.active = previous;
                Color[] result = pixels.GetPixels();
                if (path != null)
                {
                    var preview = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false, true);
                    try
                    {
                        preview.SetPixels(result);
                        preview.Apply();
                        File.WriteAllBytes(path, preview.EncodeToPNG());
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(preview);
                    }
                }
                return result;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(renderCamera.gameObject);
            }
        }

        private static float Error(Color first, Color second)
        {
            return Mathf.Max(Mathf.Abs(first.r - second.r), Mathf.Max(Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b)));
        }
    }
}
