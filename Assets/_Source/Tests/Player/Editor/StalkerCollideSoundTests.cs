using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerCollideSoundTests
    {
        private const string LIBRARY_PATH = "Assets/_Source/Settings/Player/Audio/StalkerCollideSoundLibrary.asset";

        [Test]
        public void CriterionAndVolumeFollowPhysicsGame()
        {
            float criterion = StalkerCollideSoundLibrary.GetCriterion(new Vector3(0.0f, -3.0f, 0.0f), Vector3.up, 20.0f);
            Assert.That(criterion, Is.EqualTo(3.0f * Mathf.Sqrt(20.0f)).Within(0.0001f));
            Assert.That(StalkerCollideSoundLibrary.GetCriterion(new Vector3(3.0f, 0.0f, 0.0f), Vector3.up, 20.0f), Is.EqualTo(0.0f).Within(0.0001f), "only the normal velocity counts");
            var library = ScriptableObject.CreateInstance<StalkerCollideSoundLibrary>();
            try
            {
                library.SetImportData(new List<StalkerCollideSounds>(), 0.1f, 200.0f, string.Empty);
                float expected = 0.1f + criterion * (200.0f - 0.1f) / (Mathf.Sqrt(10000.0f) * 150.0f - 10.0f);
                Assert.That(library.GetVolume(criterion), Is.EqualTo(expected).Within(0.0001f));
                Assert.That(library.VelocityCriterion, Is.EqualTo(10.0f));
            }
            finally
            {
                Object.DestroyImmediate(library);
            }
        }

        [Test]
        public void ResolverUsesSurfaceComponentThenFallback()
        {
            var tagged = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var plain = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                tagged.AddComponent<StalkerSurface>().Type = StalkerSurfaceType.Metal;
                var resolver = new StalkerSurfaceResolver(null);
                Assert.That(resolver.Resolve(tagged.GetComponent<Collider>(), Vector3.zero, out StalkerSurfaceSource source), Is.EqualTo(StalkerSurfaceType.Metal));
                Assert.That(source, Is.EqualTo(StalkerSurfaceSource.Component));
                Assert.That(resolver.Resolve(plain.GetComponent<Collider>(), Vector3.zero, out source), Is.EqualTo(StalkerSurfaceType.Earth));
                Assert.That(source, Is.EqualTo(StalkerSurfaceSource.Fallback));
            }
            finally
            {
                Object.DestroyImmediate(tagged);
                Object.DestroyImmediate(plain);
            }
        }

        [Test]
        public void ImportedLibraryHasClothesCollideSoundsForEverySurface()
        {
            var library = AssetDatabase.LoadAssetAtPath<StalkerCollideSoundLibrary>(LIBRARY_PATH);
            Assert.That(library, Is.Not.Null, "collide sounds were not set up");
            Assert.That(library.MinVolume, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(library.MaxVolume, Is.EqualTo(200.0f).Within(0.0001f));
            for (int i = 1; i < StalkerSurfaceTypes.Count; i++)
            {
                var surface = (StalkerSurfaceType)i;
                IReadOnlyList<AudioClip> clips = library.Find("OBJECTS\\clothes", surface);
                Assert.That(clips, Is.Not.Null.And.Not.Empty, "objects\\clothes @ " + StalkerSurfaceTypes.GetMaterialName(surface));
                Assert.That(clips, Has.None.Null);
            }
        }
    }
}
