using UnityEngine;
using UnityEngine.Rendering;

namespace XRay.Importer.Levels
{
    [ExecuteAlways]
    public sealed class XRayWetnessReflectionRelease : MonoBehaviour
    {
        private RTHandle _color;
        private RTHandle _depth;

        public void Initialize(RTHandle color, RTHandle depth)
        {
            _color = color;
            _depth = depth;
        }

        private void OnDestroy()
        {
            _color?.Release();
            _depth?.Release();
            _color = null;
            _depth = null;
        }
    }
}
