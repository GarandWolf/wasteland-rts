using System.Collections.Generic;
using UnityEngine;

namespace Wasteland.Prototypes
{
    /// <summary>
    /// Low-poly placeholder meshes with vertex-color team masks and UV3 vertex indices.
    /// Three infantry silhouettes and three vehicle silhouettes for the 5k test.
    /// </summary>
    public static class ProceduralUnitMeshes
    {
        public const int InfantryArchetypeCount = 3;
        public const int VehicleArchetypeCount = 3;

        public enum VertexGroup
        {
            Body = 0,
            LeftLeg = 1,
            RightLeg = 2,
            LeftArm = 3,
            RightArm = 4,
            Head = 5,
            Weapon = 6,
            Marker = 7
        }

        public struct BuiltMesh
        {
            public Mesh Mesh;
            public Bounds AnimationBounds;
            public int VertexCount;
        }

        public static BuiltMesh CreateInfantry(int archetype)
        {
            MeshBuilder b = new MeshBuilder();
            Color kit = KitColor(archetype);
            Color primary = new Color(1f, 0f, 0f, 0f);
            Color accent = new Color(0f, 1f, 0f, 0f);
            Color pip1 = new Color(0f, 0f, 0.5f, 0f);
            Color pip2 = new Color(0f, 0f, 1f, 0f);
            Color dirt = new Color(0f, 0f, 0f, 0.55f);

            // Legs
            AddBox(b, new Vector3(-0.12f, 0.28f, 0f), new Vector3(0.14f, 0.56f, 0.16f), kit, VertexGroup.LeftLeg);
            AddBox(b, new Vector3(0.12f, 0.28f, 0f), new Vector3(0.14f, 0.56f, 0.16f), kit, VertexGroup.RightLeg);
            // Torso
            AddBox(b, new Vector3(0f, 0.95f, 0f), new Vector3(0.38f, 0.7f, 0.22f), kit, VertexGroup.Body);
            AddBox(b, new Vector3(0f, 0.72f, 0f), new Vector3(0.4f, 0.18f, 0.24f), dirt, VertexGroup.Body);
            // Arms
            AddBox(b, new Vector3(-0.28f, 0.95f, 0f), new Vector3(0.12f, 0.55f, 0.12f), kit, VertexGroup.LeftArm);
            AddBox(b, new Vector3(0.28f, 0.95f, 0.08f), new Vector3(0.12f, 0.55f, 0.12f), kit, VertexGroup.RightArm);
            // Weapon
            AddBox(b, new Vector3(0.28f, 1.05f, 0.38f), new Vector3(0.06f, 0.08f, 0.7f), new Color(0.12f, 0.12f, 0.12f, 0f), VertexGroup.Weapon);

            if (archetype == 0)
            {
                AddBox(b, new Vector3(0f, 1.52f, 0f), new Vector3(0.28f, 0.28f, 0.28f), kit, VertexGroup.Head);
                AddBox(b, new Vector3(0f, 1.62f, 0f), new Vector3(0.32f, 0.14f, 0.32f), primary, VertexGroup.Head);
                AddBox(b, new Vector3(0f, 1.62f, 0.18f), new Vector3(0.34f, 0.04f, 0.04f), accent, VertexGroup.Marker);
            }
            else if (archetype == 1)
            {
                AddBox(b, new Vector3(0f, 1.5f, 0f), new Vector3(0.26f, 0.26f, 0.26f), kit, VertexGroup.Head);
                AddBox(b, new Vector3(0f, 1.58f, 0.08f), new Vector3(0.42f, 0.05f, 0.42f), primary, VertexGroup.Head);
                AddBox(b, new Vector3(-0.22f, 1.2f, 0f), new Vector3(0.08f, 0.22f, 0.18f), primary, VertexGroup.Marker);
                AddBox(b, new Vector3(-0.22f, 1.2f, 0f), new Vector3(0.1f, 0.06f, 0.2f), accent, VertexGroup.Marker);
            }
            else
            {
                AddBox(b, new Vector3(0f, 1.48f, 0.02f), new Vector3(0.24f, 0.24f, 0.24f), kit, VertexGroup.Head);
                AddBox(b, new Vector3(0f, 1.35f, 0.04f), new Vector3(0.2f, 0.12f, 0.28f), primary, VertexGroup.Marker);
                AddBox(b, new Vector3(0f, 1.36f, 0.04f), new Vector3(0.06f, 0.14f, 0.3f), accent, VertexGroup.Marker);
            }

            // Squad-leader pip ticks on a back banner (style guide: infantry pips are not readable on the body).
            AddBox(b, new Vector3(0f, 1.35f, -0.16f), new Vector3(0.18f, 0.22f, 0.03f), primary, VertexGroup.Marker);
            AddBox(b, new Vector3(-0.04f, 1.4f, -0.18f), new Vector3(0.05f, 0.08f, 0.02f), pip1, VertexGroup.Marker);
            AddBox(b, new Vector3(0.04f, 1.4f, -0.18f), new Vector3(0.05f, 0.08f, 0.02f), pip2, VertexGroup.Marker);

            BuiltMesh built = b.Build("Infantry_" + archetype);
            built.AnimationBounds = new Bounds(new Vector3(0f, 0.95f, 0f), new Vector3(1.3f, 2.2f, 1.6f));
            return built;
        }

        public static BuiltMesh CreateVehicle(int archetype)
        {
            MeshBuilder b = new MeshBuilder();
            Color body = new Color(0.28f, 0.27f, 0.24f, 0.35f);
            Color primary = new Color(1f, 0f, 0f, 0f);
            Color accent = new Color(0f, 1f, 0f, 0f);
            Color pip1 = new Color(0f, 0f, 0.5f, 0f);
            Color pip2 = new Color(0f, 0f, 1f, 0f);

            if (archetype == 0)
            {
                AddBox(b, new Vector3(0f, 0.55f, 0f), new Vector3(1.1f, 0.7f, 2.4f), body, VertexGroup.Body);
                AddBox(b, new Vector3(0f, 1.05f, -0.2f), new Vector3(0.8f, 0.35f, 1.1f), body, VertexGroup.Body);
                AddBox(b, new Vector3(0f, 1.05f, 1.4f), new Vector3(0.12f, 0.12f, 1.6f), new Color(0.15f, 0.15f, 0.15f, 0f), VertexGroup.Weapon);
                AddBox(b, new Vector3(0f, 1.28f, -0.15f), new Vector3(0.7f, 0.04f, 1.0f), primary, VertexGroup.Marker);
                AddBox(b, new Vector3(0f, 1.3f, -0.15f), new Vector3(0.74f, 0.02f, 0.06f), accent, VertexGroup.Marker);
            }
            else if (archetype == 1)
            {
                AddBox(b, new Vector3(0f, 0.7f, 0f), new Vector3(1.2f, 1.1f, 1.8f), body, VertexGroup.Body);
                AddBox(b, new Vector3(0f, 0.45f, 1.15f), new Vector3(1.3f, 0.35f, 0.25f), primary, VertexGroup.Marker);
                AddBox(b, new Vector3(0f, 0.45f, 1.18f), new Vector3(1.32f, 0.08f, 0.28f), accent, VertexGroup.Marker);
                AddBox(b, new Vector3(0f, 1.35f, 0f), new Vector3(0.5f, 0.08f, 0.5f), primary, VertexGroup.Marker);
            }
            else
            {
                AddBox(b, new Vector3(0f, 0.38f, 0.1f), new Vector3(1.05f, 0.45f, 2.2f), body, VertexGroup.Body);
                AddBox(b, new Vector3(0f, 0.7f, -0.35f), new Vector3(0.9f, 0.28f, 0.8f), body, VertexGroup.Body);
                AddBox(b, new Vector3(0f, 0.85f, 0.2f), new Vector3(0.9f, 0.05f, 1.6f), primary, VertexGroup.Marker);
                AddBox(b, new Vector3(0f, 0.88f, 0.2f), new Vector3(0.14f, 0.04f, 1.6f), accent, VertexGroup.Marker);
                AddBox(b, new Vector3(0f, 0.55f, 1.2f), new Vector3(0.9f, 0.2f, 0.12f), new Color(0.2f, 0.2f, 0.2f, 0f), VertexGroup.Weapon);
            }

            AddBox(b, new Vector3(-0.18f, RoofPipY(archetype), RoofPipZ(archetype)), new Vector3(0.12f, 0.03f, 0.28f), pip1, VertexGroup.Marker);
            AddBox(b, new Vector3(0.18f, RoofPipY(archetype), RoofPipZ(archetype)), new Vector3(0.12f, 0.03f, 0.28f), pip2, VertexGroup.Marker);

            BuiltMesh built = b.Build("Vehicle_" + archetype);
            built.AnimationBounds = built.Mesh.bounds;
            return built;
        }

        private static float RoofPipY(int archetype)
        {
            return archetype == 1 ? 1.4f : 0.95f;
        }

        private static float RoofPipZ(int archetype)
        {
            return archetype == 0 ? -0.15f : 0.05f;
        }

        private static Color KitColor(int archetype)
        {
            if (archetype == 0)
            {
                return new Color(0.32f, 0.35f, 0.32f, 0.2f);
            }

            if (archetype == 1)
            {
                return new Color(0.42f, 0.31f, 0.21f, 0.15f);
            }

            return new Color(0.15f, 0.14f, 0.13f, 0.25f);
        }

        private static void AddBox(MeshBuilder b, Vector3 center, Vector3 size, Color color, VertexGroup group)
        {
            Vector3 h = size * 0.5f;
            Vector3[] p =
            {
                center + new Vector3(-h.x, -h.y, -h.z),
                center + new Vector3(h.x, -h.y, -h.z),
                center + new Vector3(h.x, -h.y, h.z),
                center + new Vector3(-h.x, -h.y, h.z),
                center + new Vector3(-h.x, h.y, -h.z),
                center + new Vector3(h.x, h.y, -h.z),
                center + new Vector3(h.x, h.y, h.z),
                center + new Vector3(-h.x, h.y, h.z)
            };

            AddQuad(b, p[0], p[1], p[2], p[3], Vector3.down, color, group);
            AddQuad(b, p[4], p[7], p[6], p[5], Vector3.up, color, group);
            AddQuad(b, p[0], p[4], p[5], p[1], Vector3.back, color, group);
            AddQuad(b, p[3], p[2], p[6], p[7], Vector3.forward, color, group);
            AddQuad(b, p[0], p[3], p[7], p[4], Vector3.left, color, group);
            AddQuad(b, p[1], p[5], p[6], p[2], Vector3.right, color, group);
        }

        private static void AddQuad(MeshBuilder b, Vector3 a, Vector3 c, Vector3 d, Vector3 e, Vector3 n, Color color, VertexGroup group)
        {
            b.Add(a, n, color, group);
            b.Add(c, n, color, group);
            b.Add(d, n, color, group);
            b.Add(a, n, color, group);
            b.Add(d, n, color, group);
            b.Add(e, n, color, group);
        }

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> _pos = new List<Vector3>(256);
            private readonly List<Vector3> _nrm = new List<Vector3>(256);
            private readonly List<Color> _col = new List<Color>(256);
            private readonly List<Vector2> _uv = new List<Vector2>(256);
            private readonly List<Vector2> _uv2 = new List<Vector2>(256);
            private readonly List<Vector2> _uv3 = new List<Vector2>(256);
            private readonly List<int> _idx = new List<int>(256);

            public void Add(Vector3 p, Vector3 n, Color c, VertexGroup group)
            {
                int i = _pos.Count;
                _pos.Add(p);
                _nrm.Add(n.normalized);
                _col.Add(c);
                _uv.Add(new Vector2(p.x + 0.5f, p.z + 0.5f));
                _uv2.Add(new Vector2((float)group, 0f));
                _uv3.Add(new Vector2(i, 0f));
                _idx.Add(i);
            }

            public BuiltMesh Build(string name)
            {
                Mesh mesh = new Mesh { name = name };
                mesh.SetVertices(_pos);
                mesh.SetNormals(_nrm);
                mesh.SetColors(_col);
                mesh.SetUVs(0, _uv);
                mesh.SetUVs(1, _uv2);
                mesh.SetUVs(3, _uv3);
                mesh.SetTriangles(_idx, 0, true);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                return new BuiltMesh
                {
                    Mesh = mesh,
                    AnimationBounds = mesh.bounds,
                    VertexCount = _pos.Count
                };
            }
        }
    }
}
