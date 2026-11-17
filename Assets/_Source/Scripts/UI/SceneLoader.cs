using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RaidIntoDarkness.UI
{
    internal static class SceneLoader
    {
        public static bool IsLoading { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            IsLoading = false;
        }

        public static bool CanLoad(string scenePath)
        {
            return SceneUtility.GetBuildIndexByScenePath(scenePath) >= 0;
        }

        public static async UniTask LoadAsync(string scenePath, CancellationToken sourceLifetime)
        {
            if (IsLoading)
            {
                return;
            }
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
            if (buildIndex < 0)
            {
                Debug.LogError($"Scene is missing from the active build: '{scenePath}'.");
                return;
            }
            var applicationLifetime = Application.exitCancellationToken;
            IsLoading = true;
            try
            {
                await UniTask.NextFrame(cancellationToken: sourceLifetime);
                applicationLifetime.ThrowIfCancellationRequested();
                var operation = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
                if (operation == null)
                {
                    Debug.LogError($"Could not start loading scene '{scenePath}'.");
                    return;
                }
                await operation.ToUniTask(cancellationToken: applicationLifetime);
            }
            catch (OperationCanceledException) when (sourceLifetime.IsCancellationRequested || applicationLifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not load scene '{scenePath}': {exception}");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
