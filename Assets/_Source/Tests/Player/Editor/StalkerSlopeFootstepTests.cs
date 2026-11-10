using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerSlopeFootstepTests
    {
        private const float DT = 1.0f / 60.0f;
        private const StalkerMoveFlags WALK = StalkerMoveFlags.Forward | StalkerMoveFlags.Accel;

        private sealed class RampRig : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _ramp;
            private readonly GameObject _player;
            private readonly Vector3 _origin = new Vector3(4096.0f, 256.0f, 4096.0f);
            private readonly float _angle;
            private readonly StalkerFootstepSynchronizer _footsteps;
            private readonly StalkerSurfaceDetector _detector;

            public StalkerMovementMotor Motor { get; }
            public int SupportedFrames { get; private set; }
            public int AirFrames { get; private set; }
            public int DetectorMisses { get; private set; }
            public int StepCount { get; private set; }
            public int UnsupportedSteps { get; private set; }
            public int StepsWithoutDetectedSurface { get; private set; }
            public float MinimumFeetGap { get; private set; } = float.PositiveInfinity;
            public float MaximumFeetGap { get; private set; } = float.NegativeInfinity;

            public RampRig(float angle, bool downhill)
            {
                _angle = angle * Mathf.Deg2Rad;
                Scene activeScene = SceneManager.GetActiveScene();
                _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (activeScene.IsValid())
                {
                    SceneManager.SetActiveScene(activeScene);
                }
                try
                {
                    _ramp = new GameObject("Ramp", typeof(BoxCollider));
                    _ramp.hideFlags = HideFlags.HideAndDontSave;
                    SceneManager.MoveGameObjectToScene(_ramp, _scene);
                    _ramp.transform.SetPositionAndRotation(_origin, Quaternion.Euler(-angle, 0.0f, 0.0f));
                    _ramp.GetComponent<BoxCollider>().size = new Vector3(20.0f, 0.5f, 100.0f);
                    _player = new GameObject("SlopePlayer");
                    _player.hideFlags = HideFlags.HideAndDontSave;
                    SceneManager.MoveGameObjectToScene(_player, _scene);
                    Motor = _player.AddComponent<StalkerMovementMotor>();
                    var profile = AssetDatabase.LoadAssetAtPath<StalkerMovementProfile>(StalkerMotorHarness.PROFILE_PATH);
                    Assert.That(profile, Is.Not.Null);
                    Motor.Initialize(profile);
                    _detector = _player.AddComponent<StalkerSurfaceDetector>();
                    _detector.Initialize(null, ~0);
                    _footsteps = _player.AddComponent<StalkerFootstepSynchronizer>();
                    var library = AssetDatabase.LoadAssetAtPath<StalkerFootstepTimingLibrary>(StalkerFootstepTests.TIMING_PATH);
                    Assert.That(library, Is.Not.Null);
                    var serialized = new SerializedObject(_footsteps);
                    serialized.FindProperty("_library").objectReferenceValue = library;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    float z = downhill ? 8.0f : -8.0f;
                    float radius = Motor.GetComponent<CharacterController>().radius;
                    float y = SurfaceHeight(z) + radius * (1.0f / Mathf.Cos(_angle) - 1.0f) + 0.05f;
                    Motor.Teleport(_origin + new Vector3(0.0f, y, z));
                    Motor.SetView(downhill ? 180.0f : 0.0f, downhill ? Vector3.back : Vector3.forward);
                    Physics.SyncTransforms();
                    for (int i = 0; i < 36; i++)
                    {
                        Frame(StalkerMoveFlags.None, false);
                    }
                    _footsteps.Footstep += OnFootstep;
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Frame(StalkerMoveFlags wish, bool score = true)
            {
                Motor.Wish = (Motor.Wish & ~StalkerMoveFlags.Jump) | wish;
                Motor.Tick(DT);
                Motor.ClearHeldCommands();
                _detector.Detect();
                if (score)
                {
                    StalkerMovementState state = Motor.State;
                    bool airborne = state.IsJumping || state.Environment == StalkerEnvironment.InAir;
                    if (airborne)
                    {
                        AirFrames++;
                    }
                    if (Motor.HasFootstepSupport && !airborne)
                    {
                        SupportedFrames++;
                        if (!_detector.HasGround)
                        {
                            DetectorMisses++;
                        }
                        Vector3 feet = Motor.transform.position - _origin;
                        float gap = feet.y - SurfaceHeight(feet.z);
                        MinimumFeetGap = Mathf.Min(MinimumFeetGap, gap);
                        MaximumFeetGap = Mathf.Max(MaximumFeetGap, gap);
                    }
                }
                _footsteps.Tick(Motor.State, null, DT, Motor.HasFootstepSupport);
            }

            public void Run(StalkerMoveFlags wish, float seconds)
            {
                int frames = Mathf.CeilToInt(seconds / DT);
                for (int i = 0; i < frames; i++)
                {
                    Frame(wish);
                }
            }

            public string Describe()
            {
                return $"supported={SupportedFrames}; air={AirFrames}; detectorMisses={DetectorMisses}; steps={StepCount}; unsupportedSteps={UnsupportedSteps}; stepsWithoutSurface={StepsWithoutDetectedSurface}; feetGap=[{MinimumFeetGap:R},{MaximumFeetGap:R}]; position={Motor.transform.position:R}";
            }

            public void Dispose()
            {
                if (_player != null)
                {
                    UnityEngine.Object.DestroyImmediate(_player);
                }
                if (_ramp != null)
                {
                    UnityEngine.Object.DestroyImmediate(_ramp);
                }
                if (_scene.IsValid() && _scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(_scene, true);
                }
            }

            private float SurfaceHeight(float z)
            {
                return Mathf.Tan(_angle) * z + 0.25f / Mathf.Cos(_angle);
            }

            private void OnFootstep(StalkerFootstep step)
            {
                StepCount++;
                StalkerMovementState state = Motor.State;
                if (!Motor.HasFootstepSupport || state.IsJumping || state.Environment == StalkerEnvironment.InAir)
                {
                    UnsupportedSteps++;
                }
                if (!_detector.HasGround)
                {
                    StepsWithoutDetectedSurface++;
                }
            }
        }

        [TestCase(20.0f, false)]
        [TestCase(20.0f, true)]
        [TestCase(35.0f, false)]
        [TestCase(35.0f, true)]
        [TestCase(44.0f, false)]
        [TestCase(44.0f, true)]
        public void SupportedRampWalkingKeepsSurfaceDetectionAndStepEvents(float angle, bool downhill)
        {
            using (var rig = new RampRig(angle, downhill))
            {
                float startY = rig.Motor.transform.position.y;

                rig.Run(WALK, 3.0f);

                string diagnostic = rig.Describe();
                float heightChange = rig.Motor.transform.position.y - startY;
                Assert.That(downhill ? -heightChange : heightChange, Is.GreaterThan(0.2f), "The fixture did not traverse the requested slope. " + diagnostic);
                Assert.That(rig.SupportedFrames, Is.GreaterThan(60), diagnostic);
                Assert.That(rig.DetectorMisses, Is.Zero, "The capsule has physical support but the footstep surface query misses the slope. " + diagnostic);
                Assert.That(rig.StepCount, Is.GreaterThanOrEqualTo(2), diagnostic);
                Assert.That(rig.UnsupportedSteps, Is.Zero, diagnostic);
                Assert.That(rig.StepsWithoutDetectedSurface, Is.Zero, diagnostic);
            }
        }

        [TestCase(20.0f)]
        [TestCase(35.0f)]
        [TestCase(44.0f)]
        public void RepeatedSlopeJumpsSuppressAirStepsAndResumeSupportedSteps(float angle)
        {
            using (var rig = new RampRig(angle, false))
            {
                rig.Run(WALK, 0.8f);
                for (int jump = 0; jump < 2; jump++)
                {
                    rig.Frame(WALK | StalkerMoveFlags.Jump);
                    rig.Run(WALK, 2.0f);
                }

                string diagnostic = rig.Describe();
                Assert.That(rig.AirFrames, Is.GreaterThan(10), diagnostic);
                Assert.That(rig.SupportedFrames, Is.GreaterThan(30), diagnostic);
                Assert.That(rig.StepCount, Is.GreaterThanOrEqualTo(2), diagnostic);
                Assert.That(rig.UnsupportedSteps, Is.Zero, diagnostic);
                Assert.That(rig.DetectorMisses, Is.Zero, "The landed capsule is supported but its surface is not detected. " + diagnostic);
                Assert.That(rig.StepsWithoutDetectedSurface, Is.Zero, diagnostic);
            }
        }

        [Test]
        public void GroundContactAfterLandingMatchesFreshGroundContact()
        {
            var profile = AssetDatabase.LoadAssetAtPath<StalkerMovementProfile>(StalkerMotorHarness.PROFILE_PATH);
            Assert.That(profile, Is.Not.Null);
            var landed = new StalkerCharacterPhysics(profile);
            Vector3 normal = Quaternion.Euler(-35.0f, 0.0f, 0.0f) * Vector3.up;
            Vector3 control = Vector3.forward * 10.0f;
            landed.SetControl(control);
            landed.AddContact(normal);
            landed.RequestJump();
            landed.Step(Vector3.zero, profile.Engine.FixedStep);
            Assert.That(landed.Jumping, Is.True);
            landed.ResetContacts();
            Vector3 landingVelocity = new Vector3(0.0f, -3.0f, 4.0f);
            landed.SetVelocity(landingVelocity);
            landed.AddContact(normal);
            Vector3 afterJump = landed.Step(Vector3.zero, profile.Engine.FixedStep);
            var fresh = new StalkerCharacterPhysics(profile);
            fresh.SetControl(control);
            fresh.SetVelocity(landingVelocity);
            fresh.AddContact(normal);
            Vector3 withoutJump = fresh.Step(Vector3.zero, profile.Engine.FixedStep);

            string diagnostic = $"postLandingDisplacement={afterJump:R}; freshGroundDisplacement={withoutJump:R}; difference={Vector3.Distance(afterJump, withoutJump):R}";
            Assert.That(landed.Jumping, Is.False);
            Assert.That(Vector3.Distance(afterJump, withoutJump), Is.LessThan(0.000001f), "A completed jump must not permanently change grounded slope integration. " + diagnostic);
            Assert.That(Vector3.Distance(landed.Velocity, fresh.Velocity), Is.LessThan(0.000001f), diagnostic);
        }

        [Test]
        public void ExistingLadderControlCountsAsFootstepSupport()
        {
            using (var rig = new RampRig(20.0f, false))
            {
                rig.Motor.Teleport(rig.Motor.transform.position + Vector3.up * 3.0f);
                rig.Motor.Physics.ResetContacts();
                var controller = rig.Motor.GetComponent<CharacterController>();
                controller.Move(Vector3.up * 0.01f);
                rig.Motor.Physics.Climbing = true;

                Assert.That(rig.Motor.Physics.GoodGround, Is.False);
                Assert.That(controller.isGrounded, Is.False);
                Assert.That(rig.Motor.HasFootstepSupport, Is.True);
            }
        }

        [Test]
        public void RetainedGroundControlDoesNotPretendToBePhysicalFootstepSupport()
        {
            using (var rig = new RampRig(20.0f, false))
            {
                rig.Motor.Teleport(rig.Motor.transform.position + Vector3.up * 3.0f);
                rig.Motor.Physics.ResetContacts();
                rig.Motor.GetComponent<CharacterController>().Move(Vector3.up * 0.01f);

                Assert.That(rig.Motor.Environment, Is.EqualTo(StalkerEnvironment.OnGround));
                Assert.That(rig.Motor.HasFootstepSupport, Is.False);
            }
        }
    }
}
