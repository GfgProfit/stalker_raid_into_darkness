using UnityEngine;

namespace RaidIntoDarkness.Effects
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AnomalyAudioGain : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _gain = 1f;

        public float Gain
        {
            get => _gain;
            set => _gain = Mathf.Max(0f, value);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float gain = _gain;
            Apply(data, gain);
        }

        public static void Apply(float[] data, float gain)
        {
            if (data == null || gain == 1f)
            {
                return;
            }
            for (int index = 0; index < data.Length; index++)
            {
                data[index] *= gain;
            }
        }
    }
}
