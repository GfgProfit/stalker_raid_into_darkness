using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Levels
{
    public sealed class XRayWetMaterialBindings : IDisposable
    {
        private const string SHADER_NAME = "HDRP/MonolithLit";
        private const string RESPONSE = "_MonolithWetResponse";
        private const string PUDDLE_SLICE = "_MonolithPuddleMaskSlice";
        private const string KEYWORD = "_MONOLITH_PRECIPITATION";
        private const string SOURCE_TERRAIN_KEYWORD = "_MONOLITH_WET_ANOMALY_TERRAIN";

        private readonly Dictionary<Material, Material> _clones = new Dictionary<Material, Material>();
        private readonly HashSet<Renderer> _bound = new HashSet<Renderer>();
        private readonly HashSet<Component> _boundDrawers = new HashSet<Component>();
        private readonly HashSet<Material> _used = new HashSet<Material>();
        private readonly List<Material> _unused = new List<Material>();
        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly List<ResolverBinding> _resolverBindings = new List<ResolverBinding>();
        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private readonly List<Material> _currentMaterials = new List<Material>();
        private readonly List<XRayLevelInstancedRenderer> _instances = new List<XRayLevelInstancedRenderer>();
        private readonly List<XRayDetailVegetation> _details = new List<XRayDetailVegetation>();

        public int CloneCount => _clones.Count;
        public int BindingCount => _bindings.Count;
        public int CustomDrawBindingCount => _resolverBindings.Count;
        public long EstimatedBytes
        {
            get
            {
                long total = 0;
                foreach (Material material in _clones.Values)
                {
                    if (material != null) total += Profiler.GetRuntimeMemorySizeLong(material);
                }
                return total;
            }
        }

        public void Bind(Scene scene, PrecipitationConfig config)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("Wet surfaces require a loaded scene.", nameof(scene));
            if (config == null) throw new ArgumentNullException(nameof(config));
            Prune(config);
            scene.GetRootGameObjects(_roots);
            foreach (GameObject root in _roots)
            {
                root.GetComponentsInChildren(true, _renderers);
                foreach (MeshRenderer renderer in _renderers)
                {
                    if (_bound.Contains(renderer)) continue;
                    Material[] original = renderer.sharedMaterials;
                    Material[] assigned = null;
                    for (int i = 0; i < original.Length; i++)
                    {
                        Material source = original[i];
                        if (source == null) continue;
                        Material clone = GetOrCreate(source, config);
                        if (clone == source) continue;
                        if (assigned == null) assigned = (Material[])original.Clone();
                        assigned[i] = clone;
                    }
                    if (assigned == null) continue;
                    renderer.sharedMaterials = assigned;
                    _bindings.Add(new Binding(renderer, original, assigned));
                    _bound.Add(renderer);
                }
                _renderers.Clear();
                root.GetComponentsInChildren(true, _instances);
                foreach (XRayLevelInstancedRenderer instance in _instances) BindDrawer(instance, config);
                _instances.Clear();
                root.GetComponentsInChildren(true, _details);
                foreach (XRayDetailVegetation detail in _details) BindDrawer(detail, config);
                _details.Clear();
            }
            _roots.Clear();
        }

        public void Refresh(PrecipitationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            foreach (KeyValuePair<Material, Material> pair in _clones)
            {
                if (pair.Key != null && pair.Value != null) Configure(pair.Key, pair.Value, config);
            }
        }

        public void Dispose()
        {
            foreach (ResolverBinding binding in _resolverBindings) binding.Restore();
            foreach (Binding binding in _bindings)
            {
                if (binding.Renderer == null) continue;
                binding.Renderer.GetSharedMaterials(_currentMaterials);
                binding.Restore(_currentMaterials);
            }
            foreach (Material material in _clones.Values) CoreUtils.Destroy(material);
            _clones.Clear();
            _bindings.Clear();
            _resolverBindings.Clear();
            _bound.Clear();
            _boundDrawers.Clear();
            _used.Clear();
            _unused.Clear();
            _roots.Clear();
            _renderers.Clear();
            _currentMaterials.Clear();
            _instances.Clear();
            _details.Clear();
        }

        public static XRayWetSurfaceType Classify(Material material)
        {
            if (material == null) return XRayWetSurfaceType.Excluded;
            string name = material.name.ToLowerInvariant();
            Texture texture = BaseTexture(material);
            if (texture != null) name += " " + texture.name.ToLowerInvariant();
            if (Contains(name, "water", "voda", "glass", "steklo")) return XRayWetSurfaceType.Excluded;
            if (material.IsKeywordEnabled("_MONOLITH_LEVEL_DETAIL") || material.IsKeywordEnabled("_MONOLITH_LEVEL_FLORA")) return XRayWetSurfaceType.Vegetation;
            if (Contains(name, "prop_roadblock1")) return XRayWetSurfaceType.Concrete;
            if (Contains(name, "asphalt", "asfalt", "road", "dorog")) return XRayWetSurfaceType.Asphalt;
            if (Contains(name, "concrete", "beton", "brick", "cement")) return XRayWetSurfaceType.Concrete;
            if (Contains(name, "metal", "metall", "iron", "steel", "profnast")) return XRayWetSurfaceType.Metal;
            if (Contains(name, "wood", "dosk", "derev", "plank", "bark")) return XRayWetSurfaceType.Wood;
            if (Contains(name, "stone", "rock", "granit", "kamen")) return XRayWetSurfaceType.Stone;
            if (Contains(name, "soil", "earth", "ground", "mud", "gravel", "terrain") || material.IsKeywordEnabled("_MONOLITH_LEVEL_TERRAIN")) return XRayWetSurfaceType.Soil;
            if (material.HasProperty("_MonolithMaterialId") && material.GetFloat("_MonolithMaterialId") >= 3.0f) return XRayWetSurfaceType.Metal;
            return XRayWetSurfaceType.Generic;
        }

        private Material GetOrCreate(Material source, PrecipitationConfig config)
        {
            if (source == null) return null;
            if (_clones.TryGetValue(source, out Material clone))
            {
                if (clone != null) return clone;
                _clones.Remove(source);
            }
            if (!Eligible(source)) return source;
            clone = new Material(source);
            clone.name = source.name + " (Precipitation)";
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.enableInstancing = source.enableInstancing;
            try
            {
                Configure(source, clone, config);
            }
            catch
            {
                CoreUtils.Destroy(clone);
                throw;
            }
            _clones.Add(source, clone);
            return clone;
        }

        private Material Resolve(Material source)
        {
            return source != null && _clones.TryGetValue(source, out Material clone) && clone != null ? clone : source;
        }

        private void BindDrawer(Component drawer, PrecipitationConfig config)
        {
            if (_boundDrawers.Contains(drawer)) return;
            var binding = new ResolverBinding(drawer, this);
            binding.Prepare(config, null);
            binding.Attach();
            _resolverBindings.Add(binding);
            _boundDrawers.Add(drawer);
        }

        private void Prune(PrecipitationConfig config)
        {
            _used.Clear();
            for (int i = _bindings.Count - 1; i >= 0; i--)
            {
                Binding binding = _bindings[i];
                if (binding.Renderer == null)
                {
                    _bound.Remove(binding.Renderer);
                    _bindings.RemoveAt(i);
                    continue;
                }
                binding.Renderer.GetSharedMaterials(_currentMaterials);
                if (!binding.Matches(_currentMaterials))
                {
                    binding.Restore(_currentMaterials);
                    _bound.Remove(binding.Renderer);
                    _bindings.RemoveAt(i);
                    continue;
                }
                foreach (Material material in _currentMaterials) _used.Add(material);
            }
            for (int i = _resolverBindings.Count - 1; i >= 0; i--)
            {
                ResolverBinding binding = _resolverBindings[i];
                if (!binding.Matches)
                {
                    binding.Restore();
                    _boundDrawers.Remove(binding.Component);
                    _resolverBindings.RemoveAt(i);
                    continue;
                }
                binding.Prepare(config, _used);
            }
            _unused.Clear();
            foreach (KeyValuePair<Material, Material> pair in _clones)
            {
                if (!_used.Contains(pair.Value)) _unused.Add(pair.Key);
            }
            foreach (Material source in _unused)
            {
                CoreUtils.Destroy(_clones[source]);
                _clones.Remove(source);
            }
            _unused.Clear();
            _used.Clear();
            _currentMaterials.Clear();
        }

        private static bool Eligible(Material material)
        {
            if (material == null || material.shader == null || material.shader.name != SHADER_NAME || !material.HasProperty(RESPONSE)) return false;
            if (material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f) return false;
            if (material.HasProperty("_MonolithHudNormal") && material.GetFloat("_MonolithHudNormal") > 0.5f) return false;
            return Classify(material) != XRayWetSurfaceType.Excluded;
        }

        private static void Configure(Material source, Material clone, PrecipitationConfig config)
        {
            Vector4 response = source.GetVector(RESPONSE);
            XRayWetSurfaceResponse inherited = config.GetWetResponse(Classify(source));
            if (inherited == null) throw new InvalidOperationException("PrecipitationConfig requires the complete wet surface response table.");
            clone.SetVector(RESPONSE, response.w > 0.0f ? response : inherited.Response);
            clone.EnableKeyword(KEYWORD);
            if (HasAnomalyTerrainSource(source))
            {
                clone.EnableKeyword(SOURCE_TERRAIN_KEYWORD);
                ConfigureTerrainResponse(source, clone, config, "R", response);
                ConfigureTerrainResponse(source, clone, config, "G", response);
                ConfigureTerrainResponse(source, clone, config, "B", response);
                ConfigureTerrainResponse(source, clone, config, "A", response);
            }
            else clone.DisableKeyword(SOURCE_TERRAIN_KEYWORD);
            float slice = source.GetFloat(PUDDLE_SLICE);
            if (float.IsFinite(slice) && slice >= 2.0f)
            {
                clone.SetFloat(PUDDLE_SLICE, slice);
                clone.SetFloat("_MonolithPuddleMaskFlipV", source.GetFloat("_MonolithPuddleMaskFlipV"));
                return;
            }
            Texture texture = BaseTexture(source);
            clone.SetFloat(PUDDLE_SLICE, texture != null ? config.FindPuddleMaskSlice(texture.name) : -1.0f);
            clone.SetFloat("_MonolithPuddleMaskFlipV", 0.0f);
        }

        private static void ConfigureTerrainResponse(Material source, Material clone, PrecipitationConfig config, string channel, Vector4 uniformResponse)
        {
            string property = "_MonolithWetTerrainResponse" + channel;
            Vector4 response = source.GetVector(property);
            if (response.w <= 0.0f)
            {
                Texture texture = source.GetTexture("_MonolithTerrainSourceColor" + channel);
                if (texture == null) texture = source.GetTexture("_MonolithTerrainDetail" + channel);
                string name = texture != null ? texture.name.ToLowerInvariant() : string.Empty;
                XRayWetSurfaceType surface = XRayWetSurfaceType.Soil;
                if (Contains(name, "asphalt", "asfalt", "road", "dorog")) surface = XRayWetSurfaceType.Asphalt;
                else if (Contains(name, "concrete", "beton", "brick", "cement")) surface = XRayWetSurfaceType.Concrete;
                else if (Contains(name, "stone", "rock", "granit", "kamen")) surface = XRayWetSurfaceType.Stone;
                response = uniformResponse.w > 0.0f ? uniformResponse : config.GetWetResponse(surface).Response;
            }
            clone.SetVector(property, response);
        }

        private static bool HasAnomalyTerrainSource(Material material)
        {
            return material.IsKeywordEnabled("_MONOLITH_LEVEL_TERRAIN") && material.GetFloat("_MonolithSourceProfile") > 0.5f && material.GetFloat("_MonolithSourceTerrain") > 0.5f && material.GetFloat("_MonolithTerrainSource") > 0.5f && material.GetFloat("_MonolithTerrainSource") < 1.5f && material.GetFloat("_MonolithHasTerrainSourceColor") > 0.5f && material.GetFloat("_MonolithTrueStalkerSourceTerrain") <= 0.5f;
        }

        private static Texture BaseTexture(Material material)
        {
            bool sourceTerrain = material.HasProperty("_MonolithHasTerrainSourceColor") && material.GetFloat("_MonolithHasTerrainSourceColor") > 0.5f;
            Texture texture = sourceTerrain && material.HasProperty("_MonolithTerrainSourceBase") ? material.GetTexture("_MonolithTerrainSourceBase") : null;
            if (texture == null && material.HasProperty("_BaseColorMap")) texture = material.GetTexture("_BaseColorMap");
            return texture;
        }

        private static bool Contains(string text, params string[] values)
        {
            foreach (string value in values)
            {
                if (text.Contains(value)) return true;
            }
            return false;
        }

        private sealed class ResolverBinding
        {
            private readonly Component _component;
            private readonly XRayLevelInstancedRenderer _instances;
            private readonly XRayDetailVegetation _details;
            private readonly XRayInstanceBatchSet _batchSet;
            private readonly XRayDetailDatabase _database;
            private readonly XRayWetMaterialBindings _owner;
            private readonly Func<Material, Material> _previous;
            private readonly Func<Material, Material> _assigned;

            public Component Component => _component;
            public bool Matches => _component != null && ReferenceEquals(Current, _assigned) && (_instances == null || _instances.BatchSet == _batchSet) && (_details == null || _details.Database == _database);

            private Func<Material, Material> Current => _instances != null ? _instances.MaterialResolver : _details != null ? _details.MaterialResolver : null;

            public ResolverBinding(Component component, XRayWetMaterialBindings owner)
            {
                _component = component;
                _owner = owner;
                _instances = component as XRayLevelInstancedRenderer;
                _details = component as XRayDetailVegetation;
                _batchSet = _instances != null ? _instances.BatchSet : null;
                _database = _details != null ? _details.Database : null;
                _previous = Current;
                _assigned = Resolve;
            }

            public void Attach()
            {
                Assign(_assigned);
            }

            public void Restore()
            {
                if (_component != null && ReferenceEquals(Current, _assigned)) Assign(_previous);
            }

            public void Prepare(PrecipitationConfig config, HashSet<Material> used)
            {
                if (_instances != null && _batchSet != null)
                {
                    foreach (XRayInstanceBatch batch in _batchSet.Batches)
                    {
                        foreach (Material material in batch.Materials) Prepare(material, config, used);
                    }
                }
                else if (_details != null && _database != null)
                {
                    foreach (XRayDetailModelEntry model in _database.Models) Prepare(model.Material, config, used);
                }
            }

            private void Prepare(Material material, PrecipitationConfig config, HashSet<Material> used)
            {
                if (material == null) return;
                Material resolved = _previous != null ? _previous(material) : material;
                Material clone = _owner.GetOrCreate(resolved, config);
                if (used != null && clone != null) used.Add(clone);
            }

            private Material Resolve(Material material)
            {
                if (material == null) return null;
                return _owner.Resolve(_previous != null ? _previous(material) : material);
            }

            private void Assign(Func<Material, Material> resolver)
            {
                if (_instances != null)
                {
                    if (_previous == null && ReferenceEquals(resolver, _assigned)) _instances.SetOrderIndependentMaterialResolver(resolver);
                    else _instances.MaterialResolver = resolver;
                }
                else if (_details != null) _details.MaterialResolver = resolver;
            }
        }

        private sealed class Binding
        {
            private readonly Material[] _original;

            public Renderer Renderer { get; }
            public Material[] Assigned { get; }

            public Binding(Renderer renderer, Material[] original, Material[] assigned)
            {
                Renderer = renderer;
                _original = original;
                Assigned = assigned;
            }

            public bool Matches(List<Material> current)
            {
                if (current.Count != Assigned.Length) return false;
                for (int i = 0; i < current.Count; i++)
                {
                    if (Assigned[i] != _original[i] && Assigned[i] == null) return false;
                    if (current[i] != Assigned[i]) return false;
                }
                return true;
            }

            public void Restore(List<Material> current)
            {
                if (Renderer == null) return;
                bool changed = false;
                int count = Mathf.Min(current.Count, Assigned.Length);
                for (int i = 0; i < count; i++)
                {
                    if (Assigned[i] == null || Assigned[i] == _original[i] || current[i] != Assigned[i]) continue;
                    current[i] = _original[i];
                    changed = true;
                }
                if (changed) Renderer.SetSharedMaterials(current);
            }
        }
    }
}
