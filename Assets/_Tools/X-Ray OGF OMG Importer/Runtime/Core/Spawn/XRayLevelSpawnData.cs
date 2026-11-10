using System;
using System.Collections.Generic;

namespace XRay.Importer.Core.Spawn
{
    public sealed class XRayLevelSpawnData
    {
        public uint Version { get; internal set; }
        public byte GraphVersion { get; internal set; }
        public uint ObjectCount { get; internal set; }
        public uint LevelCount { get; internal set; }
        public Guid Guid { get; internal set; }
        public Guid GraphGuid { get; internal set; }
        public List<XRaySpawnLevel> Levels { get; } = new List<XRaySpawnLevel>();
        public List<XRaySpawnAnomaly> Anomalies { get; } = new List<XRaySpawnAnomaly>();
    }

    public sealed class XRaySpawnLevel
    {
        public byte Id { get; internal set; }
        public string Name { get; internal set; }
        public string Section { get; internal set; }
        public XVector3 Offset { get; internal set; }
        public Guid Guid { get; internal set; }
    }

    public enum XRaySpawnShapeType
    {
        Sphere,
        Box
    }

    public sealed class XRaySpawnShape
    {
        public XRaySpawnShapeType Type { get; internal set; }
        public XVector3 Center { get; internal set; }
        public double Radius { get; internal set; }
        public XMatrix4 Box { get; internal set; } = XMatrix4.Identity;
    }

    public sealed class XRaySpawnAnomaly
    {
        public ushort SpawnId { get; internal set; }
        public ushort Version { get; internal set; }
        public string Section { get; internal set; }
        public string Name { get; internal set; }
        public XVector3 Position { get; internal set; }
        public XVector3 Angles { get; internal set; }
        public ushort GameVertexId { get; internal set; }
        public uint LevelVertexId { get; internal set; }
        public byte LevelId { get; internal set; }
        public string LevelName { get; internal set; }
        public string IniString { get; internal set; }
        public string MotionName { get; internal set; }
        public uint EnabledTime { get; internal set; }
        public uint DisabledTime { get; internal set; }
        public uint StartTimeShift { get; internal set; }
        public List<XRaySpawnShape> Shapes { get; } = new List<XRaySpawnShape>();
    }
}
