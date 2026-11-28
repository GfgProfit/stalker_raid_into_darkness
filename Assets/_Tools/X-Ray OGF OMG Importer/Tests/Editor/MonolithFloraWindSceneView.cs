using UnityEditor;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Tests
{
    internal sealed class MonolithFloraWindSceneView : SceneView
    {
        public void Bind(Scene scene)
        {
            customScene = scene;
            drawGizmos = false;
            showGrid = false;
            sceneViewState.fxEnabled = true;
            sceneViewState.alwaysRefresh = true;
        }
    }
}
