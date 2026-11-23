using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DAVFX.Realistic6DExplosionsPack
{
  [ExecuteInEditMode]
  public class LightDataToShader : MonoBehaviour
  {
    private static readonly int MAX_DAVFX_LIGHTS = 10;
    private static List<Light> lightsAround = new List<Light>();
    private static int lastLightsAroundrefreshFrame = 0;

    private Light thisLight;

    // Start is called before the first frame update
    void Awake()
    {
        thisLight = GetComponent<Light>();
        if (thisLight && thisLight.type == LightType.Point)
        {
            if (!lightsAround.Contains(thisLight))
                lightsAround.Add(thisLight);
        }
    }

    private void OnEnable()
    {
        if (thisLight && thisLight.type == LightType.Point && !lightsAround.Contains(thisLight))
            lightsAround.Add(thisLight);

    }

    private void OnDisable()
    {
        if (thisLight && thisLight.type == LightType.Point)
            lightsAround.Remove(thisLight);
    }

    // Update is called once per frame
    void Update()
    {
        if (!thisLight)
            return;
        
        //directional light
        if (thisLight.type == LightType.Directional)
        {
            Shader.SetGlobalVector("_DavfxMainLightDirection", -thisLight.transform.forward);
            Shader.SetGlobalColor("_DavfxMainLightColor", thisLight.color);
        }

        //lights around
        if (Time.frameCount> lastLightsAroundrefreshFrame)
        {
            lastLightsAroundrefreshFrame = Time.frameCount;

            int arr_len = Mathf.Min(MAX_DAVFX_LIGHTS, lightsAround.Count);
            float[] lightsAround_range = new float[MAX_DAVFX_LIGHTS];
            Vector4[] lightsAround_pos = new Vector4[MAX_DAVFX_LIGHTS];
            Vector4[] lightsAround_col = new Vector4[MAX_DAVFX_LIGHTS];
            for (int i=0; i< MAX_DAVFX_LIGHTS; i++)
            {
                Light l = i < arr_len ? lightsAround[i] : null;
                if (l!=null)
                {
                    lightsAround_pos[i] = l.transform.position;
                    lightsAround_range[i] = l.range * l.intensity;
                    lightsAround_col[i] = l.color;
                }
                else
                    lightsAround_col[i] = Color.black;
            }

            Shader.SetGlobalVectorArray("_DavfxAdditionalLightsPosition", lightsAround_pos);
            Shader.SetGlobalFloatArray("_DavfxAdditionalLightsRange", lightsAround_range);
            Shader.SetGlobalVectorArray("_DavfxAdditionalLightsColor", lightsAround_col);
            Shader.SetGlobalInt("_DavfxAdditionalLightsCount", arr_len);
        }
    }
  }
}
