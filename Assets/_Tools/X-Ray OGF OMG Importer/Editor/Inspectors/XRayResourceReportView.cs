using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor
{
    public sealed class XRayResourceReportView
    {
        private readonly HashSet<int> _expanded = new HashSet<int>();
        private bool _show = true;

        public void Draw(XRayOgfImporter importer, bool hasUnappliedChanges)
        {
            EditorGUILayout.Space();
            _show = EditorGUILayout.Foldout(_show, "X-Ray Resource Resolution", true, EditorStyles.foldoutHeader);
            if (!_show)
            {
                return;
            }

            string path = importer.assetPath;
            XRayResourceReport report = XRayResourceReportStore.Load(AssetDatabase.AssetPathToGUID(path));
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(hasUnappliedChanges || !importer.AutoResolveResources))
            {
                if (GUILayout.Button(new GUIContent("Resolve Resources", "Resolve textures again, update changed sources and assign materials.")))
                {
                    EditorApplication.delayCall += () => XRayAssetGenerationCoordinator.Refresh(path, out XRayResourceReport _);
                }
            }

            if (GUILayout.Button("Asset Sources..."))
            {
                XRayAssetSourcesWindow.Open();
            }

            EditorGUILayout.EndHorizontal();
            if (hasUnappliedChanges)
            {
                EditorGUILayout.HelpBox("Apply the import settings before resolving resources.", MessageType.None);
            }

            if (report == null)
            {
                EditorGUILayout.HelpBox(importer.AutoResolveResources ? "No resolution report yet. It is created after the model is imported with configured asset sources." : "Resource resolution is disabled for this model.", MessageType.Info);
                return;
            }

            int textures = 0;
            foreach (XRayReportMaterial material in report.Materials)
            {
                textures += material.Textures.Count;
            }

            EditorGUILayout.LabelField("Model", Path.GetFileName(report.ModelPath));
            EditorGUILayout.LabelField("Sources", string.IsNullOrEmpty(report.Profile) ? report.SourceMode : $"{report.SourceMode}, profile '{report.Profile}'");
            EditorGUILayout.LabelField("Materials", report.Materials.Count.ToString());
            EditorGUILayout.LabelField("Textures Resolved", $"{report.CountResolvedTextures()} / {textures}");
            EditorGUILayout.LabelField("Missing Textures", report.CountTextures(XRayResourceStatus.Missing).ToString());
            if (!report.IsComplete)
            {
                EditorGUILayout.HelpBox("Generation is not complete (waiting for texture import or failed).", MessageType.Warning);
            }

            foreach (string error in report.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            foreach (string warning in report.Warnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            foreach (XRayReportMaterial material in report.Materials)
            {
                DrawMaterial(material);
            }
        }

        private void DrawMaterial(XRayReportMaterial material)
        {
            bool expanded = _expanded.Contains(material.Index);
            bool next = EditorGUILayout.Foldout(expanded, $"[{material.Index:00}] {material.Texture}   ({material.MaterialStatus})", true);
            if (next != expanded)
            {
                if (next)
                {
                    _expanded.Add(material.Index);
                }
                else
                {
                    _expanded.Remove(material.Index);
                }
            }

            if (!next)
            {
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Shader", $"{material.Shader} -> {material.SurfaceKind} ({material.SurfaceRule})");
            DrawAssetField("Material", material.MaterialPath);
            if (!string.IsNullOrEmpty(material.DescriptionOrigin))
            {
                EditorGUILayout.LabelField("Texture Description", material.DescriptionOrigin);
            }

            EditorGUILayout.LabelField("Meshes", string.Join(", ", material.Meshes));
            foreach (XRayReportTexture texture in material.Textures)
            {
                DrawTexture(texture);
            }

            foreach (string note in material.Notes)
            {
                EditorGUILayout.HelpBox(note, MessageType.None);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawTexture(XRayReportTexture texture)
        {
            EditorGUILayout.LabelField(texture.Role, $"{texture.Requested ?? "-"}   [{texture.Status}]", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            if (texture.Winner != null)
            {
                EditorGUILayout.LabelField("Source", $"{texture.Winner.Name} (priority {texture.Winner.Priority})");
                EditorGUILayout.SelectableLabel(texture.Winner.FullPath, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                foreach (XRayReportSource loser in texture.Losers)
                {
                    EditorGUILayout.LabelField("Overridden", $"{loser.Name} (priority {loser.Priority})", EditorStyles.miniLabel);
                }
            }

            if (!string.IsNullOrEmpty(texture.Format))
            {
                EditorGUILayout.LabelField("Format", texture.Format);
            }

            DrawAssetField("Asset", texture.AssetPath);
            if (!string.IsNullOrEmpty(texture.Note))
            {
                EditorGUILayout.LabelField(" ", texture.Note, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawAssetField(string label, string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(label, asset, typeof(Object), false);
            }
        }
    }
}
