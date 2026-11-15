using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XRay.Importer.Editor.Levels;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    public sealed class XRayInstanceDependencyTests
    {
        private const BindingFlags STATIC_PRIVATE = BindingFlags.Static | BindingFlags.NonPublic;

        private string _folder;
        private Mesh _meshA;
        private Mesh _meshB;
        private Material _material;
        private Material _privateMaterial;
        private XRayInstanceBatchSet _first;
        private XRayInstanceBatchSet _second;

        [SetUp]
        public void SetUp()
        {
            Flush();
            _folder = "Assets/_Tools/X-Ray OGF OMG Importer/Tests/Editor/DependencyFixture_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder(Path.GetDirectoryName(_folder).Replace('\\', '/'), Path.GetFileName(_folder));
            File.WriteAllText(_folder + "/Contract.hlsl", "float4 DependencyColor() { return float4(1, 1, 1, 1); }");
            File.WriteAllText(_folder + "/Contract.shader", "Shader \"Hidden/RVM/DependencyFixture\"\n{\nSubShader\n{\nTags { \"RenderType\"=\"Opaque\" }\nPass\n{\nHLSLPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n#include \"Contract.hlsl\"\nfloat4 vert(float4 vertex : POSITION) : SV_POSITION\n{\nreturn vertex;\n}\nfloat4 frag() : SV_Target\n{\nreturn DependencyColor();\n}\nENDHLSL\n}\n}\n}");
            AssetDatabase.ImportAsset(_folder + "/Contract.hlsl", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(_folder + "/Contract.shader", ImportAssetOptions.ForceSynchronousImport);
            _material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(_folder + "/Contract.shader")) { enableInstancing = true };
            AssetDatabase.CreateAsset(_material, _folder + "/Shared.mat");
            _privateMaterial = new Material(_material);
            AssetDatabase.CreateAsset(_privateMaterial, _folder + "/Private.mat");
            _meshA = CreateMesh("MeshA");
            _meshB = CreateMesh("MeshB");
            _first = CreateSet("First", _meshA, _privateMaterial);
            _second = CreateSet("Second", _meshB);
            Flush();
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_folder)) AssetDatabase.DeleteAsset(_folder);
            Flush();
        }

        [Test]
        public void UnrelatedAssetLeavesOutstandingLocationSignaturesUntouched()
        {
            string signature = _first.GroupingSignature;
            _first.Batches[0].Data[0] = Vector4.zero;
            var unrelated = new Mesh();
            AssetDatabase.CreateAsset(unrelated, _folder + "/Unrelated.asset");
            Assert.That(Pending(), Does.Not.Contain(AssetDatabase.GetAssetPath(_first)));
            Flush();
            Assert.That(_first.GroupingSignature, Is.EqualTo(signature));
            Assert.That(XRayInstancePreparation.Ensure(_first), Is.True);
        }

        [Test]
        public void MeshChangesQueueOnlyDependentSetAndSharedMaterialQueuesBothOnce()
        {
            string firstSignature = _first.GroupingSignature;
            string secondSignature = _second.GroupingSignature;
            _meshA.vertices = new[] { Vector3.zero, Vector3.right * 2f, Vector3.up };
            EditorUtility.SetDirty(_meshA);
            AssetDatabase.SaveAssetIfDirty(_meshA);
            Record(AssetDatabase.GetAssetPath(_meshA));
            Assert.That(SetPending(), Is.EquivalentTo(new[] { AssetDatabase.GetAssetPath(_first) }));
            Flush();
            Assert.That(_first.GroupingSignature, Is.Not.EqualTo(firstSignature));
            Assert.That(_second.GroupingSignature, Is.EqualTo(secondSignature));
            _material.enableInstancing = false;
            EditorUtility.SetDirty(_material);
            AssetDatabase.SaveAssetIfDirty(_material);
            Record(AssetDatabase.GetAssetPath(_material));
            Record(AssetDatabase.GetAssetPath(_material));
            Assert.That(SetPending(), Is.EquivalentTo(new[] { AssetDatabase.GetAssetPath(_first), AssetDatabase.GetAssetPath(_second) }));
            Flush();
            Assert.That(_first.SubmissionGroups, Is.Empty);
            Assert.That(_second.SubmissionGroups, Is.Empty);
            Assert.That(SetPending(), Is.Empty);
            Assert.That(XRayInstancePreparation.Ensure(_first), Is.False);
            Assert.That(XRayInstancePreparation.Ensure(_second), Is.False);
            Assert.That(SetPending(), Is.Empty);
        }

        [Test]
        public void PrivateMaterialUpdatesOnlyItsOwner()
        {
            string firstSignature = _first.GroupingSignature;
            string secondSignature = _second.GroupingSignature;
            _privateMaterial.renderQueue = 3000;
            EditorUtility.SetDirty(_privateMaterial);
            AssetDatabase.SaveAssetIfDirty(_privateMaterial);
            Record(AssetDatabase.GetAssetPath(_privateMaterial));
            Assert.That(SetPending(), Is.EquivalentTo(new[] { AssetDatabase.GetAssetPath(_first) }));
            Flush();
            Assert.That(_first.GroupingSignature, Is.Not.EqualTo(firstSignature));
            Assert.That(_second.GroupingSignature, Is.EqualTo(secondSignature));
        }

        [Test]
        public void ShaderAndIncludeDependenciesRefreshAllAffectedSets()
        {
            foreach (string dependency in new[] { _folder + "/Contract.shader", _folder + "/Contract.hlsl" })
            {
                Record(dependency);
                Assert.That(SetPending(), Is.EquivalentTo(new[] { AssetDatabase.GetAssetPath(_first), AssetDatabase.GetAssetPath(_second) }));
                Flush();
            }
            string signature = _first.GroupingSignature;
            File.AppendAllText(_folder + "/Contract.hlsl", "\nfloat DependencyValue() { return 2; }\n");
            AssetDatabase.ImportAsset(_folder + "/Contract.hlsl", ImportAssetOptions.ForceSynchronousImport);
            Flush();
            Assert.That(_first.GroupingSignature, Is.Not.EqualTo(signature));
            Assert.That(_first.GroupingSignature, Is.EqualTo(XRayInstanceGrouping.InputSignature(_first)));
        }

        [Test]
        public void MoveAndDeleteRefreshDependencyEdgesAndInvalidateMissingGeometry()
        {
            string original = AssetDatabase.GetAssetPath(_meshA);
            string moved = _folder + "/Moved.asset";
            Assert.That(AssetDatabase.MoveAsset(original, moved), Is.Empty);
            Flush();
            Record(original);
            Assert.That(SetPending(), Is.Empty);
            Flush();
            Record(moved);
            Assert.That(SetPending(), Is.EquivalentTo(new[] { AssetDatabase.GetAssetPath(_first) }));
            Flush();
            Assert.That(AssetDatabase.DeleteAsset(moved), Is.True);
            Flush();
            Assert.That(_first.SubmissionGroups, Is.Empty);
            Assert.That(_first.GroupingSignature, Is.EqualTo(XRayInstanceGrouping.InputSignature(_first)));
            Assert.That(_second.SubmissionGroups.Count, Is.EqualTo(1));
        }

        [Test]
        public void CachedMatrixSignatureMatchesDirectSignatureAndSecondEnsureIsUnchanged()
        {
            string matrix = XRayInstancePreparation.InputSignature(_first);
            Assert.That(XRayInstanceGrouping.InputSignature(_first, matrix), Is.EqualTo(XRayInstanceGrouping.InputSignature(_first)));
            Assert.That(XRayInstancePreparation.Ensure(_first), Is.False);
        }

        private Mesh CreateMesh(string name)
        {
            var mesh = new Mesh { name = name, vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            AssetDatabase.CreateAsset(mesh, _folder + "/" + name + ".asset");
            return mesh;
        }

        private XRayInstanceBatchSet CreateSet(string name, Mesh mesh, Material extra = null)
        {
            var set = ScriptableObject.CreateInstance<XRayInstanceBatchSet>();
            var batches = new List<XRayInstanceBatch>();
            foreach (float x in new[] { 1f, 2f }) batches.Add(new XRayInstanceBatch(name, mesh, new[] { _material }, new Bounds(new Vector3(x, 0f, 20f), Vector3.one), new[] { Matrix4x4.Translate(new Vector3(x, 0f, 20f)) }, new[] { Vector4.one }, true));
            if (extra != null) batches.Add(new XRayInstanceBatch(name, mesh, new[] { extra }, new Bounds(new Vector3(3f, 0f, 20f), Vector3.one), new[] { Matrix4x4.Translate(new Vector3(3f, 0f, 20f)) }, new[] { Vector4.one }, true));
            set.Initialize(name, 128f, batches);
            AssetDatabase.CreateAsset(set, _folder + "/" + name + ".asset");
            XRayInstancePreparation.Ensure(set);
            return set;
        }

        private static HashSet<string> Pending()
        {
            return (HashSet<string>)typeof(XRayInstancePreparation).GetField("PENDING", STATIC_PRIVATE).GetValue(null);
        }

        private static string[] SetPending()
        {
            return Pending().Where(path => AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(XRayInstanceBatchSet)).ToArray();
        }

        private static void Record(string path)
        {
            typeof(XRayInstancePreparation).GetMethod("RecordChange", STATIC_PRIVATE).Invoke(null, new object[] { path });
        }

        private static void Flush()
        {
            typeof(XRayInstancePreparation).GetMethod("Flush", STATIC_PRIVATE).Invoke(null, null);
        }
    }
}
