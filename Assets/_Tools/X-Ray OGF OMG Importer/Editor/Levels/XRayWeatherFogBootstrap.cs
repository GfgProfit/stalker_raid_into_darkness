using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XRay.Importer.Editor.Levels
{
    [InitializeOnLoad]
    public static class XRayWeatherFogBootstrap
    {
        private static bool _pending;

        static XRayWeatherFogBootstrap()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            Queue();
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (!EditorSceneManager.IsPreviewScene(scene)) Queue();
        }

        private static void Queue()
        {
            if (_pending) return;
            _pending = true;
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
            EditorApplication.update -= Update;
            _pending = false;
            if (!XRayWeatherFogSetup.IsProductionProject) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                try
                {
                    XRayWeatherFogSetup.Configure(scene);
                }
                catch (Exception exception)
                {
                    Debug.LogError("Automatic weather fog connection failed for " + scene.path + ": " + exception.Message);
                }
            }
        }
    }
}
