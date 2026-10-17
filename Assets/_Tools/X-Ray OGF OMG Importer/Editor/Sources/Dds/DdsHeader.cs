using System;

namespace XRay.Importer.Sources.Dds
{
    public enum DdsFormat
    {
        Unknown,
        BC1,
        BC2,
        BC3,
        BC4,
        BC5,
        BC6H,
        BC7,
        Rgba32,
        Bgra32,
        Bgrx32,
        Bgr24,
        Bgr565,
        Bgra5551,
        Bgra4444,
        Luminance8,
        LuminanceAlpha8,
        Alpha8
    }

    public sealed class DdsHeader
    {
        public const uint MAGIC = 0x20534444;
        public const int HEADER_SIZE = 128;
        public const int DX10_HEADER_SIZE = 20;
        public const uint DXGI_BC1_UNORM = 71;
        public const uint DXGI_BC1_UNORM_SRGB = 72;
        public const uint DXGI_BC2_UNORM = 74;
        public const uint DXGI_BC2_UNORM_SRGB = 75;
        public const uint DXGI_BC3_UNORM = 77;
        public const uint DXGI_BC3_UNORM_SRGB = 78;
        public const uint DXGI_BC4_UNORM = 80;
        public const uint DXGI_BC5_UNORM = 83;
        public const uint DXGI_BC6H_UF16 = 95;
        public const uint DXGI_BC6H_SF16 = 96;
        public const uint DXGI_BC7_UNORM = 98;
        public const uint DXGI_BC7_UNORM_SRGB = 99;
        public const uint DXGI_R8G8B8A8_UNORM = 28;
        public const uint DXGI_R8G8B8A8_UNORM_SRGB = 29;
        public const uint DXGI_B8G8R8A8_UNORM = 87;
        public const uint DXGI_B8G8R8A8_UNORM_SRGB = 91;

        private const uint DDPF_ALPHAPIXELS = 0x1;
        private const uint DDPF_ALPHA = 0x2;
        private const uint DDPF_FOURCC = 0x4;
        private const uint DDPF_RGB = 0x40;
        private const uint DDPF_LUMINANCE = 0x20000;
        private const uint DDSCAPS2_CUBEMAP = 0x200;
        private const uint DDSCAPS2_VOLUME = 0x200000;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; }
        public int MipCount { get; private set; }
        public DdsFormat Format { get; private set; }
        public string FourCC { get; private set; }
        public uint DxgiFormat { get; private set; }
        public bool HasDx10Header { get; private set; }
        public bool IsSrgb { get; private set; }
        public bool IsCubemap { get; private set; }
        public bool IsVolume { get; private set; }
        public int ArraySize { get; private set; } = 1;
        public int DataOffset { get; private set; }
        public int BitCount { get; private set; }
        public bool IsBlockCompressed => Format >= DdsFormat.BC1 && Format <= DdsFormat.BC7;
        public string Description => HasDx10Header ? $"DX10 DXGI {DxgiFormat} ({Format})" : FourCC != null ? $"{FourCC} ({Format})" : $"{BitCount} bpp ({Format})";

        public static DdsHeader Parse(byte[] data)
        {
            if (data == null || data.Length < HEADER_SIZE || ReadUInt32(data, 0) != MAGIC || ReadUInt32(data, 4) != 124)
            {
                throw new FormatException("Not a DDS file (missing 'DDS ' header).");
            }

            var header = new DdsHeader
            {
                Height = (int)ReadUInt32(data, 12),
                Width = (int)ReadUInt32(data, 16),
                Depth = Math.Max(1, (int)ReadUInt32(data, 24)),
                MipCount = Math.Max(1, (int)ReadUInt32(data, 28)),
                DataOffset = HEADER_SIZE
            };

            uint caps2 = ReadUInt32(data, 112);
            header.IsCubemap = (caps2 & DDSCAPS2_CUBEMAP) != 0;
            header.IsVolume = (caps2 & DDSCAPS2_VOLUME) != 0;
            uint flags = ReadUInt32(data, 80);
            uint fourCC = ReadUInt32(data, 84);
            header.BitCount = (int)ReadUInt32(data, 88);
            if ((flags & DDPF_FOURCC) != 0)
            {
                header.FourCC = FourCCToString(fourCC);
                header.ParseFourCC(data);
            }
            else
            {
                header.ParseMasks(flags, ReadUInt32(data, 92), ReadUInt32(data, 96), ReadUInt32(data, 100), ReadUInt32(data, 104));
            }

            if (header.Width <= 0 || header.Height <= 0)
            {
                throw new FormatException($"Invalid DDS size {header.Width}x{header.Height}.");
            }

            return header;
        }

        public static int GetLevelSize(DdsFormat format, int width, int height)
        {
            int blocksWide = Math.Max(1, (width + 3) / 4);
            int blocksHigh = Math.Max(1, (height + 3) / 4);
            switch (format)
            {
                case DdsFormat.BC1:
                case DdsFormat.BC4:
                    return blocksWide * blocksHigh * 8;
                case DdsFormat.BC2:
                case DdsFormat.BC3:
                case DdsFormat.BC5:
                case DdsFormat.BC6H:
                case DdsFormat.BC7:
                    return blocksWide * blocksHigh * 16;
                default:
                    return width * height * GetBytesPerPixel(format);
            }
        }

        public static int GetBytesPerPixel(DdsFormat format)
        {
            switch (format)
            {
                case DdsFormat.Rgba32:
                case DdsFormat.Bgra32:
                case DdsFormat.Bgrx32:
                    return 4;
                case DdsFormat.Bgr24:
                    return 3;
                case DdsFormat.Bgr565:
                case DdsFormat.Bgra5551:
                case DdsFormat.Bgra4444:
                case DdsFormat.LuminanceAlpha8:
                    return 2;
                case DdsFormat.Luminance8:
                case DdsFormat.Alpha8:
                    return 1;
                default:
                    return 0;
            }
        }

        internal static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        private void ParseFourCC(byte[] data)
        {
            switch (FourCC)
            {
                case "DXT1":
                    Format = DdsFormat.BC1;
                    break;
                case "DXT2":
                case "DXT3":
                    Format = DdsFormat.BC2;
                    break;
                case "DXT4":
                case "DXT5":
                    Format = DdsFormat.BC3;
                    break;
                case "ATI1":
                case "BC4U":
                    Format = DdsFormat.BC4;
                    break;
                case "ATI2":
                case "BC5U":
                    Format = DdsFormat.BC5;
                    break;
                case "DX10":
                    ParseDx10(data);
                    break;
                default:
                    Format = DdsFormat.Unknown;
                    break;
            }
        }

        private void ParseDx10(byte[] data)
        {
            if (data.Length < HEADER_SIZE + DX10_HEADER_SIZE)
            {
                throw new FormatException("DDS DX10 header is truncated.");
            }

            HasDx10Header = true;
            DataOffset = HEADER_SIZE + DX10_HEADER_SIZE;
            DxgiFormat = ReadUInt32(data, HEADER_SIZE);
            uint miscFlag = ReadUInt32(data, HEADER_SIZE + 8);
            ArraySize = Math.Max(1, (int)ReadUInt32(data, HEADER_SIZE + 12));
            IsCubemap |= (miscFlag & 0x4) != 0;
            IsVolume |= ReadUInt32(data, HEADER_SIZE + 4) == 4;
            switch (DxgiFormat)
            {
                case DXGI_BC1_UNORM_SRGB:
                    IsSrgb = true;
                    Format = DdsFormat.BC1;
                    break;
                case DXGI_BC1_UNORM:
                    Format = DdsFormat.BC1;
                    break;
                case DXGI_BC2_UNORM_SRGB:
                    IsSrgb = true;
                    Format = DdsFormat.BC2;
                    break;
                case DXGI_BC2_UNORM:
                    Format = DdsFormat.BC2;
                    break;
                case DXGI_BC3_UNORM_SRGB:
                    IsSrgb = true;
                    Format = DdsFormat.BC3;
                    break;
                case DXGI_BC3_UNORM:
                    Format = DdsFormat.BC3;
                    break;
                case DXGI_BC4_UNORM:
                    Format = DdsFormat.BC4;
                    break;
                case DXGI_BC5_UNORM:
                    Format = DdsFormat.BC5;
                    break;
                case DXGI_BC6H_UF16:
                case DXGI_BC6H_SF16:
                    Format = DdsFormat.BC6H;
                    break;
                case DXGI_BC7_UNORM_SRGB:
                    IsSrgb = true;
                    Format = DdsFormat.BC7;
                    break;
                case DXGI_BC7_UNORM:
                    Format = DdsFormat.BC7;
                    break;
                case DXGI_R8G8B8A8_UNORM_SRGB:
                    IsSrgb = true;
                    Format = DdsFormat.Rgba32;
                    break;
                case DXGI_R8G8B8A8_UNORM:
                    Format = DdsFormat.Rgba32;
                    break;
                case DXGI_B8G8R8A8_UNORM_SRGB:
                    IsSrgb = true;
                    Format = DdsFormat.Bgra32;
                    break;
                case DXGI_B8G8R8A8_UNORM:
                    Format = DdsFormat.Bgra32;
                    break;
                default:
                    Format = DdsFormat.Unknown;
                    break;
            }
        }

        private void ParseMasks(uint flags, uint red, uint green, uint blue, uint alpha)
        {
            bool hasAlpha = (flags & DDPF_ALPHAPIXELS) != 0;
            if ((flags & DDPF_RGB) != 0)
            {
                if (BitCount == 32 && red == 0x00FF0000 && green == 0x0000FF00 && blue == 0x000000FF)
                {
                    Format = hasAlpha && alpha == 0xFF000000 ? DdsFormat.Bgra32 : DdsFormat.Bgrx32;
                }
                else if (BitCount == 32 && red == 0x000000FF && green == 0x0000FF00 && blue == 0x00FF0000)
                {
                    Format = DdsFormat.Rgba32;
                }
                else if (BitCount == 24 && red == 0x00FF0000 && green == 0x0000FF00 && blue == 0x000000FF)
                {
                    Format = DdsFormat.Bgr24;
                }
                else if (BitCount == 16 && red == 0xF800 && green == 0x07E0 && blue == 0x001F)
                {
                    Format = DdsFormat.Bgr565;
                }
                else if (BitCount == 16 && red == 0x7C00 && green == 0x03E0 && blue == 0x001F)
                {
                    Format = DdsFormat.Bgra5551;
                }
                else if (BitCount == 16 && red == 0x0F00 && green == 0x00F0 && blue == 0x000F)
                {
                    Format = DdsFormat.Bgra4444;
                }
            }
            else if ((flags & DDPF_LUMINANCE) != 0)
            {
                Format = BitCount == 16 && hasAlpha ? DdsFormat.LuminanceAlpha8 : BitCount == 8 ? DdsFormat.Luminance8 : DdsFormat.Unknown;
            }
            else if ((flags & DDPF_ALPHA) != 0 && BitCount == 8)
            {
                Format = DdsFormat.Alpha8;
            }
        }

        private static string FourCCToString(uint value)
        {
            var chars = new char[4];
            for (int i = 0; i < 4; i++)
            {
                chars[i] = (char)((value >> (8 * i)) & 0xFF);
            }

            return new string(chars);
        }
    }
}
