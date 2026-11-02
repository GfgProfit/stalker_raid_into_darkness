using System;
using System.Collections;
using System.IO;
using Inventory.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class OutdoorsSceneInventoryCaptureTests : OutdoorsSceneInputTestFixture
    {
        private const string CAPTURE_VARIABLE = "INVENTORY_CAPTURE_DIR";
        private const int WIDTH = 1920;
        private const int HEIGHT = 1080;

        [UnityTest]
        public IEnumerator CaptureInventoryWindow()
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
            InventoryWindowController controller = UnityEngine.Object.FindAnyObjectByType<InventoryWindowController>();
            Canvas canvas = controller.Window.GetComponentInParent<Canvas>();
            Camera camera = controller.Player.CameraController.Camera;
            var target = new RenderTexture(WIDTH, HEIGHT, 24);
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + 0.02f;
            GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.transform.SetParent(camera.transform, false);
            backdrop.transform.localPosition = new Vector3(0f, 0f, camera.nearClipPlane + 0.04f);
            backdrop.transform.localScale = new Vector3(1f, 1f, 1f);
            var backdropMaterial = new Material(Shader.Find("HDRP/Unlit"));
            backdropMaterial.SetColor("_UnlitColor", new Color(0.035f, 0.03f, 0.025f, 1f));
            backdrop.GetComponent<Renderer>().sharedMaterial = backdropMaterial;
            yield return new WaitForSeconds(2f);

            controller.Open(false);
            yield return WaitForIcons(controller.Window, 6f);
            yield return new WaitForSeconds(1f);
            Capture(camera, target, Path.Combine(folder, "inventory.png"));

            controller.Open(true);
            yield return WaitForIcons(controller.Window, 6f);
            Capture(camera, target, Path.Combine(folder, "inventory_stash.png"));
            controller.Close();
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(backdrop);
        }

        private static IEnumerator WaitForIcons(InventoryWindow window, float timeout)
        {
            yield return null;

            while (timeout > 0f)
            {
                bool loading = false;

                foreach (InventoryGridViewBase grid in window.Grids)
                {
                    foreach (InventoryItemView view in grid.ItemViews.Values)
                    {
                        loading |= view.IsIconLoading;
                    }
                }

                if (loading == false)
                {
                    break;
                }

                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            yield return null;
            yield return null;
        }

        private static void Capture(Camera camera, RenderTexture target, string path)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
        }
    }
}
