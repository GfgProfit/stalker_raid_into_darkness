using UnityEngine;
#if XRAY_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace XRay.Importer.Editor
{
    public static class XRayAnmPreviewBuilder
    {
        public const string CAMERA_SUFFIX = ".camera";

        public static GameObject Build(XRayAnmAsset asset, bool createCamera, float fieldOfView)
        {
            var root = new GameObject(asset.name);
            Animator animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (asset.HasCurves)
            {
                XRayAnmPlayer player = root.AddComponent<XRayAnmPlayer>();
                player.Asset = asset;
                player.Loop = asset.Clip != null && asset.Clip.isLooping;
                player.PlayOnEnable = true;
            }

            if (createCamera)
            {
                var cameraObject = new GameObject(asset.name + CAMERA_SUFFIX);
                cameraObject.transform.SetParent(root.transform, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.fieldOfView = fieldOfView;
                camera.nearClipPlane = 0.05f;
#if XRAY_HDRP
                cameraObject.AddComponent<HDAdditionalCameraData>();
#endif
            }

            return root;
        }
    }
}
