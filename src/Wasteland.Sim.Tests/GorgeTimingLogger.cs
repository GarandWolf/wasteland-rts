using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wasteland.Sim.Ai;
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
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(AiVsAiGorgeTests.MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());

            int remnantId = AiConstants.DefaultAiFactionA;
            int compactId = AiConstants.DefaultAiFactionB;
            int dallesIndex = host.Ai.Map.IndexOfTown(host.Ai.Map.TheDallesTownId);
            AiTownRecord dalles = host.Ai.Map.TownAt(dallesIndex);
            int radius = host.Ai.Balance.CaptureRadiusCmForClass(dalles.PopulationClass);
            long radiusSq = (long)radius * radius;

            List<TimedEvent> rows = new List<TimedEvent>();
            Dictionary<string, int> spawnCounts = new Dictionary<string, int>();
            int eventCursor = 0;
            bool remnantArrived = false;
            bool compactArrived = false;
            bool remnantTier2 = false;
            bool compactTier2 = false;
            int lastCaptureFaction = host.Ai.Owned.CaptureFactionAt(dallesIndex);
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

                eventCursor = AppendSpawns(host, eventCursor, remnantId, compactId, spawnCounts, rows);
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

                int captureFaction = host.Ai.Owned.CaptureFactionAt(dallesIndex);
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

                if (!remnantTier2 && SeatTier(host, remnantId) >= 2)
                {
                    remnantTier2 = true;
                    log.RemnantTier2Tick = tick;
                    rows.Add(new TimedEvent(tick, 30, "remnant_tier2"));
                }

                if (!compactTier2 && SeatTier(host, compactId) >= 2)
                {
                    compactTier2 = true;
                    log.CompactTier2Tick = tick;
                    rows.Add(new TimedEvent(tick, 31, "compact_tier2"));
                }
            }

            log.RemnantRifleSpawns = CountPrefix(spawnCounts, remnantId + ":remnant_motor_rifles");
            log.CompactRifleSpawns = CountPrefix(spawnCounts, compactId + ":compact_rifle_battalion");
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

            log.Jsonl = text.ToString();
            log.Path = Path.Combine(AppContext.BaseDirectory, FileName);
            File.WriteAllText(log.Path, log.Jsonl);
            return log;
        }

        private static int AppendSpawns(
            AiMatchHost host,
            int cursor,
            int remnantId,
            int compactId,
            Dictionary<string, int> spawnCounts,
            List<TimedEvent> rows)
        {
            IReadOnlyList<SimEvent> events = host.World.Events;
            for (int i = cursor; i < events.Count; i++)
            {
                SimEvent ev = events[i];
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

        private static int SeatTier(AiMatchHost host, int factionId)
        {
            if (!host.World.TryGetSeat(factionId, out SeatView view))
            {
                return 1;
            }

            return view.CurrentTier;
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
    }
}
