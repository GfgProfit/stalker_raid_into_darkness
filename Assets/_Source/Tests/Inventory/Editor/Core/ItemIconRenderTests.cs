using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using RaidIntoDarkness.Inventory.IconGeneration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Inventory.Core.Tests
{
    public sealed class ItemIconRenderTests
    {
        private static bool _pipelineWarmedUp;

        private InventoryTestItems _items;
        private ItemIconGeneratorSettings _settings;
        private ItemDefinition _scope;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _items = new InventoryTestItems();
            _settings = ItemIconGeneratorSettings.LoadDefault();
            _scope = _items.Create("test_scope", Vector2Int.one, 1, 0.1f, "Attachment");

            if (_pipelineWarmedUp)
            {
                yield break;
            }

            bool previousIgnore = LogAssert.ignoreFailingMessages;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                ItemDefinition warmUp = CreateRenderableItem("warm_up_item");
                yield return UniTask.ToCoroutine(async () => await RenderPixels(warmUp, Array.Empty<string>(), timeout.Token));
                _pipelineWarmedUp = true;
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        [TearDown]
        public void TearDown()
        {
            _items.Dispose();
        }

        [UnityTest]
        public IEnumerator RenderHasTransparentBackgroundAndVisibleItem() => UniTask.ToCoroutine(async () =>
        {
            ItemDefinition definition = CreateRenderableItem("alpha_item");

            Color32[] pixels = await RenderPixels(definition, Array.Empty<string>(), CancellationToken.None);
            int width = definition.Width * definition.Icon.PixelsPerCell * _settings.IconRenderScale;

            Assert.That(pixels[0].a, Is.EqualTo(0));
            Assert.That(pixels[pixels.Length - 1].a, Is.EqualTo(0));
            Assert.That(pixels[(pixels.Length / width / 2) * width + width / 2].a, Is.GreaterThan(200));
        });

        [UnityTest]
        public IEnumerator RenderIgnoresSceneLightingAndRestoresScene() => UniTask.ToCoroutine(async () =>
        {
            ItemDefinition definition = CreateRenderableItem("isolated_item");
            Color32[] baseline = await RenderPixels(definition, Array.Empty<string>(), CancellationToken.None);
            var lightObject = new GameObject("SceneSun", typeof(Light), typeof(HDAdditionalLightData));
            _items.Track(lightObject);
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.red;
            light.intensity = 100000f;
            lightObject.transform.rotation = Quaternion.Euler(30f, 200f, 0f);
            var volumeObject = new GameObject("SceneVolume", typeof(Volume));
            _items.Track(volumeObject);
            var volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            VolumeProfile profile = _items.Track(ScriptableObject.CreateInstance<VolumeProfile>());
            ColorAdjustments adjustments = profile.Add<ColorAdjustments>(true);
            adjustments.colorFilter.value = Color.green;
            adjustments.postExposure.value = 5f;
            volume.sharedProfile = profile;

            Color32[] withSceneLighting = await RenderPixels(definition, Array.Empty<string>(), CancellationToken.None);

            Assert.That(MaxDifference(baseline, withSceneLighting), Is.LessThanOrEqualTo(3));
            Assert.That(light.enabled, Is.True);
            Assert.That(volume.enabled, Is.True);
        });

        [UnityTest]
        public IEnumerator RenderIgnoresModelFromAnotherOpenSession() => UniTask.ToCoroutine(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            CancellationToken token = timeout.Token;
            ItemDefinition first = CreateRenderableItem("first_open_session_item");
            ItemDefinition second = CreateRenderableItem("second_open_session_item");
            GameObject extraMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            extraMesh.transform.SetParent(first.VisualPrefab.transform, false);
            extraMesh.transform.localScale = new Vector3(1f, 3f, 3f);
            var material = _items.Track(new Material(Shader.Find("HDRP/Lit")));
            material.SetColor("_BaseColor", Color.red);
            extraMesh.GetComponent<Renderer>().sharedMaterial = material;

            using var secondSession = new ItemIconBatchRenderSession(_settings);
            Color32[] baseline = await RenderPixels(secondSession, second, Array.Empty<string>(), token);
            using var firstSession = new ItemIconBatchRenderSession(_settings);
            Color32[] firstPixels = await RenderPixels(firstSession, first, Array.Empty<string>(), token);
            AssertModelInactive(first);
            Color32[] withFirstSessionOpen = await RenderPixels(secondSession, second, Array.Empty<string>(), token);
            Color32[] fromNewSession = await RenderPixels(second, Array.Empty<string>(), token);

            Assert.That(MaxDifference(baseline, firstPixels), Is.GreaterThan(50));
            Assert.That(MaxDifference(baseline, withFirstSessionOpen), Is.LessThanOrEqualTo(3), () => DescribeIsolationDifference("reused session", baseline, firstPixels, withFirstSessionOpen));
            Assert.That(MaxDifference(baseline, fromNewSession), Is.LessThanOrEqualTo(3), () => DescribeIsolationDifference("new session", baseline, firstPixels, fromNewSession));

            EditorApplication.QueuePlayerLoopUpdate();
            await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, token);
            Color32[] afterEditorUpdate = await RenderPixels(secondSession, second, Array.Empty<string>(), token);
            Assert.That(MaxDifference(baseline, afterEditorUpdate), Is.LessThanOrEqualTo(3), () => DescribeIsolationDifference("reused session after queued editor update", baseline, firstPixels, afterEditorUpdate));

            Color32[] firstRenderedAgain = await RenderPixels(firstSession, first, Array.Empty<string>(), token);
            Assert.That(MaxDifference(firstPixels, firstRenderedAgain), Is.LessThanOrEqualTo(3), "the other open session can still render its model");
            AssertModelInactive(first);
            AssertModelInactive(second);
        });

        [UnityTest]
        public IEnumerator RenderIgnoresUnrelatedGeometryOnIconLayerAndRestoresVisibility() => UniTask.ToCoroutine(async () =>
        {
            ItemDefinition definition = CreateRenderableItem("layer_isolated_item");
            Color32[] baseline = await RenderPixels(definition, Array.Empty<string>(), CancellationToken.None);
            GameObject unrelated = _items.Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            unrelated.layer = _settings.RenderLayer;
            unrelated.transform.position = _settings.RenderOrigin;
            unrelated.transform.localScale = Vector3.one * 1.5f;
            var renderer = unrelated.GetComponent<Renderer>();
            var material = _items.Track(new Material(Shader.Find("HDRP/Lit")));
            material.SetColor("_BaseColor", Color.red);
            renderer.sharedMaterial = material;

            Color32[] withUnrelatedModel = await RenderPixels(definition, Array.Empty<string>(), CancellationToken.None);

            Assert.That(MaxDifference(baseline, withUnrelatedModel), Is.LessThanOrEqualTo(3));
            Assert.That(renderer.enabled, Is.True);
            Assert.That(renderer.forceRenderingOff, Is.False);
            Assert.That(unrelated.activeSelf, Is.True);
            Assert.That(unrelated.layer, Is.EqualTo(_settings.RenderLayer));
        });

        [UnityTest]
        public IEnumerator VariantChangesRenderedIcon() => UniTask.ToCoroutine(async () =>
        {
            ItemDefinition definition = CreateRenderableItem("variant_item", true);

            Color32[] plain = await RenderPixels(definition, Array.Empty<string>(), CancellationToken.None);
            Color32[] withScope = await RenderPixels(definition, new[] { _scope.DefinitionId }, CancellationToken.None);

            Assert.That(MaxDifference(plain, withScope), Is.GreaterThan(50));
        });

        [UnityTest]
        public IEnumerator SessionCleansUpCamerasLightsAndVolumes() => UniTask.ToCoroutine(async () =>
        {
            ItemDefinition definition = CreateRenderableItem("cleanup_item");
            int cameras = Count<Camera>();
            int lights = Count<Light>();
            Volume[] existingVolumes = Resources.FindObjectsOfTypeAll<Volume>();

            for (int i = 0; i < 2; i++)
            {
                VolumeProfile profile;
                VolumeComponent[] overrides;
                GameObject root;

                using (var session = new ItemIconBatchRenderSession(_settings))
                {
                    Volume volume = Resources.FindObjectsOfTypeAll<Volume>().Except(existingVolumes).Single();
                    profile = volume.sharedProfile;
                    overrides = profile.components.ToArray();
                    Assert.That(overrides, Is.Not.Empty);

                    await RenderPixels(session, definition, Array.Empty<string>(), CancellationToken.None);
                    root = Resources.FindObjectsOfTypeAll<GameObject>().Single(candidate => candidate.name == $"Runtime Icon Root - {definition.name}");
                    Assert.That(root.activeSelf, Is.False);
                }

                Assert.That(root == null, Is.True, "the model is released");
                Assert.That(profile == null, Is.True, "the profile is released");
                Assert.That(overrides.All(component => component == null), Is.True, "the profile's runtime overrides are released");
                Assert.That(Count<Camera>(), Is.EqualTo(cameras));
                Assert.That(Count<Light>(), Is.EqualTo(lights));
                Assert.That(Count<Volume>(), Is.EqualTo(existingVolumes.Length));
            }
        });

        [UnityTest]
        public IEnumerator CancelledRenderThrowsAndLeavesNoCamera() => UniTask.ToCoroutine(async () =>
        {
            ItemDefinition definition = CreateRenderableItem("cancel_item");
            int cameras = Count<Camera>();
            using var source = new CancellationTokenSource();
            source.Cancel();
            bool cancelled = false;

            try
            {
                await RenderPixels(definition, Array.Empty<string>(), source.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.That(cancelled, Is.True);
            Assert.That(Count<Camera>(), Is.EqualTo(cameras));
        });

        private async UniTask<Color32[]> RenderPixels(ItemDefinition definition, string[] variantIds, CancellationToken cancellationToken)
        {
            using var session = new ItemIconBatchRenderSession(_settings);
            return await RenderPixels(session, definition, variantIds, cancellationToken);
        }

        private static async UniTask<Color32[]> RenderPixels(ItemIconBatchRenderSession session, ItemDefinition definition, string[] variantIds, CancellationToken cancellationToken)
        {
            Texture2D texture = await session.RenderAsync(definition, variantIds, definition.Width, definition.Height, ItemIconProfileType.Default, cancellationToken);
            Assert.That(texture, Is.Not.Null);
            try
            {
                return texture.GetPixels32();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static string DescribeIsolationDifference(string pass, Color32[] baseline, Color32[] foreign, Color32[] actual)
        {
            int introduced = 0;
            int missing = 0;
            int introducedRed = 0;
            int introducedInForeignFootprint = 0;
            for (int i = 0; i < baseline.Length; i++)
            {
                if (baseline[i].a < 8 && actual[i].a > 200)
                {
                    introduced++;
                    if (actual[i].r > actual[i].g + 32 && actual[i].r > actual[i].b + 32)
                    {
                        introducedRed++;
                    }

                    if (foreign[i].a > 200)
                    {
                        introducedInForeignFootprint++;
                    }
                }
                else if (baseline[i].a > 200 && actual[i].a < 8)
                {
                    missing++;
                }
            }

            return $"{pass}: {introduced} new opaque pixels ({introducedRed} red), {missing} missing opaque pixels, {introducedInForeignFootprint} new pixels in the other icon's footprint. Graphics: {SystemInfo.graphicsDeviceType}.";
        }

        private static void AssertModelInactive(ItemDefinition definition)
        {
            string rootName = $"Runtime Icon Root - {definition.name}";
            GameObject[] roots = Resources.FindObjectsOfTypeAll<GameObject>().Where(candidate => candidate.name == rootName).ToArray();
            Assert.That(roots, Is.Not.Empty, "the open session still owns its model");
            Assert.That(roots.All(root => root.activeSelf == false), Is.True, "open session models stay inactive between render requests");
        }

        private ItemDefinition CreateRenderableItem(string id, bool withScopeVariant = false)
        {
            ItemDefinition definition = _items.Create(id, new Vector2Int(2, 1), 1, 1f, "Weapon");
            Material material = _items.Track(new Material(Shader.Find("HDRP/Lit")));
            material.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.8f, 1f));
            GameObject visual = _items.Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            visual.name = id + "_visual";
            visual.transform.position = new Vector3(0f, -1000f, 0f);
            visual.transform.localScale = new Vector3(2f, 0.5f, 0.5f);
            visual.GetComponent<Renderer>().sharedMaterial = material;

            if (withScopeVariant)
            {
                GameObject scope = GameObject.CreatePrimitive(PrimitiveType.Cube);
                scope.name = "Scope";
                scope.transform.SetParent(visual.transform, false);
                scope.transform.localPosition = new Vector3(0f, 2f, 0f);
                scope.transform.localScale = new Vector3(0.4f, 3f, 1f);
                Material scopeMaterial = _items.Track(new Material(Shader.Find("HDRP/Lit")));
                scopeMaterial.SetColor("_BaseColor", new Color(0.9f, 0.1f, 0.1f, 1f));
                scope.GetComponent<Renderer>().sharedMaterial = scopeMaterial;
                visual.AddComponent<ItemIconVariantPart>().SetData(_scope, "Optic", new[] { scope }, null);
            }

            definition.SetVisual(visual, new ItemIconSettings());
            return definition;
        }

        private static int Count<T>() where T : Object => Resources.FindObjectsOfTypeAll<T>().Length;

        private static int MaxDifference(Color32[] left, Color32[] right)
        {
            Assert.That(left.Length, Is.EqualTo(right.Length));
            int max = 0;

            for (int i = 0; i < left.Length; i++)
            {
                max = Mathf.Max(max, Mathf.Abs(left[i].r - right[i].r), Mathf.Abs(left[i].g - right[i].g), Mathf.Abs(left[i].b - right[i].b), Mathf.Abs(left[i].a - right[i].a));
            }

            return max;
        }
    }
}
