
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DAVFX.Realistic6DExplosionsPack
{
  [InitializeOnLoad]
  public class DAVFXRenderingDefines
  {
    static DAVFXRenderingDefines()
    {
        SetMaterialKeywords();
    }


    [RuntimeInitializeOnLoadMethod]
    static void OnRuntimeMethodLoad()
    {
        SetMaterialKeywords();
    }

    private static void SetMaterialKeywords()
    {
        // Check the current render pipeline
        RenderPipelineAsset currentPipeline = GraphicsSettings.currentRenderPipeline;

        // Define symbols based on pipeline type
        if (currentPipeline != null)
        {
            string pipelineName = currentPipeline.GetType().ToString();

            if (pipelineName.Contains("UniversalRenderPipelineAsset"))
            {
                Shader.EnableKeyword("URP_ENABLED");
                Shader.DisableKeyword("HDRP_ENABLED");
            }
            else if (pipelineName.Contains("HDRenderPipelineAsset"))
            {
                Shader.EnableKeyword("HDRP_ENABLED");
                Shader.DisableKeyword("URP_ENABLED");
            }
            else
            {
                Debug.LogWarning("Custom or unsupported render pipeline detected. No symbols set.");
            }
        }
        else
        {
            Shader.DisableKeyword("URP_ENABLED");
            Shader.DisableKeyword("HDRP_ENABLED");
        }
    }
  }
}
#endif
