using System.IO;

namespace RaidIntoDarkness.Settings
{
    public static class SettingsFile
    {
        public static string ResolvePath(bool editor, string dataPath, string persistentDataPath, string productName, string fileName)
        {
            return editor ? Path.Combine(Path.GetDirectoryName(persistentDataPath), productName, fileName) : Path.Combine(Path.GetDirectoryName(dataPath), "appdata", "settings", fileName);
        }

        public static void Write(string path, string json)
        {
            string temporaryPath = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
    }
}
