using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Rvm.Benchmark.Editor
{
    public static class BenchmarkBuildProvenance
    {
        public const string STAMP_FOLDER = "Assets/_BENCHMARK/Resources";
        public const string STAMP_PATH = STAMP_FOLDER + "/RvmBenchmarkBuildIdentity.bytes";

        private static readonly List<SceneProcessingEvent> SCENE_EVENTS = new List<SceneProcessingEvent>();
        private static string _buildId;
        private static int _sceneCallbackCount;

        public static string SourceDigest { get; private set; }
        public static string SettingsDigest { get; private set; }

        public static string Capture(string destination, string scene)
        {
            HdrpShadowPackagePatch.RequireApplied();
            SCENE_EVENTS.Clear();
            _sceneCallbackCount = 0;
            var code = new List<BenchmarkEntry>();
            var assets = new List<BenchmarkEntry>();
            var settings = new List<BenchmarkEntry>();
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (AssetDatabase.IsValidFolder(path))
                {
                    continue;
                }
                if (!IsGeneratedPath(path) && (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".asmdef", StringComparison.Ordinal) || path.EndsWith(".shader", StringComparison.Ordinal) || path.EndsWith(".hlsl", StringComparison.Ordinal) || path.EndsWith(".shadergraph", StringComparison.Ordinal)))
                {
                    HashFile(code, path);
                }
                if (path != STAMP_PATH && path.IndexOf("/Resources/", StringComparison.Ordinal) >= 0 && Path.GetFileNameWithoutExtension(path) == BenchmarkBuildIdentity.RESOURCE_NAME)
                {
                    throw new InvalidOperationException("Duplicate benchmark identity Resources name: " + path);
                }
            }
            HashFile(code, "Assets/_BENCHMARK/Editor/HdrpShadowPatch.json");
            foreach (string path in AssetDatabase.GetDependencies(scene, true))
            {
                if (!IsGeneratedPath(path))
                {
                    assets.Add(new BenchmarkEntry { key = path, value = AssetDatabase.GetAssetDependencyHash(path).ToString() });
                }
            }
            foreach (string path in new[] { "ProjectSettings/ProjectSettings.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/TimeManager.asset", "Packages/manifest.json", "Packages/packages-lock.json" })
            {
                HashFile(settings, path);
            }
            foreach (string path in Directory.GetFiles("Assets/Settings", "*.asset", SearchOption.AllDirectories))
            {
                HashFile(settings, path.Replace('\\', '/'));
            }
            SourceDigest = Digest(code, assets, settings);
            SettingsDigest = Digest(settings);
            var runner = UnityEngine.Object.FindObjectsByType<BenchmarkRunner>()[0];
            var metadata = BenchmarkEnvironment.Capture(runner.Scenario, runner.WorkingCamera, runner.Positions, null, 0);
            BenchmarkEditorSession.FillEnvironment(runner);
            metadata.gitSha = runner.BuildGitSha;
            metadata.gitDirty = runner.BuildGitDirty;
            var inputs = new Manifest { buildId = Guid.NewGuid().ToString("N"), utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, hdrp = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.render-pipelines.high-definition")?.version, gitSha = metadata.gitSha, gitDirty = metadata.gitDirty, scene = scene, sourceDigest = SourceDigest, renderSettingsDigest = SettingsDigest, sourceFiles = code, dependencyHashes = assets, settingsFiles = settings, metadata = metadata, generatedStampExclusions = new[] { STAMP_PATH, STAMP_PATH + ".meta", STAMP_FOLDER, STAMP_FOLDER + ".meta" }, identityMechanism = "Resources TextAsset packed into Player data; runtime validates stamp independently of cached serialized scenes against input manifest, BuildReport GUID and complete runtime artifact inventory." };
            _buildId = inputs.buildId;
            Directory.CreateDirectory(destination);
            string inputPath = Path.Combine(destination, BenchmarkBuildIdentity.INPUT_MANIFEST);
            BenchmarkOutput.WriteJson(inputPath, inputs);
            var stamp = new BenchmarkBuildStamp { buildId = inputs.buildId, sourceDigest = SourceDigest, renderSettingsDigest = SettingsDigest, inputManifestSha256 = BenchmarkBuildIdentity.HashFile(inputPath), gitSha = inputs.gitSha, gitDirty = inputs.gitDirty, unity = inputs.unity, hdrp = inputs.hdrp };
            Directory.CreateDirectory(STAMP_FOLDER);
            File.WriteAllText(STAMP_PATH, JsonUtility.ToJson(stamp, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(STAMP_PATH, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            File.Copy(STAMP_PATH, Path.Combine(destination, "embedded-stamp-input.json"));
            return SourceDigest;
        }

        public static string Finish(string destination, string executablePath, string applicationBuildGuid)
        {
            string inputPath = Path.Combine(destination, BenchmarkBuildIdentity.INPUT_MANIFEST);
            string stampInput = Path.Combine(destination, "embedded-stamp-input.json");
            var stamp = JsonUtility.FromJson<BenchmarkBuildStamp>(File.ReadAllText(stampInput));
            var input = JsonUtility.FromJson<BenchmarkBuildInputIdentity>(File.ReadAllText(inputPath));
            string actualInputHash = BenchmarkBuildIdentity.HashFile(inputPath);
            string stampHash = BenchmarkBuildIdentity.HashFile(stampInput);
            var artifacts = BenchmarkBuildIdentity.CollectArtifacts(destination, Path.GetFileNameWithoutExtension(executablePath) + "_Data");
            var manifest = new BenchmarkBuildArtifactManifest { buildId = stamp.buildId, sourceDigest = stamp.sourceDigest, inputManifestSha256 = actualInputHash, stampSha256 = stampHash, applicationBuildGuid = applicationBuildGuid, executable = Path.GetFileName(executablePath), artifacts = artifacts };
            var proof = BenchmarkBuildIdentity.Verify(stamp, stampHash, input, actualInputHash, manifest, artifacts, applicationBuildGuid, manifest.executable);
            if (proof.status != "Verified")
            {
                throw new InvalidOperationException("Build artifact sealing failed: " + proof.reason);
            }
            string path = Path.Combine(destination, BenchmarkBuildIdentity.ARTIFACT_MANIFEST);
            BenchmarkOutput.WriteJson(path, manifest);
            BenchmarkOutput.WriteJson(Path.Combine(destination, "scene-processing-evidence.json"), new SceneProcessingEvidence { buildId = stamp.buildId, applicationBuildGuid = applicationBuildGuid, callbackBuildId = _buildId, callbackCount = _sceneCallbackCount, events = new List<SceneProcessingEvent>(SCENE_EVENTS), interpretation = "OnProcessScene is incremental and may be skipped when cached scene data is reused. Zero callbacks is evidence of no callback execution observed in this build, not by itself proof of the historical cause. Authoritative identity comes from independently rebuilt Resources data, never these serialized scene fields." });
            return path;
        }

        public static void RecordScene(string scene, string runner, string before, string after)
        {
            SCENE_EVENTS.Add(new SceneProcessingEvent { buildId = _buildId, scene = scene, sceneGuid = AssetDatabase.AssetPathToGUID(scene), runner = runner, serializedDigestBefore = before, serializedDigestAfter = after });
        }

        public static void RecordSceneCallback()
        {
            _sceneCallbackCount++;
        }

        public static bool IsGeneratedPath(string path)
        {
            return path == STAMP_PATH || path == STAMP_PATH + ".meta" || path == STAMP_FOLDER || path == STAMP_FOLDER + ".meta";
        }

        private static void HashFile(List<BenchmarkEntry> entries, string path)
        {
            entries.Add(ReadInputFile(path));
        }

        public static BenchmarkEntry ReadInputFile(string path)
        {
            string resolved = ResolveInputPath(path);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException("Cannot hash required build input: " + path, resolved);
            }
            return new BenchmarkEntry { key = path, value = BenchmarkBuildIdentity.HashFile(resolved) };
        }

        public static string ResolveInputPath(string path)
        {
            if (!path.StartsWith("Packages/", StringComparison.Ordinal) || path == "Packages/manifest.json" || path == "Packages/packages-lock.json")
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            }
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
            {
                throw new InvalidOperationException("Cannot resolve registered UPM build input: " + path);
            }
            string prefix = "Packages/" + package.name + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new ArgumentException("Build input does not belong to its resolved package: " + path);
            }
            string root = Path.GetFullPath(package.resolvedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Path.Combine(root, path.Substring(prefix.Length)));
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Build input escaped its resolved package: " + path);
            }
            return resolved;
        }

        private static string Digest(params List<BenchmarkEntry>[] groups)
        {
            var text = new StringBuilder();
            foreach (var group in groups)
            {
                group.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
                foreach (var entry in group)
                {
                    text.Append(entry.key).Append('=').Append(entry.value).Append('\n');
                }
            }
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        [Serializable]
        private sealed class Manifest
        {
            public int formatVersion = 1;
            public string buildId;
            public string utc;
            public string unity;
            public string hdrp;
            public string gitSha;
            public string gitDirty;
            public string scene;
            public string sourceDigest;
            public string renderSettingsDigest;
            public List<BenchmarkEntry> sourceFiles;
            public List<BenchmarkEntry> dependencyHashes;
            public List<BenchmarkEntry> settingsFiles;
            public BenchmarkMetadata metadata;
            public string[] generatedStampExclusions;
            public string identityMechanism;
        }

        [Serializable]
        private sealed class SceneProcessingEvidence
        {
            public string buildId;
            public string applicationBuildGuid;
            public string callbackBuildId;
            public int callbackCount;
            public List<SceneProcessingEvent> events;
            public string interpretation;
        }

        [Serializable]
        private sealed class SceneProcessingEvent
        {
            public string buildId;
            public string scene;
            public string sceneGuid;
            public string runner;
            public string serializedDigestBefore;
            public string serializedDigestAfter;
        }
    }
}
