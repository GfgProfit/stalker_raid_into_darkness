using System.Collections;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class OutdoorsSceneFoleyTests
    {
        private const string SCENE = "OutdoorsScene";

        private StalkerPlayerController _player;
        private int _steps;
        private int _bodySteps;
        private int _airSteps;
        private string _lastSurface;

        [UnityTest]
        public IEnumerator FoleyAndCameraEffectsWorkInOutdoorsScene()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1.0f);
            _player = Object.FindAnyObjectByType<StalkerPlayerController>();
            Assert.That(_player, Is.Not.Null);
            Assert.That(_player.CameraEffects, Is.Not.Null, "StalkerCameraEffects is not connected");
            Assert.That(_player.Footsteps, Is.Not.Null, "StalkerFootstepSynchronizer is not connected");
            Assert.That(_player.Foley, Is.Not.Null, "StalkerPlayerFoley is not connected");
            Assert.That(_player.SurfaceDetector, Is.Not.Null, "StalkerSurfaceDetector is not connected");
            Assert.That(_player.CameraEffects.Profile, Is.Not.Null);
            Assert.That(_player.Footsteps.Library, Is.Not.Null);
            Assert.That(_player.Foley.Library, Is.Not.Null);
            Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1), "exactly one AudioListener expected");
            Transform pivot = _player.CameraEffects.Pivot;
            Assert.That(pivot, Is.Not.Null);
            Assert.That(_player.CameraController.Camera.transform.parent, Is.EqualTo(pivot), "camera must sit under CameraMotionPivot");
            Assert.That(pivot.parent, Is.EqualTo(_player.CameraController.CameraRoot));
            Assert.That(_player.Rig.Body, Is.Not.Null);
            _player.Footsteps.Footstep += OnFootstep;
            _player.CameraController.SetLook(0.0f, 0.0f);

            yield return new WaitForSeconds(1.0f);
            Assert.That(_steps, Is.EqualTo(0), "steps while idle");
            Assert.That(pivot.localPosition.magnitude, Is.LessThan(0.0001f), "camera offset while idle");

            _player.ScriptedWish = StalkerMoveFlags.Forward;
            float maxOffset = 0.0f;
            float timer = 0.0f;
            while (timer < 2.5f)
            {
                timer += Time.deltaTime;
                maxOffset = Mathf.Max(maxOffset, pivot.localPosition.magnitude);
                yield return null;
            }

            TestContext.WriteLine($"run 2.5 s: steps {_steps} (body {_bodySteps}), max camera offset {F(maxOffset)} m, surface {_lastSurface}, last clip {_player.Foley.LastClipName}, why: {_player.Foley.LastReason}");
            Assert.That(_steps, Is.GreaterThan(4));
            Assert.That(_bodySteps, Is.GreaterThan(0), "no step came from the Swm body animation phase");
            Assert.That(maxOffset, Is.GreaterThan(0.002f));
            Assert.That(_player.Foley.FootstepCount, Is.GreaterThan(0));
            Assert.That(_player.Foley.LastClipName, Is.Not.Empty);

            _player.ScriptedWish = StalkerMoveFlags.None;
            yield return new WaitForSeconds(1.0f);
            Assert.That(pivot.localPosition.magnitude, Is.LessThan(0.0001f), "camera did not return to the base position");

            _player.Motor.Teleport(new Vector3(-7.5f, 0.1f, -12.0f));
            yield return new WaitForSeconds(0.5f);
            _player.Foley.PlayFootstep(new StalkerFootstep(StalkerFootSide.Left, 1.0f, "norm_walk_fwd_0", "test", 0.2f, StalkerFootstepSource.XRayLegsTiming, StalkerMovingMode.Walk));
            TestContext.WriteLine($"surface pad: {_player.SurfaceDetector.Current} via {_player.SurfaceDetector.CurrentSource} ({_player.SurfaceDetector.CurrentCollider?.name}), clip {_player.Foley.LastClipName}");
            Assert.That(_player.SurfaceDetector.Current, Is.Not.EqualTo(_player.Foley.Library.FallbackSurface), "test pad surface was not detected");

            int landings = _player.Foley.LandingCount;
            _airSteps = 0;
            _player.Motor.Teleport(_player.transform.position + Vector3.up * 3.0f);
            yield return new WaitForSeconds(1.5f);
            TestContext.WriteLine($"drop 3 m: landing speed {F(_player.Foley.LastLandingSpeed)} m/s, landing sounds {_player.Foley.LandingCount - landings}, air steps {_airSteps}");
            Assert.That(_player.Foley.LandingCount - landings, Is.EqualTo(1));
            Assert.That(_airSteps, Is.EqualTo(0));

            StalkerCameraEffects effects = _player.CameraEffects;
            Assert.That(_player.ApplyOutfit("military_exo_outfit"), Is.True);
            yield return null;
            Assert.That(_player.CameraEffects, Is.SameAs(effects));
            _steps = 0;
            _bodySteps = 0;
            _player.ScriptedWish = StalkerMoveFlags.Forward;
            yield return new WaitForSeconds(2.0f);
            TestContext.WriteLine($"exo run: steps {_steps} (body {_bodySteps}), motion {_player.BodyPlayer.CurrentMotion}");
            Assert.That(_bodySteps, Is.GreaterThan(0));
            _player.ScriptedWish = StalkerMoveFlags.None;
            Assert.That(_player.ApplyOutfit("stalker_outfit"), Is.True);
            yield return null;
            _steps = 0;
            _player.ScriptedWish = StalkerMoveFlags.Forward | StalkerMoveFlags.Accel;
            yield return new WaitForSeconds(2.0f);
            TestContext.WriteLine($"stalker_outfit walk: steps {_steps}, motion {_player.BodyPlayer.CurrentMotion}");
            Assert.That(_steps, Is.GreaterThan(0));
            _player.ScriptedWish = StalkerMoveFlags.None;
            _player.Footsteps.Footstep -= OnFootstep;
            yield return new WaitForSeconds(0.5f);
            LogAssert.NoUnexpectedReceived();
        }

        private void OnFootstep(StalkerFootstep step)
        {
            _steps++;
            if (step.Source == StalkerFootstepSource.BodyAnimation)
            {
                _bodySteps++;
            }

            StalkerMovementState state = _player.State;
            if (state.IsJumping || state.Environment == StalkerEnvironment.InAir)
            {
                _airSteps++;
            }

            _lastSurface = _player.SurfaceDetector.Current.ToString();
        }

        private static string F(float value)
        {
            return value.ToString("0.0000", CultureInfo.InvariantCulture);
        }
    }
}
