using System;
using System.Buffers.Binary;
using NUnit.Framework;
using XRay.Importer.Sources.Dds;

namespace XRay.Importer.Tests
{
    public sealed class DdsPngDecoderTests
    {
        private const int BLOCK_OFFSET = DdsHeader.HEADER_SIZE + DdsHeader.DX10_HEADER_SIZE;

        [TestCase("y2TWOjpPUQK/yiRIFuqXMQ==", "YxAh/1stIP85nIz/Spab/1NKHv8xwRn/XJCq/zmcjP85pBv/McEZ/96MWv+7k1j/QYcc/1stIP/NkFn/Y6VS/w==")]
        [TestCase("kjWxXtK6VN+9bYc8FdslkQ==", "u1iM/9dKfv+kNG7/aVBu/40/bv+YOW7/SZPE/9dKfv+Edaf/hHWn/15Wbv+YOW7/mDlu/5g5bv9lhLb/ZYS2/w==")]
        [TestCase("vHMTvj00aCrXSKc8RwM4JA==", "zkIx/85CMf+uNFH/axiU/85CMf/OQjH/zkIx/85CMf92jOz/90pS/xClc/9rKff/dozs/xClc/9ch2j/ayn3/w==")]
        [TestCase("WMBZFqcAatwuTk7+UmwZcw==", "4QUX/4ZuPf94qdr/LIyc/4ZuPf9Rmrv/nbf5/3ip2v/hBRf/nbf5/yyMnP94qdr/UZq7/3ip2v+dt/n/LIyc/w==")]
        [TestCase("0LA2DxFDnStT1YDxHmvGPA==", "hDuEa4RRhGunMEvjhFGEa607QvehO1XQrTtC94RGhGuVO2imoUZV0IpGe3+VRmimnEZevIpGe3+tRkL3ijt7fw==")]
        [TestCase("4G9qufD4RJ5gq0NT/GGc0A==", "38uRHt/LJx6pCic+u0knNM2MbinNjJEpzYxKKakKbj7NjJEp38snHrtJbjS7SUo0zYyRKbtJkTS7SW4038snHg==")]
        [TestCase("wAvwPPeWqdZEDRu5Pg/opg==", "OtK/qUXVwKp148isL8+9qWrgx6s00L6pX93Fq2rgx6t75cmsP9TAqoDmyqwvz72pWtvEq3vlyaxQ2MKqZd7Gqw==")]
        [TestCase("gEyC1g9/3HjKeCNtRfD+vg==", "XmM7tk0cHPdxrFpxtjxVlk0cHPffYDyp83EwsspNSZ/zcTCy83EwsvNxMLLKTUmf83EwsvNxMLLKTUmfyk1Jnw==")]
        public void DecodesAllBc7Modes(string encoded, string expected)
        {
            byte[] source = CreateDds(DdsHeader.DXGI_BC7_UNORM, Convert.FromBase64String(encoded));
            DdsHeader header = DdsHeader.Parse(source);
            byte[] rgba = DdsDecoder.DecodePngTopLevel(source, header);
            Assert.That(rgba, Is.EqualTo(Convert.FromBase64String(expected)));
        }

        [Test]
        public void PngDecodePreservesBc3ChannelInterpolationWithoutChangingLegacyDecode()
        {
            byte[] block = { 128, 6, 0x92, 0x24, 0x49, 0x92, 0x24, 0x49, 0, 0xF8, 1, 0, 0xAA, 0xAA, 0xAA, 0xAA };
            byte[] source = CreateDds(DdsHeader.DXGI_BC3_UNORM, block);
            DdsHeader header = DdsHeader.Parse(source);
            byte[] pngRgba = DdsDecoder.DecodePngTopLevel(source, header);
            byte[] legacyRgba = DdsDecoder.DecodeTopLevel(source, header);
            for (int offset = 0; offset < pngRgba.Length; offset += 4)
            {
                Assert.That(pngRgba.AsSpan(offset, 4).ToArray(), Is.EqualTo(new byte[] { 170, 0, 2, 110 }));
                Assert.That(legacyRgba.AsSpan(offset, 4).ToArray(), Is.EqualTo(new byte[] { 170, 0, 3, 111 }));
            }
        }

        [Test]
        public void PngDecodePreservesBc1TransparentPixels()
        {
            byte[] block = { 0, 0, 255, 255, 255, 255, 255, 255 };
            byte[] source = CreateDds(DdsHeader.DXGI_BC1_UNORM, block);
            byte[] rgba = DdsDecoder.DecodePngTopLevel(source, DdsHeader.Parse(source));
            Assert.That(rgba, Is.EqualTo(new byte[64]));
        }

        private static byte[] CreateDds(uint format, byte[] block)
        {
            var source = new byte[BLOCK_OFFSET + block.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(0), DdsHeader.MAGIC);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(4), 124);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(12), 4);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(16), 4);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(28), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(80), 4);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(84), 0x30315844);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(128), format);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(132), 3);
            BinaryPrimitives.WriteUInt32LittleEndian(source.AsSpan(140), 1);
            Buffer.BlockCopy(block, 0, source, BLOCK_OFFSET, block.Length);
            return source;
        }
    }
}
