using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Scripting;

namespace RaidIntoDarkness.GraphicsOptions
{
    [Preserve]
    public sealed class VideoSharpening : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        private static readonly int _strengthId = Shader.PropertyToID("_VideoSharpenStrength");
        private static readonly int _pixelSizeId = Shader.PropertyToID("_VideoSharpenPixelSize");

        [SerializeField] private ClampedFloatParameter _strength = new(0.0f, 0.0f, 1.0f);

        private Material _material;

        internal ClampedFloatParameter Strength => _strength;
        public override bool visibleInSceneView => false;

        public override void Setup()
        {
            Material source = Resources.Load<Material>("VideoSharpeningMaterial");
            Shader shader = source != null ? source.shader : Shader.Find("Hidden/RaidIntoDarkness/VideoSharpening");
            if (shader != null) _material = CoreUtils.CreateEngineMaterial(shader);
        }

        public bool IsActive()
        {
            return _material != null && _strength.value > 0.0f;
        }

        public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
        {
            _material.SetFloat(_strengthId, _strength.value);
            _material.SetVector(_pixelSizeId, new Vector4(1.0f / source.rt.width, 1.0f / source.rt.height, 0.0f, 0.0f));
            HDUtils.BlitCameraTexture(cmd, source, destination, _material, 0);
        }

        public override void Cleanup()
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }
    }
}
