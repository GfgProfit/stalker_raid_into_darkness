using System;
using UnityEngine;

namespace XRay.Importer
{
    [CreateAssetMenu(menuName = "X-Ray/Monolith Environment Snapshot")]
    public sealed class MonolithEnvironmentSnapshot : ScriptableObject
    {
        [SerializeField] private Cubemap _environment0;
        [SerializeField] private Cubemap _environment1;
        [SerializeField] private Vector4 _environmentColor;
        [SerializeField] private Vector3 _ambient;
        [SerializeField] private float _decodedHemisphere = .6f;
        [SerializeField] private float _hudHemisphere = .15f;
        [SerializeField] private string _verification = "not_runtime_verified";
        [SerializeField] private TextAsset _provenance;

        public Cubemap Environment0 => _environment0;
        public Cubemap Environment1 => _environment1;
        public Vector4 EnvironmentColor => _environmentColor;
        public Vector3 Ambient => _ambient;
        public float DecodedHemisphere => _decodedHemisphere;
        public float HudHemisphere => _hudHemisphere;
        public string Verification => _verification;
        public TextAsset Provenance => _provenance;

        public void ApplyTo(Material material, bool hud)
        {
            if (material == null || material.shader == null || material.shader.name != "HDRP/MonolithLit") throw new ArgumentException("A HDRP/MonolithLit material is required", nameof(material));
            if (_environment0 == null || _environment1 == null) throw new InvalidOperationException("Both environment cubemaps are required");
            material.SetTexture("_MonolithEnv0", _environment0);
            material.SetTexture("_MonolithEnv1", _environment1);
            material.SetVector("_MonolithEnvColor", _environmentColor);
            material.SetVector("_MonolithEnvAmbient", new Vector4(_ambient.x, _ambient.y, _ambient.z, 0));
            material.SetFloat("_MonolithEnvHemisphere", _decodedHemisphere);
            material.SetFloat("_MonolithEnvHudMask", hud ? 1 : 0);
            material.SetFloat("_MonolithEnvHudHemi", _hudHemisphere);
            material.SetFloat("_MonolithEnvCombineClamp", 1);
            material.SetFloat("_MonolithEnvironmentMode", 1);
            material.EnableKeyword("_MONOLITH_ORIGINAL_ENVIRONMENT");
        }
    }
}
