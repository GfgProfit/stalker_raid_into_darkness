using System;

namespace XRay.Importer.Sources.Dds
{
    internal static class DdsBc7Decoder
    {
        private const int SUBSET_MASK = 3;
        private const int FIXUP_MASK = 4;
        private const int PBIT_MODES = 0b11001011;
        private const int PIXELS_PER_BLOCK = 16;

        private static readonly byte[] COLOR_BITS = { 4, 6, 5, 7, 5, 7, 7, 5 };
        private static readonly byte[] ALPHA_BITS = { 0, 0, 0, 0, 6, 8, 7, 5 };
        private static readonly byte[] WEIGHTS_2 = { 0, 21, 43, 64 };
        private static readonly byte[] WEIGHTS_3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
        private static readonly byte[] WEIGHTS_4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };
        private static readonly ulong[] PARTITIONS_2 =
        {
            0x5100110011001104UL, 0x5000100010001004UL, 0x5110111011101114UL, 0x5110110011001004UL,
            0x5100100010000004UL, 0x5111111011101104UL, 0x5111111011001004UL, 0x5110110010000004UL,
            0x5100100000000004UL, 0x5111111111101104UL, 0x5111111010000004UL, 0x5110100000000004UL,
            0x5111111111101004UL, 0x5111111100000004UL, 0x5111111111110004UL, 0x5111000000000004UL,
            0x5111011100010004UL, 0x0000000010001514UL, 0x0111000500000004UL, 0x0000100011001514UL,
            0x0000000010001504UL, 0x0111001500010004UL, 0x0011000500000004UL, 0x5000110011001114UL,
            0x0000100010001504UL, 0x0011000500010004UL, 0x0110011001100514UL, 0x0011011001101504UL,
            0x0001011511101004UL, 0x0000111511110004UL, 0x0111000110001514UL, 0x0011100110011504UL,
            0x5010101010101014UL, 0x5111000011110004UL, 0x0101101005011014UL, 0x0011001511001104UL,
            0x0011110000111504UL, 0x0101010510101014UL, 0x5001011010010114UL, 0x5010010101011014UL,
            0x0111001111001514UL, 0x0001001511001004UL, 0x0011001001001504UL, 0x0011101111011504UL,
            0x0110100110010514UL, 0x5100001100111104UL, 0x5001100101100114UL, 0x0000011005100004UL,
            0x0000001005110014UL, 0x0000010011100504UL, 0x0100111005000004UL, 0x0010011500100004UL,
            0x5100100100110114UL, 0x5001001101101104UL, 0x0011100111000514UL, 0x0110001110011504UL,
            0x5001001100110114UL, 0x5001110011000114UL, 0x5000000101111114UL, 0x5110011100011004UL,
            0x5100110011110004UL, 0x0000111111001504UL, 0x0111011101000504UL, 0x5110111000100014UL,
        };
        private static readonly ulong[] PARTITIONS_3 =
        {
            0x6222122011005104UL, 0x1222112611005004UL, 0x5122112610020004UL, 0x5110110022006224UL,
            0x6211221500000004UL, 0x6200220011005104UL, 0x5111111122006204UL, 0x5122112611001104UL,
            0x6222111500000004UL, 0x6222111511110004UL, 0x6222222215110004UL, 0x6100210025002104UL,
            0x6110211025102114UL, 0x6210221022502214UL, 0x6221221121105104UL, 0x0222002610025104UL,
            0x6211211011005004UL, 0x0022100611005114UL, 0x6211221522110004UL, 0x5111220022006204UL,
            0x6220222011105114UL, 0x1222122610005004UL, 0x6210221015000004UL, 0x0122052600110004UL,
            0x0000110022506214UL, 0x6222221521002104UL, 0x0110122516210114UL, 0x1221162105100004UL,
            0x6200201520112204UL, 0x6222200201500114UL, 0x5100261022101104UL, 0x5222112600020004UL,
            0x6221221520000004UL, 0x5100210022006224UL, 0x6220220021005104UL, 0x0210061002500214UL,
            0x0000262215110004UL, 0x0210250610210214UL, 0x0210106521020214UL, 0x5100261100221104UL,
            0x5100002226111104UL, 0x6222222210105014UL, 0x5212121600000004UL, 0x6211220022512204UL,
            0x5100220011006204UL, 0x5221022016210224UL, 0x5010222226221014UL, 0x5212121612120004UL,
            0x6222101010105014UL, 0x5110222011106224UL, 0x6111200021512004UL, 0x6112211221520004UL,
            0x6220111011502224UL, 0x6000211521112004UL, 0x6222011001500114UL, 0x6112251200000004UL,
            0x6222222201500114UL, 0x6200150011002204UL, 0x6200221522112204UL, 0x6152000000000004UL,
            0x5000200010006004UL, 0x6225222022212224UL, 0x6222222222225014UL, 0x0222102611025114UL,
        };

        public static void DecodeBlock(byte[] compressed, int offset, Span<byte> rgba)
        {
            var reader = new BlockReader(compressed, offset);
            int mode = 0;
            while (mode < 8 && reader.Read(1) == 0)
            {
                mode++;
            }

            if (mode == 8)
            {
                rgba.Clear();
                return;
            }

            int subsets = mode == 0 || mode == 2 ? 3 : mode == 1 || mode == 3 || mode == 7 ? 2 : 1;
            int partitionId = subsets > 1 ? reader.Read(mode == 0 ? 4 : 6) : 0;
            ulong partition = subsets == 3 ? PARTITIONS_3[partitionId] : subsets == 2 ? PARTITIONS_2[partitionId] : FIXUP_MASK;
            int rotation = mode == 4 || mode == 5 ? reader.Read(2) : 0;
            bool swapIndices = mode == 4 && reader.Read(1) != 0;

            Span<byte> endpoints = stackalloc byte[24];
            ReadEndpoints(ref reader, mode, endpoints.Slice(0, subsets * 8));

            int primaryBits = mode == 0 || mode == 1 ? 3 : mode == 6 ? 4 : 2;
            int secondaryBits = mode == 4 ? 3 : mode == 5 ? 2 : 0;
            Span<byte> primaryWeights = stackalloc byte[PIXELS_PER_BLOCK];
            for (int pixel = 0; pixel < PIXELS_PER_BLOCK; pixel++)
            {
                int bits = primaryBits - ((partition & FIXUP_MASK) != 0 ? 1 : 0);
                primaryWeights[pixel] = Weight(primaryBits, reader.Read(bits));
                partition >>= 4;
            }

            partition = subsets == 3 ? PARTITIONS_3[partitionId] : subsets == 2 ? PARTITIONS_2[partitionId] : FIXUP_MASK;
            for (int pixel = 0; pixel < PIXELS_PER_BLOCK; pixel++)
            {
                int primary = primaryWeights[pixel];
                int secondary = secondaryBits == 0 ? primary : Weight(secondaryBits, reader.Read(secondaryBits - (pixel == 0 ? 1 : 0)));
                int colorWeight = swapIndices ? secondary : primary;
                int alphaWeight = swapIndices ? primary : secondary;
                int endpoint = (int)(partition & SUBSET_MASK) * 8;
                int output = pixel * 4;
                for (int channel = 0; channel < 4; channel++)
                {
                    int weight = channel == 3 ? alphaWeight : colorWeight;
                    int a = endpoints[endpoint + channel];
                    int b = endpoints[endpoint + 4 + channel];
                    rgba[output + channel] = (byte)((a * (64 - weight) + b * weight + 32) >> 6);
                }

                if (rotation != 0)
                {
                    int scalar = output + rotation - 1;
                    byte alpha = rgba[output + 3];
                    rgba[output + 3] = rgba[scalar];
                    rgba[scalar] = alpha;
                }

                partition >>= 4;
            }
        }

        private static void ReadEndpoints(ref BlockReader reader, int mode, Span<byte> endpoints)
        {
            int colorBits = COLOR_BITS[mode];
            int alphaBits = ALPHA_BITS[mode];
            int count = endpoints.Length / 4;
            for (int channel = 0; channel < 4; channel++)
            {
                int bits = channel == 3 ? alphaBits : colorBits;
                for (int endpoint = 0; endpoint < count; endpoint++)
                {
                    endpoints[endpoint * 4 + channel] = (byte)(bits == 0 ? 0 : reader.Read(bits));
                }
            }

            bool hasPBit = (PBIT_MODES & (1 << mode)) != 0;
            int pBit = 0;
            for (int endpoint = 0; endpoint < count; endpoint++)
            {
                if (hasPBit && (mode != 1 || (endpoint & 1) == 0))
                {
                    pBit = reader.Read(1);
                }

                for (int channel = 0; channel < 4; channel++)
                {
                    int bits = channel == 3 ? alphaBits : colorBits;
                    int index = endpoint * 4 + channel;
                    if (bits == 0)
                    {
                        endpoints[index] = 255;
                        continue;
                    }

                    int value = endpoints[index];
                    if (hasPBit)
                    {
                        value = (value << 1) | pBit;
                        bits++;
                    }

                    int msb = value << (8 - bits);
                    endpoints[index] = (byte)(msb | (msb >> bits));
                }
            }
        }

        private static byte Weight(int bits, int index)
        {
            return bits == 2 ? WEIGHTS_2[index] : bits == 3 ? WEIGHTS_3[index] : WEIGHTS_4[index];
        }

        private struct BlockReader
        {
            private readonly byte[] _bytes;
            private readonly int _start;
            private int _offset;

            public BlockReader(byte[] bytes, int start)
            {
                _bytes = bytes;
                _start = start;
                _offset = 0;
            }

            public int Read(int count)
            {
                int index = _start + (_offset >> 3);
                int shift = _offset & 7;
                _offset += count;
                int encoded = _bytes[index];
                if (shift + count > 8)
                {
                    encoded |= _bytes[index + 1] << 8;
                }

                return (encoded >> shift) & ((1 << count) - 1);
            }
        }
    }
}
