using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Core.Anm;

namespace XRay.Importer.Tests
{
    public sealed class AnmPlayerTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        [Test]
        public void PlayPauseResumeSeekAndStop()
        {
            XRayAnmAsset asset = CreateAsset();
            XRayAnmPlayer player = CreatePlayer(out Transform target);
            target.localPosition = new Vector3(5.0f, 0.0f, 0.0f);
            player.Play(asset);
            Assert.IsTrue(player.IsPlaying);
            player.Tick(0.5f);
            AssertPose(asset, 0.5f, target);
            player.Pause();
            player.Tick(0.5f);
            Assert.AreEqual(0.5f, player.Time, 1e-5f);
            player.Resume();
            player.Speed = 2.0f;
            player.Tick(0.25f);
            Assert.AreEqual(1.0f, player.Time, 1e-5f);
            player.Seek(1.5f);
            AssertPose(asset, 1.5f, target);
            player.Stop();
            Assert.IsFalse(player.IsPlaying);
            Assert.AreEqual(new Vector3(5.0f, 0.0f, 0.0f), target.localPosition);
            Assert.AreEqual(Quaternion.identity, target.localRotation);
        }

        [Test]
        public void LoopWrapsAndFinishRestoresRestPose()
        {
            XRayAnmAsset asset = CreateAsset();
            XRayAnmPlayer player = CreatePlayer(out Transform target);
            player.Loop = true;
            player.Play(asset, 1.5f);
            player.Tick(1.0f);
            Assert.AreEqual(0.5f, player.Time, 1e-5f);
            player.Loop = false;
            player.Tick(5.0f);
            Assert.IsFalse(player.IsPlaying);
            Assert.AreEqual(Vector3.zero, target.localPosition);
        }

        [Test]
        public void WeightBlendsFromRestPose()
        {
            XRayAnmAsset asset = CreateAsset();
            XRayAnmPlayer player = CreatePlayer(out Transform target);
            player.Weight = 0.5f;
            player.Play(asset, 1.0f);
            asset.Evaluate(1.0f, out Vector3 position, out Quaternion rotation);
            Assert.Less((target.localPosition - position * 0.5f).magnitude, 1e-5f);
            Assert.Less(Quaternion.Angle(target.localRotation, Quaternion.Slerp(Quaternion.identity, rotation, 0.5f)), 1e-3f);
        }

        [Test]
        public void AdditiveEffectOnPivotKeepsBaseCameraControl()
        {
            XRayAnmAsset asset = CreateAsset();
            var root = new GameObject("CameraRoot");
            _created.Add(root);
            var pivot = new GameObject("CameraEffectsPivot");
            pivot.transform.SetParent(root.transform, false);
            var camera = new GameObject("MainCamera");
            camera.transform.SetParent(pivot.transform, false);
            XRayAnmPlayer player = pivot.AddComponent<XRayAnmPlayer>();
            player.DriveSelf = false;
            player.Mode = XRayAnmApplyMode.Additive;
            root.transform.localRotation = Quaternion.Euler(0.0f, 40.0f, 0.0f);
            player.Play(asset);
            Assert.Less(pivot.transform.localPosition.magnitude, 1e-5f, "Relative to the first frame the effect starts at the rest pose.");
            player.Tick(1.0f);
            root.transform.localRotation = Quaternion.Euler(-10.0f, 75.0f, 0.0f);
            asset.Evaluate(0.0f, out Vector3 firstPosition, out Quaternion firstRotation);
            asset.Evaluate(1.0f, out Vector3 position, out Quaternion rotation);
            Quaternion expectedRotation = Quaternion.Inverse(firstRotation) * rotation;
            Assert.Less(Quaternion.Angle(pivot.transform.localRotation, expectedRotation), 1e-3f);
            Quaternion world = root.transform.rotation * expectedRotation;
            Assert.Less(Quaternion.Angle(camera.transform.rotation, world), 1e-3f);
            XRayAnmTrack shake = player.PlayEffect(asset, 0.5f);
            player.Tick(0.25f);
            Assert.AreEqual(2, player.Effects.Count + (player.IsPlaying ? 1 : 0));
            player.StopEffect(shake);
            player.Stop();
            Assert.AreEqual(Vector3.zero, pivot.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, pivot.transform.localRotation);
            Assert.Less(Quaternion.Angle(camera.transform.rotation, root.transform.rotation), 1e-4f);
        }

        private XRayAnmAsset CreateAsset()
        {
            XRayAnmMotion motion = XRayAnmReader.ReadFile(AnmImportTests.CreateMotionFile(30.0f, 0, 60), "player.anm");
            var asset = ScriptableObject.CreateInstance<XRayAnmAsset>();
            asset.SetImportData("player.anm", motion, 1.0f, XRayAnmSemantics.Engine, true, null);
            _created.Add(asset);
            return asset;
        }

        private XRayAnmPlayer CreatePlayer(out Transform target)
        {
            var gameObject = new GameObject("anm_player");
            _created.Add(gameObject);
            XRayAnmPlayer player = gameObject.AddComponent<XRayAnmPlayer>();
            player.DriveSelf = false;
            target = gameObject.transform;
            return player;
        }

        private static void AssertPose(XRayAnmAsset asset, float time, Transform target)
        {
            asset.Evaluate(time, out Vector3 position, out Quaternion rotation);
            Assert.Less((target.localPosition - position).magnitude, 1e-5f);
            Assert.Less(Quaternion.Angle(target.localRotation, rotation), 1e-3f);
        }
    }
}
