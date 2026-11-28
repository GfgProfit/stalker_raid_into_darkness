using System;
using System.Collections.Generic;
using XRay.Importer.Core.IO;
using RaidIntoDarkness.Effects;
using UnityEngine;

namespace RaidIntoDarkness.Inventory.Editor
{
    public static class XRayParticleLibraryReader
    {
        private const uint CHUNK_EFFECTS = 3;
        private const uint CHUNK_GROUPS = 4;
        private const uint PED_NAME = 2;
        private const uint PED_EFFECT_DATA = 3;
        private const uint PED_ACTIONS = 4;
        private const uint PED_FLAGS = 5;
        private const uint PED_FRAME = 6;
        private const uint PED_SPRITE = 7;
        private const uint PED_TIME_LIMIT = 8;
        private const uint PED_VELOCITY_SCALE = 0x22;
        private const uint PED_ALIGN_TO_PATH = 0x25;
        private const uint PGD_NAME = 2;
        private const uint PGD_EFFECTS = 4;
        private const uint PGD_TIME_LIMIT = 5;
        private const int DOMAIN_SIZE = 68;

        public static void Read(byte[] data, Func<string, bool> filter, List<XRayParticleEffectData> effects, List<XRayParticleGroupData> groups)
        {
            Dictionary<uint, XRayParticleChunkRange> root = Chunks(data, 0, data.Length);

            if (root.TryGetValue(CHUNK_EFFECTS, out XRayParticleChunkRange effectChunk))
            {
                foreach (XRayParticleChunkRange item in List(data, effectChunk.Start, effectChunk.End))
                {
                    XRayParticleEffectData effect = ReadEffect(data, item);

                    if (effect != null && filter(effect.Name))
                    {
                        effects.Add(effect);
                    }
                }
            }

            if (root.TryGetValue(CHUNK_GROUPS, out XRayParticleChunkRange groupChunk))
            {
                foreach (XRayParticleChunkRange item in List(data, groupChunk.Start, groupChunk.End))
                {
                    XRayParticleGroupData group = ReadGroup(data, item);

                    if (group != null && filter(group.Name))
                    {
                        groups.Add(group);
                    }
                }
            }
        }

        private static XRayParticleEffectData ReadEffect(byte[] data, XRayParticleChunkRange range)
        {
            Dictionary<uint, XRayParticleChunkRange> chunks = Chunks(data, range.Start, range.End);

            if (chunks.TryGetValue(PED_NAME, out XRayParticleChunkRange name) == false)
            {
                return null;
            }

            var effect = new XRayParticleEffectData { Name = StringZ(data, name.Start, out _) };
            effect.MaxParticles = chunks.TryGetValue(PED_EFFECT_DATA, out XRayParticleChunkRange effectData) ? (int)BitConverter.ToUInt32(data, effectData.Start) : 0;
            effect.Flags = chunks.TryGetValue(PED_FLAGS, out XRayParticleChunkRange flags) ? BitConverter.ToUInt32(data, flags.Start) : 0u;

            if ((effect.Flags & XRayParticleEffectAsset.SPRITE) != 0 && chunks.TryGetValue(PED_SPRITE, out XRayParticleChunkRange sprite))
            {
                effect.ShaderName = StringZ(data, sprite.Start, out int next);
                effect.TextureName = StringZ(data, next, out _);
            }

            if ((effect.Flags & XRayParticleEffectAsset.FRAMED) != 0 && chunks.TryGetValue(PED_FRAME, out XRayParticleChunkRange frame))
            {
                effect.FrameSize = new Vector2(BitConverter.ToSingle(data, frame.Start), BitConverter.ToSingle(data, frame.Start + 4));
                effect.FrameColumns = BitConverter.ToInt32(data, frame.Start + 16);
                effect.FrameCount = BitConverter.ToInt32(data, frame.Start + 20);
                effect.FrameSpeed = BitConverter.ToSingle(data, frame.Start + 24);
            }

            if ((effect.Flags & XRayParticleEffectAsset.TIME_LIMIT) != 0 && chunks.TryGetValue(PED_TIME_LIMIT, out XRayParticleChunkRange limit))
            {
                effect.TimeLimit = BitConverter.ToSingle(data, limit.Start);
            }

            if ((effect.Flags & XRayParticleEffectAsset.VELOCITY_SCALE) != 0 && chunks.TryGetValue(PED_VELOCITY_SCALE, out XRayParticleChunkRange velocity))
            {
                effect.VelocityScale = Vector(data, velocity.Start);
            }

            if ((effect.Flags & XRayParticleEffectAsset.ALIGN_TO_PATH) != 0 && chunks.TryGetValue(PED_ALIGN_TO_PATH, out XRayParticleChunkRange align))
            {
                effect.DefaultRotation = Vector(data, align.Start);
            }

            if (chunks.TryGetValue(PED_ACTIONS, out XRayParticleChunkRange actions))
            {
                ReadActions(data, actions, effect);
            }

            return effect;
        }

        private static void ReadActions(byte[] data, XRayParticleChunkRange range, XRayParticleEffectData effect)
        {
            int p = range.Start;
            uint count = BitConverter.ToUInt32(data, p);
            p += 4;

            for (uint i = 0; i < count && p < range.End; i++)
            {
                var type = (XRayParticleActionType)BitConverter.ToUInt32(data, p);
                uint flags = BitConverter.ToUInt32(data, p + 4);
                p += 12;

                switch (type)
                {
                    case XRayParticleActionType.Source:
                        XRayParticleDomain position = Domain(data, ref p);
                        XRayParticleDomain velocity = Domain(data, ref p);
                        XRayParticleDomain rotation = Domain(data, ref p);
                        XRayParticleDomain size = Domain(data, ref p);
                        XRayParticleDomain color = Domain(data, ref p);
                        float alpha = Float(data, ref p);
                        float rate = Float(data, ref p);
                        float age = Float(data, ref p);
                        float ageSigma = Float(data, ref p);
                        Vector3 parentVelocity = Vector(data, ref p);
                        float parentMotion = Float(data, ref p);
                        effect.Actions.Add(XRayParticleAction.CreateSource(flags, position, velocity, rotation, size, color, alpha, rate, age, ageSigma, parentVelocity, parentMotion));
                        break;
                    case XRayParticleActionType.KillOld:
                        float ageLimit = Float(data, ref p);
                        uint killLess = BitConverter.ToUInt32(data, p);
                        p += 4;
                        effect.Actions.Add(XRayParticleAction.CreateKillOld(flags, ageLimit, killLess != 0));
                        break;
                    case XRayParticleActionType.Move:
                        effect.Actions.Add(XRayParticleAction.CreateEmpty(type, flags));
                        break;
                    case XRayParticleActionType.TargetColor:
                        Vector3 target = Vector(data, ref p);
                        float targetAlpha = Float(data, ref p);
                        float scale = Float(data, ref p);
                        float from = Float(data, ref p);
                        float to = Float(data, ref p);
                        effect.Actions.Add(XRayParticleAction.CreateTargetColor(flags, target, targetAlpha, scale, from, to));
                        break;
                    case XRayParticleActionType.TargetSize:
                        Vector3 targetSize = Vector(data, ref p);
                        Vector3 sizeScale = Vector(data, ref p);
                        effect.Actions.Add(XRayParticleAction.CreateVector(type, flags, targetSize, sizeScale, 0f));
                        break;
                    case XRayParticleActionType.TargetRotate:
                    case XRayParticleActionType.TargetRotateD:
                    case XRayParticleActionType.TargetVelocity:
                    case XRayParticleActionType.TargetVelocityD:
                        Vector3 vector = Vector(data, ref p);
                        float factor = Float(data, ref p);
                        effect.Actions.Add(XRayParticleAction.CreateVector(type, flags, vector, Vector3.zero, factor));
                        break;
                    case XRayParticleActionType.Gravity:
                        effect.Actions.Add(XRayParticleAction.CreateVector(type, flags, Vector(data, ref p), Vector3.zero, 0f));
                        break;
                    case XRayParticleActionType.RandomAccel:
                    case XRayParticleActionType.RandomDisplace:
                    case XRayParticleActionType.RandomVelocity:
                        effect.Actions.Add(XRayParticleAction.CreateDomain(type, flags, Domain(data, ref p)));
                        break;
                    case XRayParticleActionType.Damping:
                        int dampingStart = p;
                        Vector3 damping = Vector(data, ref p);
                        p += 8;
                        effect.Actions.Add(XRayParticleAction.CreateVector(type, flags, damping, Vector3.zero, 0f));
                        var dampingPayload = new byte[p - dampingStart];
                        Buffer.BlockCopy(data, dampingStart, dampingPayload, 0, dampingPayload.Length);
                        effect.UnsupportedPayloads.Add(new XRayParticleActionPayload((int)i, type, flags, dampingPayload));
                        break;
                    default:
                        int end = SkipUnsupported(type, p);
                        if (end > range.End)
                        {
                            throw new FormatException("Truncated X-Ray particle action " + type);
                        }
                        var payload = new byte[end - p];
                        Buffer.BlockCopy(data, p, payload, 0, payload.Length);
                        effect.UnsupportedPayloads.Add(new XRayParticleActionPayload((int)i, type, flags, payload));
                        p = end;
                        effect.Unsupported.Add(type.ToString());
                        break;
                }
            }
        }

        private static int SkipUnsupported(XRayParticleActionType type, int p)
        {
            switch (type)
            {
                case XRayParticleActionType.Avoid:
                case XRayParticleActionType.Bounce:
                    return p + DOMAIN_SIZE + 12;
                case XRayParticleActionType.CopyVertexB:
                    return p + 4;
                case XRayParticleActionType.Explosion:
                    return p + 32;
                case XRayParticleActionType.Follow:
                case XRayParticleActionType.Gravitate:
                case XRayParticleActionType.MatchVelocity:
                    return p + 12;
                case XRayParticleActionType.Jet:
                    return p + 12 + DOMAIN_SIZE + 12;
                case XRayParticleActionType.OrbitLine:
                case XRayParticleActionType.Vortex:
                    return p + 36;
                case XRayParticleActionType.OrbitPoint:
                case XRayParticleActionType.Scatter:
                    return p + 24;
                case XRayParticleActionType.Restore:
                    return p + 4;
                case XRayParticleActionType.Sink:
                case XRayParticleActionType.SinkVelocity:
                    return p + 4 + DOMAIN_SIZE;
                case XRayParticleActionType.SpeedLimit:
                    return p + 8;
                case XRayParticleActionType.Turbulence:
                    return p + 28;
                default:
                    throw new FormatException("Unknown X-Ray particle action " + (int)type);
            }
        }

        private static XRayParticleGroupData ReadGroup(byte[] data, XRayParticleChunkRange range)
        {
            Dictionary<uint, XRayParticleChunkRange> chunks = Chunks(data, range.Start, range.End);

            if (chunks.TryGetValue(PGD_NAME, out XRayParticleChunkRange name) == false)
            {
                return null;
            }

            var group = new XRayParticleGroupData { Name = StringZ(data, name.Start, out _) };
            bool hasLimit = chunks.TryGetValue(PGD_TIME_LIMIT, out XRayParticleChunkRange limit);
            group.TimeLimit = hasLimit ? BitConverter.ToSingle(data, limit.Start) : 0f;

            if (chunks.TryGetValue(PGD_EFFECTS, out XRayParticleChunkRange effects))
            {
                int p = effects.Start;
                uint count = BitConverter.ToUInt32(data, p);
                p += 4;

                for (uint i = 0; i < count; i++)
                {
                    var effect = new XRayParticleGroupEffectData { EffectName = StringZ(data, p, out p) };
                    effect.OnPlayChild = StringZ(data, p, out p);
                    effect.OnBirthChild = StringZ(data, p, out p);
                    effect.OnDeathChild = StringZ(data, p, out p);
                    effect.Time0 = Float(data, ref p);
                    effect.Time1 = Float(data, ref p);
                    effect.Flags = BitConverter.ToUInt32(data, p);
                    p += 4;
                    group.Effects.Add(effect);

                    if (hasLimit == false || group.TimeLimit <= 0f)
                    {
                        group.TimeLimit = Mathf.Max(group.TimeLimit, effect.Time1);
                    }
                }
            }

            return group;
        }

        private static XRayParticleDomain Domain(byte[] data, ref int p)
        {
            var type = (XRayParticleDomainType)BitConverter.ToUInt32(data, p);
            Vector3 p1 = Vector(data, p + 4);
            Vector3 p2 = Vector(data, p + 16);
            Vector3 u = Vector(data, p + 28);
            Vector3 v = Vector(data, p + 40);
            float radius1 = BitConverter.ToSingle(data, p + 52);
            float radius2 = BitConverter.ToSingle(data, p + 56);
            p += DOMAIN_SIZE;
            return new XRayParticleDomain(type, p1, p2, u, v, radius1, radius2);
        }

        private static float Float(byte[] data, ref int p)
        {
            float value = BitConverter.ToSingle(data, p);
            p += 4;
            return value;
        }

        private static Vector3 Vector(byte[] data, ref int p)
        {
            Vector3 value = Vector(data, p);
            p += 12;
            return value;
        }

        private static Vector3 Vector(byte[] data, int p)
        {
            return new Vector3(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8));
        }

        private static string StringZ(byte[] data, int start, out int next)
        {
            int end = start;

            while (end < data.Length && data[end] != 0)
            {
                end++;
            }

            next = end + 1;
            return Cp1251.Decode(data, start, end - start);
        }

        private static Dictionary<uint, XRayParticleChunkRange> Chunks(byte[] data, int start, int end)
        {
            var result = new Dictionary<uint, XRayParticleChunkRange>();

            foreach (XRayParticleChunkRange range in Walk(data, start, end))
            {
                result[range.Id] = range;
            }

            return result;
        }

        private static List<XRayParticleChunkRange> List(byte[] data, int start, int end)
        {
            return Walk(data, start, end);
        }

        private static List<XRayParticleChunkRange> Walk(byte[] data, int start, int end)
        {
            var result = new List<XRayParticleChunkRange>();
            int p = start;

            while (p + 8 <= end)
            {
                uint id = BitConverter.ToUInt32(data, p) & 0x7fffffffu;
                int size = (int)BitConverter.ToUInt32(data, p + 4);
                result.Add(new XRayParticleChunkRange(id, p + 8, Math.Min(end, p + 8 + size)));
                p += 8 + size;
            }

            return result;
        }
    }
}
