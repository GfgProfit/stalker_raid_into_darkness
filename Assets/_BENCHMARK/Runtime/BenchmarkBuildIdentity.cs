using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Rvm.Benchmark
{
    [Serializable]
    public sealed class BenchmarkBuildStamp
    {
        public int formatVersion = 1;
        public string buildId;
        public string sourceDigest;
        public string renderSettingsDigest;
        public string inputManifestSha256;
        public string gitSha;
        public string gitDirty;
        public string unity;
        public string hdrp;
    }

    [Serializable]
    public sealed class BenchmarkBuildInputIdentity
    {
        public int formatVersion = 1;
        public string buildId;
        public string sourceDigest;
        public string renderSettingsDigest;
        public string gitSha;
        public string gitDirty;
        public string unity;
        public string hdrp;
    }

    [Serializable]
    public sealed class BenchmarkBuildArtifact
    {
        public string path;
        public string sha256;
    }

    [Serializable]
    public sealed class BenchmarkBuildArtifactManifest
    {
        public int formatVersion = 1;
        public string buildId;
        public string sourceDigest;
        public string inputManifestSha256;
        public string stampSha256;
        public string applicationBuildGuid;
        public string executable;
        public List<BenchmarkBuildArtifact> artifacts = new List<BenchmarkBuildArtifact>();
    }

    [Serializable]
    public sealed class BenchmarkBuildIdentityEvidence
    {
        public int formatVersion = 1;
        public string status = "Unverified";
        public string reason;
        public BenchmarkBuildStamp stamp;
        public string stampSha256;
        public string inputManifestSha256;
        public string artifactsManifestSha256;
        public string applicationBuildGuid;
        public string executablePath;
        public string executableSha256;
        public int checkedArtifacts;
        public List<string> mismatches = new List<string>();
    }

    public static class BenchmarkBuildIdentity
    {
        public const string RESOURCE_NAME = "RvmBenchmarkBuildIdentity";
        public const string INPUT_MANIFEST = "build-inputs.json";
        public const string ARTIFACT_MANIFEST = "build-artifacts.json";

        private static BenchmarkBuildIdentityEvidence _cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _cached = null;
        }

        public static BenchmarkBuildIdentityEvidence Capture()
        {
            if (_cached == null)
            {
                _cached = ReadPlayer();
            }
            return JsonUtility.FromJson<BenchmarkBuildIdentityEvidence>(JsonUtility.ToJson(_cached));
        }

        private static BenchmarkBuildIdentityEvidence ReadPlayer()
        {
            var evidence = new BenchmarkBuildIdentityEvidence { reason = "No independently verified embedded Player identity." };
            if (Application.isEditor)
            {
                evidence.reason = "Editor execution is not a built Player; embedded Player provenance is unverified.";
                return evidence;
            }
            try
            {
                string root = Path.GetDirectoryName(Application.dataPath);
                var asset = Resources.Load<TextAsset>(RESOURCE_NAME);
                if (asset == null)
                {
                    evidence.reason = "Player has no embedded benchmark build stamp.";
                    return evidence;
                }
                byte[] bytes = asset.bytes;
                evidence.stampSha256 = HashBytes(bytes);
                evidence.stamp = JsonUtility.FromJson<BenchmarkBuildStamp>(Encoding.UTF8.GetString(bytes));
                Resources.UnloadAsset(asset);
                evidence.applicationBuildGuid = Application.buildGUID;
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    evidence.executablePath = Path.GetFullPath(process.MainModule.FileName);
                }
                string inputPath = Path.Combine(root, INPUT_MANIFEST);
                string artifactPath = Path.Combine(root, ARTIFACT_MANIFEST);
                if (!File.Exists(inputPath) || !File.Exists(artifactPath))
                {
                    evidence.reason = "Embedded stamp found, but build input/artifact manifest is absent.";
                    return evidence;
                }
                evidence.inputManifestSha256 = HashFile(inputPath);
                evidence.artifactsManifestSha256 = HashFile(artifactPath);
                var manifest = JsonUtility.FromJson<BenchmarkBuildArtifactManifest>(File.ReadAllText(artifactPath));
                var input = JsonUtility.FromJson<BenchmarkBuildInputIdentity>(File.ReadAllText(inputPath));
                string executable = Path.GetFileName(evidence.executablePath);
                string expectedExecutable = ResolveArtifact(root, executable);
                if (!string.Equals(expectedExecutable, evidence.executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    evidence.status = "Mismatch";
                    evidence.reason = "Running executable is outside the Player data directory.";
                    evidence.mismatches.Add(evidence.reason);
                    return evidence;
                }
                var actual = CollectArtifacts(root, Path.GetFileName(Application.dataPath));
                var verified = Verify(evidence.stamp, evidence.stampSha256, input, evidence.inputManifestSha256, manifest, actual, evidence.applicationBuildGuid, executable);
                if (evidence.stamp != null && evidence.stamp.unity != Application.unityVersion)
                {
                    verified.status = "Mismatch";
                    verified.mismatches.Add("Embedded Unity version differs from actual Player version.");
                    verified.reason = string.Join("; ", verified.mismatches);
                }
                verified.artifactsManifestSha256 = evidence.artifactsManifestSha256;
                verified.executablePath = evidence.executablePath;
                return verified;
            }
            catch (Exception exception)
            {
                evidence.status = "Mismatch";
                evidence.reason = "Player build identity validation failed: " + exception.GetType().Name + ": " + exception.Message;
                evidence.mismatches.Add(evidence.reason);
                return evidence;
            }
        }

        public static BenchmarkBuildIdentityEvidence Verify(BenchmarkBuildStamp stamp, string stampSha256, BenchmarkBuildInputIdentity input, string inputManifestSha256, BenchmarkBuildArtifactManifest manifest, List<BenchmarkBuildArtifact> actual, string applicationBuildGuid, string executable)
        {
            var result = new BenchmarkBuildIdentityEvidence { stamp = stamp, stampSha256 = stampSha256, inputManifestSha256 = inputManifestSha256, applicationBuildGuid = applicationBuildGuid, status = "Mismatch" };
            if (stamp == null || stamp.formatVersion != 1 || !Guid.TryParse(stamp.buildId, out var id) || id == Guid.Empty || !IsSha256(stamp.sourceDigest) || !IsSha256(stamp.renderSettingsDigest) || !IsSha256(stamp.inputManifestSha256))
            {
                result.mismatches.Add("Embedded stamp is missing or malformed.");
            }
            if (manifest == null || manifest.formatVersion != 1 || manifest.artifacts == null || actual == null || input == null || input.formatVersion != 1)
            {
                result.mismatches.Add("Artifact manifest or actual artifact inventory is missing/malformed.");
            }
            if (result.mismatches.Count == 0)
            {
                Check(result, stamp.buildId == manifest.buildId, "Embedded buildId differs from artifact manifest.");
                Check(result, stamp.sourceDigest == manifest.sourceDigest, "Embedded source digest differs from artifact manifest.");
                Check(result, stamp.buildId == input.buildId && stamp.sourceDigest == input.sourceDigest && stamp.renderSettingsDigest == input.renderSettingsDigest, "Embedded identity differs from input manifest fields.");
                Check(result, stamp.gitSha == input.gitSha && stamp.gitDirty == input.gitDirty && stamp.unity == input.unity && stamp.hdrp == input.hdrp && !string.IsNullOrEmpty(stamp.gitSha) && (stamp.gitDirty == "True" || stamp.gitDirty == "False") && !string.IsNullOrEmpty(stamp.unity) && !string.IsNullOrEmpty(stamp.hdrp), "Embedded source/version metadata differs from input manifest.");
                Check(result, stamp.inputManifestSha256 == inputManifestSha256 && manifest.inputManifestSha256 == inputManifestSha256, "Actual input manifest SHA256 differs from embedded/artifact identities.");
                Check(result, stampSha256 == manifest.stampSha256 && IsSha256(stampSha256), "Embedded stamp bytes differ from artifact manifest.");
                Check(result, !string.IsNullOrEmpty(applicationBuildGuid) && string.Equals(applicationBuildGuid, manifest.applicationBuildGuid, StringComparison.OrdinalIgnoreCase), "Application.buildGUID differs from BuildReport identity.");
                Check(result, string.Equals(executable, manifest.executable, StringComparison.OrdinalIgnoreCase), "Actual executable name differs from artifact manifest.");
                var expected = ArtifactMap(manifest.artifacts, result, "expected");
                var observed = ArtifactMap(actual, result, "actual");
                foreach (var item in expected)
                {
                    Check(result, observed.TryGetValue(item.Key, out string hash) && item.Value == hash, "Binary artifact missing/changed: " + item.Key);
                }
                foreach (var item in observed)
                {
                    Check(result, expected.ContainsKey(item.Key), "Unexpected runtime artifact: " + item.Key);
                }
                Check(result, expected.ContainsKey(executable), "Executable is absent from artifact coverage.");
                Check(result, expected.ContainsKey("UnityPlayer.dll"), "UnityPlayer.dll is absent from artifact coverage.");
                bool playerData = false;
                foreach (string key in expected.Keys)
                {
                    playerData |= key.IndexOf("_Data/", StringComparison.Ordinal) >= 0;
                }
                Check(result, playerData, "Packed Player data is absent from artifact coverage.");
                result.checkedArtifacts = observed.Count;
                observed.TryGetValue(executable, out result.executableSha256);
            }
            result.status = result.mismatches.Count == 0 ? "Verified" : "Mismatch";
            result.reason = result.mismatches.Count == 0 ? "Embedded resource stamp, input manifest, BuildReport GUID and all runtime binary/data artifact hashes agree." : string.Join("; ", result.mismatches);
            return result;
        }

        public static bool IsVerified(BenchmarkBuildIdentityEvidence evidence)
        {
            return evidence != null && evidence.formatVersion == 1 && evidence.status == "Verified" && evidence.stamp != null && evidence.stamp.formatVersion == 1 && Guid.TryParse(evidence.stamp.buildId, out var id) && id != Guid.Empty && IsSha256(evidence.stamp.sourceDigest) && IsSha256(evidence.stamp.renderSettingsDigest) && !string.IsNullOrEmpty(evidence.stamp.gitSha) && (evidence.stamp.gitDirty == "True" || evidence.stamp.gitDirty == "False") && !string.IsNullOrEmpty(evidence.stamp.unity) && !string.IsNullOrEmpty(evidence.stamp.hdrp) && IsSha256(evidence.stampSha256) && IsSha256(evidence.inputManifestSha256) && evidence.inputManifestSha256 == evidence.stamp.inputManifestSha256 && IsSha256(evidence.artifactsManifestSha256) && IsSha256(evidence.executableSha256) && !string.IsNullOrEmpty(evidence.applicationBuildGuid) && evidence.checkedArtifacts >= 3 && evidence.mismatches != null && evidence.mismatches.Count == 0;
        }

        public static bool IsVerified(BenchmarkBuildIdentityEvidence evidence, string sourceDigest, string renderSettingsDigest)
        {
            return IsVerified(evidence) && evidence.stamp.sourceDigest == sourceDigest && evidence.stamp.renderSettingsDigest == renderSettingsDigest;
        }

        private static Dictionary<string, string> ArtifactMap(List<BenchmarkBuildArtifact> artifacts, BenchmarkBuildIdentityEvidence evidence, string role)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artifact in artifacts)
            {
                if (artifact == null || !IsRelativeArtifact(artifact.path) || !IsSha256(artifact.sha256) || result.ContainsKey(artifact.path))
                {
                    evidence.mismatches.Add("Invalid/duplicate " + role + " binary artifact entry.");
                    continue;
                }
                result.Add(artifact.path, artifact.sha256);
            }
            return result;
        }

        public static List<BenchmarkBuildArtifact> CollectArtifacts(string root, string playerDataDirectory)
        {
            var paths = new List<string>();
            paths.AddRange(Directory.GetFiles(root, "*.exe", SearchOption.TopDirectoryOnly));
            paths.AddRange(Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly));
            foreach (string folder in new[] { playerDataDirectory, "MonoBleedingEdge", "D3D12" })
            {
                string full = ResolveArtifact(root, folder);
                if (Directory.Exists(full))
                {
                    paths.AddRange(Directory.GetFiles(full, "*", SearchOption.AllDirectories));
                }
            }
            paths.Sort(StringComparer.Ordinal);
            var result = new List<BenchmarkBuildArtifact>(paths.Count);
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string path in paths)
            {
                string relative = Path.GetFullPath(path).Substring(prefix.Length).Replace('\\', '/');
                result.Add(new BenchmarkBuildArtifact { path = relative, sha256 = HashFile(ResolveArtifact(root, relative)) });
            }
            return result;
        }

        public static string ResolveArtifact(string root, string relative)
        {
            if (!IsRelativeArtifact(relative))
            {
                throw new ArgumentException("Artifact path must be relative and stay inside the Player directory.");
            }
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Artifact path escaped the Player directory.");
            }
            return path;
        }

        private static bool IsRelativeArtifact(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0)
            {
                return false;
            }
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    return false;
                }
            }
            return true;
        }

        public static string HashFile(string path)
        {
            using var algorithm = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static string HashBytes(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f'))
                {
                    return false;
                }
            }
            return true;
        }

        private static void Check(BenchmarkBuildIdentityEvidence evidence, bool valid, string reason)
        {
            if (!valid)
            {
                evidence.mismatches.Add(reason);
            }
        }
    }
}
