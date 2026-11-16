using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rvm.Benchmark
{
    public sealed class BenchmarkPacingState : IDisposable
    {
        private readonly int _quality = QualitySettings.GetQualityLevel();
        private readonly int _vSync = QualitySettings.vSyncCount;
        private readonly int _target = Application.targetFrameRate;
        private readonly bool _background = Application.runInBackground;
        private readonly int _interval = OnDemandRendering.renderFrameInterval;
        private readonly int _width = Screen.width;
        private readonly int _height = Screen.height;
        private readonly FullScreenMode _fullscreen = Screen.fullScreenMode;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            QualitySettings.SetQualityLevel(_quality, true);
            QualitySettings.vSyncCount = _vSync;
            Application.targetFrameRate = _target;
            Application.runInBackground = _background;
            OnDemandRendering.renderFrameInterval = _interval;
            Screen.SetResolution(_width, _height, _fullscreen);
        }
    }
}
