using System;
using System.Collections.Generic;
using XRay.Importer.Core.IO;

namespace XRay.Importer.Core.Anm
{
    public static class XRayAnmReader
    {
        private const int KEY_SIZE_V3 = 40;
        private const int KEY_SIZE_MIN_V4 = 9;
        private const int QUANTIZED_PARAMS_SIZE = 14;

        public static XRayAnmMotion ReadFile(byte[] data, string context)
        {
            var reader = new XRayReader(data, context);
            Dictionary<uint, XRayChunk> chunks = XRayChunks.ReadMap(reader);
            if (!chunks.TryGetValue(XRayAnmFormat.CHUNK_MAIN, out XRayChunk main))
            {
                throw new XRayFormatException($"{context}: main chunk 0x{XRayAnmFormat.CHUNK_MAIN:X} is missing; this is not an *.anm object motion.");
            }

            var motion = Read(main.CreateReader(context + " chunk 0x1100"));
            foreach (uint id in chunks.Keys)
            {
                if (id != XRayAnmFormat.CHUNK_MAIN)
                {
                    motion.Warnings.Add($"Unknown chunk 0x{id:X} ignored.");
                }
            }

            return motion;
        }

        public static XRayAnmMotion Read(XRayReader reader)
        {
            var motion = new XRayAnmMotion { Name = reader.ReadStringZ() };
            motion.FrameStart = reader.ReadInt32();
            motion.FrameEnd = reader.ReadInt32();
            motion.Fps = reader.ReadSingle();
            motion.Version = reader.ReadUInt16();
            if (!XRayAnmFormat.IsSupported(motion.Version))
            {
                throw new XRayFormatException($"{reader.Context}: ANM version {motion.Version} at offset {reader.Position - 2} is not supported (supported: 3, 4, 5).");
            }

            if (!(motion.Fps > 0.0) || double.IsInfinity(motion.Fps))
            {
                throw new XRayFormatException($"{reader.Context}: invalid FPS {motion.Fps}.");
            }

            if (motion.FrameEnd < motion.FrameStart)
            {
                motion.Warnings.Add($"Frame range {motion.FrameStart}..{motion.FrameEnd} is reversed.");
            }

            for (int channel = 0; channel < XRayAnmFormat.CHANNEL_COUNT; channel++)
            {
                motion.FileEnvelopes[channel] = ReadEnvelope(reader, motion.Version, channel, motion.Warnings);
            }

            if (!reader.IsEnd)
            {
                motion.Warnings.Add($"{reader.Remaining} unread bytes after the six envelopes.");
            }

            return motion;
        }

        private static XRayAnmEnvelope ReadEnvelope(XRayReader reader, ushort version, int channel, List<string> warnings)
        {
            var envelope = new XRayAnmEnvelope();
            int start = reader.Position;
            int pre;
            int post;
            int count;
            if (version > XRayAnmFormat.VERSION_3)
            {
                pre = reader.ReadByte();
                post = reader.ReadByte();
                count = reader.ReadUInt16();
            }
            else
            {
                pre = reader.ReadInt32();
                post = reader.ReadInt32();
                uint rawCount = reader.ReadUInt32();
                if (rawCount > XRayAnmFormat.MAX_KEYS_PER_CHANNEL)
                {
                    throw new XRayFormatException($"{reader.Context}: envelope {channel} at offset {start} declares {rawCount} keys.");
                }

                count = (int)rawCount;
            }

            envelope.PreBehavior = ToBehavior(pre, channel, warnings);
            envelope.PostBehavior = ToBehavior(post, channel, warnings);
            int minimumKeySize = version > XRayAnmFormat.VERSION_3 ? KEY_SIZE_MIN_V4 : KEY_SIZE_V3;
            if ((long)count * minimumKeySize > reader.Remaining)
            {
                throw new XRayFormatException($"{reader.Context}: envelope {channel} at offset {start} declares {count} keys, but only {reader.Remaining} bytes remain.");
            }

            for (int i = 0; i < count; i++)
            {
                XRayAnmKey key = version > XRayAnmFormat.VERSION_3 ? ReadKeyQuantized(reader) : ReadKeyFloat(reader);
                if (!Enum.IsDefined(typeof(XRayAnmShape), key.Shape))
                {
                    warnings.Add($"Envelope {channel} key {i}: unknown shape {(int)key.Shape}; the engine evaluates such a span as 0.");
                }

                if (i > 0 && key.Time < envelope.Keys[i - 1].Time)
                {
                    warnings.Add($"Envelope {channel} key {i}: time {key.Time} is before the previous key.");
                }

                envelope.Keys.Add(key);
            }

            return envelope;
        }

        private static XRayAnmKey ReadKeyQuantized(XRayReader reader)
        {
            var key = new XRayAnmKey { Value = reader.ReadSingle(), Time = reader.ReadSingle(), Shape = (XRayAnmShape)reader.ReadByte() };
            if (key.Shape == XRayAnmShape.Stepped)
            {
                return key;
            }

            if (reader.Remaining < QUANTIZED_PARAMS_SIZE)
            {
                throw new XRayFormatException($"{reader.Context}: key parameters at offset {reader.Position} are truncated.");
            }

            key.Tension = ReadQuantized(reader);
            key.Continuity = ReadQuantized(reader);
            key.Bias = ReadQuantized(reader);
            key.Param0 = ReadQuantized(reader);
            key.Param1 = ReadQuantized(reader);
            key.Param2 = ReadQuantized(reader);
            key.Param3 = ReadQuantized(reader);
            return key;
        }

        private static XRayAnmKey ReadKeyFloat(XRayReader reader)
        {
            var key = new XRayAnmKey { Value = reader.ReadSingle(), Time = reader.ReadSingle(), Shape = (XRayAnmShape)(reader.ReadUInt32() & 0xFF) };
            key.Tension = reader.ReadSingle();
            key.Continuity = reader.ReadSingle();
            key.Bias = reader.ReadSingle();
            key.Param0 = reader.ReadSingle();
            key.Param1 = reader.ReadSingle();
            key.Param2 = reader.ReadSingle();
            key.Param3 = reader.ReadSingle();
            return key;
        }

        private static double ReadQuantized(XRayReader reader)
        {
            ushort value = reader.ReadUInt16();
            return (float)(value * (XRayAnmFormat.QUANTIZATION_MAX - XRayAnmFormat.QUANTIZATION_MIN) / 65535.0f + XRayAnmFormat.QUANTIZATION_MIN);
        }

        private static XRayAnmBehavior ToBehavior(int value, int channel, List<string> warnings)
        {
            if (value < 0 || value > (int)XRayAnmBehavior.Linear)
            {
                warnings.Add($"Envelope {channel}: unknown behavior {value}; treated as CONSTANT.");
                return XRayAnmBehavior.Constant;
            }

            return (XRayAnmBehavior)value;
        }
    }
}
