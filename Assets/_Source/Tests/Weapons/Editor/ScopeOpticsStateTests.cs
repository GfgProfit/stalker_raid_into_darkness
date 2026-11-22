using System.Reflection;
using NUnit.Framework;
using RaidIntoDarkness.Weapons.Optics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Weapons.Tests
{
    public sealed class ScopeOpticsStateTests
    {
        private ScopeOpticsConfig _configuration;

        [SetUp]
        public void SetUp()
        {
            _configuration = ScriptableObject.CreateInstance<ScopeOpticsConfig>();
            _configuration.hideFlags = HideFlags.HideAndDontSave;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_configuration);
        }

        [Test]
        public void MagnificationControlsClampAndImmediateUpdatesDoNotRequireATick()
        {
            var state = new ScopeOpticsState(_configuration);
            state.SetMagnification(100f, _configuration, true);
            Assert.That(state.Magnification, Is.EqualTo(_configuration.MaxMagnification));
            Assert.That(state.TargetMagnification, Is.EqualTo(_configuration.MaxMagnification));
            state.SetMagnification(-5f, _configuration, true);
            Assert.That(state.Magnification, Is.EqualTo(_configuration.MinMagnification));
            Assert.That(state.TargetMagnification, Is.EqualTo(_configuration.MinMagnification));
        }

        [Test]
        public void DiscreteMagnificationOnlyUsesConfiguredStepsAndChangesImmediately()
        {
            SetField(_configuration, "_maxMagnification", 4f);
            SetField(_configuration, "_calibrationMagnification", 1f);
            SetField(_configuration, "_magnificationSteps", new[] { 1f, 2f, 4f });
            Assert.That(_configuration.TryValidate(out string diagnostic), Is.True, diagnostic);
            var state = new ScopeOpticsState(_configuration);
            state.SetMagnification(_configuration.GetNextMagnificationStep(state.Magnification, 1f), _configuration);
            Assert.That(state.Magnification, Is.EqualTo(2f));
            state.SetMagnification(_configuration.GetNextMagnificationStep(state.Magnification, 1f), _configuration);
            Assert.That(state.Magnification, Is.EqualTo(4f));
            state.SetMagnification(_configuration.GetNextMagnificationStep(state.Magnification, -1f), _configuration);
            Assert.That(state.Magnification, Is.EqualTo(2f));
            Assert.That(state.TargetMagnification, Is.EqualTo(state.Magnification));
            state.SetMagnification(3.9f, _configuration);
            Assert.That(state.Magnification, Is.EqualTo(4f));
        }

        [Test]
        public void MagnificationResponseIsIndependentOfFrameSubdivision()
        {
            var coarse = new ScopeOpticsState(_configuration);
            var fine = new ScopeOpticsState(_configuration);
            coarse.SetMagnification(1f, _configuration, true);
            fine.SetMagnification(1f, _configuration, true);
            coarse.SetMagnification(6f, _configuration);
            fine.SetMagnification(6f, _configuration);
            coarse.Tick(0.1f, 12f, _configuration);
            for (int index = 0; index < 10; index++)
            {
                fine.Tick(0.01f, 12f, _configuration);
            }
            Assert.That(coarse.Magnification, Is.EqualTo(fine.Magnification).Within(0.00001f));
        }

        [Test]
        public void RuntimeControlsNeverMutateSharedConfiguration()
        {
            string original = JsonUtility.ToJson(_configuration);
            var first = new ScopeOpticsState(_configuration);
            var second = new ScopeOpticsState(_configuration);
            first.SetActive(true);
            first.SetMagnification(6f, _configuration, true);
            first.SetCorrections(4f, -3f, _configuration);
            Assert.That(second.Active, Is.False);
            Assert.That(JsonUtility.ToJson(_configuration), Is.EqualTo(original));
        }

        [Test]
        public void StateRoundTripPreservesSerializableControls()
        {
            var state = new ScopeOpticsState(_configuration);
            state.SetActive(true);
            state.SetMagnification(3f, _configuration, true);
            state.SetCorrections(2f, -3f, _configuration);
            string json = JsonUtility.ToJson(state);
            ScopeOpticsState restored = JsonUtility.FromJson<ScopeOpticsState>(json);
            restored.Sanitize(_configuration);
            Assert.That(restored.Active, Is.EqualTo(state.Active));
            Assert.That(restored.Magnification, Is.EqualTo(state.Magnification));
            Assert.That(restored.TargetMagnification, Is.EqualTo(state.TargetMagnification));
            Assert.That(restored.WindageMilliradians, Is.EqualTo(state.WindageMilliradians));
            Assert.That(restored.ElevationMilliradians, Is.EqualTo(state.ElevationMilliradians));
        }

        [Test]
        public void InvalidSerializedNumbersAreSanitizedBeforeUse()
        {
            var state = new ScopeOpticsState(_configuration);
            SetField(state, "_magnification", float.NaN);
            SetField(state, "_targetMagnification", float.PositiveInfinity);
            SetField(state, "_windageMilliradians", float.NaN);
            SetField(state, "_elevationMilliradians", float.NegativeInfinity);
            state.Sanitize(_configuration);
            Assert.That(float.IsFinite(state.Magnification), Is.True);
            Assert.That(float.IsFinite(state.TargetMagnification), Is.True);
            Assert.That(float.IsFinite(state.WindageMilliradians), Is.True);
            Assert.That(float.IsFinite(state.ElevationMilliradians), Is.True);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(instance, value);
        }
    }
}
