using System;

namespace XRay.Importer.Sources.Dds
{
    public static class DdsDecoder
    {
        public static bool CanDecode(DdsHeader header)
        {
            if (header.IsCubemap || header.IsVolume || header.ArraySize > 1)
            {
                return false;
            }

            switch (header.Format)
            {
                case DdsFormat.Unknown:
                case DdsFormat.BC6H:
                case DdsFormat.RgbaHalf:
                    return false;
                default:
                    return true;
            }
        }

        public static byte[] DecodeTopLevel(byte[] data, DdsHeader header)
        {
            return DecodeTopLevel(data, header, false);
        }

        public static byte[] DecodePngTopLevel(byte[] data, DdsHeader header)
        {
            return DecodeTopLevel(data, header, true);
        }

        private static byte[] DecodeTopLevel(byte[] data, DdsHeader header, bool preserveChannels)
        {
            if (!CanDecode(header))
            {
                throw new NotSupportedException($"DDS format {header.Description} cannot be decoded.");
            }

            int outputSize = checked(header.Width * header.Height * 4);
            int required = DdsHeader.GetLevelSize(header.Format, header.Width, header.Height);
            if (required > data.Length - header.DataOffset)
            {
                throw new FormatException($"DDS data is truncated: need {required} bytes at offset {header.DataOffset}, file has {data.Length}.");
            }

            var output = new byte[outputSize];
            if (header.IsBlockCompressed)
            {
                DecodeBlocks(data, header, output, preserveChannels);
            }
            else
            {
                DecodeUncompressed(data, header, output);
            }

            return output;
        }

        private static void DecodeBlocks(byte[] data, DdsHeader header, byte[] output, bool preserveChannels)
        {
            int blocksWide = Math.Max(1, (header.Width + 3) / 4);
            int blocksHigh = Math.Max(1, (header.Height + 3) / 4);
            int blockSize = header.Format == DdsFormat.BC1 || header.Format == DdsFormat.BC4 ? 8 : 16;
            var block = new byte[64];
            int offset = header.DataOffset;
            for (int by = 0; by < blocksHigh; by++)
            {
                for (int bx = 0; bx < blocksWide; bx++)
                {
                    DecodeBlock(data, offset, header.Format, block, preserveChannels);
                    offset += blockSize;
                    int x = bx * 4;
                    int bytesPerRow = Math.Min(4, header.Width - x) * 4;
                    for (int py = 0; py < 4; py++)
                    {
                        int y = by * 4 + py;
                        if (y >= header.Height)
                        {
                            break;
                        }

                        Buffer.BlockCopy(block, py * 16, output, (y * header.Width + x) * 4, bytesPerRow);
                    }
                }
            }
        }

        private static void DecodeBlock(byte[] data, int offset, DdsFormat format, byte[] block, bool preserveChannels)
        {
            switch (format)
            {
                case DdsFormat.BC1:
                    DecodeColorBlock(data, offset, block, true, preserveChannels);
                    break;
                case DdsFormat.BC2:
                    DecodeColorBlock(data, offset + 8, block, false, preserveChannels);
                    for (int i = 0; i < 16; i++)
                    {
                        int nibble = (data[offset + i / 2] >> ((i % 2) * 4)) & 0xF;
                        block[i * 4 + 3] = (byte)(nibble * 17);
                    }

                    break;
                case DdsFormat.BC3:
                    DecodeColorBlock(data, offset + 8, block, false, preserveChannels);
                    DecodeAlphaBlock(data, offset, block, 3, preserveChannels);
                    break;
                case DdsFormat.BC4:
                    DecodeAlphaBlock(data, offset, block, 0, preserveChannels);
                    for (int i = 0; i < 16; i++)
                    {
                        byte value = preserveChannels ? (byte)0 : block[i * 4];
                        block[i * 4 + 1] = value;
                        block[i * 4 + 2] = value;
                        block[i * 4 + 3] = 255;
                    }

                    break;
                case DdsFormat.BC5:
                    DecodeAlphaBlock(data, offset, block, 0, preserveChannels);
                    DecodeAlphaBlock(data, offset + 8, block, 1, preserveChannels);
                    for (int i = 0; i < 16; i++)
                    {
                        block[i * 4 + 2] = preserveChannels ? (byte)0 : ReconstructNormalZ(block[i * 4], block[i * 4 + 1]);
                        block[i * 4 + 3] = 255;
                    }

                    break;
                case DdsFormat.BC7:
                    DdsBc7Decoder.DecodeBlock(data, offset, block);
                    break;
            }
        }

        private static byte ReconstructNormalZ(byte x, byte y)
        {
            double nx = x / 127.5 - 1.0;
            double ny = y / 127.5 - 1.0;
            double nz = Math.Sqrt(Math.Max(0.0, 1.0 - nx * nx - ny * ny));
            return (byte)Math.Round((nz * 0.5 + 0.5) * 255.0);
        }

        private static void DecodeColorBlock(byte[] data, int offset, byte[] block, bool allowTransparent, bool preserveChannels)
        {
            int c0 = data[offset] | (data[offset + 1] << 8);
            int c1 = data[offset + 2] | (data[offset + 3] << 8);
            uint indices = DdsHeader.ReadUInt32(data, offset + 4);
            Span<int> palette = stackalloc int[16];
            Expand565(c0, palette, 0);
            Expand565(c1, palette, 4);
            if (c0 > c1 || !allowTransparent)
            {
                int rounding = preserveChannels ? 0 : 1;
                for (int channel = 0; channel < 3; channel++)
                {
                    palette[8 + channel] = (2 * palette[channel] + palette[4 + channel] + rounding) / 3;
                    palette[12 + channel] = (palette[channel] + 2 * palette[4 + channel] + rounding) / 3;
                }

                palette[11] = 255;
                palette[15] = 255;
            }
            else
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    palette[8 + channel] = (palette[channel] + palette[4 + channel]) / 2;
                    palette[12 + channel] = 0;
                }

                palette[11] = 255;
                palette[15] = 0;
            }

            for (int i = 0; i < 16; i++)
            {
                int index = (int)((indices >> (2 * i)) & 0x3);
                for (int channel = 0; channel < 4; channel++)
                {
                    block[i * 4 + channel] = (byte)palette[index * 4 + channel];
                }
            }
        }

        private static void Expand565(int color, Span<int> palette, int offset)
        {
            int r = (color >> 11) & 0x1F;
            int g = (color >> 5) & 0x3F;
            int b = color & 0x1F;
            palette[offset] = (r << 3) | (r >> 2);
            palette[offset + 1] = (g << 2) | (g >> 4);
            palette[offset + 2] = (b << 3) | (b >> 2);
            palette[offset + 3] = 255;
        }

        private static void DecodeAlphaBlock(byte[] data, int offset, byte[] block, int channel, bool preserveChannels)
        {
            int a0 = data[offset];
            int a1 = data[offset + 1];
            Span<int> values = stackalloc int[8];
            values[0] = a0;
            values[1] = a1;
            if (a0 > a1)
            {
                int rounding = preserveChannels ? 0 : 3;
                for (int i = 1; i < 7; i++)
                {
                    values[i + 1] = ((7 - i) * a0 + i * a1 + rounding) / 7;
                }
            }
            else
            {
                int rounding = preserveChannels ? 0 : 2;
                for (int i = 1; i < 5; i++)
                {
                    values[i + 1] = ((5 - i) * a0 + i * a1 + rounding) / 5;
                }

                values[6] = 0;
                values[7] = 255;
            }

            ulong bits = 0;
            for (int i = 0; i < 6; i++)
            {
                bits |= (ulong)data[offset + 2 + i] << (8 * i);
            }

            for (int i = 0; i < 16; i++)
            {
                block[i * 4 + channel] = (byte)values[(int)((bits >> (3 * i)) & 0x7)];
            }
        }

        private static void DecodeUncompressed(byte[] data, DdsHeader header, byte[] output)
        {
            int bytesPerPixel = DdsHeader.GetBytesPerPixel(header.Format);
            int pitch = header.Width * bytesPerPixel;
            for (int y = 0; y < header.Height; y++)
            {
                for (int x = 0; x < header.Width; x++)
                {
                    int source = header.DataOffset + y * pitch + x * bytesPerPixel;
                    int target = (y * header.Width + x) * 4;
                    DecodePixel(data, source, header.Format, output, target);
                }
            }
        }

        private static void DecodePixel(byte[] data, int source, DdsFormat format, byte[] output, int target)
        {
            byte r;
            byte g;
            byte b;
            byte a = 255;
            int packed;
            switch (format)
            {
                case DdsFormat.Rgba32:
                    r = data[source];
                    g = data[source + 1];
                    b = data[source + 2];
                    a = data[source + 3];
                    break;
                case DdsFormat.Bgra32:
                    b = data[source];
                    g = data[source + 1];
                    r = data[source + 2];
                    a = data[source + 3];
                    break;
                case DdsFormat.Bgrx32:
                case DdsFormat.Bgr24:
                    b = data[source];
                    g = data[source + 1];
                    r = data[source + 2];
                    break;
                case DdsFormat.Bgr565:
                    packed = data[source] | (data[source + 1] << 8);
                    r = Expand(packed >> 11, 5);
                    g = Expand(packed >> 5, 6);
                    b = Expand(packed, 5);
                    break;
                case DdsFormat.Bgra5551:
                    packed = data[source] | (data[source + 1] << 8);
                    r = Expand(packed >> 10, 5);
                    g = Expand(packed >> 5, 5);
                    b = Expand(packed, 5);
                    a = (packed & 0x8000) != 0 ? (byte)255 : (byte)0;
                    break;
                case DdsFormat.Bgra4444:
                    packed = data[source] | (data[source + 1] << 8);
                    r = Expand(packed >> 8, 4);
                    g = Expand(packed >> 4, 4);
                    b = Expand(packed, 4);
                    a = Expand(packed >> 12, 4);
                    break;
                case DdsFormat.Luminance8:
                    r = g = b = data[source];
                    break;
                case DdsFormat.LuminanceAlpha8:
                    r = g = b = data[source];
                    a = data[source + 1];
                    break;
                case DdsFormat.Alpha8:
                    r = g = b = 255;
                    a = data[source];
                    break;
                default:
                    throw new NotSupportedException($"Uncompressed DDS format {format} is not supported.");
            }

            output[target] = r;
            output[target + 1] = g;
            output[target + 2] = b;
            output[target + 3] = a;
        }

        private static byte Expand(int value, int bits)
        {
            int max = (1 << bits) - 1;
            return (byte)(((value & max) * 255 + max / 2) / max);
        }
    }
}
