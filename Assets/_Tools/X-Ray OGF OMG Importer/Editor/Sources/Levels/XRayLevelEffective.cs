using System;
using System.Collections.Generic;
using System.IO;
using XRay.Importer.Core.IO;
using XRay.Importer.Core.Level;
using XRay.Importer.Sources.Vfs;

namespace XRay.Importer.Sources.Levels
{
    public enum XRayLevelProbeStatus
    {
        Ready,
        Incomplete,
        Incompatible,
        Unsupported,
        Unreadable
    }

    public sealed class XRayLevelProbe
    {
        public int Version { get; internal set; }
        public bool FormatSupported { get; internal set; }
        public bool GeometryChecked { get; internal set; }
        public bool Compatible { get; internal set; }
        public int Shaders { get; internal set; }
        public int Visuals { get; internal set; }
        public int Trees { get; internal set; }
        public int VertexBuffers { get; internal set; }
        public int IndexBuffers { get; internal set; }
        public int Lightmaps { get; internal set; }
        public bool HasDetails { get; internal set; }
        public bool HasCollision { get; internal set; }
        public bool HasWallmarks { get; internal set; }
        public bool HasLods { get; internal set; }
        public List<string> Errors { get; } = new List<string>();
        public List<string> MissingRequired { get; } = new List<string>();
        public List<string> MissingOptional { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();
        public bool DependenciesComplete => MissingRequired.Count == 0;

        public XRayLevelProbeStatus Status
        {
            get
            {
                if (Version == 0)
                {
                    return XRayLevelProbeStatus.Unreadable;
                }

                if (!FormatSupported)
                {
                    return XRayLevelProbeStatus.Unsupported;
                }

                if (!Compatible)
                {
                    return XRayLevelProbeStatus.Incompatible;
                }

                return DependenciesComplete ? XRayLevelProbeStatus.Ready : XRayLevelProbeStatus.Incomplete;
            }
        }

        public static XRayLevelProbe Run(XRayEffectiveGamedata gamedata, XRayLevelEntry entry)
        {
            var probe = new XRayLevelProbe();
            string folder = entry.VirtualFolder;
            byte[] data;
            try
            {
                data = gamedata.ReadAllBytes(folder + "/level");
            }
            catch (Exception exception) when (exception is XRaySourceException || exception is IOException)
            {
                probe.Errors.Add("level: " + exception.Message);
                return probe;
            }

            if (data.Length < 16 || !XRayLevelReader.IsLevelSignature(data))
            {
                probe.Errors.Add("level: the file has no X-Ray level header (chunk 0x1).");
                return probe;
            }

            probe.Version = XRayLevelReader.ReadVersion(data);
            XRayLevelData level;
            try
            {
                level = XRayLevelReader.Read(data, folder + "/level");
            }
            catch (XRayFormatException exception)
            {
                probe.Errors.Add(exception.Message);
                return probe;
            }

            probe.FormatSupported = true;
            probe.Compatible = true;
            probe.Shaders = level.Shaders.Count;
            probe.Visuals = level.Visuals.Count;
            var lightmaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XRayLevelShader shader in level.Shaders)
            {
                if (shader.Lightmaps.Count >= 2)
                {
                    lightmaps.Add(shader.Lightmaps[1]);
                }
            }

            foreach (XRayLevelVisual visual in level.Visuals)
            {
                if (visual.IsTree)
                {
                    probe.Trees++;
                }
            }

            probe.Lightmaps = lightmaps.Count;
            foreach (string lightmap in lightmaps)
            {
                if (!gamedata.Exists($"{folder}/{lightmap}.dds"))
                {
                    probe.MissingOptional.Add($"{folder}/{lightmap}.dds (hemisphere lightmap; materials fall back to the constant hemisphere)");
                }
            }

            if (!gamedata.Exists("shaders.xr"))
            {
                probe.MissingOptional.Add("shaders.xr (engine shader classes are guessed from names)");
            }

            probe.HasCollision = gamedata.Exists(folder + "/level.cform");
            probe.HasDetails = gamedata.Exists(folder + "/level.details");
            probe.HasWallmarks = gamedata.Exists(folder + "/level.wallmarks");
            probe.HasLods = gamedata.Exists(folder + "/level_lods.dds");
            if (!probe.HasCollision)
            {
                probe.MissingOptional.Add(folder + "/level.cform (collision and detail ground tests)");
            }

            string geometry = folder + "/level.geom";
            if (!gamedata.Exists(geometry))
            {
                probe.MissingRequired.Add(geometry + $" (xrLC {probe.Version} keeps vertex and index buffers in level.geom)");
                return probe;
            }

            CheckGeometry(gamedata, geometry, level, probe);
            return probe;
        }

        private static void CheckGeometry(XRayEffectiveGamedata gamedata, string path, XRayLevelData level, XRayLevelProbe probe)
        {
            XRayEffectiveFile levelFile = gamedata.Resolve(path.Substring(0, path.Length - ".geom".Length));
            XRayEffectiveFile geomFile = gamedata.Resolve(path);
            try
            {
                using (Stream stream = gamedata.OpenRead(path))
                using (XRayLevelGeometry geometry = XRayLevelGeometry.Open(stream, path))
                {
                    probe.GeometryChecked = true;
                    probe.VertexBuffers = geometry.VertexBuffers.Count;
                    probe.IndexBuffers = geometry.IndexBuffers.Count;
                    int broken = 0;
                    string first = null;
                    foreach (XRayLevelVisual visual in level.Visuals)
                    {
                        if (!visual.IsGeometry)
                        {
                            continue;
                        }

                        string problem = Validate(visual, geometry);
                        if (problem == null)
                        {
                            continue;
                        }

                        broken++;
                        first = first ?? problem;
                    }

                    if (broken > 0)
                    {
                        probe.Compatible = false;
                        probe.Errors.Add($"level ({levelFile?.Origin}) and level.geom ({geomFile?.Origin}) do not match: {broken} visuals reference missing geometry ({first}).");
                    }
                }
            }
            catch (Exception exception) when (exception is XRayFormatException || exception is XRaySourceException || exception is IOException)
            {
                probe.Compatible = false;
                probe.Errors.Add($"level.geom ({geomFile?.Origin}): {exception.Message}");
            }

            if (levelFile != null && geomFile != null && levelFile.Origin != geomFile.Origin)
            {
                probe.Notes.Add($"level comes from '{levelFile.Origin}', level.geom from '{geomFile.Origin}'.");
            }
        }

        private static string Validate(XRayLevelVisual visual, XRayLevelGeometry geometry)
        {
            XRayGeometryRange range = visual.Geometry;
            if (range.VertexBuffer < 0 || range.VertexBuffer >= geometry.VertexBuffers.Count)
            {
                return $"visual {visual.Index}: vertex buffer {range.VertexBuffer} of {geometry.VertexBuffers.Count}";
            }

            if ((long)range.VertexOffset + range.VertexCount > geometry.VertexBuffers[range.VertexBuffer].Count)
            {
                return $"visual {visual.Index}: vertices {range.VertexOffset}+{range.VertexCount} exceed buffer {range.VertexBuffer}";
            }

            if (range.IndexBuffer < 0 || range.IndexBuffer >= geometry.IndexBuffers.Count)
            {
                return $"visual {visual.Index}: index buffer {range.IndexBuffer} of {geometry.IndexBuffers.Count}";
            }

            if ((long)range.IndexOffset + range.IndexCount > geometry.IndexBuffers[range.IndexBuffer].Count)
            {
                return $"visual {visual.Index}: indices {range.IndexOffset}+{range.IndexCount} exceed buffer {range.IndexBuffer}";
            }

            if (visual.SwiContainer >= geometry.SlideWindows.Count)
            {
                return $"visual {visual.Index}: slide window container {visual.SwiContainer} of {geometry.SlideWindows.Count}";
            }

            return null;
        }
    }

    public sealed class XRayLevelRegistry
    {
        public const string GAME_LEVELS = "configs/game_levels.ltx";
        public const string MOD_GAME_PREFIX = "configs/mod_game_";
        public const string LEVELS_SECTION = "levels";

        private readonly Dictionary<string, string> _registered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<string> Files { get; } = new List<string>();
        public int Count => _registered.Count;

        public bool TryGet(string level, out string origin)
        {
            return _registered.TryGetValue(level, out origin);
        }

        public static XRayLevelRegistry Read(XRayEffectiveGamedata gamedata)
        {
            var registry = new XRayLevelRegistry();
            var files = new List<string>();
            if (gamedata.Exists(GAME_LEVELS))
            {
                files.Add(GAME_LEVELS);
            }

            var mods = new List<string>();
            foreach (XRayEffectiveFile file in gamedata.Enumerate("configs", false))
            {
                if (file.VirtualPath.StartsWith(MOD_GAME_PREFIX, StringComparison.Ordinal) && file.VirtualPath.EndsWith(".ltx", StringComparison.Ordinal))
                {
                    mods.Add(file.VirtualPath);
                }
            }

            mods.Sort(StringComparer.Ordinal);
            files.AddRange(mods);
            var list = new List<string>();
            var names = new Dictionary<string, (string Name, string Origin)>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                XRayEffectiveFile file = gamedata.Resolve(path);
                byte[] data;
                try
                {
                    data = gamedata.ReadAllBytes(path);
                }
                catch (Exception exception) when (exception is XRaySourceException || exception is IOException)
                {
                    registry.Files.Add($"{path}: {exception.Message}");
                    continue;
                }

                registry.Files.Add($"{path} ({file.Origin})");
                Parse(Cp1251.Decode(data, 0, data.Length), file.Origin, list, names);
            }

            foreach (string section in list)
            {
                if (names.TryGetValue(section, out (string Name, string Origin) level) && !registry._registered.ContainsKey(level.Name))
                {
                    registry._registered.Add(level.Name, level.Origin);
                }
            }

            return registry;
        }

        private static void Parse(string text, string origin, List<string> list, Dictionary<string, (string Name, string Origin)> names)
        {
            string section = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = StripComment(raw).Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                int open = line.IndexOf('[');
                int close = line.IndexOf(']');
                if (open >= 0 && close > open && open <= 2 && line.Substring(0, open).Trim('!', '@').Length == 0)
                {
                    string prefix = line.Substring(0, open);
                    section = line.Substring(open + 1, close - open - 1).Trim();
                    if (string.Equals(section, LEVELS_SECTION, StringComparison.OrdinalIgnoreCase) && (prefix.Length == 0 || prefix == "!!"))
                    {
                        list.Clear();
                    }

                    continue;
                }

                if (section == null)
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                string key = (separator >= 0 ? line.Substring(0, separator) : line).Trim();
                string value = separator >= 0 ? line.Substring(separator + 1).Trim().Trim('"') : string.Empty;
                if (string.Equals(section, LEVELS_SECTION, StringComparison.OrdinalIgnoreCase))
                {
                    if (key.StartsWith("!", StringComparison.Ordinal))
                    {
                        list.RemoveAll(item => string.Equals(item, key.Substring(1), StringComparison.OrdinalIgnoreCase));
                    }
                    else if (!list.Exists(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(key);
                    }

                    continue;
                }

                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                {
                    names[section] = (value, origin);
                }
            }
        }

        private static string StripComment(string line)
        {
            int semicolon = line.IndexOf(';');
            int slashes = line.IndexOf("//", StringComparison.Ordinal);
            int cut = semicolon < 0 ? slashes : slashes < 0 ? semicolon : Math.Min(semicolon, slashes);
            return cut >= 0 ? line.Substring(0, cut) : line;
        }
    }

    public static class XRayLevelEffectiveCatalog
    {
        public const string LEVELS_PREFIX = "levels/";

        public static XRayLevelGamedataInfo Inspect(XRayAssetIndex index, string description)
        {
            var gamedata = new XRayEffectiveGamedata(index);
            var info = new XRayLevelGamedataInfo { RequestedFolder = description, Effective = gamedata, LevelsFolder = "levels (effective gamedata)" };
            var entries = new Dictionary<string, XRayLevelEntry>(StringComparer.Ordinal);
            foreach (XRayIndexedFile file in index.Files)
            {
                string[] parts = file.VirtualPath.Split('/');
                if (parts.Length != 3 || parts[0] != "levels" || parts[2] != XRayLevelGamedata.LEVEL_FILE)
                {
                    continue;
                }

                XRayAssetSource source = index.GetSource(file.Winner);
                bool baseCandidate = false;
                foreach (XRayIndexCandidate candidate in file.Candidates)
                {
                    baseCandidate |= IsBase(index.GetSource(candidate));
                }

                string[] display = file.Winner.RelativePath.Replace('\\', '/').Split('/');
                var entry = new XRayLevelEntry
                {
                    Name = display.Length >= 2 ? display[display.Length - 2] : parts[1],
                    VirtualFolder = LEVELS_PREFIX + parts[1],
                    LevelPath = index.Describe(file.Winner),
                    LevelLength = file.Winner.Size,
                    Origin = source.Origin,
                    OriginKind = source.Kind,
                    OriginCandidates = file.Candidates.Count,
                    IsModded = !baseCandidate,
                    IsReplaced = baseCandidate && !IsBase(source)
                };
                entries[parts[1]] = entry;
            }

            foreach (XRayIndexedFile file in index.Files)
            {
                if (!file.VirtualPath.StartsWith(LEVELS_PREFIX, StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = file.VirtualPath.Split('/');
                if (parts.Length < 3 || !entries.TryGetValue(parts[1], out XRayLevelEntry entry))
                {
                    continue;
                }

                if (parts.Length == 3)
                {
                    string relative = file.Winner.RelativePath.Replace('\\', '/');
                    entry.Files.Add(relative.Substring(relative.LastIndexOf('/') + 1));
                    continue;
                }

                entry.Subfolders.TryGetValue(parts[2], out int count);
                entry.Subfolders[parts[2]] = count + 1;
            }

            XRayLevelRegistry registry = XRayLevelRegistry.Read(gamedata);
            foreach (XRayLevelEntry entry in entries.Values)
            {
                entry.Files.Sort(StringComparer.OrdinalIgnoreCase);
                entry.IsRegistered = registry.TryGet(entry.Name, out string origin);
                entry.RegisteredBy = origin;
                info.Levels.Add(entry);
            }

            info.Levels.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (info.Levels.Count == 0)
            {
                info.Errors.Add("The effective gamedata contains no 'levels/<name>/level' files.");
            }

            if (!gamedata.Exists("shaders.xr"))
            {
                info.Warnings.Add("shaders.xr is missing; engine shader classes are guessed from shader names.");
            }

            if (!gamedata.Exists("gamemtl.xr"))
            {
                info.Warnings.Add("gamemtl.xr is missing; passable collision materials cannot be excluded from detail placement.");
            }

            if (registry.Count == 0)
            {
                info.Warnings.Add($"{XRayLevelRegistry.GAME_LEVELS} was not found or lists no levels; registration status is unknown.");
            }

            return info;
        }

        public static bool IsBase(XRayAssetSource source)
        {
            return source.Kind == XRayAssetSourceKind.GameGamedata || (source.Kind == XRayAssetSourceKind.Archive && source.Origin == Mo2ArchiveLayout.GAME_FOLDER_ORIGIN);
        }
    }
}
