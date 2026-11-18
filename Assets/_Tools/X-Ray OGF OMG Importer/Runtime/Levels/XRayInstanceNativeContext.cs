using UnityEngine;

namespace XRay.Importer.Levels
{
    public sealed class XRayInstanceNativeContext
    {
        public bool Supports(Camera camera)
        {
            return camera != null && camera.cameraType == CameraType.Game && !camera.stereoEnabled;
        }
    }
}
