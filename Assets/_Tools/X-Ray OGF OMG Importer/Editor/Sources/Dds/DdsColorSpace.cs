using System;

namespace XRay.Importer.Sources.Dds
{
    public static class DdsColorSpace
    {
        private const uint DDPF_FOURCC = 0x4;
        private const uint DX10_FOURCC = 0x30315844;
        private const uint DIMENSION_TEXTURE2D = 3;

        public static bool TryGetDxgiFormat(DdsFormat format, bool srgb, out uint dxgiFormat)
        {
            switch (format)
            {
                case DdsFormat.BC1:
                    dxgiFormat = srgb ? DdsHeader.DXGI_BC1_UNORM_SRGB : DdsHeader.DXGI_BC1_UNORM;
                    return true;
                case DdsFormat.BC3:
                    dxgiFormat = srgb ? DdsHeader.DXGI_BC3_UNORM_SRGB : DdsHeader.DXGI_BC3_UNORM;
                    return true;
                case DdsFormat.BC7:
                    dxgiFormat = srgb ? DdsHeader.DXGI_BC7_UNORM_SRGB : DdsHeader.DXGI_BC7_UNORM;
                    return true;
                case DdsFormat.BC4:
                    dxgiFormat = DdsHeader.DXGI_BC4_UNORM;
                    return !srgb;
                case DdsFormat.BC5:
                    dxgiFormat = DdsHeader.DXGI_BC5_UNORM;
                    return !srgb;
                default:
                    dxgiFormat = 0;
                    return false;
            }
        }

        public static byte[] WithDx10Header(byte[] data, DdsHeader header, uint dxgiFormat)
        {
            if (header.IsCubemap || header.IsVolume || header.ArraySize > 1)
            {
                throw new NotSupportedException("Only 2D DDS textures can be rewritten.");
            }

            int payload = data.Length - header.DataOffset;
            var output = new byte[DdsHeader.HEADER_SIZE + DdsHeader.DX10_HEADER_SIZE + payload];
            Buffer.BlockCopy(data, 0, output, 0, DdsHeader.HEADER_SIZE);
            WriteUInt32(output, 80, DDPF_FOURCC);
            WriteUInt32(output, 84, DX10_FOURCC);
            for (int offset = 88; offset < 108; offset += 4)
            {
                WriteUInt32(output, offset, 0);
            }

            WriteUInt32(output, DdsHeader.HEADER_SIZE, dxgiFormat);
            WriteUInt32(output, DdsHeader.HEADER_SIZE + 4, DIMENSION_TEXTURE2D);
            WriteUInt32(output, DdsHeader.HEADER_SIZE + 8, 0);
            WriteUInt32(output, DdsHeader.HEADER_SIZE + 12, 1);
            WriteUInt32(output, DdsHeader.HEADER_SIZE + 16, 0);
            Buffer.BlockCopy(data, header.DataOffset, output, DdsHeader.HEADER_SIZE + DdsHeader.DX10_HEADER_SIZE, payload);
            return output;
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }
}
