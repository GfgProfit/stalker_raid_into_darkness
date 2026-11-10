using System;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerConditionHudTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _root;

            public StalkerMovementProfile Profile { get; }
            public StalkerActorCondition Condition { get; }
            public StalkerConditionHud Hud { get; }
            public Image Health { get; }
            public Image PsyHealth { get; }
            public Image Power { get; }
            public RectTransform HealthLimit { get; }
            public RectTransform PsyHealthLimit { get; }
            public RectTransform PowerLimit { get; }

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                _root = new GameObject(nameof(StalkerConditionHudTests), typeof(RectTransform));
                _root.hideFlags = HideFlags.HideAndDontSave;
                _root.SetActive(false);
                SceneManager.MoveGameObjectToScene(_root, _scene);
                Profile = ScriptableObject.CreateInstance<StalkerMovementProfile>();
                Profile.hideFlags = HideFlags.HideAndDontSave;
                Condition = _root.AddComponent<StalkerActorCondition>();
                Condition.Initialize(Profile);
                Hud = _root.AddComponent<StalkerConditionHud>();
                Health = CreateFill("Health");
                PsyHealth = CreateFill("PsyHealth");
                Power = CreateFill("Power");
                HealthLimit = CreateLimit("HealthLimit");
                PsyHealthLimit = CreateLimit("PsyHealthLimit");
                PowerLimit = CreateLimit("PowerLimit");
                Bind();
            }

            public void Bind()
            {
                Hud.Bind(Condition, Health, PsyHealth, Power, HealthLimit, PsyHealthLimit, PowerLimit);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_root);
                UnityEngine.Object.DestroyImmediate(Profile);
                EditorSceneManager.ClosePreviewScene(_scene);
            }

            private Image CreateFill(string name)
            {
                var child = new GameObject(name, typeof(RectTransform), typeof(Image));
                child.transform.SetParent(_root.transform, false);
                var image = child.GetComponent<Image>();
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)Image.OriginHorizontal.Left;
                image.rectTransform.sizeDelta = new Vector2(300.0f, 10.0f);
                return image;
            }

            private RectTransform CreateLimit(string name)
            {
                var child = new GameObject(name, typeof(RectTransform), typeof(Image));
                child.transform.SetParent(_root.transform, false);
                var rect = child.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(5.0f, 10.0f);
                rect.localPosition = new Vector3(0.0f, 7.0f, 2.0f);
                return rect;
            }
        }

        [Test]
        public void DefaultHundredPointStatsFillEveryBarAndPlaceEveryLimitAtTheEnd()
        {
            using (var rig = new Rig())
            {
                Assert.That(rig.Condition.Health, Is.EqualTo(100.0f));
                Assert.That(rig.Condition.MaxHealth, Is.EqualTo(100.0f));
                Assert.That(rig.Condition.HealthCapacity, Is.EqualTo(100.0f));
                Assert.That(rig.Condition.PsyHealth, Is.EqualTo(100.0f));
                Assert.That(rig.Condition.MaxPsyHealth, Is.EqualTo(100.0f));
                Assert.That(rig.Condition.PsyHealthCapacity, Is.EqualTo(100.0f));
                Assert.That(rig.Condition.Power, Is.EqualTo(100.0f));
                Assert.That(rig.Condition.MaxPower, Is.EqualTo(100.0f));
                Assert.That(rig.Condition.PowerCapacity, Is.EqualTo(100.0f));
                Assert.That(rig.Health.fillAmount, Is.EqualTo(1.0f));
                Assert.That(rig.PsyHealth.fillAmount, Is.EqualTo(1.0f));
                Assert.That(rig.Power.fillAmount, Is.EqualTo(1.0f));
                Assert.That(rig.HealthLimit.localPosition.x, Is.EqualTo(150.0f).Within(0.0001f));
                Assert.That(rig.PsyHealthLimit.localPosition.x, Is.EqualTo(150.0f).Within(0.0001f));
                Assert.That(rig.PowerLimit.localPosition.x, Is.EqualTo(150.0f).Within(0.0001f));
            }
        }

        [Test]
        public void ValuesAndMaximumMarkersUseTheAbsoluteConditionScale()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetMaxHealth(60.0f);
                rig.Condition.SetHealth(40.0f);
                rig.Condition.SetMaxPsyHealth(70.0f);
                rig.Condition.SetPsyHealth(20.0f);
                rig.Condition.SetMaxPower(70.0f);
                rig.Condition.SetPower(30.0f);

                rig.Hud.Refresh();

                Assert.That(rig.Health.fillAmount, Is.EqualTo(0.4f).Within(0.000001f));
                Assert.That(rig.PsyHealth.fillAmount, Is.EqualTo(0.2f).Within(0.000001f));
                Assert.That(rig.Power.fillAmount, Is.EqualTo(0.3f).Within(0.000001f));
                Assert.That(rig.HealthLimit.localPosition.x, Is.EqualTo(30.0f).Within(0.0001f));
                Assert.That(rig.PsyHealthLimit.localPosition.x, Is.EqualTo(60.0f).Within(0.0001f));
                Assert.That(rig.PowerLimit.localPosition.x, Is.EqualTo(60.0f).Within(0.0001f));
                Assert.That(rig.Condition.Health, Is.EqualTo(40.0f).Within(0.000001f));
                Assert.That(rig.Condition.PsyHealth, Is.EqualTo(20.0f).Within(0.000001f));
                Assert.That(rig.Condition.Power, Is.EqualTo(30.0f).Within(0.000001f));
                Assert.That(rig.Condition.MaxHealth, Is.EqualTo(60.0f).Within(0.000001f));
                Assert.That(rig.Condition.MaxPsyHealth, Is.EqualTo(70.0f).Within(0.000001f));
                Assert.That(rig.Condition.MaxPower, Is.EqualTo(70.0f).Within(0.000001f));
            }
        }

        [Test]
        public void RefreshShowsStateChangesAfterBinding()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetMaxHealth(90.0f);
                rig.Condition.SetHealth(80.0f);
                rig.Condition.SetPsyHealth(45.0f);
                rig.Condition.SetPower(55.0f);

                rig.Hud.Refresh();

                Assert.That(rig.Health.fillAmount, Is.EqualTo(0.8f).Within(0.000001f));
                Assert.That(rig.PsyHealth.fillAmount, Is.EqualTo(0.45f).Within(0.000001f));
                Assert.That(rig.Power.fillAmount, Is.EqualTo(0.55f).Within(0.000001f));
                Assert.That(rig.HealthLimit.localPosition.x, Is.EqualTo(120.0f).Within(0.0001f));
            }
        }

        [Test]
        public void MaximumMarkerFollowsResizedOffsetFillAndPreservesAuthoredAppearance()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetMaxHealth(60.0f);
                rig.Health.rectTransform.sizeDelta = new Vector2(400.0f, 10.0f);
                rig.Health.rectTransform.pivot = new Vector2(0.2f, 0.5f);
                rig.Health.rectTransform.localPosition = new Vector3(17.0f, 23.0f, 0.0f);
                rig.Health.rectTransform.localScale = new Vector3(1.2f, 1.0f, 1.0f);
                rig.Health.color = new Color(0.8f, 0.2f, 0.3f, 0.7f);
                Image marker = rig.HealthLimit.GetComponent<Image>();
                marker.color = Color.magenta;

                rig.Hud.Refresh();

                Assert.That(rig.HealthLimit.localPosition.x, Is.EqualTo(209.0f).Within(0.0001f));
                Assert.That(rig.HealthLimit.localPosition.y, Is.EqualTo(7.0f));
                Assert.That(rig.HealthLimit.localPosition.z, Is.EqualTo(2.0f));
                Assert.That(rig.HealthLimit.sizeDelta, Is.EqualTo(new Vector2(5.0f, 10.0f)));
                Assert.That(rig.Health.color, Is.EqualTo(new Color(0.8f, 0.2f, 0.3f, 0.7f)));
                Assert.That(marker.color, Is.EqualTo(Color.magenta));
                Assert.That(rig.Health.type, Is.EqualTo(Image.Type.Filled));
                Assert.That(rig.Health.fillOrigin, Is.EqualTo((int)Image.OriginHorizontal.Left));
            }
        }

        [Test]
        public void RebindingUsesOnlyTheNewCondition()
        {
            using (var first = new Rig())
            using (var second = new Rig())
            {
                second.Condition.SetHealth(65.0f);
                second.Condition.SetPsyHealth(25.0f);
                second.Condition.SetMaxPower(80.0f);
                second.Condition.SetPower(45.0f);
                first.Hud.Bind(second.Condition, first.Health, first.PsyHealth, first.Power, first.HealthLimit, first.PsyHealthLimit, first.PowerLimit);
                first.Condition.SetHealth(10.0f);
                first.Condition.SetPsyHealth(15.0f);
                first.Condition.SetPower(20.0f);

                first.Hud.Refresh();

                Assert.That(first.Health.fillAmount, Is.EqualTo(0.65f).Within(0.000001f));
                Assert.That(first.PsyHealth.fillAmount, Is.EqualTo(0.25f).Within(0.000001f));
                Assert.That(first.Power.fillAmount, Is.EqualTo(0.45f).Within(0.000001f));
                Assert.That(first.PowerLimit.localPosition.x, Is.EqualTo(90.0f).Within(0.0001f));
            }
        }

        [Test]
        public void MissingOptionalBarsDoNotPreventTheBoundBarFromUpdating()
        {
            using (var rig = new Rig())
            {
                rig.Condition.SetPsyHealth(35.0f);

                Assert.DoesNotThrow(() => rig.Hud.Bind(rig.Condition, null, rig.PsyHealth, null, null, rig.PsyHealthLimit, null));

                Assert.That(rig.PsyHealth.fillAmount, Is.EqualTo(0.35f).Within(0.000001f));
                Assert.That(rig.PsyHealthLimit.localPosition.x, Is.EqualTo(150.0f).Within(0.0001f));
            }
        }

        [Test]
        public void DestroyedConditionDoesNotOverwriteOrAccessTheRemainingHud()
        {
            using (var rig = new Rig())
            {
                UnityEngine.Object.DestroyImmediate(rig.Condition);
                rig.Health.fillAmount = 0.73f;

                Assert.DoesNotThrow(rig.Hud.Refresh);

                Assert.That(rig.Health.fillAmount, Is.EqualTo(0.73f).Within(0.000001f));
            }
        }
    }
}
