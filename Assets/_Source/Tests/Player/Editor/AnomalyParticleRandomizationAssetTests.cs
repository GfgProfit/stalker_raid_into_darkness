using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using RaidIntoDarkness.Effects;
using RaidIntoDarkness.Player.Editor;
using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Player.Tests
{
    public sealed class AnomalyParticleRandomizationAssetTests
    {
        [Test]
        public void SevenFamiliesUseOriginalSourcesWithoutDoubleEmission()
        {
            AnomalyParticleSourceManifest manifest = LoadManifest();
            Assert.That(manifest.families.Length, Is.EqualTo(7));
            int sources = 0;
            int eventSources = 0;
            foreach (AnomalyParticleFamilySourceData family in manifest.families)
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(family.prefab);
                Assert.That(root, Is.Not.Null, family.prefab);
                AnomalyParticleEmission controller = root.GetComponent<AnomalyParticleEmission>();
                Assert.That(controller, Is.Not.Null, family.family);
                var registered = new Dictionary<ParticleSystem, AnomalyParticleEmissionSource>();
                foreach (AnomalyParticleEmissionSource source in controller.Sources)
                {
                    Assert.That(registered.ContainsKey(source.System), Is.False, family.family);
                    registered.Add(source.System, source);
                    Assert.That(source.System.emission.rateOverTime.constant, Is.Zero, source.System.name);
                }

                int expected = 0;
                foreach (AnomalyParticleEffectSourceData effect in family.effects)
                {
                    if (effect.unityBindings == null)
                    {
                        continue;
                    }

                    foreach (AnomalyParticleUnityBinding binding in effect.unityBindings)
                    {
                        ParticleSystem system = root.transform.Find(binding.path).GetComponent<ParticleSystem>();
                        if (binding.driveEmission)
                        {
                            expected++;
                            sources++;
                            Assert.That(registered.TryGetValue(system, out AnomalyParticleEmissionSource source), Is.True, binding.path);
                            Assert.That(source.Rate, Is.EqualTo(effect.sourceActions[0].ratePerSecond), binding.path);
                            Assert.That(source.TimeLimit, Is.EqualTo(effect.finiteEmitter ? Mathf.Max(0f, effect.emitterTimeLimit) : 0f), binding.path);
                            Assert.That(system.emission.enabled, Is.True, binding.path);
                        }
                        if (binding.eventControlled)
                        {
                            eventSources++;
                            Assert.That(registered.ContainsKey(system), Is.False, binding.path);
                            Assert.That(system.emission.burstCount, Is.GreaterThan(0), binding.path);
                        }
                    }
                }

                Assert.That(registered.Count, Is.EqualTo(expected), family.family);
            }

            Assert.That(sources, Is.EqualTo(50));
            Assert.That(eventSources, Is.EqualTo(7));
        }

        [Test]
        public void FramesPreserveOriginalFlagsAtlasAndCustomVertexChannel()
        {
            foreach (AnomalyParticleFamilySourceData family in LoadManifest().families)
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(family.prefab);
                foreach (AnomalyParticleEffectSourceData effect in family.effects)
                {
                    if (effect.unityBindings == null)
                    {
                        continue;
                    }

                    foreach (AnomalyParticleUnityBinding binding in effect.unityBindings)
                    {
                        ParticleSystem system = root.transform.Find(binding.path).GetComponent<ParticleSystem>();
                        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
                        if (!effect.framed || binding.duplicateDirectional || !renderer.enabled)
                        {
                            continue;
                        }

                        AnomalyParticleFrames frames = system.GetComponent<AnomalyParticleFrames>();
                        Assert.That(frames, Is.Not.Null, family.family + "/" + binding.path);
                        var serialized = new SerializedObject(frames);
                        Assert.That(serialized.FindProperty("_system").objectReferenceValue, Is.SameAs(system));
                        Assert.That(serialized.FindProperty("_frameCount").intValue, Is.EqualTo(effect.frameCount));
                        Assert.That(serialized.FindProperty("_frameColumns").intValue, Is.EqualTo(effect.frameColumns));
                        Assert.That(serialized.FindProperty("_frameSize").vector2Value, Is.EqualTo(new Vector2(effect.frameSizeX, effect.frameSizeY)));
                        Assert.That(serialized.FindProperty("_framesPerSecond").floatValue, Is.EqualTo(effect.fps));
                        Assert.That(serialized.FindProperty("_animated").boolValue, Is.EqualTo(effect.animated));
                        Assert.That(serialized.FindProperty("_randomStart").boolValue, Is.EqualTo(effect.randomStartFrame));
                        Assert.That(serialized.FindProperty("_randomPlayback").boolValue, Is.EqualTo(effect.randomPlayback));
                        Assert.That(system.textureSheetAnimation.enabled, Is.False);
                        var streams = new List<ParticleSystemVertexStream>();
                        renderer.GetActiveVertexStreams(streams);
                        Assert.That(streams, Is.EqualTo(new[] { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1X }));
                    }
                }
            }
        }

        [Test]
        public void GazirovkaUsesOneOriginalSourceWithRandomPlayback()
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Source/Anomalies/Gazirovka/Gazirovka.prefab");
            ParticleSystem forward = root.transform.Find("Idle/studen_idle_bottom_glow_02__forward").GetComponent<ParticleSystem>();
            ParticleSystem backward = root.transform.Find("Idle/studen_idle_bottom_glow_02__backward").GetComponent<ParticleSystem>();
            Assert.That(forward.main.maxParticles, Is.EqualTo(12));
            Assert.That(backward.emission.enabled, Is.False);
            Assert.That(backward.GetComponent<ParticleSystemRenderer>().enabled, Is.False);
            Assert.That(new SerializedObject(forward.GetComponent<AnomalyParticleFrames>()).FindProperty("_randomPlayback").boolValue, Is.True);
            int matching = 0;
            foreach (AnomalyParticleEmissionSource source in root.GetComponent<AnomalyParticleEmission>().Sources)
            {
                if (source.System == forward)
                {
                    matching++;
                    Assert.That(source.Rate, Is.EqualTo(24f));
                }

                Assert.That(source.System, Is.Not.SameAs(backward));
            }

            Assert.That(matching, Is.EqualTo(1));
        }

        [Test]
        public void SetupIsIdempotentWithoutWritingPrefabAssets()
        {
            foreach (AnomalyParticleFamilySourceData family in LoadManifest().families)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(family.prefab);
                try
                {
                    string before = Snapshot(root);
                    AnomalyParticleRandomizationSetup.Configure(root, family);
                    Assert.That(Snapshot(root), Is.EqualTo(before), family.family);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        [Test]
        public void SpatialBubbleRemainsOutsideThisAdaptation()
        {
            foreach (string path in new[] { "Assets/_Source/Anomalies/SpatialBubble/SpatialBubble.prefab", "Assets/_Source/Anomalies/SpatialBubble/Src/Prefabs/SpatialBubbleSource.prefab" })
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(root, Is.Not.Null, path);
                Assert.That(root.GetComponentsInChildren<AnomalyParticleEmission>(true), Is.Empty, path);
                Assert.That(root.GetComponentsInChildren<AnomalyParticleFrames>(true), Is.Empty, path);
            }
        }

        private static AnomalyParticleSourceManifest LoadManifest()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AnomalyParticleRandomizationSetup.SOURCE_MANIFEST);
            Assert.That(asset, Is.Not.Null);
            return JsonUtility.FromJson<AnomalyParticleSourceManifest>(asset.text);
        }

        private static string Snapshot(GameObject root)
        {
            var text = new StringBuilder();
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component is ParticleSystem || component is ParticleSystemRenderer || component is AnomalyParticleFrames || component is AnomalyParticleEmission)
                {
                    text.Append(EditorJsonUtility.ToJson(component));
                }
            }

            return text.ToString();
        }
    }
}
