using UnityEngine;
using Wasteland.Presentation;

namespace Wasteland.Prototypes
{
    /// <summary>
    /// Builds in-memory placeholder VAT textures (idle/walk/fire) for a procedural infantry mesh.
    /// Used when no baked character asset exists. The editor baker can persist the same data.
    /// </summary>
    public sealed class PlaceholderVatSet
    {
        public Mesh BindMesh;
        public Texture2D Positions;
        public Texture2D Normals;
        public Texture2D ClipTable;
        public float RowsPerFrame = 1f;
        public Bounds RenderBounds;
        public int VertexCount;
        public int FrameCount;
        public float MemoryMegabytes;
        public VatClipDesc[] Clips;
    }

    public struct VatClipDesc
    {
        public string Name;
        public int FirstRow;
        public int FrameCount;
        public float Fps;
        public bool Loop;
        public float LengthSeconds;
    }

    public static class PrototypeVatFactory
    {
        public const int IdleFrames = 60;
        public const int WalkFrames = 30;
        public const int FireFrames = 24;
        public const float BakeFps = 30f;

        public static PlaceholderVatSet BuildInfantry(int archetype)
        {
            ProceduralUnitMeshes.BuiltMesh built = ProceduralUnitMeshes.CreateInfantry(archetype);
            Vector3[] bindPos = built.Mesh.vertices;
            Vector3[] bindNrm = built.Mesh.normals;
            Vector2[] groups = new Vector2[bindPos.Length];
            ListUv(built.Mesh, 1, groups);

            int vertexCount = bindPos.Length;
            int width = Mathf.NextPowerOfTwo(Mathf.Max(vertexCount, 8));
            int rowsPerFrame = Mathf.CeilToInt(vertexCount / (float)width);
            int totalFrames = IdleFrames + WalkFrames + FireFrames;
            int height = totalFrames * rowsPerFrame;

            Texture2D posTex = CreateHalfTex("VATPos_" + archetype, width, height);
            Texture2D nrmTex = CreateHalfTex("VATNrm_" + archetype, width, height);
            Color[] posPixels = new Color[width * height];
            Color[] nrmPixels = new Color[width * height];

            VatClipDesc[] clips =
            {
                new VatClipDesc { Name = "idle", FirstRow = 0, FrameCount = IdleFrames, Fps = BakeFps, Loop = true, LengthSeconds = IdleFrames / BakeFps },
                new VatClipDesc { Name = "walk", FirstRow = IdleFrames, FrameCount = WalkFrames, Fps = BakeFps, Loop = true, LengthSeconds = WalkFrames / BakeFps },
                new VatClipDesc { Name = "fire", FirstRow = IdleFrames + WalkFrames, FrameCount = FireFrames, Fps = BakeFps, Loop = false, LengthSeconds = FireFrames / BakeFps }
            };

            Vector3[] posed = new Vector3[vertexCount];
            Vector3[] posedN = new Vector3[vertexCount];
            for (int clip = 0; clip < clips.Length; clip++)
            {
                VatClipDesc desc = clips[clip];
                for (int f = 0; f < desc.FrameCount; f++)
                {
                    float t = f / desc.Fps;
                    Pose(clip, t, bindPos, bindNrm, groups, posed, posedN);
                    int frameRow = desc.FirstRow + f;
                    for (int v = 0; v < vertexCount; v++)
                    {
                        int col = v % width;
                        int sub = v / width;
                        int y = frameRow * rowsPerFrame + sub;
                        int i = y * width + col;
                        Vector3 p = posed[v];
                        Vector3 n = posedN[v];
                        posPixels[i] = new Color(p.x, p.y, p.z, 1f);
                        nrmPixels[i] = new Color(n.x, n.y, n.z, 0f);
                    }
                }
            }

            posTex.SetPixels(posPixels);
            nrmTex.SetPixels(nrmPixels);
            posTex.Apply(false, false);
            nrmTex.Apply(false, false);

            Texture2D clipTex = new Texture2D(64, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "VATClip_" + archetype,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] clipPixels = new Color[64];
            for (int i = 0; i < clips.Length; i++)
            {
                VatClipDesc c = clips[i];
                clipPixels[i] = new Color(c.FirstRow, c.FrameCount, c.Fps, c.Loop ? 1f : 0f);
            }

            clipTex.SetPixels(clipPixels);
            clipTex.Apply(false, false);

            float mb = width * height * 8f * 2f / (1024f * 1024f);
            built.Mesh.bounds = built.AnimationBounds;

            return new PlaceholderVatSet
            {
                BindMesh = built.Mesh,
                Positions = posTex,
                Normals = nrmTex,
                ClipTable = clipTex,
                RowsPerFrame = rowsPerFrame,
                RenderBounds = built.AnimationBounds,
                VertexCount = vertexCount,
                FrameCount = totalFrames,
                MemoryMegabytes = mb,
                Clips = clips
            };
        }

        public static string FormatReport(PlaceholderVatSet set)
        {
            return string.Format(
                "{0}: {1} verts, {2} frames, rowsPerFrame={3:0}, {4:0.00} MB (pos+nrm). Clips idle/walk/fire.",
                set.BindMesh.name,
                set.VertexCount,
                set.FrameCount,
                set.RowsPerFrame,
                set.MemoryMegabytes);
        }

        private static void ListUv(Mesh mesh, int channel, Vector2[] dest)
        {
            var list = new System.Collections.Generic.List<Vector2>(dest.Length);
            mesh.GetUVs(channel, list);
            for (int i = 0; i < dest.Length; i++)
            {
                dest[i] = i < list.Count ? list[i] : Vector2.zero;
            }
        }

        private static Texture2D CreateHalfTex(string name, int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
        }

        private static void Pose(
            int clip,
            float t,
            Vector3[] bindPos,
            Vector3[] bindNrm,
            Vector2[] groups,
            Vector3[] posed,
            Vector3[] posedN)
        {
            for (int i = 0; i < bindPos.Length; i++)
            {
                Vector3 p = bindPos[i];
                Vector3 n = bindNrm[i];
                int g = (int)groups[i].x;
                if (clip == VatClipPacking.ClipIdle)
                {
                    float bob = Mathf.Sin(t * Mathf.PI) * 0.02f;
                    p.y += bob;
                    if (g == (int)ProceduralUnitMeshes.VertexGroup.Body)
                    {
                        p.x *= 1f + 0.02f * Mathf.Sin(t * Mathf.PI);
                    }
                }
                else if (clip == VatClipPacking.ClipWalk)
                {
                    float phase = t * Mathf.PI * 2f;
                    float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.04f;
                    p.y += bob;
                    if (g == (int)ProceduralUnitMeshes.VertexGroup.LeftLeg)
                    {
                        p.z += Mathf.Sin(phase) * 0.16f;
                        p.y += Mathf.Max(0f, Mathf.Sin(phase)) * 0.06f;
                    }
                    else if (g == (int)ProceduralUnitMeshes.VertexGroup.RightLeg)
                    {
                        p.z += Mathf.Sin(phase + Mathf.PI) * 0.16f;
                        p.y += Mathf.Max(0f, Mathf.Sin(phase + Mathf.PI)) * 0.06f;
                    }
                    else if (g == (int)ProceduralUnitMeshes.VertexGroup.LeftArm)
                    {
                        p.z += Mathf.Sin(phase + Mathf.PI) * 0.12f;
                    }
                    else if (g == (int)ProceduralUnitMeshes.VertexGroup.RightArm ||
                             g == (int)ProceduralUnitMeshes.VertexGroup.Weapon)
                    {
                        p.z += Mathf.Sin(phase) * 0.12f;
                    }
                }
                else
                {
                    float pulse = Mathf.Clamp01(Mathf.Sin(t / 0.8f * Mathf.PI));
                    if (g == (int)ProceduralUnitMeshes.VertexGroup.Weapon ||
                        g == (int)ProceduralUnitMeshes.VertexGroup.RightArm)
                    {
                        p.z -= pulse * 0.12f;
                    }
                    else if (g == (int)ProceduralUnitMeshes.VertexGroup.Body ||
                             g == (int)ProceduralUnitMeshes.VertexGroup.Head)
                    {
                        p.z -= pulse * 0.04f;
                    }
                }

                posed[i] = p;
                posedN[i] = n;
            }
        }
    }
}
