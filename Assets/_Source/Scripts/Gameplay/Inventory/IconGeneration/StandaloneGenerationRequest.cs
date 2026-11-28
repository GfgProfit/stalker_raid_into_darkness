using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.IconGeneration
{
    internal readonly struct StandaloneGenerationRequest
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
