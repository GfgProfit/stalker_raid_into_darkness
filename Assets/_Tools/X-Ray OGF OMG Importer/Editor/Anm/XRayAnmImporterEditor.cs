using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using XRay.Importer.Core.Anm;

namespace XRay.Importer.Editor
{
    [CustomEditor(typeof(XRayAnmImporter))]
    public sealed class XRayAnmImporterEditor : ScriptedImporterEditor
    {
        private static readonly string[] SETTINGS = { "_importAnimation", "_createPreviewObject", "_scale", "_preserveOriginalCurves", "_generateAnimationClip", "_samplingMode", "_semantics", "_loop", "_loopPose", "_createCameraPreview", "_previewFieldOfView" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);
            foreach (string setting in SETTINGS)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(setting));
            }

            serializedObject.ApplyModifiedProperties();
            DrawInfo(((XRayAnmImporter)target).assetPath);
            ApplyRevertGUI();
        }

        private static void DrawInfo(string assetPath)
        {
            XRayAnmAsset asset = AssetDatabase.LoadAssetAtPath<XRayAnmAsset>(assetPath);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Information", EditorStyles.boldLabel);
            if (asset == null || asset.Channels.Count != XRayAnmFormat.CHANNEL_COUNT)
            {
                EditorGUILayout.HelpBox("No motion information. The last import failed; see the Console for the reason.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Animation Name", asset.MotionName);
            EditorGUILayout.LabelField("ANM Version", asset.Version.ToString());
            EditorGUILayout.LabelField("FPS", asset.Fps.ToString("0.###"));
            EditorGUILayout.LabelField("Start Frame", asset.FrameStart.ToString());
            EditorGUILayout.LabelField("End Frame", asset.FrameEnd.ToString());
            EditorGUILayout.LabelField("Duration", $"{asset.Duration:0.###} s");
            EditorGUILayout.LabelField("Curves", $"{asset.Channels.Count} (original curves {(asset.HasCurves ? "preserved" : "not preserved")})");
            XRayAnmChannel[] order = XRayAnmFormat.GetFileOrder((ushort)asset.Version, asset.Semantics);
            for (int i = 0; i < asset.Channels.Count; i++)
            {
                XRayAnmChannelData channel = asset.Channels[i];
                EditorGUILayout.LabelField(order[i].ToString(), $"{channel.KeyCount} keys, {channel.Shapes}, {channel.PreBehavior}/{channel.PostBehavior}");
            }

            foreach (string warning in asset.Warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            if (GUILayout.Button("Create Preview In Scene"))
            {
                CreatePreview(assetPath, asset);
            }
        }

        private static void CreatePreview(string assetPath, XRayAnmAsset asset)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            GameObject instance = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : XRayAnmPreviewBuilder.Build(asset, true, 67.5f);
            Undo.RegisterCreatedObjectUndo(instance, "Create ANM Preview");
            Selection.activeGameObject = instance;
        }
    }
}
