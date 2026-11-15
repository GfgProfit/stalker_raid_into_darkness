using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEngine;

namespace XRay.Importer.Editor.Rendering
{
    [InitializeOnLoad]
    public sealed class HdrpShadowBindingPatch : IPreprocessBuildWithReport
    {
        private const string PAYLOAD_PATH = "Assets/_Tools/X-Ray OGF OMG Importer/Editor/Rendering/HdrpShadowBindingPatch.json";
        private const string PACKAGE_NAME = "com.unity.render-pipelines.high-definition";
        private const string PACKAGE_VERSION = "17.6.0";

        private static bool _queued;
        private static string _lastError;

        public int callbackOrder => -10000;

        static HdrpShadowBindingPatch()
        {
            Events.registeredPackages += OnPackagesRegistered;
            ScheduleApply();
        }

        [MenuItem("Tools/RVM/Rendering/Apply HDRP Shadow Binding Fix")]
        private static void ApplyMenu()
        {
            Debug.Log(Apply());
        }

        public static string Apply()
        {
            var payload = ReadPayload();
            string root = ResolvePackage();
            var writes = new List<PendingWrite>();
            foreach (var file in payload.files)
            {
                string path = ResolveFile(root, file.path);
                byte[] original = File.Exists(path) ? File.ReadAllBytes(path) : null;
                if (original != null && Hash(original) == file.patchedSha256)
                {
                    continue;
                }
                byte[] replacement;
                if (file.create)
                {
                    if (original != null)
                    {
                        throw new InvalidOperationException("Refusing to replace an unknown HDRP patch file: " + path);
                    }
                    replacement = new UTF8Encoding(false).GetBytes(file.content);
                }
                else
                {
                    if (original == null || Hash(original) != file.originalSha256)
                    {
                        throw new InvalidOperationException("HDRP original/patched SHA256 guard failed: " + path);
                    }
                    string text = new UTF8Encoding(false, true).GetString(original);
                    foreach (var edit in file.edits)
                    {
                        int first = text.IndexOf(edit.find, StringComparison.Ordinal);
                        if (first < 0 || text.IndexOf(edit.find, first + edit.find.Length, StringComparison.Ordinal) >= 0)
                        {
                            throw new InvalidOperationException("HDRP patch anchor must occur exactly once: " + file.path);
                        }
                        text = text.Substring(0, first) + edit.replace + text.Substring(first + edit.find.Length);
                    }
                    replacement = new UTF8Encoding(false).GetBytes(text);
                }
                if (Hash(replacement) != file.patchedSha256)
                {
                    throw new InvalidOperationException("HDRP patch output SHA256 guard failed: " + file.path);
                }
                writes.Add(new PendingWrite { Path = path, Original = original, Replacement = replacement, Attributes = original == null ? FileAttributes.Normal : File.GetAttributes(path) });
            }
            if (writes.Count == 0)
            {
                return "HDRP 17.6.0 shadow binding patch already applied and SHA256 verified.";
            }
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var write in writes)
                {
                    write.Applied = true;
                    Directory.CreateDirectory(Path.GetDirectoryName(write.Path));
                    if (File.Exists(write.Path))
                    {
                        File.SetAttributes(write.Path, write.Attributes & ~FileAttributes.ReadOnly);
                    }
                    File.WriteAllBytes(write.Path, write.Replacement);
                    File.SetAttributes(write.Path, write.Attributes);
                }
            }
            catch
            {
                foreach (var write in writes)
                {
                    if (!write.Applied)
                    {
                        continue;
                    }
                    if (File.Exists(write.Path))
                    {
                        File.SetAttributes(write.Path, FileAttributes.Normal);
                    }
                    if (write.Original == null)
                    {
                        if (File.Exists(write.Path))
                        {
                            File.Delete(write.Path);
                        }
                    }
                    else
                    {
                        File.WriteAllBytes(write.Path, write.Original);
                        File.SetAttributes(write.Path, write.Attributes);
                    }
                }
                throw;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();
            RequireApplied();
            return "Applied and SHA256 verified HDRP 17.6.0 shadow binding patch to " + root + ". Wait for Unity script compilation before building.";
        }

        public static bool IsApplied(out string reason)
        {
            try
            {
                var payload = ReadPayload();
                string root = ResolvePackage();
                foreach (var file in payload.files)
                {
                    string path = ResolveFile(root, file.path);
                    if (!File.Exists(path) || Hash(File.ReadAllBytes(path)) != file.patchedSha256)
                    {
                        reason = "HDRP shadow binding patch is absent or differs: " + file.path;
                        return false;
                    }
                }
                reason = "HDRP 17.6.0 shadow binding patch verified against project-owned payload.";
                return true;
            }
            catch (Exception exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        public static void RequireApplied()
        {
            if (!IsApplied(out string reason))
            {
                throw new InvalidOperationException(reason + " Run Tools/RVM/Rendering/Apply HDRP Shadow Binding Fix and wait for compilation before building.");
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!IsApplied(out string reason))
            {
                ScheduleApply();
                throw new BuildFailedException(reason + " Build refused before compiling a Player with missing shadow binding fix. Apply the project patch, wait for script reload, then rebuild.");
            }
        }

        private static void OnPackagesRegistered(PackageRegistrationEventArgs args)
        {
            ScheduleApply();
        }

        private static void ScheduleApply()
        {
            if (_queued)
            {
                return;
            }
            _queued = true;
            EditorApplication.delayCall += AutoApply;
        }

        private static void AutoApply()
        {
            _queued = false;
            try
            {
                Apply();
                _lastError = null;
            }
            catch (Exception exception)
            {
                if (_lastError != exception.Message)
                {
                    _lastError = exception.Message;
                    Debug.LogWarning("HDRP shadow patch not applied: " + exception.Message);
                }
            }
        }

        private static PatchPayload ReadPayload()
        {
            string path = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), PAYLOAD_PATH);
            var payload = JsonUtility.FromJson<PatchPayload>(File.ReadAllText(path));
            if (payload == null || payload.version != 1 || payload.packageName != PACKAGE_NAME || payload.packageVersion != PACKAGE_VERSION || payload.files == null || payload.files.Length == 0)
            {
                throw new InvalidOperationException("Invalid project-owned HDRP patch payload: " + path);
            }
            return payload;
        }

        private static string ResolvePackage()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + PACKAGE_NAME);
            if (package == null || package.name != PACKAGE_NAME || package.version != PACKAGE_VERSION)
            {
                throw new InvalidOperationException("Expected resolved HDRP " + PACKAGE_VERSION + ". Wait for Package Manager resolution.");
            }
            string cache = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/PackageCache"));
            string root = Path.GetFullPath(package.resolvedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!root.StartsWith(cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith(PACKAGE_NAME + "@", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("HDRP patch refuses non-project-cache package, including user embedded packages: " + root);
            }
            for (var directory = new DirectoryInfo(root); directory != null && directory.FullName.StartsWith(cache, StringComparison.OrdinalIgnoreCase); directory = directory.Parent)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("HDRP patch refuses reparse-point traversal: " + directory.FullName);
                }
            }
            return root;
        }

        private static string ResolveFile(string root, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative))
            {
                throw new InvalidOperationException("Invalid HDRP patch relative path.");
            }
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("HDRP patch path leaves resolved package: " + relative);
            }
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("HDRP patch refuses a file reparse point: " + path);
            }
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)); directory != null && directory.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase); directory = directory.Parent)
            {
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("HDRP patch refuses directory reparse-point traversal: " + directory.FullName);
                }
            }
            return path;
        }

        private static string Hash(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        [Serializable]
        private sealed class PatchPayload
        {
            public int version;
            public string packageName;
            public string packageVersion;
            public PatchFile[] files;
        }

        [Serializable]
        private sealed class PatchFile
        {
            public string path;
            public bool create;
            public string originalSha256;
            public string patchedSha256;
            public string content;
            public PatchEdit[] edits;
        }

        [Serializable]
        private sealed class PatchEdit
        {
            public string find;
            public string replace;
        }

        private sealed class PendingWrite
        {
            public string Path { get; set; }
            public byte[] Original { get; set; }
            public byte[] Replacement { get; set; }
            public FileAttributes Attributes { get; set; }
            public bool Applied { get; set; }
        }
    }
}
