using System.IO;
using UnityEngine;

namespace XRay.Importer.Levels
{
    [DisallowMultipleComponent]
    public sealed class XRayGrassSettingsProvider : MonoBehaviour
    {
        public static XRayGrassSettingsProvider Active { get; private set; }

        public XRayGrassSettings Settings { get; private set; }

        public void Initialize(XRayGrassSettings settings)
        {
            if (Active == this) throw new System.InvalidOperationException("Grass settings are already initialized.");
            Settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
        }

        public static XRayGrassSettingsProvider Ensure()
        {
            if (Active != null) return Active;
            var existing = FindFirstObjectByType<XRayGrassSettingsProvider>();
            return existing != null ? existing : new GameObject("XRay Grass Settings").AddComponent<XRayGrassSettingsProvider>();
        }

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(gameObject);
                return;
            }

            TextAsset profile = Resources.Load<TextAsset>("XRayRvmGrassDefaults");
            if (profile == null)
            {
                Debug.LogError("DEFAULT INPUT BLOCKED: XRayRvmGrassDefaults is missing.");
                enabled = false;
                return;
            }

            if (Settings == null) Settings = new XRayGrassSettings(XRayGrassDefaults.Load(profile.text), Path.Combine(Application.persistentDataPath, "XRayGrassSettings.json"));
            Active = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }
    }
}
