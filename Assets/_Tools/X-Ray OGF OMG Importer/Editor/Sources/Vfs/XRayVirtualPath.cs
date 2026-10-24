using System.IO;
using System.Text;

namespace XRay.Importer.Sources.Vfs
{
    public static class XRayVirtualPath
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(path.Length);
            bool previousSeparator = true;
            foreach (char ch in path.Trim())
            {
                if (ch == '\\' || ch == '/')
                {
                    if (!previousSeparator)
                    {
                        builder.Append('/');
                    }

                    previousSeparator = true;
                    continue;
                }

                builder.Append(char.ToLowerInvariant(ch));
                previousSeparator = false;
            }

            if (builder.Length > 0 && builder[builder.Length - 1] == '/')
            {
                builder.Length--;
            }

            return builder.ToString();
        }

        public static bool TryNormalizeSafe(string path, out string normalized)
        {
            normalized = Normalize(path);
            if (normalized.IndexOf(':') >= 0)
            {
                return false;
            }

            string[] segments = normalized.Split('/');
            var builder = new StringBuilder(normalized.Length);
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }

                if (segment.Trim('.').Length == 0)
                {
                    return false;
                }

                if (builder.Length > 0)
                {
                    builder.Append('/');
                }

                builder.Append(segment);
            }

            normalized = builder.ToString();
            return true;
        }

        public static string ToDisplay(string normalizedPath)
        {
            return normalizedPath.Replace('/', '\\');
        }

        public static string ToFileSystem(string root, string relativePath)
        {
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        }

        public static string TextureToVirtualPath(string textureName, string extension)
        {
            string name = (textureName ?? string.Empty).Trim().Replace('/', '\\');
            if (name.EndsWith(".dds", System.StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }

            return Normalize("textures\\" + name + extension);
        }
    }
}
