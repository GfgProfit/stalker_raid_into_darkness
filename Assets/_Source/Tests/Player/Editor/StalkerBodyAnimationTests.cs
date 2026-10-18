using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using RaidIntoDarkness.FirstPerson;
using UnityEditor;
using UnityEngine;
using XRay.Importer;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class StalkerBodyAnimationTests
    {
        private const string CATALOG_PATH = "Assets/_Source/FirstPerson/Outfits/OutfitCatalog.asset";
        private const float DT = 1.0f / 60.0f;

        private static readonly string[] SECTIONS = { "actor", "army_outfit", "military_exo_outfit" };

        private static readonly int[] STATES =
        {
            0,
            (int)StalkerMoveFlags.Forward,
            (int)(StalkerMoveFlags.Forward | StalkerMoveFlags.Accel),
            (int)StalkerMoveFlags.Back,
            (int)StalkerMoveFlags.LeftStrafe,
            (int)StalkerMoveFlags.RightStrafe,
            (int)(StalkerMoveFlags.Forward | StalkerMoveFlags.LeftStrafe),
            (int)(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint),
            (int)StalkerMoveFlags.Crouch,
            (int)(StalkerMoveFlags.Crouch | StalkerMoveFlags.Forward),
            (int)(StalkerMoveFlags.Crouch | StalkerMoveFlags.Accel),
            (int)(StalkerMoveFlags.Crouch | StalkerMoveFlags.Accel | StalkerMoveFlags.Forward),
            (int)StalkerMoveFlags.Jump,
            (int)StalkerMoveFlags.Fall,
            (int)StalkerMoveFlags.Landing,
            (int)StalkerMoveFlags.Climb,
            (int)(StalkerMoveFlags.Climb | StalkerMoveFlags.Forward),
            (int)(StalkerMoveFlags.Climb | StalkerMoveFlags.Back)
        };

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in _created)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            _created.Clear();
        }

        [Test]
        public void EveryMovementStateResolvesToAnExistingClip()
        {
            FirstPersonOutfitCatalog catalog = LoadCatalog();
            BodyAnimationLibrary library = catalog.GetOrDefault("actor").BodyAnimations;
            foreach (bool calm in new[] { true, false })
            {
                foreach (bool slowed in new[] { false, true })
                {
                    foreach (int state in STATES)
                    {
                        BodyAnimationBinding binding = library.Resolve(state, calm, slowed);
                        TestContext.WriteLine($"state {state} calm {calm} slowed {slowed} -> {binding?.Motion}");
                        Assert.That(binding, Is.Not.Null);
                        Assert.That(binding.Clip, Is.Not.Null, binding.Motion);
                    }
                }
            }

            Assert.That(library.TurnLeft?.Clip, Is.Not.Null);
            Assert.That(library.TurnRight?.Clip, Is.Not.Null);
            Assert.That(library.Idles.Count, Is.EqualTo(3));
        }

        [Test]
        public void PlayablesDriveBodyBonesOnNormalAndExoBodies()
        {
            FirstPersonOutfitCatalog catalog = LoadCatalog();
            var host = new GameObject("BodyAnimationHost");
            _created.Add(host);
            StalkerBodyAnimationPlayer player = host.AddComponent<StalkerBodyAnimationPlayer>();
            foreach (string section in SECTIONS)
            {
                FirstPersonOutfitEntry entry = catalog.GetOrDefault(section);
                Assert.That(entry.Section, Is.EqualTo(section));
                GameObject body = Object.Instantiate(entry.BodyModel);
                _created.Add(body);
                XRayModel model = body.GetComponent<XRayModel>();
                Quaternion[] rest = Capture(model);
                player.Bind(body, entry.BodyAnimations);
                Assert.That(player.IsBound, Is.True);
                foreach (int state in new[] { 0, (int)StalkerMoveFlags.Forward, (int)(StalkerMoveFlags.Forward | StalkerMoveFlags.Sprint), (int)(StalkerMoveFlags.Crouch | StalkerMoveFlags.Forward), (int)StalkerMoveFlags.Jump, (int)(StalkerMoveFlags.Climb | StalkerMoveFlags.Forward) })
                {
                    BodyAnimationBinding binding = entry.BodyAnimations.Resolve(state, true, false);
                    player.Play(binding);
                    for (int frame = 0; frame < 45; frame++)
                    {
                        player.Tick(DT);
                    }

                    int moved = CountMoved(model, rest, out int invalid);
                    float extent = SkinnedExtent(body);
                    TestContext.WriteLine($"{section} ({entry.BodyModel.name}) {binding.Motion}: bones moved {moved}/{model.Bones.Count}, invalid {invalid}, skinned extent {extent.ToString("0.00", CultureInfo.InvariantCulture)} m, blend [{player.DescribeBlend()}]");
                    Assert.That(invalid, Is.Zero, binding.Motion);
                    Assert.That(moved, Is.GreaterThan(state == 0 ? 0 : model.Bones.Count / 2), binding.Motion);
                    Assert.That(extent, Is.LessThan(3.0f), binding.Motion);
                }
            }

            PlayableGraphCheck(player);
        }

        [Test]
        public void BlendKeepsWeightsNormalizedAndFadesOldMotion()
        {
            FirstPersonOutfitCatalog catalog = LoadCatalog();
            FirstPersonOutfitEntry entry = catalog.GetOrDefault("actor");
            var host = new GameObject("BlendHost");
            _created.Add(host);
            StalkerBodyAnimationPlayer player = host.AddComponent<StalkerBodyAnimationPlayer>();
            GameObject body = Object.Instantiate(entry.BodyModel);
            _created.Add(body);
            player.Bind(body, entry.BodyAnimations);
            BodyAnimationBinding idle = entry.BodyAnimations.Resolve(0, false, false);
            BodyAnimationBinding run = entry.BodyAnimations.Resolve((int)StalkerMoveFlags.Forward, false, false);
            player.Play(idle);
            player.Tick(0.5f);
            player.Play(run);
            player.Tick(DT);
            float idleWeight = player.GetWeight(idle.Clip);
            float runWeight = player.GetWeight(run.Clip);
            TestContext.WriteLine($"after one frame: {player.DescribeBlend()}");
            Assert.That(idleWeight, Is.GreaterThan(0.0f));
            Assert.That(runWeight, Is.GreaterThan(0.0f));
            for (int frame = 0; frame < 120; frame++)
            {
                player.Tick(DT);
            }

            TestContext.WriteLine($"after two seconds: {player.DescribeBlend()}");
            Assert.That(player.GetWeight(idle.Clip), Is.Zero);
            Assert.That(player.GetWeight(run.Clip), Is.EqualTo(1.0f));
            Assert.That(player.ActiveSlots, Is.EqualTo(1));
        }

        private static void PlayableGraphCheck(StalkerBodyAnimationPlayer player)
        {
            var graph = player.Graph;
            Assert.That(graph.IsValid(), Is.True);
            player.Unbind();
            Assert.That(graph.IsValid(), Is.False, "PlayableGraph leaked after Unbind");
        }

        private static FirstPersonOutfitCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FirstPersonOutfitCatalog>(CATALOG_PATH);
            Assert.That(catalog, Is.Not.Null);
            return catalog;
        }

        private static Quaternion[] Capture(XRayModel model)
        {
            var rotations = new Quaternion[model.Bones.Count];
            for (int i = 0; i < rotations.Length; i++)
            {
                rotations[i] = model.Bones[i].localRotation;
            }

            return rotations;
        }

        private static int CountMoved(XRayModel model, Quaternion[] rest, out int invalid)
        {
            int moved = 0;
            invalid = 0;
            for (int i = 0; i < rest.Length; i++)
            {
                Transform bone = model.Bones[i];
                Quaternion rotation = bone.localRotation;
                Vector3 position = bone.localPosition;
                if (!IsFinite(rotation.x + rotation.y + rotation.z + rotation.w) || !IsFinite(position.x + position.y + position.z))
                {
                    invalid++;
                    continue;
                }

                if (Quaternion.Angle(rotation, rest[i]) > 0.01f)
                {
                    moved++;
                }
            }

            return moved;
        }

        private static float SkinnedExtent(GameObject body)
        {
            float extent = 0.0f;
            var mesh = new Mesh();
            foreach (SkinnedMeshRenderer renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.BakeMesh(mesh, true);
                Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 world = matrix.MultiplyPoint3x4(vertex);
                    if (!IsFinite(world.x + world.y + world.z))
                    {
                        Object.DestroyImmediate(mesh);
                        return float.PositiveInfinity;
                    }

                    extent = Mathf.Max(extent, Vector3.Distance(world, body.transform.position));
                }
            }

            Object.DestroyImmediate(mesh);
            return extent;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
