using NUnit.Framework;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class LevelShadowInfluenceTests
    {
        [Test]
        public void DirectionalKeepsUpstreamCastersAndIntersectingCrowns()
        {
            var cameraObject = new GameObject("Shadow test camera", typeof(Camera));
            var lightObject = new GameObject("Shadow test sun", typeof(Light));
            try
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.farClipPlane = 50.0f;
                camera.aspect = 1.0f;
                Light light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional;
                light.shadows = LightShadows.Hard;
                var influence = new XRayShadowInfluence(new[] { light }, 0.0f);
                foreach (float yaw in new[] { -4.0f, -2.0f, 0.0f, 2.0f, 4.0f })
                {
                    camera.transform.rotation = Quaternion.Euler(0, yaw, 0);
                    influence.Prepare(camera);
                    Assert.IsTrue(influence.Intersects(new Bounds(new Vector3(0, 0, -1000), Vector3.one * 10)));
                    Assert.IsTrue(influence.Intersects(new Bounds(new Vector3(60, 0, 20), new Vector3(100, 20, 20))));
                    Assert.IsFalse(influence.Intersects(new Bounds(new Vector3(300, 0, 20), Vector3.one)));
                    Assert.IsFalse(influence.Intersects(new Bounds(new Vector3(0, 0, 100), Vector3.one)));
                }
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(lightObject);
            }
        }

        [TestCase(LightType.Point)]
        [TestCase(LightType.Spot)]
        public void LocalSourceUsesWholeRangeIncludingOffscreenCasters(LightType type)
        {
            var cameraObject = new GameObject("Shadow test camera", typeof(Camera));
            var lightObject = new GameObject("Shadow test local", typeof(Light));
            try
            {
                Light light = lightObject.GetComponent<Light>();
                light.type = type;
                light.range = 10.0f;
                light.shadows = LightShadows.Hard;
                var influence = new XRayShadowInfluence(new[] { light }, 0.0f);
                influence.Prepare(cameraObject.GetComponent<Camera>());
                Assert.IsTrue(influence.Intersects(new Bounds(new Vector3(0, 0, -8), Vector3.one)));
                Assert.IsFalse(influence.Intersects(new Bounds(new Vector3(50, 0, 0), Vector3.one)));
                light.transform.position = new Vector3(50, 0, 0);
                influence.Prepare(cameraObject.GetComponent<Camera>());
                Assert.IsTrue(influence.Intersects(new Bounds(new Vector3(50, 0, 0), Vector3.one)));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(lightObject);
            }
        }

        [Test]
        public void MissingSourceRegistryKeepsCorrectSubmissionPath()
        {
            var cameraObject = new GameObject("Shadow test camera", typeof(Camera));
            try
            {
                var influence = new XRayShadowInfluence(null, 0.0f);
                influence.Prepare(cameraObject.GetComponent<Camera>());
                Assert.IsTrue(influence.Intersects(new Bounds(Vector3.one * 10000.0f, Vector3.one)));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
