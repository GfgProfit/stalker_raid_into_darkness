using Inventory.IconGeneration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Inventory.Tests
{
    public sealed class ItemIconProcessingTests
    {
        private const int SIZE = 7;

        private InventoryTestItems _items;

        [SetUp]
        public void SetUp()
        {
            _items = new InventoryTestItems();
        }

        [TearDown]
        public void TearDown()
        {
            _items.Dispose();
        }

        [Test]
        public void ProcessWithoutEffectsKeepsSourceAndTransparency()
        {
            Color32[] source = CreateSource();
            Color32[] result = ItemIconPostProcessor.Process(source, SIZE, SIZE, new ItemIconPostProcessSettings(false, Color.black, Vector2Int.zero, 0, false, Color.black, 0));

            Assert.That(result[Index(3, 3)], Is.EqualTo(new Color32(200, 100, 50, 255)));
            Assert.That(result[Index(0, 0)].a, Is.EqualTo(0));
            Assert.That(result[Index(4, 3)].a, Is.EqualTo(0));
        }

        [Test]
        public void OutlineSurroundsVisiblePixels()
        {
            Color32[] result = ItemIconPostProcessor.Process(CreateSource(), SIZE, SIZE, new ItemIconPostProcessSettings(false, Color.black, Vector2Int.zero, 0, true, new Color(0f, 0f, 0f, 1f), 1));

            Assert.That(result[Index(4, 3)], Is.EqualTo(new Color32(0, 0, 0, 255)));
            Assert.That(result[Index(3, 2)], Is.EqualTo(new Color32(0, 0, 0, 255)));
            Assert.That(result[Index(4, 4)].a, Is.EqualTo(0));
            Assert.That(result[Index(3, 3)], Is.EqualTo(new Color32(200, 100, 50, 255)));
        }

        [Test]
        public void ShadowIsOffsetBehindItem()
        {
            Color32[] result = ItemIconPostProcessor.Process(CreateSource(), SIZE, SIZE, new ItemIconPostProcessSettings(true, new Color(0f, 0f, 0f, 0.5f), new Vector2Int(2, -1), 0, false, Color.black, 0));

            Assert.That(result[Index(5, 2)].a, Is.EqualTo(128).Within(1));
            Assert.That(result[Index(1, 4)].a, Is.EqualTo(0));
            Assert.That(result[Index(3, 3)], Is.EqualTo(new Color32(200, 100, 50, 255)));
        }

        [Test]
        public void PostProcessSettingsScaleWithRenderScale()
        {
            ItemDefinition definition = _items.Create("scaled", Vector2Int.one);
            ItemIconGeneratorSettings settings = _items.Track(ScriptableObject.CreateInstance<ItemIconGeneratorSettings>());
            IconRenderProfile profile = IconRenderProfile.CreateDefault(definition, settings);

            ItemIconPostProcessSettings processSettings = ItemIconTextureProcessor.CreatePostProcessSettings(definition, profile, settings);

            Assert.That(processSettings.OutlineWidth, Is.EqualTo(definition.Icon.OutlineWidth * profile.RenderScale));
            Assert.That(processSettings.ShadowOffset, Is.EqualTo(Vector2Int.RoundToInt(definition.Icon.ShadowOffset * profile.RenderScale)));
            Assert.That(profile.TextureWidth, Is.EqualTo(definition.Icon.PixelsPerCell * profile.RenderScale));
        }

        [Test]
        public void VariantKeysAreCanonical()
        {
            Assert.That(ItemIconVariantKeyCache.GetCanonicalKey(new[] { "b", "a", "a", "" }), Is.EqualTo("a|b"));
            Assert.That(ItemIconVariantKeyCache.GetCanonicalKey(new[] { "a", "b" }), Is.EqualTo(ItemIconVariantKeyCache.GetCanonicalKey(new[] { "b", "a" })));
            Assert.That(ItemIconVariantKeyCache.GetCanonicalKey(System.Array.Empty<string>()), Is.Empty);
        }

        [Test]
        public void CacheKeysSeparateVariantsSizesProfilesAndVisualSettings()
        {
            ItemDefinition definition = _items.Create("rifle", new Vector2Int(4, 2), 1, 1f, "Weapon");
            ItemIconGeneratorSettings settings = _items.Track(ScriptableObject.CreateInstance<ItemIconGeneratorSettings>());
            IconRenderProfile defaultProfile = IconRenderProfile.CreateDefault(definition, settings);
            IconCacheKey baseKey = ItemIconStableKeyBuilder.Build(definition, new[] { "scope" }, settings, defaultProfile);

            Assert.That(ItemIconStableKeyBuilder.Build(definition, new[] { "scope" }, settings, defaultProfile), Is.EqualTo(baseKey));
            Assert.That(ItemIconStableKeyBuilder.Build(definition, System.Array.Empty<string>(), settings, defaultProfile), Is.Not.EqualTo(baseKey));
            Assert.That(ItemIconStableKeyBuilder.Build(definition, new[] { "scope" }, settings, IconRenderProfile.CreateDefault(definition, 2, 4, settings)), Is.Not.EqualTo(baseKey));
            Assert.That(ItemIconStableKeyBuilder.Build(definition, new[] { "scope" }, settings, IconRenderProfile.CreateSlot(definition, 4, 2, settings)), Is.Not.EqualTo(baseKey));

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_icon._defaultView._lightIntensity").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            IconRenderProfile changedProfile = IconRenderProfile.CreateDefault(definition, settings);

            Assert.That(ItemIconStableKeyBuilder.Build(definition, new[] { "scope" }, settings, changedProfile).VisualSignature, Is.Not.EqualTo(baseKey.VisualSignature));
        }

        [Test]
        public void StableHashIsDeterministic()
        {
            ulong first = ItemIconStableHash.Add(ItemIconStableHash.Add(ItemIconStableHash.Begin(), "item"), 42);
            ulong second = ItemIconStableHash.Add(ItemIconStableHash.Add(ItemIconStableHash.Begin(), "item"), 42);
            ulong other = ItemIconStableHash.Add(ItemIconStableHash.Add(ItemIconStableHash.Begin(), "item"), 43);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(other));
            Assert.That(ItemIconStableHash.TryParseHex(ItemIconStableHash.ToHex(first), out ulong parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(first));
        }

        [Test]
        public void VariantEnumerationIncludesEmptyAndInstalledParts()
        {
            ItemDefinition scope = _items.Create("scope", Vector2Int.one, 1, 0.2f, "Attachment");
            ItemDefinition rifle = _items.Create("rifle", new Vector2Int(4, 2), 1, 1f, "Weapon");
            GameObject visual = _items.Track(new GameObject("RifleVisual"));
            var scopeObject = new GameObject("Scope");
            scopeObject.transform.SetParent(visual.transform, false);
            visual.AddComponent<ItemIconVariantPart>().SetData(scope, "Optic", new[] { scopeObject }, null);
            rifle.SetVisual(visual, new ItemIconSettings());

            var variants = ItemIconVariantEnumeration.EnumerateVariants(rifle);

            Assert.That(variants.Count, Is.EqualTo(2));
            Assert.That(variants[0], Is.Empty);
            Assert.That(variants[1], Is.EqualTo(new[] { "scope" }));
        }

        private static Color32[] CreateSource()
        {
            var pixels = new Color32[SIZE * SIZE];
            pixels[Index(3, 3)] = new Color32(200, 100, 50, 255);
            return pixels;
        }

        private static int Index(int x, int y) => y * SIZE + x;
    }
}
