using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace XRay.Importer.Editor
{
    public sealed class XRayAssetPostprocessor : AssetPostprocessor
    {
        private const string ATTEMPT_KEY_PREFIX = "XRayImporter.Generation.";

        private static readonly List<string> _deferredModels = new List<string>();
        private static bool _isProcessing;

        internal static int ProcessedModelCount { get; set; }

        public override int GetPostprocessOrder()
        {
            return 1000;
        }

        private void OnPreprocessAsset()
        {
            if (XRayTextureImportQueue.TryPeek(assetPath, out XRayTextureImportRequest _))
            {
                XRayTextureImportQueue.Apply(assetImporter, assetPath);
            }
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        {
            var models = new List<string>();
            foreach (string path in importedAssets)
            {
                if (path.EndsWith(".ogf", StringComparison.OrdinalIgnoreCase) && !models.Contains(path))
                {
                    models.Add(path);
                }
            }

            var imported = new HashSet<string>(importedAssets);
            foreach (XRayPendingGeneration pending in XRayPendingGenerationStore.Load())
            {
                if (!models.Contains(pending.ModelPath) && (didDomainReload || pending.AwaitedAssets.Exists(imported.Contains)))
                {
                    models.Add(pending.ModelPath);
                }
            }

            if (models.Count == 0)
            {
                return;
            }

            if (_isProcessing)
            {
                _deferredModels.AddRange(models);
                return;
            }

            _isProcessing = true;
            try
            {
                ProcessModels(models);
                while (_deferredModels.Count > 0)
                {
                    var deferred = new List<string>(_deferredModels);
                    _deferredModels.Clear();
                    ProcessModels(deferred);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private static void ProcessModels(List<string> models)
        {
            XRayAssetSourceOptions options = XRayAssetSourceSettings.GetEffective();
            foreach (string model in models)
            {
                ProcessedModelCount++;
                if (AssetImporter.GetAtPath(model) == null)
                {
                    XRayPendingGenerationStore.Remove(model);
                    continue;
                }

                XRayPendingGeneration pending = XRayPendingGenerationStore.Find(model);
                bool explicitRefresh = pending != null && pending.ExplicitRefresh;
                string attemptKey = ATTEMPT_KEY_PREFIX + AssetDatabase.AssetPathToGUID(model);
                if (!explicitRefresh && SessionState.GetInt(attemptKey, 0) > XRayAssetGenerationCoordinator.MAX_ATTEMPTS)
                {
                    continue;
                }

                try
                {
                    XRayGenerationOutcome outcome = XRayAssetGenerationCoordinator.Process(model, options, explicitRefresh, out XRayResourceReport _);
                    if (outcome == XRayGenerationOutcome.Completed)
                    {
                        int attempts = SessionState.GetInt(attemptKey, 0) + 1;
                        SessionState.SetInt(attemptKey, attempts);
                        if (attempts > XRayAssetGenerationCoordinator.MAX_ATTEMPTS)
                        {
                            Debug.LogError($"[X-Ray] {model}: resource generation repeated {attempts} times in this session; automatic generation is suspended for this model to prevent an import loop. Use 'Resolve Resources' in the importer inspector.");
                        }
                    }
                    else if (outcome == XRayGenerationOutcome.NothingToDo)
                    {
                        SessionState.EraseInt(attemptKey);
                    }
                }
                catch (Exception exception)
                {
                    XRayPendingGenerationStore.Remove(model);
                    Debug.LogError($"[X-Ray] {model}: resource generation failed: {exception}");
                }
            }
        }

        internal static void ResetAttempts(string modelPath)
        {
            SessionState.EraseInt(ATTEMPT_KEY_PREFIX + AssetDatabase.AssetPathToGUID(modelPath));
        }
    }
}
