using System.Collections.Generic;
using RaidIntoDarkness.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.Integrations.Inventory.Tests
{
    public abstract class OutdoorsSceneInputTestFixture : InputTestFixture
    {
        private const string SCENE = "OutdoorsScene";

        protected virtual string SceneName => SCENE;

        public override void Setup()
        {
            DisposeSceneInput(SceneName);
            base.Setup();
        }

        public override void TearDown()
        {
            try
            {
                DisposeSceneInput(SceneName);
            }
            finally
            {
                base.TearDown();
            }
        }

        private static void DisposeSceneInput(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            var assets = new HashSet<InputActionAsset>();
            foreach (GameObject root in roots)
            {
                foreach (StalkerPlayerInput input in root.GetComponentsInChildren<StalkerPlayerInput>(true))
                {
                    if (input.Actions != null)
                    {
                        assets.Add(input.Actions);
                    }
                }

                foreach (InputSystemUIInputModule input in root.GetComponentsInChildren<InputSystemUIInputModule>(true))
                {
                    if (input.actionsAsset != null)
                    {
                        assets.Add(input.actionsAsset);
                    }
                }
            }

            foreach (GameObject root in roots)
            {
                Object.DestroyImmediate(root);
            }

            foreach (InputActionAsset asset in assets)
            {
                asset.Disable();
                foreach (InputActionMap map in asset.actionMaps)
                {
                    map.Dispose();
                }
            }
        }
    }
}
