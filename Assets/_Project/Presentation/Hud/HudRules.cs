#nullable disable
using System;
using System.Collections.Generic;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Enemy attack lines and target markers stay off the map.
    /// The under-attack ring sits on the player's own town or battalion.
    /// </summary>
    public static class EnemyIntent
    {
        public static bool ShowOrders
        {
            get { return false; }
        }

        public static bool ShowTargets
        {
            get { return false; }
        }
    }

    /// <summary>Faction fill with an ink outline. Neutral grey is never used.</summary>
    public static class AlertRingStyle
    {
        public static Rgb Outline
        {
            get { return Rgb.FromHex(MarkerArt.Ink); }
        }

        public static Rgb Fill(Rgb faction)
        {
            return OrderLineColor.Resolve(faction);
        }

        /// <summary>0.7 at the start of each second, 1.0 a quarter second later.</summary>
        public static float Pulse(float seconds)
        {
            double wave = Math.Sin(seconds * 6.283185307179586);
            if (wave < 0.0)
            {
                wave = -wave;
            }

            return 0.7f + 0.3f * (float)wave;
        }
    }

    public readonly struct UnderAttackAlert
    {
        public readonly bool PlayCue;
        public readonly bool ShowRing;
        public readonly bool Spatial;
        public readonly bool RevealsEnemyOrder;
        public readonly float Volume;
        public readonly string CueFile;
        public readonly int XCm;
        public readonly int YCm;
        public readonly Rgb Fill;
        public readonly Rgb Outline;

        public UnderAttackAlert(
            bool playCue,
            bool showRing,
            float volume,
            string cueFile,
            int xCm,
            int yCm,
            Rgb fill,
            Rgb outline)
        {
            PlayCue = playCue;
            ShowRing = showRing;
            Spatial = false;
            RevealsEnemyOrder = false;
            Volume = volume;
            CueFile = cueFile ?? string.Empty;
            XCm = xCm;
            YCm = yCm;
            Fill = fill;
            Outline = outline;
        }
    }

    /// <summary>
    /// A targeted battalion is in contact when the attacker is inside weapon
    /// range, or the target has lost hit points since the last sample.
    /// A far <c>AttackTargetId</c> is not contact.
    /// </summary>
    public static class BattalionContact
    {
        public static bool WithinWeaponRange(int attackerXCm, int attackerYCm, int targetXCm, int targetYCm, int weaponRangeCm)
        {
            if (weaponRangeCm <= 0)
            {
                return false;
            }

            long dx = (long)attackerXCm - targetXCm;
            long dy = (long)attackerYCm - targetYCm;
            long range = weaponRangeCm;
            return (dx * dx) + (dy * dy) <= range * range;
        }
    }

    /// <summary>
    /// Remembers hit points so a later sample can see a loss. The first sample
    /// is a baseline, not damage.
    /// </summary>
    public sealed class BattalionContactWatch
    {
        readonly Dictionary<int, int> _hp = new Dictionary<int, int>();

        public bool InContact(
            int targetId,
            int attackerXCm,
            int attackerYCm,
            int targetXCm,
            int targetYCm,
            int weaponRangeCm,
            int hp)
        {
            int previous;
            bool known = _hp.TryGetValue(targetId, out previous);
            _hp[targetId] = hp;
            bool lostHp = known && hp < previous;
            return lostHp || BattalionContact.WithinWeaponRange(attackerXCm, attackerYCm, targetXCm, targetYCm, weaponRangeCm);
        }
    }

    /// <summary>
    /// Area key for hit alerts: the land territory under the point (read from the
    /// loaded map), or a coarse map-space grid cell where there is none. No ids are
    /// hard-coded.
    /// </summary>
    public static class CombatArea
    {
        public const int GridCellCm = 1000000;

        public static int Key(int territoryId, int xCm, int yCm)
        {
            if (territoryId > 0)
            {
                return territoryId;
            }

            int cx = FloorDiv(xCm, GridCellCm) & 0x7FFF;
            int cy = FloorDiv(yCm, GridCellCm) & 0x7FFF;
            return -1 - ((cy << 15) | cx);
        }

        static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            if (value % divisor != 0 && value < 0)
            {
                q--;
            }

            return q;
        }
    }

    public readonly struct HpHit
    {
        public readonly int Id;
        public readonly int OwnerId;
        public readonly int XCm;
        public readonly int YCm;

        public HpHit(int id, int ownerId, int xCm, int yCm)
        {
            Id = id;
            OwnerId = ownerId;
            XCm = xCm;
            YCm = yCm;
        }
    }

    /// <summary>
    /// Per-tick hit point watch over every live battalion, both sides. Combat
    /// fire does not set <c>AttackTargetId</c>, so a loss of hit points is the
    /// presentation's contact signal. The first sample of a battalion is a baseline.
    /// A hit flashes the marker toward white for <see cref="FlashTicks"/> ticks,
    /// and every hit is drawn at least once even at high sim speed.
    /// </summary>
    public sealed class HpLossWatch
    {
        public const int FlashTicks = 6;

        readonly Dictionary<int, int> _hp = new Dictionary<int, int>();
        readonly Dictionary<int, int> _hitTick = new Dictionary<int, int>();
        readonly HashSet<int> _undrawn = new HashSet<int>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _stale = new List<int>();

        public void BeginScan()
        {
            _seen.Clear();
        }

        /// <summary>True when this live battalion lost hit points since its last sample.</summary>
        public bool Sample(int tick, int id, int hp)
        {
            _seen.Add(id);
            int previous;
            bool hit = _hp.TryGetValue(id, out previous) && hp < previous;
            _hp[id] = hp;
            if (hit)
            {
                _hitTick[id] = tick;
                _undrawn.Add(id);
            }

            return hit;
        }

        /// <summary>Forgets battalions that were not sampled since <see cref="BeginScan"/>.</summary>
        public void EndScan()
        {
            _stale.Clear();
            foreach (KeyValuePair<int, int> pair in _hp)
            {
                if (!_seen.Contains(pair.Key))
                {
                    _stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                _hp.Remove(_stale[i]);
                _hitTick.Remove(_stale[i]);
                _undrawn.Remove(_stale[i]);
            }
        }

        /// <summary>0 = no flash, 1 = full white. Marks the hit as drawn.</summary>
        public float Flash(int id, int tick)
        {
            int hitTick;
            if (!_hitTick.TryGetValue(id, out hitTick))
            {
                return 0f;
            }

            bool undrawn = _undrawn.Remove(id);
            int age = tick - hitTick;
            if (age >= 0 && age < FlashTicks)
            {
                return 1f - (age / (float)FlashTicks);
            }

            return undrawn ? 1f : 0f;
        }
    }

    /// <summary>
    /// Battalion destroyed (sim event 15). Reuses the existing CC0 placeholder
    /// <see cref="CueFile"/>; no new asset. Full volume for the human's own loss,
    /// quieter for anyone else's.
    /// </summary>
    public static class DeathCue
    {
        public const string CueFile = "town_lost.wav";
        public const float OwnVolume = 1f;
        public const float OtherVolume = 0.6f;

        public static float Volume(bool hasHuman, int humanSeat, int ownerId)
        {
            return hasHuman && ownerId == humanSeat ? OwnVolume : OtherVolume;
        }
    }

    /// <summary>
    /// One alert when the human seat is attacked. Towns wait
    /// <see cref="CooldownTicks"/> between cues. The HQ does not wait.
    /// A spectator match stays silent.
    /// </summary>
    public sealed class UnderAttackAlerts
    {
        public const int CooldownTicks = 600;
        public const float Volume = 0.8f;
        public const string CueFile = "under_attack.wav";

        readonly Dictionary<int, int> _towns = new Dictionary<int, int>();
        readonly Dictionary<int, int> _battalions = new Dictionary<int, int>();
        readonly Dictionary<int, int> _areas = new Dictionary<int, int>();

        /// <summary>One battalion-hit alert per area per 10 seconds (200 ticks at 20 Hz).</summary>
        public const int AreaCooldownTicks = 200;

        /// <summary>
        /// A human battalion lost hit points. <paramref name="areaKey"/> comes from
        /// <see cref="CombatArea.Key"/>. Does not need the attacker's
        /// <c>AttackTargetId</c>: auto-fire never sets it.
        /// </summary>
        public bool TryHit(
            int tick,
            int areaKey,
            int xCm,
            int yCm,
            bool humanSeat,
            Rgb faction,
            out UnderAttackAlert alert)
        {
            return Try(_areas, AreaCooldownTicks, tick, areaKey, false, xCm, yCm, humanSeat, faction, out alert);
        }

        public bool TryTown(
            int tick,
            int townId,
            bool hq,
            int xCm,
            int yCm,
            bool humanSeat,
            Rgb faction,
            out UnderAttackAlert alert)
        {
            return Try(_towns, CooldownTicks, tick, townId, hq, xCm, yCm, humanSeat, faction, out alert);
        }

        public bool TryBattalion(
            int tick,
            int battalionId,
            int xCm,
            int yCm,
            bool humanSeat,
            Rgb faction,
            out UnderAttackAlert alert)
        {
            return Try(_battalions, CooldownTicks, tick, battalionId, false, xCm, yCm, humanSeat, faction, out alert);
        }

        bool Try(
            Dictionary<int, int> last,
            int cooldownTicks,
            int tick,
            int subjectId,
            bool bypass,
            int xCm,
            int yCm,
            bool humanSeat,
            Rgb faction,
            out UnderAttackAlert alert)
        {
            alert = default(UnderAttackAlert);
            if (!humanSeat)
            {
                return false;
            }

            int previous;
            if (!bypass && last.TryGetValue(subjectId, out previous) && tick - previous < cooldownTicks)
            {
                return false;
            }

            last[subjectId] = tick;
            alert = new UnderAttackAlert(
                true,
                true,
                Volume,
                CueFile,
                xCm,
                yCm,
                AlertRingStyle.Fill(faction),
                AlertRingStyle.Outline);
            return true;
        }
    }

    public readonly struct HqBanner
    {
        public readonly bool Show;
        public readonly bool Play;
        public readonly string Title;
        public readonly string Detail;
        public readonly string StingerFile;

        public HqBanner(bool show, bool play, string title, string detail, string stingerFile)
        {
            Show = show;
            Play = play;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
            StingerFile = stingerFile ?? string.Empty;
        }

        public HqBanner WithoutReplay()
        {
            return new HqBanner(Show, false, Title, Detail, StingerFile);
        }
    }

    /// <summary>
    /// HQ capture is the only MVP result. A draw is titled Draw and still plays
    /// the defeat stinger. The human hears their own side. A spectator victory
    /// names the winner.
    /// </summary>
    public static class HqOutcome
    {
        public const int ReasonHqCaptured = 1;
        public const int ReasonDraw = 2;
        public const string WinFile = "win_stinger.wav";
        public const string DefeatFile = "lose_stinger.wav";

        public static HqBanner Read(
            bool over,
            int reason,
            int winnerSeat,
            bool hasHuman,
            int humanSeat,
            string winnerName)
        {
            if (!over || (reason != ReasonHqCaptured && reason != ReasonDraw))
            {
                return new HqBanner(false, false, string.Empty, string.Empty, string.Empty);
            }

            bool draw = reason == ReasonDraw || winnerSeat < 0;
            if (draw)
            {
                return new HqBanner(true, true, "Draw", "Both headquarters fell.", DefeatFile);
            }

            if (hasHuman)
            {
                if (winnerSeat == humanSeat)
                {
                    return new HqBanner(true, true, "Victory", "Enemy headquarters captured.", WinFile);
                }

                return new HqBanner(true, true, "Defeat", string.Empty, DefeatFile);
            }

            return new HqBanner(true, true, "Victory", winnerName ?? string.Empty, WinFile);
        }
    }

    public sealed class HqOutcomeGate
    {
        bool _played;

        public HqBanner Read(
            bool over,
            int reason,
            int winnerSeat,
            bool hasHuman,
            int humanSeat,
            string winnerName)
        {
            HqBanner banner = HqOutcome.Read(over, reason, winnerSeat, hasHuman, humanSeat, winnerName);
            if (!banner.Show)
            {
                return banner;
            }

            if (_played)
            {
                return banner.WithoutReplay();
            }

            _played = true;
            return banner;
        }
    }

    public readonly struct ResearchBarView
    {
        public readonly bool Visible;
        public readonly float Fill;

        public ResearchBarView(bool visible, float fill)
        {
            Visible = visible;
            Fill = fill;
        }
    }

    /// <summary>
    /// Tier II progress for the viewer's own seat. Clamped to 0–1.
    /// Hidden once event 6 (<see cref="Wasteland.Sim.Events.SimEventKind.TierReached"/>) lands.
    /// </summary>
    public static class ResearchBar
    {
        public static ResearchBarView Sample(bool ownSeat, int startTick, int completeTick, int nowTick, bool tierReached)
        {
            if (!ownSeat || tierReached || startTick < 0 || completeTick < startTick)
            {
                return new ResearchBarView(false, 0f);
            }

            int span = completeTick - startTick;
            float fill = 1f;
            if (span > 0)
            {
                fill = (nowTick - startTick) / (float)span;
            }

            if (fill < 0f)
            {
                fill = 0f;
            }
            else if (fill > 1f)
            {
                fill = 1f;
            }

            return new ResearchBarView(true, fill);
        }
    }

    public static class ResearchCue
    {
        public const int TierReachedKind = 6;
        public const float HumanVolume = 1f;
        public const float SpectatorVolume = 0.6f;
        public const string CueFile = "research_done.wav";
        public const string FileName = CueFile;

        public static bool Spatial
        {
            get { return false; }
        }
    }

    /// <summary>Plays research_done once per seat and tier. Event 6 only.</summary>
    public sealed class ResearchCueGate
    {
        readonly HashSet<long> _played = new HashSet<long>();

        public bool TryConsume(int eventKind, bool hasHuman, int humanSeat, int subjectSeat, int tier, out float volume)
        {
            volume = 0f;
            if (eventKind != ResearchCue.TierReachedKind)
            {
                return false;
            }

            if (hasHuman && subjectSeat != humanSeat)
            {
                return false;
            }

            long key = ((long)subjectSeat << 32) | (uint)tier;
            if (!_played.Add(key))
            {
                return false;
            }

            volume = hasHuman ? ResearchCue.HumanVolume : ResearchCue.SpectatorVolume;
            return true;
        }
    }

    public sealed class ResearchTrack
    {
        int _start = -1;
        int _complete = -1;
        bool _reached;

        public void Started(int startTick, int completeTick)
        {
            _start = startTick;
            _complete = completeTick;
            _reached = false;
        }

        public void Reached()
        {
            _reached = true;
        }

        public ResearchBarView Sample(bool ownSeat, int nowTick)
        {
            return ResearchBar.Sample(ownSeat, _start, _complete, nowTick, _reached);
        }
    }

    /// <summary>Kenney click2. A different 2D click from the accept cue, human seat only.</summary>
    public static class OrderDeny
    {
        public const string KenneyFileName = "order_deny.wav";
        public const string KenneySource = "click2.ogg";

        public static bool Spatial
        {
            get { return false; }
        }

        public static bool ForHumanSeat(bool humanSeat)
        {
            return humanSeat;
        }
    }

    public static class DepotButton
    {
        public const string NeedRoad = "Click a road to place a depot.";

        public static bool Enabled(bool roadChosen)
        {
            return roadChosen;
        }

        public static string Tooltip(bool roadChosen)
        {
            return roadChosen ? string.Empty : NeedRoad;
        }
    }

    public readonly struct RoadNodePin
    {
        public readonly int Id;
        public readonly int XCm;
        public readonly int YCm;

        public RoadNodePin(int id, int xCm, int yCm)
        {
            Id = id;
            XCm = xCm;
            YCm = yCm;
        }
    }

    /// <summary>The seat HQ node, read from the home town on the loaded map.</summary>
    public static class HqNode
    {
        public static bool TryFromHomeTown(int homeTownId, MapTown[] towns, RoadNodePin[] nodes, out int nodeId, out int xCm, out int yCm)
        {
            nodeId = 0;
            xCm = 0;
            yCm = 0;
            if (towns == null || nodes == null || homeTownId == 0)
            {
                return false;
            }

            int roadNode = 0;
            bool foundTown = false;
            for (int i = 0; i < towns.Length; i++)
            {
                MapTown town = towns[i];
                if (town != null && town.Id == homeTownId)
                {
                    roadNode = town.RoadNodeId;
                    foundTown = true;
                    break;
                }
            }

            if (!foundTown || roadNode == 0)
            {
                return false;
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Id != roadNode)
                {
                    continue;
                }

                nodeId = nodes[i].Id;
                xCm = nodes[i].XCm;
                yCm = nodes[i].YCm;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// A clicked road wins. Otherwise the HQ. The node nearest the map origin is not a target.
    /// </summary>
    public static class OrderAnchor
    {
        public static bool TryPlace(
            bool roadChosen,
            int roadNodeId,
            int roadXCm,
            int roadYCm,
            bool hqKnown,
            int hqNodeId,
            int hqXCm,
            int hqYCm,
            out int nodeId,
            out int xCm,
            out int yCm)
        {
            if (roadChosen && roadNodeId != 0)
            {
                nodeId = roadNodeId;
                xCm = roadXCm;
                yCm = roadYCm;
                return true;
            }

            if (hqKnown && hqNodeId != 0)
            {
                nodeId = hqNodeId;
                xCm = hqXCm;
                yCm = hqYCm;
                return true;
            }

            nodeId = 0;
            xCm = 0;
            yCm = 0;
            return false;
        }
    }

    public static class RoadPick
    {
        public const int ClickRadiusCm = 8000;

        public static bool TryHit(MapRoad[] roads, int xCm, int yCm, int radiusCm, out int nodeId, out int nodeXCm, out int nodeYCm)
        {
            nodeId = 0;
            nodeXCm = 0;
            nodeYCm = 0;
            if (roads == null || radiusCm < 0)
            {
                return false;
            }

            long best = (long)radiusCm * radiusCm;
            bool hit = false;
            for (int i = 0; i < roads.Length; i++)
            {
                MapRoad road = roads[i];
                if (road == null || road.XCm == null || road.YCm == null)
                {
                    continue;
                }

                int count = road.XCm.Length < road.YCm.Length ? road.XCm.Length : road.YCm.Length;
                for (int p = 1; p < count; p++)
                {
                    long dist = DistanceSq(xCm, yCm, road.XCm[p - 1], road.YCm[p - 1], road.XCm[p], road.YCm[p]);
                    if (dist > best)
                    {
                        continue;
                    }

                    best = dist;
                    hit = true;
                    long from = DistanceSqPoint(xCm, yCm, road.XCm[p - 1], road.YCm[p - 1]);
                    long to = DistanceSqPoint(xCm, yCm, road.XCm[p], road.YCm[p]);
                    if (from <= to)
                    {
                        nodeId = road.FromNodeId;
                        nodeXCm = road.XCm[p - 1];
                        nodeYCm = road.YCm[p - 1];
                    }
                    else
                    {
                        nodeId = road.ToNodeId;
                        nodeXCm = road.XCm[p];
                        nodeYCm = road.YCm[p];
                    }
                }
            }

            return hit;
        }

        static long DistanceSq(int px, int py, int ax, int ay, int bx, int by)
        {
            long abx = (long)bx - ax;
            long aby = (long)by - ay;
            long apx = (long)px - ax;
            long apy = (long)py - ay;
            long len = (abx * abx) + (aby * aby);
            if (len <= 0)
            {
                return DistanceSqPoint(px, py, ax, ay);
            }

            double t = ((double)apx * abx + (double)apy * aby) / len;
            if (t < 0.0)
            {
                t = 0.0;
            }
            else if (t > 1.0)
            {
                t = 1.0;
            }

            double qx = ax + (t * abx);
            double qy = ay + (t * aby);
            double dx = px - qx;
            double dy = py - qy;
            return (long)((dx * dx) + (dy * dy));
        }

        static long DistanceSqPoint(int px, int py, int x, int y)
        {
            long dx = (long)px - x;
            long dy = (long)py - y;
            return (dx * dx) + (dy * dy);
        }
    }

    public static class SeatLabel
    {
        public static string FromKey(string factionKey)
        {
            if (string.IsNullOrEmpty(factionKey))
            {
                return string.Empty;
            }

            string[] parts = factionKey.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0)
                {
                    continue;
                }

                parts[i] = char.ToUpperInvariant(part[0]) + part.Substring(1);
            }

            return string.Join(" ", parts);
        }
    }
}
