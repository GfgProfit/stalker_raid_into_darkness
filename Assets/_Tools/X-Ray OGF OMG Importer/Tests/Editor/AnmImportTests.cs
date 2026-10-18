using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using XRay.Importer.Core.Anm;
using XRay.Importer.Editor;

namespace XRay.Importer.Tests
{
    public sealed class AnmImportTests
    {
        private const string FOLDER = XRayImportTestScope.ROOT_FOLDER + "/anm";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(FOLDER);
            if (AssetDatabase.IsValidFolder(XRayImportTestScope.ROOT_FOLDER) && AssetDatabase.FindAssets(string.Empty, new[] { XRayImportTestScope.ROOT_FOLDER }).Length == 0)
            {
                AssetDatabase.DeleteAsset(XRayImportTestScope.ROOT_FOLDER);
            }
        }

        [Test]
        public void AnmImportsPreviewClipAndMotionAsset()
        {
            string path = Import("camera_shoot.anm", CreateMotionFile(24.0f, 10, 58));
            var preview = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
            Assert.IsNotNull(preview);
            Assert.IsNotNull(preview.GetComponent<XRayAnmPlayer>());
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            var asset = AssetDatabase.LoadAssetAtPath<XRayAnmAsset>(path);
            Assert.IsNotNull(clip);
            Assert.IsNotNull(asset);
            Assert.AreEqual(clip, asset.Clip);
            Assert.AreEqual(2.0f, clip.length, 1e-4f);
            Assert.AreEqual(24.0f, clip.frameRate, 1e-4f);
            Assert.IsFalse(clip.isLooping);
            Assert.IsTrue(asset.HasCurves);
            Assert.AreEqual(10, asset.FrameStart);
            var probe = new GameObject("probe");
            try
            {
                XRayAnmMotion motion = asset.GetMotion();
                AnmErrorStats stats = AnmValidationTests.CompareClip(clip, motion, XRayAnmSemantics.Engine, probe, 8);
                Assert.Less(stats.MaxPosition, 1e-4, stats.ToString());
                Assert.Less(stats.MaxRotationDegrees, 0.01, stats.ToString());
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        [Test]
        public void ReimportKeepsSubAssetsWithoutDuplicates()
        {
            string path = Import("stable.anm", CreateMotionFile(30.0f, 0, 60));
            List<string> before = DescribeSubAssets(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            CollectionAssert.AreEqual(before, DescribeSubAssets(path));
        }

        [Test]
        public void ImportSettingsChangeTheResult()
        {
            string path = Import("settings.anm", CreateMotionFile(30.0f, 0, 60));
            var importer = (XRayAnmImporter)AssetImporter.GetAtPath(path);
            var settings = new SerializedObject(importer);
            settings.FindProperty("_createPreviewObject").boolValue = false;
            settings.FindProperty("_loop").boolValue = true;
            settings.FindProperty("_scale").floatValue = 2.0f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
            Assert.IsInstanceOf<XRayAnmAsset>(AssetDatabase.LoadMainAssetAtPath(path));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Assert.IsTrue(clip.isLooping);
            var asset = AssetDatabase.LoadAssetAtPath<XRayAnmAsset>(path);
            asset.Evaluate(1.0f, out Vector3 scaled, out Quaternion _);
            Assert.AreEqual(2.0f, asset.Scale);
            settings = new SerializedObject(importer);
            settings.FindProperty("_generateAnimationClip").boolValue = false;
            settings.FindProperty("_preserveOriginalCurves").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            importer.SaveAndReimport();
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
            Assert.IsFalse(AssetDatabase.LoadAssetAtPath<XRayAnmAsset>(path).HasCurves);
            Assert.Greater(scaled.magnitude, 0.0f);
        }

        [Test]
        public void CorruptedAnmLogsErrorWithoutClip()
        {
            byte[] data = CreateMotionFile(30.0f, 0, 60);
            var truncated = new byte[data.Length / 2];
            System.Array.Copy(data, truncated, truncated.Length);
            LogAssert.Expect(LogType.Error, new Regex("broken\\.anm"));
            string path = Import("broken.anm", truncated);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<XRayAnmAsset>(path));
        }

        [Test]
        public void UnknownVersionLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex("version 9"));
            string path = Import("future.anm", AnmTestWriter.Write("future", 0, 30, 30.0f, 9, AnmCoreTests.DistinctChannels()));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
        }

        public static byte[] CreateMotionFile(float fps, int frameStart, int frameEnd)
        {
            double start = frameStart / (double)fps;
            double end = frameEnd / (double)fps;
            var channels = new List<AnmTestEnvelope>
            {
                new AnmTestEnvelope().Key(start, 0.0, XRayAnmShape.Tcb).Key(start + 0.5, 0.2, XRayAnmShape.Tcb).Key(end, -0.1, XRayAnmShape.Tcb),
                new AnmTestEnvelope().Key(start, 0.0, XRayAnmShape.Linear).Key(end, 0.3, XRayAnmShape.Linear),
                new AnmTestEnvelope().Key(start, 0.1, XRayAnmShape.Stepped).Key(start + 1.0, -0.2, XRayAnmShape.Stepped).Key(end, 0.0, XRayAnmShape.Tcb),
                new AnmTestEnvelope().Key(start, 2.9, XRayAnmShape.Tcb).Key(start + 0.7, 3.3, XRayAnmShape.Tcb).Key(end, 6.6, XRayAnmShape.Tcb),
                new AnmTestEnvelope().Key(new XRayAnmKey { Time = start, Value = -0.3, Shape = XRayAnmShape.Hermite, Param1 = 0.5 }).Key(new XRayAnmKey { Time = end, Value = 0.4, Shape = XRayAnmShape.Hermite, Param0 = -0.5 }),
                new AnmTestEnvelope().Key(new XRayAnmKey { Time = start, Value = 0.0, Shape = XRayAnmShape.Bezier2D, Param2 = 0.3, Param3 = 0.2 }).Key(new XRayAnmKey { Time = end, Value = -0.5, Shape = XRayAnmShape.Bezier2D, Param0 = -0.4, Param1 = 0.1 })
            };
            return AnmTestWriter.Write("camera_shoot", frameStart, frameEnd, fps, XRayAnmFormat.VERSION_5, channels);
        }

        private static string Import(string fileName, byte[] data)
        {
            Directory.CreateDirectory(FOLDER);
            string path = FOLDER + "/" + fileName;
            File.WriteAllBytes(path, data);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return path;
        }

        private static List<string> DescribeSubAssets(string path)
        {
            var result = new List<string>();
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId);
                result.Add($"{asset.GetType().Name}:{asset.name}:{guid}:{localId}");
            }

            result.Sort();
            return result;
        }
    }
}
