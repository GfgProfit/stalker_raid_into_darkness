using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Tests
{
    public sealed class MonolithNormalBufferTests
    {
        [TestCase(MotionVectorGenerationMode.Camera, false)]
        [TestCase(MotionVectorGenerationMode.Camera, true)]
        [TestCase(MotionVectorGenerationMode.Object, false)]
        [TestCase(MotionVectorGenerationMode.Object, true)]
        public void ForwardOnlyWritesNormalsInDeferred(MotionVectorGenerationMode motion, bool top)
        {
            var root = new GameObject("Monolith normal buffer regression");
            root.hideFlags = HideFlags.HideAndDontSave;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf);
            var pixels = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                root.layer = 31;
                root.transform.position = new Vector3(10000, 10000, 10000);
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(root.transform, false);
                cube.layer = 31;
                Renderer renderer = cube.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.motionVectorGenerationMode = motion;
                material.SetFloat("_MonolithDebug", 68);
                material.SetFloat("_MonolithHasBump", 0);
                Volume volume = root.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10000;
                volume.sharedProfile = profile;
                Exposure exposure = profile.Add<Exposure>(true);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(0);
                profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
                var cameraObject = new GameObject("Normal buffer camera");
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << 31;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 10;
                camera.targetTexture = target;
                camera.transform.position = root.transform.position + (top ? Vector3.up : Vector3.left) * 3;
                camera.transform.rotation = Quaternion.LookRotation(root.transform.position - camera.transform.position);
                HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
                data.volumeLayerMask = 1 << 31;
                data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
                data.customRenderingSettings = true;
                data.renderingPathCustomFrameSettings.litShaderMode = LitShaderMode.Deferred;
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.LitShaderMode] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.MotionVectors, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.MotionVectors] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.ObjectMotionVectors, true);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.ObjectMotionVectors] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.ExposureControl, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.ExposureControl] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
                data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.AtmosphericScattering, false);
                data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.AtmosphericScattering] = true;
                camera.Render();
                camera.Render();
                Assert.AreEqual(LitShaderMode.Deferred, HDCamera.GetOrCreate(camera).frameSettings.litShaderMode);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                pixels.Apply();
                Color actual = pixels.GetPixel(32, 32);
                Vector3 normal = top ? Vector3.up : Vector3.left;
                Assert.That(actual.r, Is.EqualTo(normal.x * 0.5f + 0.5f).Within(0.025f));
                Assert.That(actual.g, Is.EqualTo(normal.y * 0.5f + 0.5f).Within(0.025f));
                Assert.That(actual.b, Is.EqualTo(normal.z * 0.5f + 0.5f).Within(0.025f));
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(root);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(material);
                foreach (VolumeComponent component in profile.components) Object.DestroyImmediate(component);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
