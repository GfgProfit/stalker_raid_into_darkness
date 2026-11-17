using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidIntoDarkness.GraphicsOptions
{
    public sealed class VideoDisplayOptions
    {
        private readonly List<Resolution> _modes = new();
        private readonly List<VideoDisplay> _sizes = new();
        private readonly List<VideoDisplay> _rates = new();

        private VideoDisplay _desktop;
        private string _desktopName;

        public bool Supported => !Application.isEditor && (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.OSXPlayer);
        public bool ExclusiveSupported => Application.platform == RuntimePlatform.WindowsPlayer && _modes.Count > 0;
        public RefreshRate CurrentRefresh => Screen.currentResolution.refreshRateRatio;
        public VideoDisplay SafeDisplay => _desktop;

        public void Refresh()
        {
            DisplayInfo display = Screen.mainWindowDisplayInfo;
            Resolution current = Screen.currentResolution;
            int width = display.width > 0 ? display.width : Mathf.Max(1, current.width, Screen.width);
            int height = display.height > 0 ? display.height : Mathf.Max(1, current.height, Screen.height);
            if (!_desktop.Valid || Screen.fullScreenMode != FullScreenMode.ExclusiveFullScreen || display.name != _desktopName)
            {
                _desktop = new VideoDisplay(FullScreenMode.FullScreenWindow, width, height);
                _desktopName = display.name;
            }
            _modes.Clear();
            foreach (Resolution mode in Screen.resolutions)
            {
                if (mode.width > 0 && mode.height > 0 && mode.refreshRateRatio.numerator > 0 && mode.refreshRateRatio.denominator > 0)
                {
                    _modes.Add(mode);
                }
            }
            _modes.Sort(CompareModes);
        }

        public IReadOnlyList<VideoDisplay> Sizes(FullScreenMode mode)
        {
            _sizes.Clear();
            if (mode == FullScreenMode.FullScreenWindow)
            {
                _sizes.Add(_desktop);
                return _sizes;
            }
            foreach (Resolution entry in _modes)
            {
                if (mode == FullScreenMode.Windowed && (entry.width > _desktop.width || entry.height > _desktop.height))
                {
                    continue;
                }
                if (_sizes.Count == 0 || _sizes[^1].width != entry.width || _sizes[^1].height != entry.height)
                {
                    _sizes.Add(new VideoDisplay(mode, entry.width, entry.height, entry.refreshRateRatio));
                }
            }
            if (mode == FullScreenMode.Windowed)
            {
                VideoDisplay current = Read();
                if (current.mode == FullScreenMode.Windowed && !_sizes.Contains(current))
                {
                    _sizes.Add(current);
                    _sizes.Sort(CompareSizes);
                }
                if (_sizes.Count == 0)
                {
                    _sizes.Add(new VideoDisplay(mode, _desktop.width, _desktop.height));
                }
            }
            return _sizes;
        }

        public IReadOnlyList<VideoDisplay> Rates(VideoDisplay display)
        {
            _rates.Clear();
            if (display.mode != FullScreenMode.ExclusiveFullScreen)
            {
                return _rates;
            }
            foreach (Resolution entry in _modes)
            {
                if (entry.width == display.width && entry.height == display.height)
                {
                    var candidate = new VideoDisplay(display.mode, entry.width, entry.height, entry.refreshRateRatio);
                    if (_rates.Count == 0 || !_rates[^1].Matches(candidate))
                    {
                        _rates.Add(candidate);
                    }
                }
            }
            return _rates;
        }

        public VideoDisplay Normalize(VideoDisplay requested)
        {
            if (!Supported)
            {
                return Read();
            }
            if (requested.mode == FullScreenMode.FullScreenWindow || requested.mode == FullScreenMode.ExclusiveFullScreen && !ExclusiveSupported)
            {
                return _desktop;
            }
            IReadOnlyList<VideoDisplay> sizes = Sizes(requested.mode);
            VideoDisplay closest = sizes[0];
            long best = long.MaxValue;
            foreach (VideoDisplay candidate in sizes)
            {
                long dx = candidate.width - (long)requested.width;
                long dy = candidate.height - (long)requested.height;
                long distance = dx * dx + dy * dy;
                if (distance < best)
                {
                    closest = candidate;
                    best = distance;
                }
            }
            if (requested.mode != FullScreenMode.ExclusiveFullScreen)
            {
                return closest;
            }
            IReadOnlyList<VideoDisplay> rates = Rates(closest);
            VideoDisplay rate = rates[^1];
            double desired = requested.refreshDenominator > 0 ? requested.Refresh.value : CurrentRefresh.value;
            double difference = double.MaxValue;
            foreach (VideoDisplay candidate in rates)
            {
                double distance = Math.Abs(candidate.Refresh.value - desired);
                if (distance < difference)
                {
                    rate = candidate;
                    difference = distance;
                }
            }
            return rate;
        }

        public VideoDisplay Read()
        {
            FullScreenMode mode = Screen.fullScreenMode;
            if (mode != FullScreenMode.ExclusiveFullScreen && mode != FullScreenMode.Windowed)
            {
                mode = FullScreenMode.FullScreenWindow;
            }
            return new VideoDisplay(mode, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height), CurrentRefresh);
        }

        public void Apply(VideoDisplay settings)
        {
            if (Supported)
            {
                Screen.SetResolution(settings.width, settings.height, settings.mode, settings.Refresh);
            }
        }

        private static int CompareModes(Resolution first, Resolution second)
        {
            int width = first.width.CompareTo(second.width);
            if (width != 0)
            {
                return width;
            }
            int height = first.height.CompareTo(second.height);
            return height != 0 ? height : first.refreshRateRatio.value.CompareTo(second.refreshRateRatio.value);
        }

        private static int CompareSizes(VideoDisplay first, VideoDisplay second)
        {
            int width = first.width.CompareTo(second.width);
            return width != 0 ? width : first.height.CompareTo(second.height);
        }
    }
}
