using UnityEngine;

[ExecuteAlways]
public class HDRPPointLightsToShader : MonoBehaviour
{
    const int MaxLights = 4;
    const int RescanInterval = 30;

    static readonly int PosID = Shader.PropertyToID("_HDRP_PointLightPos");
    static readonly int ColID = Shader.PropertyToID("_HDRP_PointLightColor");
    static readonly int CountID = Shader.PropertyToID("_HDRP_PointLightCount");

    readonly Vector4[] posArray = new Vector4[MaxLights];
    readonly Vector4[] colArray = new Vector4[MaxLights];

    Light[] cachedLights;
    int frameCounter;

    void OnEnable()
    {
        CacheLights();
    }

    void Update()
    {
        frameCounter++;

       
        if (cachedLights == null || frameCounter % RescanInterval == 0)
        {
            CacheLights();
        }

        UpdateShaderData();
    }

    void CacheLights()
    {
        cachedLights = FindObjectsOfType<Light>();
    }

    void UpdateShaderData()
    {
        int count = 0;

        foreach (var l in cachedLights)
        {
            if (l == null) continue;
            if (!l.enabled) continue;
            if (l.type != LightType.Point) continue;

            if (count >= MaxLights)
                break;

            Vector3 pos = l.transform.position;
            float range = Mathf.Max(0.0001f, l.range);
            Color col = l.color * l.intensity;

            posArray[count] = new Vector4(pos.x, pos.y, pos.z, range);
            colArray[count] = new Vector4(col.r, col.g, col.b, 1f);

            count++;
        }

        Shader.SetGlobalInt(CountID, count);
        Shader.SetGlobalVectorArray(PosID, posArray);
        Shader.SetGlobalVectorArray(ColID, colArray);
    }
}
