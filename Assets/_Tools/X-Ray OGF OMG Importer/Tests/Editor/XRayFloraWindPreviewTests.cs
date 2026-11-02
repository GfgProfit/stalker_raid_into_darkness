using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayFloraWindPreviewTests
    {
        [Test]
        public void ExistingProviderKeepsOwnershipAndDisablingItSelectsOneStandby()
        {
            var firstRoot = new GameObject("DIAGNOSTIC primary wind provider");
            var secondRoot = new GameObject("DIAGNOSTIC standby wind provider");
            try
            {
                XRayFloraWindProvider first = firstRoot.AddComponent<XRayFloraWindProvider>();
                first.Profile = XRayFloraWindProfile.AnomalySsfx;
                first.ResetAnimation();
                XRayFloraWindProvider second = secondRoot.AddComponent<XRayFloraWindProvider>();
                second.Profile = XRayFloraWindProfile.TrueStalkerXRay;
                second.TrueStalkerDiagnosticConstants = true;
                second.ResetAnimation();
                Assert.AreSame(first, XRayFloraWindProvider.Active);
                Assert.AreEqual(1.0f, Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY));
                first.enabled = false;
                Assert.AreSame(second, XRayFloraWindProvider.Active);
                Assert.AreEqual(2.0f, Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY));
                second.enabled = false;
                Assert.IsNull(XRayFloraWindProvider.Active);
                Assert.AreEqual(0.0f, Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY));
            }
            finally
            {
                Object.DestroyImmediate(secondRoot);
                Object.DestroyImmediate(firstRoot);
            }
        }

        [Test]
        public void EditorClockAdvancesOncePausesAndRebasesAfterVisibilityChanges()
        {
            var root = new GameObject("DIAGNOSTIC wind clock acceptance");
            try
            {
                XRayFloraWindProvider provider = root.AddComponent<XRayFloraWindProvider>();
                provider.Profile = XRayFloraWindProfile.AnomalySsfx;
                provider.PreviewEnabled = true;
                provider.SetWeather(30.0f, 900.0f, 0.004f);
                provider.ResetAnimation();
                Assert.IsFalse(XRayFloraWindPreview.Advance(10.0, true));
                Assert.IsTrue(XRayFloraWindPreview.Advance(10.05, true));
                Assert.AreEqual(0.05, provider.State.Time, 0.00001);
                int ticks = provider.State.Ticks;
                Assert.IsFalse(XRayFloraWindPreview.Advance(10.05, true));
                Assert.AreEqual(ticks, provider.State.Ticks);
                provider.Paused = true;
                Assert.IsFalse(XRayFloraWindPreview.Advance(500.0, true));
                Assert.AreEqual(provider.State.Anim, provider.State.AnimPrev);
                Assert.AreEqual(0.05, provider.State.Time, 0.00001);
                provider.Paused = false;
                Assert.IsFalse(XRayFloraWindPreview.Advance(600.0, true));
                Assert.IsTrue(XRayFloraWindPreview.Advance(600.05, true));
                Assert.AreEqual(0.1, provider.State.Time, 0.00001);
                Assert.IsFalse(XRayFloraWindPreview.Advance(900.0, false));
                Assert.IsFalse(XRayFloraWindPreview.Advance(1000.0, true));
                Assert.IsTrue(XRayFloraWindPreview.Advance(1000.05, true));
                Assert.AreEqual(0.15, provider.State.Time, 0.00001);
                Vector4 phase = provider.State.Anim;
                provider.ResetAnimation();
                Assert.AreEqual(0.15, provider.State.Time, 0.00001);
                Assert.AreEqual(phase, provider.State.Anim);
                Assert.AreEqual(phase, provider.State.AnimPrev);
                provider.PreviewEnabled = false;
                Assert.IsFalse(XRayFloraWindPreview.Advance(2000.0, true));
                Assert.AreEqual(0.15, provider.State.Time, 0.00001);
            }
            finally
            {
                Object.DestroyImmediate(root);
                XRayFloraWindPreview.Advance(3000.0, false);
            }
        }
    }
}
