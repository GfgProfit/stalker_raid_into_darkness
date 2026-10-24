namespace XRay.Importer.Core.Level
{
    public static class XRayLevelFormat
    {
        public const uint CHUNK_HEADER = 1;
        public const uint CHUNK_SHADERS = 2;
        public const uint CHUNK_VISUALS = 3;
        public const uint CHUNK_PORTALS = 4;
        public const uint CHUNK_LIGHT_DYNAMIC = 6;
        public const uint CHUNK_GLOWS = 7;
        public const uint CHUNK_SECTORS = 8;
        public const uint CHUNK_VB = 9;
        public const uint CHUNK_IB = 10;
        public const uint CHUNK_SWIS = 11;
        public const uint SECTOR_CHUNK_PORTALS = 1;
        public const uint SECTOR_CHUNK_ROOT = 2;
        public const ushort VERSION_13 = 13;
        public const ushort VERSION_14 = 14;
        public const int HEADER_SIZE = 4;
        public const int PORTAL_SIZE = 80;
        public const int PORTAL_MAX_VERTICES = 6;
        public const int LIGHT_SIZE = 108;
        public const int GLOW_SIZE = 18;
        public const int LOD_FACES = 8;
        public const int LOD_VERTEX_SIZE = 28;
        public const int OGF_HEADER_SIZE = 44;
        public const byte OGF_VERSION_4 = 4;
        public const uint OGF_CHUNK_HEADER = 1;
        public const uint OGF_CHUNK_SWIDATA = 6;
        public const uint OGF_CHUNK_CHILDREN_L = 10;
        public const uint OGF_CHUNK_LODDEF2 = 11;
        public const uint OGF_CHUNK_TREEDEF2 = 12;
        public const uint OGF_CHUNK_SWICONTAINER = 20;
        public const uint OGF_CHUNK_GCONTAINER = 21;
        public const uint OGF_CHUNK_FASTPATH = 22;
        public const byte MT_NORMAL = 0;
        public const byte MT_HIERRARHY = 1;
        public const byte MT_PROGRESSIVE = 2;
        public const byte MT_LOD = 6;
        public const byte MT_TREE_ST = 7;
        public const byte MT_TREE_PM = 11;
        public const byte DECL_FLOAT2 = 1;
        public const byte DECL_FLOAT3 = 2;
        public const byte DECL_FLOAT4 = 3;
        public const byte DECL_D3DCOLOR = 4;
        public const byte DECL_SHORT2 = 6;
        public const byte DECL_SHORT4 = 7;
        public const byte DECL_UNUSED = 17;
        public const byte USAGE_POSITION = 0;
        public const byte USAGE_NORMAL = 3;
        public const byte USAGE_TEXCOORD = 5;
        public const byte USAGE_TANGENT = 6;
        public const byte USAGE_BINORMAL = 7;
        public const byte USAGE_COLOR = 10;
        public const int LIGHT_CONTROLLER_SUN = 1;
        public const int LIGHT_CONTROLLER_STATIC = 2;
        public const int D3D_LIGHT_POINT = 1;
        public const int D3D_LIGHT_SPOT = 2;
        public const int D3D_LIGHT_DIRECTIONAL = 3;
        public const float TREE_QUANT = 2048.0f;
        public const float TREE_SHADER_SCALE = 2.0f;
        public const float BASE_UV_SCALE = 32.0f / 32768.0f;
        public const float LIGHTMAP_UV_SCALE = 1.0f / 32768.0f;

        public static int GetDeclSize(byte type)
        {
            switch (type)
            {
                case DECL_FLOAT2:
                    return 8;
                case DECL_FLOAT3:
                    return 12;
                case DECL_FLOAT4:
                    return 16;
                case DECL_D3DCOLOR:
                    return 4;
                case DECL_SHORT2:
                    return 4;
                case DECL_SHORT4:
                    return 8;
                default:
                    return -1;
            }
        }

        public static string GetModelTypeName(byte type)
        {
            switch (type)
            {
                case MT_NORMAL:
                    return "Normal";
                case MT_HIERRARHY:
                    return "Hierarchy";
                case MT_PROGRESSIVE:
                    return "Progressive";
                case MT_LOD:
                    return "Lod";
                case MT_TREE_ST:
                    return "TreeST";
                case MT_TREE_PM:
                    return "TreePM";
                default:
                    return "Type" + type;
            }
        }
    }
}
