using System;
using UnityEngine;

namespace RaidIntoDarkness.GraphicsOptions
{
    public enum VideoApplyState
    {
        Idle,
        Switching,
        Confirming,
        Restoring
    }

    public interface IVideoSettingsTarget
    {
        VideoDisplay ReadDisplay();
        VideoDisplay NormalizeDisplay(VideoDisplay display);
        VideoDisplay SafeDisplay { get; }
        void SetDisplay(VideoDisplay display);
        void SetGraphics(VideoSettings settings);
        void PreviewBrightness(int brightness);
        bool PrepareDisplayTrial(VideoSettings confirmed);
        bool Save(VideoSettings settings);
    }

    public sealed class VideoSettingsSession
    {
        public const int CONFIRM_SECONDS = 15;

        private const double SCREEN_SETTLE_SECONDS = 2.0;
        private const double FIRST_CHECK_SECONDS = 0.15;

        private readonly IVideoSettingsTarget _target;
        private readonly VideoPresets _presets;
        private readonly VideoSettings _defaults;
        private double _firstCheck;
        private double _deadline;
        private bool _triedSafeMode;
        private int _seconds;

        public VideoSettings Confirmed { get; private set; }
        public VideoSettings Applied { get; private set; }
        public VideoSettings Draft { get; private set; }
        public VideoApplyState State { get; private set; }
        public string Error { get; private set; }
        public bool Dirty => !Draft.Equals(Confirmed);
        public int Seconds => _seconds;

        public event Action Changed;

        public VideoSettingsSession(IVideoSettingsTarget target, VideoPresets presets, VideoSettings defaults, VideoSettings initial)
        {
            _target = target;
            _presets = presets;
            _defaults = defaults;
            initial.quality = _presets.LimitGrass(initial.quality);
            int matchedPreset = _presets.Match(initial.quality);
            if (matchedPreset >= 0)
            {
                initial.preset = matchedPreset;
            }
            Confirmed = initial;
            Applied = initial;
            Draft = initial;
        }

        public void RestoreAtStartup(double now)
        {
            VideoDisplay actual = _target.ReadDisplay();
            VideoSettings initial = Confirmed;
            initial.display = _target.NormalizeDisplay(initial.display);
            Confirmed = initial;
            Applied = initial;
            Draft = initial;
            _target.SetGraphics(initial);
            if (!actual.Matches(initial.display))
            {
                BeginRestore(now, initial.display);
            }
            Changed?.Invoke();
        }

        public void SyncDisplay()
        {
            if (State != VideoApplyState.Idle || Dirty)
            {
                return;
            }
            SetConfirmedDisplay(_target.ReadDisplay());
            Changed?.Invoke();
        }

        public void Edit(VideoSettings value)
        {
            if (State != VideoApplyState.Idle || !value.Valid)
            {
                return;
            }
            value.quality = _presets.LimitGrass(value.quality);
            if (value.Equals(Draft))
            {
                return;
            }
            if (value.brightness != Draft.brightness)
            {
                _target.PreviewBrightness(value.brightness);
            }
            Draft = value;
            Error = null;
            Changed?.Invoke();
        }

        public void SelectPreset(int preset)
        {
            Edit(_presets.Select(Draft, preset));
        }

        public void Reset(VideoCategory category)
        {
            VideoSettings defaults = _defaults;
            defaults.display = _target.NormalizeDisplay(defaults.display);
            Edit(VideoPresets.ResetCategory(Draft, defaults, category));
        }

        public void Apply(double now)
        {
            if (!Dirty || State != VideoApplyState.Idle)
            {
                return;
            }
            VideoSettings candidate = Draft;
            VideoDisplay actual = _target.ReadDisplay();
            bool requestedDisplayChange = !candidate.display.Matches(Confirmed.display);
            candidate.display = requestedDisplayChange ? _target.NormalizeDisplay(candidate.display) : actual.Valid ? actual : Confirmed.display;
            bool switching = requestedDisplayChange && !candidate.display.Matches(actual);
            if (switching && !_target.PrepareDisplayTrial(Confirmed))
            {
                Error = "Не удалось сохранить исходный режим экрана. Изменения пока не применены.";
                Changed?.Invoke();
                return;
            }
            Applied = candidate;
            Draft = candidate;
            _target.SetGraphics(candidate);
            Error = null;
            if (switching)
            {
                State = VideoApplyState.Switching;
                SwitchDisplay(candidate.display, now);
            }
            else
            {
                Commit(now);
            }
            Changed?.Invoke();
        }

        public void Keep(double now)
        {
            if (State != VideoApplyState.Confirming)
            {
                return;
            }
            if (now >= _deadline || !_target.ReadDisplay().Matches(Applied.display))
            {
                Rollback(now, "Режим экрана изменился. Восстановлены предыдущие настройки.");
            }
            else
            {
                Commit(now);
            }
            Changed?.Invoke();
        }

        public void Cancel(double now)
        {
            if (State == VideoApplyState.Restoring)
            {
                return;
            }
            Rollback(now, null);
            Changed?.Invoke();
        }

        public void Tick(double now)
        {
            switch (State)
            {
                case VideoApplyState.Switching:
                    if (now >= _firstCheck && _target.ReadDisplay().Matches(Applied.display))
                    {
                        _deadline = now + CONFIRM_SECONDS;
                        _seconds = CONFIRM_SECONDS;
                        State = VideoApplyState.Confirming;
                        Changed?.Invoke();
                    }
                    else if (now >= _deadline)
                    {
                        Rollback(now, "Экран не принял выбранный режим. Восстановлены предыдущие настройки.");
                        Changed?.Invoke();
                    }
                    break;
                case VideoApplyState.Confirming:
                    if (now >= _deadline)
                    {
                        Rollback(now, null);
                        Changed?.Invoke();
                        break;
                    }
                    int seconds = (int)Math.Ceiling(_deadline - now);
                    if (_seconds != seconds)
                    {
                        _seconds = seconds;
                        Changed?.Invoke();
                    }
                    break;
                case VideoApplyState.Restoring:
                    CheckRestore(now);
                    break;
            }
        }

        private void Commit(double now)
        {
            if (!_target.Save(Applied))
            {
                Rollback(now, "Не удалось сохранить настройки видео. Восстановлены предыдущие значения.");
                return;
            }
            Confirmed = Applied;
            Draft = Confirmed;
            State = VideoApplyState.Idle;
        }

        private void Rollback(double now, string error)
        {
            bool restoreDisplay = State == VideoApplyState.Switching || State == VideoApplyState.Confirming;
            bool restoreGraphics = !Applied.Equals(Confirmed);
            bool restoreBrightness = Draft.brightness != Confirmed.brightness;
            Draft = Confirmed;
            Applied = Confirmed;
            Error = error;
            if (restoreGraphics)
            {
                _target.SetGraphics(Confirmed);
            }
            else if (restoreBrightness)
            {
                _target.PreviewBrightness(Confirmed.brightness);
            }
            if (restoreDisplay)
            {
                BeginRestore(now, Confirmed.display);
            }
        }

        private void BeginRestore(double now, VideoDisplay display)
        {
            _triedSafeMode = false;
            State = VideoApplyState.Restoring;
            SwitchDisplay(display, now);
        }

        private void SwitchDisplay(VideoDisplay display, double now)
        {
            _target.SetDisplay(display);
            _firstCheck = now + FIRST_CHECK_SECONDS;
            _deadline = now + SCREEN_SETTLE_SECONDS;
        }

        private void CheckRestore(double now)
        {
            if (now < _firstCheck)
            {
                return;
            }
            VideoDisplay actual = _target.ReadDisplay();
            if (actual.Matches(Confirmed.display))
            {
                State = VideoApplyState.Idle;
                Changed?.Invoke();
                return;
            }
            if (now < _deadline)
            {
                return;
            }
            if (!_triedSafeMode)
            {
                _triedSafeMode = true;
                SetConfirmedDisplay(_target.SafeDisplay);
                SwitchDisplay(Confirmed.display, now);
                Error = "Предыдущий режим недоступен. Выбран безопасный режим без рамки.";
                Changed?.Invoke();
                return;
            }
            SetConfirmedDisplay(actual);
            State = VideoApplyState.Idle;
            Error = "Режим экрана недоступен. Показаны фактические параметры.";
            Changed?.Invoke();
        }

        private void SetConfirmedDisplay(VideoDisplay display)
        {
            if (!display.Valid)
            {
                return;
            }
            VideoSettings settings = Confirmed;
            settings.display = display;
            Confirmed = settings;
            Applied = settings;
            Draft = settings;
        }
    }
}
