using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace XRay.Importer.Tests
{
    public static class HdrpTestWarmup
    {
        private const int SIZE = 16;
        private const int LAYER = 31;

        private static bool _done;

        public static void Ensure()
        {
            if (_done || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return;
            }

            var target = new RenderTexture(SIZE, SIZE, 24, RenderTextureFormat.ARGB32);
            var cameraObject = new GameObject("HDRP warm-up camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << LAYER;
                camera.targetTexture = target;
                HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
                data.volumeLayerMask = 0;
                data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                camera.Render();
                _done = true;
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
