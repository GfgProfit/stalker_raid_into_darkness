using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Rvm.Benchmark
{
    [Serializable]
    public sealed class BenchmarkConditionEvidence
    {
        public int version = 2;
        public string protocolId;
        public string sourceDigest;
        public string buildId;
        public string inputManifestSha256;
        public string artifactsManifestSha256;
        public string applicationBuildGuid;
        public string executableSha256;
        public string scene;
        public string weatherProfile;
        public double gameSeconds;
        public string[] reviewedBehaviourTypes;
        public string reviewSummary;
        public string scope;
        public string[] runIds;
    }

    public static class BenchmarkConditionProtocol
    {
        public static bool Validate(BenchmarkMetadata metadata, out string reason)
        {
            if (!ValidateOrigin(metadata, out reason))
            {
                return false;
            }
            if (metadata.renderedMeasurementFrames <= 0)
            {
                reason = "Working-camera rendering is unverified: no rendered measurement frames were recorded.";
                return false;
            }
            reason = "No scenario-specific condition evidence supplied; NPC/custom AI remains unverified.";
            if (string.IsNullOrEmpty(metadata.conditionEvidence))
            {
                return false;
            }
            BenchmarkConditionEvidence evidence;
            try
            {
                evidence = JsonUtility.FromJson<BenchmarkConditionEvidence>(metadata.conditionEvidence);
            }
            catch (Exception exception)
            {
                reason = "Invalid condition evidence: " + exception.GetType().Name;
                return false;
            }
            if (evidence == null || evidence.version != 2 || string.IsNullOrEmpty(evidence.protocolId) || string.IsNullOrEmpty(evidence.reviewSummary) || string.IsNullOrEmpty(evidence.scope) || evidence.reviewedBehaviourTypes == null || evidence.reviewedBehaviourTypes.Length == 0 || evidence.sourceDigest != metadata.buildSourceDigest || evidence.scene != metadata.scene)
            {
                reason = "Condition evidence does not match build source digest, scene, protocol or reviewed inventory.";
                return false;
            }
            var identity = metadata.buildIdentity;
            if (evidence.buildId != identity.stamp.buildId || evidence.inputManifestSha256 != identity.inputManifestSha256 || evidence.artifactsManifestSha256 != identity.artifactsManifestSha256 || evidence.applicationBuildGuid != identity.applicationBuildGuid || evidence.executableSha256 != identity.executableSha256)
            {
                reason = "Condition evidence does not match the independently verified embedded build, input manifest, artifact manifest, application GUID or actual executable.";
                return false;
            }
            if (evidence.runIds == null || Array.IndexOf(evidence.runIds, metadata.runId) < 0)
            {
                reason = "Condition attestation is not bound to this Run ID.";
                return false;
            }
            foreach (var entries in new[] { metadata.environment, metadata.endEnvironment })
            {
                string types = Value(entries, "inventory.behaviourTypes");
                if (types == null)
                {
                    reason = "Observed scene behaviour inventory is absent.";
                    return false;
                }
                var allowed = new HashSet<string>(evidence.reviewedBehaviourTypes, StringComparer.Ordinal);
                foreach (string type in types.Split('\n'))
                {
                    if (!allowed.Contains(type))
                    {
                        reason = "Unreviewed participant type: " + type;
                        return false;
                    }
                }
                string activeFx = Value(entries, "weather.0.ActiveFx");
                bool noFx = activeFx != null && (activeFx.Length == 0 || activeFx == "none");
                if (Value(entries, "npc.activeNavMeshAgents") != "0" || Value(entries, "weather.0.ActiveProfile") != evidence.weatherProfile || Value(entries, "weather.0.Paused") != "True" || Value(entries, "weather.0.IsTransitioning") != "False" || !noFx || !double.TryParse(Value(entries, "weather.0.GameTimeSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || Math.Abs(seconds - evidence.gameSeconds) > 0.000001)
                {
                    reason = "Observed NPC/navigation or fixed weather/time/transition/FX conditions violate the reviewed protocol.";
                    return false;
                }
            }
            reason = "Reviewed scenario configuration and participant-type envelope confirmed against this build. Scope: " + evidence.scope + ". Physics, random state, particles and anomaly phase are not claimed deterministic.";
            return true;
        }

        public static bool ValidateOrigin(BenchmarkMetadata metadata, out string reason)
        {
            reason = "Run origin is unverified: embedded identity, actual artifacts and input manifest have not been independently matched to this metadata.";
            if (metadata == null || !BenchmarkBuildIdentity.IsVerified(metadata.buildIdentity, metadata.buildSourceDigest, metadata.renderSettingsDigest))
            {
                if (metadata != null && metadata.buildIdentity != null)
                {
                    reason += " Recorded status=" + metadata.buildIdentity.status + "; evidence=" + metadata.buildIdentity.reason;
                }
                return false;
            }
            var stamp = metadata.buildIdentity.stamp;
            if (metadata.gitSha != stamp.gitSha || metadata.gitDirty != stamp.gitDirty || metadata.unity != stamp.unity || metadata.hdrp != stamp.hdrp)
            {
                reason = "Run source/version metadata differs from the independently verified embedded stamp.";
                return false;
            }
            reason = "Embedded stamp, source/settings identity, input manifest, actual binary artifacts and runtime source/version metadata are verified.";
            return true;
        }

        private static string Value(List<BenchmarkEntry> entries, string key)
        {
            return entries.Find(value => value.key == key)?.value;
        }
    }
}
