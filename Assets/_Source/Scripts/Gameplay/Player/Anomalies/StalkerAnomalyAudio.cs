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
        [SerializeField] private AudioClip[] _strikeClips;

        private AudioSource _shockSource;
        private AudioSource _thermalSource;
        private AudioSource _chemicalSource;
        private AudioSource _strikeSource;
        private Vector3 _shockPosition;
        private Vector3 _thermalPosition;
        private Vector3 _chemicalPosition;
        private Vector3 _strikePosition;

        public void Configure(AudioClip[] painClips)
        {
            Stop(_shockSource);
            Stop(_thermalSource);
            Stop(_chemicalSource);
            _painClips = painClips;
        }

        public void PlayPain(bool shock, Vector3 position)
        {
            PlayPain(shock ? AnomalyInfluenceKind.Electric : AnomalyInfluenceKind.Thermal, position);
        }

        private void PlayPain(AnomalyInfluenceKind kind, Vector3 position)
        {
            if (isActiveAndEnabled == false || _painClips == null || _painClips.Length == 0)
            {
                return;
            }

            EnsureSources();
            AudioSource source = kind == AnomalyInfluenceKind.Chemical ? _chemicalSource : kind == AnomalyInfluenceKind.Thermal ? _thermalSource : _shockSource;
            if (source.isPlaying)
            {
                return;
            }

            AudioClip clip = _painClips[Random.Range(0, _painClips.Length)];
            if (clip == null)
            {
                return;
            }

            if (kind == AnomalyInfluenceKind.Chemical)
            {
                _chemicalPosition = position;
            }
            else if (kind != AnomalyInfluenceKind.Thermal)
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

        public void PlayReaction(AnomalyInfluenceKind kind, Vector3 position)
        {
            if (kind != AnomalyInfluenceKind.Vortex)
            {
                PlayPain(kind, position);
                return;
            }
            if (isActiveAndEnabled == false || _strikeClips == null || _strikeClips.Length == 0)
            {
                return;
            }
            EnsureSources();
            if (_strikeSource.isPlaying)
            {
                return;
            }
            AudioClip clip = _strikeClips[Random.Range(0, _strikeClips.Length)];
            if (clip != null)
            {
                _strikePosition = position;
                _strikeSource.clip = clip;
                _strikeSource.transform.position = position;
                _strikeSource.Play();
            }
        }

        private void Awake()
        {
            EnsureSources();
        }

        private void OnDisable()
        {
            Stop(_shockSource);
            Stop(_thermalSource);
            Stop(_chemicalSource);
            Stop(_strikeSource);
        }

        private void OnDestroy()
        {
            DestroySource(_shockSource);
            DestroySource(_thermalSource);
            DestroySource(_chemicalSource);
            DestroySource(_strikeSource);
        }

        private void LateUpdate()
        {
            KeepPosition(_shockSource, _shockPosition);
            KeepPosition(_thermalSource, _thermalPosition);
            KeepPosition(_chemicalSource, _chemicalPosition);
            KeepPosition(_strikeSource, _strikePosition);
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
            if (_chemicalSource == null)
            {
                _chemicalSource = CreateSource("ChemicalPain");
            }
            if (_strikeSource == null && _strikeClips != null && _strikeClips.Length > 0)
            {
                _strikeSource = CreateSource("StrikePain", 0.5f, 10f);
            }
        }

        private AudioSource CreateSource(string name, float minimum = MINIMUM_DISTANCE, float maximum = MAXIMUM_DISTANCE)
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
            source.minDistance = minimum;
            source.maxDistance = maximum;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, CreateRolloff(minimum, maximum));
            return source;
        }

        private static AnimationCurve CreateRolloff(float minimum, float maximum)
        {
            Keyframe[] keys = new Keyframe[ROLLOFF_INTERVALS + 2];
            keys[0] = new Keyframe(0f, 1f);
            for (int index = 0; index <= ROLLOFF_INTERVALS; index++)
            {
                float distance = minimum * Mathf.Pow(maximum / minimum, index / (float)ROLLOFF_INTERVALS);
                float linear = Mathf.Clamp01((maximum - distance) / (maximum - minimum));
                float inverse = minimum / (minimum + DISTANCE_ROLLOFF * (Mathf.Clamp(distance, minimum, maximum) - minimum));
                keys[index + 1] = new Keyframe(distance / maximum, linear * inverse);
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
