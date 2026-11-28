using System.Collections.Generic;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    public sealed class XRayParticleEffectData
    {
        public string Name { get; set; }
        public int MaxParticles { get; set; }
        public uint Flags { get; set; }
        public string ShaderName { get; set; } = string.Empty;
        public string TextureName { get; set; } = string.Empty;
        public Vector2 FrameSize { get; set; } = Vector2.one;
        public int FrameColumns { get; set; } = 1;
        public int FrameCount { get; set; } = 1;
        public float FrameSpeed { get; set; }
        public float TimeLimit { get; set; }
        public Vector3 VelocityScale { get; set; }
        public Vector3 DefaultRotation { get; set; }
        public List<XRayParticleAction> Actions { get; } = new List<XRayParticleAction>();
        public List<string> Unsupported { get; } = new List<string>();
        public List<XRayParticleActionPayload> UnsupportedPayloads { get; } = new List<XRayParticleActionPayload>();
    }
}
