#nullable disable
using System.Collections.Generic;
using UnityEngine;
using Wasteland.Audio;
using Wasteland.Sim.Events;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Feeds the paper map and the combat beds from the running match.
    /// Beds sit at town positions. The listener stays on the camera.
    /// </summary>
    public sealed class TacticalMatchBridge : MonoBehaviour
    {
        readonly Dictionary<int, MapTown> _towns = new Dictionary<int, MapTown>();
        readonly Dictionary<int, int> _sideA = new Dictionary<int, int>();
        readonly Dictionary<int, int> _sideB = new Dictionary<int, int>();
        readonly List<BedEvent> _pending = new List<BedEvent>();
        readonly List<AudibleBed> _beds = new List<AudibleBed>();
        readonly List<ContestedTown> _voices = new List<ContestedTown>();
        readonly List<TownView> _townViews = new List<TownView>();
        readonly GlyphBook _glyphs = new GlyphBook();

        MapDocument _map;
        SimScenario _scenario;
        CombatLoopManager _loops;
        CombatBedClock _clock;
        TacticalMapView _view;
        TacticalCamera _camera;
        int _tick;
        int _warned;
        int _ownerHash = int.MinValue;
        int _selectedTown = -1;
        int _selectedUnit = -1;
        bool _closed;

        public bool BedActive
        {
            get { return _beds.Count > 0; }
        }

        public void Open(MapDocument map, SimScenario scenario, CombatLoopManager loops, TacticalCamera camera, TacticalMapView view)
        {
            _map = map;
            _scenario = scenario;
            _loops = loops;
            _camera = camera;
            _view = view;
            _clock = new CombatBedClock();
            ulong[] keys = new ulong[scenario.SeatCount];
            for (int i = 0; i < scenario.SeatCount; i++)
            {
                keys[i] = scenario.SeatAt(i).HomeTerritoryKey;
            }

            map.BindScenarioSeats(keys);
            for (int i = 0; i < map.Towns.Length; i++)
            {
                _towns[map.Towns[i].Id] = map.Towns[i];
            }

            double x0 = MapSpace.Metres(map.OriginXCm);
            double z0 = MapSpace.Metres(map.OriginYCm);
            double x1 = MapSpace.Metres(map.OriginXCm + map.ExtentXCm);
            double z1 = MapSpace.Metres(map.OriginYCm + map.ExtentYCm);
            camera.Frame(x0, x1, z0, z1);
            // Exactly one listener, on the tactical camera.
            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener == null)
            {
                listener = camera.gameObject.AddComponent<AudioListener>();
            }

            AudioListener[] all = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != listener)
                {
                    all[i].enabled = false;
                }
            }

            listener.enabled = true;
            loops.SetListener(listener);
            view.Build(map, camera.Camera);
        }

        public void BeginTick(int tick)
        {
            _tick = tick;
            _pending.Clear();
        }

        public void Note(SimEvent ev)
        {
            if (_closed)
            {
                return;
            }

            int kind = (int)ev.Kind;
            if (kind != (int)SimEventKind.TownContestStarted && kind != (int)SimEventKind.TownContestEnded)
            {
                return;
            }

            MapTown town;
            if (!_towns.TryGetValue(ev.SubjectId, out town))
            {
                return;
            }

            _sideA[town.Id] = ev.AuxId;
            _sideB[town.Id] = ev.Param0;
            _pending.Add(new BedEvent(kind, town.Id, town.XCm, town.YCm));
        }

        public void EndTick()
        {
            if (_clock == null || _closed)
            {
                return;
            }

            _clock.ApplyTick(_tick, _pending.ToArray());
            _pending.Clear();
            Publish();
        }

        public void ClearBeds()
        {
            _closed = true;
            _pending.Clear();
            if (_clock != null)
            {
                _clock.Clear();
            }

            _beds.Clear();
            if (_loops != null)
            {
                _loops.Clear();
            }
        }

        public void Sync(SimWorld world)
        {
            if (_map == null || world == null || _view == null)
            {
                return;
            }

            world.CopyTowns(_townViews);
            PaintWash(world);
            Pick(world);
            List<TownMarker> towns = new List<TownMarker>(_townViews.Count);
            for (int i = 0; i < _townViews.Count; i++)
            {
                TownView live = _townViews[i];
                MapTown placed;
                if (!_towns.TryGetValue(live.TownId, out placed))
                {
                    continue;
                }

                bool contested = live.Contested;
                Rgb owner = MarkerArt.OwnerColor(live.OwnerFactionId, FactionKey(world, live.OwnerFactionId));
                Rgb other = owner;
                int sideA;
                int sideB;
                if (contested && _sideA.TryGetValue(placed.Id, out sideA) && _sideB.TryGetValue(placed.Id, out sideB))
                {
                    owner = MarkerArt.OwnerColor(sideA, FactionKey(world, sideA));
                    other = MarkerArt.OwnerColor(sideB, FactionKey(world, sideB));
                }

                float capture = 0f;
                if (placed.IsSeatTown && live.CaptureNeededTicks > 0 && (live.CaptureProgressTicks > 0 || contested))
                {
                    capture = live.CaptureProgressTicks / (float)live.CaptureNeededTicks;
                    if (capture < 0f)
                    {
                        capture = 0f;
                    }
                    else if (capture > 1f)
                    {
                        capture = 1f;
                    }

                    if (!contested)
                    {
                        other = MarkerArt.OwnerColor(live.CaptureFactionId, FactionKey(world, live.CaptureFactionId));
                    }
                }

                towns.Add(new TownMarker
                {
                    XCm = placed.XCm,
                    YCm = placed.YCm,
                    PopulationClass = placed.PopulationClass,
                    Hq = placed.IsScenarioHq,
                    Contested = contested,
                    Selected = placed.Id == _selectedTown,
                    Capture = capture,
                    Owner = owner,
                    Other = other,
                    Name = placed.Name
                });
            }

            int alive = 0;
            for (int i = 0; i < world.Battalions.Count; i++)
            {
                if (world.Battalions[i].Hp > 0)
                {
                    alive++;
                }
            }

            BattalionMarker[] units = new BattalionMarker[alive];
            int written = 0;
            for (int i = 0; i < world.Battalions.Count; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.Hp <= 0)
                {
                    continue;
                }

                BattalionView view;
                string unitId = string.Empty;
                if (world.TryGetBattalionView(battalion.Id, out view))
                {
                    unitId = view.UnitId;
                }

                int before = _glyphs.Warnings.Count;
                BattalionGlyph glyph = _glyphs.Resolve(unitId);
                if (_glyphs.Warnings.Count > before && _glyphs.Warnings.Count > _warned)
                {
                    _warned = _glyphs.Warnings.Count;
                    Debug.LogWarning("No battalion glyph for unit '" + unitId + "'. Drawing a plain frame.");
                }

                units[written] = new BattalionMarker
                {
                    XCm = battalion.PositionXCm,
                    YCm = battalion.PositionYCm,
                    Selected = battalion.Id == _selectedUnit,
                    Owner = MarkerArt.OwnerColor(battalion.OwnerId, FactionKey(world, battalion.OwnerId)),
                    Glyph = glyph,
                    Label = unitId
                };
                written++;
            }

            _view.SetMarkers(towns.ToArray(), units);
        }

        void LateUpdate()
        {
            if (_camera == null || _map == null)
            {
                return;
            }

            int xCm = (int)(_camera.transform.position.x * MapSpace.CentimetresPerMetre);
            int yCm = (int)(_camera.transform.position.z * MapSpace.CentimetresPerMetre);
            float ground = (float)MapSpace.Metres(_map.ElevationCmAt(xCm, yCm));
            _camera.SetHeight(ground + 40f);
            if (_loops == null || _camera.Camera == null)
            {
                return;
            }

            float halfH = _camera.OrthographicSize;
            float aspect = _camera.Camera.aspect > 0.01f ? _camera.Camera.aspect : 1f;
            float halfW = halfH * aspect;
            CombatBedRolloff.Compute(halfW, halfH, 40f, out float fullGain, out float reach);
            _loops.SetRolloff(fullGain, reach);
        }

        void Publish()
        {
            _clock.CopyAudible(_beds);
            _voices.Clear();
            for (int i = 0; i < _beds.Count; i++)
            {
                AudibleBed bed = _beds[i];
                int elev = _map.ElevationCmAt(bed.XCm, bed.YCm);
                double x;
                double y;
                double z;
                MapSpace.ToUnity(bed.XCm, elev, bed.YCm, out x, out y, out z);
                _voices.Add(new ContestedTown(bed.VoiceId, new Vector3((float)x, (float)y, (float)z)));
            }

            _loops.SetContestedTowns(_voices);
        }

        void PaintWash(SimWorld world)
        {
            int hash = 17;
            int maxId = 0;
            for (int i = 0; i < _townViews.Count; i++)
            {
                if (_townViews[i].TerritoryId > maxId)
                {
                    maxId = _townViews[i].TerritoryId;
                }

                hash = hash * 31 + _townViews[i].TerritoryId;
                hash = hash * 31 + _townViews[i].OwnerFactionId;
            }

            if (hash == _ownerHash)
            {
                return;
            }

            _ownerHash = hash;
            Color32[] colors = new Color32[maxId + 1];
            bool[] painted = new bool[maxId + 1];
            for (int i = 0; i < _townViews.Count; i++)
            {
                TownView town = _townViews[i];
                MapTown placed;
                if (!_towns.TryGetValue(town.TownId, out placed) || !placed.IsSeatTown)
                {
                    continue;
                }

                Rgb rgb = MarkerArt.OwnerColor(town.OwnerFactionId, FactionKey(world, town.OwnerFactionId));
                colors[town.TerritoryId] = new Color32(rgb.R, rgb.G, rgb.B, 255);
                painted[town.TerritoryId] = true;
            }

            _view.SetTerritoryWash(colors, painted);
        }

        void Pick(SimWorld world)
        {
            Vector2 screen;
            if (_camera == null || !_camera.TryPickScreen(out screen))
            {
                return;
            }

            float best = 18f * 18f;
            _selectedTown = -1;
            _selectedUnit = -1;
            Camera cam = _camera.Camera;
            for (int i = 0; i < _townViews.Count; i++)
            {
                MapTown placed;
                if (!_towns.TryGetValue(_townViews[i].TownId, out placed))
                {
                    continue;
                }

                float radius = MarkerArt.TownDiameterPx(placed.PopulationClass) * 0.5f + 4f;
                if (Closer(cam, placed.XCm, placed.YCm, screen, radius * radius, ref best))
                {
                    _selectedTown = placed.Id;
                    _selectedUnit = -1;
                }
            }

            for (int i = 0; i < world.Battalions.Count; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.Hp <= 0)
                {
                    continue;
                }

                if (Closer(cam, battalion.PositionXCm, battalion.PositionYCm, screen, 16f * 16f, ref best))
                {
                    _selectedUnit = battalion.Id;
                    _selectedTown = -1;
                }
            }
        }

        static bool Closer(Camera cam, int xCm, int yCm, Vector2 screen, float radius2, ref float best)
        {
            Vector3 world = new Vector3((float)MapSpace.Metres(xCm), 0.2f, (float)MapSpace.Metres(yCm));
            Vector3 point = cam.WorldToScreenPoint(world);
            if (point.z <= 0f)
            {
                return false;
            }

            float dx = point.x - screen.x;
            float dy = point.y - screen.y;
            float dist = dx * dx + dy * dy;
            if (dist > radius2 || dist > best)
            {
                return false;
            }

            best = dist;
            return true;
        }

        string FactionKey(SimWorld world, int factionId)
        {
            if (factionId < 0 || _scenario == null)
            {
                return null;
            }

            SeatView seat;
            if (!world.TryGetSeat(factionId, out seat))
            {
                return null;
            }

            for (int i = 0; i < _scenario.SeatCount; i++)
            {
                ScenarioSeat scenarioSeat = _scenario.SeatAt(i);
                if (scenarioSeat.HomeTownId == seat.HomeTownId)
                {
                    return scenarioSeat.Faction;
                }
            }

            return null;
        }
    }
}
