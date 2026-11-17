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
            Application.runInBackground = true;
            OnDemandRendering.renderFrameInterval = 1;
        }
    }
}
