using System.Collections.Generic;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace RaidIntoDarkness.Player.Tests.Editor
{
    public sealed class SpatialBubbleMeshAssetTests
    {
        private const string ROOT = "Assets/_Source/Anomalies/SpatialBubble/Presentation";
        private const string PREFAB = "Assets/_Source/Anomalies/SpatialBubble/SpatialBubble.prefab";
        private const string SOURCE = "Assets/_Source/Anomalies/SpatialBubble/Src/Prefabs/SpatialBubbleSource.prefab";
        private const string SHADER = ROOT + "/Src/Shaders/SpatialBubbleMesh.shader";

        private static readonly HashSet<string> _ambientEffects = new HashSet<string> { "anomaly_teleport_particle_output", "teleport_leaves_orbit", "teleport_leaves_orbit_00" };

        [Test]
        public void ExperimentIsAVariantWithTheInheritedTeleportAndVisualAnchorSource()
        {
            GameObject prefab = Prefab();
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE);
            Assert.That(source, Is.Not.Null);
            Assert.That(PrefabUtility.GetPrefabAssetType(prefab), Is.EqualTo(PrefabAssetType.Variant));
            Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(prefab)), Is.EqualTo(SOURCE));
            SpatialBubbleTeleport sourceTeleport = source.GetComponent<SpatialBubbleTeleport>();
            SpatialBubbleTeleport teleport = prefab.GetComponent<SpatialBubbleTeleport>();
            Assert.That(teleport, Is.Not.Null);
            Assert.That(teleport.Destination, Is.EqualTo(sourceTeleport.Destination));
            Transform anchor = prefab.transform.Find("SourceVisualOffset");
            Assert.That(anchor, Is.Not.Null);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(anchor), Is.EqualTo(source.transform.Find("SourceVisualOffset")));
            Assert.That(source.GetComponentsInChildren<MeshFilter>(true), Is.Empty);
        }

        [Test]
        public void CoreUsesAClosedThreeDimensionalSphereWithOutwardTriangles()
        {
            GameObject prefab = Prefab();
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(filters.Length, Is.EqualTo(1));
            MeshFilter filter = filters[0];
            Assert.That(filter.transform.parent, Is.EqualTo(prefab.transform.Find("SourceVisualOffset")));
            Assert.That(filter.transform.localScale.x, Is.GreaterThan(0f));
            Assert.That(filter.transform.localScale.y, Is.GreaterThan(0f));
            Assert.That(filter.transform.localScale.z, Is.GreaterThan(0f));
            Assert.That(filter.GetComponent<Collider>(), Is.Null);
            Mesh mesh = filter.sharedMesh;
            Assert.That(mesh, Is.Not.Null);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            Assert.That(vertices.Length, Is.EqualTo(2145));
            Assert.That(triangles.Length / 3, Is.EqualTo(3968));
            Assert.That(mesh.bounds.extents.x, Is.GreaterThan(1.1f));
            Assert.That(mesh.bounds.extents.y, Is.GreaterThan(1.1f));
            Assert.That(mesh.bounds.extents.z, Is.GreaterThan(1.1f));
            for (int index = 0; index < vertices.Length; index++)
            {
                Assert.That(vertices[index].magnitude, Is.EqualTo(1f).Within(0.00001f));
                Assert.That(Vector3.Dot(vertices[index], normals[index]), Is.GreaterThan(0.9999f));
            }
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 first = vertices[triangles[index]];
                Vector3 second = vertices[triangles[index + 1]];
                Vector3 third = vertices[triangles[index + 2]];
                Vector3 cross = Vector3.Cross(second - first, third - first);
                Assert.That(cross.sqrMagnitude, Is.GreaterThan(0.0000000001f));
                Assert.That(Vector3.Dot(cross, first + second + third), Is.GreaterThan(0f));
            }
            var welded = new Dictionary<Vector3Int, int>();
            var vertexIds = new int[vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 position = vertices[index] * 10000f;
                var key = new Vector3Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y), Mathf.RoundToInt(position.z));
                if (welded.TryGetValue(key, out int id) == false)
                {
                    id = welded.Count;
                    welded.Add(key, id);
                }
                vertexIds[index] = id;
            }
            var edges = new Dictionary<long, int>();
            for (int index = 0; index < triangles.Length; index += 3)
            {
                for (int edge = 0; edge < 3; edge++)
                {
                    int first = vertexIds[triangles[index + edge]];
                    int second = vertexIds[triangles[index + (edge + 1) % 3]];
                    long key = (long)Mathf.Min(first, second) * vertices.Length + Mathf.Max(first, second);
                    edges.TryGetValue(key, out int count);
                    edges[key] = count + 1;
                }
            }
            foreach (int count in edges.Values)
            {
                Assert.That(count, Is.EqualTo(2));
            }
            Assert.That(welded.Count - edges.Count + triangles.Length / 3, Is.EqualTo(2));
        }

        [Test]
        public void AllLargeBillboardsAndTheirPresentersAreDisabled()
        {
            GameObject prefab = Prefab();
            int active = 0;
            int disabled = 0;
            foreach (ParticleSystem system in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.name == "Idle")
                {
                    continue;
                }
                if (IsActive(system.transform, prefab.transform))
                {
                    active++;
                    Assert.That(_ambientEffects.Contains(system.name), Is.True, system.name);
                }
                else
                {
                    disabled++;
                }
            }
            Assert.That(active, Is.EqualTo(3));
            Assert.That(disabled, Is.EqualTo(21));
            foreach (SpatialBubblePersistentParticle persistent in prefab.GetComponentsInChildren<SpatialBubblePersistentParticle>(true))
            {
                Assert.That(persistent.enabled, Is.False);
                Assert.That(IsActive(persistent.transform, prefab.transform), Is.False);
            }
            foreach (SpatialBubbleParticleFrames frames in prefab.GetComponentsInChildren<SpatialBubbleParticleFrames>(true))
            {
                Assert.That(frames.enabled, Is.EqualTo(_ambientEffects.Contains(frames.name)));
            }
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(renderers.Length, Is.EqualTo(1));
            Assert.That(renderers[0].shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderers[0].receiveShadows, Is.False);
            Assert.That(prefab.GetComponentsInChildren<Light>(true), Is.Empty);
        }

        [Test]
        public void ExperimentReusesTheOriginalSoundPostProcessAndAmbientMaterials()
        {
            GameObject prefab = Prefab();
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(source.GetComponentsInChildren<Collider>(true).Length));
            Assert.That(prefab.GetComponent<SphereCollider>().radius, Is.GreaterThan(0f));
            Assert.That(prefab.GetComponent<SphereCollider>().isTrigger, Is.True);
            SpatialBubbleTeleport sourceTeleport = source.GetComponent<SpatialBubbleTeleport>();
            SpatialBubbleTeleport teleport = prefab.GetComponent<SpatialBubbleTeleport>();
            Assert.That(new SerializedObject(teleport).FindProperty("_teleportPostProcess").objectReferenceValue, Is.EqualTo(new SerializedObject(sourceTeleport).FindProperty("_teleportPostProcess").objectReferenceValue));
            AudioSource[] originalAudio = source.GetComponentsInChildren<AudioSource>(true);
            AudioSource[] audio = prefab.GetComponentsInChildren<AudioSource>(true);
            Assert.That(audio.Length, Is.EqualTo(originalAudio.Length));
            for (int index = 0; index < audio.Length; index++)
            {
                Assert.That(audio[index].clip, Is.EqualTo(originalAudio[index].clip));
            }
            foreach (ParticleSystemRenderer renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (_ambientEffects.Contains(renderer.name) == false)
                {
                    continue;
                }
                ParticleSystemRenderer original = source.transform.Find("SourceVisualOffset/Idle/" + renderer.name).GetComponent<ParticleSystemRenderer>();
                Assert.That(renderer.sharedMaterial, Is.EqualTo(original.sharedMaterial));
            }
            Assert.That(AssetDatabase.FindAssets("t:Texture2D", new[] { ROOT }), Is.Empty);
            Assert.That(AssetDatabase.FindAssets("t:AudioClip", new[] { ROOT }), Is.Empty);
        }

        [Test]
        public void DestinationViewUsesTheInheritedTeleportAndMeshWithoutSerializedCaptureResources()
        {
            GameObject prefab = Prefab();
            SpatialBubbleDestinationView view = prefab.GetComponent<SpatialBubbleDestinationView>();
            Assert.That(view, Is.Not.Null);
            var serialized = new SerializedObject(view);
            Assert.That(serialized.FindProperty("_teleport").objectReferenceValue, Is.EqualTo(prefab.GetComponent<SpatialBubbleTeleport>()));
            Assert.That(serialized.FindProperty("_surface").objectReferenceValue, Is.EqualTo(prefab.GetComponentInChildren<MeshRenderer>(true)));
            Assert.That(serialized.FindProperty("_viewerCamera").objectReferenceValue, Is.Null);
            Assert.That(serialized.FindProperty("_fullParallax").propertyType, Is.EqualTo(SerializedPropertyType.Boolean));
            Assert.That(serialized.FindProperty("_adaptiveResolution").propertyType, Is.EqualTo(SerializedPropertyType.Boolean));
            Assert.That(serialized.FindProperty("_minimumResolution").intValue, Is.InRange(128, 2048));
            Assert.That(serialized.FindProperty("_antialiasing").enumValueIndex, Is.InRange(0, 2));
            SpatialBubbleMeshReaction reaction = prefab.GetComponent<SpatialBubbleMeshReaction>();
            Assert.That(reaction, Is.Not.Null);
            var reactionData = new SerializedObject(reaction);
            Assert.That(reactionData.FindProperty("_teleport").objectReferenceValue, Is.EqualTo(prefab.GetComponent<SpatialBubbleTeleport>()));
            Assert.That(reactionData.FindProperty("_surface").objectReferenceValue, Is.EqualTo(prefab.GetComponentInChildren<MeshRenderer>(true)));
            Assert.That(reactionData.FindProperty("_viewerCamera").objectReferenceValue, Is.Null);
            Assert.That(prefab.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(AssetDatabase.FindAssets("t:RenderTexture", new[] { ROOT }), Is.Empty);
            Material material = prefab.GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
            foreach (string property in new[] { "_DestinationViewBlend", "_DestinationViewTex", "_DestinationViewAvailable", "_DestinationViewAspect", "_DestinationViewParallax" })
            {
                Assert.That(material.HasProperty(property), Is.True, property);
            }
            Assert.That(material.GetFloat("_DestinationViewBlend"), Is.InRange(0f, 1f));
            Assert.That(material.GetFloat("_DestinationViewAvailable"), Is.Zero);
        }

        [Test]
        public void MeshMaterialUsesTheVolumeShaderAndConservativeBounds()
        {
            MeshRenderer renderer = Prefab().GetComponentInChildren<MeshRenderer>(true);
            Material material = renderer.sharedMaterial;
            Assert.That(material, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(material.shader), Is.EqualTo(SHADER));
            Assert.That(material.enableInstancing, Is.True);
            Assert.That(material.GetFloat("_Warp"), Is.InRange(0f, 0.1f));
            Assert.That(material.GetFloat("_Density"), Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(material.GetFloat("_DepthFade"), Is.GreaterThan(0f));
            Assert.That(material.GetFloat("_DistortionPixels"), Is.GreaterThan(0f));
            Assert.That(material.GetColor("_EnergyColor").b, Is.GreaterThan(0.9f));
            foreach (string property in new[] { "_CoreRadius", "_CoreSoftness", "_CoreEnergyFade", "_CoreDistortionPixels" })
            {
                Assert.That(material.HasProperty(property), Is.True, property);
            }
            Assert.That(material.GetFloat("_CoreRadius"), Is.InRange(0f, 1f));
            Assert.That(material.GetFloat("_CoreSoftness"), Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.That(material.GetFloat("_CoreEnergyFade"), Is.InRange(0f, 1f));
            Assert.That(material.GetFloat("_CoreDistortionPixels"), Is.InRange(0f, 32f));
            foreach (string property in new[] { "_DestinationGrainEnabled", "_DestinationGrainStrength", "_DestinationGrainSize", "_DestinationChromaticAberrationEnabled", "_DestinationChromaticAberrationPixels", "_DestinationEdgeStart", "_DestinationViewHeightOffset" })
            {
                Assert.That(material.HasProperty(property), Is.True, property);
            }
            Assert.That(material.GetFloat("_DestinationGrainEnabled"), Is.InRange(0f, 1f));
            Assert.That(material.GetFloat("_DestinationGrainStrength"), Is.InRange(0f, 1f));
            Assert.That(material.GetFloat("_DestinationGrainSize"), Is.InRange(1f, 4f));
            Assert.That(material.GetFloat("_DestinationChromaticAberrationEnabled"), Is.InRange(0f, 1f));
            Assert.That(material.GetFloat("_DestinationChromaticAberrationPixels"), Is.InRange(0f, 12f));
            Assert.That(material.GetFloat("_DestinationEdgeStart"), Is.InRange(0f, 1f));
            foreach (string property in new[] { "_LiveEdgeEnabled", "_LiveEdgeAmount", "_LiveEdgeScale", "_LiveEdgeSpeed", "_LiveEdgeWidth", "_LiveEdgeGlow", "_ReactionEnabled", "_ProximityBoost", "_ChargeBoost", "_ChargeContraction", "_ImpactCollapse", "_ImpactBoost", "_WavesEnabled", "_WaveInterval", "_WaveSpeed", "_WaveWidth", "_WaveStrength", "_WaveEmission", "_BubbleProximity", "_BubbleCharge", "_BubbleImpact", "_BubbleFlowOffset" })
            {
                Assert.That(material.HasProperty(property), Is.True, property);
                Assert.That(float.IsNaN(material.GetFloat(property)) || float.IsInfinity(material.GetFloat(property)), Is.False, property);
            }
            foreach (string property in new[] { "_OuterFadeEnabled", "_OuterFadeWidth", "_OuterFadePower", "_OuterFadeInsideTransition", "_OuterFadeShell", "_OuterFadeView", "_OuterFadeDistortion", "_ShellOpacity", "_DestinationOpacity", "_DistortionOpacity" })
            {
                Assert.That(material.HasProperty(property), Is.True, property);
                float value = material.GetFloat(property);
                Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False, property);
            }
            foreach (string property in new[] { "_OuterFadeEnabled", "_OuterFadeWidth", "_OuterFadeShell", "_OuterFadeView", "_OuterFadeDistortion", "_ShellOpacity", "_DestinationOpacity", "_DistortionOpacity" })
            {
                Assert.That(material.GetFloat(property), Is.InRange(0f, 1f), property);
            }
            Assert.That(material.GetFloat("_OuterFadePower"), Is.InRange(0.1f, 8f));
            Assert.That(material.GetFloat("_OuterFadeInsideTransition"), Is.InRange(0.01f, 0.5f));
            float heightOffset = material.GetFloat("_DestinationViewHeightOffset");
            Assert.That(float.IsNaN(heightOffset) || float.IsInfinity(heightOffset), Is.False);
        }

        [Test]
        public void BothVolumePassesCompileForWindowsWithAndWithoutInstancing()
        {
            Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(SHADER);
            Assert.That(shader, Is.Not.Null);
            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            Assert.That(subshader.PassCount, Is.EqualTo(2));
            var passes = new HashSet<string>();
            for (int passIndex = 0; passIndex < subshader.PassCount; passIndex++)
            {
                ShaderData.Pass pass = subshader.GetPass(passIndex);
                passes.Add(pass.Name);
                foreach (bool instancing in new[] { false, true })
                {
                    string[] keywords = instancing ? new[] { "INSTANCING_ON" } : new string[0];
                    foreach (ShaderCompilerPlatform platform in new[] { ShaderCompilerPlatform.D3D, ShaderCompilerPlatform.D3D12 })
                    {
                        foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            ShaderData.VariantCompileInfo result = pass.CompileVariant(stage, keywords, platform, BuildTarget.StandaloneWindows64);
                            string context = pass.Name + " " + platform + " " + stage + " " + instancing;
                            Assert.That(result.Success, Is.True, context);
                            Assert.That(result.ShaderData, Is.Not.Null.And.Not.Empty, context);
                            foreach (ShaderMessage message in result.Messages)
                            {
                                Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), context + " " + message.message);
                            }
                        }
                    }
                }
            }
            Assert.That(passes.SetEquals(new[] { "BubbleVolume", "DistortionVectors" }), Is.True);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
        }

        private static GameObject Prefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB);
            Assert.That(prefab, Is.Not.Null, PREFAB);
            return prefab;
        }

        private static bool IsActive(Transform transform, Transform root)
        {
            Transform current = transform;
            while (current != null)
            {
                if (current.gameObject.activeSelf == false)
                {
                    return false;
                }
                if (current == root)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
