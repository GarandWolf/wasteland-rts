#nullable disable
using System.Collections.Generic;
using UnityEngine;

namespace Wasteland.Presentation
{
    public struct TownMarker
    {
        public int XCm;
        public int YCm;
        public int PopulationClass;
        public bool Hq;
        public bool Contested;
        public bool Selected;
        public float Capture;
        public Rgb Owner;
        public Rgb Other;
        public string Name;
    }

    public struct BattalionMarker
    {
        public int XCm;
        public int YCm;
        public bool Selected;
        public Rgb Owner;
        public BattalionGlyph Glyph;
        public string Label;
    }

    /// <summary>
    /// Paper map: hillshade, water, territory wash, roads, then markers.
    /// Strokes stay a constant number of screen pixels as the camera zooms.
    /// </summary>
    public sealed class TacticalMapView : MonoBehaviour
    {
        const float GroundY = 0f;
        const float WaterY = 0.05f;
        const float RoadY = 0.1f;
        const float TownY = 0.2f;
        const float BattalionY = 0.3f;
        const float SelectionY = 0.4f;

        MapDocument _map;
        Camera _camera;
        Texture2D _texture;
        Color32[] _base;
        Color32[] _shown;
        int[] _land;
        readonly List<Material> _strokes = new List<Material>();
        Material _ground;
        Material _marks;
        Mesh _towns;
        Mesh _units;
        Mesh _selection;
        TownMarker[] _townMarkers = System.Array.Empty<TownMarker>();
        BattalionMarker[] _unitMarkers = System.Array.Empty<BattalionMarker>();
        GUIStyle _label;

        public Camera ViewCamera
        {
            get { return _camera; }
        }

        public void Build(MapDocument map, Camera camera)
        {
            _map = map;
            _camera = camera;
            BakeBase();
            ApplyWash(null, null);
            BuildGround();
            BuildWaterEdges();
            BuildRoads();
            _marks = MakeMaterial("Wasteland/Tactical Mark", 2400);
            _towns = new Mesh { name = "Towns", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _units = new Mesh { name = "Battalions", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _selection = new Mesh { name = "Selection", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            AddMesh("Town markers", _towns, _marks);
            AddMesh("Battalion tokens", _units, _marks);
            Material selection = _marks != null ? new Material(_marks) : null;
            if (selection != null)
            {
                selection.renderQueue = 2500;
                _strokes.Add(selection);
            }

            AddMesh("Selection", _selection, selection);
        }

        public void SetTerritoryWash(Color32[] colorByTerritory, bool[] painted)
        {
            if (_base == null)
            {
                return;
            }

            ApplyWash(colorByTerritory, painted);
        }

        public void SetMarkers(TownMarker[] towns, BattalionMarker[] units)
        {
            _townMarkers = towns ?? System.Array.Empty<TownMarker>();
            _unitMarkers = units ?? System.Array.Empty<BattalionMarker>();
            MarkBatch townsBatch = new MarkBatch();
            MarkBatch unitsBatch = new MarkBatch();
            MarkBatch selection = new MarkBatch();
            for (int i = 0; i < _townMarkers.Length; i++)
            {
                TownMarker town = _townMarkers[i];
                int diameter = MarkerArt.TownDiameterPx(town.PopulationClass);
                float pad = 8f;
                Vector3 center = Flat(town.XCm, town.YCm, TownY);
                townsBatch.Quad(
                    center,
                    diameter * 0.5f + pad,
                    diameter * 0.5f + pad,
                    diameter,
                    diameter,
                    1f,
                    town.Hq ? 1f : 0f,
                    town.Contested ? 1f : 0f,
                    town.Capture,
                    0f,
                    ToColor(town.Owner),
                    ToColor(town.Other));
                if (town.Selected)
                {
                    float outer = diameter + (town.Hq ? 8f : 0f);
                    selection.Quad(Flat(town.XCm, town.YCm, SelectionY), outer * 0.5f + 4f, outer * 0.5f + 4f, outer, outer, 3f, 0f, 0f, 0f, 0f, Color.white, Color.white);
                }
            }

            for (int i = 0; i < _unitMarkers.Length; i++)
            {
                BattalionMarker unit = _unitMarkers[i];
                unitsBatch.Quad(
                    Flat(unit.XCm, unit.YCm, BattalionY),
                    MarkerArt.BattalionWidthPx * 0.5f,
                    MarkerArt.BattalionHeightPx * 0.5f,
                    MarkerArt.BattalionWidthPx,
                    MarkerArt.BattalionHeightPx,
                    2f,
                    0f,
                    0f,
                    0f,
                    (float)unit.Glyph,
                    ToColor(unit.Owner),
                    ToColor(unit.Owner));
                if (unit.Selected)
                {
                    selection.Quad(
                        Flat(unit.XCm, unit.YCm, SelectionY),
                        MarkerArt.BattalionWidthPx * 0.5f + 4f,
                        MarkerArt.BattalionHeightPx * 0.5f + 4f,
                        MarkerArt.BattalionWidthPx,
                        MarkerArt.BattalionHeightPx,
                        3f,
                        1f,
                        0f,
                        0f,
                        0f,
                        Color.white,
                        Color.white);
                }
            }

            townsBatch.Upload(_towns);
            unitsBatch.Upload(_units);
            selection.Upload(_selection);
        }

        public void SetWorldPerPixel(float worldPerPixel)
        {
            for (int i = 0; i < _strokes.Count; i++)
            {
                if (_strokes[i] != null && _strokes[i].HasProperty("_WorldPerPixel"))
                {
                    _strokes[i].SetFloat("_WorldPerPixel", worldPerPixel);
                }
            }

            if (_marks != null)
            {
                _marks.SetFloat("_WorldPerPixel", worldPerPixel);
            }
        }

        void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            SetWorldPerPixel(MapSpace.WorldUnitsPerPixel(_camera.orthographicSize, Screen.height));
        }

        void OnGUI()
        {
            if (_camera == null || _townMarkers.Length == 0)
            {
                return;
            }

            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label);
                _label.normal.textColor = new Color(0.1176f, 0.1216f, 0.1137f, 1f);
                _label.fontSize = 13;
                _label.alignment = TextAnchor.LowerCenter;
            }

            for (int i = 0; i < _townMarkers.Length; i++)
            {
                DrawLabel(_townMarkers[i].Name, _townMarkers[i].XCm, _townMarkers[i].YCm, MarkerArt.TownDiameterPx(_townMarkers[i].PopulationClass) * 0.5f + 6f);
            }

            for (int i = 0; i < _unitMarkers.Length; i++)
            {
                if (_unitMarkers[i].Selected && !string.IsNullOrEmpty(_unitMarkers[i].Label))
                {
                    DrawLabel(_unitMarkers[i].Label, _unitMarkers[i].XCm, _unitMarkers[i].YCm, 12f);
                }
            }
        }

        void OnDestroy()
        {
            if (_texture != null)
            {
                Destroy(_texture);
            }

            for (int i = 0; i < _strokes.Count; i++)
            {
                if (_strokes[i] != null)
                {
                    Destroy(_strokes[i]);
                }
            }
        }

        void DrawLabel(string text, int xCm, int yCm, float liftPx)
        {
            Vector3 screen = _camera.WorldToScreenPoint(Flat(xCm, yCm, TownY));
            if (screen.z <= 0f)
            {
                return;
            }

            float guiY = Screen.height - screen.y - liftPx;
            if (screen.x < -40f || screen.x > Screen.width + 40f || guiY < -20f || guiY > Screen.height + 20f)
            {
                return;
            }

            GUI.Label(new Rect(screen.x - 80f, guiY - 16f, 160f, 18f), text, _label);
        }

        void BakeBase()
        {
            int width = _map.Width;
            int height = _map.Height;
            int cells = width * height;
            float[] shade = new float[cells];
            ushort[] raw = new ushort[cells];
            for (int i = 0; i < cells; i++)
            {
                raw[i] = _map.ElevationRawAt(i);
            }

            Hillshade.Compute(width, height, raw, _map.ElevOffsetCm, _map.ElevUnitCm, _map.ExtentXCm, _map.ExtentYCm, Hillshade.Exaggeration, shade);
            _base = new Color32[cells];
            _land = new int[cells];
            Rgb paper = Rgb.FromHex(PaperPaint.Paper);
            for (int row = 0; row < height; row++)
            {
                int yCm = MapSpace.CellCenterCm(row, _map.OriginYCm, height, _map.ExtentYCm);
                for (int col = 0; col < width; col++)
                {
                    int index = row * width + col;
                    int xCm = MapSpace.CellCenterCm(col, _map.OriginXCm, width, _map.ExtentXCm);
                    Rgb color = PaperPaint.MultiplyHillshade(paper, shade[index]);
                    color = PaperPaint.PaintWater(color, _map.Water[index]);
                    _base[index] = new Color32(color.R, color.G, color.B, 255);
                    _land[index] = _map.LandTerritoryAt(xCm, yCm);
                }
            }
        }

        void ApplyWash(Color32[] colors, bool[] painted)
        {
            _shown = new Color32[_base.Length];
            float alpha = PaperPaint.WashAlpha;
            float keep = 1f - alpha;
            for (int i = 0; i < _base.Length; i++)
            {
                Color32 under = _base[i];
                int territory = _land[i];
                if (colors == null || painted == null || territory < 0 || territory >= painted.Length || !painted[territory])
                {
                    _shown[i] = under;
                    continue;
                }

                Color32 wash = colors[territory];
                _shown[i] = new Color32(
                    (byte)(wash.r * alpha + under.r * keep + 0.5f),
                    (byte)(wash.g * alpha + under.g * keep + 0.5f),
                    (byte)(wash.b * alpha + under.b * keep + 0.5f),
                    255);
            }

            if (_texture == null)
            {
                _texture = new Texture2D(_map.Width, _map.Height, TextureFormat.RGBA32, false);
                _texture.wrapMode = TextureWrapMode.Clamp;
                _texture.filterMode = FilterMode.Bilinear;
            }

            _texture.SetPixels32(_shown);
            _texture.Apply(false, false);
            if (_ground != null)
            {
                _ground.SetTexture("_BaseMap", _texture);
            }
        }

        void BuildGround()
        {
            float x0 = (float)MapSpace.Metres(_map.OriginXCm);
            float z0 = (float)MapSpace.Metres(_map.OriginYCm);
            float x1 = (float)MapSpace.Metres(_map.OriginXCm + _map.ExtentXCm);
            float z1 = (float)MapSpace.Metres(_map.OriginYCm + _map.ExtentYCm);
            Mesh mesh = new Mesh { name = "Paper" };
            mesh.vertices = new[]
            {
                new Vector3(x0, GroundY, z0),
                new Vector3(x1, GroundY, z0),
                new Vector3(x1, GroundY, z1),
                new Vector3(x0, GroundY, z1)
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            _ground = MakeMaterial("Wasteland/Tactical Ground", 2000);
            if (_ground != null)
            {
                _ground.SetTexture("_BaseMap", _texture);
            }

            AddMesh("Paper", mesh, _ground);
        }

        void BuildWaterEdges()
        {
            StrokeBuilder blocking = new StrokeBuilder();
            StrokeBuilder fordable = new StrokeBuilder();
            int width = _map.Width;
            int height = _map.Height;
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    int code = _map.Water[row * width + col];
                    int px = PaperPaint.EdgePx(code);
                    if (px == 0)
                    {
                        continue;
                    }

                    StrokeBuilder target = px >= 2 ? blocking : fordable;
                    int east = col + 1 < width ? _map.Water[row * width + col + 1] : -1;
                    if (east != code)
                    {
                        AddEdge(target, col + 1, row, col + 1, row + 1, true);
                    }

                    int north = row + 1 < height ? _map.Water[(row + 1) * width + col] : -1;
                    if (north != code)
                    {
                        AddEdge(target, col, row + 1, col + 1, row + 1, false);
                    }
                }
            }

            Color edge = ToColor(Rgb.FromHex(PaperPaint.WaterEdge));
            AddStroke("Blocking water edge", blocking, WaterY, edge, PaperPaint.BlockingEdgePx, 0f, 0f, 2100);
            AddStroke("Fordable water edge", fordable, WaterY, edge, PaperPaint.FordableEdgePx, 0f, 0f, 2110);
        }

        void AddEdge(StrokeBuilder target, int col0, int row0, int col1, int row1, bool vertical)
        {
            float x0 = EdgeMetres(col0, _map.OriginXCm, _map.Width, _map.ExtentXCm);
            float z0 = EdgeMetres(row0, _map.OriginYCm, _map.Height, _map.ExtentYCm);
            float x1 = vertical ? x0 : EdgeMetres(col1, _map.OriginXCm, _map.Width, _map.ExtentXCm);
            float z1 = vertical ? EdgeMetres(row1, _map.OriginYCm, _map.Height, _map.ExtentYCm) : z0;
            target.Add(x0, z0, x1, z1);
        }

        void BuildRoads()
        {
            StrokeBuilder pavedInk = new StrokeBuilder();
            StrokeBuilder pavedBody = new StrokeBuilder();
            StrokeBuilder gravel = new StrokeBuilder();
            StrokeBuilder dirt = new StrokeBuilder();
            for (int i = 0; i < _map.Roads.Length; i++)
            {
                MapRoad road = _map.Roads[i];
                StrokeBuilder target = dirt;
                if (road.Surface == RoadArt.Paved)
                {
                    target = pavedInk;
                }
                else if (road.Surface == RoadArt.Gravel)
                {
                    target = gravel;
                }

                AddRoad(target, road);
                if (road.Surface == RoadArt.Paved)
                {
                    AddRoad(pavedBody, road);
                }
            }

            Color ink = ToColor(Rgb.FromHex(RoadArt.Ink));
            Color amber = ToColor(Rgb.FromHex(RoadArt.Amber));
            AddStroke("Paved ink", pavedInk, RoadY, ink, RoadArt.PavedBodyPx + RoadArt.PavedInkSidePx * 2f, 0f, 0f, 2200);
            AddStroke("Paved", pavedBody, RoadY, amber, RoadArt.PavedBodyPx, 0f, 0f, 2210);
            AddStroke("Gravel", gravel, RoadY, ink, RoadArt.GravelPx, 0f, 0f, 2200);
            AddStroke("Dirt", dirt, RoadY, ink, RoadArt.DirtPx, RoadArt.DashOnPx, RoadArt.DashOffPx, 2200);
        }

        static void AddRoad(StrokeBuilder target, MapRoad road)
        {
            for (int p = 1; p < road.XCm.Length; p++)
            {
                target.Add(
                    (float)MapSpace.Metres(road.XCm[p - 1]),
                    (float)MapSpace.Metres(road.YCm[p - 1]),
                    (float)MapSpace.Metres(road.XCm[p]),
                    (float)MapSpace.Metres(road.YCm[p]));
            }
        }

        void AddStroke(string name, StrokeBuilder builder, float y, Color color, float widthPx, float dashOn, float dashOff, int queue)
        {
            Mesh mesh = builder.Build(name, y);
            if (mesh == null)
            {
                return;
            }

            Material material = MakeMaterial("Wasteland/Tactical Stroke", queue);
            if (material != null)
            {
                material.SetColor("_Color", color);
                material.SetFloat("_WidthPx", widthPx);
                material.SetFloat("_DashOn", dashOn);
                material.SetFloat("_DashOff", dashOff);
                material.SetFloat("_WorldPerPixel", 1f);
            }

            AddMesh(name, mesh, material);
        }

        Material MakeMaterial(string shaderName, int queue)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("Missing shader " + shaderName + ".");
                return null;
            }

            Material material = new Material(shader) { renderQueue = queue };
            _strokes.Add(material);
            return material;
        }

        void AddMesh(string name, Mesh mesh, Material material)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static float EdgeMetres(int index, int originCm, int cells, int extentCm)
        {
            long cm = originCm + ((long)index * extentCm) / cells;
            return (float)(cm / MapSpace.CentimetresPerMetre);
        }

        static Vector3 Flat(int xCm, int yCm, float layer)
        {
            return new Vector3((float)MapSpace.Metres(xCm), layer, (float)MapSpace.Metres(yCm));
        }

        static Color ToColor(Rgb rgb)
        {
            return new Color(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f, 1f);
        }
    }

    sealed class StrokeBuilder
    {
        readonly List<Vector3> _centers = new List<Vector3>();
        readonly List<Vector4> _sideAlong = new List<Vector4>();
        readonly List<Vector2> _perp = new List<Vector2>();
        readonly List<int> _indices = new List<int>();
        float _along;

        public void Add(float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0;
            float dz = z1 - z0;
            float length = Mathf.Sqrt(dx * dx + dz * dz);
            if (length < 0.05f)
            {
                return;
            }

            float inv = 1f / length;
            float px = -dz * inv;
            float pz = dx * inv;
            int v = _centers.Count;
            AddVert(x0, z0, -1f, _along, px, pz);
            AddVert(x0, z0, 1f, _along, px, pz);
            AddVert(x1, z1, 1f, _along + length, px, pz);
            AddVert(x1, z1, -1f, _along + length, px, pz);
            _indices.Add(v);
            _indices.Add(v + 1);
            _indices.Add(v + 2);
            _indices.Add(v);
            _indices.Add(v + 2);
            _indices.Add(v + 3);
            _along += length;
        }

        public Mesh Build(string name, float y)
        {
            if (_centers.Count == 0)
            {
                return null;
            }

            Vector3[] vertices = _centers.ToArray();
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i].y = y;
            }

            Mesh mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.SetUVs(0, _sideAlong);
            mesh.SetUVs(1, _perp);
            mesh.SetIndices(_indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        void AddVert(float x, float z, float side, float along, float px, float pz)
        {
            _centers.Add(new Vector3(x, 0f, z));
            _sideAlong.Add(new Vector4(side, along, 0f, 0f));
            _perp.Add(new Vector2(px, pz));
        }
    }

    sealed class MarkBatch
    {
        readonly List<Vector3> _pos = new List<Vector3>();
        readonly List<Vector4> _offset = new List<Vector4>();
        readonly List<Vector4> _shape = new List<Vector4>();
        readonly List<Vector4> _extra = new List<Vector4>();
        readonly List<Vector4> _secondary = new List<Vector4>();
        readonly List<Color> _color = new List<Color>();
        readonly List<int> _indices = new List<int>();

        public void Quad(
            Vector3 center,
            float halfW,
            float halfH,
            float widthPx,
            float heightPx,
            float mode,
            float flag,
            float contested,
            float capture,
            float glyph,
            Color primary,
            Color secondary)
        {
            int v = _pos.Count;
            float[] ox = { -halfW, halfW, halfW, -halfW };
            float[] oy = { -halfH, -halfH, halfH, halfH };
            Vector4 shape = new Vector4(widthPx, heightPx, mode, flag);
            Vector4 extra = new Vector4(contested, capture, glyph, 0f);
            Vector4 second = new Vector4(secondary.r, secondary.g, secondary.b, 1f);
            for (int i = 0; i < 4; i++)
            {
                _pos.Add(center);
                _offset.Add(new Vector4(ox[i], oy[i], 0f, 0f));
                _shape.Add(shape);
                _extra.Add(extra);
                _secondary.Add(second);
                _color.Add(primary);
            }

            _indices.Add(v);
            _indices.Add(v + 1);
            _indices.Add(v + 2);
            _indices.Add(v);
            _indices.Add(v + 2);
            _indices.Add(v + 3);
        }

        public void Upload(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            if (_pos.Count == 0)
            {
                return;
            }

            mesh.SetVertices(_pos);
            mesh.SetUVs(0, _offset);
            mesh.SetUVs(1, _shape);
            mesh.SetUVs(2, _extra);
            mesh.SetUVs(3, _secondary);
            mesh.SetColors(_color);
            mesh.SetIndices(_indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}
