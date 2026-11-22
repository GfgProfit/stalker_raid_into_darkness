using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Inventory.IconGeneration
{
    public static class ItemIconRenderer
    {
        internal static ItemIconRenderSession CreateSession(ItemIconGeneratorSettings settings)
        {
            return new ItemIconRenderSession(settings ?? ItemIconGeneratorSettings.LoadDefault());
        }

        public static async UniTask<Texture2D> RenderPreviewTextureAsync(ItemDefinition definition, IReadOnlyList<string> variantIds = null, int width = 0, int height = 0, ItemIconProfileType profileType = ItemIconProfileType.Default, ItemIconGeneratorSettings settings = null, CancellationToken cancellationToken = default)
        {
            if (definition == null || definition.CanGenerateIcon == false)
            {
                return null;
            }

            await UniTask.SwitchToMainThread(cancellationToken);
            ItemIconGeneratorSettings resolvedSettings = settings ?? ItemIconGeneratorSettings.LoadDefault();
            using ItemIconRenderSession renderSession = CreateSession(resolvedSettings);
            int resolvedWidth = width > 0 ? width : definition.Width;
            int resolvedHeight = height > 0 ? height : definition.Height;
            IconRenderProfile profile = IconRenderProfile.Create(definition, resolvedWidth, resolvedHeight, profileType, resolvedSettings);
            return await RenderIconTextureAsync(definition, variantIds ?? Array.Empty<string>(), profile, renderSession, true, cancellationToken);
        }

        internal static async UniTask<Texture2D> RenderIconTextureAsync(ItemDefinition definition, IReadOnlyList<string> variantIds, IconRenderProfile renderProfile, ItemIconRenderSession renderSession, CancellationToken cancellationToken)
        {
            return await RenderIconTextureAsync(definition, variantIds, renderProfile, renderSession, true, cancellationToken);
        }

        internal static async UniTask<Texture2D> RenderIconTextureAsync(ItemDefinition definition, IReadOnlyList<string> variantIds, IconRenderProfile renderProfile, ItemIconRenderSession renderSession, bool makeNoLongerReadable, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.SwitchToMainThread(cancellationToken);
            RawIconRenderResult rawResult = await renderSession.RenderRawIconAsync(definition, variantIds, renderProfile, cancellationToken);

            if (rawResult == null)
            {
                return null;
            }

            try
            {
                ItemIconPostProcessSettings postProcessSettings = ItemIconTextureProcessor.CreatePostProcessSettings(definition, renderProfile, renderSession.Settings);
                Color32[] processedPixels = await UniTask.RunOnThreadPool(() => ItemIconTextureProcessor.CreateProcessedPixels(rawResult.ItemPixels, rawResult.Width, rawResult.Height, postProcessSettings), true, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                rawResult.Texture.SetPixels32(processedPixels);
                rawResult.Texture.Apply(false, makeNoLongerReadable);
                return rawResult.Texture;
            }
            catch
            {
                await UniTask.SwitchToMainThread();
                DestroyObject(rawResult.Texture);
                throw;
            }
        }

        public static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (target is GameObject gameObject)
            {
                gameObject.SetActive(false);
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }

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

            _rootObject.transform.SetPositionAndRotation(_settings.RenderOrigin, Quaternion.Euler(renderProfile.ModelEulerAngles));
            _rootObject.transform.localScale = renderProfile.ModelScale;
            BuildEffectiveVariant(definition, variantIds);
            ItemIconVisualConfigurators.Apply(_rootObject, definition, _effectiveVariant);
            ItemIconSceneBuilder.DisableLocalEnvironment(_rootObject);

            if (ItemIconSceneBuilder.TryCalculateBounds(_renderers, _rootObject.transform.position, out Bounds bounds) == false)
            {
                return null;
            }

            RenderTexture renderTexture = null;
            RenderTexture readbackTexture = null;
            int antiAliasing = definition.Icon.AntiAliasing;

            try
            {
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
            try
            {
                _sceneIsolation.Apply(_settings.ExcludeIconLayerFromSceneLights, _rootObject.transform, _settings.RenderLayer);
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

    public sealed class ItemIconBatchRenderSession : IDisposable
    {
        private readonly ItemIconGeneratorSettings _settings;
        private readonly ItemIconRenderSession _renderSession;

        public ItemIconBatchRenderSession(ItemIconGeneratorSettings settings)
        {
            _settings = settings ?? ItemIconGeneratorSettings.LoadDefault();
            _renderSession = ItemIconRenderer.CreateSession(_settings);
        }

        public UniTask<Texture2D> RenderAsync(ItemDefinition definition, IReadOnlyList<string> variantIds, int width, int height, ItemIconProfileType profileType, CancellationToken cancellationToken)
        {
            IconRenderProfile renderProfile = IconRenderProfile.Create(definition, width, height, profileType, _settings);
            return ItemIconRenderer.RenderIconTextureAsync(definition, variantIds, renderProfile, _renderSession, false, cancellationToken);
        }

        public void Dispose() => _renderSession.Dispose();
    }
}
