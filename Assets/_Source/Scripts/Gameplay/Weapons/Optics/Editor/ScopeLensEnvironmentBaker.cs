using System;
using System.IO;
using RaidIntoDarkness.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RaidIntoDarkness.Weapons.Optics.Editor
{
    public static class ScopeLensEnvironmentBaker
    {
        private const string OUTPUT_ROOT = "Assets/_Source/Shaders/Optics/Environments";
        private const string ARCHIVE_ROOT = "E:/Projects/stalker_raid_into_darkness_Archive/LocationOpticsEnvironment_20261111";
        private const int RESOLUTION = 256;

        [MenuItem("Tools/Optics/Bake Active Scene Lens Environment")]
        public static void BakeActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || string.IsNullOrEmpty(scene.path) || !(RenderPipelineManager.currentPipeline is HDRenderPipeline))
            {
                throw new InvalidOperationException("Open a saved location scene in Edit Mode with HDRP active before baking the lens environment.");
            }

            string sceneGuid = AssetDatabase.AssetPathToGUID(scene.path);
            if (string.IsNullOrEmpty(sceneGuid))
            {
                throw new InvalidOperationException("The active scene must have a valid asset GUID.");
            }

            ScopeLensEnvironment environment = FindEnvironment(scene);
            StalkerCameraController source = FindPlayerCamera(scene);
            Camera sourceCamera = source != null ? source.Camera : null;
            Vector3 position;
            if (environment != null)
            {
                position = environment.transform.position;
            }
            else if (sourceCamera != null)
            {
                position = sourceCamera.transform.position;
            }
            else if (SceneView.lastActiveSceneView != null)
            {
                position = SceneView.lastActiveSceneView.camera.transform.position;
            }
            else
            {
                throw new InvalidOperationException("The location needs a ScopeLensEnvironment capture point, a player camera, or an open Scene view.");
            }

            string outputFolder = OUTPUT_ROOT + "/" + sceneGuid;
            string outputPath = outputFolder + "/LensEnvironment.asset";
            Renderer[] hidden = source != null ? source.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            bool[] original = new bool[hidden.Length];
            for (int index = 0; index < hidden.Length; index++)
            {
                original[index] = hidden[index].forceRenderingOff;
            }

            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject probeObject = null;
            RenderTexture target = null;
            Texture2D face = null;
            Cubemap cube = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                probeObject = new GameObject("LensEnvironmentBake");
                probeObject.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(probeObject, preview);
                probeObject.transform.position = position;
                HDAdditionalReflectionData probe = probeObject.AddComponent<HDAdditionalReflectionData>();
                probe.mode = ProbeSettings.Mode.Baked;
                probe.settingsRaw.cameraSettings.culling.cullingMask = sourceCamera != null ? sourceCamera.cullingMask : -1;
                probe.settingsRaw.cameraSettings.volumes.layerMask = -1;
                target = HDRenderUtilities.CreateReflectionProbeRenderTarget(RESOLUTION, GraphicsFormat.R16G16B16A16_SFloat);
                for (int index = 0; index < hidden.Length; index++)
                {
                    hidden[index].forceRenderingOff = true;
                }

                HDRenderUtilities.ComputeCameraSettingsFromProbeSettings(probe.settings, ProbeCapturePositionSettings.ComputeFrom(probe, null), out CameraSettings cameraSettings, out CameraPositionSettings cameraPosition, EditorSceneManager.GetSceneCullingMask(scene));
                cameraSettings.flipYMode = HDAdditionalCameraData.FlipYMode.Automatic;
                cameraSettings.invertFaceCulling = true;
                HDRenderUtilities.Render(cameraSettings, cameraPosition, target);
                cube = new Cubemap(RESOLUTION, TextureFormat.RGBAHalf, true);
                cube.name = scene.name + "_LensEnvironment";
                cube.wrapMode = TextureWrapMode.Clamp;
                cube.filterMode = FilterMode.Trilinear;
                face = new Texture2D(RESOLUTION, RESOLUTION, TextureFormat.RGBAHalf, false, true);
                float maximum = 0f;
                for (int index = 0; index < 6; index++)
                {
                    Graphics.SetRenderTarget(target, 0, (CubemapFace)index);
                    face.ReadPixels(new Rect(0, 0, RESOLUTION, RESOLUTION), 0, 0, false);
                    Color[] pixels = face.GetPixels();
                    foreach (Color pixel in pixels)
                    {
                        if (!float.IsFinite(pixel.r) || !float.IsFinite(pixel.g) || !float.IsFinite(pixel.b))
                        {
                            throw new InvalidOperationException("The lens environment capture contains non-finite HDR pixels.");
                        }

                        maximum = Mathf.Max(maximum, Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)));
                    }

                    cube.SetPixels(pixels, (CubemapFace)index);
                }

                if (maximum <= 0f)
                {
                    throw new InvalidOperationException("The lens environment capture is black. Check the scene lighting and the player camera culling mask.");
                }

                cube.Apply(true, false);
                string run = Path.Combine(ARCHIVE_ROOT, "Bake_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
                Directory.CreateDirectory(run);
                BackupAsset(scene.path, run);
                BackupAsset(outputPath, run);
                if (!AssetDatabase.IsValidFolder(OUTPUT_ROOT))
                {
                    AssetDatabase.CreateFolder("Assets/_Source/Shaders/Optics", "Environments");
                }

                if (!AssetDatabase.IsValidFolder(outputFolder))
                {
                    AssetDatabase.CreateFolder(OUTPUT_ROOT, sceneGuid);
                }

                Cubemap existing = AssetDatabase.LoadAssetAtPath<Cubemap>(outputPath);
                if (existing == null && AssetDatabase.LoadMainAssetAtPath(outputPath) != null)
                {
                    throw new InvalidOperationException("The lens environment output path is occupied by another asset.");
                }

                if (existing != null)
                {
                    if (existing.width != RESOLUTION || existing.format != TextureFormat.RGBAHalf || (existing.mipmapCount != 1 && existing.mipmapCount != cube.mipmapCount))
                    {
                        throw new InvalidOperationException("The existing lens environment has an incompatible format. Move it aside before rebaking.");
                    }

                    Undo.RecordObject(existing, "Bake Lens Environment");
                    for (int index = 0; index < 6; index++)
                    {
                        existing.SetPixels(cube.GetPixels((CubemapFace)index), (CubemapFace)index);
                    }

                    existing.Apply(existing.mipmapCount > 1, false);
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssetIfDirty(existing);
                }
                else
                {
                    AssetDatabase.CreateAsset(cube, outputPath);
                    existing = cube;
                    cube = null;
                }

                if (environment == null)
                {
                    GameObject environmentObject = new GameObject("Scope Lens Environment");
                    SceneManager.MoveGameObjectToScene(environmentObject, scene);
                    environmentObject.transform.position = position;
                    Undo.RegisterCreatedObjectUndo(environmentObject, "Create Lens Environment");
                    environment = Undo.AddComponent<ScopeLensEnvironment>(environmentObject);
                }

                var settings = new SerializedObject(environment);
                settings.FindProperty("_cubemap").objectReferenceValue = existing;
                settings.FindProperty("_useBakedEnvironment").boolValue = true;
                settings.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(scene);
                File.WriteAllText(Path.Combine(run, "bake.txt"), "Scene: " + scene.path + "\nPosition: " + position.ToString("F4") + "\nCubemap: " + outputPath + "\nResolution: " + RESOLUTION + "\nOrientation: Native cubemap; no forced FlipY.\nBinding: ScopeLensEnvironment in the location scene; save the scene to persist the binding.");
                EditorGUIUtility.PingObject(environment);
                Debug.Log("Lens environment baked: " + outputPath + ". Save the location scene. Scope materials are unchanged; adjust Lens Reflection Amount on each lens material.", environment);
            }
            finally
            {
                RenderTexture.active = previous;
                for (int index = 0; index < hidden.Length; index++)
                {
                    if (hidden[index] != null)
                    {
                        hidden[index].forceRenderingOff = original[index];
                    }
                }

                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }

                if (face != null)
                {
                    Object.DestroyImmediate(face);
                }

                if (cube != null)
                {
                    Object.DestroyImmediate(cube);
                }

                if (probeObject != null)
                {
                    Object.DestroyImmediate(probeObject);
                }

                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static StalkerCameraController FindPlayerCamera(Scene scene)
        {
            foreach (StalkerCameraController candidate in Object.FindObjectsByType<StalkerCameraController>())
            {
                if (candidate.gameObject.scene == scene && candidate.Camera != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static ScopeLensEnvironment FindEnvironment(Scene scene)
        {
            ScopeLensEnvironment result = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ScopeLensEnvironment candidate in root.GetComponentsInChildren<ScopeLensEnvironment>(true))
                {
                    if (result != null)
                    {
                        throw new InvalidOperationException("The active location scene must contain at most one ScopeLensEnvironment.");
                    }

                    result = candidate;
                }
            }

            return result;
        }

        private static void BackupAsset(string path, string directory)
        {
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));
            }

            if (File.Exists(path + ".meta"))
            {
                File.Copy(path + ".meta", Path.Combine(directory, Path.GetFileName(path) + ".meta"));
            }
        }
    }
}
