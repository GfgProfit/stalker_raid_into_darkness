using System;
using UnityEngine;

namespace XRay.Importer.Tests
{
    internal sealed class TrueStalkerTerrainPixelTarget : IDisposable
    {
        private readonly Camera _camera;
        private readonly RenderTexture _target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        private readonly Texture2D _pixels = new Texture2D(640, 360, TextureFormat.RGBAFloat, false, true);

        public TrueStalkerTerrainPixelTarget(Camera camera)
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

        public void Dispose()
        {
            _camera.targetTexture = null;
            _target.Release();
            UnityEngine.Object.DestroyImmediate(_pixels);
            UnityEngine.Object.DestroyImmediate(_target);
        }
    }
}
