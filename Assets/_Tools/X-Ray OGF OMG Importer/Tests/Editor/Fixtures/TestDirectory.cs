using System;
using System.IO;
using System.Text;

namespace XRay.Importer.Tests
{
    public sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "XRayImporterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Combine(string relative)
        {
            return System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar).Replace('\\', System.IO.Path.DirectorySeparatorChar));
        }

        public string WriteFile(string relative, byte[] data)
        {
            string path = Combine(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
            return path;
        }

        public string WriteText(string relative, string text)
        {
            return WriteFile(relative, new UTF8Encoding(false).GetBytes(text));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
