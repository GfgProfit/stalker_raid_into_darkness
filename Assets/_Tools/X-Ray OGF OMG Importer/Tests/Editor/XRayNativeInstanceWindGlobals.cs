using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Tests
{
    internal sealed class XRayNativeInstanceWindGlobals
    {
        private readonly string[] _properties = { XRayFloraWindState.PARAMS_PROPERTY, XRayFloraWindState.TREES_SETUP_PROPERTY, XRayFloraWindState.ANIM_PROPERTY, XRayFloraWindState.ANIM_PREV_PROPERTY, XRayFloraWindState.TREE_WIND_PROPERTY, XRayFloraWindState.TREE_WAVE_PROPERTY, XRayFloraWindState.TREE_WIND_PREV_PROPERTY, XRayFloraWindState.TREE_WAVE_PREV_PROPERTY };
        private readonly Vector4[] _values;
        private readonly float _active;
        private readonly int _benders;
        private readonly Vector4[] _currentBenders;
        private readonly Vector4[] _previousBenders;

        public XRayNativeInstanceWindGlobals()
        {
            _active = Shader.GetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY);
            _values = new Vector4[_properties.Length];
            for (int index = 0; index < _properties.Length; index++) _values[index] = Shader.GetGlobalVector(_properties[index]);
            _benders = Shader.GetGlobalInteger(XRayGrassBenderState.COUNT_PROPERTY);
            _currentBenders = Shader.GetGlobalVectorArray(XRayGrassBenderState.CURRENT_PROPERTY);
            _previousBenders = Shader.GetGlobalVectorArray(XRayGrassBenderState.PREVIOUS_PROPERTY);
        }

        public void Restore()
        {
            Shader.SetGlobalFloat(XRayFloraWindState.ACTIVE_PROPERTY, _active);
            for (int index = 0; index < _properties.Length; index++) Shader.SetGlobalVector(_properties[index], _values[index]);
            Shader.SetGlobalInteger(XRayGrassBenderState.COUNT_PROPERTY, _benders);
            if (_currentBenders != null && _currentBenders.Length > 0) Shader.SetGlobalVectorArray(XRayGrassBenderState.CURRENT_PROPERTY, _currentBenders);
            if (_previousBenders != null && _previousBenders.Length > 0) Shader.SetGlobalVectorArray(XRayGrassBenderState.PREVIOUS_PROPERTY, _previousBenders);
        }
    }
}
