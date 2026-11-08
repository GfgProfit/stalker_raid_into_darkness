using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerAnomalyAudio : MonoBehaviour
    {
        private const float MINIMUM_DISTANCE = 1f;
        private const float MAXIMUM_DISTANCE = 5f;
        private const float DISTANCE_ROLLOFF = 0.75f;
        private const int ROLLOFF_INTERVALS = 128;

        [SerializeField] private AudioClip[] _painClips;

        private AudioSource _shockSource;
        private AudioSource _thermalSource;
        private Vector3 _shockPosition;
        private Vector3 _thermalPosition;

        public void Configure(AudioClip[] painClips)
        {
            Stop(_shockSource);
            Stop(_thermalSource);
            _painClips = painClips;
        }

        public void PlayPain(bool shock, Vector3 position)
        {
            if (isActiveAndEnabled == false || _painClips == null || _painClips.Length == 0)
            {
                return;
            }

            EnsureSources();
            AudioSource source = shock ? _shockSource : _thermalSource;
            if (source.isPlaying)
            {
                return;
            }

            AudioClip clip = _painClips[Random.Range(0, _painClips.Length)];
            if (clip == null)
            {
                return;
            }

            if (shock)
            {
                _shockPosition = position;
            }
            else
            {
                _thermalPosition = position;
            }
            source.clip = clip;
            source.transform.position = position;
            source.Play();
        }

        private void Awake()
        {
            EnsureSources();
        }

        private void OnDisable()
        {
            Stop(_shockSource);
            Stop(_thermalSource);
        }

        private void OnDestroy()
        {
            DestroySource(_shockSource);
            DestroySource(_thermalSource);
        }

        private void LateUpdate()
        {
            KeepPosition(_shockSource, _shockPosition);
            KeepPosition(_thermalSource, _thermalPosition);
        }

        private void EnsureSources()
        {
            if (_shockSource == null)
            {
                _shockSource = CreateSource("ShockPain");
            }
            if (_thermalSource == null)
            {
                _thermalSource = CreateSource("ThermalPain");
            }
        }

        private AudioSource CreateSource(string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.volume = 1f;
            source.pitch = 1f;
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.spread = 0f;
            source.minDistance = MINIMUM_DISTANCE;
            source.maxDistance = MAXIMUM_DISTANCE;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, CreateRolloff());
            return source;
        }

        private static AnimationCurve CreateRolloff()
        {
            Keyframe[] keys = new Keyframe[ROLLOFF_INTERVALS + 2];
            keys[0] = new Keyframe(0f, 1f);
            for (int index = 0; index <= ROLLOFF_INTERVALS; index++)
            {
                float distance = MINIMUM_DISTANCE * Mathf.Pow(MAXIMUM_DISTANCE / MINIMUM_DISTANCE, index / (float)ROLLOFF_INTERVALS);
                float linear = Mathf.Clamp01((MAXIMUM_DISTANCE - distance) / (MAXIMUM_DISTANCE - MINIMUM_DISTANCE));
                float inverse = MINIMUM_DISTANCE / (MINIMUM_DISTANCE + DISTANCE_ROLLOFF * (Mathf.Clamp(distance, MINIMUM_DISTANCE, MAXIMUM_DISTANCE) - MINIMUM_DISTANCE));
                keys[index + 1] = new Keyframe(distance / MAXIMUM_DISTANCE, linear * inverse);
            }
            for (int index = 0; index < keys.Length; index++)
            {
                Keyframe key = keys[index];
                key.inTangent = index > 0 ? (key.value - keys[index - 1].value) / (key.time - keys[index - 1].time) : 0f;
                key.outTangent = index < keys.Length - 1 ? (keys[index + 1].value - key.value) / (keys[index + 1].time - key.time) : 0f;
                keys[index] = key;
            }
            return new AnimationCurve(keys);
        }

        private static void KeepPosition(AudioSource source, Vector3 position)
        {
            if (source != null && source.isPlaying)
            {
                source.transform.position = position;
            }
        }

        private static void Stop(AudioSource source)
        {
            if (source != null)
            {
                source.Stop();
                source.clip = null;
            }
        }

        private static void DestroySource(AudioSource source)
        {
            if (source != null)
            {
                Destroy(source.gameObject);
            }
        }
    }
}
