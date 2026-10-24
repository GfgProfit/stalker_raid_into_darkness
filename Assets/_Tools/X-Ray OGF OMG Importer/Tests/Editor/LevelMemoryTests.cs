using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor.Levels;

namespace XRay.Importer.Tests
{
    public sealed class LevelMemoryTests
    {
        [Test]
        public void LevelMaterialKeepsPackedBumpAndReleasesReservedCorrection()
        {
            string path = "Assets/XRayLevelMemoryProbe_" + Guid.NewGuid().ToString("N") + ".asset";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            byte[] bytes = { 17, 39, 83, 211, 23, 41, 89, 223, 31, 43, 97, 227, 37, 47, 101, 229 };
            try
            {
                texture.LoadRawTextureData(bytes);
                texture.Apply(false, false);
                AssetDatabase.CreateAsset(texture, path);
                var library = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Update);
                var request = new XRayLevelMaterialRequest { Surface = new XRayLevelSurface { Kind = XRayLevelSurfaceKind.Opaque }, Textures = new XRayLevelTextureSet { BumpPath = path, CorrectionPath = path } };
                typeof(XRayLevelMaterialLibrary).GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(library, new object[] { material, request });
                Assert.AreEqual(path, AssetDatabase.GetAssetPath(material.GetTexture("_MonolithBump")));
                Assert.IsNull(material.GetTexture("_MonolithBumpCorrection"));
                CollectionAssert.AreEqual(bytes, ((Texture2D)material.GetTexture("_MonolithBump")).GetRawTextureData());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                AssetDatabase.DeleteAsset(path);
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        [Test]
        [Category("RealData")]
        public void OriginalEscapeLodPayloadPreservesAuthoredCoordinatesAndLighting()
        {
            const string SOURCE = "E:/True Stalker Unpacked Patch 1.5/levels/escape/level";
            Assume.That(File.Exists(SOURCE));
            XRayLevelData level = XRayLevelReader.Read(File.ReadAllBytes(SOURCE), SOURCE);
            Assert.AreEqual(4102, level.Visuals.Count(visual => visual.LodVertices != null));
            XRayLodVertex first = level.Visuals[7793].LodVertices[0];
            Assert.AreEqual(32, level.Visuals[7793].LodVertices.Length);
            Assert.AreEqual(19.1903209686279, first.Position.X, 1e-9);
            Assert.AreEqual(18.7860736846924, first.Position.Y, 1e-9);
            Assert.AreEqual(661.280395507813, first.Position.Z, 1e-9);
            Assert.AreEqual(0.0001220703125f, first.U);
            Assert.AreEqual(0.000244140625f, first.V);
            Assert.AreEqual(1023410176u, first.RgbHemi);
            Assert.AreEqual(0, first.Sun);
        }
    }
}
