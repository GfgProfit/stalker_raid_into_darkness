using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.IconGeneration
{
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
