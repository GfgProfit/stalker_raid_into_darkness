using UnityEngine;
using UnityEngine.Rendering;

namespace RaidIntoDarkness
{
    internal static class RuntimeFramePacing
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (Application.isEditor)
            {
                return;
            }
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            OnDemandRendering.renderFrameInterval = 1;
        }
    }
}
