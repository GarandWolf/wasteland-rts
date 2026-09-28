using System;
using System.Collections.Generic;

namespace Wasteland.Sim.Movement
{
    /// <summary>
    /// In-memory authoring helper. Sorts by id on write so callers may insert
    /// in any order. Used by tests and, later, the map pipeline.
    /// </summary>
    public sealed class RoadGraphBuilder
    {
        private readonly List<NodeRec> _nodes = new List<NodeRec>();
        private readonly List<EdgeRec> _edges = new List<EdgeRec>();
        private int _originXCm;
        private int _originYCm;

        public RoadGraphBuilder Origin(int xCm, int yCm)
        {
            _originXCm = xCm;
            _originYCm = yCm;
            return this;
        }

        public RoadGraphBuilder AddNode(int id, int xCm, int yCm, RoadNodeFlags flags = RoadNodeFlags.None)
        {
            _nodes.Add(new NodeRec(id, xCm, yCm, (ushort)flags));
            return this;
        }

        public RoadGraphBuilder AddEdge(
            int id,
            int fromNodeId,
            int toNodeId,
            int lengthCm,
            RoadTier tier,
            RoadSurface surface,
            RoadEdgeFlags flags = RoadEdgeFlags.None)
        {
            if (lengthCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lengthCm));
            }

            _edges.Add(new EdgeRec(id, fromNodeId, toNodeId, lengthCm, (byte)tier, (byte)surface, (ushort)flags));
            return this;
        }

        public byte[] ToBlob()
        {
            _nodes.Sort((a, b) => a.Id.CompareTo(b.Id));
            _edges.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 1; i < _nodes.Count; i++)
            {
                if (_nodes[i].Id == _nodes[i - 1].Id)
                {
                    throw new InvalidOperationException("Duplicate node id " + _nodes[i].Id + ".");
                }
            }

            for (int i = 1; i < _edges.Count; i++)
            {
                if (_edges[i].Id == _edges[i - 1].Id)
                {
                    throw new InvalidOperationException("Duplicate edge id " + _edges[i].Id + ".");
                }
            }

            int[] nodeId = new int[_nodes.Count];
            int[] nodeX = new int[_nodes.Count];
            int[] nodeY = new int[_nodes.Count];
            ushort[] nodeFlags = new ushort[_nodes.Count];
            for (int i = 0; i < _nodes.Count; i++)
            {
                nodeId[i] = _nodes[i].Id;
                nodeX[i] = _nodes[i].XCm;
                nodeY[i] = _nodes[i].YCm;
                nodeFlags[i] = _nodes[i].Flags;
            }

            int[] edgeId = new int[_edges.Count];
            int[] edgeFrom = new int[_edges.Count];
            int[] edgeTo = new int[_edges.Count];
            int[] edgeLength = new int[_edges.Count];
            byte[] edgeTier = new byte[_edges.Count];
            byte[] edgeSurface = new byte[_edges.Count];
            ushort[] edgeFlags = new ushort[_edges.Count];
            for (int i = 0; i < _edges.Count; i++)
            {
                EdgeRec e = _edges[i];
                int fromIndex = Array.BinarySearch(nodeId, e.FromId);
                int toIndex = Array.BinarySearch(nodeId, e.ToId);
                if (fromIndex < 0 || toIndex < 0)
                {
                    throw new InvalidOperationException("Edge " + e.Id + " references a missing node.");
                }

                edgeId[i] = e.Id;
                edgeFrom[i] = fromIndex;
                edgeTo[i] = toIndex;
                edgeLength[i] = e.LengthCm;
                edgeTier[i] = e.Tier;
                edgeSurface[i] = e.Surface;
                edgeFlags[i] = e.Flags;
            }

            RoadGraph.BuildAdjacency(
                nodeId.Length,
                edgeFrom,
                edgeTo,
                edgeId,
                nodeId,
                out int[] adjOffset,
                out int[] adjNeighbor,
                out int[] adjEdge);

            RoadGraph graph = new RoadGraph(
                nodeId,
                nodeX,
                nodeY,
                nodeFlags,
                edgeId,
                edgeFrom,
                edgeTo,
                edgeLength,
                edgeTier,
                edgeSurface,
                edgeFlags,
                adjOffset,
                adjNeighbor,
                adjEdge,
                _originXCm,
                _originYCm);

            return Wrmb.WriteRoadGraph(graph);
        }

        private readonly struct NodeRec
        {
            public readonly int Id;
            public readonly int XCm;
            public readonly int YCm;
            public readonly ushort Flags;

            public NodeRec(int id, int xCm, int yCm, ushort flags)
            {
                Id = id;
                XCm = xCm;
                YCm = yCm;
                Flags = flags;
            }
        }

        private readonly struct EdgeRec
        {
            public readonly int Id;
            public readonly int FromId;
            public readonly int ToId;
            public readonly int LengthCm;
            public readonly byte Tier;
            public readonly byte Surface;
            public readonly ushort Flags;

            public EdgeRec(int id, int fromId, int toId, int lengthCm, byte tier, byte surface, ushort flags)
            {
                Id = id;
                FromId = fromId;
                ToId = toId;
                LengthCm = lengthCm;
                Tier = tier;
                Surface = surface;
                Flags = flags;
            }
        }
    }
}
