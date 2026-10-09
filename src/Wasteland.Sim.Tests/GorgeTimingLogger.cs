using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Tests
{
    /// <summary>
    /// Test-only observer for the 10,000-tick Gorge two-AI match.
    /// Reads sim events and live state; does not add hashed or saved state.
    /// </summary>
    internal static class GorgeTimingLogger
    {
        public const string FileName = "gorge_two_ai_timings.jsonl";

        public static GorgeTimingLog Run()
        {
            return Run(AiBalanceTablesTests.LoadBalance());
        }

        public static GorgeTimingLog Run(AiBalanceTables balance)
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(AiVsAiGorgeTests.MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                balance);

            int remnantId = AiConstants.DefaultAiFactionA;
            int compactId = AiConstants.DefaultAiFactionB;
            int dallesIndex = host.Ai.Map.IndexOfTown(host.Ai.Map.TheDallesTownId);
            AiTownRecord dalles = host.Ai.Map.TownAt(dallesIndex);
            if (!host.World.TryGetTown(dalles.Id, out TownView dallesView))
            {
                throw new InvalidOperationException("The Dalles town view is missing.");
            }

            long radiusSq = (long)dallesView.ZoneRadiusCm * dallesView.ZoneRadiusCm;
            if (!host.World.TryGetTown(host.Ai.Map.WhiteSalmonTownId, out TownView whiteSalmon))
            {
                throw new InvalidOperationException("White Salmon town view is missing.");
            }

            if (!host.Ai.Map.TryGetTown(host.Ai.Map.HoodRiverTownId, out AiTownRecord hood))
            {
                throw new InvalidOperationException("Hood River map town is missing.");
            }

            List<TimedEvent> rows = new List<TimedEvent>();
            Dictionary<string, int> spawnCounts = new Dictionary<string, int>();
            int eventCursor = 0;
            bool remnantArrived = false;
            bool compactArrived = false;
            bool remnantLeft = false;
            bool compactLeft = false;
            long remnantSpawnDistSq = MinDistSqTo(host, remnantId, dalles.XCm, dalles.YCm);
            long compactSpawnDistSq = MinDistSqTo(host, compactId, dalles.XCm, dalles.YCm);
            int lastCaptureFaction = CaptureFaction(host, host.Ai.Map.TheDallesTownId);
            int lastOwner = host.Ai.Owned.TownOwnerAt(dallesIndex);

            GorgeTimingLog log = new GorgeTimingLog();
            for (int i = 0; i < AiConstants.M1DeterminismTicks; i++)
            {
                host.Step();
                int tick = host.Tick - 1;
                if (tick < 0)
                {
                    tick = 0;
                }

                eventCursor = AppendEvents(host, eventCursor, remnantId, compactId, spawnCounts, rows, log);
                if (!compactLeft && MinDistSqTo(host, compactId, dalles.XCm, dalles.YCm) < compactSpawnDistSq)
                {
                    compactLeft = true;
                    log.CompactLeaveTick = tick;
                    rows.Add(new TimedEvent(tick, 48, "compact_dalles_leave"));
                }

                if (!remnantLeft && MinDistSqTo(host, remnantId, dalles.XCm, dalles.YCm) < remnantSpawnDistSq)
                {
                    remnantLeft = true;
                    log.RemnantLeaveTick = tick;
                    rows.Add(new TimedEvent(tick, 49, "remnant_dalles_leave"));
                }

                if (!compactArrived && FactionInDalles(host, compactId, dalles, radiusSq))
                {
                    compactArrived = true;
                    log.CompactArriveTick = tick;
                    rows.Add(new TimedEvent(tick, 50, "compact_dalles_arrive"));
                }

                if (!remnantArrived && FactionInDalles(host, remnantId, dalles, radiusSq))
                {
                    remnantArrived = true;
                    log.RemnantArriveTick = tick;
                    rows.Add(new TimedEvent(tick, 51, "remnant_dalles_arrive"));
                }

                int captureFaction = CaptureFaction(host, host.Ai.Map.TheDallesTownId);
                if (lastCaptureFaction == 0 && captureFaction != 0)
                {
                    if (captureFaction == compactId)
                    {
                        log.CompactCaptureStartTick = tick;
                        rows.Add(new TimedEvent(tick, 10, "compact_dalles_capture_start"));
                    }
                    else if (captureFaction == remnantId)
                    {
                        log.RemnantCaptureStartTick = tick;
                        rows.Add(new TimedEvent(tick, 11, "remnant_dalles_capture_start"));
                    }
                }

                lastCaptureFaction = captureFaction;

                int owner = host.Ai.Owned.TownOwnerAt(dallesIndex);
                if (lastOwner != owner && owner != 0)
                {
                    if (owner == compactId)
                    {
                        log.CompactCaptureTick = tick;
                        rows.Add(new TimedEvent(tick, 20, "compact_dalles_capture"));
                    }
                    else if (owner == remnantId)
                    {
                        log.RemnantCaptureTick = tick;
                        rows.Add(new TimedEvent(tick, 21, "remnant_dalles_capture_commit"));
                    }
                }

                lastOwner = owner;
                if (host.World.TryGetTown(host.Ai.Map.WhiteSalmonTownId, out whiteSalmon))
                {
                    SampleWhiteSalmon(host, compactId, whiteSalmon, hood, tick, log);
                }
            }

            log.RemnantRifleSpawns = CountPrefix(spawnCounts, remnantId + ":remnant_motor_rifles");
            log.CompactRifleSpawns = CountPrefix(spawnCounts, compactId + ":compact_rifle_battalion");
            CollectCommandStats(host, log);
            host.Shutdown();

            rows.Sort(CompareEvents);
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < rows.Count; i++)
            {
                TimedEvent row = rows[i];
                text.Append("{\"tick\": ");
                text.Append(row.Tick);
                text.Append(", \"event\": \"");
                text.Append(row.EventId);
                text.Append("\"}\n");
            }

            AppendCommandStats(text, log);
            log.Jsonl = text.ToString();
            log.Path = Path.Combine(AppContext.BaseDirectory, FileName);
            File.WriteAllText(log.Path, log.Jsonl);
            return log;
        }

        private static void CollectCommandStats(AiMatchHost host, GorgeTimingLog log)
        {
            IReadOnlyList<SimCommand> commands = host.CommandLog;
            for (int i = 0; i < commands.Count; i++)
            {
                SimCommand command = commands[i];
                if (command.Kind == CommandKind.BuildDepot)
                {
                    AddCount(log.DepotOrders, command.IssuerId, command.Arg0);
                }

                if (command.Kind == CommandKind.Move)
                {
                    AddMove(log.Moves, command.BattalionId);
                }
            }

            IReadOnlyList<SimEvent> events = host.World.Events;
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind != SimEventKind.CommandRejected)
                {
                    continue;
                }

                CommandRejectReason reason = ev.RejectReason;
                if (reason != CommandRejectReason.UnknownNode
                    && reason != CommandRejectReason.TerritoryNotOwned
                    && reason != CommandRejectReason.NotConnectedToSupply
                    && reason != CommandRejectReason.DepotTooClose
                    && reason != CommandRejectReason.NodeHasDepot)
                {
                    continue;
                }

                if (ev.Param0 == 0)
                {
                    continue;
                }

                AddCount(log.DepotRejects, (int)(ev.Key >> 32), ev.Param0);
            }
        }

        private static void AddCount(List<NodeCount> rows, int seatId, int nodeId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].SeatId == seatId && rows[i].NodeId == nodeId)
                {
                    NodeCount have = rows[i];
                    have.Count++;
                    rows[i] = have;
                    return;
                }
            }

            NodeCount next = default;
            next.SeatId = seatId;
            next.NodeId = nodeId;
            next.Count = 1;
            rows.Add(next);
        }

        private static void AddMove(List<BattalionMoveCount> rows, int battalionId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].BattalionId == battalionId)
                {
                    BattalionMoveCount have = rows[i];
                    have.Count++;
                    rows[i] = have;
                    return;
                }
            }

            BattalionMoveCount next = default;
            next.BattalionId = battalionId;
            next.Count = 1;
            rows.Add(next);
        }

        private static void AppendCommandStats(StringBuilder text, GorgeTimingLog log)
        {
            for (int i = 0; i < log.DepotOrders.Count; i++)
            {
                NodeCount row = log.DepotOrders[i];
                text.Append("{\"event\":\"depot_order\",\"seat\":");
                text.Append(row.SeatId);
                text.Append(",\"node\":");
                text.Append(row.NodeId);
                text.Append(",\"count\":");
                text.Append(row.Count);
                text.Append("}\n");
            }

            for (int i = 0; i < log.DepotRejects.Count; i++)
            {
                NodeCount row = log.DepotRejects[i];
                text.Append("{\"event\":\"depot_reject\",\"seat\":");
                text.Append(row.SeatId);
                text.Append(",\"node\":");
                text.Append(row.NodeId);
                text.Append(",\"count\":");
                text.Append(row.Count);
                text.Append("}\n");
            }

            int maxMoves = 0;
            int maxBattalion = 0;
            for (int i = 0; i < log.Moves.Count; i++)
            {
                if (log.Moves[i].Count > maxMoves)
                {
                    maxMoves = log.Moves[i].Count;
                    maxBattalion = log.Moves[i].BattalionId;
                }
            }

            text.Append("{\"event\":\"max_moves\",\"battalion\":");
            text.Append(maxBattalion);
            text.Append(",\"count\":");
            text.Append(maxMoves);
            text.Append("}\n");
        }

        private static int AppendEvents(
            AiMatchHost host,
            int cursor,
            int remnantId,
            int compactId,
            Dictionary<string, int> spawnCounts,
            List<TimedEvent> rows,
            GorgeTimingLog log)
        {
            IReadOnlyList<SimEvent> events = host.World.Events;
            for (int i = cursor; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind == SimEventKind.TierReached && ev.AuxId == 2)
                {
                    if (ev.SubjectId == remnantId && log.RemnantTier2Tick < 0)
                    {
                        log.RemnantTier2Tick = ev.Tick;
                        rows.Add(new TimedEvent(ev.Tick, 30, "remnant_tier2"));
                    }
                    else if (ev.SubjectId == compactId && log.CompactTier2Tick < 0)
                    {
                        log.CompactTier2Tick = ev.Tick;
                        rows.Add(new TimedEvent(ev.Tick, 31, "compact_tier2"));
                    }

                    continue;
                }

                if (ev.Kind != SimEventKind.BattalionSpawned)
                {
                    continue;
                }

                if (!host.World.TryGetBattalionView(ev.SubjectId, out BattalionView view))
                {
                    continue;
                }

                string key = view.OwnerId + ":" + view.UnitId;
                int n = 1;
                if (spawnCounts.TryGetValue(key, out int have))
                {
                    n = have + 1;
                }

                spawnCounts[key] = n;
                string eventId = SpawnEventId(view.OwnerId, view.UnitId, n, remnantId, compactId);
                rows.Add(new TimedEvent(ev.Tick, 35 + n, eventId));
            }

            return events.Count;
        }

        private static string SpawnEventId(
            int ownerId,
            string unitId,
            int n,
            int remnantId,
            int compactId)
        {
            if (ownerId == remnantId)
            {
                if (unitId == "remnant_armoured_battalion")
                {
                    return "remnant_armour_" + n;
                }

                if (unitId == "remnant_mech_battalion")
                {
                    return "remnant_mech_" + n;
                }
            }

            if (ownerId == compactId)
            {
                if (unitId == "compact_at_battalion")
                {
                    return "compact_at_" + n;
                }

                if (unitId == "compact_militia")
                {
                    return "compact_militia";
                }
            }

            return unitId;
        }

        private static void SampleWhiteSalmon(
            AiMatchHost host,
            int compactId,
            TownView whiteSalmon,
            AiTownRecord hood,
            int tick,
            GorgeTimingLog log)
        {
            AiFactionState compact = host.Ai.Owned.GetFaction(compactId);
            bool contesting = compact.AttackTargetTownId == whiteSalmon.TownId
                || compact.DefendTownId == whiteSalmon.TownId;
            long radius = whiteSalmon.ZoneRadiusCm;
            long radiusSq = radius * radius;
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != compactId)
                {
                    continue;
                }

                long dx = (long)battalion.PositionXCm - whiteSalmon.XCm;
                long dy = (long)battalion.PositionYCm - whiteSalmon.YCm;
                if ((dx * dx) + (dy * dy) > radiusSq)
                {
                    continue;
                }

                long hoodDx = (long)battalion.PositionXCm - hood.XCm;
                long hoodDy = (long)battalion.PositionYCm - hood.YCm;
                int distHood = ApproxDistCm(battalion.PositionXCm, battalion.PositionYCm, hood.XCm, hood.YCm);
                string unitId = string.Empty;
                if (host.World.TryGetBattalionView(battalion.Id, out BattalionView view))
                {
                    unitId = view.UnitId;
                }

                bool engineer = host.Ai.Balance.UnitCatalog.TryGetRole(unitId, out string role)
                    && role == AiConstants.RoleEngineer;
                log.WhiteSalmonSamples.Add(new WhiteSalmonSample(
                    tick,
                    battalion.Id,
                    unitId,
                    engineer,
                    contesting,
                    battalion.PositionXCm,
                    battalion.PositionYCm,
                    distHood,
                    (int)global::System.Math.Sqrt((double)((hoodDx * hoodDx) + (hoodDy * hoodDy)))));
            }
        }

        private static int ApproxDistCm(int ax, int ay, int bx, int by)
        {
            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;
            int max = dx > dy ? dx : dy;
            int min = dx > dy ? dy : dx;
            return max + (min / 2);
        }

        private static int CaptureFaction(AiMatchHost host, int townId)
        {
            if (host.World.TryGetTown(townId, out TownView town))
            {
                return town.CaptureFactionId;
            }

            return 0;
        }

        private static long MinDistSqTo(AiMatchHost host, int factionId, int xCm, int yCm)
        {
            long best = long.MaxValue;
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != factionId)
                {
                    continue;
                }

                long dx = (long)battalion.PositionXCm - xCm;
                long dy = (long)battalion.PositionYCm - yCm;
                long distSq = (dx * dx) + (dy * dy);
                if (distSq < best)
                {
                    best = distSq;
                }
            }

            return best;
        }

        private static bool FactionInDalles(
            AiMatchHost host,
            int factionId,
            AiTownRecord dalles,
            long radiusSq)
        {
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != factionId)
                {
                    continue;
                }

                long dx = (long)battalion.PositionXCm - dalles.XCm;
                long dy = (long)battalion.PositionYCm - dalles.YCm;
                if ((dx * dx) + (dy * dy) <= radiusSq)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountPrefix(Dictionary<string, int> spawnCounts, string key)
        {
            int n;
            if (spawnCounts.TryGetValue(key, out n))
            {
                return n;
            }

            return 0;
        }

        private static int CompareEvents(TimedEvent a, TimedEvent b)
        {
            int c = a.Tick.CompareTo(b.Tick);
            if (c != 0)
            {
                return c;
            }

            return a.Order.CompareTo(b.Order);
        }

        private readonly struct TimedEvent
        {
            public readonly int Tick;
            public readonly int Order;
            public readonly string EventId;

            public TimedEvent(int tick, int order, string eventId)
            {
                Tick = tick;
                Order = order;
                EventId = eventId;
            }
        }
    }

    internal sealed class GorgeTimingLog
    {
        public string Path = string.Empty;
        public string Jsonl = string.Empty;
        public int CompactLeaveTick = -1;
        public int RemnantLeaveTick = -1;
        public int CompactArriveTick = -1;
        public int RemnantArriveTick = -1;
        public int CompactCaptureStartTick = -1;
        public int RemnantCaptureStartTick = -1;
        public int CompactCaptureTick = -1;
        public int RemnantCaptureTick = -1;
        public int RemnantTier2Tick = -1;
        public int CompactTier2Tick = -1;
        public int RemnantRifleSpawns;
        public int CompactRifleSpawns;
        public readonly List<WhiteSalmonSample> WhiteSalmonSamples = new List<WhiteSalmonSample>();
        public readonly List<NodeCount> DepotOrders = new List<NodeCount>();
        public readonly List<NodeCount> DepotRejects = new List<NodeCount>();
        public readonly List<BattalionMoveCount> Moves = new List<BattalionMoveCount>();
    }

    internal struct NodeCount
    {
        public int SeatId;
        public int NodeId;
        public int Count;
    }

    internal struct BattalionMoveCount
    {
        public int BattalionId;
        public int Count;
    }

    internal readonly struct WhiteSalmonSample
    {
        public readonly int Tick;
        public readonly int BattalionId;
        public readonly string UnitId;
        public readonly bool Engineer;
        public readonly bool Contesting;
        public readonly int XCm;
        public readonly int YCm;
        public readonly int DistHoodApproxCm;
        public readonly int DistHoodCm;

        public WhiteSalmonSample(
            int tick,
            int battalionId,
            string unitId,
            bool engineer,
            bool contesting,
            int xCm,
            int yCm,
            int distHoodApproxCm,
            int distHoodCm)
        {
            Tick = tick;
            BattalionId = battalionId;
            UnitId = unitId ?? string.Empty;
            Engineer = engineer;
            Contesting = contesting;
            XCm = xCm;
            YCm = yCm;
            DistHoodApproxCm = distHoodApproxCm;
            DistHoodCm = distHoodCm;
        }
    }
}
