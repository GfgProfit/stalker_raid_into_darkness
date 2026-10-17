using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace XRay.Importer.Editor
{
    [Serializable]
    public sealed class XRayPendingGeneration
    {
        [SerializeField] private string _modelPath;
        [SerializeField] private bool _explicitRefresh;
        [SerializeField] private int _attempts;
        [SerializeField] private List<string> _awaitedAssets = new List<string>();

        public XRayPendingGeneration(string modelPath, bool explicitRefresh)
        {
            _modelPath = modelPath;
            _explicitRefresh = explicitRefresh;
        }

        public string ModelPath => _modelPath;
        public List<string> AwaitedAssets => _awaitedAssets;

        public bool ExplicitRefresh
        {
            get => _explicitRefresh;
            set => _explicitRefresh = value;
        }

        public int Attempts
        {
            get => _attempts;
            set => _attempts = value;
        }
    }

    [Serializable]
    public sealed class XRayPendingGenerationList
    {
        [SerializeField] private List<XRayPendingGeneration> _items = new List<XRayPendingGeneration>();

        public List<XRayPendingGeneration> Items => _items;
    }

    public static class XRayPendingGenerationStore
    {
        public const string PATH = XRayAssetIndexCache.CACHE_FOLDER + "/PendingGeneration.json";

        public static List<XRayPendingGeneration> Load()
        {
            if (!File.Exists(PATH))
            {
                return new List<XRayPendingGeneration>();
            }

            try
            {
                XRayPendingGenerationList list = JsonUtility.FromJson<XRayPendingGenerationList>(File.ReadAllText(PATH));
                return list != null ? list.Items : new List<XRayPendingGeneration>();
            }
            catch (ArgumentException)
            {
                return new List<XRayPendingGeneration>();
            }
        }

        public static XRayPendingGeneration Find(string modelPath)
        {
            return Load().Find(item => item.ModelPath == modelPath);
        }

        public static void Set(XRayPendingGeneration pending)
        {
            List<XRayPendingGeneration> items = Load();
            items.RemoveAll(item => item.ModelPath == pending.ModelPath);
            items.Add(pending);
            Save(items);
        }

        public static void Remove(string modelPath)
        {
            List<XRayPendingGeneration> items = Load();
            if (items.RemoveAll(item => item.ModelPath == modelPath) > 0)
            {
                Save(items);
            }
        }

        public static void Clear()
        {
            if (File.Exists(PATH))
            {
                File.Delete(PATH);
            }
        }

        private static void Save(List<XRayPendingGeneration> items)
        {
            Directory.CreateDirectory(XRayAssetIndexCache.CACHE_FOLDER);
            var list = new XRayPendingGenerationList();
            list.Items.AddRange(items);
            File.WriteAllText(PATH, JsonUtility.ToJson(list, true));
        }
    }
}
