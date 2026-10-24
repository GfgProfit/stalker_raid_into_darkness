using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityOpenMcpBridge.Screenshot;

namespace UnityOpenMcpBridge.Tests
{
    public class ScreenshotWindowBoundsTests
    {
        [TestCase(true)]
        [TestCase(false)]
        [Platform("Win")]
        public static void ScreenshotUnavailableRenderTargetDoesNotLogGraphicsErrors(bool bindRenderTarget)
        {
            EditorWindow window = ScriptableObject.CreateInstance<EditorWindow>();
            var target = new RenderTexture(618, 377, 0);
            RenderTexture previous = RenderTexture.active;
            string error = null;
            Application.LogCallback captureError = (message, stackTrace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    error = message;
                }
            };
            Application.logMessageReceived += captureError;
            try
            {
                window.titleContent = new GUIContent("MCP Bounds Regression");
                window.position = new Rect(1436.0f, 251.0f, 483.0f, 750.0f);
                Assert.IsTrue(target.Create());
                RenderTexture.active = bindRenderTarget ? target : null;
                string result = new Tool_ScreenshotWindow().ScreenshotWindow(window_title: window.titleContent.text, width: 1600, height: 1600);
                StringAssert.Contains("\"code\":\"execution_error\"", result);
                StringAssert.Contains(bindRenderTarget ? "outside the current 618x377 render target" : "no render target is bound", result);
                Assert.AreSame(bindRenderTarget ? target : null, RenderTexture.active);
                Assert.IsNull(error);
            }
            finally
            {
                Application.logMessageReceived -= captureError;
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(window);
            }
        }
    }
}
