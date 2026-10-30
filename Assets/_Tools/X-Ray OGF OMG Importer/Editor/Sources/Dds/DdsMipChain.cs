using System;
using System.Collections.Generic;

namespace XRay.Importer.Sources.Dds
{
    public static class DdsMipChain
    {
        public const int MAX_DIMENSION = 16384;

        private const uint DDSD_MIPMAPCOUNT = 0x20000;

        public static bool TryValidate(byte[] source, DdsHeader header, out string reason)
        {
            reason = null;
            if (source == null || header == null)
            {
                reason = "DDS source or header is missing";
                return false;
            }

            if (header.IsCubemap || header.IsVolume || header.ArraySize > 1)
            {
                reason = "cube, volume and array textures are not supported";
                return false;
            }

            if (header.Format != DdsFormat.BC1 && header.Format != DdsFormat.BC2 && header.Format != DdsFormat.BC3)
            {
                reason = $"authored mip chain is preserved only for BC1/BC2/BC3, source is {header.Description}";
                return false;
            }

            if (header.Width <= 0 || header.Height <= 0 || header.Width > MAX_DIMENSION || header.Height > MAX_DIMENSION)
            {
                reason = $"top level {header.Width}x{header.Height} is outside 1..{MAX_DIMENSION}";
                return false;
            }

            if (header.Width % 4 != 0 || header.Height % 4 != 0)
            {
                reason = $"top level {header.Width}x{header.Height} is not a multiple of 4";
                return false;
            }

            if (source.Length < DdsHeader.HEADER_SIZE)
            {
                reason = $"file has {source.Length} bytes, shorter than the DDS header";
                return false;
            }

            int fullChain = FullChainLength(header.Width, header.Height);
            uint rawMipCount = DdsHeader.ReadUInt32(source, 28);
            if (rawMipCount > (uint)fullChain)
            {
                reason = $"raw mip count {rawMipCount} exceeds the full chain of {fullChain} levels for {header.Width}x{header.Height}";
                return false;
            }

            if (rawMipCount > 1 && (DdsHeader.ReadUInt32(source, 8) & DDSD_MIPMAPCOUNT) == 0)
            {
                reason = $"mip count {rawMipCount} is set without DDSD_MIPMAPCOUNT, so the authored chain length is ambiguous";
                return false;
            }

            if (header.MipCount != Math.Max(1, (int)rawMipCount))
            {
                reason = $"parsed mip count {header.MipCount} does not match the raw header value {rawMipCount}";
                return false;
            }

            int levelHeight = header.Height;
            for (int mip = 0; mip < header.MipCount; mip++)
            {
                if (levelHeight > 4 && levelHeight % 4 != 0)
                {
                    reason = $"mip {mip} height {levelHeight} is not a multiple of 4; the block-row flip cannot move padded rows";
                    return false;
                }

                levelHeight = Math.Max(1, levelHeight >> 1);
            }

            if (header.DataOffset < DdsHeader.HEADER_SIZE || header.DataOffset > source.Length)
            {
                reason = $"data offset {header.DataOffset} is outside the {source.Length}-byte file";
                return false;
            }

            long required = header.DataOffset;
            long width = header.Width;
            long height = header.Height;
            long blockSize = header.Format == DdsFormat.BC1 ? 8 : 16;
            for (int mip = 0; mip < header.MipCount; mip++)
            {
                required += Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockSize;
                width = Math.Max(1, width >> 1);
                height = Math.Max(1, height >> 1);
            }

            if (required > source.Length)
            {
                reason = $"payload is truncated: {header.MipCount} levels need {required} bytes, the file has {source.Length}";
                return false;
            }

            return true;
        }

        public static int FullChainLength(int width, int height)
        {
            int levels = 1;
            int size = Math.Max(width, height);
            while (size > 1)
            {
                size >>= 1;
                levels++;
            }

            return levels;
        }

        public static bool TryReadFlipped(byte[] source, DdsHeader header, out List<byte[]> levels, out string reason)
        {
            levels = new List<byte[]>();
            if (!TryValidate(source, header, out reason))
            {
                return false;
            }

            int offset = header.DataOffset;
            int width = header.Width;
            int height = header.Height;
            for (int mip = 0; mip < header.MipCount; mip++)
            {
                int size = DdsHeader.GetLevelSize(header.Format, width, height);
                levels.Add(Flip(source, offset, header.Format, width, height));
                offset += size;
                width = Math.Max(1, width >> 1);
                height = Math.Max(1, height >> 1);
            }

            return true;
        }

        public static byte[] Flip(byte[] source, int offset, DdsFormat format, int width, int height)
        {
            int blockSize = format == DdsFormat.BC1 ? 8 : 16;
            int blocksX = Math.Max(1, (width + 3) / 4);
            int blocksY = Math.Max(1, (height + 3) / 4);
            int rows = Math.Min(4, height);
            var result = new byte[blocksX * blocksY * blockSize];
            for (int y = 0; y < blocksY; y++)
            {
                for (int x = 0; x < blocksX; x++)
                {
                    int from = offset + (y * blocksX + x) * blockSize;
                    int to = ((blocksY - 1 - y) * blocksX + x) * blockSize;
                    Buffer.BlockCopy(source, from, result, to, blockSize);
                    FlipBlock(result, to, format, rows);
                }
            }

            return result;
        }

        private static void FlipBlock(byte[] block, int offset, DdsFormat format, int rows)
        {
            int color = format == DdsFormat.BC1 ? offset : offset + 8;
            for (int row = 0; row < rows / 2; row++)
            {
                Swap(block, color + 4 + row, color + 4 + rows - 1 - row);
            }

            if (format == DdsFormat.BC2)
            {
                for (int row = 0; row < rows / 2; row++)
                {
                    Swap(block, offset + row * 2, offset + (rows - 1 - row) * 2);
                    Swap(block, offset + row * 2 + 1, offset + (rows - 1 - row) * 2 + 1);
                }
            }
            else if (format == DdsFormat.BC3)
            {
                ulong bits = 0;
                for (int i = 0; i < 6; i++)
                {
                    bits |= (ulong)block[offset + 2 + i] << (8 * i);
                }

                var source = new ulong[4];
                for (int row = 0; row < 4; row++)
                {
                    source[row] = (bits >> (12 * row)) & 0xFFFUL;
                }

                ulong flipped = 0;
                for (int row = 0; row < 4; row++)
                {
                    ulong value = row < rows ? source[rows - 1 - row] : source[row];
                    flipped |= value << (12 * row);
                }

                for (int i = 0; i < 6; i++)
                {
                    block[offset + 2 + i] = (byte)(flipped >> (8 * i));
                }
            }
        }

        private static void Swap(byte[] data, int a, int b)
        {
            byte value = data[a];
            data[a] = data[b];
            data[b] = value;
        }
    }
}
