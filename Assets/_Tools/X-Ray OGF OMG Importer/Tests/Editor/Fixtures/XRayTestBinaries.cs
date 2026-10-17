using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XRay.Importer.Tests
{
    public sealed class OgfTestMesh
    {
        public OgfTestMesh(string texture, string shader)
        {
            Texture = texture;
            Shader = shader;
        }

        public string Texture { get; }
        public string Shader { get; }
    }

    public sealed class BinaryBuffer
    {
        private readonly MemoryStream _stream = new MemoryStream();

        public BinaryBuffer()
        {
            Writer = new BinaryWriter(_stream);
        }

        public BinaryWriter Writer { get; }

        public void WriteChunk(uint id, BinaryBuffer content)
        {
            byte[] data = content.ToArray();
            Writer.Write(id);
            Writer.Write((uint)data.Length);
            Writer.Write(data);
        }

        public byte[] ToArray()
        {
            Writer.Flush();
            return _stream.ToArray();
        }
    }

    public static class XRayTestBinaries
    {
        private const uint FVF_OGF_STATIC = 0x112;

        public static byte[] Bc1Solid(int width, int height, byte r, byte g, byte b)
        {
            int blocks = ((width + 3) / 4) * ((height + 3) / 4);
            var data = new List<byte>(DdsHeader(width, height, "DXT1", blocks * 8));
            for (int i = 0; i < blocks; i++)
            {
                data.AddRange(Bc1Block(r, g, b));
            }

            return data.ToArray();
        }

        public static byte[] Bc1TopBottom(byte[] top, byte[] bottom)
        {
            var data = new List<byte>(DdsHeader(4, 8, "DXT1", 16));
            data.AddRange(Bc1Block(top[0], top[1], top[2]));
            data.AddRange(Bc1Block(bottom[0], bottom[1], bottom[2]));
            return data.ToArray();
        }

        public static byte[] Bc3Solid(int width, int height, byte r, byte g, byte b, byte a)
        {
            int blocks = ((width + 3) / 4) * ((height + 3) / 4);
            var data = new List<byte>(DdsHeader(width, height, "DXT5", blocks * 16));
            for (int i = 0; i < blocks; i++)
            {
                data.AddRange(new byte[] { a, a, 0, 0, 0, 0, 0, 0 });
                data.AddRange(Bc1Block(r, g, b));
            }

            return data.ToArray();
        }

        public static byte[] Dxt3Solid(int width, int height, byte r, byte g, byte b, byte alphaNibble)
        {
            int blocks = ((width + 3) / 4) * ((height + 3) / 4);
            var data = new List<byte>(DdsHeader(width, height, "DXT3", blocks * 16));
            byte packed = (byte)(alphaNibble | (alphaNibble << 4));
            for (int i = 0; i < blocks; i++)
            {
                for (int k = 0; k < 8; k++)
                {
                    data.Add(packed);
                }

                data.AddRange(Bc1Block(r, g, b));
            }

            return data.ToArray();
        }

        public static byte[] Bgra32(int width, int height, byte[] rgbaTopDown)
        {
            byte[] header = DdsHeader(width, height, null, width * height * 4);
            WriteUInt32(header, 80, 0x41);
            WriteUInt32(header, 88, 32);
            WriteUInt32(header, 92, 0x00FF0000);
            WriteUInt32(header, 96, 0x0000FF00);
            WriteUInt32(header, 100, 0x000000FF);
            WriteUInt32(header, 104, 0xFF000000);
            var data = new List<byte>(header);
            for (int i = 0; i < width * height; i++)
            {
                data.Add(rgbaTopDown[i * 4 + 2]);
                data.Add(rgbaTopDown[i * 4 + 1]);
                data.Add(rgbaTopDown[i * 4]);
                data.Add(rgbaTopDown[i * 4 + 3]);
            }

            return data.ToArray();
        }

        public static byte[] Thm(string bumpName, uint flags)
        {
            var file = new BinaryBuffer();
            var version = new BinaryBuffer();
            version.Writer.Write((ushort)0x12);
            file.WriteChunk(0x0810, version);
            var type = new BinaryBuffer();
            type.Writer.Write(1u);
            file.WriteChunk(0x0813, type);
            var parameters = new BinaryBuffer();
            parameters.Writer.Write(3u);
            parameters.Writer.Write(flags);
            for (int i = 0; i < 4; i++)
            {
                parameters.Writer.Write(0u);
            }

            parameters.Writer.Write(16);
            parameters.Writer.Write(16);
            file.WriteChunk(0x0812, parameters);
            var textureType = new BinaryBuffer();
            textureType.Writer.Write(0u);
            file.WriteChunk(0x0814, textureType);
            var detail = new BinaryBuffer();
            detail.Writer.Write((byte)0);
            detail.Writer.Write(1.0f);
            file.WriteChunk(0x0815, detail);
            var material = new BinaryBuffer();
            material.Writer.Write(1u);
            material.Writer.Write(0.0f);
            file.WriteChunk(0x0816, material);
            if (bumpName != null)
            {
                var bump = new BinaryBuffer();
                bump.Writer.Write(0.05f);
                bump.Writer.Write(2u);
                WriteStringZ(bump.Writer, bumpName);
                file.WriteChunk(0x0817, bump);
            }

            return file.ToArray();
        }

        public static byte[] StaticOgf(IList<OgfTestMesh> meshes)
        {
            if (meshes.Count == 1)
            {
                return Visual(meshes[0], 0, 0.0f);
            }

            var file = new BinaryBuffer();
            file.WriteChunk(1, Header(1));
            var children = new BinaryBuffer();
            for (int i = 0; i < meshes.Count; i++)
            {
                byte[] child = Visual(meshes[i], 0, i * 2.0f);
                children.Writer.Write((uint)i);
                children.Writer.Write((uint)child.Length);
                children.Writer.Write(child);
            }

            file.WriteChunk(9, children);
            return file.ToArray();
        }

        private static byte[] Visual(OgfTestMesh mesh, byte modelType, float offset)
        {
            var file = new BinaryBuffer();
            file.WriteChunk(1, Header(modelType));
            var texture = new BinaryBuffer();
            WriteStringZ(texture.Writer, mesh.Texture);
            WriteStringZ(texture.Writer, mesh.Shader);
            file.WriteChunk(2, texture);
            var vertices = new BinaryBuffer();
            vertices.Writer.Write(FVF_OGF_STATIC);
            vertices.Writer.Write(3u);
            float[][] positions = { new[] { offset, 0.0f, 0.0f }, new[] { offset, 1.0f, 0.0f }, new[] { offset + 1.0f, 0.0f, 0.0f } };
            float[][] uvs = { new[] { 0.0f, 1.0f }, new[] { 0.0f, 0.0f }, new[] { 1.0f, 1.0f } };
            for (int i = 0; i < 3; i++)
            {
                vertices.Writer.Write(positions[i][0]);
                vertices.Writer.Write(positions[i][1]);
                vertices.Writer.Write(positions[i][2]);
                vertices.Writer.Write(0.0f);
                vertices.Writer.Write(0.0f);
                vertices.Writer.Write(-1.0f);
                vertices.Writer.Write(uvs[i][0]);
                vertices.Writer.Write(uvs[i][1]);
            }

            file.WriteChunk(3, vertices);
            var indices = new BinaryBuffer();
            indices.Writer.Write(3u);
            indices.Writer.Write((ushort)0);
            indices.Writer.Write((ushort)1);
            indices.Writer.Write((ushort)2);
            file.WriteChunk(4, indices);
            return file.ToArray();
        }

        private static BinaryBuffer Header(byte modelType)
        {
            var header = new BinaryBuffer();
            header.Writer.Write((byte)4);
            header.Writer.Write(modelType);
            header.Writer.Write((ushort)0);
            for (int i = 0; i < 10; i++)
            {
                header.Writer.Write(0.0f);
            }

            return header;
        }

        private static byte[] Bc1Block(byte r, byte g, byte b)
        {
            int color = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
            return new[] { (byte)color, (byte)(color >> 8), (byte)color, (byte)(color >> 8), (byte)0, (byte)0, (byte)0, (byte)0 };
        }

        private static byte[] DdsHeader(int width, int height, string fourCC, int linearSize)
        {
            var header = new byte[128];
            WriteUInt32(header, 0, 0x20534444);
            WriteUInt32(header, 4, 124);
            WriteUInt32(header, 8, 0x1 | 0x2 | 0x4 | 0x1000 | 0x80000);
            WriteUInt32(header, 12, (uint)height);
            WriteUInt32(header, 16, (uint)width);
            WriteUInt32(header, 20, (uint)linearSize);
            WriteUInt32(header, 28, 1);
            WriteUInt32(header, 76, 32);
            if (fourCC != null)
            {
                WriteUInt32(header, 80, 0x4);
                byte[] code = Encoding.ASCII.GetBytes(fourCC);
                for (int i = 0; i < 4; i++)
                {
                    header[84 + i] = code[i];
                }
            }

            WriteUInt32(header, 108, 0x1000);
            return header;
        }

        private static void WriteStringZ(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.ASCII.GetBytes(value ?? string.Empty));
            writer.Write((byte)0);
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
