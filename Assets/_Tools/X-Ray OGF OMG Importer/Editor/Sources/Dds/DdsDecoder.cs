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
                case DdsFormat.BC7:
                case DdsFormat.RgbaHalf:
                    return false;
                default:
                    return true;
            }
        }

        public static byte[] DecodeTopLevel(byte[] data, DdsHeader header)
        {
            if (!CanDecode(header))
            {
                throw new NotSupportedException($"DDS format {header.Description} cannot be decoded.");
            }

            int required = DdsHeader.GetLevelSize(header.Format, header.Width, header.Height);
            if (header.DataOffset + required > data.Length)
            {
                throw new FormatException($"DDS data is truncated: need {required} bytes at offset {header.DataOffset}, file has {data.Length}.");
            }

            var output = new byte[header.Width * header.Height * 4];
            if (header.IsBlockCompressed)
            {
                DecodeBlocks(data, header, output);
            }
            else
            {
                DecodeUncompressed(data, header, output);
            }

            return output;
        }

        private static void DecodeBlocks(byte[] data, DdsHeader header, byte[] output)
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
                    DecodeBlock(data, offset, header.Format, block);
                    offset += blockSize;
                    for (int py = 0; py < 4; py++)
                    {
                        int y = by * 4 + py;
                        if (y >= header.Height)
                        {
                            break;
                        }

                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            if (x >= header.Width)
                            {
                                break;
                            }

                            Buffer.BlockCopy(block, (py * 4 + px) * 4, output, (y * header.Width + x) * 4, 4);
                        }
                    }
                }
            }
        }

        private static void DecodeBlock(byte[] data, int offset, DdsFormat format, byte[] block)
        {
            switch (format)
            {
                case DdsFormat.BC1:
                    DecodeColorBlock(data, offset, block, true);
                    break;
                case DdsFormat.BC2:
                    DecodeColorBlock(data, offset + 8, block, false);
                    for (int i = 0; i < 16; i++)
                    {
                        int nibble = (data[offset + i / 2] >> ((i % 2) * 4)) & 0xF;
                        block[i * 4 + 3] = (byte)(nibble * 17);
                    }

                    break;
                case DdsFormat.BC3:
                    DecodeColorBlock(data, offset + 8, block, false);
                    DecodeAlphaBlock(data, offset, block, 3);
                    break;
                case DdsFormat.BC4:
                    DecodeAlphaBlock(data, offset, block, 0);
                    for (int i = 0; i < 16; i++)
                    {
                        block[i * 4 + 1] = block[i * 4];
                        block[i * 4 + 2] = block[i * 4];
                        block[i * 4 + 3] = 255;
                    }

                    break;
                case DdsFormat.BC5:
                    DecodeAlphaBlock(data, offset, block, 0);
                    DecodeAlphaBlock(data, offset + 8, block, 1);
                    for (int i = 0; i < 16; i++)
                    {
                        double nx = block[i * 4] / 127.5 - 1.0;
                        double ny = block[i * 4 + 1] / 127.5 - 1.0;
                        double nz = Math.Sqrt(Math.Max(0.0, 1.0 - nx * nx - ny * ny));
                        block[i * 4 + 2] = (byte)Math.Round((nz * 0.5 + 0.5) * 255.0);
                        block[i * 4 + 3] = 255;
                    }

                    break;
            }
        }

        private static void DecodeColorBlock(byte[] data, int offset, byte[] block, bool allowTransparent)
        {
            int c0 = data[offset] | (data[offset + 1] << 8);
            int c1 = data[offset + 2] | (data[offset + 3] << 8);
            uint indices = DdsHeader.ReadUInt32(data, offset + 4);
            var palette = new int[16];
            Expand565(c0, palette, 0);
            Expand565(c1, palette, 4);
            if (c0 > c1 || !allowTransparent)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    palette[8 + channel] = (2 * palette[channel] + palette[4 + channel] + 1) / 3;
                    palette[12 + channel] = (palette[channel] + 2 * palette[4 + channel] + 1) / 3;
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

        private static void Expand565(int color, int[] palette, int offset)
        {
            int r = (color >> 11) & 0x1F;
            int g = (color >> 5) & 0x3F;
            int b = color & 0x1F;
            palette[offset] = (r << 3) | (r >> 2);
            palette[offset + 1] = (g << 2) | (g >> 4);
            palette[offset + 2] = (b << 3) | (b >> 2);
            palette[offset + 3] = 255;
        }

        private static void DecodeAlphaBlock(byte[] data, int offset, byte[] block, int channel)
        {
            int a0 = data[offset];
            int a1 = data[offset + 1];
            var values = new int[8];
            values[0] = a0;
            values[1] = a1;
            if (a0 > a1)
            {
                for (int i = 1; i < 7; i++)
                {
                    values[i + 1] = ((7 - i) * a0 + i * a1 + 3) / 7;
                }
            }
            else
            {
                for (int i = 1; i < 5; i++)
                {
                    values[i + 1] = ((5 - i) * a0 + i * a1 + 2) / 5;
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
