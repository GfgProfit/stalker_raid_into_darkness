using UnityEngine;

[ExecuteAlways]
public class HDRPDirectionalLightToShader : MonoBehaviour
{
    static readonly int DirLightColorID =
        Shader.PropertyToID("_MainDirectionalLightColor");

    Light directionalLight;

    void Update()
    {
        if (directionalLight == null)
            directionalLight = RenderSettings.sun;

        if (directionalLight == null)
            return;

        if (!directionalLight.enabled ||
            directionalLight.type != LightType.Directional)
            return;

        Shader.SetGlobalVector(DirLightColorID,
            directionalLight.color * directionalLight.intensity);
    }
}
