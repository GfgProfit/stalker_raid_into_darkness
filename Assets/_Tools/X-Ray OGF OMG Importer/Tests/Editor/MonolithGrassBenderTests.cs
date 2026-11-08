using System;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class MonolithGrassBenderTests
    {
        private sealed class Rig : IDisposable
        {
            private readonly Scene _scene;

            public GameObject Owner { get; }
            public XRayGrassBenderState State { get; } = new XRayGrassBenderState();

            public Rig()
            {
                _scene = EditorSceneManager.NewPreviewScene();
                Owner = new GameObject(nameof(MonolithGrassBenderTests));
                SceneManager.MoveGameObjectToScene(Owner, _scene);
                Owner.hideFlags = HideFlags.HideAndDontSave;
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Owner);
                EditorSceneManager.ClosePreviewScene(_scene);
            }
        }

        [Test]
        public void ExplosionMatchesNativeFloat32SnapshotsAndRetainsFinalPreviousFrame()
        {
            using (var rig = new Rig())
            {
                Assert.That(rig.State.TryAddExplosion(rig.Owner, new Vector3(2f, 3f, 4f), 1.33f, 3f, 1f, 3f), Is.True);
                int[] frames = { 1, 2, 20, 60, 102, 120, 148, 149 };
                float[] radii = { 0.150000006f, 0.300000012f, 3f, 3f, 3f, 3f, 3f, 3f };
                float[] strengths = { 0.997783303f, 1.002783298f, 0.669844866f, 0.273254693f, 0.106583692f, 0.066401824f, 0.004335150f, 0.002118483f };
                int sample = 0;
                for (int frame = 1; frame <= 149; frame++)
                {
                    Vector4 previous = rig.State.Position(0);
                    float previousStrength = rig.State.Strength(0);
                    rig.State.Advance(1f / 60f);
                    Assert.That(rig.State.Position(0, true), Is.EqualTo(previous));
                    Assert.That(rig.State.Strength(0, true), Is.EqualTo(previousStrength));
                    if (sample < frames.Length && frame == frames[sample])
                    {
                        Assert.That(rig.State.Position(0).w, Is.EqualTo(radii[sample]).Within(0.00001f));
                        Assert.That(rig.State.Strength(0), Is.EqualTo(strengths[sample]).Within(0.00001f));
                        sample++;
                    }
                }
                Assert.That(sample, Is.EqualTo(frames.Length));
                rig.State.Advance(1f / 60f);
                Assert.That(rig.State.ActiveCount, Is.Zero);
                Assert.That(rig.State.Position(0), Is.EqualTo(Vector4.zero));
                Assert.That(rig.State.Strength(0, true), Is.GreaterThan(0f));
                Assert.That(rig.State.HasData, Is.True);
                rig.State.Advance(1f / 60f);
                Assert.That(rig.State.HasData, Is.False);
            }
        }

        [Test]
        public void SourceCycleAllowsOverlappingPulsesAndDropsOnlyWhenAllEightSlotsAreOccupied()
        {
            using (var rig = new Rig())
            {
                Assert.That(rig.State.TryAddExplosion(rig.Owner, Vector3.zero, 1.33f, 3f, 1f, 3f), Is.True);
                for (int frame = 0; frame < 102; frame++)
                {
                    rig.State.Advance(1f / 60f);
                }
                Assert.That(rig.State.ActiveCount, Is.EqualTo(1));
                Assert.That(rig.State.TryAddExplosion(rig.Owner, Vector3.right, 1.33f, 3f, 1f, 3f), Is.True);
                rig.State.Advance(1f / 60f);
                Assert.That(rig.State.ActiveCount, Is.EqualTo(2));
                Assert.That(rig.State.Position(1, true), Is.EqualTo(Vector4.zero));
                Assert.That(rig.State.Strength(0), Is.LessThan(rig.State.Strength(1)));
                for (int index = 2; index < XRayGrassBenderState.SLOT_COUNT; index++)
                {
                    Assert.That(rig.State.TryAddExplosion(rig.Owner, Vector3.right * index, 1.33f, 3f, 1f, 3f), Is.True);
                }
                Assert.That(rig.State.ActiveCount, Is.EqualTo(8));
                Assert.That(rig.State.TryAddExplosion(rig.Owner, Vector3.left, 1.33f, 3f, 1f, 3f), Is.False);
                Assert.That(rig.State.ActiveCount, Is.EqualTo(8));
            }
        }

        [Test]
        public void ZeroDeltaKeepsCurrentAndPreviousSnapshotsIdentical()
        {
            using (var rig = new Rig())
            {
                rig.State.TryAddExplosion(rig.Owner, Vector3.zero, 1.33f, 3f, 1f, 3f);
                rig.State.Advance(0.05f);
                rig.State.Advance(0.05f);
                Vector4 position = rig.State.Position(0);
                float strength = rig.State.Strength(0);
                rig.State.Advance(0f);
                Assert.That(rig.State.Position(0), Is.EqualTo(position));
                Assert.That(rig.State.Position(0, true), Is.EqualTo(position));
                Assert.That(rig.State.Strength(0), Is.EqualTo(strength));
                Assert.That(rig.State.Strength(0, true), Is.EqualTo(strength));
            }
        }

        [Test]
        public void ReleasingOwnerClearsEveryOwnedPulseWithoutRemovingOtherOwners()
        {
            using (var rig = new Rig())
            {
                GameObject other = new GameObject("OtherOwner");
                SceneManager.MoveGameObjectToScene(other, rig.Owner.scene);
                other.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    rig.State.TryAddExplosion(rig.Owner, Vector3.zero, 1.33f, 3f, 1f, 3f);
                    rig.State.TryAddExplosion(rig.Owner, Vector3.right, 1.33f, 3f, 1f, 3f);
                    rig.State.TryAddExplosion(other, Vector3.left, 1.33f, 3f, 1f, 3f);
                    rig.State.Advance(0.1f);
                    rig.State.RemoveOwner(rig.Owner);
                    Assert.That(rig.State.ActiveCount, Is.EqualTo(1));
                    rig.State.Advance(0f);
                    Assert.That(rig.State.Position(0), Is.EqualTo(Vector4.zero));
                    Assert.That(rig.State.Position(1), Is.EqualTo(Vector4.zero));
                    Assert.That(rig.State.Strength(0, true), Is.GreaterThan(0f));
                    Assert.That(rig.State.Strength(2), Is.GreaterThan(0f));
                    UnityEngine.Object.DestroyImmediate(other);
                    rig.State.Advance(0f);
                    Assert.That(rig.State.ActiveCount, Is.Zero);
                    rig.State.Advance(0f);
                    Assert.That(rig.State.HasData, Is.False);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(other);
                }
            }
        }

        [Test]
        public void CullingBoundsContainBothFramesSequentialOverlapAndSourceStrengthOvershoot()
        {
            using (var rig = new Rig())
            {
                rig.State.TryAddExplosion(rig.Owner, Vector3.zero, 1.33f, 3f, 1f, 3f);
                rig.State.TryAddExplosion(rig.Owner, new Vector3(1.3f, 0f, -0.5f), 1.33f, 3f, 1f, 3f);
                rig.State.Advance(1f / 60f);
                rig.State.Advance(1f / 60f);
                Assert.That(rig.State.Strength(0), Is.GreaterThan(1f));
                Vector3 maximum = rig.State.MaximumDisplacement(2f);
                Assert.That(maximum.x, Is.GreaterThan(8f));
                for (int frame = 2; frame < 40; frame++)
                {
                    rig.State.Advance(1f / 60f);
                }
                Bounds original = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(7f, 2f, 7f));
                Bounds expanded = rig.State.ExpandBounds(original, 2f);
                for (int x = -6; x <= 6; x++)
                {
                    for (int z = -6; z <= 6; z++)
                    {
                        for (int y = 0; y <= 8; y++)
                        {
                            float height = y * 0.25f;
                            Vector3 position = new Vector3(x * 0.5f, height, z * 0.5f);
                            Assert.That(expanded.Contains(SourcePosition(rig.State, position, height, false)), Is.True);
                            Assert.That(expanded.Contains(SourcePosition(rig.State, position, height, true)), Is.True);
                        }
                    }
                }
                Bounds far = new Bounds(new Vector3(100f, 1f, 100f), Vector3.one * 2f);
                Assert.That(rig.State.ExpandBounds(far, 2f), Is.EqualTo(far));
            }
        }

        [Test]
        public void NativeRadialDeformationPinsRootsAndStopsOutsideRadiusOrHeight()
        {
            using (var rig = new Rig())
            {
                rig.State.TryAddExplosion(rig.Owner, Vector3.zero, 1.33f, 3f, 1f, 3f);
                for (int frame = 0; frame < 20; frame++)
                {
                    rig.State.Advance(1f / 60f);
                }
                Vector3 root = new Vector3(0.1f, 0f, 0f);
                Assert.That(SourcePosition(rig.State, root, 0f, false), Is.EqualTo(root));
                Vector3 outside = new Vector3(4f, 1f, 0f);
                Assert.That(SourcePosition(rig.State, outside, 1f, false), Is.EqualTo(outside));
                Vector3 above = new Vector3(0.5f, 4f, 0f);
                Assert.That(SourcePosition(rig.State, above, 4f, false), Is.EqualTo(above));
                Vector3 inside = new Vector3(0.5f, 1f, 0f);
                Vector3 result = SourcePosition(rig.State, inside, 1f, false);
                Assert.That(result.x, Is.GreaterThan(inside.x));
                Assert.That(result.y, Is.LessThan(inside.y));
                Assert.That(result.z, Is.EqualTo(inside.z));
            }
        }

        private static Vector3 SourcePosition(XRayGrassBenderState state, Vector3 position, float height, bool previous)
        {
            for (int slot = 0; slot < XRayGrassBenderState.SLOT_COUNT; slot++)
            {
                Vector4 sphere = state.Position(slot, previous);
                float strength = state.Strength(slot, previous);
                if (sphere.w <= 0f || strength <= 0f)
                {
                    continue;
                }
                Vector3 relative = position - new Vector3(sphere.x, sphere.y, sphere.z);
                float heightLimit = (1f - Mathf.Clamp01(Mathf.Abs(relative.y) / sphere.w)) * height;
                float bend = 1f - Mathf.Clamp01(new Vector2(relative.x, relative.z).magnitude / (sphere.w + 0.001f));
                Vector3 direction = relative.normalized * bend;
                position.x += direction.x * 2f * strength * heightLimit;
                position.z += direction.z * 2f * strength * heightLimit;
                position.y -= bend * 0.6f * strength * heightLimit;
            }
            return position;
        }
    }
}
