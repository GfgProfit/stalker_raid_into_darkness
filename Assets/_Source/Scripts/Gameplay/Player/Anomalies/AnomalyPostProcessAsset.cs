using UnityEngine;

namespace RaidIntoDarkness.Player
{
    public sealed class AnomalyPostProcessAsset : ScriptableObject
    {
        public const int BASE_RED = 0;
        public const int BASE_GREEN = 1;
        public const int BASE_BLUE = 2;
        public const int ADD_RED = 3;
        public const int ADD_GREEN = 4;
        public const int ADD_BLUE = 5;
        public const int GRAY_RED = 6;
        public const int GRAY_GREEN = 7;
        public const int GRAY_BLUE = 8;
        public const int GRAY = 9;
        public const int BLUR = 10;
        public const int DUALITY_HORIZONTAL = 11;
        public const int DUALITY_VERTICAL = 12;
        public const int NOISE_INTENSITY = 13;
        public const int NOISE_GRAIN = 14;
        public const int NOISE_FPS = 15;
        public const int COLOR_MAP_INFLUENCE = 16;
        public const int CHANNEL_COUNT = 17;

        [SerializeField] private string _sourceFile;
        [SerializeField] private float _duration;
        [SerializeField] private AnimationCurve[] _channels = new AnimationCurve[CHANNEL_COUNT];
        [SerializeField] private Texture2D _colorMap;

        public string SourceFile => _sourceFile;
        public float Duration => _duration;
        public Texture2D ColorMap => _colorMap;

        public void SetImportData(string sourceFile, float duration, AnimationCurve[] channels, Texture2D colorMap)
        {
            _sourceFile = sourceFile;
            _duration = duration;
            _channels = channels;
            _colorMap = colorMap;
        }

        public float Evaluate(int channel, float time)
        {
            if (_channels == null || channel < 0 || channel >= _channels.Length || _channels[channel] == null || _channels[channel].length == 0)
            {
                return channel == NOISE_GRAIN ? 1.0f : channel == NOISE_FPS ? 30.0f : 0.0f;
            }

            return _channels[channel].Evaluate(_duration > 0.0f ? Mathf.Repeat(time, _duration) : 0.0f);
        }
    }
}
