using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace RaidIntoDarkness.Player
{
    [Serializable]
    public sealed class StalkerAnomalyScreenPass : CustomPass
    {
        public const string SOURCE_PROPERTY = "_AnomalyScreenSource";

        [SerializeField] private Material _material;
        [SerializeField] private Camera _camera;

        private RTHandle _copy;

        public Material Material
        {
            get => _material;
            set => _material = value;
        }
        public Camera Camera
        {
            get => _camera;
            set => _camera = value;
        }

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            _copy = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, useDynamicScale: true, name: nameof(StalkerAnomalyScreenPass));
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (_material == null || _copy == null || _camera == null || ctx.hdCamera.camera != _camera)
            {
                return;
            }

            CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, _copy);
            ctx.propertyBlock.SetTexture(SOURCE_PROPERTY, _copy);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, _material, ctx.propertyBlock, 0);
        }

        protected override void Cleanup()
        {
            if (_copy != null)
            {
                _copy.Release();
                _copy = null;
            }
        }
    }
}
