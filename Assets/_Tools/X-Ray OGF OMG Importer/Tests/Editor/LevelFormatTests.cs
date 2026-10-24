using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using XRay.Importer.Core.Level;
using XRay.Importer.Sources.Levels;

namespace XRay.Importer.Tests
{
    public sealed class LevelFormatTests
    {
        private const string GAMEDATA = "E:\\True Stalker Unpacked Patch 1.5";

        [Test]
        public void RandomMatchesMsvcRand()
        {
            var random = new XRayRandom(1);
            int[] expected = { 41, 18467, 6334, 26500, 19169, 15724, 11478, 29358 };
            foreach (int value in expected)
            {
                Assert.AreEqual(value, random.RandI());
            }
        }

        [Test]
        public void DetailSlotBitfieldsDecode()
        {
            var slot = new XRayDetailSlot(0x1050f438u, 0x902c6u, 0, 0, 0, 0);
            Assert.AreEqual(16.0f, slot.YBase, 1e-4f);
            Assert.AreEqual(1.5f, slot.YHeight, 1e-4f);
            Assert.AreEqual(5, slot.GetId(0));
            Assert.AreEqual(4, slot.GetId(1));
            Assert.AreEqual(6, slot.GetId(2));
            Assert.AreEqual(11, slot.GetId(3));
            Assert.AreEqual(9, slot.Hemi);
            Assert.AreEqual(0, slot.Sun);
        }

        [Test]
        public void ShaderEntryIsSplit()
        {
            var shader = new XRayLevelShader(3, "def_shaders\\def_aref/mtl\\mtl_walls9,lmap#10_1,lmap#10_2");
            Assert.AreEqual("def_shaders\\def_aref", shader.EngineShader);
            Assert.AreEqual("mtl\\mtl_walls9", shader.Texture);
            CollectionAssert.AreEqual(new[] { "lmap#10_1", "lmap#10_2" }, shader.Lightmaps);
        }

        [Test]
        [Category("RealData")]
        public void TrueStalkerLevelsParseWithConsistentGeometry()
        {
            XRayLevelGamedataInfo info = XRayLevelGamedata.Inspect(GAMEDATA);
            Assume.That(info.IsValid, "True Stalker gamedata is not available: " + string.Join("; ", info.Errors));
            foreach (XRayLevelEntry entry in info.Levels)
            {
                byte[] data = File.ReadAllBytes(entry.LevelPath);
                Assert.IsTrue(XRayLevelReader.IsLevelSignature(data), entry.Name);
                XRayLevelData level = XRayLevelReader.Read(data, entry.LevelPath);
                Assert.Greater(level.Visuals.Count, 0, entry.Name);
                using (XRayLevelGeometry geometry = XRayLevelGeometry.Open(Path.Combine(entry.Folder, "level.geom")))
                {
                    byte[] head = new byte[16];
                    using (FileStream stream = File.OpenRead(Path.Combine(entry.Folder, "level.geom")))
                    {
                        stream.Read(head, 0, head.Length);
                    }

                    Assert.IsFalse(XRayLevelReader.IsLevelSignature(head), entry.Name + " level.geom must not be detected as a level");
                    int agree = 0;
                    int disagree = 0;
                    foreach (XRayLevelVisual visual in level.Visuals)
                    {
                        if (!visual.HasGeometry || visual.ModelType != XRayLevelFormat.MT_NORMAL)
                        {
                            continue;
                        }

                        XRayGeometryRange range = visual.Geometry;
                        XRayLevelVertexBlock block = geometry.ReadVertices(range.VertexBuffer, range.VertexOffset, range.VertexCount);
                        ushort[] indices = geometry.ReadIndices(range.IndexBuffer, range.IndexOffset, range.IndexCount);
                        for (int t = 0; t + 2 < indices.Length; t += 3)
                        {
                            Assert.Less(indices[t], range.VertexCount);
                            double winding = Winding(block, indices[t], indices[t + 1], indices[t + 2]);
                            if (winding > 0.0)
                            {
                                agree++;
                            }
                            else if (winding < 0.0)
                            {
                                disagree++;
                            }
                        }
                    }

                    Assert.Greater(agree, disagree * 100, $"{entry.Name}: face winding must agree with stored normals");
                }
            }
        }

        [Test]
        [Category("RealData")]
        public void DetailPlacementIsDeterministic()
        {
            string folder = Path.Combine(GAMEDATA, "levels", "escape");
            Assume.That(File.Exists(Path.Combine(folder, "level.details")), "escape level.details is not available");
            XRayDetails details = XRayDetails.Read(File.ReadAllBytes(Path.Combine(folder, "level.details")), "level.details");
            XRayCollisionModel collision = XRayCollisionModel.Read(Path.Combine(folder, "level.cform"));
            collision.BuildGrid(XRayDetails.SLOT_SIZE);
            var first = new List<XRayDetailInstance>();
            var second = new List<XRayDetailInstance>();
            var a = new XRayDetailPlacement(details, collision, null, new XRayDetailPlacementSettings());
            var b = new XRayDetailPlacement(details, collision, null, new XRayDetailPlacementSettings());
            for (int z = details.SizeZ / 2; z < details.SizeZ / 2 + 40; z++)
            {
                for (int x = details.SizeX / 2; x < details.SizeX / 2 + 40; x++)
                {
                    a.PlaceSlot(x, z, first);
                    b.PlaceSlot(x, z, second);
                }
            }

            Assert.Greater(first.Count, 0);
            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].X, second[i].X);
                Assert.AreEqual(first[i].Y, second[i].Y);
                Assert.AreEqual(first[i].Yaw, second[i].Yaw);
                Assert.AreEqual(first[i].Model, second[i].Model);
            }
        }

        private static double Winding(XRayLevelVertexBlock block, int a, int b, int c)
        {
            float[] p = block.Positions;
            double e1x = p[b * 3] - p[a * 3];
            double e1y = p[b * 3 + 1] - p[a * 3 + 1];
            double e1z = p[b * 3 + 2] - p[a * 3 + 2];
            double e2x = p[c * 3] - p[a * 3];
            double e2y = p[c * 3 + 1] - p[a * 3 + 1];
            double e2z = p[c * 3 + 2] - p[a * 3 + 2];
            double fx = e1y * e2z - e1z * e2y;
            double fy = e1z * e2x - e1x * e2z;
            double fz = e1x * e2y - e1y * e2x;
            double nx = 0.0;
            double ny = 0.0;
            double nz = 0.0;
            foreach (int index in new[] { a, b, c })
            {
                nx += block.Normals[index * 4 + 2] / 255.0 * 2.0 - 1.0;
                ny += block.Normals[index * 4 + 1] / 255.0 * 2.0 - 1.0;
                nz += block.Normals[index * 4] / 255.0 * 2.0 - 1.0;
            }

            return fx * nx + fy * ny + fz * nz;
        }
    }
}
