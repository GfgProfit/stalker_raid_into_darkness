using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    [InitializeOnLoad]
    public static class XRayWeatherPreview
    {
        private const double UPDATE_INTERVAL = 1.0 / 30.0;
        private const double MAX_PREVIEW_DELTA = 0.25;

        private static XRayWeatherController _controller;
        private static double _lastTime;
        private static bool _visible;

        public static XRayWeatherController Controller => _controller;

        static XRayWeatherPreview()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        public static bool Start(XRayWeatherController controller)
        {
            Stop();
            if (controller == null || !controller.isActiveAndEnabled || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            controller.BeginPreview();
            if (!controller.PreviewActive) return false;
            _controller = controller;
            _lastTime = EditorApplication.timeSinceStartup;
            _visible = false;
            EditorApplication.update += Update;
            Undo.undoRedoPerformed += ApplyUndo;
            return true;
        }

        public static void Stop()
        {
            EditorApplication.update -= Update;
            Undo.undoRedoPerformed -= ApplyUndo;
            if (_controller != null) _controller.EndPreview();
            _controller = null;
            _visible = false;
        }

        public static bool Advance(double now, bool visible)
        {
            if (_controller == null) return false;
            if (!_controller.isActiveAndEnabled || !_controller.PreviewActive || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Stop();
                return false;
            }

            if (!visible || !_visible)
            {
                _lastTime = now;
                _visible = visible;
                return false;
            }

            double delta = now - _lastTime;
            if (delta < 0.0 || delta > MAX_PREVIEW_DELTA)
            {
                _lastTime = now;
                return false;
            }
            if (delta < UPDATE_INTERVAL) return false;
            _lastTime = now;
            _controller.TickPreview((float)delta);
            return true;
        }

        private static void Update()
        {
            if (!Advance(EditorApplication.timeSinceStartup, !Application.isBatchMode && SceneView.sceneViews.Count > 0)) return;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private static void ApplyUndo()
        {
            if (_controller != null) _controller.ApplySettings();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            Stop();
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            XRayWeatherController active = XRayWeatherController.Active;
            Stop();
            if (active != null && active.PreviewActive) active.EndPreview();
        }

        private static void Dispose()
        {
            Stop();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
        }
    }
}
