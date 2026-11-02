using System.Collections;
using NUnit.Framework;
using RaidIntoDarkness.Weapons;
using UnityEngine;
using UnityEngine.TestTools;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public sealed class MonolithBallisticsTests
    {
        [TestCase(0.1f, 95f, -0.04905f, 900f)]
        [TestCase(0.5f, 375f, -1.22625f, 500f)]
        [TestCase(0.9f, 495f, -3.97305f, 100f)]
        [TestCase(1.2f, 525f, -7.0632f, 0f)]
        [TestCase(2f, 605f, -19.62f, 0f)]
        public void TrajectoryMatchesMonolithSinglePlayerReference(float time, float distance, float height, float forwardSpeed)
        {
            Vector3 position = MonolithBallistics.Position(Vector3.zero, Vector3.forward * 1000f, time);
            Vector3 velocity = MonolithBallistics.Velocity(Vector3.forward * 1000f, time);
            Assert.That(position.z, Is.EqualTo(distance).Within(0.001f));
            Assert.That(position.y, Is.EqualTo(height).Within(0.0001f));
            Assert.That(velocity.z, Is.EqualTo(forwardSpeed).Within(0.001f));
            Assert.That(velocity.y, Is.EqualTo(-9.81f * time).Within(0.0001f));
        }

        [TestCase(1000f)]
        [TestCase(-1000f)]
        public void VerticalShotUsesMonolithVerticalBranch(float speed)
        {
            Vector3 origin = new Vector3(1f, 20f, 3f);
            Vector3 position = MonolithBallistics.Position(origin, Vector3.up * speed, 0.5f);
            Assert.That(position.x, Is.EqualTo(origin.x));
            Assert.That(position.z, Is.EqualTo(origin.z));
            Assert.That(position.y, Is.EqualTo(20f + speed * 0.5f - 1.22625f).Within(0.001f));
        }

        [Test]
        public void HigherFlatnessReducesDropAtTheSameDistance()
        {
            float slowTime = 1f - Mathf.Sqrt(1f - 200f / 300f);
            float fastTime = 1f - Mathf.Sqrt(1f - 200f / 930f);
            Vector3 slow = MonolithBallistics.Position(Vector3.zero, Vector3.forward * 300f, slowTime);
            Vector3 fast = MonolithBallistics.Position(Vector3.zero, Vector3.forward * 930f, fastTime);
            Assert.That(slow.z, Is.EqualTo(100f).Within(0.001f));
            Assert.That(fast.z, Is.EqualTo(100f).Within(0.001f));
            Assert.That(fast.y, Is.GreaterThan(slow.y));
            Assert.That(fastTime, Is.LessThan(slowTime));
        }

        [UnityTest]
        public IEnumerator FastBulletIgnoresOwnerAndStopsAtTheNearestThinWall()
        {
            Vector3 origin = new Vector3(10000f, 10000f, 10000f);
            GameObject owner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            owner.transform.position = origin + Vector3.forward * 2f;
            GameObject nearWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nearWall.transform.position = origin + Vector3.forward * 5f;
            nearWall.transform.localScale = new Vector3(10f, 10f, 0.01f);
            GameObject farWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            farWall.transform.position = origin + Vector3.forward * 10f;
            var root = new GameObject("Test bullet");
            BallisticProjectile projectile = root.AddComponent<BallisticProjectile>();

            try
            {
                Physics.SyncTransforms();
                var data = new BallisticAmmoData();
                projectile.Initialize(new WeaponBallisticsSettings(), data, origin, Vector3.forward, 1488f, 1000f, owner.transform, null);
                var step = typeof(BallisticProjectile).GetMethod("SimulateStep", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That((bool)step.Invoke(projectile, new object[] { 0.033f }), Is.False);
                Assert.That(projectile.DistanceTravelledMeters, Is.EqualTo(4.995f).Within(0.01f));
                Assert.That(root.transform.position.z, Is.EqualTo(nearWall.transform.position.z - 0.005f).Within(0.01f));
                yield return null;
                Assert.That(projectile == null, Is.True);
            }
            finally
            {
                Object.Destroy(owner);
                Object.Destroy(nearWall);
                Object.Destroy(farWall);
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator TracerIgnitesAtItsDistanceBurnsOutAndRemovesTheTail()
        {
            BallisticAmmoData data = JsonUtility.FromJson<BallisticAmmoData>("{\"_ammoTracerEnabled\":true,\"_ammoTracerWidthMeters\":0.225,\"_ammoTracerTrailDurationSeconds\":0.02,\"_ammoTracerIgnitionDistanceMeters\":20,\"_ammoTracerBurnDurationSeconds\":0.1}");
            BallisticTracerView tracer = BallisticTracerView.Create(data, Vector3.zero);
            Assert.That(tracer, Is.Not.Null);
            LineRenderer line = tracer.GetComponent<LineRenderer>();
            tracer.MoveTo(Vector3.forward * 10f, 10f, 0.01f);
            Assert.That(line.positionCount, Is.Zero);
            tracer.MoveTo(Vector3.forward * 20f, 20f, 0.02f);
            tracer.MoveTo(Vector3.forward * 21f, 21f, 0.021f);
            Assert.That(line.positionCount, Is.EqualTo(2));
            Assert.That(line.GetPosition(0).z, Is.EqualTo(20f));
            Assert.That(line.GetPosition(1).z, Is.EqualTo(21f));
            tracer.MoveTo(Vector3.forward * 100f, 100f, 0.2f);
            Assert.That(line.positionCount, Is.EqualTo(2));
            Assert.That(tracer.GetComponents<Collider>(), Is.Empty);
            tracer.Complete();
            yield return new WaitForSeconds(0.06f);
            Assert.That(tracer == null, Is.True);
        }

        [UnityTest]
        public IEnumerator TracerPaletteRendersThroughTheOutdoorsHdrpCamera()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("OutdoorsScene");
            yield return new WaitForSeconds(0.6f);
            InventoryWindowController inventory = Object.FindAnyObjectByType<InventoryWindowController>();
            Transform camera = inventory.Player.CameraController.Camera.transform;
            var tracers = new BallisticTracerView[4];
            Color[] colors = { new Color(0.1f, 1f, 0.05f), new Color(1f, 0.12f, 0.02f), new Color(1f, 0.35f, 0.015f), new Color(1f, 0.65f, 0.03f) };
            Texture2D capture = null;

            try
            {
                for (int i = 0; i < tracers.Length; i++)
                {
                    BallisticAmmoData data = JsonUtility.FromJson<BallisticAmmoData>("{\"_ammoTracerEnabled\":true,\"_ammoTracerWidthMeters\":0.225,\"_ammoTracerTrailDurationSeconds\":1,\"_ammoTracerIgnitionDistanceMeters\":0,\"_ammoTracerBurnDurationSeconds\":3,\"_ammoTracerEmissionIntensity\":12}");
                    Vector3 start = camera.TransformPoint(new Vector3(-2f, 0.9f - i * 0.6f, 10f));
                    tracers[i] = BallisticTracerView.Create(data, start);
                    LineRenderer line = tracers[i].GetComponent<LineRenderer>();
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_BaseColor", new Color(colors[i].r * 12f, colors[i].g * 12f, colors[i].b * 12f, 1f));
                    line.SetPropertyBlock(block);

                    for (int point = 1; point <= 20; point++)
                    {
                        tracers[i].MoveTo(camera.TransformPoint(new Vector3(-2f + point * 0.2f, 0.9f - i * 0.6f, 10f)), point * 0.2f, point * 0.001f);
                    }
                }

                yield return new WaitForEndOfFrame();
                capture = ScreenCapture.CaptureScreenshotAsTexture();
                Assert.That(capture.width, Is.GreaterThan(0));
                string output = System.Environment.GetEnvironmentVariable("INVENTORY_CAPTURE_DIR");
                if (string.IsNullOrEmpty(output))
                {
                    output = System.IO.Path.Combine(@"E:\Projects\stalker_raid_into_darkness_Archive\BallisticTracerCaptures", System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
                }

                Assert.That(System.IO.Path.IsPathRooted(output), Is.True, "an absolute test output directory is required");
                System.IO.Directory.CreateDirectory(output);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(output, "ballistic-tracers.png"), capture.EncodeToPNG());
            }
            finally
            {
                foreach (BallisticTracerView tracer in tracers)
                {
                    if (tracer != null)
                    {
                        Object.Destroy(tracer.gameObject);
                    }
                }

                Object.Destroy(capture);
            }
        }
    }
}
