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

        public AiTownRecord(int id, int territoryId, int nameHash)
        {
            Id = id;
            TerritoryId = territoryId;
            NameHash = nameHash;
        }
    }

    public readonly struct AiTerritoryRecord
    {
        public readonly int Id;
        public readonly int SeatTownId;

        public AiTerritoryRecord(int id, int seatTownId)
        {
            Id = id;
            SeatTownId = seatTownId;
        }
    }

    /// <summary>
    /// Thin map ingest for M0 seams: towns, seats, The Dalles id, and the
    /// road-connected adjacency stopgap. Accepts WRMB v0 and v1 via
    /// <see cref="Wrmb.ReadTerritories"/> / <see cref="Wrmb.ReadChokepoints"/>.
    /// Not the M1 derived-map pass.
    /// </summary>
    public sealed class AiMapSnapshot
    {
        public const int TownRecordSize = 64;
        public const int RoadEdgeRecordSize = 64;

        private readonly AiTownRecord[] _towns;
        private readonly AiTerritoryRecord[] _territories;

        private AiMapSnapshot(
            ulong contentHash,
            ulong mapKey,
            int theDallesTownId,
            int theDallesTerritoryId,
            AiTownRecord[] towns,
            AiTerritoryRecord[] territories,
            ITerritoryAdjacency adjacency)
        {
            ContentHash = contentHash;
            MapKey = mapKey;
            TheDallesTownId = theDallesTownId;
            TheDallesTerritoryId = theDallesTerritoryId;
            _towns = towns;
            _territories = territories;
            Adjacency = adjacency;
        }

        public ulong ContentHash { get; }

        public ulong MapKey { get; }

        public int TheDallesTownId { get; }

        public int TheDallesTerritoryId { get; }

        public ITerritoryAdjacency Adjacency { get; }

        public int TownCount
        {
            get { return _towns.Length; }
        }

        public int TerritoryCount
        {
            get { return _territories.Length; }
        }

        public AiTownRecord TownAt(int index)
        {
            return _towns[index];
        }

        public AiTerritoryRecord TerritoryAt(int index)
        {
            return _territories[index];
        }

        public bool TryGetTown(int townId, out AiTownRecord town)
        {
            for (int i = 0; i < _towns.Length; i++)
            {
                if (_towns[i].Id == townId)
                {
                    town = _towns[i];
                    return true;
                }
            }

            town = default;
            return false;
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
            Territory[] wrmbTerritories = Wrmb.ReadTerritories(file);
            _ = Wrmb.ReadChokepoints(file);

            WrmbDirEntry townEntry = file.RequireSection("TOWN");
            WrmbDirEntry terrEntry = file.RequireSection("TERR");
            WrmbDirEntry redgEntry = file.RequireSection("REDG");
            WrmbDirEntry i32pEntry = file.RequireSection("I32P");
            WrmbDirEntry strpEntry = file.RequireSection("STRP");

            if (townEntry.ElementSize != TownRecordSize)
            {
                throw new InvalidDataException("TOWN elementSize must be 64.");
            }

            if (redgEntry.ElementSize != RoadEdgeRecordSize)
            {
                throw new InvalidDataException("REDG elementSize must be 64.");
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
            BlobReader townReader = new BlobReader(file.SectionBytes(townEntry));
            int previousTownId = -1;
            for (int i = 0; i < townEntry.Count; i++)
            {
                int id = townReader.I32();
                townReader.Skip(8);
                int territoryId = townReader.I32();
                int nameHash = townReader.I32();
                townReader.Skip(16);
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
                towns[i] = new AiTownRecord(id, territoryId, nameHash);
                string name = ReadUtf8(strp, nameOff, nameLen);
                if (name == AiConstants.TheDallesTownName)
                {
                    theDallesTownId = id;
                    theDallesTerritoryId = territoryId;
                }
            }

            AiTerritoryRecord[] territories = new AiTerritoryRecord[terrEntry.Count];
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

                if (i > 0 && id <= previousTerrId)
                {
                    throw new InvalidDataException("TERR ids must be sorted unique.");
                }

                previousTerrId = id;
                territories[i] = new AiTerritoryRecord(id, seatTownId);
            }

            ITerritoryAdjacency adjacency = BuildRoadConnected(
                territories,
                towns,
                i32p,
                townNeighborOffsets,
                townNeighborCounts,
                file.SectionBytes(redgEntry),
                redgEntry.Count);
            return new AiMapSnapshot(contentHash, mapKey, theDallesTownId, theDallesTerritoryId, towns, territories, adjacency);
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
