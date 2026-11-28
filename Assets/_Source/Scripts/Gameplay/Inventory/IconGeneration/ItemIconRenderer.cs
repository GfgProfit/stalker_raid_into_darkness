using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.IconGeneration
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
}
