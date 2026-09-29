using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Ai
{
    public readonly struct AiTownRecord
    {
        public readonly int Id;
        public readonly int TerritoryId;
        public readonly int NameHash;
        public readonly int XCm;
        public readonly int YCm;
        public readonly int RoadNodeId;
        public readonly int StrategicValue;
        public readonly int PopulationClass;
        public readonly int Population;

        public AiTownRecord(
            int id,
            int territoryId,
            int nameHash,
            int xCm,
            int yCm,
            int roadNodeId,
            int strategicValue,
            int populationClass,
            int population)
        {
            Id = id;
            TerritoryId = territoryId;
            NameHash = nameHash;
            XCm = xCm;
            YCm = yCm;
            RoadNodeId = roadNodeId;
            StrategicValue = strategicValue;
            PopulationClass = populationClass;
            Population = population;
        }

        public bool IsSeatClassTownOrCity
        {
            get { return PopulationClass >= 1; }
        }
    }

    public readonly struct AiTerritoryRecord
    {
        public readonly int Id;
        public readonly int SeatTownId;
        public readonly ulong StableKey;

        public AiTerritoryRecord(int id, int seatTownId, ulong stableKey)
        {
            Id = id;
            SeatTownId = seatTownId;
            StableKey = stableKey;
        }
    }

    public readonly struct AiChokepointRecord
    {
        public readonly int Id;
        public readonly byte DefenseGroupId;
        public readonly byte BypassWheeledBlocked;
        public readonly int BypassMaxGradePpt;
        public readonly int SideATerritoryId;
        public readonly int SideBTerritoryId;
        public readonly int XCm;
        public readonly int YCm;
        public readonly int RoadNodeId;
        public readonly ulong StableKey;

        public AiChokepointRecord(
            int id,
            byte defenseGroupId,
            byte bypassWheeledBlocked,
            int bypassMaxGradePpt,
            int sideATerritoryId,
            int sideBTerritoryId,
            int xCm,
            int yCm,
            int roadNodeId,
            ulong stableKey)
        {
            Id = id;
            DefenseGroupId = defenseGroupId;
            BypassWheeledBlocked = bypassWheeledBlocked;
            BypassMaxGradePpt = bypassMaxGradePpt;
            SideATerritoryId = sideATerritoryId;
            SideBTerritoryId = sideBTerritoryId;
            XCm = xCm;
            YCm = yCm;
            RoadNodeId = roadNodeId;
            StableKey = stableKey;
        }

        public bool HasDefenseGroup
        {
            get { return DefenseGroupId != Wrmb.NoDefenseGroup; }
        }

        public bool IsBorder
        {
            get
            {
                return SideATerritoryId != 0
                    && SideBTerritoryId != 0
                    && SideATerritoryId != SideBTerritoryId;
            }
        }

        public bool IsHardPost
        {
            get { return IsBorder && BypassMaxGradePpt == 65535; }
        }
    }

    /// <summary>
    /// Map ingest for M1: towns, seats, neighbour lists, defense groups,
    /// wheeled-bypass flags, node territory ids, and stable u64 keys.
    /// v0 still builds <see cref="RoadConnectedTerritoryAdjacency"/>.
    /// Territory and chokepoint sizes are validated through
    /// <see cref="Wrmb.ReadTerritories"/> / <see cref="Wrmb.ReadChokepoints"/>.
    /// </summary>
    public sealed class AiMapSnapshot
    {
        public const int TownRecordSize = 64;
        public const int TerritoryRecordSizeV0 = 40;
        public const int TerritoryRecordSizeV1 = 48;
        public const int RoadEdgeRecordSize = 64;
        public const int ChokepointRecordSizeV0 = 80;
        public const int ChokepointRecordSizeV1 = 88;

        private readonly AiTownRecord[] _towns;
        private readonly AiTerritoryRecord[] _territories;
        private readonly AiChokepointRecord[] _chokepoints;

        private AiMapSnapshot(
            ushort formatVersion,
            ulong contentHash,
            ulong mapKey,
            int theDallesTownId,
            int theDallesTerritoryId,
            int goldendaleTownId,
            int hoodRiverTownId,
            int whiteSalmonTownId,
            int klickitatTownId,
            int wheeledBlockedCount,
            AiTownRecord[] towns,
            AiTerritoryRecord[] territories,
            AiChokepointRecord[] chokepoints,
            RoadGraph roads,
            ITerritoryAdjacency adjacency)
        {
            FormatVersion = formatVersion;
            ContentHash = contentHash;
            MapKey = mapKey;
            TheDallesTownId = theDallesTownId;
            TheDallesTerritoryId = theDallesTerritoryId;
            GoldendaleTownId = goldendaleTownId;
            HoodRiverTownId = hoodRiverTownId;
            WhiteSalmonTownId = whiteSalmonTownId;
            KlickitatTownId = klickitatTownId;
            WheeledBlockedCount = wheeledBlockedCount;
            _towns = towns;
            _territories = territories;
            _chokepoints = chokepoints;
            Roads = roads;
            Adjacency = adjacency;
        }

        public ushort FormatVersion { get; }

        public ulong ContentHash { get; }

        public ulong MapKey { get; }

        public int TheDallesTownId { get; }

        public int TheDallesTerritoryId { get; }

        public int GoldendaleTownId { get; }

        public int HoodRiverTownId { get; }

        public int WhiteSalmonTownId { get; }

        public int KlickitatTownId { get; }

        public int WheeledBlockedCount { get; }

        public ITerritoryAdjacency Adjacency { get; }

        public RoadGraph Roads { get; }

        public bool UsesMapNeighborLists
        {
            get { return Adjacency.SourceName == AiConstants.MapNeighborListAdjacencySource; }
        }

        public int TownCount
        {
            get { return _towns.Length; }
        }

        public int TerritoryCount
        {
            get { return _territories.Length; }
        }

        public int ChokepointCount
        {
            get { return _chokepoints.Length; }
        }

        public AiTownRecord TownAt(int index)
        {
            return _towns[index];
        }

        public AiTerritoryRecord TerritoryAt(int index)
        {
            return _territories[index];
        }

        public AiChokepointRecord ChokepointAt(int index)
        {
            return _chokepoints[index];
        }

        public bool TryGetTown(int townId, out AiTownRecord town)
        {
            int index = IndexOfTown(townId);
            if (index < 0)
            {
                town = default;
                return false;
            }

            town = _towns[index];
            return true;
        }

        public int IndexOfTown(int townId)
        {
            int lo = 0;
            int hi = _towns.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int id = _towns[mid].Id;
                if (id == townId)
                {
                    return mid;
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

            return -1;
        }

        public bool TryGetTerritory(int territoryId, out AiTerritoryRecord territory)
        {
            int index = IndexOfTerritory(territoryId);
            if (index < 0)
            {
                territory = default;
                return false;
            }

            territory = _territories[index];
            return true;
        }

        public int IndexOfTerritory(int territoryId)
        {
            int lo = 0;
            int hi = _territories.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int id = _territories[mid].Id;
                if (id == territoryId)
                {
                    return mid;
                }

                if (id < territoryId)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return -1;
        }

        public bool TryGetChokepoint(int chokepointId, out AiChokepointRecord chokepoint)
        {
            for (int i = 0; i < _chokepoints.Length; i++)
            {
                if (_chokepoints[i].Id == chokepointId)
                {
                    chokepoint = _chokepoints[i];
                    return true;
                }
            }

            chokepoint = default;
            return false;
        }

        public static int IncomePerSecondForClass(int populationClass)
        {
            if (populationClass <= 0)
            {
                return AiConstants.IncomeVillagePerSecond;
            }

            if (populationClass == 1)
            {
                return AiConstants.IncomeTownPerSecond;
            }

            return AiConstants.IncomeCityPerSecond;
        }

        public static AiMapSnapshot FromWrmb(byte[] mapBlob)
        {
            if (mapBlob == null)
            {
                throw new ArgumentNullException(nameof(mapBlob));
            }

            WrmbFile file = WrmbFile.Parse(mapBlob);
            ulong contentHash = HashBytes(file.ContentHash);
            ulong mapKey = global::Wasteland.Sim.Movement.MapKey.For(file, AiConstants.MapKeySectionTags());
            RoadGraph roads = Wrmb.ReadRoadGraph(file);
            Territory[] wrmbTerritories = Wrmb.ReadTerritories(file);
            _ = Wrmb.ReadChokepoints(file);

            WrmbDirEntry townEntry = file.RequireSection("TOWN");
            WrmbDirEntry terrEntry = file.RequireSection("TERR");
            WrmbDirEntry i32pEntry = file.RequireSection("I32P");
            WrmbDirEntry strpEntry = file.RequireSection("STRP");

            if (townEntry.ElementSize != TownRecordSize)
            {
                throw new InvalidDataException("TOWN elementSize must be 64.");
            }

            bool v1 = file.FileFormatVersion >= WrmbFile.FormatVersionV1;
            int expectedTerr = v1 ? TerritoryRecordSizeV1 : TerritoryRecordSizeV0;
            if (terrEntry.ElementSize != expectedTerr)
            {
                throw new InvalidDataException("TERR elementSize must be " + expectedTerr + ".");
            }

            if (wrmbTerritories.Length != terrEntry.Count)
            {
                throw new InvalidDataException("TERR record count does not match Wrmb.ReadTerritories.");
            }

            int[] i32p = ReadInt32Pool(file.SectionBytes(i32pEntry));
            byte[] strp = file.SectionBytes(strpEntry);
            int[] townNeighborOffsets = new int[townEntry.Count];
            int[] townNeighborCounts = new int[townEntry.Count];

            AiTownRecord[] towns = new AiTownRecord[townEntry.Count];
            int theDallesTownId = 0;
            int theDallesTerritoryId = 0;
            int goldendaleTownId = 0;
            int hoodRiverTownId = 0;
            int whiteSalmonTownId = 0;
            int klickitatTownId = 0;
            BlobReader townReader = new BlobReader(file.SectionBytes(townEntry));
            int previousTownId = -1;
            for (int i = 0; i < townEntry.Count; i++)
            {
                int id = townReader.I32();
                int xCm = townReader.I32();
                int yCm = townReader.I32();
                int territoryId = townReader.I32();
                int nameHash = townReader.I32();
                int population = townReader.I32();
                townReader.Skip(4);
                int roadNodeId = townReader.I32();
                short strategicValue = townReader.I16();
                sbyte populationClass = townReader.I8();
                townReader.U8();
                int neighborOff = (int)townReader.U32();
                int neighborCount = (int)townReader.U32();
                townReader.Skip(8);
                int nameOff = (int)townReader.U32();
                int nameLen = (int)townReader.U32();
                townReader.Skip(4);
                townNeighborOffsets[i] = neighborOff;
                townNeighborCounts[i] = neighborCount;
                if (i > 0 && id <= previousTownId)
                {
                    throw new InvalidDataException("TOWN ids must be sorted unique.");
                }

                previousTownId = id;
                if (strategicValue < 0 || strategicValue > 1000)
                {
                    throw new InvalidDataException("TOWN strategicValue out of range.");
                }

                towns[i] = new AiTownRecord(
                    id,
                    territoryId,
                    nameHash,
                    xCm,
                    yCm,
                    roadNodeId,
                    strategicValue,
                    populationClass,
                    population);
                string name = ReadUtf8(strp, nameOff, nameLen);
                if (name == AiConstants.TheDallesTownName)
                {
                    theDallesTownId = id;
                    theDallesTerritoryId = territoryId;
                }
                else if (name == AiConstants.GoldendaleTownName)
                {
                    goldendaleTownId = id;
                }
                else if (name == AiConstants.HoodRiverTownName)
                {
                    hoodRiverTownId = id;
                }
                else if (name == AiConstants.WhiteSalmonTownName)
                {
                    whiteSalmonTownId = id;
                }
                else if (name == AiConstants.KlickitatTownName)
                {
                    klickitatTownId = id;
                }
            }

            AiTerritoryRecord[] territories = new AiTerritoryRecord[terrEntry.Count];
            int[] territoryIds = new int[terrEntry.Count];
            int[][] neighborArrays = new int[terrEntry.Count][];
            byte[] terrBytes = file.SectionBytes(terrEntry);
            BlobReader terrReader = new BlobReader(terrBytes);
            int previousTerrId = -1;
            for (int i = 0; i < terrEntry.Count; i++)
            {
                terrReader.Seek(i * terrEntry.ElementSize);
                int id = terrReader.I32();
                int seatTownId = terrReader.I32();
                if (id != wrmbTerritories[i].Id)
                {
                    throw new InvalidDataException("TERR id does not match Wrmb.ReadTerritories.");
                }

                terrReader.Skip(24);
                int neighborOff = (int)terrReader.U32();
                int neighborCount = (int)terrReader.U32();
                ulong stableKey = 0;
                if (v1)
                {
                    stableKey = terrReader.U64();
                }

                if (i > 0 && id <= previousTerrId)
                {
                    throw new InvalidDataException("TERR ids must be sorted unique.");
                }

                previousTerrId = id;
                if (neighborOff < 0 || neighborCount < 0 || neighborOff + neighborCount > i32p.Length)
                {
                    throw new InvalidDataException("TERR neighborTerritoryIds span is invalid.");
                }

                int[] neighbors = new int[neighborCount];
                for (int n = 0; n < neighborCount; n++)
                {
                    neighbors[n] = i32p[neighborOff + n];
                }

                territoryIds[i] = id;
                neighborArrays[i] = neighbors;
                territories[i] = new AiTerritoryRecord(id, seatTownId, stableKey);
            }

            AiChokepointRecord[] chokepoints = ReadChokepoints(file, v1, out int wheeledBlocked);

            ITerritoryAdjacency adjacency;
            if (v1)
            {
                adjacency = new MapNeighborListAdjacency(territoryIds, neighborArrays);
            }
            else
            {
                WrmbDirEntry redgEntry = file.RequireSection("REDG");
                if (redgEntry.ElementSize != RoadEdgeRecordSize)
                {
                    throw new InvalidDataException("REDG elementSize must be 64.");
                }

                adjacency = BuildRoadConnected(
                    territories,
                    towns,
                    i32p,
                    townNeighborOffsets,
                    townNeighborCounts,
                    file.SectionBytes(redgEntry),
                    redgEntry.Count);
            }

            return new AiMapSnapshot(
                file.FileFormatVersion,
                contentHash,
                mapKey,
                theDallesTownId,
                theDallesTerritoryId,
                goldendaleTownId,
                hoodRiverTownId,
                whiteSalmonTownId,
                klickitatTownId,
                wheeledBlocked,
                towns,
                territories,
                chokepoints,
                roads,
                adjacency);
        }

        private static AiChokepointRecord[] ReadChokepoints(WrmbFile file, bool v1, out int wheeledBlocked)
        {
            wheeledBlocked = 0;
            if (!file.TryGetSection("CHOK", out WrmbDirEntry chok))
            {
                return Array.Empty<AiChokepointRecord>();
            }

            int expected = v1 ? ChokepointRecordSizeV1 : ChokepointRecordSizeV0;
            if (chok.ElementSize != expected)
            {
                throw new InvalidDataException("CHOK elementSize must be " + expected + ".");
            }

            AiChokepointRecord[] records = new AiChokepointRecord[chok.Count];
            BlobReader r = new BlobReader(file.SectionBytes(chok));
            for (int i = 0; i < chok.Count; i++)
            {
                r.Seek(i * chok.ElementSize);
                int id = r.I32();
                r.U8();
                r.U8();
                r.U8();
                byte defenseGroupId = r.U8();
                int x = r.I32();
                int y = r.I32();
                r.I32();
                int roadNodeId = r.I32();
                r.I32();
                r.Skip(16);
                int sideA = r.I32();
                int sideB = r.I32();
                r.Skip(24);
                int bypassMaxGradePpt = r.U16();
                r.U8();
                byte bypassWheeled = r.U8();
                ulong stableKey = 0;
                if (chok.ElementSize >= ChokepointRecordSizeV1)
                {
                    stableKey = r.U64();
                }

                if (bypassWheeled != 0)
                {
                    wheeledBlocked++;
                }

                records[i] = new AiChokepointRecord(
                    id,
                    defenseGroupId,
                    bypassWheeled,
                    bypassMaxGradePpt,
                    sideA,
                    sideB,
                    x,
                    y,
                    roadNodeId,
                    stableKey);
            }

            return records;
        }

        private static ITerritoryAdjacency BuildRoadConnected(
            AiTerritoryRecord[] territories,
            AiTownRecord[] towns,
            int[] i32p,
            int[] townNeighborOffsets,
            int[] townNeighborCounts,
            byte[] redgBytes,
            int edgeCount)
        {
            int[] territoryIds = new int[territories.Length];
            for (int i = 0; i < territories.Length; i++)
            {
                territoryIds[i] = territories[i].Id;
            }

            int maxNodeId = 0;
            BlobReader pass1 = new BlobReader(redgBytes);
            for (int i = 0; i < edgeCount; i++)
            {
                pass1.Skip(4);
                int a = pass1.I32();
                int b = pass1.I32();
                pass1.Skip(52);
                if (a > maxNodeId)
                {
                    maxNodeId = a;
                }

                if (b > maxNodeId)
                {
                    maxNodeId = b;
                }
            }

            List<int>[] nodeTerritories = new List<int>[maxNodeId + 1];
            BlobReader pass2 = new BlobReader(redgBytes);
            for (int i = 0; i < edgeCount; i++)
            {
                pass2.Skip(4);
                int a = pass2.I32();
                int b = pass2.I32();
                pass2.Skip(24);
                int territoryId = pass2.I32();
                pass2.Skip(24);
                if (territoryId == 0)
                {
                    continue;
                }

                if (IndexOfSorted(territoryIds, territoryId) < 0)
                {
                    continue;
                }

                AddUniqueSorted(ref nodeTerritories[a], territoryId);
                AddUniqueSorted(ref nodeTerritories[b], territoryId);
            }

            List<int>[] neighbors = new List<int>[territoryIds.Length];
            for (int i = 0; i < neighbors.Length; i++)
            {
                neighbors[i] = new List<int>();
            }

            for (int node = 0; node < nodeTerritories.Length; node++)
            {
                List<int> atNode = nodeTerritories[node];
                if (atNode == null || atNode.Count < 2)
                {
                    continue;
                }

                AddTerritoryPairs(territoryIds, neighbors, atNode);
            }

            for (int i = 0; i < towns.Length; i++)
            {
                int fromTerritory = towns[i].TerritoryId;
                int neighborOff = townNeighborOffsets[i];
                int neighborCount = townNeighborCounts[i];
                if (fromTerritory == 0 || neighborOff < 0 || neighborCount < 0 || neighborOff + neighborCount > i32p.Length)
                {
                    throw new InvalidDataException("TOWN neighborTownIds span is invalid.");
                }

                for (int n = 0; n < neighborCount; n++)
                {
                    int otherTownId = i32p[neighborOff + n];
                    if (!TryGetTownTerritory(towns, otherTownId, out int toTerritory) || toTerritory == 0 || toTerritory == fromTerritory)
                    {
                        continue;
                    }

                    int fromIndex = IndexOfSorted(territoryIds, fromTerritory);
                    int toIndex = IndexOfSorted(territoryIds, toTerritory);
                    if (fromIndex < 0 || toIndex < 0)
                    {
                        continue;
                    }

                    AddUniqueSorted(neighbors[fromIndex], toTerritory);
                    AddUniqueSorted(neighbors[toIndex], fromTerritory);
                }
            }

            int[][] neighborArrays = new int[neighbors.Length][];
            for (int i = 0; i < neighbors.Length; i++)
            {
                neighborArrays[i] = neighbors[i].ToArray();
            }

            return new RoadConnectedTerritoryAdjacency(territoryIds, neighborArrays);
        }

        private static void AddTerritoryPairs(int[] territoryIds, List<int>[] neighbors, List<int> atNode)
        {
            for (int i = 0; i < atNode.Count; i++)
            {
                int leftIndex = IndexOfSorted(territoryIds, atNode[i]);
                if (leftIndex < 0)
                {
                    continue;
                }

                for (int j = 0; j < atNode.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    AddUniqueSorted(neighbors[leftIndex], atNode[j]);
                }
            }
        }

        private static bool TryGetTownTerritory(AiTownRecord[] towns, int townId, out int territoryId)
        {
            for (int i = 0; i < towns.Length; i++)
            {
                if (towns[i].Id == townId)
                {
                    territoryId = towns[i].TerritoryId;
                    return true;
                }
            }

            territoryId = 0;
            return false;
        }

        private static int[] ReadInt32Pool(byte[] bytes)
        {
            if ((bytes.Length & 3) != 0)
            {
                throw new InvalidDataException("I32P length must be a multiple of 4.");
            }

            int count = bytes.Length / 4;
            int[] values = new int[count];
            BlobReader reader = new BlobReader(bytes);
            for (int i = 0; i < count; i++)
            {
                values[i] = reader.I32();
            }

            return values;
        }

        private static void AddUniqueSorted(ref List<int> list, int value)
        {
            if (list == null)
            {
                list = new List<int>();
            }

            AddUniqueSorted(list, value);
        }

        private static void AddUniqueSorted(List<int> list, int value)
        {
            int lo = 0;
            int hi = list.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int current = list[mid];
                if (current == value)
                {
                    return;
                }

                if (current < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            list.Insert(lo, value);
        }

        private static int IndexOfSorted(int[] ids, int value)
        {
            int lo = 0;
            int hi = ids.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int current = ids[mid];
                if (current == value)
                {
                    return mid;
                }

                if (current < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return -1;
        }

        private static string ReadUtf8(byte[] strp, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > strp.Length)
            {
                throw new InvalidDataException("STRP span out of bounds.");
            }

            return Encoding.UTF8.GetString(strp, offset, length);
        }

        private static ulong HashBytes(byte[] bytes)
        {
            return XxHash64.Hash(bytes, 0);
        }
    }
}
