using System;
using System.IO;
using UnityEngine;

namespace XRay.Importer.Tests
{
    internal sealed class XRayTerrainImportCaptureTarget : IDisposable
    {
        private readonly Camera _camera;
        private readonly RenderTexture _target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        private readonly Texture2D _pixels = new Texture2D(640, 360, TextureFormat.RGBAFloat, false, true);

        public XRayTerrainImportCaptureTarget(Camera camera)
        {
            _camera = camera;
            _target.Create();
            camera.targetTexture = _target;
        }

        public Color[] Pixels()
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _pixels.ReadPixels(new Rect(0, 0, 640, 360), 0, 0);
            _pixels.Apply();
            RenderTexture.active = previous;
            return _pixels.GetPixels();
        }

        public void Save(string path)
        {
            File.WriteAllBytes(path + ".exr", _pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            var display = new Texture2D(640, 360, TextureFormat.RGBA32, false, true);
            try
            {
                display.SetPixels(_pixels.GetPixels());
                display.Apply();
                File.WriteAllBytes(path + ".png", display.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(display);
            }
        }

        public void Dispose()
        {
            _camera.targetTexture = null;
            _target.Release();
            UnityEngine.Object.DestroyImmediate(_pixels);
            UnityEngine.Object.DestroyImmediate(_target);
        }
    }
}
