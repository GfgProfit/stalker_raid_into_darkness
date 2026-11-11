using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace RaidIntoDarkness.Weapons.Optics
{
    internal sealed class ScopeOpticsCameraState
    {
        private readonly bool _enabled;
        private readonly RenderTexture _target;
        private readonly Vector3 _localPosition;
        private readonly Quaternion _localRotation;
        private readonly bool _allowHdr;
        private readonly bool _allowMsaa;
        private readonly bool _allowDynamicResolution;
        private readonly bool _orthographic;
        private readonly Rect _rect;
        private readonly float _aspect;
        private readonly float _depth;
        private readonly float _fieldOfView;
        private readonly Matrix4x4 _projection;
        private readonly Matrix4x4 _culling;
        private readonly Matrix4x4 _worldToCamera;
        private readonly bool _customProjection;
        private readonly bool _customWorldToCamera;
        private readonly bool _customCulling;
        private readonly bool _customRenderingSettings;
        private readonly Transform _volumeAnchor;
        private readonly bool _dataDynamicResolution;
        private readonly bool _xrRendering;
        private readonly bool _fullscreenPassthrough;
        private readonly bool _clearDepth;
        private readonly bool _dithering;
        private readonly HDAdditionalCameraData.AntialiasingMode _antialiasing;
        private readonly HDAdditionalCameraData.FlipYMode _flipY;

        public FrameSettings FrameSettings { get; }
        public FrameSettingsOverrideMask FrameSettingsMask { get; }

        public ScopeOpticsCameraState(Camera camera, HDAdditionalCameraData data)
        {
            _enabled = camera.enabled;
            _target = camera.targetTexture;
            _localPosition = camera.transform.localPosition;
            _localRotation = camera.transform.localRotation;
            _allowHdr = camera.allowHDR;
            _allowMsaa = camera.allowMSAA;
            _allowDynamicResolution = camera.allowDynamicResolution;
            _orthographic = camera.orthographic;
            _rect = camera.rect;
            _aspect = camera.aspect;
            _depth = camera.depth;
            _fieldOfView = camera.fieldOfView;
            _projection = camera.projectionMatrix;
            _culling = camera.cullingMatrix;
            _worldToCamera = camera.worldToCameraMatrix;
            camera.ResetProjectionMatrix();
            _customProjection = !Approximately(_projection, camera.projectionMatrix);
            camera.projectionMatrix = _projection;
            if (!_customProjection)
            {
                camera.ResetProjectionMatrix();
            }

            camera.ResetWorldToCameraMatrix();
            _customWorldToCamera = !Approximately(_worldToCamera, camera.worldToCameraMatrix);
            if (_customWorldToCamera)
            {
                camera.worldToCameraMatrix = _worldToCamera;
            }

            camera.ResetCullingMatrix();
            _customCulling = !Approximately(_culling, camera.cullingMatrix);
            if (_customCulling)
            {
                camera.cullingMatrix = _culling;
            }

            _customRenderingSettings = data.customRenderingSettings;
            _volumeAnchor = data.volumeAnchorOverride;
            _dataDynamicResolution = data.allowDynamicResolution;
            _xrRendering = data.xrRendering;
            _fullscreenPassthrough = data.fullscreenPassthrough;
            _clearDepth = data.clearDepth;
            _dithering = data.dithering;
            _antialiasing = data.antialiasing;
            _flipY = data.flipYMode;
            FrameSettings = data.renderingPathCustomFrameSettings;
            FrameSettingsMask = data.renderingPathCustomFrameSettingsOverrideMask;
        }

        public void Restore(Camera camera, HDAdditionalCameraData data)
        {
            if (camera != null)
            {
                camera.enabled = false;
                camera.targetTexture = _target;
                camera.transform.localPosition = _localPosition;
                camera.transform.localRotation = _localRotation;
                camera.allowHDR = _allowHdr;
                camera.allowMSAA = _allowMsaa;
                camera.allowDynamicResolution = _allowDynamicResolution;
                camera.orthographic = _orthographic;
                camera.rect = _rect;
                camera.aspect = _aspect;
                camera.depth = _depth;
                camera.fieldOfView = _fieldOfView;
                if (_customProjection) camera.projectionMatrix = _projection;
                else camera.ResetProjectionMatrix();
                if (_customWorldToCamera) camera.worldToCameraMatrix = _worldToCamera;
                else camera.ResetWorldToCameraMatrix();
                if (_customCulling) camera.cullingMatrix = _culling;
                else camera.ResetCullingMatrix();
                camera.enabled = _enabled;
            }

            if (data != null)
            {
                data.customRenderingSettings = _customRenderingSettings;
                data.volumeAnchorOverride = _volumeAnchor;
                data.allowDynamicResolution = _dataDynamicResolution;
                data.xrRendering = _xrRendering;
                data.fullscreenPassthrough = _fullscreenPassthrough;
                data.clearDepth = _clearDepth;
                data.dithering = _dithering;
                data.antialiasing = _antialiasing;
                data.flipYMode = _flipY;
                data.renderingPathCustomFrameSettings = FrameSettings;
                data.renderingPathCustomFrameSettingsOverrideMask = FrameSettingsMask;
            }
        }

        private static bool Approximately(Matrix4x4 left, Matrix4x4 right)
        {
            for (int index = 0; index < 16; index++)
            {
                if (Mathf.Abs(left[index] - right[index]) > 0.00001f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
