using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerSurfaceFoleyTests
    {
        public const string LIBRARY_PATH = "Assets/_Source/Settings/Player/Audio/StalkerSurfaceAudioLibrary.asset";

        private readonly List<Object> _objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in _objects)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            _objects.Clear();
        }

        [Test]
        public void DetectorPrefersComponentThenPhysicsMaterialThenTerrainThenFallback()
        {
            var metal = Track(new PhysicsMaterial("TestMetal"));
            var grassLayer = Track(new TerrainLayer());
            var gravelLayer = Track(new TerrainLayer());
            StalkerSurfaceAudioLibrary library = CreateLibrary();
            library.SetSurfaceMappings(new List<StalkerPhysicsMaterialSurface> { new StalkerPhysicsMaterialSurface(metal, StalkerSurfaceType.Metal) }, new List<StalkerTerrainLayerSurface> { new StalkerTerrainLayerSurface(grassLayer, StalkerSurfaceType.Grass), new StalkerTerrainLayerSurface(gravelLayer, StalkerSurfaceType.Gravel) });
            GameObject plain = Box(new Vector3(0.0f, -0.25f, 0.0f));
            GameObject tagged = Box(new Vector3(10.0f, -0.25f, 0.0f));
            tagged.AddComponent<StalkerSurface>().Type = StalkerSurfaceType.Wood;
            GameObject withMaterial = Box(new Vector3(20.0f, -0.25f, 0.0f));
            withMaterial.GetComponent<Collider>().sharedMaterial = metal;
            GameObject both = Box(new Vector3(30.0f, -0.25f, 0.0f));
            both.GetComponent<Collider>().sharedMaterial = metal;
            both.AddComponent<StalkerSurface>().Type = StalkerSurfaceType.Concrete;
            Terrain terrain = CreateTerrain(new Vector3(40.0f, 0.0f, -4.0f), grassLayer, gravelLayer);
            Physics.SyncTransforms();
            StalkerSurfaceDetector detector = CreateDetector(library);

            Assert.That(DetectAt(detector, new Vector3(0.0f, 0.0f, 0.0f)), Is.EqualTo(StalkerSurfaceType.Earth));
            Assert.That(detector.CurrentSource, Is.EqualTo(StalkerSurfaceSource.Fallback));
            Assert.That(DetectAt(detector, new Vector3(10.0f, 0.0f, 0.0f)), Is.EqualTo(StalkerSurfaceType.Wood));
            Assert.That(detector.CurrentSource, Is.EqualTo(StalkerSurfaceSource.Component));
            Assert.That(DetectAt(detector, new Vector3(20.0f, 0.0f, 0.0f)), Is.EqualTo(StalkerSurfaceType.Metal));
            Assert.That(detector.CurrentSource, Is.EqualTo(StalkerSurfaceSource.PhysicsMaterial));
            Assert.That(DetectAt(detector, new Vector3(30.0f, 0.0f, 0.0f)), Is.EqualTo(StalkerSurfaceType.Concrete));
            Assert.That(DetectAt(detector, terrain.transform.position + new Vector3(1.5f, 0.0f, 4.0f)), Is.EqualTo(StalkerSurfaceType.Grass));
            Assert.That(detector.CurrentSource, Is.EqualTo(StalkerSurfaceSource.TerrainLayer));
            Assert.That(DetectAt(detector, terrain.transform.position + new Vector3(6.5f, 0.0f, 4.0f)), Is.EqualTo(StalkerSurfaceType.Gravel));
            DetectAt(detector, new Vector3(0.0f, 50.0f, 0.0f));
            Assert.That(detector.HasGround, Is.False);
        }

        [Test]
        public void FoleyPlaysSurfaceAndModeClipsWithoutImmediateRepeats()
        {
            StalkerSurfaceAudioLibrary library = CreateLibrary();
            GameObject grass = Box(new Vector3(0.0f, -0.25f, 0.0f));
            grass.AddComponent<StalkerSurface>().Type = StalkerSurfaceType.Grass;
            Physics.SyncTransforms();
            StalkerSurfaceDetector detector = CreateDetector(library);
            StalkerPlayerFoley foley = CreateFoley(library, detector);
            detector.transform.position = Vector3.zero;
            string previous = null;
            for (int i = 0; i < 40; i++)
            {
                foley.PlayFootstep(new StalkerFootstep(i % 2 == 0 ? StalkerFootSide.Left : StalkerFootSide.Right, 1.0f, "norm_run_fwd_0", "body", 0.2f, StalkerFootstepSource.XRayLegsTiming, StalkerMovingMode.Walk));
                StringAssert.Contains("grass", foley.LastReason.ToLowerInvariant());
                string walk = FindPlayed(foley.LastReason, "mode Walk [");
                StringAssert.StartsWith("walk_grass_", walk);
                Assert.That(walk, Is.Not.EqualTo(previous), "same walk clip twice in a row");
                previous = walk;
            }

            foley.PlayFootstep(new StalkerFootstep(StalkerFootSide.Left, 1.0f, "cr_walk_fwd_0", "body", 0.2f, StalkerFootstepSource.XRayLegsTiming, StalkerMovingMode.Crouch));
            StringAssert.StartsWith("crouch_grass_", FindPlayed(foley.LastReason, "mode Crouch ["));
            Assert.That(foley.FootstepCount, Is.EqualTo(41));
        }

        [Test]
        public void LandingSoundRespectsEftThresholdAndFallback()
        {
            StalkerSurfaceAudioLibrary library = CreateLibrary();
            Box(new Vector3(0.0f, -0.25f, 0.0f));
            Physics.SyncTransforms();
            StalkerSurfaceDetector detector = CreateDetector(library);
            StalkerPlayerFoley foley = CreateFoley(library, detector);
            foley.PlayLanding(library.LandSoundSpeed - 0.5f);
            Assert.That(foley.LandingCount, Is.EqualTo(0));
            foley.PlayLanding(library.LandSoundSpeed + 3.0f);
            Assert.That(foley.LandingCount, Is.EqualTo(1));
            StringAssert.StartsWith("land_earth_", foley.LastClipName);
            foley.PlayJump();
            Assert.That(foley.JumpCount, Is.EqualTo(1));
            StringAssert.StartsWith("jump_earth_", foley.LastClipName);
        }

        [Test]
        public void ImportedLibraryContainsOriginalGraSounds()
        {
            var library = AssetDatabase.LoadAssetAtPath<StalkerSurfaceAudioLibrary>(LIBRARY_PATH);
            Assert.That(library, Is.Not.Null, "StalkerSurfaceAudioLibrary.asset is missing; run Tools > X-Ray > Build Player Foley And Camera Effects.");
            StalkerSurfaceType[] required = { StalkerSurfaceType.Earth, StalkerSurfaceType.Grass, StalkerSurfaceType.Gravel, StalkerSurfaceType.Concrete, StalkerSurfaceType.Metal, StalkerSurfaceType.Wood, StalkerSurfaceType.Asphalt };
            foreach (StalkerSurfaceType type in required)
            {
                StalkerSurfaceSounds surface = library.GetSurface(type);
                Assert.That(surface, Is.Not.Null, type.ToString());
                TestContext.WriteLine($"{surface.MaterialName}: pair {surface.PairStepClips.Count}, walk {surface.WalkClips.Count}, run {surface.RunClips.Count}, crouch {surface.CrouchClips.Count}, jump {surface.JumpClips.Count}, land {surface.LandClips.Count}");
                Assert.That(surface.WalkClips.Count + surface.PairStepClips.Count, Is.GreaterThan(0), type + " has no footstep clips");
                Assert.That(surface.RunClips.Count, Is.EqualTo(surface.WalkClips.Count), type.ToString());
                Assert.That(surface.LandClips.Count, Is.GreaterThan(0), type + " has no landing clips");
                foreach (AudioClip clip in surface.WalkClips)
                {
                    Assert.That(clip, Is.Not.Null);
                    StringAssert.StartsWith("Assets/_Source/Audio/Player/", AssetDatabase.GetAssetPath(clip));
                }
            }

            Assert.That(library.GearRustleClips.Count, Is.GreaterThan(0));
            Assert.That(library.GetSurface(library.FallbackSurface).WalkClips.Count, Is.GreaterThan(0), "fallback surface has no clips");
        }

        [Test]
        public void MotorRaisesOneLandedPerJump()
        {
            using (var rig = new StalkerMotorHarness())
            {
                int jumps = 0;
                int left = 0;
                int falling = 0;
                int landed = 0;
                float landingSpeed = 0.0f;
                rig.Settle(0.5f);
                rig.Motor.JumpStarted += () => jumps++;
                rig.Motor.LeftGround += () => left++;
                rig.Motor.StartedFalling += () => falling++;
                rig.Motor.Landed += speed =>
                {
                    landed++;
                    landingSpeed = speed;
                };

                for (int i = 0; i < 3; i++)
                {
                    rig.Frame(StalkerMoveFlags.Jump, 1.0f / 60.0f);
                    rig.Run(StalkerMoveFlags.None, 1.5f, 1.0f / 60.0f);
                }

                TestContext.WriteLine($"3 jumps: JumpStarted {jumps}, LeftGround {left}, StartedFalling {falling}, Landed {landed}, last landing speed {landingSpeed.ToString("0.00", CultureInfo.InvariantCulture)} m/s");
                Assert.That(jumps, Is.EqualTo(3));
                Assert.That(left, Is.EqualTo(3));
                Assert.That(falling, Is.EqualTo(3));
                Assert.That(landed, Is.EqualTo(3));
                Assert.That(landingSpeed, Is.GreaterThan(2.0f));

                rig.AddBox("Ledge", new Vector3(0.0f, 2.0f, 10.0f), new Vector3(4.0f, 4.0f, 4.0f), Quaternion.identity);
                rig.Place(new Vector3(0.0f, 4.05f, 10.0f));
                rig.Settle(0.5f);
                landed = 0;
                left = 0;
                rig.Run(StalkerMoveFlags.Forward, 2.5f, 1.0f / 60.0f);
                TestContext.WriteLine($"walk off a 4 m ledge: LeftGround {left}, Landed {landed}, speed {landingSpeed.ToString("0.00", CultureInfo.InvariantCulture)} m/s");
                Assert.That(left, Is.EqualTo(1));
                Assert.That(landed, Is.EqualTo(1));
            }
        }

        private StalkerSurfaceAudioLibrary CreateLibrary()
        {
            var library = Track(ScriptableObject.CreateInstance<StalkerSurfaceAudioLibrary>());
            var surfaces = new List<StalkerSurfaceSounds>();
            for (int i = 0; i < StalkerSurfaceTypes.Count; i++)
            {
                var type = (StalkerSurfaceType)i;
                string key = StalkerSurfaceTypes.GetShortName(type);
                var surface = new StalkerSurfaceSounds(type, StalkerSurfaceTypes.GetMaterialName(type));
                for (int variant = 1; variant <= 3; variant++)
                {
                    surface.WalkClips.Add(Clip($"walk_{key}_{variant}"));
                    surface.RunClips.Add(Clip($"run_{key}_{variant}"));
                    surface.CrouchClips.Add(Clip($"crouch_{key}_{variant}"));
                    surface.JumpClips.Add(Clip($"jump_{key}_{variant}"));
                    surface.LandClips.Add(Clip($"land_{key}_{variant}"));
                }

                surfaces.Add(surface);
            }

            library.SetImportData(surfaces, StalkerSurfaceType.Earth, new List<AudioClip> { Clip("tac_gear_1"), Clip("tac_gear_2") }, new List<AudioClip>(), 1.0f, 1.0f, 5.0f, 5.0f, 1.0f, 1.1f, "test");
            return library;
        }

        private StalkerSurfaceDetector CreateDetector(StalkerSurfaceAudioLibrary library)
        {
            var go = Track(new GameObject("Detector"));
            var detector = go.AddComponent<StalkerSurfaceDetector>();
            detector.Initialize(library, ~0);
            return detector;
        }

        private StalkerPlayerFoley CreateFoley(StalkerSurfaceAudioLibrary library, StalkerSurfaceDetector detector)
        {
            var source = detector.gameObject.AddComponent<AudioSource>();
            var foley = detector.gameObject.AddComponent<StalkerPlayerFoley>();
            var serialized = new SerializedObject(foley);
            serialized.FindProperty("_library").objectReferenceValue = library;
            serialized.FindProperty("_detector").objectReferenceValue = detector;
            serialized.FindProperty("_source").objectReferenceValue = source;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            foley.Initialize();
            return foley;
        }

        private static StalkerSurfaceType DetectAt(StalkerSurfaceDetector detector, Vector3 position)
        {
            detector.transform.position = position;
            return detector.Detect();
        }

        private static string FindPlayed(string reason, string marker)
        {
            int start = reason.IndexOf(marker, System.StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), reason);
            start += marker.Length;
            int end = reason.IndexOf(']', start);
            return reason.Substring(start, end - start);
        }

        private GameObject Box(Vector3 position)
        {
            GameObject box = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            box.transform.position = position;
            box.transform.localScale = new Vector3(8.0f, 0.5f, 8.0f);
            return box;
        }

        private Terrain CreateTerrain(Vector3 position, TerrainLayer left, TerrainLayer right)
        {
            var data = Track(new TerrainData());
            data.heightmapResolution = 33;
            data.alphamapResolution = 16;
            data.size = new Vector3(8.0f, 1.0f, 8.0f);
            data.terrainLayers = new[] { left, right };
            var alphas = new float[16, 16, 2];
            for (int z = 0; z < 16; z++)
            {
                for (int x = 0; x < 16; x++)
                {
                    alphas[z, x, 0] = x < 8 ? 1.0f : 0.0f;
                    alphas[z, x, 1] = x < 8 ? 0.0f : 1.0f;
                }
            }

            data.SetAlphamaps(0, 0, alphas);
            GameObject go = Track(Terrain.CreateTerrainGameObject(data));
            go.transform.position = position;
            return go.GetComponent<Terrain>();
        }

        private AudioClip Clip(string name)
        {
            return Track(AudioClip.Create(name, 441, 1, 44100, false));
        }

        private T Track<T>(T item) where T : Object
        {
            _objects.Add(item);
            return item;
        }
    }
}
