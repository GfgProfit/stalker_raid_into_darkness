using System;
using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Levels;

namespace RaidIntoDarkness.Player
{
    public sealed class StalkerVegetationAudio : IDisposable
    {
        private const float MIN_SPEED_SQUARED = 0.01f;

        private readonly StalkerPlayerFoley _foley;
        private readonly List<XRayPassableContact> _contacts = new List<XRayPassableContact>();

        private AudioSource _source;

        public StalkerVegetationAudio(StalkerPlayerFoley foley)
        {
            _foley = foley;
        }

        public AudioSource Source => _source;
        public int PlayedCount { get; private set; }
        public string LastMaterial { get; private set; }

        public void Tick(CharacterController controller, Vector3 velocity)
        {
            if (controller == null || _foley == null || _foley.Library == null || velocity.sqrMagnitude <= MIN_SPEED_SQUARED || _source != null && _source.isPlaying)
            {
                return;
            }

            Transform transform = controller.transform;
            Vector3 scale = transform.lossyScale;
            float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            Vector3 center = transform.TransformPoint(controller.center);
            Vector3 offset = transform.up * Mathf.Max(controller.height * Mathf.Abs(scale.y) * 0.5f - radius, 0f);
            _contacts.Clear();
            XRayLevelContactSource.GetPassableContacts(center - offset, center + offset, radius, _contacts);
            foreach (XRayPassableContact contact in _contacts)
            {
                if (!StalkerSurfaceTypes.TryGetType(contact.Material.Name, out StalkerSurfaceType type))
                {
                    continue;
                }

                List<AudioClip> clips = _foley.Library.GetSurface(type)?.CollideClips;
                if (clips == null || clips.Count == 0)
                {
                    continue;
                }

                AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Count)];
                if (clip == null)
                {
                    continue;
                }

                EnsureSource();
                _source.transform.position = contact.Point;
                _source.clip = clip;
                _source.volume = _foley.MasterVolume * _foley.Library.EffectsVolume;
                _source.Play();
                LastMaterial = contact.Material.Name;
                PlayedCount++;
                return;
            }
        }

        public void Dispose()
        {
            if (_source == null)
            {
                return;
            }

            _source.Stop();
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_source.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_source.gameObject);
            }

            _source = null;
        }

        private void EnsureSource()
        {
            if (_source != null)
            {
                return;
            }

            var emitter = new GameObject("VegetationContactAudio");
            emitter.transform.SetParent(_foley.transform, false);
            _source = emitter.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 1f;
            _source.dopplerLevel = 0f;
            if (_foley.Source != null)
            {
                _source.outputAudioMixerGroup = _foley.Source.outputAudioMixerGroup;
                _source.priority = _foley.Source.priority;
            }
        }
    }
}
