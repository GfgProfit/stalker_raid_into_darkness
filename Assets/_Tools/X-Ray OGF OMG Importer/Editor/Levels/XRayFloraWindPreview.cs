using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    [InitializeOnLoad]
    public static class XRayFloraWindPreview
    {
        private const double UPDATE_INTERVAL = 1.0 / 30.0;

        private static XRayFloraWindProvider _provider;
        private static double _lastTime;

        static XRayFloraWindPreview()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        public static bool Advance(double now, bool visible)
        {
            XRayFloraWindProvider provider = XRayFloraWindProvider.Active;
            bool running = !EditorApplication.isPlayingOrWillChangePlaymode && visible && provider != null && provider.isActiveAndEnabled && !provider.WeatherDriven && provider.PreviewEnabled && !provider.Paused;
            if (!running || provider != _provider)
            {
                if (_provider != null && !_provider.WeatherDriven) _provider.Reinitialize();
                _provider = running ? provider : null;
                _lastTime = now;
                if (_provider != null) _provider.Tick(0.0f);
                return false;
            }

            double delta = now - _lastTime;
            if (delta < UPDATE_INTERVAL) return false;
            _lastTime = now;
            provider.Tick((float)delta);
            return true;
        }

        private static void Update()
        {
            if (!Advance(EditorApplication.timeSinceStartup, !Application.isBatchMode && SceneView.sceneViews.Count > 0)) return;
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (_provider != null && !_provider.WeatherDriven) _provider.Reinitialize();
            _provider = null;
            _lastTime = EditorApplication.timeSinceStartup;
        }

        private static void Dispose()
        {
            EditorApplication.update -= Update;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
        }
    }
}
