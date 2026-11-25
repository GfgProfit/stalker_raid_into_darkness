using R3;
using RaidIntoDarkness.UI;

namespace RaidIntoDarkness.GraphicsOptions
{
    public sealed class VideoSettingsPage : SettingsPageView
    {
        private VideoSettingsSession _session;

        internal void Bind(VideoSettingsSession session)
        {
            _session = session;
        }

        internal override ISettingsPageViewModel Open() => new VideoSettingsPageViewModel(_session);
    }

    internal sealed class VideoSettingsPageViewModel : ISettingsPageViewModel
    {
        private readonly VideoSettingsSession _session;
        private readonly ReactiveProperty<bool> _dirty;

        public ReadOnlyReactiveProperty<bool> IsDirty => _dirty;

        public VideoSettingsPageViewModel(VideoSettingsSession session)
        {
            _session = session;
            _dirty = new ReactiveProperty<bool>(session.Dirty);
            _session.Changed += UpdateDirty;
        }

        public bool Apply(double now)
        {
            _session.Apply(now);
            return true;
        }

        public void Cancel(double now) => _session.Cancel(now);
        public void Reset() => _session.Reset(VideoCategory.All);

        public void Dispose()
        {
            _session.Changed -= UpdateDirty;
            _dirty.Dispose();
        }

        private void UpdateDirty() => _dirty.Value = _session.Dirty;
    }
}
