using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Rvm.CleanReleaseRoute07
{
    public sealed class PunctualShadowDistanceTrial : IDisposable
    {
        private const float DISTANCE = 75f;
        private const int EVIDENCE_VERSION = 2;
        private const int POSE_COUNT = 4;
        private const string ARCHIVE_ROOT = @"E:\Projects\stalker_raid_into_darkness_Archive\";

        private readonly Camera _camera;
        private readonly string _mode;
        private readonly List<PunctualShadowDistanceTrialEntry> _entries = new List<PunctualShadowDistanceTrialEntry>();
        private readonly Dictionary<Light, PunctualShadowDistanceTrialEntry> _byLight = new Dictionary<Light, PunctualShadowDistanceTrialEntry>();
        private readonly PunctualShadowDistancePoseRecord[] _poses = new PunctualShadowDistancePoseRecord[POSE_COUNT];

        private int _eligibleCount;
        private int _discoveryPasses;
        private int _worldDiscoveryPasses;
        private int _poseCount;
        private int _removedDestroyed;
        private int _maximumTracked;
        private bool _restored;

        public string Mode => _mode;
        public int TrackedCount => _entries.Count;
        public int EligibleCount => _eligibleCount;
        public int DiscoveryPasses => _discoveryPasses;
        public int WorldDiscoveryPasses => _worldDiscoveryPasses;
        public bool Restored => _restored;

        private PunctualShadowDistanceTrial(Camera camera, string mode, IReadOnlyList<Light> lights)
        {
            ValidateMode(mode);
            if (lights == null) throw new ArgumentNullException(nameof(lights));
            _camera = camera;
            _mode = mode;
            try
            {
                Discover(lights);
            }
            catch
            {
                Restore();
                throw;
            }
        }

        public static PunctualShadowDistanceTrial Attach(Camera camera, string mode)
        {
            ValidateMode(mode);
            var trial = new PunctualShadowDistanceTrial(camera, mode, FindLights());
            trial._worldDiscoveryPasses++;
            return trial;
        }

        public static PunctualShadowDistanceTrial Attach(Camera camera, string mode, IReadOnlyList<Light> lights)
        {
            return new PunctualShadowDistanceTrial(camera, mode, lights);
        }

        public bool Tracks(Light light)
        {
            return light != null && _byLight.ContainsKey(light);
        }

        public void RefreshTargetsAtPose()
        {
            if (_restored) return;
            if (_poseCount == POSE_COUNT) throw new InvalidOperationException("The trial supports exactly four route-pose discoveries after startup.");
            _worldDiscoveryPasses++;
            RefreshTargetsAtPose(FindLights());
        }

        public void RefreshTargetsAtPose(IReadOnlyList<Light> lights)
        {
            if (_restored) return;
            if (lights == null) throw new ArgumentNullException(nameof(lights));
            if (_poseCount == POSE_COUNT) throw new InvalidOperationException("The trial supports exactly four route-pose discoveries after startup.");
            int previous = _entries.Count;
            Discover(lights);
            _poses[_poseCount++] = new PunctualShadowDistancePoseRecord { frame = Time.frameCount, tracked = _entries.Count, eligible = _eligibleCount, added = Mathf.Max(0, _entries.Count - previous) };
        }

        public void Restore()
        {
            if (_restored) return;
            _restored = true;
            foreach (PunctualShadowDistanceTrialEntry entry in _entries) Restore(entry);
        }

        public void Dispose()
        {
            Restore();
        }

        public void RecordEvidence(string outputDirectory, string label, bool detailed = false)
        {
            if (string.IsNullOrEmpty(label) || label.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("The evidence label must be a file name.", nameof(label));
            string output = Path.GetFullPath(outputDirectory);
            if (!output.StartsWith(ARCHIVE_ROOT, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Trial evidence must remain inside the canonical Archive.", nameof(outputDirectory));
            PruneDestroyed();
            int restrictedNone = 0;
            int missingData = 0;
            int restrictedValues = 0;
            int directional = 0;
            int other = 0;
            int dimmed = 0;
            var records = detailed ? new PunctualShadowDistanceLightRecord[_entries.Count] : null;
            for (int index = 0; index < _entries.Count; index++)
            {
                PunctualShadowDistanceTrialEntry entry = _entries[index];
                bool punctual = entry.Type == LightType.Point || entry.Type == LightType.Spot;
                if (entry.Type == LightType.Directional) directional++;
                else if (!punctual) other++;
                else if (entry.Shadows == LightShadows.None) restrictedNone++;
                else if (entry.Data == null) missingData++;
                else if (!entry.Eligible) restrictedValues++;
                if (entry.Eligible && entry.Data != null && entry.Data.shadowDimmer < entry.ShadowDimmer) dimmed++;
                if (detailed) records[index] = Capture(entry);
            }
            var report = new PunctualShadowDistanceTrialReport { schemaVersion = EVIDENCE_VERSION, mode = _mode, restored = _restored, frame = Time.frameCount, distance = DISTANCE, discoveryPasses = _discoveryPasses, worldDiscoveryPasses = _worldDiscoveryPasses, tracked = _entries.Count, eligible = _eligibleCount, originalNone = restrictedNone, missingHdData = missingData, restrictedOriginalValues = restrictedValues, directionalsExcluded = directional, otherTypesExcluded = other, currentlyDimmed = dimmed, removedDestroyed = _removedDestroyed, maximumTracked = _maximumTracked, cameraAvailable = HasCamera(), cameraEntityId = _camera != null ? _camera.GetEntityId().ToString() : null, cameraPosition = _camera != null ? _camera.transform.position : Vector3.zero, poseCount = _poseCount, poses = _poses, lights = records, scope = "Module-only bounded HDRP route trial. A preserves the current production baseline; B caps native HDRP per-view shadow fade at75. No coded distance loop or shadow-dimmer mutation is installed. Dynamically spawned lights between bounded discoveries are not governed by this trial; technical inventory must check coverage. A/B can be equivalent when production already caps the same sources at75; historical pre-integration evidence is not a current A/B gain claim." };
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "punctual-shadow-" + label + ".json"), JsonUtility.ToJson(report, true));
        }

        private static Light[] FindLights()
        {
            return UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
        }

        private static void ValidateMode(string mode)
        {
            if (mode != "A" && mode != "B") throw new ArgumentException("The punctual shadow trial requires mode A or B. The retired coded-distance mode C is not supported.", nameof(mode));
        }

        private void Discover(IReadOnlyList<Light> lights)
        {
            PruneDestroyed();
            _discoveryPasses++;
            foreach (Light light in lights)
            {
                if (light == null || !light.gameObject.scene.IsValid() || !light.gameObject.scene.isLoaded) continue;
                light.TryGetComponent(out HDAdditionalLightData data);
                if (_byLight.TryGetValue(light, out PunctualShadowDistanceTrialEntry existing))
                {
                    if (existing.Data == data && existing.Type == light.type) continue;
                    Restore(existing);
                    if (existing.Eligible) _eligibleCount--;
                    _entries.Remove(existing);
                    _byLight.Remove(light);
                }
                var entry = new PunctualShadowDistanceTrialEntry(light, data);
                _entries.Add(entry);
                _byLight.Add(light, entry);
                if (entry.Eligible)
                {
                    _eligibleCount++;
                    if (_mode == "B" && entry.FadeDistance > DISTANCE)
                    {
                        data.shadowFadeDistance = DISTANCE;
                        entry.FadeOverridden = true;
                    }
                }
            }
            _maximumTracked = Mathf.Max(_maximumTracked, _entries.Count);
        }

        private void PruneDestroyed()
        {
            for (int index = _entries.Count - 1; index >= 0; index--)
            {
                PunctualShadowDistanceTrialEntry entry = _entries[index];
                if (entry.Light != null && entry.Transform != null) continue;
                Restore(entry);
                _byLight.Remove(entry.Light);
                _entries.RemoveAt(index);
                if (entry.Eligible) _eligibleCount--;
                _removedDestroyed++;
            }
        }

        private bool HasCamera()
        {
            return _camera != null && _camera.cameraType == CameraType.Game && Finite(_camera.transform.position);
        }

        private static bool Finite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static void Restore(PunctualShadowDistanceTrialEntry entry)
        {
            if (!entry.FadeOverridden || entry.Data == null) return;
            entry.Data.shadowFadeDistance = entry.FadeDistance;
            entry.FadeOverridden = false;
        }

        private static PunctualShadowDistanceLightRecord Capture(PunctualShadowDistanceTrialEntry entry)
        {
            Light light = entry.Light;
            HDAdditionalLightData data = entry.Data;
            return new PunctualShadowDistanceLightRecord { entityId = light.GetEntityId().ToString(), path = Hierarchy(entry.Transform), eligible = entry.Eligible, originalType = entry.Type.ToString(), originalEnabled = entry.Enabled, currentEnabled = light.enabled, active = light.gameObject.activeInHierarchy, originalIntensity = entry.Intensity, currentIntensity = light.intensity, originalRange = entry.Range, currentRange = light.range, originalColor = entry.Color, currentColor = light.color, originalShadows = entry.Shadows.ToString(), currentShadows = light.shadows.ToString(), currentType = light.type.ToString(), originalCullingMask = entry.CullingMask, currentCullingMask = light.cullingMask, originalRenderingLayerMask = entry.RenderingLayerMask, currentRenderingLayerMask = light.renderingLayerMask, originalFadeDistance = entry.FadeDistance, currentFadeDistance = data != null ? data.shadowFadeDistance : 0f, originalShadowDimmer = entry.ShadowDimmer, currentShadowDimmer = data != null ? data.shadowDimmer : 0f, volumetricShadowDimmerGoverned = false, originalVolumetricShadowDimmer = entry.VolumetricShadowDimmer, currentVolumetricShadowDimmer = data != null ? data.volumetricShadowDimmer : 0f };
        }

        private static string Hierarchy(Transform value)
        {
            string path = value.name;
            while (value.parent != null)
            {
                value = value.parent;
                path = value.name + "/" + path;
            }
            return path;
        }
    }
}
