using System.Collections.Generic;
using UnityEngine;
using XRay.Importer.Core.Level;
using XRay.Importer.Levels;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Editor.Levels
{
    public sealed class XRayLevelSceneContent
    {
        public string LevelName { get; set; }
        public XRayLevelData Level { get; set; }
        public string SourceFolder { get; set; }
        public Bounds Bounds { get; set; }
        public List<XRayLevelChunkRenderer> Chunks { get; } = new List<XRayLevelChunkRenderer>();
        public XRayInstanceBatchSet Instances { get; set; }
        public XRayDetailDatabase Details { get; set; }
        public XRaySourcePlan DetailSettingsSource { get; set; }
        public bool FloraWindAccepted { get; set; }
        public XRayWeatherProfile WeatherProfile { get; set; }
        public XRayWeatherCatalog WeatherCatalog { get; set; }
        public bool InheritWeather { get; set; }
        public List<XRayLevelCollisionChunk> Collision { get; } = new List<XRayLevelCollisionChunk>();
        public TextAsset ContactCollision { get; set; }
        public TextAsset ContactMaterials { get; set; }
        public bool ImportLights { get; set; }
        public bool LightShadows { get; set; }
        public bool OpenScene { get; set; }
    }
}
