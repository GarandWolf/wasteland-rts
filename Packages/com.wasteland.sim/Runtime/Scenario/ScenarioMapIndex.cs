using System;
using System.IO;
using System.Text;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Scenario
{
    /// <summary>
    /// Territory and town index from the main map WRMB. Keys are compared as u64.
    /// Territory id 0 with a zero stable key is the unused slot and is omitted.
    /// </summary>
    internal sealed class ScenarioMapIndex
    {
        private const int TownRecordSize = 64;
        private const int StableKeyOffset = 40;

        private readonly ScenarioMapTerritory[] _territories;
        private readonly int[] _townIds;
        private readonly int[] _townTerritoryIds;
        private readonly string[] _townNames;

        private ScenarioMapIndex(
            ushort formatVersion,
            ScenarioMapTerritory[] territories,
            int[] townIds,
            int[] townTerritoryIds,
            string[] townNames)
        {
            FormatVersion = formatVersion;
            _territories = territories;
            _townIds = townIds;
            _townTerritoryIds = townTerritoryIds;
            _townNames = townNames;
        }

        public ushort FormatVersion { get; }

        public int TerritoryCount
        {
            get { return _territories.Length; }
        }

        public ScenarioMapTerritory TerritoryAt(int index)
        {
            return _territories[index];
        }

        public static ScenarioMapIndex Read(byte[] mapFileBytes)
        {
            WrmbFile file = WrmbFile.Parse(mapFileBytes);
            ScenarioMapTerritory[] territories = ReadTerritories(file);
            int[] townIds;
            int[] townTerritoryIds;
            string[] townNames;
            ReadTowns(file, out townIds, out townTerritoryIds, out townNames);
            return new ScenarioMapIndex(file.FileFormatVersion, territories, townIds, townTerritoryIds, townNames);
        }

        public bool TryGetTerritory(ulong stableKey, out ScenarioMapTerritory territory)
        {
            int lo = 0;
            int hi = _territories.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                ulong key = _territories[mid].StableKey;
                if (key == stableKey)
                {
                    territory = _territories[mid];
                    return true;
                }

                if (key < stableKey)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            territory = default;
            return false;
        }

        public bool TryGetTown(int townId, out int territoryId, out string name)
        {
            int lo = 0;
            int hi = _townIds.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int id = _townIds[mid];
                if (id == townId)
                {
                    territoryId = _townTerritoryIds[mid];
                    name = _townNames[mid];
                    return true;
                }

                if (id < townId)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            territoryId = 0;
            name = string.Empty;
            return false;
        }

        private static ScenarioMapTerritory[] ReadTerritories(WrmbFile file)
        {
            WrmbDirEntry terr = file.RequireSection("TERR");
            int expected = file.FileFormatVersion >= WrmbFile.FormatVersionV1
                ? Wrmb.TerrElementSizeV1
                : Wrmb.TerrElementSizeV0;
            if (terr.ElementSize != expected)
            {
                throw new InvalidDataException("TERR elementSize must be " + expected + ".");
            }

            byte[] data = file.SectionBytes(terr);
            ScenarioMapTerritory[] records = new ScenarioMapTerritory[terr.Count];
            int written = 0;
            BlobReader reader = new BlobReader(data);
            for (int i = 0; i < terr.Count; i++)
            {
                reader.Seek(i * terr.ElementSize);
                int id = reader.I32();
                int seatTownId = reader.I32();
                ulong stableKey = 0UL;
                if (terr.ElementSize >= Wrmb.TerrElementSizeV1)
                {
                    reader.Seek((i * terr.ElementSize) + StableKeyOffset);
                    stableKey = reader.U64();
                }

                if (stableKey == 0UL)
                {
                    if (id != 0)
                    {
                        throw new InvalidDataException("TERR id " + id + " is missing a stable key.");
                    }

                    continue;
                }

                records[written] = new ScenarioMapTerritory(id, seatTownId, stableKey);
                written++;
            }

            if (written == 0)
            {
                throw new InvalidDataException("TERR has no stable keys.");
            }

            ScenarioMapTerritory[] trimmed = new ScenarioMapTerritory[written];
            for (int i = 0; i < written; i++)
            {
                trimmed[i] = records[i];
            }

            Array.Sort(trimmed, CompareTerritory);
            for (int i = 1; i < trimmed.Length; i++)
            {
                if (trimmed[i].StableKey == trimmed[i - 1].StableKey)
                {
                    throw new InvalidDataException("Duplicate TERR stable key.");
                }
            }

            int[] ids = new int[trimmed.Length];
            for (int i = 0; i < trimmed.Length; i++)
            {
                ids[i] = trimmed[i].TerritoryId;
            }

            Array.Sort(ids);
            for (int i = 1; i < ids.Length; i++)
            {
                if (ids[i] == ids[i - 1])
                {
                    throw new InvalidDataException("Duplicate TERR id " + ids[i] + ".");
                }
            }

            return trimmed;
        }

        public bool TryGetTerritoryById(int territoryId, out ScenarioMapTerritory territory)
        {
            for (int i = 0; i < _territories.Length; i++)
            {
                if (_territories[i].TerritoryId == territoryId)
                {
                    territory = _territories[i];
                    return true;
                }
            }

            territory = default;
            return false;
        }

        private static int CompareTerritory(ScenarioMapTerritory a, ScenarioMapTerritory b)
        {
            if (a.StableKey < b.StableKey)
            {
                return -1;
            }

            if (a.StableKey > b.StableKey)
            {
                return 1;
            }

            return 0;
        }

        private static void ReadTowns(WrmbFile file, out int[] townIds, out int[] townTerritoryIds, out string[] townNames)
        {
            WrmbDirEntry towns = file.RequireSection("TOWN");
            WrmbDirEntry strp = file.RequireSection("STRP");
            if (towns.ElementSize != TownRecordSize)
            {
                throw new InvalidDataException("TOWN elementSize must be " + TownRecordSize + ".");
            }

            byte[] data = file.SectionBytes(towns);
            byte[] names = file.SectionBytes(strp);
            int count = towns.Count;
            townIds = new int[count];
            townTerritoryIds = new int[count];
            townNames = new string[count];
            BlobReader reader = new BlobReader(data);
            for (int i = 0; i < count; i++)
            {
                reader.Seek(i * TownRecordSize);
                int id = reader.I32();
                reader.Skip(8);
                int territoryId = reader.I32();
                reader.Skip(36);
                int nameOff = (int)reader.U32();
                int nameLen = (int)reader.U32();
                townIds[i] = id;
                townTerritoryIds[i] = territoryId;
                townNames[i] = ReadUtf8(names, nameOff, nameLen);
            }

            int[] order = new int[count];
            int[] idOrder = townIds;
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, delegate (int a, int b)
            {
                return idOrder[a].CompareTo(idOrder[b]);
            });

            int[] sortedIds = new int[count];
            int[] sortedTerritories = new int[count];
            string[] sortedNames = new string[count];
            for (int i = 0; i < count; i++)
            {
                int index = order[i];
                sortedIds[i] = townIds[index];
                sortedTerritories[i] = townTerritoryIds[index];
                sortedNames[i] = townNames[index];
            }

            for (int i = 1; i < count; i++)
            {
                if (sortedIds[i] == sortedIds[i - 1])
                {
                    throw new InvalidDataException("Duplicate TOWN id " + sortedIds[i] + ".");
                }
            }

            townIds = sortedIds;
            townTerritoryIds = sortedTerritories;
            townNames = sortedNames;
        }

        private static string ReadUtf8(byte[] strp, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > strp.Length)
            {
                throw new InvalidDataException("STRP span out of bounds.");
            }

            return Encoding.UTF8.GetString(strp, offset, length);
        }
    }
}
