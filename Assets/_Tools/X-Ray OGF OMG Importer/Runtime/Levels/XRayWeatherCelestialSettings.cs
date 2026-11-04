using System;
using System.Collections.Generic;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [Serializable]
    public struct XRayWeatherFlareElement
    {
        [SerializeField] private Texture2D _texture;
        [SerializeField] private float _radius;
        [SerializeField] private float _position;
        [SerializeField] private float _opacity;
        [SerializeField] private bool _ignoreColor;

        public Texture2D Texture => _texture;
        public float Radius => _radius;
        public float Position => _position;
        public float Opacity => _opacity;
        public bool IgnoreColor => _ignoreColor;

        public XRayWeatherFlareElement(Texture2D texture, float radius, float position, float opacity, bool ignoreColor)
        {
            _texture = texture;
            _radius = radius;
            _position = position;
            _opacity = opacity;
            _ignoreColor = ignoreColor;
        }
    }

    [Serializable]
    public sealed class XRayWeatherFlareDefinition
    {
        [SerializeField] private string _name;
        [SerializeField] private XRayWeatherFlareElement _source;
        [SerializeField] private XRayWeatherFlareElement _gradient;
        [SerializeField] private XRayWeatherFlareElement[] _elements;
        [SerializeField] private float _riseTime;
        [SerializeField] private float _downTime;

        public string Name => _name;
        public XRayWeatherFlareElement Source => _source;
        public XRayWeatherFlareElement Gradient => _gradient;
        public IReadOnlyList<XRayWeatherFlareElement> Elements => _elements ?? Array.Empty<XRayWeatherFlareElement>();
        public float RiseTime => _riseTime;
        public float DownTime => _downTime;

        public XRayWeatherFlareDefinition(string name, XRayWeatherFlareElement source, XRayWeatherFlareElement gradient, XRayWeatherFlareElement[] elements, float riseTime, float downTime)
        {
            _name = name;
            _source = source;
            _gradient = gradient;
            _elements = elements ?? Array.Empty<XRayWeatherFlareElement>();
            _riseTime = riseTime;
            _downTime = downTime;
        }
    }

    public sealed class XRayWeatherCelestialSettings : ScriptableObject
    {
        public const string RESOURCE_PATH = "XRayWeatherCelestial";

        [SerializeField] private Shader _shader;
        [SerializeField] private float _shaftsMin;
        [SerializeField] private float _shaftsValue;
        [SerializeField] private bool _screenSpace;
        [SerializeField] private XRayWeatherFlareDefinition[] _definitions = Array.Empty<XRayWeatherFlareDefinition>();

        public Shader Shader => _shader;
        public float ShaftsMin => _shaftsMin;
        public float ShaftsValue => _shaftsValue;
        public bool ScreenSpace => _screenSpace;
        public IReadOnlyList<XRayWeatherFlareDefinition> Definitions => _definitions;

        public void Initialize(Shader shader, float shaftsMin, float shaftsValue, bool screenSpace, XRayWeatherFlareDefinition[] definitions)
        {
            _shader = shader;
            _shaftsMin = shaftsMin;
            _shaftsValue = shaftsValue;
            _screenSpace = screenSpace;
            _definitions = definitions ?? Array.Empty<XRayWeatherFlareDefinition>();
        }

        public XRayWeatherFlareDefinition FindDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition)) return null;
            foreach (XRayWeatherFlareDefinition candidate in _definitions)
            {
                if (candidate != null && string.Equals(candidate.Name, definition, StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return null;
        }
    }
}
