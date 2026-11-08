using System;
using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [Serializable]
    public sealed class AnomalyLightAnimation
    {
        [SerializeField] private float _fps;
        [SerializeField] private int _frameCount;
        [SerializeField] private int[] _frames;
        [SerializeField] private Color32[] _colors;

        public bool IsValid => _fps > 0f && _frameCount > 0 && _frames != null && _colors != null && _frames.Length > 0 && _frames.Length == _colors.Length;

        public AnomalyLightAnimation(float fps, int frameCount, int[] frames, Color32[] colors)
        {
            _fps = fps;
            _frameCount = frameCount;
            _frames = frames;
            _colors = colors;
        }

        public Color Evaluate(float globalTime)
        {
            if (IsValid == false)
            {
                return Color.white;
            }
            int frame = Mathf.FloorToInt(globalTime % (_frameCount / _fps) * _fps);
            int index = 0;
            while (index + 1 < _frames.Length && frame >= _frames[index + 1])
            {
                index++;
            }
            Color32 color = _colors[index];
            if (index + 1 < _frames.Length && frame > _frames[index])
            {
                float factor = (frame - _frames[index]) / (float)(_frames[index + 1] - _frames[index]);
                Color32 next = _colors[index + 1];
                float inverse = 1f - factor;
                float scale = 1f / 255f;
                color = new Color32(Quantize(scale * color.r * inverse + scale * next.r * factor), Quantize(scale * color.g * inverse + scale * next.g * factor), Quantize(scale * color.b * inverse + scale * next.b * factor), 255);
            }
            float normalization = 1f / 255f;
            return new Color(normalization * color.r, normalization * color.g, normalization * color.b, 1f);
        }

        private static byte Quantize(float channel)
        {
            return (byte)Mathf.Clamp(Mathf.FloorToInt(channel * 255f), 0, 255);
        }
    }
}
