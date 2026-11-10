using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayAnomalyScheduleTests
    {
        [Test]
        public void EditorConfigurationAndEarlyCallbacksDoNotTouchUninitializedEndpointsBeforeStart()
        {
            Assert.That(Application.isPlaying, Is.False);
            Scene scene = EditorSceneManager.NewPreviewScene();
            GameObject owner = new GameObject(nameof(XRayAnomalyScheduleTests));
            SceneManager.MoveGameObjectToScene(owner, scene);
            owner.SetActive(false);
            try
            {
                var endpoint = new ActivitySource();
                XRayAnomalySchedule schedule = owner.AddComponent<XRayAnomalySchedule>();
                schedule.Configure(1, 1, 1);
                typeof(XRayAnomalySchedule).GetField("_sources", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(schedule, new IXRayAnomalyActivitySource[] { endpoint });
                typeof(XRayAnomalySchedule).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(schedule, true);
                Invoke(schedule, "Awake");
                Invoke(schedule, "OnEnable");
                Assert.That(endpoint.ActivityChanges, Is.Zero);
                Assert.That(endpoint.GateReads, Is.Zero);
                Assert.That(endpoint.IsActive, Is.True);
                endpoint.Initialize();
                Invoke(schedule, "Start");
                Assert.That(endpoint.ActivityChanges, Is.EqualTo(1));
                Assert.That(endpoint.GateReads, Is.EqualTo(1));
                Assert.That(endpoint.IsActive, Is.False);
                Assert.That(schedule.IsActive, Is.False);
                uint epoch = (uint)typeof(XRayAnomalySchedule).GetField("_startedAt", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(schedule);
                schedule.Tick(unchecked(epoch + 1000U));
                Assert.That(endpoint.ActivityChanges, Is.EqualTo(2));
                Assert.That(endpoint.GateReads, Is.EqualTo(1));
                Assert.That(endpoint.IsActive, Is.True);
                Assert.That(schedule.IsActive, Is.True);
                Assert.That(owner.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void NativeScheduleRequiresBothNonzeroDurationFields()
        {
            foreach (uint now in new[] { 0U, 1000U, 999999U, uint.MaxValue })
            {
                Assert.That(XRayAnomalySchedule.Evaluate(0, 0, 7, 0, now), Is.True);
                Assert.That(XRayAnomalySchedule.Evaluate(0, 5, 7, 0, now), Is.True);
                Assert.That(XRayAnomalySchedule.Evaluate(5, 0, 7, 0, now), Is.True);
            }
        }

        [Test]
        public void MillisecondIntervalsUseTheOriginalStrictEnabledEndBoundary()
        {
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 0, 10000, 10000), Is.True);
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 0, 10000, 11999), Is.True);
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 0, 10000, 12000), Is.False);
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 0, 10000, 14999), Is.False);
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 0, 10000, 15000), Is.True);
        }

        [Test]
        public void PositiveSourceStartShiftAdvancesThePhaseInsteadOfDelayingIt()
        {
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 1, 0, 0), Is.True);
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 1, 0, 999), Is.True);
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 1, 0, 1000), Is.False);
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 1, 0, 3999), Is.False);
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 1, 0, 4000), Is.True);
        }

        [Test]
        public void AShiftLongerThanTheCycleKeepsNativeModuloSemantics()
        {
            foreach (uint elapsed in new[] { 0U, 999U, 1000U, 4000U, 15000U })
            {
                Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 11, 0, elapsed), Is.EqualTo(XRayAnomalySchedule.Evaluate(2, 3, 1, 0, elapsed)));
            }
        }

        [Test]
        public void DeviceClockWrappingDoesNotResetTheSpawnEpoch()
        {
            uint started = uint.MaxValue - 499U;
            Assert.That(XRayAnomalySchedule.Evaluate(1, 1, 0, started, uint.MaxValue), Is.True);
            Assert.That(XRayAnomalySchedule.Evaluate(1, 1, 0, started, 499), Is.True);
            Assert.That(XRayAnomalySchedule.Evaluate(1, 1, 0, started, 500), Is.False);
            Assert.That(XRayAnomalySchedule.Evaluate(1, 1, 0, started, 1500), Is.True);
        }

        [Test]
        public void SourceSecondConversionUsesTheOriginalUnsignedIntegerArithmetic()
        {
            Assert.That(XRayAnomalySchedule.Evaluate(4294968, 1, 0, 0, 703), Is.True);
            Assert.That(XRayAnomalySchedule.Evaluate(4294968, 1, 0, 0, 704), Is.False);
            Assert.That(XRayAnomalySchedule.Evaluate(4294968, 1, 0, 0, 1704), Is.True);
            Assert.That(XRayAnomalySchedule.Evaluate(uint.MaxValue, 1, 0, 0, 1000), Is.True);
        }

        [Test]
        public void ACompletedOffWindowCannotCancelAnOngoingAwakingBlowoutOrAccumulatePhase()
        {
            Assert.That(XRayAnomalySchedule.ResolveActivity(true, false, false), Is.True);
            Assert.That(XRayAnomalySchedule.ResolveActivity(true, false, true), Is.False);
            Assert.That(XRayAnomalySchedule.ResolveActivity(true, true, false), Is.True);
        }

        [Test]
        public void NativeReactivationDoesNotWaitForTheDeactivationGate()
        {
            Assert.That(XRayAnomalySchedule.ResolveActivity(false, true, false), Is.True);
            Assert.That(XRayAnomalySchedule.ResolveActivity(false, true, true), Is.True);
            Assert.That(XRayAnomalySchedule.ResolveActivity(false, false, false), Is.False);
        }

        [Test]
        public void AnUnchangedEngineClockKeepsThePhasePausedWithoutAnyWeatherGameTimeInput()
        {
            bool before = XRayAnomalySchedule.Evaluate(2, 3, 0, 10000, 12400);
            for (int frame = 0; frame < 100; frame++)
            {
                Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 0, 10000, 12400), Is.EqualTo(before));
            }
            Assert.That(before, Is.False);
            Assert.That(XRayAnomalySchedule.Evaluate(2, 3, 0, 10000, 15000), Is.True);
        }

        [Test]
        public void SerializedSourceTimingFieldsRemainUnsignedAndRuntimeHasNoPlayerAssemblyDependency()
        {
            foreach (string name in new[] { "_enabledTime", "_disabledTime", "_startTimeShift" })
            {
                FieldInfo field = typeof(XRayAnomalySchedule).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, name);
                Assert.That(field.FieldType, Is.EqualTo(typeof(uint)), name);
                Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null, name);
            }
            foreach (AssemblyName assembly in typeof(XRayAnomalySchedule).Assembly.GetReferencedAssemblies())
            {
                Assert.That(assembly.Name, Is.Not.EqualTo("RaidIntoDarkness.Player"));
            }
        }

        private static void Invoke(XRayAnomalySchedule source, string name)
        {
            MethodInfo method = typeof(XRayAnomalySchedule).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(source, null);
        }

        private sealed class ActivitySource : IXRayAnomalyActivitySource
        {
            private bool _ready;
            private bool _active = true;
            private int _activityChanges;
            private int _gateReads;

            public bool IsActive => _active;
            public int ActivityChanges => _activityChanges;
            public int GateReads => _gateReads;
            public bool CanDeactivate
            {
                get
                {
                    if (_ready == false)
                    {
                        throw new System.InvalidOperationException("The schedule queried an endpoint before initialization.");
                    }
                    _gateReads++;
                    return true;
                }
            }

            public void Initialize()
            {
                _ready = true;
            }

            public void SetSourceActive(bool active)
            {
                if (_ready == false)
                {
                    throw new System.InvalidOperationException("The schedule activated an endpoint before initialization.");
                }
                _active = active;
                _activityChanges++;
            }
        }
    }
}
