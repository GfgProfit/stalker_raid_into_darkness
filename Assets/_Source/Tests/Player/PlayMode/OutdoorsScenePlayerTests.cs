using System.Collections;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class OutdoorsScenePlayerTests
    {
        private const string SCENE = "OutdoorsScene";

        [UnityTest]
        public IEnumerator PlayerSpawnsMovesAndSwitchesOutfits()
        {
            SceneManager.LoadScene(SCENE, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForSeconds(1.0f);
            StalkerPlayerController player = Object.FindAnyObjectByType<StalkerPlayerController>();
            Assert.That(player, Is.Not.Null, "OutdoorsScene has no StalkerPlayerController");
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Assert.That(camera.transform.IsChildOf(player.transform), Is.True, "Camera.main is not the player camera");
            Assert.That(player.Motor.Physics.GoodGround, Is.True, "player is not standing on ground");
            Assert.That(player.Rig.Body, Is.Not.Null, "body was not created");
            Assert.That(player.BodyPlayer.IsBound, Is.True, "body PlayableGraph is not bound");
            Assert.That(player.OutfitSection, Is.EqualTo("actor"));
            Assert.That(player.Rig.Body.GetComponent<XRay.Importer.XRayRigPlayer>(), Is.Null, "XRayRigPlayer competes with the Playables body player");
            TestContext.WriteLine($"spawn: {player.transform.position}, camera height {player.Motor.Stance.CameraHeight.ToString("0.000", CultureInfo.InvariantCulture)}, body motion {player.BodyPlayer.CurrentMotion}");
            AssertNoShadows(player.Rig.Body);
            AssertNoShadows(player.Rig.Hands);
            string idle = player.BodyPlayer.CurrentMotion;
            Assert.That(idle, Is.Not.Null.And.Not.Empty);

            Vector3 start = player.transform.position;
            player.ScriptedWish = StalkerMoveFlags.Forward;
            yield return new WaitForSeconds(1.5f);
            float speed = player.State.HorizontalVelocity.magnitude;
            string run = player.BodyPlayer.CurrentMotion;
            TestContext.WriteLine($"run: {speed.ToString("0.000", CultureInfo.InvariantCulture)} m/s, moved {Vector3.Distance(start, player.transform.position).ToString("0.00", CultureInfo.InvariantCulture)} m, motion {run}, blend [{player.BodyPlayer.DescribeBlend()}]");
            Assert.That(speed, Is.GreaterThan(2.0f));
            Assert.That(run, Is.Not.EqualTo(idle));

            player.ScriptedWish = StalkerMoveFlags.Crouch;
            yield return new WaitForSeconds(0.6f);
            Assert.That(player.State.IsCrouching, Is.True);
            TestContext.WriteLine($"crouch: camera {player.Motor.Stance.CameraHeight.ToString("0.000", CultureInfo.InvariantCulture)}, motion {player.BodyPlayer.CurrentMotion}");

            player.ScriptedWish = StalkerMoveFlags.None;
            yield return new WaitForSeconds(0.6f);
            player.ScriptedWish = StalkerMoveFlags.Jump;
            yield return new WaitForSeconds(0.15f);
            player.ScriptedWish = StalkerMoveFlags.None;
            float apexFeet = player.transform.position.y;
            string jumpMotion = player.BodyPlayer.CurrentMotion;
            yield return new WaitForSeconds(0.25f);
            apexFeet = Mathf.Max(apexFeet, player.transform.position.y);
            TestContext.WriteLine($"jump: feet {apexFeet.ToString("0.00", CultureInfo.InvariantCulture)} m, motion at take-off {jumpMotion}, in air {player.BodyPlayer.CurrentMotion}");
            Assert.That(apexFeet, Is.GreaterThan(start.y + 0.5f));
            yield return new WaitForSeconds(1.0f);
            Assert.That(player.Motor.Physics.GoodGround, Is.True, "did not land");

            GameObject oldBody = player.Rig.Body;
            PlayableGraph oldGraph = player.BodyPlayer.Graph;
            Assert.That(player.ApplyOutfit("military_exo_outfit"), Is.True);
            yield return null;
            Assert.That(player.OutfitSection, Is.EqualTo("military_exo_outfit"));
            Assert.That(player.Rig.Body, Is.Not.Null);
            Assert.That(player.Rig.Body, Is.Not.EqualTo(oldBody));
            Assert.That(oldGraph.IsValid(), Is.False, "old PlayableGraph leaked");
            AssertNoShadows(player.Rig.Body);
            AssertNoShadows(player.Rig.Hands);
            Assert.That(player.BodyPlayer.IsBound, Is.True);
            player.ScriptedWish = StalkerMoveFlags.Forward;
            yield return new WaitForSeconds(1.0f);
            TestContext.WriteLine($"exo: body {player.Rig.Body.name}, motion {player.BodyPlayer.CurrentMotion}, speed {player.State.HorizontalVelocity.magnitude.ToString("0.000", CultureInfo.InvariantCulture)}");
            Assert.That(player.State.HorizontalVelocity.magnitude, Is.GreaterThan(2.0f));

            GameObject exoBody = player.Rig.Body;
            Assert.That(player.ApplyOutfit("army_nosorog_outfit"), Is.True);
            yield return null;
            TestContext.WriteLine($"nosorog shares the exo body: {player.Rig.Body == exoBody}");
            Assert.That(player.Rig.Body, Is.EqualTo(exoBody), "body was recreated although the OGF is the same");
            player.ScriptedWish = StalkerMoveFlags.Forward;
            yield return new WaitForSeconds(0.8f);
            player.ScriptedWish = StalkerMoveFlags.Forward | StalkerMoveFlags.Jump;
            yield return new WaitForSeconds(0.1f);
            player.ScriptedWish = StalkerMoveFlags.Forward;
            float timeout = 2.0f;
            while (!player.State.IsJumping && timeout > 0.0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            while ((player.State.IsJumping || player.State.Environment == StalkerEnvironment.InAir) && timeout > 0.0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            int maxSlots = 0;
            float landed = Time.time;
            float nextSample = 0.0f;
            var samples = new System.Text.StringBuilder();
            while (Time.time - landed < 0.6f)
            {
                maxSlots = Mathf.Max(maxSlots, player.BodyPlayer.ActiveSlots);
                if (Time.time - landed < nextSample)
                {
                    yield return null;
                    continue;
                }

                nextSample += 0.05f;
                samples.Append($"{(Time.time - landed).ToString("0.00", CultureInfo.InvariantCulture)}: [{player.BodyPlayer.DescribeBlend()}]\n");
                yield return null;
            }

            TestContext.WriteLine("landing into run:\n" + samples);
            Assert.That(maxSlots, Is.GreaterThanOrEqualTo(2), "landing switched to locomotion without a crossfade");
            Assert.That(player.BodyPlayer.CurrentMotion, Is.Not.EqualTo(jumpMotion));
            Assert.That(player.BodyPlayer.CurrentWeight, Is.EqualTo(1.0f));
            player.ScriptedWish = StalkerMoveFlags.None;
            yield return new WaitForSeconds(0.5f);
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertNoShadows(GameObject model)
        {
            Assert.That(model, Is.Not.Null);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off), renderer.name);
            }
        }
    }
}
