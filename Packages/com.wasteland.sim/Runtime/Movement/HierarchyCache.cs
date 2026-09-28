using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// Optional on-disk cache of per-class HPA* graphs. Keyed by terrain
    /// content hash, movement-table hash, algorithm version, cluster size,
    /// and grid shape. Payload SHA-256 is verified on load.
    /// </summary>
    internal static class HierarchyCache
    {
        public const string Magic = "WHPA";

        public static bool TryLoad(
            string directory,
            byte[] terrainContentHash,
            ulong tableHash,
            int clusterSize,
            int width,
            int height,
            out GridHierarchy[] hierarchies)
        {
            hierarchies = Array.Empty<GridHierarchy>();
            string path = FilePath(directory, terrainContentHash, tableHash, clusterSize, width, height);
            if (!File.Exists(path))
            {
                return false;
            }

            byte[] data;
            try
            {
                data = File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            if (data.Length < 64)
            {
                return false;
            }

            BlobReader reader = new BlobReader(data);
            try
            {
                reader.ExpectMagic(Magic);
                ushort algo = reader.U16();
                if (algo != MovementConfig.HierarchyAlgorithmVersion)
                {
                    return false;
                }

                ushort fileCluster = reader.U16();
                int fileWidth = reader.I32();
                int fileHeight = reader.I32();
                int classCount = reader.I32();
                if (fileCluster != clusterSize || fileWidth != width || fileHeight != height || classCount != MovementTables.ClassCount)
                {
                    return false;
                }

                byte[] fileTerrain = reader.Bytes(32);
                ulong fileTable = reader.U64();
                if (!BytesEqual(fileTerrain, terrainContentHash) || fileTable != tableHash)
                {
                    return false;
                }

                byte[] payloadHash = reader.Bytes(32);
                int payloadOffset = reader.Offset;
                byte[] actual;
                using (SHA256 sha = SHA256.Create())
                {
                    actual = sha.ComputeHash(data, payloadOffset, data.Length - payloadOffset);
                }

                if (!BytesEqual(actual, payloadHash))
                {
                    return false;
                }

                GridHierarchy[] loaded = new GridHierarchy[classCount];
                for (int c = 0; c < classCount; c++)
                {
                    int nodes = reader.I32();
                    int cx = reader.I32();
                    int cy = reader.I32();
                    int clusterNodesLen = reader.I32();
                    int adjLen = reader.I32();
                    if (nodes < 0)
                    {
                        loaded[c] = null!;
                        continue;
                    }

                    int[] nodeCell = ReadInts(reader, nodes);
                    int[] nodeCluster = ReadInts(reader, nodes);
                    int[] clusterNodeOffset = ReadInts(reader, cx * cy + 1);
                    int[] clusterNodes = ReadInts(reader, clusterNodesLen);
                    int[] adjOffset = ReadInts(reader, nodes + 1);
                    int[] adjNeighbor = ReadInts(reader, adjLen);
                    int[] adjCost = ReadInts(reader, adjLen);
                    byte[] adjInter = reader.Bytes(adjLen);
                    loaded[c] = GridHierarchy.FromCache(
                        clusterSize,
                        cx,
                        cy,
                        nodeCell,
                        nodeCluster,
                        clusterNodeOffset,
                        clusterNodes,
                        adjOffset,
                        adjNeighbor,
                        adjCost,
                        adjInter);
                }

                hierarchies = loaded;
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        public static void TrySave(
            string directory,
            byte[] terrainContentHash,
            ulong tableHash,
            int clusterSize,
            int width,
            int height,
            GridHierarchy[] hierarchies)
        {
            if (hierarchies == null || hierarchies.Length != MovementTables.ClassCount)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            BlobWriter body = new BlobWriter(256);
            for (int c = 0; c < hierarchies.Length; c++)
            {
                GridHierarchy h = hierarchies[c];
                if (h == null)
                {
                    body.I32(-1);
                    body.I32(0);
                    body.I32(0);
                    body.I32(0);
                    body.I32(0);
                    continue;
                }

                body.I32(h.NodeCount);
                body.I32(h.ClustersX);
                body.I32(h.ClustersY);
                body.I32(h.ClusterNodes.Length);
                body.I32(h.AdjNeighbor.Length);
                WriteInts(body, h.NodeCell);
                WriteInts(body, h.NodeCluster);
                WriteInts(body, h.ClusterNodeOffset);
                WriteInts(body, h.ClusterNodes);
                WriteInts(body, h.AdjOffset);
                WriteInts(body, h.AdjNeighbor);
                WriteInts(body, h.AdjCost);
                body.Bytes(h.AdjInter);
            }

            byte[] payload = body.ToArray();
            byte[] payloadHash;
            using (SHA256 sha = SHA256.Create())
            {
                payloadHash = sha.ComputeHash(payload);
            }

            BlobWriter file = new BlobWriter(64 + payload.Length);
            file.Magic(Magic);
            file.U16(MovementConfig.HierarchyAlgorithmVersion);
            file.U16((ushort)clusterSize);
            file.I32(width);
            file.I32(height);
            file.I32(MovementTables.ClassCount);
            file.Bytes(terrainContentHash);
            file.U64(tableHash);
            file.Bytes(payloadHash);
            file.Bytes(payload);

            string path = FilePath(directory, terrainContentHash, tableHash, clusterSize, width, height);
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, file.ToArray());
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tmp, path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static string FilePath(
            string directory,
            byte[] terrainContentHash,
            ulong tableHash,
            int clusterSize,
            int width,
            int height)
        {
            StringBuilder name = new StringBuilder();
            name.Append("whpa-v");
            name.Append(MovementConfig.HierarchyAlgorithmVersion);
            name.Append('-');
            name.Append(ToHex(terrainContentHash, 8));
            name.Append('-');
            name.Append(tableHash.ToString("x16"));
            name.Append('-');
            name.Append(clusterSize);
            name.Append('-');
            name.Append(width);
            name.Append('x');
            name.Append(height);
            name.Append(".bin");
            return Path.Combine(directory, name.ToString());
        }

        private static int[] ReadInts(BlobReader reader, int count)
        {
            int[] values = new int[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = reader.I32();
            }

            return values;
        }

        private static void WriteInts(BlobWriter writer, int[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                writer.I32(values[i]);
            }
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string ToHex(byte[] data, int bytes)
        {
            int n = bytes < data.Length ? bytes : data.Length;
            char[] chars = new char[n * 2];
            for (int i = 0; i < n; i++)
            {
                int hi = data[i] >> 4;
                int lo = data[i] & 15;
                chars[i * 2] = (char)(hi < 10 ? '0' + hi : 'a' + (hi - 10));
                chars[i * 2 + 1] = (char)(lo < 10 ? '0' + lo : 'a' + (lo - 10));
            }

            return new string(chars);
        }
    }
}
