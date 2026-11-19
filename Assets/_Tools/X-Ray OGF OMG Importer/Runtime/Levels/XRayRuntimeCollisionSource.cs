using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using XRay.Importer.Core.Level;

namespace XRay.Importer.Levels
{
    public static class XRayRuntimeCollisionSource
    {
        private static ConditionalWeakTable<TextAsset, XRayCollisionModel> _cache = new ConditionalWeakTable<TextAsset, XRayCollisionModel>();

        public static XRayCollisionModel Load(TextAsset source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return Application.isPlaying ? _cache.GetValue(source, Parse) : Parse(source);
        }

        internal static XRayCollisionModel GetCached(TextAsset source)
        {
            return _cache.TryGetValue(source, out XRayCollisionModel collision) ? collision : null;
        }

        internal static void Cache(TextAsset source, XRayCollisionModel collision)
        {
            if (!_cache.TryGetValue(source, out _)) _cache.Add(source, collision);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _cache = new ConditionalWeakTable<TextAsset, XRayCollisionModel>();
        }

        private static XRayCollisionModel Parse(TextAsset source)
        {
            XRayCollisionModel collision = XRayCollisionModel.Read(source.bytes, source.name + " level.cform");
            collision.BuildGrid(XRayDetails.SLOT_SIZE);
            return collision;
        }
    }
}
