using System;
using System.IO;
using UnityEngine;

namespace XRay.Importer.Tests
{
    internal sealed class MonolithFloraImportCaptureTarget : IDisposable
    {
        private readonly Camera _camera;
        private readonly RenderTexture _target;
        private readonly Texture2D _pixels;
        private readonly int _width;
        private readonly int _height;

        public MonolithFloraImportCaptureTarget(Camera camera, int width, int height)
        {
            _camera = camera;
            _width = width;
            _height = height;
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _pixels = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            _target.Create();
            camera.aspect = (float)width / height;
            camera.targetTexture = _target;
        }

        public Color[] Pixels()
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _pixels.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
            _pixels.Apply();
            RenderTexture.active = previous;
            return _pixels.GetPixels();
        }

        public void Save(string path)
        {
            File.WriteAllBytes(path + ".exr", _pixels.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
            var display = new Texture2D(_width, _height, TextureFormat.RGBA32, false, true);
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
