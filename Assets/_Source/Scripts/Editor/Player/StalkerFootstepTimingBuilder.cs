using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RaidIntoDarkness.FirstPerson;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Omf;
using XRay.Importer.Sources.Ltx;

namespace RaidIntoDarkness.Player.Editor
{
    public static class StalkerFootstepTimingBuilder
    {
        public const string TIMING_PATH = "Assets/_Source/Settings/Player/Audio/StalkerFootstepTiming.asset";
        public const string CATALOG_PATH = "Assets/_Source/FirstPerson/Outfits/OutfitCatalog.asset";
        public const string LEFT_FOOT = "bip01_l_foot";
        public const string RIGHT_FOOT = "bip01_r_foot";

        private const string ACTOR = "actor";
        private const string ACTOR_ANIMATIONS = "meshes/actors/stalker_animation.omf";
        private const int SAMPLES_PER_SECOND = 120;
        private const float CONTACT_THRESHOLD = 0.2f;
        private const float MIN_FOOT_LIFT = 0.02f;
        private const float MIN_CONTACT_SPACING = 0.15f;

        public static readonly string[] INDEX_SCOPE = { "configs", "meshes/actors" };

        public static StalkerFootstepTimingLibrary Build(StalkerXRaySources sources, StringBuilder log)
        {
            var legs = new List<StalkerLegsMotionSteps>();
            if (sources != null)
            {
                ReadLegs(sources, legs, log);
            }
            else
            {
                log.AppendLine("WARNING: X-Ray sources unavailable; X-Ray legs timing fallback is empty.");
            }

            var bodies = new List<StalkerBodyMotionSteps>();
            ReadBodies(bodies, log);
            var library = AssetDatabase.LoadAssetAtPath<StalkerFootstepTimingLibrary>(TIMING_PATH);
            if (library == null)
            {
                StalkerXRaySources.EnsureFolder(System.IO.Path.GetDirectoryName(TIMING_PATH));
                library = ScriptableObject.CreateInstance<StalkerFootstepTimingLibrary>();
                AssetDatabase.CreateAsset(library, TIMING_PATH);
            }

            library.SetImportData(bodies, legs, log.ToString());
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        public static List<StalkerFootContact> FindContacts(float[] left, float[] right)
        {
            var contacts = new List<StalkerFootContact>();
            AddContacts(left, StalkerFootSide.Left, contacts);
            AddContacts(right, StalkerFootSide.Right, contacts);
            contacts.Sort((a, b) => a.Phase.CompareTo(b.Phase));
            return contacts;
        }

        private static void ReadLegs(StalkerXRaySources sources, List<StalkerLegsMotionSteps> legs, StringBuilder log)
        {
            XRayLtxDatabase system = XRayLtxDatabase.LoadSystem(sources.Index);
            string section = system.GetValue(ACTOR, "step_params");
            string legsCount = system.GetValue(ACTOR, "LegsCount");
            if (string.IsNullOrEmpty(section) || !system.Sections.TryGetValue(section.Trim().ToLowerInvariant(), out XRayLtxSection steps))
            {
                log.AppendLine("WARNING: [actor] step_params section not found.");
                return;
            }

            byte[] data = sources.ReadBytes(ACTOR_ANIMATIONS);
            OmfData omf = data != null ? OmfReader.ReadFile(data, ACTOR_ANIMATIONS) : null;
            log.AppendLine($"[actor] LegsCount = {legsCount}, step_params = {section} <- {steps.Origin}");
            log.AppendLine(omf != null ? $"{ACTOR_ANIMATIONS} <- {sources.GetSourceName(ACTOR_ANIMATIONS)}: {omf.Motions.Count} motions" : $"WARNING: {ACTOR_ANIMATIONS} not found; cycle times are unknown.");
            var names = new List<string>(steps.Values.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
            {
                string[] items = steps.Values[name].Split(',');
                if (items.Length < 5 || !int.TryParse(items[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cycles) || !StalkerXRaySources.TryFloat(items[1], out float leftTime) || !StalkerXRaySources.TryFloat(items[2], out float leftPower) || !StalkerXRaySources.TryFloat(items[3], out float rightTime) || !StalkerXRaySources.TryFloat(items[4], out float rightPower))
                {
                    log.AppendLine($"WARNING: [{section}] {name} has an invalid value.");
                    continue;
                }

                OmfMotion motion = omf != null ? omf.FindMotion(name) : null;
                if (motion == null)
                {
                    log.AppendLine($"  {name}: motion not found in {ACTOR_ANIMATIONS}, skipped (CStepManager::reload skips it too)");
                    continue;
                }

                float speed = motion.Params != null && motion.Params.Speed > 0.0 ? (float)motion.Params.Speed : 1.0f;
                float cycleTime = (float)(motion.FrameCount / OmfFormat.SAMPLE_FPS) / speed;
                bool stopAtEnd = motion.Params != null && motion.Params.StopAtEnd;
                legs.Add(new StalkerLegsMotionSteps(name, cycleTime, cycles, leftTime, leftPower, rightTime, rightPower, stopAtEnd));
                log.AppendLine($"  {name}: cycles {cycles}, L {leftTime}/{leftPower}, R {rightTime}/{rightPower}, blend time {StalkerXRaySources.Format(cycleTime)} s ({motion.FrameCount} frames / 30 fps / speed {StalkerXRaySources.Format(speed)}){(stopAtEnd ? ", stop at end" : string.Empty)}");
            }
        }

        private static void ReadBodies(List<StalkerBodyMotionSteps> bodies, StringBuilder log)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FirstPersonOutfitCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                log.AppendLine("WARNING: outfit catalog not found; body animation contacts are empty.");
                return;
            }

            var models = new Dictionary<BodyAnimationLibrary, GameObject>();
            foreach (FirstPersonOutfitEntry entry in catalog.Entries)
            {
                if (entry.BodyAnimations != null && entry.BodyModel != null && !models.ContainsKey(entry.BodyAnimations))
                {
                    models.Add(entry.BodyAnimations, entry.BodyModel);
                }
            }

            var handled = new HashSet<AnimationClip>();
            foreach (KeyValuePair<BodyAnimationLibrary, GameObject> pair in models)
            {
                log.AppendLine($"Body contacts: {AssetDatabase.GetAssetPath(pair.Key)} sampled on {pair.Value.name}, foot bones {LEFT_FOOT}/{RIGHT_FOOT}, contact = foot height falls below min + {CONTACT_THRESHOLD} * range");
                foreach (BodyAnimationBinding binding in EnumerateBindings(pair.Key))
                {
                    if (binding == null || binding.Clip == null || !handled.Add(binding.Clip))
                    {
                        continue;
                    }

                    StalkerBodyMotionSteps steps = Analyze(pair.Value, binding, log);
                    if (steps != null)
                    {
                        bodies.Add(steps);
                    }
                }
            }
        }

        private static IEnumerable<BodyAnimationBinding> EnumerateBindings(BodyAnimationLibrary library)
        {
            foreach (BodyAnimationTable table in Enum.GetValues(typeof(BodyAnimationTable)))
            {
                foreach (BodyAnimationBinding binding in library.GetTable(table))
                {
                    yield return binding;
                }
            }

            yield return library.BasicFallback;
            yield return library.TurnLeft;
            yield return library.TurnRight;
        }

        private static StalkerBodyMotionSteps Analyze(GameObject model, BodyAnimationBinding binding, StringBuilder log)
        {
            AnimationClip clip = binding.Clip;
            GameObject instance = UnityEngine.Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Transform left = FindDeep(instance.transform, LEFT_FOOT);
                Transform right = FindDeep(instance.transform, RIGHT_FOOT);
                if (left == null || right == null || clip.length <= 0.0f)
                {
                    log.AppendLine($"  {binding.Motion}: foot bones missing, skipped");
                    return null;
                }

                int samples = Mathf.Max(32, Mathf.CeilToInt(clip.length * SAMPLES_PER_SECOND));
                var leftHeights = new float[samples];
                var rightHeights = new float[samples];
                for (int i = 0; i < samples; i++)
                {
                    clip.SampleAnimation(instance, clip.length * i / samples);
                    leftHeights[i] = instance.transform.InverseTransformPoint(left.position).y;
                    rightHeights[i] = instance.transform.InverseTransformPoint(right.position).y;
                }

                List<StalkerFootContact> contacts = FindContacts(leftHeights, rightHeights);
                var text = new StringBuilder();
                foreach (StalkerFootContact contact in contacts)
                {
                    text.Append(contact.Side == StalkerFootSide.Left ? " L@" : " R@").Append(StalkerXRaySources.Format(contact.Phase));
                }

                log.AppendLine($"  {binding.Motion} ({clip.name}, {StalkerXRaySources.Format(clip.length)} s): {(contacts.Count > 0 ? text.ToString().Trim() : "no foot contacts (feet do not lift)")}");
                return new StalkerBodyMotionSteps(binding.Motion, clip, clip.length, contacts);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AddContacts(float[] heights, StalkerFootSide side, List<StalkerFootContact> contacts)
        {
            int count = heights.Length;
            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (float height in heights)
            {
                min = Mathf.Min(min, height);
                max = Mathf.Max(max, height);
            }

            if (max - min < MIN_FOOT_LIFT)
            {
                return;
            }

            float threshold = min + (max - min) * CONTACT_THRESHOLD;
            var found = new List<float>();
            for (int i = 0; i < count; i++)
            {
                float previous = heights[(i - 1 + count) % count];
                float current = heights[i];
                if (previous > threshold && current <= threshold)
                {
                    float t = (previous - threshold) / (previous - current);
                    float phase = Mathf.Repeat((i - 1 + t) / count, 1.0f);
                    if (found.Count == 0 || Mathf.Abs(Mathf.DeltaAngle(found[found.Count - 1] * 360.0f, phase * 360.0f)) / 360.0f >= MIN_CONTACT_SPACING)
                    {
                        found.Add(phase);
                    }
                }
            }

            foreach (float phase in found)
            {
                contacts.Add(new StalkerFootContact(phase, side));
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
