using RaidIntoDarkness.Inventory.Domain;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace RaidIntoDarkness.Inventory.Infrastructure.IconGeneration
{
    internal sealed class ItemIconRenderSession : IDisposable
    {
        private readonly ItemIconGeneratorSettings _settings;
        private readonly GameObject _cameraObject;
        private readonly Camera _renderCamera;
        private readonly GameObject _volumeObject;
        private readonly Volume _volume;
        private readonly VolumeProfile _volumeProfile;
        private readonly Light[] _lights;
        private readonly ItemIconSceneIsolationScope _sceneIsolation;
        private readonly List<string> _effectiveVariant = new();

        private ItemDefinition _currentDefinition;
        private GameObject _rootObject;
        private Renderer[] _renderers = Array.Empty<Renderer>();
        private bool _requiresExposureWarmup;
        private bool _disposed;

        public ItemIconRenderSession(ItemIconGeneratorSettings settings)
        {
            _settings = settings;
            _sceneIsolation = new ItemIconSceneIsolationScope();
            _volumeObject = ItemIconVolumeFactory.Create("Shared Runtime Item Icons", settings, out _volumeProfile);
            _volume = _volumeObject.GetComponent<Volume>();
            _volume.enabled = false;
            _cameraObject = new GameObject("Shared Runtime Item Icon Camera", typeof(Camera))
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            _renderCamera = _cameraObject.GetComponent<Camera>();
            _renderCamera.enabled = false;
            HDAdditionalCameraData hdCameraData = _cameraObject.AddComponent<HDAdditionalCameraData>();
            ItemIconCameraConfigurator.ConfigureHdCamera(hdCameraData, settings);
            _lights = ItemIconLightFactory.CreateReusable(settings);
        }

        public ItemIconGeneratorSettings Settings => _settings;
        public Camera RenderCamera => _renderCamera;
        public bool IsSceneIsolated => _sceneIsolation.IsApplied;

        public async UniTask<RawIconRenderResult> RenderRawIconAsync(ItemDefinition definition, IReadOnlyList<string> variantIds, IconRenderProfile renderProfile, CancellationToken cancellationToken)
        {
            if (_disposed || definition == null || definition.CanGenerateIcon == false)
            {
                return null;
            }

            EnsureItemVisual(definition);

            if (_rootObject == null)
            {
                return null;
            }

            GameObject root = _rootObject;
            RenderTexture renderTexture = null;
            RenderTexture readbackTexture = null;
            int antiAliasing = definition.Icon.AntiAliasing;

            try
            {
                root.transform.SetPositionAndRotation(_settings.RenderOrigin, Quaternion.Euler(renderProfile.ModelEulerAngles));
                root.transform.localScale = renderProfile.ModelScale;
                root.SetActive(true);
                BuildEffectiveVariant(definition, variantIds);
                ItemIconVisualConfigurators.Apply(root, definition, _effectiveVariant);
                ItemIconSceneBuilder.DisableLocalEnvironment(root);

                if (ItemIconSceneBuilder.TryCalculateBounds(_renderers, root.transform.position, out Bounds bounds) == false)
                {
                    return null;
                }

                int textureWidth = renderProfile.TextureWidth;
                int textureHeight = renderProfile.TextureHeight;
                renderTexture = RenderTexture.GetTemporary(textureWidth, textureHeight, 24, _settings.RenderTextureFormat, RenderTextureReadWrite.Default, antiAliasing);
                renderTexture.filterMode = FilterMode.Bilinear;
                if (_requiresExposureWarmup)
                {
                    RenderFrame(definition, bounds, renderTexture, renderProfile, antiAliasing);
                    _requiresExposureWarmup = false;
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                RenderFrame(definition, bounds, renderTexture, renderProfile, antiAliasing);
                readbackTexture = PrepareReadbackTexture(renderTexture, textureWidth, textureHeight);
                AsyncGPUReadbackRequest readbackRequest = AsyncGPUReadback.Request(readbackTexture, 0, TextureFormat.RGBA32);

                while (readbackRequest.done == false)
                {
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate);
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (readbackRequest.hasError)
                {
                    Debug.LogError($"Asynchronous GPU readback failed while generating the icon for '{definition.name}'.", definition);
                    return null;
                }

                NativeArray<Color32> readbackData = readbackRequest.GetData<Color32>();
                Color32[] itemPixels = readbackData.ToArray();

                if (ItemIconTextureProcessor.HasVisiblePixels(itemPixels) == false)
                {
                    return null;
                }

                var texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    name = $"{definition.name} Runtime Icon Texture"
                };

                return new RawIconRenderResult(texture, itemPixels, textureWidth, textureHeight);
            }
            finally
            {
                if (root != null && root.activeSelf)
                {
                    root.SetActive(false);
                }

                if (renderTexture != null)
                {
                    RenderTexture.ReleaseTemporary(renderTexture);
                }

                if (readbackTexture != null && readbackTexture != renderTexture)
                {
                    RenderTexture.ReleaseTemporary(readbackTexture);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseCurrentItemVisual();

            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] != null)
                {
                    ItemIconRenderer.DestroyObject(_lights[i].gameObject);
                }
            }

            ItemIconRenderer.DestroyObject(_cameraObject);
            ItemIconRenderer.DestroyObject(_volumeObject);

            for (int i = 0; i < _volumeProfile.components.Count; i++)
            {
                ItemIconRenderer.DestroyObject(_volumeProfile.components[i]);
            }

            _volumeProfile.components.Clear();
            ItemIconRenderer.DestroyObject(_volumeProfile);
        }

        private void EnsureItemVisual(ItemDefinition definition)
        {
            if (_currentDefinition == definition && _rootObject != null)
            {
                return;
            }

            ReleaseCurrentItemVisual();
            _currentDefinition = definition;
            _requiresExposureWarmup = RequiresExposureWarmup();
            _rootObject = new GameObject($"Runtime Icon Root - {definition.name}")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            GameObject iconInstance = ItemIconSceneBuilder.InstantiateSource(definition.VisualPrefab, _rootObject.transform);

            if (iconInstance == null)
            {
                ReleaseCurrentItemVisual();
                return;
            }

            _renderers = _rootObject.GetComponentsInChildren<Renderer>(true);
            ItemIconSceneBuilder.SetLayerRecursively(_rootObject, _settings.RenderLayer);
            ItemIconSceneBuilder.SetRendererIsolation(_renderers, _settings);
            ItemIconSceneBuilder.DisableLocalEnvironment(_rootObject);
        }

        private void ReleaseCurrentItemVisual()
        {
            _currentDefinition = null;
            _renderers = Array.Empty<Renderer>();

            if (_rootObject == null)
            {
                return;
            }

            ItemIconRenderer.DestroyObject(_rootObject);
            _rootObject = null;
        }

        private void BuildEffectiveVariant(ItemDefinition definition, IReadOnlyList<string> variantIds)
        {
            _effectiveVariant.Clear();
            AddUnique(ItemIconVariantKeyCache.ToIds(definition.DefaultIconVariant));
            AddUnique(variantIds);
        }

        private void AddUnique(IReadOnlyList<string> ids)
        {
            if (ids == null)
            {
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (string.IsNullOrEmpty(ids[i]) == false && _effectiveVariant.Contains(ids[i]) == false)
                {
                    _effectiveVariant.Add(ids[i]);
                }
            }
        }

        private void RenderFrame(ItemDefinition definition, Bounds bounds, RenderTexture renderTexture, IconRenderProfile renderProfile, int antiAliasing)
        {
            GameObject root = _rootObject;
            try
            {
                if (root.activeSelf == false)
                {
                    root.SetActive(true);
                }

                _sceneIsolation.Apply(_settings.ExcludeIconLayerFromSceneLights, root.transform, _settings.RenderLayer);
                _volume.enabled = true;
                ItemIconCameraConfigurator.ConfigureRenderCamera(_renderCamera, bounds, renderTexture, _settings, renderProfile, antiAliasing);

                if (renderProfile.UseDirectionalLight)
                {
                    ItemIconLightFactory.Configure(_lights, definition, _renderCamera.transform, _settings, renderProfile);
                }

                ClearRenderTexture(renderTexture, true);
                _renderCamera.Render();
            }
            finally
            {
                _renderCamera.targetTexture = null;
                _volume.enabled = false;
                _sceneIsolation.Restore();
                ItemIconLightFactory.Disable(_lights);
                root.SetActive(false);
            }
        }

        private RenderTexture PrepareReadbackTexture(RenderTexture renderTexture, int width, int height)
        {
            if (renderTexture.antiAliasing <= 1)
            {
                return renderTexture;
            }

            RenderTexture resolvedTexture = RenderTexture.GetTemporary(width, height, 0, _settings.RenderTextureFormat, RenderTextureReadWrite.Default, 1);
            resolvedTexture.filterMode = FilterMode.Bilinear;
            ClearRenderTexture(resolvedTexture, false);
            Graphics.Blit(renderTexture, resolvedTexture);
            return resolvedTexture;
        }

        private bool RequiresExposureWarmup()
        {
            return _settings.EnableExposureControl && (_settings.ExposureMode == ExposureMode.Automatic || _settings.ExposureMode == ExposureMode.AutomaticHistogram || _settings.ExposureMode == ExposureMode.CurveMapping);
        }

        private static void ClearRenderTexture(RenderTexture renderTexture, bool clearDepth)
        {
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                RenderTexture.active = renderTexture;
                GL.Clear(clearDepth, true, Color.clear);
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }
    }
}
