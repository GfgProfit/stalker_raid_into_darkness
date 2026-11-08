using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace RaidIntoDarkness.Player
{
    [DisallowMultipleComponent]
    public sealed class StalkerAnomalyScreenEffects : MonoBehaviour
    {
        private const float MIN_FACTOR = 0.0001f;

        private sealed class Layer
        {
            public int Id { get; set; }
            public AnomalyPostProcessAsset Asset { get; set; }
            public float Time { get; set; }
            public float Factor { get; set; }
            public float Expiry { get; set; }
            public bool Stopping { get; set; }
        }

        [SerializeField] private Material _material;
        [SerializeField] private Camera _camera;

        private readonly List<Layer> _layers = new List<Layer>();
        private CustomPassVolume _volume;
        private StalkerAnomalyScreenPass _pass;
        private Material _instance;
        private float _time;
        private float _noiseTimer;
        private Vector2 _noiseShift;

        public int ActiveLayerCount => _layers.Count;

        public void Configure(Material material, Camera camera = null)
        {
            _material = material;
            _camera = camera;
        }

        public void Refresh(int id, AnomalyPostProcessAsset asset, float factor, float expirySeconds)
        {
            if (!isActiveAndEnabled || asset == null || factor <= MIN_FACTOR)
            {
                Remove(id);
                return;
            }

            Layer layer = Find(id);
            if (layer == null)
            {
                layer = new Layer { Id = id };
                _layers.Add(layer);
            }

            if (layer.Asset != asset || layer.Stopping)
            {
                layer.Time = 0.0f;
            }

            layer.Asset = asset;
            layer.Factor = Mathf.Clamp01(factor);
            layer.Expiry = _time + Mathf.Max(0.0f, expirySeconds);
            layer.Stopping = false;
        }

        public void Remove(int id)
        {
            Layer layer = Find(id);
            if (layer != null)
            {
                layer.Stopping = true;
            }
        }

        public void RemoveImmediately(int id)
        {
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                if (_layers[i].Id == id)
                {
                    _layers.RemoveAt(i);
                    return;
                }
            }
        }

        public void Clear()
        {
            _layers.Clear();
            if (_volume != null)
            {
                _volume.enabled = false;
            }
        }

        public void Tick(float deltaTime)
        {
            deltaTime = Mathf.Max(0.0f, deltaTime);
            _time += deltaTime;
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                Layer layer = _layers[i];
                layer.Time += deltaTime;
                if (_time > layer.Expiry)
                {
                    layer.Stopping = true;
                }

                if (layer.Stopping)
                {
                    layer.Factor -= deltaTime;
                }

                if (layer.Asset == null || layer.Factor <= MIN_FACTOR)
                {
                    _layers.RemoveAt(i);
                }
            }

            Apply(deltaTime);
        }

        private void LateUpdate()
        {
            Tick(Time.deltaTime);
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            if (_volume != null)
            {
                Destroy(_volume);
            }

            CoreUtils.Destroy(_instance);
        }

        private Layer Find(int id)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (_layers[i].Id == id)
                {
                    return _layers[i];
                }
            }

            return null;
        }

        private void Apply(float deltaTime)
        {
            if (_layers.Count == 0 || !EnsureVolume())
            {
                if (_volume != null)
                {
                    _volume.enabled = false;
                }

                return;
            }

            Vector3 baseColor = Vector3.one * 0.5f;
            Vector3 addColor = Vector3.zero;
            Vector3 grayColor = Vector3.one * 0.333f;
            Vector4 geometry = Vector4.zero;
            float noiseIntensity = 0.0f;
            float noiseGrain = 1.0f;
            float noiseFps = 30.0f;
            Texture2D firstMap = null;
            Texture2D secondMap = null;
            float mapInfluence = 0.0f;
            float mapInterpolate = 0.0f;
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                Layer layer = _layers[i];
                AnomalyPostProcessAsset asset = layer.Asset;
                float time = layer.Time;
                float factor = layer.Factor;
                baseColor += new Vector3(asset.Evaluate(0, time), asset.Evaluate(1, time), asset.Evaluate(2, time)) * factor;
                addColor += new Vector3(asset.Evaluate(3, time), asset.Evaluate(4, time), asset.Evaluate(5, time)) * factor;
                grayColor += new Vector3(asset.Evaluate(6, time), asset.Evaluate(7, time), asset.Evaluate(8, time)) * factor;
                geometry += new Vector4(asset.Evaluate(9, time), asset.Evaluate(10, time), asset.Evaluate(11, time), asset.Evaluate(12, time)) * factor;
                noiseIntensity = Mathf.Max(noiseIntensity, asset.Evaluate(13, time));
                noiseGrain = Mathf.Max(noiseGrain, asset.Evaluate(14, time));
                noiseFps = Mathf.Max(noiseFps, asset.Evaluate(15, time));
                if (asset.ColorMap != null)
                {
                    float influence = asset.Evaluate(16, time) * factor;
                    if (firstMap == null)
                    {
                        firstMap = asset.ColorMap;
                        mapInfluence = influence;
                    }
                    else
                    {
                        secondMap = asset.ColorMap;
                        float total = mapInfluence + influence;
                        mapInterpolate = total > MIN_FACTOR ? influence / total : 0.0f;
                        mapInfluence = Mathf.Max(mapInfluence, influence);
                    }
                }
            }

            _noiseTimer -= deltaTime;
            if (_noiseTimer < 0.0f)
            {
                _noiseShift = new Vector2(Random.value, Random.value);
                _noiseTimer = 1.0f / Mathf.Max(1.0f, noiseFps);
            }

            _instance.SetVector("_AnomalyBaseColor", baseColor);
            _instance.SetVector("_AnomalyAddColor", addColor);
            _instance.SetVector("_AnomalyGrayColor", grayColor);
            _instance.SetVector("_AnomalyGeometry", geometry);
            _instance.SetVector("_AnomalyNoise", new Vector4(noiseIntensity, noiseGrain, _noiseShift.x, _noiseShift.y));
            _instance.SetVector("_AnomalyColorMap", new Vector4(mapInfluence, mapInterpolate, 0.0f, 0.0f));
            _instance.SetTexture("_AnomalyGradient0", firstMap != null ? firstMap : Texture2D.grayTexture);
            _instance.SetTexture("_AnomalyGradient1", secondMap != null ? secondMap : firstMap != null ? firstMap : Texture2D.grayTexture);
            _volume.enabled = true;
        }

        private bool EnsureVolume()
        {
            if (_volume != null)
            {
                return true;
            }

            if (_material == null || _camera == null)
            {
                return false;
            }

            _instance = new Material(_material);
            _volume = gameObject.AddComponent<CustomPassVolume>();
            _volume.isGlobal = true;
            _volume.targetCamera = _camera;
            _volume.priority = 10.0f;
            _volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
            _pass = (StalkerAnomalyScreenPass)_volume.AddPassOfType(typeof(StalkerAnomalyScreenPass));
            _pass.name = nameof(StalkerAnomalyScreenEffects);
            _pass.Material = _instance;
            _pass.Camera = _camera;
            return true;
        }
    }
}
