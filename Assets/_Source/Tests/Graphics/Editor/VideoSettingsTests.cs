using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace RaidIntoDarkness.GraphicsOptions.Tests
{
    public sealed class VideoSettingsTests
    {
        private readonly VideoPresets _presets = new VideoPresets(0.45f, 75, false);
        private readonly VideoDisplay _desktop = new VideoDisplay(FullScreenMode.FullScreenWindow, 1920, 1080);
        private readonly VideoDisplay _window = new VideoDisplay(FullScreenMode.Windowed, 1280, 720);

        [Test]
        public void EditorUsesProductNameAndPlayerUsesBuildDirectory()
        {
            const string PRODUCT = "S.T.A.L.K.E.R. Raid into Darkness";
            string root = Path.GetPathRoot(Path.GetFullPath(Application.dataPath));
            string company = Path.Combine(root, "LocalLow", "DefaultCompany");
            string unityPath = Path.Combine(company, "S_T_A_L_K_E_R_ Raid into Darkness");
            string playerRoot = Path.Combine(root, "Game", "ExtractedBuild");
            string data = Path.Combine(playerRoot, "Raid_Data");
            Assert.That(VideoSettingsStore.ResolvePath(true, data, unityPath, PRODUCT), Is.EqualTo(Path.Combine(company, PRODUCT, "VideoSettings.json")));
            Assert.That(VideoSettingsStore.ResolvePath(false, data, unityPath, PRODUCT), Is.EqualTo(Path.Combine(playerRoot, "appdata", "settings", "VideoSettings.json")));
        }

        [Test]
        public void PresetsAreDistinctAndPreserveIndependentValues()
        {
            VideoSettings settings = _presets.Defaults(_window);
            settings.vSync = true;
            settings.fpsLimit = 144;
            settings.brightness = 73;
            settings.grassHeight = 1.35f;
            int[] grassDensities = { 30, 50, 75, 100, 115 };
            int[] grassDistances = { 50, 80, 90, 100, 110 };
            for (int index = 0; index < VideoPresets.COUNT; index++)
            {
                settings = _presets.Select(settings, index);
                Assert.That(settings.Valid, Is.True);
                Assert.That(settings.quality.textureQuality, Is.EqualTo(index));
                Assert.That(settings.quality.TextureMipmapLimit, Is.EqualTo(4 - index));
                Assert.That(_presets.GrassPercent(settings.quality.grassSpacing), Is.EqualTo(grassDensities[index]));
                Assert.That(settings.quality.grassDistance, Is.EqualTo(grassDistances[index]));
                Assert.That(_presets.Match(settings.quality), Is.EqualTo(index));
                Assert.That(_presets.IsCustom(settings), Is.False);
                Assert.That(settings.display.Matches(_window), Is.True);
                Assert.That(settings.vSync, Is.True);
                Assert.That(settings.fpsLimit, Is.EqualTo(144));
                Assert.That(settings.brightness, Is.EqualTo(73));
                Assert.That(settings.grassHeight, Is.EqualTo(1.35f));
            }
        }

        [Test]
        public void ContactShadowsFollowPresetsAndLightingReset()
        {
            VideoSettings defaults = _presets.Defaults(_desktop);
            int[] tiers = { 0, 0, 1, 1, 2 };
            for (int i = 0; i < tiers.Length; i++)
            {
                VideoSettings settings = _presets.Select(defaults, i);
                Assert.That(settings.quality.contactShadows, Is.EqualTo(i > 0));
                Assert.That(settings.quality.contactShadowQuality, Is.EqualTo(tiers[i]));
                settings.brightness = 80;
                VideoSettings reset = VideoPresets.ResetCategory(settings, defaults, VideoCategory.Lighting);
                Assert.That(reset.quality.contactShadows, Is.True);
                Assert.That(reset.quality.contactShadowQuality, Is.EqualTo(1));
                Assert.That(reset.brightness, Is.EqualTo(80));
            }
        }

        [Test]
        public void JsonRoundTripPreservesEveryPresetAndFractionalRefresh()
        {
            var display = new VideoDisplay(FullScreenMode.ExclusiveFullScreen, 1920, 1080, new RefreshRate { numerator = 60000, denominator = 1001 });
            for (int i = 0; i < VideoPresets.COUNT; i++)
            {
                VideoSettings value = _presets.Select(_presets.Defaults(display), i);
                value.grassHeight = 1.35f;
                VideoSettings restored = JsonUtility.FromJson<VideoSettings>(JsonUtility.ToJson(value));
                Assert.That(restored.Valid, Is.True);
                Assert.That(restored, Is.EqualTo(value));
                Assert.That(_presets.Match(restored.quality), Is.EqualTo(i));
            }
            Assert.That(JsonUtility.FromJson<VideoSettings>("{\"version\":" + VideoPresets.SAVE_VERSION + "}").Valid, Is.False);
        }

        [Test]
        public void OlderGraphicsFileUsesExistingGrassHeight()
        {
            VideoSettings legacy = _presets.Defaults(_desktop);
            legacy.brightness = 86;
            legacy.grassHeight = 1.35f;
            string json = $"{{\"version\":{legacy.version},\"preset\":{legacy.preset},\"display\":{JsonUtility.ToJson(legacy.display)},\"fpsLimit\":{legacy.fpsLimit},\"brightness\":{legacy.brightness},\"quality\":{JsonUtility.ToJson(legacy.quality)}}}";
            MethodInfo deserialize = typeof(VideoSettingsStore).GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Static);
            VideoSettings restored = (VideoSettings)deserialize.Invoke(null, new object[] { json, legacy.grassHeight, false });
            Assert.That(restored.Valid, Is.True);
            Assert.That(restored, Is.EqualTo(legacy));
        }

        [Test]
        public void VersionFourRestoresIndependentAdvancedSettings()
        {
            VideoSettings legacy = _presets.Select(_presets.Defaults(_desktop), 0);
            legacy.version = 4;
            legacy.quality.shadowQuality = 1;
            legacy.quality.shadowCascades = 0;
            MethodInfo deserialize = typeof(VideoSettingsStore).GetMethod("Deserialize", BindingFlags.NonPublic | BindingFlags.Static);
            object[] arguments = { JsonUtility.ToJson(legacy), legacy.grassHeight, false };
            VideoSettings settings = (VideoSettings)deserialize.Invoke(null, arguments);

            Assert.That(arguments[2], Is.True);
            Assert.That(settings.Valid, Is.True);
            Assert.That(settings.quality.shadowQuality, Is.EqualTo(1));
            Assert.That(settings.quality.shadowCascades, Is.EqualTo(3));
            Assert.That(settings.quality.reflections, Is.EqualTo(3));
            Assert.That(VideoPresets.LocalShadowBudget(settings.quality.localShadowBudget), Is.EqualTo(-1));
            Assert.That(VideoPresets.StreamingBudget(settings.quality.streamingBudget), Is.EqualTo(512));
            Assert.That(settings.quality.objectDrawDistance, Is.EqualTo(VideoPresets.MAX_OBJECT_DISTANCE));
        }

        [Test]
        public void EditingResolutionDoesNotChangeShadowCascadesOrBudget()
        {
            VideoSettings settings = _presets.Defaults(_desktop);
            settings.quality.shadowQuality = 3;
            settings.quality.shadowCascades = 1;
            settings.quality.localShadowBudget = 2;
            settings.quality.upscalingMethod = 1;
            settings.quality.upscalingQuality = 2;
            VideoSettings restored = JsonUtility.FromJson<VideoSettings>(JsonUtility.ToJson(settings));

            Assert.That(restored.Valid, Is.True);
            Assert.That(restored.quality.shadowQuality, Is.EqualTo(3));
            Assert.That(restored.quality.shadowCascades, Is.EqualTo(1));
            Assert.That(VideoPresets.LocalShadowBudget(restored.quality.localShadowBudget), Is.EqualTo(8));
            VideoSettings reset = VideoPresets.ResetCategory(restored, _presets.Defaults(_desktop), VideoCategory.Lighting);
            Assert.That(reset.quality.upscalingMethod, Is.EqualTo(1));
            Assert.That(reset.quality.upscalingQuality, Is.EqualTo(2));
        }

        [Test]
        public void IdentifiesMatchingPresetAfterLoadingAndMarksManualChanges()
        {
            var target = new Target(_desktop, _desktop);
            VideoSettings defaults = _presets.Defaults(_desktop);
            VideoSettings saved = _presets.Select(defaults, 1);
            saved.preset = 2;
            var session = new VideoSettingsSession(target, _presets, defaults, saved);
            Assert.That(session.Confirmed.preset, Is.EqualTo(1));
            saved = session.Draft;
            saved.quality.ambientOcclusion = 3;
            session.Edit(saved);
            Assert.That(_presets.IsCustom(session.Draft), Is.True);
            session.SelectPreset(1);
            Assert.That(_presets.IsCustom(session.Draft), Is.False);
        }

        [Test]
        public void CategoryAndAllResetThroughDraftAndCancelRestoresBrightness()
        {
            var target = new Target(_desktop, _desktop);
            VideoSettings defaults = _presets.Defaults(_desktop);
            VideoSettings initial = _presets.Select(defaults, 1);
            initial.brightness = 89;
            initial.fpsLimit = 120;
            initial.grassHeight = 1.35f;
            var session = new VideoSettingsSession(target, _presets, defaults, initial);
            session.Reset(VideoCategory.Image);
            Assert.That(session.Draft.brightness, Is.EqualTo(defaults.brightness));
            Assert.That(session.Draft.quality.renderScale, Is.EqualTo(defaults.quality.renderScale));
            Assert.That(session.Draft.quality.anisotropy, Is.EqualTo(initial.quality.anisotropy));
            Assert.That(session.Draft.fpsLimit, Is.EqualTo(120));
            Assert.That(session.Draft.grassHeight, Is.EqualTo(initial.grassHeight));
            Assert.That(target.Brightness, Is.EqualTo(100));
            session.Cancel(0.0);
            Assert.That(target.Brightness, Is.EqualTo(89));
            Assert.That(session.Dirty, Is.False);
            session.Reset(VideoCategory.Vegetation);
            Assert.That(session.Draft.grassHeight, Is.EqualTo(defaults.grassHeight));
            session.Reset(VideoCategory.All);
            Assert.That(session.Draft, Is.EqualTo(defaults));
            Assert.That(session.Confirmed, Is.EqualTo(initial));
            session.Apply(0.0);
            Assert.That(target.Saved, Is.EqualTo(defaults));
        }

        [Test]
        public void TrialWritesOnlyConfirmedBaselineUntilExplicitKeep()
        {
            var target = new Target(_desktop, _desktop);
            VideoSettings before = _presets.Defaults(_desktop);
            var session = new VideoSettingsSession(target, _presets, before, before);
            VideoSettings draft = WithWindow(before);
            session.Edit(draft);
            session.Apply(1.0);
            Assert.That(session.State, Is.EqualTo(VideoApplyState.Switching));
            Assert.That(session.Applied, Is.EqualTo(draft));
            Assert.That(session.Confirmed, Is.EqualTo(before));
            Assert.That(target.Baseline, Is.EqualTo(before));
            Assert.That(target.SaveCount, Is.Zero);
            session.Tick(1.2);
            Assert.That(session.State, Is.EqualTo(VideoApplyState.Confirming));
            Assert.That(session.Seconds, Is.EqualTo(15));
            session.Keep(4.0);
            Assert.That(session.State, Is.EqualTo(VideoApplyState.Idle));
            Assert.That(session.Confirmed, Is.EqualTo(draft));
            Assert.That(target.Saved, Is.EqualTo(draft));
            Assert.That(target.SaveCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TimeoutOrExplicitReturnRestoresEntireConfirmedState(bool timeout)
        {
            var target = new Target(_desktop, _desktop);
            VideoSettings before = _presets.Defaults(_desktop);
            var session = new VideoSettingsSession(target, _presets, before, before);
            session.Edit(WithWindow(before));
            session.Apply(1.0);
            session.Tick(1.2);
            if (timeout)
            {
                session.Tick(17.0);
            }
            else
            {
                session.Cancel(3.0);
            }
            session.Tick(18.0);
            Assert.That(session.State, Is.EqualTo(VideoApplyState.Idle));
            Assert.That(session.Applied, Is.EqualTo(before));
            Assert.That(session.Draft, Is.EqualTo(before));
            Assert.That(target.Active, Is.EqualTo(before));
            Assert.That(target.Brightness, Is.EqualTo(before.brightness));
            Assert.That(target.SaveCount, Is.Zero);
        }

        [Test]
        public void UnavailableModeUsesSafeModeAndResynchronizesAfterSwitchFailure()
        {
            var target = new Target(_desktop, _desktop);
            VideoSettings before = _presets.Defaults(_window);
            var session = new VideoSettingsSession(target, _presets, before, before);
            target.AllowWindow = false;
            session.RestoreAtStartup(0.0);
            Assert.That(session.Confirmed.display.Matches(_desktop), Is.True);
            Assert.That(session.State, Is.EqualTo(VideoApplyState.Idle));
            target.AllowWindow = true;
            target.RejectWindow = true;
            session.Edit(WithWindow(before));
            session.Apply(3.0);
            session.Tick(6.0);
            session.Tick(7.0);
            Assert.That(session.State, Is.EqualTo(VideoApplyState.Idle));
            Assert.That(session.Draft.display.Matches(_desktop), Is.True);
            Assert.That(session.Error, Is.Not.Empty);
            Assert.That(target.SaveCount, Is.Zero);
        }

        [Test]
        public void FailedConfirmationSaveRestoresPreviousModeAndGraphics()
        {
            var target = new Target(_desktop, _desktop) { AllowSave = false };
            VideoSettings before = _presets.Defaults(_desktop);
            var session = new VideoSettingsSession(target, _presets, before, before);
            session.Edit(WithWindow(before));
            session.Apply(10.0);
            session.Tick(11.0);
            session.Keep(12.0);
            session.Tick(13.0);
            Assert.That(session.Confirmed, Is.EqualTo(before));
            Assert.That(target.Active, Is.EqualTo(before));
            Assert.That(target.ReadDisplay().Matches(_desktop), Is.True);
            Assert.That(session.Error, Is.Not.Empty);
        }

        [Test]
        public void ResizingWindowExternallyDoesNotTriggerConfirmationForGraphicsChange()
        {
            var target = new Target(_desktop, _desktop);
            VideoSettings before = _presets.Defaults(_desktop);
            var session = new VideoSettingsSession(target, _presets, before, before);
            VideoSettings edited = before;
            edited.fpsLimit = 60;
            session.Edit(edited);
            target.SetDisplay(_window);
            session.Apply(1.0);
            Assert.That(session.State, Is.EqualTo(VideoApplyState.Idle));
            Assert.That(session.Confirmed.display.Matches(_window), Is.True);
            Assert.That(session.Confirmed.fpsLimit, Is.EqualTo(60));
        }

        [Test]
        public void MoreGrassMeansSmallerSamplingSpacing()
        {
            Assert.That(_presets.GrassSpacing(30), Is.GreaterThan(_presets.GrassSpacing(100)));
            Assert.That(_presets.GrassSpacing(100), Is.EqualTo(0.45f));
            Assert.That(_presets.GrassSpacing(115), Is.LessThan(_presets.GrassSpacing(100)));
            Assert.That(_presets.GrassSpacing(150), Is.EqualTo(_presets.GrassSpacing(115)));
            Assert.That(_presets.GrassPercent(_presets.GrassSpacing(115)), Is.EqualTo(115));
        }

        [Test]
        public void GrassAboveTheLimitsIsClampedOnLoadAndApply()
        {
            var target = new Target(_desktop, _desktop);
            VideoSettings defaults = _presets.Defaults(_desktop);
            VideoSettings saved = defaults;
            saved.quality.grassSpacing = 0.45f / Mathf.Sqrt(1.5f);
            saved.quality.grassDistance = 250;
            saved.brightness = 73;
            saved.fpsLimit = 144;
            VideoSettings restored = JsonUtility.FromJson<VideoSettings>(JsonUtility.ToJson(saved));
            Assert.That(restored.Valid, Is.True);

            var session = new VideoSettingsSession(target, _presets, defaults, restored);
            session.RestoreAtStartup(0.0);
            VideoSettings expected = saved;
            expected.quality.grassSpacing = _presets.GrassSpacing(115);
            expected.quality.grassDistance = 110;
            Assert.That(session.Confirmed, Is.EqualTo(expected));
            Assert.That(target.Active, Is.EqualTo(expected));
            Assert.That(session.Dirty, Is.False);

            saved.brightness = 74;
            expected.brightness = 74;
            session.Edit(saved);
            session.Apply(1.0);
            Assert.That(target.Active, Is.EqualTo(expected));
            Assert.That(target.Saved, Is.EqualTo(expected));
        }

        private VideoSettings WithWindow(VideoSettings value)
        {
            value.display = _window;
            value.brightness = 71;
            value.quality.bloom = !value.quality.bloom;
            return value;
        }

        private sealed class Target : IVideoSettingsTarget
        {
            private VideoDisplay _display;

            public VideoDisplay SafeDisplay { get; }
            public VideoSettings Baseline { get; private set; }
            public VideoSettings Saved { get; private set; }
            public VideoSettings Active { get; private set; }
            public int SaveCount { get; private set; }
            public int Brightness { get; private set; }
            public bool AllowSave { get; set; } = true;
            public bool AllowWindow { get; set; } = true;
            public bool RejectWindow { get; set; }

            public Target(VideoDisplay display, VideoDisplay safe)
            {
                _display = display;
                SafeDisplay = safe;
            }

            public VideoDisplay ReadDisplay()
            {
                return _display;
            }

            public VideoDisplay NormalizeDisplay(VideoDisplay display)
            {
                return !AllowWindow && display.mode == FullScreenMode.Windowed ? SafeDisplay : display;
            }

            public void SetDisplay(VideoDisplay display)
            {
                if (!RejectWindow || display.mode != FullScreenMode.Windowed)
                {
                    _display = display;
                }
            }

            public void SetGraphics(VideoSettings settings)
            {
                Active = settings;
                Brightness = settings.brightness;
            }

            public void PreviewBrightness(int brightness)
            {
                Brightness = brightness;
            }

            public bool PrepareDisplayTrial(VideoSettings confirmed)
            {
                Baseline = confirmed;
                return true;
            }

            public bool Save(VideoSettings settings)
            {
                SaveCount++;
                if (!AllowSave)
                {
                    return false;
                }
                Saved = settings;
                return true;
            }
        }
    }
}
