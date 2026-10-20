using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [Serializable]
    public sealed class StalkerCollideSounds
    {
        [SerializeField] private string _objectMaterial;
        [SerializeField] private StalkerSurfaceType _surface;
        [SerializeField] private string _origin;
        [SerializeField] private List<AudioClip> _clips = new List<AudioClip>();

        public StalkerCollideSounds(string objectMaterial, StalkerSurfaceType surface, string origin, List<AudioClip> clips)
        {
            _objectMaterial = objectMaterial;
            _surface = surface;
            _origin = origin;
            _clips = clips ?? new List<AudioClip>();
        }

        public string ObjectMaterial => _objectMaterial;
        public StalkerSurfaceType Surface => _surface;
        public string Origin => _origin;
        public IReadOnlyList<AudioClip> Clips => _clips;
    }

    [CreateAssetMenu(menuName = "Raid Into Darkness/Player/Collide Sound Library", fileName = "StalkerCollideSoundLibrary")]
    public sealed class StalkerCollideSoundLibrary : ScriptableObject
    {
        public const float MASS_LIMIT = 10000.0f;
        public const float DEFAULT_L_LIMIT = 150.0f;

        [Tooltip("EffectPars::vel_cret_sound: a contact plays a sound when |v·n|·sqrt(mass) exceeds this value.")]
        [SerializeField] private float _velocityCriterion = 10.0f;
        [Tooltip("[sound] snd_collide_min_volume.")]
        [SerializeField] private float _minVolume = 0.1f;
        [Tooltip("[sound] snd_collide_max_volume.")]
        [SerializeField] private float _maxVolume = 200.0f;
        [Tooltip("SOUND_EFFECT_DIST in physics_game.cpp: contacts farther from the camera are silent.")]
        [SerializeField] private float _maxDistance = 70.0f;
        [SerializeField] private List<StalkerCollideSounds> _entries = new List<StalkerCollideSounds>();
        [SerializeField] [TextArea(3, 20)] private string _sourceReport;

        public float VelocityCriterion => _velocityCriterion;
        public float MinVolume => _minVolume;
        public float MaxVolume => _maxVolume;
        public float MaxDistance => _maxDistance;
        public IReadOnlyList<StalkerCollideSounds> Entries => _entries;
        public string SourceReport => _sourceReport;

        public static float GetCriterion(Vector3 velocity, Vector3 normal, float mass)
        {
            return Mathf.Abs(Vector3.Dot(velocity, normal)) * Mathf.Sqrt(Mathf.Max(0.0f, mass));
        }

        public float GetVolume(float criterion)
        {
            return _minVolume + criterion * (_maxVolume - _minVolume) / (Mathf.Sqrt(MASS_LIMIT) * DEFAULT_L_LIMIT - _velocityCriterion);
        }

        public IReadOnlyList<AudioClip> Find(string objectMaterial, StalkerSurfaceType surface)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Surface == surface && string.Equals(_entries[i].ObjectMaterial, objectMaterial, StringComparison.OrdinalIgnoreCase))
                {
                    return _entries[i].Clips;
                }
            }

            return null;
        }

        public void SetImportData(List<StalkerCollideSounds> entries, float minVolume, float maxVolume, string sourceReport)
        {
            _entries = entries ?? new List<StalkerCollideSounds>();
            _minVolume = minVolume;
            _maxVolume = maxVolume;
            _sourceReport = sourceReport;
        }
    }
}
