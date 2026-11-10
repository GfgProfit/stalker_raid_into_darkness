using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerPlayerFoley : MonoBehaviour
    {
        [SerializeField] private StalkerSurfaceAudioLibrary _library;
        [SerializeField] private StalkerSurfaceDetector _detector;
        [SerializeField] private AudioSource _source;
        [SerializeField] private float _masterVolume = 1.0f;

        private readonly Dictionary<List<AudioClip>, int> _lastIndex = new Dictionary<List<AudioClip>, int>();
        private readonly List<AudioClip> _gearClips = new List<AudioClip>();
        private readonly List<AudioClip> _crouchClips = new List<AudioClip>();

        public StalkerSurfaceAudioLibrary Library => _library;
        public StalkerSurfaceDetector Detector => _detector;
        public AudioSource Source => _source;
        public string LastClipName { get; private set; } = string.Empty;
        public string LastReason { get; private set; } = string.Empty;
        public int FootstepCount { get; private set; }
        public int JumpCount { get; private set; }
        public int LandingCount { get; private set; }
        public float LastLandingSpeed { get; private set; }

        public float MasterVolume
        {
            get => _masterVolume;
            set => _masterVolume = Mathf.Max(0.0f, value);
        }

        public void Initialize()
        {
            _gearClips.Clear();
            _crouchClips.Clear();
            if (_library != null)
            {
                _gearClips.AddRange(_library.GearRustleClips);
                _crouchClips.AddRange(_library.CrouchKeyClips);
            }
        }

        public void PlayFootstep(StalkerFootstep step)
        {
            if (_library == null || _detector == null)
            {
                return;
            }

            StalkerSurfaceType surfaceType = _detector.Detect();
            if (!_detector.HasGround)
            {
                LastReason = "footstep skipped: no ground under the feet";
                return;
            }

            FootstepCount++;
            StalkerSurfaceSounds surface = _library.GetSurface(surfaceType);
            string fallback = string.Empty;
            if (surface == null || (!HasFootsteps(surface) && _detector.CurrentSource != StalkerSurfaceSource.XRayMaterial))
            {
                surface = _library.GetSurface(_library.FallbackSurface);
                fallback = $" -> fallback {_library.FallbackSurface} (no original clips)";
            }

            List<AudioClip> movingClips = surface?.GetMovingModeClips(step.Mode);
            float movingVolume = _library.EffectsVolume;
            string note = string.Empty;
            if (step.Mode == StalkerMovingMode.Crouch && (movingClips == null || movingClips.Count == 0) && _library.CrouchFallbackVolume > 0.0f)
            {
                movingClips = surface?.WalkClips;
                movingVolume *= _library.CrouchFallbackVolume;
                note = " walk clips, RMS has no crouch files";
            }

            string pair = Play(surface?.PairStepClips, step.Power * _library.HudStepVolume);
            string moving = Play(movingClips, movingVolume);
            string gear = Play(_gearClips, _library.EffectsVolume);
            LastClipName = moving.Length > 0 ? moving : pair.Length > 0 ? pair : gear;
            LastReason = $"{step.Side} {step.Source} {step.LegsMotion} power {step.Power:0.##} on {surfaceType} ({_detector.CurrentSource}){fallback}: pair [{pair}] mode {step.Mode} [{moving}]{note} gear [{gear}]";
        }

        public void PlayJump()
        {
            if (_library == null || _detector == null)
            {
                return;
            }

            StalkerSurfaceType surfaceType = _detector.Detect();
            if (!_detector.HasGround)
            {
                return;
            }

            JumpCount++;
            StalkerSurfaceSounds surface = _library.GetSurface(surfaceType);
            bool fallback = surface == null || surface.JumpClips.Count == 0;
            string clip = Play((fallback ? _library.GetSurface(_library.FallbackSurface) : surface)?.JumpClips, _library.EffectsVolume);
            LastReason = $"jump on {surfaceType}{(fallback ? " -> fallback " + _library.FallbackSurface : string.Empty)}: [{clip}]";
        }

        public void PlayLanding(float contactSpeed)
        {
            LastLandingSpeed = contactSpeed;
            if (_library == null || _detector == null || contactSpeed <= _library.LandSoundSpeed)
            {
                return;
            }

            StalkerSurfaceType surfaceType = _detector.Detect();
            if (!_detector.HasGround)
            {
                return;
            }

            LandingCount++;
            float volume = _library.EffectsVolume * contactSpeed / _library.LandVolumeDivider;
            StalkerSurfaceSounds surface = _library.GetSurface(surfaceType);
            bool fallback = surface == null || surface.LandClips.Count == 0;
            string clip = Play((fallback ? _library.GetSurface(_library.FallbackSurface) : surface)?.LandClips, volume);
            LastReason = $"landing {contactSpeed:0.00} m/s on {surfaceType}{(fallback ? " -> fallback " + _library.FallbackSurface : string.Empty)}: [{clip}] volume {volume:0.00}";
        }

        private static bool HasFootsteps(StalkerSurfaceSounds surface)
        {
            return surface != null && (surface.PairStepClips.Count > 0 || surface.WalkClips.Count > 0 || surface.RunClips.Count > 0);
        }

        public void PlayCrouchKey()
        {
            if (_library != null)
            {
                Play(_crouchClips, _library.EffectsVolume);
            }
        }

        private string Play(List<AudioClip> clips, float volume)
        {
            if (clips == null || clips.Count == 0 || _source == null)
            {
                return string.Empty;
            }

            int index = Random.Range(0, clips.Count);
            if (clips.Count > 1 && _lastIndex.TryGetValue(clips, out int last) && index == last)
            {
                index = (last + 1 + Random.Range(0, clips.Count - 1)) % clips.Count;
            }

            _lastIndex[clips] = index;
            AudioClip clip = clips[index];
            if (clip == null)
            {
                return string.Empty;
            }

            _source.PlayOneShot(clip, volume * _masterVolume);
            LastClipName = clip.name;
            return clip.name;
        }
    }
}
