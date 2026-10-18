using System;
using System.Collections.Generic;
using System.IO;
using XRay.Importer.Core.IO;
using XRay.Importer.Sources.Ltx;

namespace RaidIntoDarkness.Player.Editor
{
    public sealed class StalkerGameMtlPair
    {
        public StalkerGameMtlPair(int material0, int material1, string stepSounds, string origin)
        {
            Material0 = material0;
            Material1 = material1;
            StepSounds = stepSounds;
            Origin = origin;
        }

        public int Material0 { get; }
        public int Material1 { get; }
        public string StepSounds { get; set; }
        public string Origin { get; set; }
    }

    public sealed class StalkerGameMtlLibrary
    {
        public const string GAMEMTL = "gamemtl.xr";
        public const string PAIRS_FOLDER = "materials";
        public const string PAIRS_FILE = "materials/material_pairs.ltx";

        private const int GAMEMTLS_CHUNK_MTLS = 0x1002;
        private const int GAMEMTLS_CHUNK_MTLS_PAIR = 0x1003;
        private const int GAMEMTL_CHUNK_MAIN = 0x1000;
        private const int GAMEMTLPAIR_CHUNK_PAIR = 0x1000;
        private const int GAMEMTLPAIR_CHUNK_STEP = 0x1003;
        private const string STEP_SOUNDS = "step_sounds";
        private const string MOD_PREFIX = "mod_material_pairs";
        private const string INCLUDE_PREFIX = "material_pairs_";
        private const string MODDED_EXES_ARCHIVE = "db/mods/00_modded_exes_gamedata.db0";

        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private readonly Dictionary<string, int> _ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<StalkerGameMtlPair> _pairs = new List<StalkerGameMtlPair>();

        public IReadOnlyDictionary<int, string> Names => _names;
        public IReadOnlyList<StalkerGameMtlPair> Pairs => _pairs;
        public List<string> Log { get; } = new List<string>();

        public static StalkerGameMtlLibrary Load(StalkerXRaySources sources)
        {
            byte[] data = sources.ReadBytes(GAMEMTL);
            if (data == null)
            {
                return null;
            }

            var library = new StalkerGameMtlLibrary();
            library.Log.Add($"gamemtl.xr <- {sources.GetSourceName(GAMEMTL)}");
            library.Read(data);
            library.ApplyPairOverrides(sources);
            return library;
        }

        public bool TryGetId(string name, out int id)
        {
            return _ids.TryGetValue(name, out id);
        }

        public StalkerGameMtlPair FindPair(string material0, string material1)
        {
            if (!_ids.TryGetValue(material0, out int a) || !_ids.TryGetValue(material1, out int b))
            {
                return null;
            }

            StalkerGameMtlPair found = null;
            foreach (StalkerGameMtlPair pair in _pairs)
            {
                if ((pair.Material0 == a && pair.Material1 == b) || (pair.Material0 == b && pair.Material1 == a))
                {
                    found = pair;
                }
            }

            return found;
        }

        public static List<string> SplitSounds(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            foreach (string item in value.Split(','))
            {
                string trimmed = item.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        private void Read(byte[] data)
        {
            foreach (KeyValuePair<int, ArraySegment<byte>> root in Chunks(data, 0, data.Length))
            {
                if (root.Key == GAMEMTLS_CHUNK_MTLS)
                {
                    foreach (KeyValuePair<int, ArraySegment<byte>> material in Chunks(data, root.Value.Offset, root.Value.Count))
                    {
                        ReadMaterial(data, material.Value);
                    }
                }
                else if (root.Key == GAMEMTLS_CHUNK_MTLS_PAIR)
                {
                    foreach (KeyValuePair<int, ArraySegment<byte>> pair in Chunks(data, root.Value.Offset, root.Value.Count))
                    {
                        ReadPair(data, pair.Value);
                    }
                }
            }

            Log.Add($"gamemtl.xr: {_names.Count} materials, {_pairs.Count} pairs");
        }

        private void ReadMaterial(byte[] data, ArraySegment<byte> chunk)
        {
            foreach (KeyValuePair<int, ArraySegment<byte>> part in Chunks(data, chunk.Offset, chunk.Count))
            {
                if (part.Key == GAMEMTL_CHUNK_MAIN)
                {
                    int id = BitConverter.ToInt32(data, part.Value.Offset);
                    string name = ReadString(data, part.Value.Offset + 4, part.Value.Offset + part.Value.Count, out _);
                    _names[id] = name;
                    _ids[name] = id;
                }
            }
        }

        private void ReadPair(byte[] data, ArraySegment<byte> chunk)
        {
            int material0 = -1;
            int material1 = -1;
            string step = null;
            foreach (KeyValuePair<int, ArraySegment<byte>> part in Chunks(data, chunk.Offset, chunk.Count))
            {
                if (part.Key == GAMEMTLPAIR_CHUNK_PAIR)
                {
                    material0 = BitConverter.ToInt32(data, part.Value.Offset);
                    material1 = BitConverter.ToInt32(data, part.Value.Offset + 4);
                }
                else if (part.Key == GAMEMTLPAIR_CHUNK_STEP)
                {
                    step = ReadString(data, part.Value.Offset, part.Value.Offset + part.Value.Count, out _);
                }
            }

            if (material0 >= 0 && material1 >= 0)
            {
                _pairs.Add(new StalkerGameMtlPair(material0, material1, step ?? string.Empty, GAMEMTL));
            }
        }

        private void ApplyPairOverrides(StalkerXRaySources sources)
        {
            if (sources.Exists(PAIRS_FILE))
            {
                Apply(XRayLtxDatabase.LoadFile(sources.Index, PAIRS_FILE), PAIRS_FILE + " (" + sources.GetSourceName(PAIRS_FILE) + ")");
            }
            else if (!string.IsNullOrEmpty(sources.GamePath) && File.Exists(Path.Combine(sources.GamePath, MODDED_EXES_ARCHIVE)))
            {
                Log.Add($"materials\\material_pairs.ltx comes from {MODDED_EXES_ARCHIVE} (archive, not indexed); its body is '#include \"material_pairs_*.ltx\"', so the loose includes are applied in name order.");
                foreach (string include in sources.FindFiles(PAIRS_FOLDER, INCLUDE_PREFIX, ".ltx"))
                {
                    Apply(XRayLtxDatabase.LoadFile(sources.Index, include), include + " (" + sources.GetSourceName(include) + ")");
                }
            }
            else
            {
                Log.Add("materials\\material_pairs.ltx is absent; gamemtl.xr pairs are used as is.");
                return;
            }

            foreach (string mod in sources.FindFiles(PAIRS_FOLDER, MOD_PREFIX, ".ltx"))
            {
                Apply(XRayLtxDatabase.LoadFile(sources.Index, mod), mod + " (" + sources.GetSourceName(mod) + ")");
            }
        }

        private void Apply(XRayLtxDatabase database, string origin)
        {
            int changed = 0;
            int unknown = 0;
            foreach (XRayLtxSection section in database.Sections.Values)
            {
                string[] materials = section.Name.Split('@');
                if (materials.Length < 2 || !section.Values.TryGetValue(STEP_SOUNDS, out string step))
                {
                    continue;
                }

                if (!_ids.TryGetValue(materials[0].Trim(), out int a) || !_ids.TryGetValue(materials[1].Trim(), out int b))
                {
                    unknown++;
                    continue;
                }

                StalkerGameMtlPair pair = _pairs.Find(item => item.Material0 == a && item.Material1 == b);
                if (pair == null)
                {
                    pair = new StalkerGameMtlPair(a, b, string.Empty, origin);
                    _pairs.Add(pair);
                }

                pair.StepSounds = step ?? string.Empty;
                pair.Origin = origin;
                changed++;
            }

            Log.Add($"{origin}: {changed} pair step_sounds overrides{(unknown > 0 ? $", {unknown} pairs with materials outside gamemtl.xr skipped" : string.Empty)}");
        }

        private static string ReadString(byte[] data, int start, int limit, out int next)
        {
            int end = start;
            while (end < limit && data[end] != 0)
            {
                end++;
            }

            next = end + 1;
            return Cp1251.Decode(data, start, end - start);
        }

        private static IEnumerable<KeyValuePair<int, ArraySegment<byte>>> Chunks(byte[] data, int offset, int count)
        {
            int position = offset;
            int end = offset + count;
            while (position + 8 <= end)
            {
                int id = BitConverter.ToInt32(data, position) & 0x7fffffff;
                int size = BitConverter.ToInt32(data, position + 4);
                if (size < 0 || position + 8 + size > end)
                {
                    yield break;
                }

                yield return new KeyValuePair<int, ArraySegment<byte>>(id, new ArraySegment<byte>(data, position + 8, size));
                position += 8 + size;
            }
        }
    }
}
