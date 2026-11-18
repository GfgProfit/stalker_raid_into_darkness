using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using XRay.Importer.Editor.Rendering;

namespace XRay.Importer.Tests
{
    public sealed class HdrpShadowBindingTests
    {
        private const string RESOLVER_TYPE = "UnityEngine.Rendering.HighDefinition.HDShadowLightIndexResolver, Unity.RenderPipelines.HighDefinition.Runtime";
        private const string CAMERA_SCOPE_TYPE = "UnityEngine.Rendering.HighDefinition.HDShadowCullingCameraScope";
        private const string ARCHIVE_ROOT = @"E:\Projects\stalker_raid_into_darkness_Archive";

        [Test]
        public void ParticleTemplateRequestKeepsTheNativeCasterWithPreparedSplits()
        {
            using var entities = new NativeArray<int>(new[] { -1, -1, -1, -1, -1, -1, -1, 53, 53 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(entities.Length, Allocator.Temp);
            try
            {
                BuildMap(entities, ref map);
                int resolved = Resolve(53, 7, map[53], entities);
                int[] preparedSplitCounts = { 0, 0, 0, 0, 0, 0, 0, 6, 0 };
                Assert.That(resolved, Is.EqualTo(7));
                Assert.That(preparedSplitCounts[resolved], Is.EqualTo(6));
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void UniqueEntityStillRemapsAnOlderNativeIndex()
        {
            using var entities = new NativeArray<int>(new[] { 99, -1, -1, 53 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(entities.Length, Allocator.Temp);
            try
            {
                BuildMap(entities, ref map);
                Assert.That(Resolve(53, 0, map[53], entities), Is.EqualTo(3));
            }
            finally
            {
                map.Dispose();
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EveryRequestOfASharedTemplateRetainsItsOwnNativeIndex(int requestLightIndex)
        {
            using var entities = new NativeArray<int>(new[] { 53, 53, 53, 99 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(entities.Length, Allocator.Temp);
            try
            {
                BuildMap(entities, ref map);
                Assert.That(Resolve(53, requestLightIndex, map[53], entities), Is.EqualTo(requestLightIndex));
                Assert.That(Resolve(99, 0, map[99], entities), Is.EqualTo(3));
            }
            finally
            {
                map.Dispose();
            }
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(3)]
        public void AmbiguousMappingCannotSelectAnOutOfRangeOrDifferentOwner(int requestLightIndex)
        {
            using var entities = new NativeArray<int>(new[] { 99, 53, 53 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(entities.Length, Allocator.Temp);
            try
            {
                BuildMap(entities, ref map);
                var error = Assert.Throws<TargetInvocationException>(() => Resolve(53, requestLightIndex, map[53], entities));
                Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void RebuildingForAnotherCameraReplacesThePreviousCameraMapping()
        {
            using var first = new NativeArray<int>(new[] { 53, 53, 99 }, Allocator.Temp);
            using var second = new NativeArray<int>(new[] { -1, 99, 53 }, Allocator.Temp);
            var map = new NativeHashMap<int, int>(first.Length, Allocator.Temp);
            try
            {
                BuildMap(first, ref map);
                BuildMap(second, ref map);
                Assert.That(Resolve(53, 0, map[53], second), Is.EqualTo(2));
                Assert.That(Resolve(99, 2, map[99], second), Is.EqualTo(1));
                Assert.That(map.ContainsKey(-1), Is.False);
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void CreatedPatchSupportsFreshInstallKnownMigrationAndSecondPass()
        {
            byte[] previous = Encoding.UTF8.GetBytes("previous owner scope\n");
            byte[] target = Encoding.UTF8.GetBytes("current owner scope\n");
            object file = PatchFile(true, null, target, previous);
            Assert.That(PrepareReplacement(file, null), Is.EqualTo(target));
            Assert.That(PrepareReplacement(file, previous), Is.EqualTo(target));
            Assert.That(PrepareReplacement(file, target), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CreatedPatchRefusesUnknownOrUndeclaredPreviousBytes(bool declaredPrevious)
        {
            byte[] previous = Encoding.UTF8.GetBytes("known previous owner scope\n");
            byte[] target = Encoding.UTF8.GetBytes("current owner scope\n");
            object file = PatchFile(true, null, target, declaredPrevious ? previous : null);
            if (!declaredPrevious) SetPatchField(file, "originalSha256", Hash(previous));
            byte[] current = declaredPrevious ? Encoding.UTF8.GetBytes("unrecognized owner scope\n") : previous;
            var error = Assert.Throws<TargetInvocationException>(() => PrepareReplacement(file, current));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ExistingPatchKeepsOriginalGuardAndSecondPassIdempotence()
        {
            byte[] original = Encoding.UTF8.GetBytes("original culling call\n");
            byte[] target = Encoding.UTF8.GetBytes("scoped culling call\n");
            object file = PatchFile(false, original, target);
            Assert.That(PrepareReplacement(file, original), Is.EqualTo(target));
            Assert.That(PrepareReplacement(file, target), Is.Null);
            Assert.That(original, Is.EqualTo(Encoding.UTF8.GetBytes("original culling call\n")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExistingPatchRefusesMissingOrUnknownSource(bool missing)
        {
            byte[] original = Encoding.UTF8.GetBytes("original culling call\n");
            byte[] target = Encoding.UTF8.GetBytes("scoped culling call\n");
            object file = PatchFile(false, original, target);
            byte[] current = missing ? null : Encoding.UTF8.GetBytes("unrecognized culling call\n");
            var error = Assert.Throws<TargetInvocationException>(() => PrepareReplacement(file, current));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReplacementTargetHashMustMatchPreparedBytes(bool create)
        {
            byte[] original = create ? null : Encoding.UTF8.GetBytes("original culling call\n");
            byte[] target = Encoding.UTF8.GetBytes("scoped culling call\n");
            object file = PatchFile(create, original, target);
            SetPatchField(file, "patchedSha256", Hash(Encoding.UTF8.GetBytes("different target\n")));
            var error = Assert.Throws<TargetInvocationException>(() => PrepareReplacement(file, original));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void TransactionRestoresExactBytesAndAttributesAfterLaterWriteFailure()
        {
            string output = Environment.GetEnvironmentVariable("RVM_NATIVE_VALIDATION_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) Assert.Ignore("RVM_NATIVE_VALIDATION_OUTPUT must select a task directory under Archive for the patch transaction fixture.");
            string directory = Path.Combine(Path.GetFullPath(output), "HdrpOwnerHook_" + Guid.NewGuid().ToString("N"));
            AssertArchivePath(directory);
            string existingPath = Path.Combine(directory, "existing.bin");
            string createdPath = Path.Combine(directory, "created.bin");
            string blockedPath = Path.Combine(directory, "blocked");
            byte[] original = { 0, 1, 2, 127, 200, 255 };
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllBytes(existingPath, original);
                File.SetAttributes(existingPath, File.GetAttributes(existingPath) | FileAttributes.ReadOnly);
                FileAttributes attributes = File.GetAttributes(existingPath);
                Directory.CreateDirectory(blockedPath);
                Type pendingType = typeof(HdrpShadowBindingPatch).GetNestedType("PendingWrite", BindingFlags.NonPublic);
                Assert.That(pendingType, Is.Not.Null);
                var writes = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(pendingType));
                AddWrite(writes, pendingType, existingPath, original, new byte[] { 7, 8, 9 }, attributes);
                AddWrite(writes, pendingType, createdPath, null, new byte[] { 10, 11, 12 }, FileAttributes.Normal);
                AddWrite(writes, pendingType, blockedPath, null, new byte[] { 13 }, FileAttributes.Normal);
                MethodInfo apply = typeof(HdrpShadowBindingPatch).GetMethod("ApplyWrites", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(apply, Is.Not.Null);
                var error = Assert.Throws<TargetInvocationException>(() => apply.Invoke(null, new object[] { writes }));
                Assert.That(error.InnerException, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
                Assert.That(File.ReadAllBytes(existingPath), Is.EqualTo(original));
                Assert.That(File.GetAttributes(existingPath), Is.EqualTo(attributes));
                Assert.That(File.Exists(createdPath), Is.False);
                Assert.That(Directory.Exists(blockedPath), Is.True);
                Assert.That(Directory.GetFileSystemEntries(blockedPath), Is.Empty);
            }
            finally
            {
                DeleteFixtureFile(existingPath);
                DeleteFixtureFile(createdPath);
                DeleteEmptyFixtureDirectory(blockedPath);
                DeleteEmptyFixtureDirectory(directory);
            }
        }

        [Test]
        public void CameraScopePreservesExactIdentityThroughNestedThrowAndRestoration()
        {
            Type scope = typeof(HDRenderPipelineAsset).Assembly.GetType(CAMERA_SCOPE_TYPE, true);
            PropertyInfo version = scope.GetProperty("ApiVersion", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo current = scope.GetProperty("CurrentCamera", BindingFlags.Public | BindingFlags.Static);
            MethodInfo set = scope.GetMethod("SetCurrentCamera", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(version, Is.Not.Null);
            Assert.That(current, Is.Not.Null);
            Assert.That(set, Is.Not.Null);
            Assert.That(version.GetValue(null), Is.EqualTo(1));
            Assert.That(current.PropertyType, Is.EqualTo(typeof(Camera)));
            Assert.That(current.GetSetMethod(true), Is.Null);
            Camera saved = (Camera)current.GetValue(null);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("HDRP shadow owner identity fixture");
            SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                var firstObject = new GameObject("First identical owner camera");
                firstObject.transform.SetParent(root.transform);
                Camera first = firstObject.AddComponent<Camera>();
                var secondObject = new GameObject("Second identical owner camera");
                secondObject.transform.SetParent(root.transform);
                Camera second = secondObject.AddComponent<Camera>();
                first.enabled = false;
                second.enabled = false;
                Assert.That(first.transform.position, Is.EqualTo(second.transform.position));
                Assert.That(first.transform.rotation, Is.EqualTo(second.transform.rotation));
                Assert.That(first.fieldOfView, Is.EqualTo(second.fieldOfView));
                Assert.That(first.nearClipPlane, Is.EqualTo(second.nearClipPlane));
                Assert.That(first.farClipPlane, Is.EqualTo(second.farClipPlane));
                Assert.That(first.orthographic, Is.EqualTo(second.orthographic));
                Assert.That(first.cullingMask, Is.EqualTo(second.cullingMask));
                Assert.That(first.projectionMatrix, Is.EqualTo(second.projectionMatrix));
                Assert.That(first.GetEntityId(), Is.Not.EqualTo(second.GetEntityId()));
                set.Invoke(null, new object[] { first });
                Assert.That(current.GetValue(null), Is.SameAs(first));
                Camera outer = (Camera)current.GetValue(null);
                bool thrown = false;
                try
                {
                    set.Invoke(null, new object[] { second });
                    Assert.That(current.GetValue(null), Is.SameAs(second));
                    throw new InvalidOperationException("Nested owner fixture control.");
                }
                catch (InvalidOperationException)
                {
                    thrown = true;
                }
                finally
                {
                    set.Invoke(null, new object[] { outer });
                }
                Assert.That(thrown, Is.True);
                Assert.That(current.GetValue(null), Is.SameAs(first));
            }
            finally
            {
                try
                {
                    set.Invoke(null, new object[] { saved });
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            Assert.That(current.GetValue(null), Is.SameAs(saved));
        }

        private static object PatchFile(bool create, byte[] original, byte[] target, byte[] previous = null)
        {
            Type type = typeof(HdrpShadowBindingPatch).GetNestedType("PatchFile", BindingFlags.NonPublic);
            Assert.That(type, Is.Not.Null);
            object file = Activator.CreateInstance(type, true);
            SetPatchField(file, "path", "OwnerScope.cs");
            SetPatchField(file, "create", create);
            SetPatchField(file, "originalSha256", original != null ? Hash(original) : null);
            SetPatchField(file, "previousPatchedSha256", previous != null ? Hash(previous) : null);
            SetPatchField(file, "patchedSha256", Hash(target));
            SetPatchField(file, "content", Encoding.UTF8.GetString(target));
            if (!create)
            {
                Type editType = typeof(HdrpShadowBindingPatch).GetNestedType("PatchEdit", BindingFlags.NonPublic);
                Assert.That(editType, Is.Not.Null);
                Array edits = Array.CreateInstance(editType, 1);
                object edit = Activator.CreateInstance(editType, true);
                editType.GetField("find").SetValue(edit, Encoding.UTF8.GetString(original));
                editType.GetField("replace").SetValue(edit, Encoding.UTF8.GetString(target));
                edits.SetValue(edit, 0);
                SetPatchField(file, "edits", edits);
            }
            return file;
        }

        private static void SetPatchField(object file, string name, object value)
        {
            FieldInfo field = file.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(file, value);
        }

        private static byte[] PrepareReplacement(object file, byte[] original)
        {
            MethodInfo method = typeof(HdrpShadowBindingPatch).GetMethod("PrepareReplacement", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (byte[])method.Invoke(null, new object[] { file, original, "OwnerScope.cs" });
        }

        private static void AddWrite(IList writes, Type type, string path, byte[] original, byte[] replacement, FileAttributes attributes)
        {
            object write = Activator.CreateInstance(type, true);
            type.GetProperty("Path").SetValue(write, path);
            type.GetProperty("Original").SetValue(write, original);
            type.GetProperty("Replacement").SetValue(write, replacement);
            type.GetProperty("Attributes").SetValue(write, attributes);
            writes.Add(write);
        }

        private static string Hash(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static void AssertArchivePath(string path)
        {
            string archive = Path.GetFullPath(ARCHIVE_ROOT).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(path);
            Assert.That(full.StartsWith(archive + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Is.True, "The patch fixture must stay inside Archive.");
            var directory = new DirectoryInfo(Directory.Exists(full) ? full : Path.GetDirectoryName(full));
            while (directory != null)
            {
                if (directory.Exists) Assert.That((int)(directory.Attributes & FileAttributes.ReparsePoint), Is.Zero, directory.FullName);
                if (string.Equals(directory.FullName.TrimEnd(Path.DirectorySeparatorChar), archive, StringComparison.OrdinalIgnoreCase)) return;
                directory = directory.Parent;
            }
            Assert.Fail("The patch fixture did not resolve inside Archive.");
        }

        private static void DeleteFixtureFile(string path)
        {
            AssertArchivePath(path);
            if (!File.Exists(path)) return;
            Assert.That((int)(File.GetAttributes(path) & FileAttributes.ReparsePoint), Is.Zero, path);
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }

        private static void DeleteEmptyFixtureDirectory(string path)
        {
            AssertArchivePath(path);
            if (!Directory.Exists(path)) return;
            Assert.That(Directory.GetFileSystemEntries(path), Is.Empty, path);
            Directory.Delete(path, false);
        }

        private static void BuildMap(NativeArray<int> entities, ref NativeHashMap<int, int> map)
        {
            var arguments = new object[] { entities, entities.Length, map };
            ResolverMethod("BuildMap").Invoke(null, arguments);
            map = (NativeHashMap<int, int>)arguments[2];
        }

        private static int Resolve(int dataIndex, int requestLightIndex, int mappedLightIndex, NativeArray<int> entities)
        {
            return (int)ResolverMethod("Resolve").Invoke(null, new object[] { dataIndex, requestLightIndex, mappedLightIndex, entities, entities.Length });
        }

        private static MethodInfo ResolverMethod(string name)
        {
            return Type.GetType(RESOLVER_TYPE, true).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        }
    }
}
