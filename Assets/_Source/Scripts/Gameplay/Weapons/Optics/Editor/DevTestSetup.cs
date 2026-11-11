using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Weapons.Optics.Editor
{
    public static class DevTestSetup
    {
        [MenuItem("Tools/Development/Add DevTest To Active Scene")]
        public static void AddToActiveScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Add DevTest in Edit Mode.");
            }

            Scene scene = SceneManager.GetActiveScene();
            FirstPersonWeaponController controller = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (FirstPersonWeaponController candidate in root.GetComponentsInChildren<FirstPersonWeaponController>(true))
                {
                    if (controller != null)
                    {
                        throw new InvalidOperationException("The active scene must contain exactly one player weapon controller.");
                    }

                    controller = candidate;
                }
            }

            if (controller == null)
            {
                throw new InvalidOperationException("The active scene has no FirstPersonWeaponController.");
            }

            DevTest test = controller.GetComponent<DevTest>();
            if (test == null)
            {
                test = Undo.AddComponent<DevTest>(controller.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
            }

            Selection.activeGameObject = controller.gameObject;
            EditorGUIUtility.PingObject(test);
        }
    }
}
