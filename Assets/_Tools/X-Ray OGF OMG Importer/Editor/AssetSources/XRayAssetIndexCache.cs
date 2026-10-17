using System.IO;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor
{
    public static class XRayAssetIndexCache
    {
        public const string CACHE_FOLDER = "Library/XRayImporter";
        public const string CACHE_PATH = CACHE_FOLDER + "/AssetIndex.bin";

        private static XRayAssetIndex _index;

        public static XRayAssetIndex Current => _index;
        public static XRaySourcePlan LastPlan { get; private set; }

        public static XRayAssetIndex GetOrBuild(XRayAssetSourceOptions options)
        {
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(options.Sources);
            LastPlan = plan;
            if (!plan.IsValid)
            {
                return null;
            }

            if (_index != null && _index.Fingerprint == plan.Fingerprint)
            {
                return _index;
            }

            if (options.PersistIndexCache)
            {
                XRayAssetIndex cached = XRayAssetIndex.Load(CACHE_PATH);
                if (cached != null && cached.Fingerprint == plan.Fingerprint)
                {
                    _index = cached;
                    return _index;
                }
            }

            return Build(plan, options.PersistIndexCache);
        }

        public static XRayAssetIndex Rebuild(XRayAssetSourceOptions options)
        {
            XRaySourcePlan plan = Mo2VirtualFileSystem.Plan(options.Sources);
            LastPlan = plan;
            return plan.IsValid ? Build(plan, options.PersistIndexCache) : null;
        }

        public static void Clear()
        {
            _index = null;
            LastPlan = null;
            if (File.Exists(CACHE_PATH))
            {
                File.Delete(CACHE_PATH);
            }
        }

        private static XRayAssetIndex Build(XRaySourcePlan plan, bool persist)
        {
            _index = XRayAssetIndex.Build(plan);
            if (persist)
            {
                _index.Save(CACHE_PATH);
            }

            return _index;
        }
    }
}
