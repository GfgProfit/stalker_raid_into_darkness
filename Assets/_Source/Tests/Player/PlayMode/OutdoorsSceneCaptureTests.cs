using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class OutdoorsSceneCaptureTests
    {
        private const string CAPTURE_VARIABLE = "STALKER_CAPTURE_DIR";
        private const int WIDTH = 960;
        private const int HEIGHT = 540;

        [UnityTest]
        public IEnumerator CaptureFirstPersonViews()
        {
            string folder = Environment.GetEnvironmentVariable(CAPTURE_VARIABLE);
            if (string.IsNullOrEmpty(folder))
            {
                Assert.Ignore(CAPTURE_VARIABLE + " is not set.");
            }

            LogAssert.ignoreFailingMessages = true;
            Directory.CreateDirectory(folder);
            SceneManager.LoadScene("OutdoorsScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1.0f);
            StalkerPlayerController player = UnityEngine.Object.FindAnyObjectByType<StalkerPlayerController>();
            Camera camera = player.CameraController.Camera;
            player.CameraController.SetLook(0.0f, 0.0f);
            yield return new WaitForSeconds(0.2f);
            Capture(camera, Path.Combine(folder, "fp_forward.png"));
            player.CameraController.SetLook(0.0f, 60.0f);
            yield return new WaitForSeconds(0.3f);
            Capture(camera, Path.Combine(folder, "fp_down_idle.png"));
            player.ScriptedWish = StalkerMoveFlags.Forward;
            yield return new WaitForSeconds(0.8f);
            Capture(camera, Path.Combine(folder, "fp_down_run.png"));
            player.ScriptedWish = StalkerMoveFlags.Forward | StalkerMoveFlags.Crouch;
            yield return new WaitForSeconds(0.8f);
            Capture(camera, Path.Combine(folder, "fp_down_crouch.png"));
            player.ScriptedWish = StalkerMoveFlags.None;
            player.CameraController.SetLook(0.0f, 85.0f);
            yield return new WaitForSeconds(0.5f);
            Capture(camera, Path.Combine(folder, "fp_down_steep.png"));
            Assert.That(player.ApplyOutfit("military_exo_outfit"), Is.True);
            player.CameraController.SetLook(0.0f, 60.0f);
            yield return new WaitForSeconds(0.5f);
            Capture(camera, Path.Combine(folder, "fp_down_exo.png"));

            var external = new GameObject("CaptureCamera").AddComponent<Camera>();
            external.fieldOfView = 50.0f;
            Transform body = player.Rig.Body.transform;
            external.transform.position = body.position + player.transform.right * 2.2f + Vector3.up * 1.2f;
            external.transform.LookAt(body.position + Vector3.up * 0.9f);
            player.ScriptedWish = StalkerMoveFlags.Forward;
            yield return new WaitForSeconds(0.5f);
            external.transform.position = body.position + player.transform.right * 2.2f + Vector3.up * 1.2f;
            external.transform.LookAt(body.position + Vector3.up * 0.9f);
            Capture(external, Path.Combine(folder, "side_run_exo.png"));
            player.ScriptedWish = StalkerMoveFlags.None;
            UnityEngine.Object.Destroy(external.gameObject);
        }

        private static void Capture(Camera source, string path)
        {
            var camera = new GameObject("CaptureProbe").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            camera.fieldOfView = source.fieldOfView;
            camera.nearClipPlane = source.nearClipPlane;
            CaptureCamera(camera, path);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }

        private static void CaptureCamera(Camera camera, string path)
        {
            var target = new RenderTexture(WIDTH, HEIGHT, 24);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.Destroy(texture);
            target.Release();
            UnityEngine.Object.Destroy(target);
            TestContext.WriteLine("captured " + path);
        }
    }
}
