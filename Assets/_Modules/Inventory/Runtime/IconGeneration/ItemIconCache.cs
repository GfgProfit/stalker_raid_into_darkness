using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Inventory.IconGeneration
{
    public static class ItemIconCache
    {
        private static Dictionary<IconCacheKey, IconCacheEntry> _cache = new();
        private static readonly Dictionary<IconCacheKey, UniTaskCompletionSource<Sprite>> _inFlight = new();
        private static readonly Queue<StandaloneGenerationRequest> _standaloneQueue = new();
        private static CancellationTokenSource _generationCancellation = new();
        private static ItemIconRenderSession _standaloneRenderSession;
        private static ItemIconGeneratorSettings _standaloneRenderSettings;
        private static int _standaloneRenderSettingsHash;
        private static bool _standaloneWorkerRunning;
        private static bool _releaseStandaloneRenderSessionWhenWorkerStops;

        public static bool IsPrewarmed { get; private set; }
        public static int CachedCount => _cache.Count;
        public static int GeneratedCount { get; private set; }
        public static bool IsWorking => _standaloneWorkerRunning || _standaloneQueue.Count > 0 || _inFlight.Count > 0;

        public static Sprite GetOrCreate(ItemDefinition definition, IReadOnlyList<string> variantIds = null)
        {
            return definition == null ? null : GetOrCreate(definition, definition.Width, definition.Height, ItemIconProfileType.Default, variantIds);
        }

        public static Sprite GetOrCreate(ItemDefinition definition, int width, int height, ItemIconProfileType profileType, IReadOnlyList<string> variantIds = null)
        {
            if (definition == null)
            {
                return null;
            }

            if (definition.IconOverride != null)
            {
                return definition.IconOverride;
            }

            ItemIconGeneratorSettings settings = ItemIconGeneratorSettings.LoadDefault();
            IconRenderProfile renderProfile = IconRenderProfile.Create(definition, width, height, profileType, settings);
            IconCacheKey key = ItemIconStableKeyBuilder.Build(definition, variantIds, settings, renderProfile);
            return TryGetCachedSprite(key, out Sprite cachedSprite) ? cachedSprite : definition.FallbackIcon;
        }

        public static bool TryGet(ItemDefinition definition, int width, int height, ItemIconProfileType profileType, IReadOnlyList<string> variantIds, out Sprite icon)
        {
            if (definition == null)
            {
                icon = null;
                return true;
            }

            if (definition.IconOverride != null)
            {
                icon = definition.IconOverride;
                return true;
            }

            ItemIconGeneratorSettings settings = ItemIconGeneratorSettings.LoadDefault();
            IconRenderProfile renderProfile = IconRenderProfile.Create(definition, width, height, profileType, settings);
            IconCacheKey key = ItemIconStableKeyBuilder.Build(definition, variantIds, settings, renderProfile);

            if (TryGetCachedSprite(key, out icon))
            {
                return true;
            }

            if (definition.CanGenerateIcon == false)
            {
                icon = definition.FallbackIcon;
                return true;
            }

            return false;
        }

        public static UniTask<Sprite> GetOrCreateAsync(ItemDefinition definition, IReadOnlyList<string> variantIds = null, CancellationToken cancellationToken = default)
        {
            return definition == null ? UniTask.FromResult<Sprite>(null) : GetOrCreateAsync(definition, definition.Width, definition.Height, ItemIconProfileType.Default, variantIds, cancellationToken);
        }

        public static UniTask<Sprite> GetOrCreateAsync(ItemDefinition definition, int width, int height, ItemIconProfileType profileType, IReadOnlyList<string> variantIds = null, CancellationToken cancellationToken = default)
        {
            if (definition == null)
            {
                return UniTask.FromResult<Sprite>(null);
            }

            ItemIconGeneratorSettings settings = ItemIconGeneratorSettings.LoadDefault();
            IconRenderProfile renderProfile = IconRenderProfile.Create(definition, width, height, profileType, settings);
            return GetOrCreateStandaloneAsync(definition, variantIds, settings, renderProfile, cancellationToken);
        }

        public static async UniTask PrewarmAsync(IReadOnlyList<ItemDefinition> definitions, IReadOnlyList<ItemIconSlotProfile> slotProfiles, Action<int, int, ItemDefinition> progressCallback, CancellationToken cancellationToken)
        {
            if (definitions == null)
            {
                return;
            }

            ItemIconGeneratorSettings settings = ItemIconGeneratorSettings.LoadDefault();
            List<PrewarmRequest> requests = BuildPrewarmRequests(BuildUniqueList(definitions), slotProfiles, settings);
            int total = requests.Count;
            float frameBudgetMilliseconds = settings.PrewarmFrameBudgetMilliseconds;
            int budgetFrame = -1;
            var frameTimer = new Stopwatch();
            progressCallback?.Invoke(0, total, null);

            if (total == 0)
            {
                IsPrewarmed = true;
                return;
            }

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            while (_standaloneWorkerRunning || _standaloneQueue.Count > 0)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            using ItemIconRenderSession renderSession = ItemIconRenderer.CreateSession(settings);

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (budgetFrame != Time.frameCount)
                {
                    budgetFrame = Time.frameCount;
                    frameTimer.Restart();
                }

                PrewarmRequest request = requests[i];

                try
                {
                    await GetOrCreateAsync(request, renderSession, true, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    CacheFallbackSprite(request.Key, request.Definition);
                }

                progressCallback?.Invoke(i + 1, total, request.Definition);

                if (budgetFrame == Time.frameCount && frameTimer.Elapsed.TotalMilliseconds >= frameBudgetMilliseconds)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    budgetFrame = Time.frameCount;
                    frameTimer.Restart();
                }
            }

            IsPrewarmed = true;
        }

        public static void Clear()
        {
            IsPrewarmed = false;
            CancellationToken canceledGenerationToken = _generationCancellation.Token;
            _generationCancellation.Cancel();
            _generationCancellation.Dispose();
            _generationCancellation = new CancellationTokenSource();

            foreach (UniTaskCompletionSource<Sprite> completionSource in _inFlight.Values)
            {
                completionSource.TrySetCanceled(canceledGenerationToken);
            }

            _inFlight.Clear();
            _standaloneQueue.Clear();

            if (_standaloneWorkerRunning)
            {
                _releaseStandaloneRenderSessionWhenWorkerStops = true;
            }
            else
            {
                ReleaseStandaloneRenderSession();
            }

            foreach (IconCacheEntry entry in _cache.Values)
            {
                if (entry == null)
                {
                    continue;
                }

                if (entry.OwnsSprite)
                {
                    ItemIconRenderer.DestroyObject(entry.Sprite);
                }

                if (entry.OwnsTexture)
                {
                    ItemIconRenderer.DestroyObject(entry.Texture);
                }
            }

            _cache.Clear();
            GeneratedCount = 0;
        }

        private static async UniTask<Sprite> GetOrCreateStandaloneAsync(ItemDefinition definition, IReadOnlyList<string> variantIds, ItemIconGeneratorSettings settings, IconRenderProfile renderProfile, CancellationToken cancellationToken)
        {
            await UniTask.SwitchToMainThread(cancellationToken);

            if (definition.IconOverride != null)
            {
                return definition.IconOverride;
            }

            int settingsHash = settings.BuildHash();
            IconCacheKey key = ItemIconStableKeyBuilder.Build(definition, variantIds, settings, renderProfile);

            if (TryGetCachedSprite(key, out Sprite cachedSprite))
            {
                return cachedSprite;
            }

            if (definition.CanGenerateIcon == false)
            {
                return definition.FallbackIcon;
            }

            if (_inFlight.TryGetValue(key, out UniTaskCompletionSource<Sprite> existingGeneration))
            {
                return await existingGeneration.Task.AttachExternalCancellation(cancellationToken);
            }

            string[] variant = CopyVariant(variantIds);
            var completionSource = new UniTaskCompletionSource<Sprite>();
            var request = new PrewarmRequest(definition, variant, renderProfile, key);
            _inFlight.Add(key, completionSource);
            _standaloneQueue.Enqueue(new StandaloneGenerationRequest(request, settings, settingsHash, completionSource));
            StartStandaloneWorker();
            return await completionSource.Task.AttachExternalCancellation(cancellationToken);
        }

        private static void StartStandaloneWorker()
        {
            if (_standaloneWorkerRunning)
            {
                return;
            }

            _standaloneWorkerRunning = true;
            ProcessStandaloneQueueAsync().Forget(Debug.LogException);
        }

        private static async UniTask ProcessStandaloneQueueAsync()
        {
            CancellationToken generationCancellation = _generationCancellation.Token;

            try
            {
                while (_standaloneQueue.Count > 0)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, generationCancellation);
                    generationCancellation.ThrowIfCancellationRequested();
                    StandaloneGenerationRequest generationRequest = _standaloneQueue.Dequeue();

                    try
                    {
                        if (_standaloneRenderSession == null || _standaloneRenderSettings != generationRequest.Settings || _standaloneRenderSettingsHash != generationRequest.SettingsHash)
                        {
                            ReleaseStandaloneRenderSession();
                            _standaloneRenderSettings = generationRequest.Settings;
                            _standaloneRenderSettingsHash = generationRequest.SettingsHash;
                            _standaloneRenderSession = ItemIconRenderer.CreateSession(_standaloneRenderSettings);
                        }

                        Sprite sprite = await GetOrCreateAsync(generationRequest.Request, _standaloneRenderSession, false, generationCancellation);
                        generationRequest.CompletionSource.TrySetResult(sprite);
                    }
                    catch (OperationCanceledException)
                    {
                        generationRequest.CompletionSource.TrySetCanceled(generationCancellation);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);

                        if (_standaloneRenderSession == null)
                        {
                            _standaloneRenderSettings = null;
                            _standaloneRenderSettingsHash = 0;
                        }

                        CacheFallbackSprite(generationRequest.Request.Key, generationRequest.Request.Definition);
                        generationRequest.CompletionSource.TrySetResult(generationRequest.Request.Definition.FallbackIcon);
                    }
                    finally
                    {
                        if (_inFlight.TryGetValue(generationRequest.Request.Key, out UniTaskCompletionSource<Sprite> current) && ReferenceEquals(current, generationRequest.CompletionSource))
                        {
                            _inFlight.Remove(generationRequest.Request.Key);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (_releaseStandaloneRenderSessionWhenWorkerStops)
                {
                    ReleaseStandaloneRenderSession();
                    _releaseStandaloneRenderSessionWhenWorkerStops = false;
                }

                _standaloneWorkerRunning = false;

                if (_standaloneQueue.Count > 0)
                {
                    StartStandaloneWorker();
                }
                else
                {
                    ReleaseStandaloneRenderSession();
                }
            }
        }

        private static void ReleaseStandaloneRenderSession()
        {
            _standaloneRenderSession?.Dispose();
            _standaloneRenderSession = null;
            _standaloneRenderSettings = null;
            _standaloneRenderSettingsHash = 0;
        }

        private static async UniTask<Sprite> GetOrCreateAsync(PrewarmRequest request, ItemIconRenderSession renderSession, bool checkInFlight, CancellationToken cancellationToken)
        {
            if (TryGetCachedSprite(request.Key, out Sprite cachedSprite))
            {
                return cachedSprite;
            }

            if (checkInFlight && _inFlight.TryGetValue(request.Key, out UniTaskCompletionSource<Sprite> inFlightGeneration))
            {
                return await inFlightGeneration.Task.AttachExternalCancellation(cancellationToken);
            }

            Texture2D generatedTexture = await ItemIconRenderer.RenderIconTextureAsync(request.Definition, request.VariantIds, request.RenderProfile, renderSession, cancellationToken);

            if (generatedTexture == null)
            {
                CacheFallbackSprite(request.Key, request.Definition);
                return request.Definition.FallbackIcon;
            }

            Sprite generatedSprite = ItemIconTextureProcessor.CreateSprite(request.Definition, generatedTexture, request.RenderProfile);
            _cache[request.Key] = IconCacheEntry.CreateGenerated(generatedSprite, generatedTexture);
            GeneratedCount++;
            return generatedSprite;
        }

        private static List<ItemDefinition> BuildUniqueList(IReadOnlyList<ItemDefinition> definitions)
        {
            var unique = new List<ItemDefinition>();
            var seen = new HashSet<EntityId>();

            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];

                if (definition != null && seen.Add(definition.GetEntityId()))
                {
                    unique.Add(definition);
                }
            }

            return unique;
        }

        private static List<PrewarmRequest> BuildPrewarmRequests(IReadOnlyList<ItemDefinition> definitions, IReadOnlyList<ItemIconSlotProfile> slotProfiles, ItemIconGeneratorSettings settings)
        {
            var requests = new List<PrewarmRequest>();
            var requestKeys = new HashSet<IconCacheKey>();

            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];

                if (definition.CanGenerateIcon == false)
                {
                    continue;
                }

                foreach (string[] variant in ItemIconVariantEnumeration.EnumerateVariants(definition))
                {
                    AddVariantRequests(definition, variant, slotProfiles, settings, requests, requestKeys);
                }
            }

            return requests;
        }

        private static void AddVariantRequests(ItemDefinition definition, string[] variant, IReadOnlyList<ItemIconSlotProfile> slotProfiles, ItemIconGeneratorSettings settings, ICollection<PrewarmRequest> requests, ISet<IconCacheKey> requestKeys)
        {
            Vector2Int sizeDelta = ItemIconVariantEnumeration.CalculateSizeDelta(definition, variant);
            AddRequest(definition, variant, settings, IconRenderProfile.CreateDefault(definition, Mathf.Max(1, definition.Width + sizeDelta.x), Mathf.Max(1, definition.Height + sizeDelta.y), settings), requests, requestKeys);

            if (slotProfiles == null)
            {
                return;
            }

            for (int i = 0; i < slotProfiles.Count; i++)
            {
                ItemIconSlotProfile slotProfile = slotProfiles[i];

                if (slotProfile.Accepts(definition))
                {
                    AddRequest(definition, variant, settings, IconRenderProfile.CreateSlot(definition, slotProfile.Width, slotProfile.Height, settings), requests, requestKeys);
                }
            }
        }

        private static void AddRequest(ItemDefinition definition, string[] variant, ItemIconGeneratorSettings settings, IconRenderProfile renderProfile, ICollection<PrewarmRequest> requests, ISet<IconCacheKey> requestKeys)
        {
            IconCacheKey key = ItemIconStableKeyBuilder.Build(definition, variant, settings, renderProfile);

            if (requestKeys.Add(key))
            {
                requests.Add(new PrewarmRequest(definition, variant, renderProfile, key));
            }
        }

        private static string[] CopyVariant(IReadOnlyList<string> variantIds)
        {
            if (variantIds == null || variantIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = new string[variantIds.Count];

            for (int i = 0; i < variantIds.Count; i++)
            {
                result[i] = variantIds[i];
            }

            return result;
        }

        private static bool TryGetCachedSprite(IconCacheKey key, out Sprite sprite)
        {
            if (_cache.TryGetValue(key, out IconCacheEntry entry) && entry.Sprite != null)
            {
                sprite = entry.Sprite;
                return true;
            }

            BakedItemIconCatalog catalog = BakedItemIconCatalog.LoadDefault();

            if (catalog != null && catalog.TryGetSprite(key, out sprite))
            {
                _cache[key] = IconCacheEntry.CreateExternal(sprite);
                return true;
            }

            sprite = null;
            return false;
        }

        private static void CacheFallbackSprite(IconCacheKey key, ItemDefinition definition)
        {
            if (definition.FallbackIcon != null)
            {
                _cache[key] = IconCacheEntry.CreateFallback(definition.FallbackIcon);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Clear();
            _cache = new Dictionary<IconCacheKey, IconCacheEntry>();
        }

        private readonly struct PrewarmRequest
        {
            public PrewarmRequest(ItemDefinition definition, string[] variantIds, IconRenderProfile renderProfile, IconCacheKey key)
            {
                Definition = definition;
                VariantIds = variantIds;
                RenderProfile = renderProfile;
                Key = key;
            }

            public ItemDefinition Definition { get; }
            public string[] VariantIds { get; }
            public IconRenderProfile RenderProfile { get; }
            public IconCacheKey Key { get; }
        }

        private readonly struct StandaloneGenerationRequest
        {
            public StandaloneGenerationRequest(PrewarmRequest request, ItemIconGeneratorSettings settings, int settingsHash, UniTaskCompletionSource<Sprite> completionSource)
            {
                Request = request;
                Settings = settings;
                SettingsHash = settingsHash;
                CompletionSource = completionSource;
            }

            public PrewarmRequest Request { get; }
            public ItemIconGeneratorSettings Settings { get; }
            public int SettingsHash { get; }
            public UniTaskCompletionSource<Sprite> CompletionSource { get; }
        }
    }

    public sealed class ItemIconCacheProvider : IItemIconProvider
    {
        public bool TryGetIcon(in ItemIconRequest request, out Sprite icon)
        {
            return ItemIconCache.TryGet(request.Definition, request.Width, request.Height, request.ProfileType, request.VariantIds, out icon);
        }

        public void RequestIcon(in ItemIconRequest request, Action<Sprite> onReady, CancellationToken cancellationToken)
        {
            RequestAsync(request, onReady, cancellationToken).Forget();
        }

        private static async UniTaskVoid RequestAsync(ItemIconRequest request, Action<Sprite> onReady, CancellationToken cancellationToken)
        {
            try
            {
                Sprite sprite = await ItemIconCache.GetOrCreateAsync(request.Definition, request.Width, request.Height, request.ProfileType, request.VariantIds, cancellationToken);

                if (cancellationToken.IsCancellationRequested == false)
                {
                    onReady?.Invoke(sprite);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                onReady?.Invoke(request.Definition != null ? request.Definition.FallbackIcon : null);
            }
        }
    }
}
