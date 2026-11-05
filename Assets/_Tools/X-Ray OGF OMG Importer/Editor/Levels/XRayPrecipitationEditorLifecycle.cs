using UnityEditor;
using UnityEngine;
using XRay.Importer.Levels;

namespace XRay.Importer.Editor.Levels
{
    [InitializeOnLoad]
    public static class XRayPrecipitationEditorLifecycle
    {
        static XRayPrecipitationEditorLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseAll;
            EditorApplication.quitting += ReleaseAll;
        }

        public static void Release(XRayWeatherController controller)
        {
            if (controller != null && controller.Precipitation != null) controller.Precipitation.Dispose();
        }

        private static void ReleaseAll()
        {
            foreach (XRayWeatherController controller in Resources.FindObjectsOfTypeAll<XRayWeatherController>()) Release(controller);
        }
    }
}
