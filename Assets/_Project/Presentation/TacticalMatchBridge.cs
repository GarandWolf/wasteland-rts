#nullable disable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
        SimWorld _world;
        PlayerOrderSession _orders;
        BalanceCatalog _catalog;
        SoundPlayer _sounds;
        string[] _raisable = System.Array.Empty<string>();
        int _humanSeat;
        bool _hasHuman;
        int _groundX;
        int _groundY;
        bool _groundSet;
        readonly UnderAttackAlerts _attacks = new UnderAttackAlerts();
        readonly BattalionContactWatch _contact = new BattalionContactWatch();
        readonly ResearchTrack _research = new ResearchTrack();
        readonly ResearchCueGate _researchCue = new ResearchCueGate();
        readonly HqOutcomeGate _outcome = new HqOutcomeGate();
        readonly List<LiveRing> _rings = new List<LiveRing>();
        HqBanner _banner;
        bool _roadChosen;
        int _roadNode;
        int _roadX;
        int _roadY;
        int _jumpX;
        int _jumpY;
        bool _jumpReady;

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

        /// <summary>
        /// Human seat for orders. An AI-versus-AI match passes <paramref name="hasHuman"/> false
        /// and no order click is played.
        /// </summary>
        public void BindPlayer(int humanSeat, bool hasHuman, string[] raisableUnitIds, BalanceCatalog catalog, SoundPlayer sounds)
        {
            _humanSeat = humanSeat;
            _hasHuman = hasHuman;
            _raisable = raisableUnitIds ?? System.Array.Empty<string>();
            _catalog = catalog;
            _sounds = sounds;
            _orders = null;
        }

        public void BeginTick(int tick)
        {
            BeginTick(tick, null);
        }

        public void BeginTick(int tick, SimWorld world)
        {
            _tick = tick;
            if (world != null)
            {
                _world = world;
            }

            _pending.Clear();
        }

        public void Note(SimEvent ev)
        {
            int kind = (int)ev.Kind;
            if (kind == (int)SimEventKind.TierResearchStarted)
            {
                if (_hasHuman && ev.SubjectId == _humanSeat)
                {
                    _research.Started(ev.Param0, ev.Param1);
                }

                return;
            }

            if (kind == (int)SimEventKind.TierReached)
            {
                if (_hasHuman && ev.SubjectId == _humanSeat)
                {
                    _research.Reached();
                }

                float volume;
                if (_sounds != null && _researchCue.TryConsume(kind, _hasHuman, _humanSeat, ev.SubjectId, ev.AuxId, out volume))
                {
                    _sounds.Play(SoundCue.ResearchDone, volume);
                }

                return;
            }

            if (kind == (int)SimEventKind.MatchEnded)
            {
                _banner = _outcome.Read(true, ev.AuxId, ev.SubjectId, _hasHuman, _humanSeat, WinnerName(ev.SubjectId));
                PlayStinger(_banner);
                return;
            }

            if (_closed)
            {
                return;
            }

            if (kind == (int)SimEventKind.TownContestStarted)
            {
                NoteTownAttack(ev);
            }

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

            _world = world;
            EnsureOrders(world);
            WatchBattles(world);
            string polled = _orders.PollRejection();
            if (!string.IsNullOrEmpty(polled))
            {
                ApplyNotice(new PlayerOrderNotice(false, polled, OrderClick.ForHumanSeat(_hasHuman), true, default(Wasteland.Sim.Commands.SimCommand)));
            }

            world.CopyTowns(_townViews);
            PaintWash(world);
            Pick(world);
            _view.SetOrderLines(_orders.Lines());
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

        void Update()
        {
            PublishRings();
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                JumpToAlert();
            }

            Vector2 screen;
            if (_orders == null || _camera == null || _map == null || _world == null || !_camera.TryConsumeOrderClick(out screen))
            {
                return;
            }

            if (_banner.Show)
            {
                return;
            }

            if (screen.x < 440f && screen.y > Screen.height - 528f && screen.y < Screen.height - 108f)
            {
                return;
            }

            IssueAt(screen);
        }

        void OnGUI()
        {
            if (_orders == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(12f, 108f, 420f, 420f));
            if (!_orders.HasHuman)
            {
                GUILayout.Label("No human seat. Player orders are not sent.");
            }

            string research = PlayerOrderSession.TierTwoLabel(_catalog);
            ResearchBarView bar = _research.Sample(_hasHuman, _world == null ? _tick : _world.Tick);
            if (bar.Visible)
            {
                Rect row = GUILayoutUtility.GetRect(400f, 36f);
                if (GUI.Button(row, research))
                {
                    ApplyNotice(_orders.ResearchTierTwo());
                }

                Rect meter = new Rect(row.x + 6f, row.yMax - 8f, row.width - 12f, 4f);
                DrawResearchBar(meter, bar.Fill);
            }
            else if (GUILayout.Button(research))
            {
                ApplyNotice(_orders.ResearchTierTwo());
            }

            bool depotOn = DepotButton.Enabled(_roadChosen);
            GUI.enabled = depotOn;
            if (GUILayout.Button(new GUIContent("Build depot", DepotButton.Tooltip(_roadChosen))))
            {
                int fromX;
                int fromY;
                SelectedPoint(out fromX, out fromY);
                ApplyNotice(_orders.BuildDepot(fromX, fromY, _roadX, _roadY, FactionRgb(_humanSeat)));
            }

            GUI.enabled = true;
            if (!depotOn)
            {
                GUILayout.Label(DepotButton.Tooltip(false));
            }

            for (int i = 0; i < _raisable.Length; i++)
            {
                string unitId = _raisable[i];
                if (string.IsNullOrEmpty(unitId))
                {
                    continue;
                }

                if (GUILayout.Button("Raise " + unitId))
                {
                    int x;
                    int y;
                    if (!TryRally(out x, out y))
                    {
                        ApplyNotice(new PlayerOrderNotice(false, "No headquarters is on this map.", OrderClick.ForHumanSeat(_hasHuman), true, default(Wasteland.Sim.Commands.SimCommand)));
                    }
                    else
                    {
                        ApplyNotice(_orders.Raise(unitId, x, y, FactionRgb(_humanSeat)));
                    }
                }
            }

            if (_jumpReady && GUILayout.Button("Under attack"))
            {
                JumpToAlert();
            }

            GUILayout.EndArea();
            if (_banner.Show)
            {
                DrawResult();
            }
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

            if (_orders != null)
            {
                _orders.SelectBattalion(_selectedUnit > 0 ? _selectedUnit : 0);
            }

            if (_selectedTown < 0 && _selectedUnit < 0)
            {
                Vector3 ground = TacticalCamera.GroundPoint(cam, screen);
                int xCm;
                int yCm;
                int column;
                int row;
                MapSpace.PickGround(
                    ground.x,
                    ground.z,
                    _map.OriginXCm,
                    _map.OriginYCm,
                    _map.Width,
                    _map.Height,
                    _map.ExtentXCm,
                    _map.ExtentYCm,
                    out xCm,
                    out yCm,
                    out column,
                    out row);
                RecordRoad(xCm, yCm);
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

        void EnsureOrders(SimWorld world)
        {
            if (_orders != null || _map == null || world == null)
            {
                return;
            }

            _orders = new PlayerOrderSession(
                new SimPlayerCommandStream(world),
                _humanSeat,
                _hasHuman,
                _map.Map.Roads);
        }

        void IssueAt(Vector2 screen)
        {
            Camera cam = _camera.Camera;
            if (cam == null || _world == null)
            {
                return;
            }

            Vector3 ground = TacticalCamera.GroundPoint(cam, screen);
            int xCm;
            int yCm;
            int column;
            int row;
            MapSpace.PickGround(
                ground.x,
                ground.z,
                _map.OriginXCm,
                _map.OriginYCm,
                _map.Width,
                _map.Height,
                _map.ExtentXCm,
                _map.ExtentYCm,
                out xCm,
                out yCm,
                out column,
                out row);
            if (column < 0 || row < 0 || column >= _map.Width || row >= _map.Height)
            {
                return;
            }

            _groundX = xCm;
            _groundY = yCm;
            _groundSet = true;
            RecordRoad(xCm, yCm);

            float best = 18f * 18f;
            int battalionId = 0;
            int battalionX = 0;
            int battalionY = 0;
            for (int i = 0; i < _world.Battalions.Count; i++)
            {
                SimBattalion battalion = _world.Battalions[i];
                if (battalion.Hp <= 0)
                {
                    continue;
                }

                if (Closer(cam, battalion.PositionXCm, battalion.PositionYCm, screen, 16f * 16f, ref best))
                {
                    battalionId = battalion.Id;
                    battalionX = battalion.PositionXCm;
                    battalionY = battalion.PositionYCm;
                }
            }

            int fromX;
            int fromY;
            SelectedPoint(out fromX, out fromY);
            Rgb color = FactionRgb(_humanSeat);
            if (battalionId != 0)
            {
                ApplyNotice(_orders.Attack(fromX, fromY, battalionId, battalionX, battalionY, color));
                return;
            }

            best = 18f * 18f;
            int townId = 0;
            int townX = 0;
            int townY = 0;
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
                    townId = placed.Id;
                    townX = placed.XCm;
                    townY = placed.YCm;
                }
            }

            if (townId != 0)
            {
                ApplyNotice(_orders.ClaimTown(fromX, fromY, townId, townX, townY, color));
                return;
            }

            ApplyNotice(_orders.Move(fromX, fromY, xCm, yCm, color));
        }

        void SelectedPoint(out int xCm, out int yCm)
        {
            xCm = _groundX;
            yCm = _groundY;
            if (_world == null || _orders == null || _orders.SelectedBattalionId == 0)
            {
                return;
            }

            for (int i = 0; i < _world.Battalions.Count; i++)
            {
                SimBattalion battalion = _world.Battalions[i];
                if (battalion.Id == _orders.SelectedBattalionId)
                {
                    xCm = battalion.PositionXCm;
                    yCm = battalion.PositionYCm;
                    return;
                }
            }
        }

        bool TryRally(out int xCm, out int yCm)
        {
            int hqNode;
            int hqX;
            int hqY;
            bool hq = TryHumanHq(out hqNode, out hqX, out hqY);
            int node;
            return OrderAnchor.TryPlace(_roadChosen, _roadNode, _roadX, _roadY, hq, hqNode, hqX, hqY, out node, out xCm, out yCm);
        }

        Rgb FactionRgb(int factionId)
        {
            return MarkerArt.OwnerColor(factionId, FactionKey(_world, factionId));
        }

        void ApplyNotice(PlayerOrderNotice notice)
        {
            if (_view != null)
            {
                _view.SetRejectReason(notice.Reason);
                if (_orders != null)
                {
                    _view.SetOrderLines(_orders.Lines());
                }
            }

            if (!notice.PlayClick || _sounds == null)
            {
                return;
            }

            if (notice.SoftDeny)
            {
                if (OrderDeny.ForHumanSeat(_hasHuman) && SoundCues.FileName(SoundCue.OrderDeny) == OrderDeny.KenneyFileName)
                {
                    _sounds.Play(SoundCue.OrderDeny);
                }

                return;
            }

            if (SoundCues.FileName(SoundCue.UiClick) != OrderClick.KenneyFileName)
            {
                return;
            }

            _sounds.Play(SoundCue.UiClick);
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

        void NoteTownAttack(SimEvent ev)
        {
            if (!_hasHuman || ev.Param1 != _humanSeat)
            {
                return;
            }

            MapTown town;
            if (!_towns.TryGetValue(ev.SubjectId, out town))
            {
                return;
            }

            bool hq = false;
            SeatView seat;
            if (_world != null && _world.TryGetSeat(_humanSeat, out seat))
            {
                hq = town.Id == seat.HomeTownId;
            }

            UnderAttackAlert alert;
            if (_attacks.TryTown(_tick, town.Id, hq, town.XCm, town.YCm, true, FactionRgb(_humanSeat), out alert))
            {
                RaiseAlert(alert);
            }
        }

        void WatchBattles(SimWorld world)
        {
            if (!_hasHuman || world == null)
            {
                return;
            }

            for (int i = 0; i < world.Battalions.Count; i++)
            {
                SimBattalion attacker = world.Battalions[i];
                if (attacker.Hp <= 0 || attacker.OwnerId == _humanSeat || attacker.AttackTargetId == 0)
                {
                    continue;
                }

                for (int j = 0; j < world.Battalions.Count; j++)
                {
                    SimBattalion target = world.Battalions[j];
                    if (target.Id != attacker.AttackTargetId || target.OwnerId != _humanSeat || target.Hp <= 0)
                    {
                        continue;
                    }

                    if (!_contact.InContact(
                        target.Id,
                        attacker.PositionXCm,
                        attacker.PositionYCm,
                        target.PositionXCm,
                        target.PositionYCm,
                        WeaponRangeCm(world, attacker),
                        target.Hp))
                    {
                        continue;
                    }

                    UnderAttackAlert alert;
                    if (_attacks.TryBattalion(world.Tick, target.Id, target.PositionXCm, target.PositionYCm, true, FactionRgb(_humanSeat), out alert))
                    {
                        RaiseAlert(alert);
                    }
                }
            }
        }

        int WeaponRangeCm(SimWorld world, SimBattalion attacker)
        {
            if (_catalog == null || world == null)
            {
                return 0;
            }

            BattalionView view;
            int range;
            if (!world.TryGetBattalionView(attacker.Id, out view) || !_catalog.TryGetWeaponRangeCm(view.UnitId, out range))
            {
                return 0;
            }

            return range;
        }

        void RaiseAlert(UnderAttackAlert alert)
        {
            if (!alert.ShowRing && !alert.PlayCue)
            {
                return;
            }

            if (alert.RevealsEnemyOrder || EnemyIntent.ShowOrders)
            {
                return;
            }

            _jumpX = alert.XCm;
            _jumpY = alert.YCm;
            _jumpReady = true;
            _rings.Add(new LiveRing(alert.XCm, alert.YCm, alert.Fill, Time.unscaledTime));
            if (alert.PlayCue && _sounds != null && !alert.Spatial && SoundCues.FileName(SoundCue.UnderAttack) == alert.CueFile)
            {
                _sounds.Play(SoundCue.UnderAttack, alert.Volume);
            }
        }

        void PublishRings()
        {
            if (_view == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                if (now - _rings[i].Born > 8f)
                {
                    _rings.RemoveAt(i);
                }
            }

            AlertRingMark[] marks = new AlertRingMark[_rings.Count];
            float pulse = AlertRingStyle.Pulse(now);
            for (int i = 0; i < _rings.Count; i++)
            {
                LiveRing ring = _rings[i];
                marks[i] = new AlertRingMark
                {
                    XCm = ring.XCm,
                    YCm = ring.YCm,
                    Pulse = pulse,
                    Fill = ring.Fill
                };
            }

            _view.SetAlertRings(marks);
            if (_rings.Count == 0)
            {
                _jumpReady = false;
            }
        }

        void JumpToAlert()
        {
            if (!_jumpReady || _camera == null)
            {
                return;
            }

            _camera.LookAt((float)MapSpace.Metres(_jumpX), (float)MapSpace.Metres(_jumpY));
        }

        void RecordRoad(int xCm, int yCm)
        {
            if (_map == null)
            {
                return;
            }

            int node;
            int nx;
            int ny;
            if (!RoadPick.TryHit(_map.Roads, xCm, yCm, RoadPick.ClickRadiusCm, out node, out nx, out ny) || node == 0)
            {
                return;
            }

            _roadChosen = true;
            _roadNode = node;
            _roadX = nx;
            _roadY = ny;
        }

        bool TryHumanHq(out int nodeId, out int xCm, out int yCm)
        {
            nodeId = 0;
            xCm = 0;
            yCm = 0;
            if (_world == null || _map == null || !_hasHuman)
            {
                return false;
            }

            SeatView seat;
            if (!_world.TryGetSeat(_humanSeat, out seat))
            {
                return false;
            }

            Wasteland.Sim.Movement.RoadGraph roads = _map.Map.Roads;
            RoadNodePin[] pins = new RoadNodePin[roads.NodeCount];
            for (int i = 0; i < roads.NodeCount; i++)
            {
                pins[i] = new RoadNodePin(roads.GetNodeId(i), roads.GetNodeXCm(i), roads.GetNodeYCm(i));
            }

            return HqNode.TryFromHomeTown(seat.HomeTownId, _map.Towns, pins, out nodeId, out xCm, out yCm);
        }

        string WinnerName(int seatId)
        {
            return SeatLabel.FromKey(FactionKey(_world, seatId));
        }

        void PlayStinger(HqBanner banner)
        {
            if (!banner.Play || _sounds == null)
            {
                return;
            }

            if (banner.StingerFile == SoundCues.FileName(SoundCue.WinStinger))
            {
                _sounds.Play(SoundCue.WinStinger);
                return;
            }

            if (banner.StingerFile == SoundCues.FileName(SoundCue.LoseStinger))
            {
                _sounds.Play(SoundCue.LoseStinger);
            }
        }

        void DrawResearchBar(Rect row, float fill)
        {
            if (fill < 0f)
            {
                fill = 0f;
            }
            else if (fill > 1f)
            {
                fill = 1f;
            }

            Color previous = GUI.color;
            GUI.color = new Color(0.12f, 0.12f, 0.11f, 0.9f);
            GUI.DrawTexture(row, Texture2D.whiteTexture);
            if (fill > 0f)
            {
                Rect done = row;
                done.width = row.width * fill;
                Rgb rgb = FactionRgb(_humanSeat);
                GUI.color = new Color(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f, 1f);
                GUI.DrawTexture(done, Texture2D.whiteTexture);
            }

            GUI.color = previous;
        }

        void DrawResult()
        {
            const float width = 520f;
            const float height = 180f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(panel, GUIContent.none);
            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.alignment = TextAnchor.MiddleCenter;
            title.fontSize = 42;
            title.normal.textColor = new Color(0.1176f, 0.1216f, 0.1137f, 1f);
            GUI.Label(new Rect(panel.x, panel.y + 36f, width, 64f), _banner.Title, title);
            if (!string.IsNullOrEmpty(_banner.Detail))
            {
                GUIStyle detail = new GUIStyle(title);
                detail.fontSize = 22;
                GUI.Label(new Rect(panel.x, panel.y + 108f, width, 36f), _banner.Detail, detail);
            }
        }

        struct LiveRing
        {
            public readonly int XCm;
            public readonly int YCm;
            public readonly Rgb Fill;
            public readonly float Born;

            public LiveRing(int xCm, int yCm, Rgb fill, float born)
            {
                XCm = xCm;
                YCm = yCm;
                Fill = fill;
                Born = born;
            }
        }
    }
}
