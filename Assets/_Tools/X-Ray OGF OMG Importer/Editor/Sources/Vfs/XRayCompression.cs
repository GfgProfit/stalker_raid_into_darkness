using System;
using System.IO;

namespace XRay.Importer.Sources.Vfs
{
    public static class XRayCompression
    {
        private const int LZ_N = 4096;
        private const int LZ_F = 60;
        private const int LZ_THRESHOLD = 2;
        private const int LZ_N_CHAR = 256 - LZ_THRESHOLD + LZ_F;
        private const int LZ_T = LZ_N_CHAR * 2 - 1;
        private const int LZ_R = LZ_T - 1;
        private const uint LZ_MAX_FREQ = 0x4000;
        private const int LZO_M2_MAX_OFFSET = 0x0800;

        private static readonly byte[] P_LEN =
        {
            3, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
        };

        private static readonly byte[] P_CODE =
        {
            0x00, 0x20, 0x30, 0x40, 0x50, 0x58, 0x60, 0x68, 0x70, 0x78, 0x80, 0x88, 0x90, 0x94, 0x98, 0x9C,
            0xA0, 0xA4, 0xA8, 0xAC, 0xB0, 0xB4, 0xB8, 0xBC, 0xC0, 0xC2, 0xC4, 0xC6, 0xC8, 0xCA, 0xCC, 0xCE,
            0xD0, 0xD2, 0xD4, 0xD6, 0xD8, 0xDA, 0xDC, 0xDE, 0xE0, 0xE2, 0xE4, 0xE6, 0xE8, 0xEA, 0xEC, 0xEE,
            0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA, 0xFB, 0xFC, 0xFD, 0xFE, 0xFF
        };

        private static readonly byte[] D_CODE = new byte[256];
        private static readonly byte[] D_LEN = new byte[256];
        private static readonly uint[] CRC_TABLE = new uint[256];

        static XRayCompression()
        {
            for (int i = 0; i < P_LEN.Length; i++)
            {
                int span = 1 << (8 - P_LEN[i]);
                for (int k = 0; k < span; k++)
                {
                    D_CODE[P_CODE[i] + k] = (byte)i;
                    D_LEN[P_CODE[i] + k] = P_LEN[i];
                }
            }

            for (uint i = 0; i < 256; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                }

                CRC_TABLE[i] = value;
            }
        }

        public static uint Crc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++)
            {
                crc = CRC_TABLE[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }

        public static byte[] DecompressLzhuf(byte[] source, int offset, int count)
        {
            if (count < 4)
            {
                throw new XRaySourceException("LZHUF stream is shorter than its size header.");
            }

            var state = new LzhufState(source, offset + 4, offset + count);
            uint size = BitConverter.ToUInt32(source, offset);
            if (size > int.MaxValue / 2)
            {
                throw new XRaySourceException($"LZHUF stream declares an invalid size {size}.");
            }

            var output = new byte[size];
            var text = new byte[LZ_N + LZ_F];
            for (int i = 0; i < LZ_N - LZ_F; i++)
            {
                text[i] = 0x20;
            }

            int r = LZ_N - LZ_F;
            int written = 0;
            while (written < output.Length)
            {
                int c = state.DecodeChar();
                if (c < 256)
                {
                    output[written++] = (byte)c;
                    text[r++] = (byte)c;
                    r &= LZ_N - 1;
                    continue;
                }

                int position = (r - state.DecodePosition() - 1) & (LZ_N - 1);
                int length = c - 255 + LZ_THRESHOLD;
                for (int k = 0; k < length && written < output.Length; k++)
                {
                    byte value = text[(position + k) & (LZ_N - 1)];
                    output[written++] = value;
                    text[r++] = value;
                    r &= LZ_N - 1;
                }
            }

            return output;
        }

        public static uint Crc32(Stream stream)
        {
            var buffer = new byte[1 << 16];
            uint crc = 0xFFFFFFFFu;
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    crc = CRC_TABLE[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
                }
            }

            return ~crc;
        }

        public static byte[] DecompressLzo1x(byte[] source, int offset, int count, int outputSize)
        {
            var output = new byte[outputSize];
            int ip = offset;
            int end = offset + count;
            int op = 0;
            int t = 0;
            int match;
            int state = 0;
            if (Byte(source, ip, end) > 17)
            {
                t = source[ip++] - 17;
                if (t < 4)
                {
                    state = 3;
                }
                else
                {
                    CopyLiterals(source, ref ip, end, output, ref op, t);
                    state = 1;
                }
            }

            while (true)
            {
                if (state == 0)
                {
                    t = Byte(source, ip++, end);
                    if (t >= 16)
                    {
                        state = 2;
                        goto Match;
                    }

                    if (t == 0)
                    {
                        while (Byte(source, ip, end) == 0)
                        {
                            t += 255;
                            ip++;
                        }

                        t += 15 + source[ip++];
                    }

                    CopyLiterals(source, ref ip, end, output, ref op, t + 3);
                    state = 1;
                }

                if (state == 1)
                {
                    t = Byte(source, ip++, end);
                    if (t >= 16)
                    {
                        state = 2;
                        goto Match;
                    }

                    match = op - (1 + LZO_M2_MAX_OFFSET) - (t >> 2) - (Byte(source, ip++, end) << 2);
                    CopyMatch(output, ref op, match, 3);
                    goto MatchDone;
                }

                if (state == 3)
                {
                    t = Byte(source, ip - 1, end) - 17;
                    goto MatchNext;
                }

            Match:
                t = Byte(source, ip - 1, end);
                if (t >= 64)
                {
                    match = op - 1 - ((t >> 2) & 7) - (Byte(source, ip++, end) << 3);
                    CopyMatch(output, ref op, match, (t >> 5) + 1);
                }
                else if (t >= 32)
                {
                    t &= 31;
                    if (t == 0)
                    {
                        while (Byte(source, ip, end) == 0)
                        {
                            t += 255;
                            ip++;
                        }

                        t += 31 + source[ip++];
                    }

                    match = op - 1 - ((Byte(source, ip, end) >> 2) + (Byte(source, ip + 1, end) << 6));
                    ip += 2;
                    CopyMatch(output, ref op, match, t + 2);
                }
                else if (t >= 16)
                {
                    match = op - ((t & 8) << 11);
                    t &= 7;
                    if (t == 0)
                    {
                        while (Byte(source, ip, end) == 0)
                        {
                            t += 255;
                            ip++;
                        }

                        t += 7 + source[ip++];
                    }

                    match -= (Byte(source, ip, end) >> 2) + (Byte(source, ip + 1, end) << 6);
                    ip += 2;
                    if (match == op)
                    {
                        break;
                    }

                    match -= 0x4000;
                    CopyMatch(output, ref op, match, t + 2);
                }
                else
                {
                    match = op - 1 - (t >> 2) - (Byte(source, ip++, end) << 2);
                    CopyMatch(output, ref op, match, 2);
                }

            MatchDone:
                t = Byte(source, ip - 2, end) & 3;
                if (t == 0)
                {
                    state = 0;
                    continue;
                }

            MatchNext:
                CopyLiterals(source, ref ip, end, output, ref op, t);
                ip++;
                state = 2;
                goto Match;
            }

            if (op != outputSize)
            {
                throw new XRaySourceException($"LZO stream produced {op} bytes instead of {outputSize}.");
            }

            return output;
        }

        private static int Byte(byte[] source, int index, int end)
        {
            if (index < 0 || index >= end)
            {
                throw new XRaySourceException("LZO stream is truncated.");
            }

            return source[index];
        }

        private static void CopyLiterals(byte[] source, ref int ip, int end, byte[] output, ref int op, int count)
        {
            if (ip + count > end || op + count > output.Length)
            {
                throw new XRaySourceException("LZO literal run exceeds the stream bounds.");
            }

            Buffer.BlockCopy(source, ip, output, op, count);
            ip += count;
            op += count;
        }

        private static void CopyMatch(byte[] output, ref int op, int match, int count)
        {
            if (match < 0 || op + count > output.Length)
            {
                throw new XRaySourceException("LZO match points outside the output buffer.");
            }

            for (int i = 0; i < count; i++)
            {
                output[op++] = output[match++];
            }
        }

        private sealed class LzhufState
        {
            private readonly byte[] _source;
            private readonly int _end;
            private readonly uint[] _freq = new uint[LZ_T + 1];
            private readonly int[] _parent = new int[LZ_T + LZ_N_CHAR + 1];
            private readonly int[] _son = new int[LZ_T];
            private int _position;
            private uint _bits;
            private int _bitCount;

            public LzhufState(byte[] source, int start, int end)
            {
                _source = source;
                _position = start;
                _end = end;
                for (int i = 0; i < LZ_N_CHAR; i++)
                {
                    _freq[i] = 1;
                    _son[i] = i + LZ_T;
                    _parent[i + LZ_T] = i;
                }

                int a = 0;
                for (int j = LZ_N_CHAR; j <= LZ_R; j++)
                {
                    _freq[j] = _freq[a] + _freq[a + 1];
                    _son[j] = a;
                    _parent[a] = _parent[a + 1] = j;
                    a += 2;
                }

                _freq[LZ_T] = 0xFFFF;
                _parent[LZ_R] = 0;
            }

            public int DecodeChar()
            {
                int c = _son[LZ_R];
                while (c < LZ_T)
                {
                    c += GetBit();
                    c = _son[c];
                }

                c -= LZ_T;
                Update(c);
                return c;
            }

            public int DecodePosition()
            {
                int i = GetByte();
                int c = D_CODE[i] << 6;
                int j = D_LEN[i] - 2;
                while (j-- > 0)
                {
                    i = (i << 1) + GetBit();
                }

                return c | (i & 0x3F);
            }

            private int ReadSource()
            {
                return _position < _end ? _source[_position++] : 0;
            }

            private int GetBit()
            {
                while (_bitCount <= 8)
                {
                    _bits |= (uint)ReadSource() << (8 - _bitCount);
                    _bitCount += 8;
                }

                uint value = _bits;
                _bits <<= 1;
                _bitCount--;
                return (int)((value & 0x8000) >> 15);
            }

            private int GetByte()
            {
                while (_bitCount <= 8)
                {
                    _bits |= (uint)ReadSource() << (8 - _bitCount);
                    _bitCount += 8;
                }

                uint value = _bits;
                _bits <<= 8;
                _bitCount -= 8;
                return (int)((value & 0xFF00) >> 8);
            }

            private void Reconstruct()
            {
                int j = 0;
                for (int i = 0; i < LZ_T; i++)
                {
                    if (_son[i] >= LZ_T)
                    {
                        _freq[j] = (_freq[i] + 1) / 2;
                        _son[j] = _son[i];
                        j++;
                    }
                }

                for (int i = 0, k = LZ_N_CHAR; k < LZ_T; i += 2, k++)
                {
                    uint f = _freq[k] = _freq[i] + _freq[i + 1];
                    int l = k - 1;
                    while (f < _freq[l])
                    {
                        l--;
                    }

                    l++;
                    Array.Copy(_freq, l, _freq, l + 1, k - l);
                    _freq[l] = f;
                    Array.Copy(_son, l, _son, l + 1, k - l);
                    _son[l] = i;
                }

                for (int i = 0; i < LZ_T; i++)
                {
                    int k = _son[i];
                    if (k >= LZ_T)
                    {
                        _parent[k] = i;
                    }
                    else
                    {
                        _parent[k] = _parent[k + 1] = i;
                    }
                }
            }

            private void Update(int symbol)
            {
                if (_freq[LZ_R] == LZ_MAX_FREQ)
                {
                    Reconstruct();
                }

                int c = _parent[symbol + LZ_T];
                do
                {
                    uint k = ++_freq[c];
                    int l = c + 1;
                    if (k > _freq[l])
                    {
                        while (k > _freq[++l])
                        {
                        }

                        l--;
                        _freq[c] = _freq[l];
                        _freq[l] = k;
                        int i = _son[c];
                        _parent[i] = l;
                        if (i < LZ_T)
                        {
                            _parent[i + 1] = l;
                        }

                        int j = _son[l];
                        _son[l] = i;
                        _parent[j] = c;
                        if (j < LZ_T)
                        {
                            _parent[j + 1] = c;
                        }

                        _son[c] = j;
                        c = l;
                    }

                    c = _parent[c];
                }
                while (c != 0);
            }
        }
    }
}
