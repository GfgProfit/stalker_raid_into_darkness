using System.Collections.Generic;

namespace XRay.Importer.Core.Omf
{
    public static class OmfFormat
    {
        public const uint CHUNK_MOTIONS_COUNT = 0x0;
        public const uint CHUNK_MOTIONS_2 = 14;
        public const uint CHUNK_SMPARAMS_1 = 15;
        public const byte FL_T_KEY_PRESENT = 1 << 0;
        public const byte FL_R_KEY_ABSENT = 1 << 1;
        public const byte KPF_T_HQ = 1 << 2;
        public const uint MOTION_FX = 1 << 0;
        public const uint MOTION_STOP_AT_END = 1 << 1;
        public const uint MOTION_NO_MIX = 1 << 2;
        public const uint MOTION_SYNC_PART = 1 << 3;
        public const uint MOTION_USE_FOOT_STEPS = 1 << 4;
        public const uint MOTION_ROOT_MOVER = 1 << 5;
        public const uint MOTION_IDLE = 1 << 6;
        public const uint MOTION_USE_WEAPON_BONE = 1 << 7;
        public const ushort BONEPART_NONE = 0xFFFF;
        public const double SAMPLE_FPS = 30.0;
        public const double QUAT_QUANT = 32767.0;
    }

    public sealed class OmfBonePart
    {
        public string Name { get; internal set; }
        public List<string> BoneNames { get; } = new List<string>();
        public List<int> BoneIds { get; } = new List<int>();
    }

    public sealed class OmfMotionMark
    {
        public string Name { get; internal set; }
        public List<double> Intervals { get; } = new List<double>();
    }

    public sealed class OmfMotionParams
    {
        public string Name { get; internal set; }
        public uint Flags { get; internal set; }
        public ushort BoneOrPart { get; internal set; }
        public ushort MotionIndex { get; internal set; }
        public double Speed { get; internal set; }
        public double Power { get; internal set; }
        public double Accrue { get; internal set; }
        public double Falloff { get; internal set; }
        public List<OmfMotionMark> Marks { get; } = new List<OmfMotionMark>();
        public bool StopAtEnd => (Flags & OmfFormat.MOTION_STOP_AT_END) != 0;
        public bool IsFx => (Flags & OmfFormat.MOTION_FX) != 0;
    }

    public sealed class OmfBoneTrack
    {
        public int BoneId { get; internal set; }
        public string BoneName { get; internal set; }
        public byte Flags { get; internal set; }
        public XQuaternion[] Rotations { get; internal set; }
        public XVector3[] Translations { get; internal set; }
        public bool HasRotationKeys => Rotations.Length > 1;
        public bool HasTranslationKeys => Translations.Length > 1;

        public XQuaternion GetRotation(int frame)
        {
            return Rotations.Length == 1 ? Rotations[0] : Rotations[frame];
        }

        public XVector3 GetTranslation(int frame)
        {
            return Translations.Length == 1 ? Translations[0] : Translations[frame];
        }
    }

    public sealed class OmfMotion
    {
        public string Name { get; internal set; }
        public int Index { get; internal set; }
        public int FrameCount { get; internal set; }
        public OmfMotionParams Params { get; internal set; }
        public List<OmfBoneTrack> Tracks { get; } = new List<OmfBoneTrack>();

        public OmfBoneTrack FindTrack(string boneName)
        {
            foreach (OmfBoneTrack track in Tracks)
            {
                if (track.BoneName == boneName)
                {
                    return track;
                }
            }

            return null;
        }
    }

    public sealed class OmfData
    {
        public ushort ParamsVersion { get; internal set; }
        public int MotionsVersion { get; internal set; }
        public List<OmfBonePart> BoneParts { get; } = new List<OmfBonePart>();
        public SortedDictionary<int, string> BoneNames { get; } = new SortedDictionary<int, string>();
        public List<OmfMotionParams> Params { get; } = new List<OmfMotionParams>();
        public List<OmfMotion> Motions { get; } = new List<OmfMotion>();
        public List<string> Warnings { get; } = new List<string>();

        public OmfMotion FindMotion(string name)
        {
            foreach (OmfMotion motion in Motions)
            {
                if (motion.Name == name)
                {
                    return motion;
                }
            }

            return null;
        }
    }
}
