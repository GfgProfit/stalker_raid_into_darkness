using System.Collections;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class OutdoorsSceneCameraSmoothnessTests
    {
        private const float MAX_ANGULAR_SPEED = 30.0f;
        private const float MAX_LINEAR_SPEED = 0.15f;
        private const float ANGLE_EPSILON = 0.02f;
        private const float POSITION_EPSILON = 0.0003f;

        [UnityTest]
        public IEnumerator CameraEffectsStayContinuousOnStartAndDirectionChanges()
        {
            SceneManager.LoadScene("OutdoorsScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1.5f);
            StalkerPlayerController player = Object.FindAnyObjectByType<StalkerPlayerController>();
            Transform pivot = player.CameraEffects.Pivot;
            int steps = 0;
            player.Footsteps.Footstep += step => steps++;
            StalkerMoveFlags[] plan = { StalkerMoveFlags.Forward, StalkerMoveFlags.Forward | StalkerMoveFlags.RightStrafe, StalkerMoveFlags.RightStrafe, StalkerMoveFlags.Back, StalkerMoveFlags.LeftStrafe, StalkerMoveFlags.Forward | StalkerMoveFlags.Accel, StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint, StalkerMoveFlags.None, StalkerMoveFlags.Forward | StalkerMoveFlags.LeftStrafe };
            Vector3 lastPosition = pivot.localPosition;
            Quaternion lastRotation = pivot.localRotation;
            float worstAngle = 0.0f;
            float worstMove = 0.0f;
            string worst = string.Empty;
            foreach (StalkerMoveFlags wish in plan)
            {
                player.ScriptedWish = wish;
                float timer = 0.0f;
                while (timer < 1.2f)
                {
                    yield return null;
                    float dt = Time.deltaTime;
                    timer += dt;
                    float angle = Quaternion.Angle(lastRotation, pivot.localRotation);
                    float move = (pivot.localPosition - lastPosition).magnitude;
                    float angleExcess = angle - (MAX_ANGULAR_SPEED * dt + ANGLE_EPSILON);
                    float moveExcess = move - (MAX_LINEAR_SPEED * dt + POSITION_EPSILON);
                    if (angleExcess > worstAngle || moveExcess > worstMove)
                    {
                        worst = $"{wish} at {F(timer)} s: {F(angle)} deg, {F(move * 1000.0f)} mm in {F(dt * 1000.0f)} ms, source {player.Footsteps.ActiveSource}, motion {player.BodyPlayer.CurrentMotion}";
                    }

                    worstAngle = Mathf.Max(worstAngle, angleExcess);
                    worstMove = Mathf.Max(worstMove, moveExcess);
                    lastPosition = pivot.localPosition;
                    lastRotation = pivot.localRotation;
                }
            }

            player.ScriptedWish = StalkerMoveFlags.None;
            TestContext.WriteLine($"steps {steps}, worst angle excess {F(worstAngle)} deg, worst move excess {F(worstMove * 1000.0f)} mm; {worst}");
            Assert.That(worstAngle, Is.LessThanOrEqualTo(0.0f), worst);
            Assert.That(worstMove, Is.LessThanOrEqualTo(0.0f), worst);
            Assert.That(steps, Is.GreaterThan(10));
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
