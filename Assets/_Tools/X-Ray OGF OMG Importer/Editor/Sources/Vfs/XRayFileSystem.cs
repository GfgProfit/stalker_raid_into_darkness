using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XRay.Importer.Sources.Vfs
{
    public static class XRayFileSystem
    {
        private static readonly string[] ARCHIVE_EXTENSIONS = { ".db", ".xdb" };

        public static string FindChildDirectory(string parent, string name)
        {
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return null;
            }

            string relative = name.Replace('\\', '/');
            int separator = relative.IndexOf('/');
            if (separator >= 0)
            {
                string child = FindChildDirectory(parent, relative.Substring(0, separator));
                return child == null ? null : FindChildDirectory(child, relative.Substring(separator + 1));
            }

            foreach (string directory in Directory.GetDirectories(parent))
            {
                if (string.Equals(Path.GetFileName(directory), name, StringComparison.OrdinalIgnoreCase))
                {
                    return directory;
                }
            }

            return null;
        }

        public static string FindChildFile(string parent, string name)
        {
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return null;
            }

            string relative = name.Replace('\\', '/');
            int separator = relative.LastIndexOf('/');
            if (separator >= 0)
            {
                string directory = FindChildDirectory(parent, relative.Substring(0, separator));
                return directory == null ? null : FindChildFile(directory, relative.Substring(separator + 1));
            }

            foreach (string file in Directory.GetFiles(parent))
            {
                if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }

            return null;
        }

        public static int CountArchives(string folder, int maxDepth)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return 0;
            }

            int count = 0;
            foreach (string file in Directory.GetFiles(folder))
            {
                if (IsArchive(file))
                {
                    count++;
                }
            }

            if (maxDepth > 0)
            {
                foreach (string directory in Directory.GetDirectories(folder))
                {
                    count += CountArchives(directory, maxDepth - 1);
                }
            }

            return count;
        }

        public static bool IsArchive(string path)
        {
            string extension = Path.GetExtension(path);
            foreach (string archive in ARCHIVE_EXTENSIONS)
            {
                if (extension.StartsWith(archive, StringComparison.OrdinalIgnoreCase) && IsArchiveSuffix(extension.Substring(archive.Length)))
                {
                    return true;
                }
            }

            return false;
        }

        public static void AppendStamp(StringBuilder builder, string path)
        {
            builder.Append('|').Append(path ?? string.Empty).Append(':');
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                builder.Append(info.Length).Append('@').Append(info.LastWriteTimeUtc.Ticks);
            }
            else if (Directory.Exists(path))
            {
                builder.Append("dir@").Append(Directory.GetLastWriteTimeUtc(path).Ticks);
            }
            else
            {
                builder.Append("missing");
            }
        }

        public static string Hash(string text)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            foreach (byte value in Encoding.UTF8.GetBytes(text))
            {
                hash ^= value;
                hash *= prime;
            }

            return hash.ToString("x16");
        }

        public static IEnumerable<string> EnumerateFilesSafe(string root, ICollection<string> skipDirectories, List<string> warnings)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] files;
                string[] directories;
                try
                {
                    files = Directory.GetFiles(directory);
                    directories = Directory.GetDirectories(directory);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    warnings?.Add($"Cannot read '{directory}': {exception.Message}");
                    continue;
                }

                foreach (string file in files)
                {
                    yield return file;
                }

                foreach (string child in directories)
                {
                    if (skipDirectories == null || !Contains(skipDirectories, Path.GetFileName(child)))
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        public static IEnumerable<FileInfo> EnumerateFileInfosSafe(string root, ICollection<string> skipDirectories, List<string> warnings)
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(root));
            while (pending.Count > 0)
            {
                DirectoryInfo directory = pending.Pop();
                FileInfo[] files;
                DirectoryInfo[] directories;
                try
                {
                    files = directory.GetFiles();
                    directories = directory.GetDirectories();
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    warnings?.Add($"Cannot read '{directory.FullName}': {exception.Message}");
                    continue;
                }

                foreach (FileInfo file in files)
                {
                    yield return file;
                }

                foreach (DirectoryInfo child in directories)
                {
                    if (skipDirectories == null || !Contains(skipDirectories, child.Name))
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        private static bool Contains(ICollection<string> values, string value)
        {
            foreach (string item in values)
            {
                if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsArchiveSuffix(string suffix)
        {
            foreach (char ch in suffix)
            {
                if (!char.IsDigit(ch))
                {
                    return false;
                }
            }

            return suffix.Length <= 2;
        }
    }
}
