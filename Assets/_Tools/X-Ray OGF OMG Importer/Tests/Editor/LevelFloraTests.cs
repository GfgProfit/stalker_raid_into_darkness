using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Sources.Textures;
using Object = UnityEngine.Object;

namespace XRay.Importer.Tests
{
    public sealed class LevelFloraTests
    {
        [Test]
        public void LeafShadersAreFloraAndTrunksAreNot()
        {
            XRayLevelSurface leaves = XRayLevelSurfaceRules.Classify(new XRayLevelShader(1, "flora\\leaf_wave/trees\\trees_vetkasux1_v"), null, true);
            XRayLevelSurface trunk = XRayLevelSurfaceRules.Classify(new XRayLevelShader(2, "flora\\trunk_wave/trees\\trees_bark_a_01"), null, true);
            Assert.AreEqual("tree_leaves", leaves.RuleName);
            Assert.IsTrue(leaves.Flora);
            Assert.AreEqual("tree_trunk", trunk.RuleName);
            Assert.IsFalse(trunk.Flora);
        }

        [Test]
        public void FloraNormalFollowsUberDefferVariantSelection()
        {
            Assert.IsFalse(XRayLevelMaterialLibrary.UsesFloraNormal(null));
            Assert.IsTrue(XRayLevelMaterialLibrary.UsesFloraNormal(Description("trees\\leaf_bump", XRayBumpMode.Use, null, 0u)));
            Assert.IsFalse(XRayLevelMaterialLibrary.UsesFloraNormal(Description("trees\\leaf_bump", XRayBumpMode.UseParallax, null, 0u)));
            Assert.IsFalse(XRayLevelMaterialLibrary.UsesFloraNormal(Description(null, XRayBumpMode.Use, null, 0u)));
            Assert.IsFalse(XRayLevelMaterialLibrary.UsesFloraNormal(Description("trees\\leaf_bump", XRayBumpMode.Use, "detail\\detail_grnd", XRayTextureDescription.FLAG_DIFFUSE_DETAIL)));
        }

        [Test]
        public void LeafMaterialUsesTrueStalkerFloraBranch()
        {
            string bumpPath = "Assets/XRayLevelFloraBump_" + Guid.NewGuid().ToString("N") + ".asset";
            var bump = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            var leaves = new Material(Shader.Find("HDRP/MonolithLit"));
            var trunk = new Material(Shader.Find("HDRP/MonolithLit"));
            try
            {
                AssetDatabase.CreateAsset(bump, bumpPath);
                var library = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Update);
                XRayTextureDescription description = Description("trees\\leaf_bump", XRayBumpMode.Use, null, 0u);
                Set(description, "HasMaterial", true);
                Set(description, "MaterialId", 0u);
                Set(description, "MaterialWeight", 0.5);
                var resolution = new XRayTextureResolution();
                Set(resolution, "Description", description);
                var textures = new XRayLevelTextureSet { BumpPath = bumpPath, Resolution = resolution };
                MethodInfo configure = typeof(XRayLevelMaterialLibrary).GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Instance);
                configure.Invoke(library, new object[] { leaves, new XRayLevelMaterialRequest { ShaderName = "flora\\leaf_wave", Surface = XRayLevelSurfaceRules.Classify(new XRayLevelShader(1, "flora\\leaf_wave/trees\\leaf"), null, true), Textures = textures } });
                configure.Invoke(library, new object[] { trunk, new XRayLevelMaterialRequest { ShaderName = "flora\\trunk_wave", Surface = XRayLevelSurfaceRules.Classify(new XRayLevelShader(2, "flora\\trunk_wave/trees\\bark"), null, true), Textures = textures } });
                Assert.IsTrue(leaves.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA"));
                Assert.IsTrue(leaves.IsKeywordEnabled("_MONOLITH_LEVEL"));
                Assert.IsTrue(leaves.IsKeywordEnabled("_ALPHATEST_ON"));
                Assert.AreEqual(0.0f, leaves.GetFloat("_MonolithMaterialId"));
                Assert.AreEqual(XRayLevelMaterialLibrary.FLORA_MATERIAL_WEIGHT, leaves.GetFloat("_MonolithMaterialWeight"), 1e-6f);
                Assert.IsFalse(trunk.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA"));
                Assert.AreEqual(0.5f, trunk.GetFloat("_MonolithMaterialWeight"), 1e-6f);
                Assert.AreEqual(1, leaves.GetFloat("_MonolithFloraSubsurface"));
                Assert.AreEqual(0, trunk.GetFloat("_MonolithFloraSubsurface"));
            }
            finally
            {
                Object.DestroyImmediate(leaves);
                Object.DestroyImmediate(trunk);
                AssetDatabase.DeleteAsset(bumpPath);
            }
        }

        [Test]
        public void WoodMaterialKeepsThmIdWithoutLeafSubsurface()
        {
            var material = new Material(Shader.Find("HDRP/MonolithLit"));
            try
            {
                XRayTextureDescription description = Description(null, XRayBumpMode.Use, null, 0u);
                Set(description, "HasMaterial", true);
                Set(description, "MaterialId", 0u);
                Set(description, "MaterialWeight", 0.05);
                var resolution = new XRayTextureResolution();
                Set(resolution, "Description", description);
                var textures = new XRayLevelTextureSet { Resolution = resolution };
                var library = new XRayLevelMaterialLibrary(new XRayLevelImportRecord(), XRayLevelMaterialUpdateMode.Update);
                XRayLevelSurface surface = XRayLevelSurfaceRules.Classify(new XRayLevelShader(1, "default/wood\\wood_old"), null, true);
                MethodInfo configure = typeof(XRayLevelMaterialLibrary).GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Instance);
                configure.Invoke(library, new object[] { material, new XRayLevelMaterialRequest { ShaderName = "default", Surface = surface, Textures = textures } });
                Assert.IsFalse(surface.Flora);
                Assert.AreEqual(0, material.GetFloat("_MonolithMaterialId"));
                Assert.AreEqual(0.05f, material.GetFloat("_MonolithMaterialWeight"), 1e-6f);
                Assert.AreEqual(0, material.GetFloat("_MonolithFloraSubsurface"));
                Assert.AreEqual(0, material.GetFloat("_MonolithSourceProfile"));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static XRayTextureDescription Description(string bump, XRayBumpMode mode, string detail, uint flags)
        {
            var description = new XRayTextureDescription();
            Set(description, "BumpName", bump);
            Set(description, "BumpMode", mode);
            Set(description, "DetailName", detail);
            Set(description, "Flags", flags);
            return description;
        }

        private static void Set(object target, string property, object value)
        {
            target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
        }
    }
}
