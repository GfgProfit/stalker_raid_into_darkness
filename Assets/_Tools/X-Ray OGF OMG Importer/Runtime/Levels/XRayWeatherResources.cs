using System;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [Serializable]
    public struct XRayWeatherResources
    {
        [SerializeField] private string _name0;
        [SerializeField] private string _name1;
        [SerializeField] private string _name2;
        [SerializeField] private string _name3;
        [SerializeField] private Texture _texture0;
        [SerializeField] private Texture _texture1;
        [SerializeField] private Texture _texture2;
        [SerializeField] private Texture _texture3;
        [SerializeField] private Vector4 _weights;

        public XRayWeatherResources(string name, Texture texture = null)
        {
            _name0 = name ?? string.Empty;
            _name1 = _name2 = _name3 = string.Empty;
            _texture0 = texture;
            _texture1 = _texture2 = _texture3 = null;
            _weights = string.IsNullOrEmpty(name) ? Vector4.zero : new Vector4(1.0f, 0.0f, 0.0f, 0.0f);
        }

        public Vector4 Weights => _weights;

        public string Name(int index)
        {
            return index switch { 0 => _name0, 1 => _name1, 2 => _name2, 3 => _name3, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        }

        public Texture Texture(int index)
        {
            return index switch { 0 => _texture0, 1 => _texture1, 2 => _texture2, 3 => _texture3, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        }

        public static XRayWeatherResources Blend(XRayWeatherResources a, XRayWeatherResources b, float weight)
        {
            var result = new XRayWeatherResources();
            for (int i = 0; i < 4; i++)
            {
                result.Add(a.Name(i), a.Texture(i), a._weights[i] * (1.0f - weight));
                result.Add(b.Name(i), b.Texture(i), b._weights[i] * weight);
            }
            return result;
        }

        private void Add(string name, Texture texture, float weight)
        {
            if (weight <= 0.0f || string.IsNullOrEmpty(name)) return;
            for (int i = 0; i < 4; i++)
            {
                if (_weights[i] > 0.0f && Name(i) != name) continue;
                switch (i)
                {
                    case 0: _name0 = name; _texture0 = texture; break;
                    case 1: _name1 = name; _texture1 = texture; break;
                    case 2: _name2 = name; _texture2 = texture; break;
                    case 3: _name3 = name; _texture3 = texture; break;
                }
                _weights[i] += weight;
                return;
            }
            throw new InvalidOperationException("A weather blend cannot contain more than four resources; finish the active transition first.");
        }
    }
}
