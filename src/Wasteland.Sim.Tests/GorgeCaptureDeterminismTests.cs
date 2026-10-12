using System;
using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    /// <summary>
    /// Two scripted Gorge runs from one seed, plus a mid-run save.
    /// The save is the host command log and AI blob. ReplayTo rebuilds the world
    /// by stepping those commands, which is how a match resumes.
    /// </summary>
    public sealed class GorgeCaptureDeterminismTests
    {
        private const int Remnant = AiConstants.DefaultAiFactionA;
        private const int Compact = AiConstants.DefaultAiFactionB;
        private const int MaxTicks = 45000;

        [Fact]
        public void ScriptedCapture_MatchesAcrossRuns_AndSaveResume()
        {
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { Remnant, Compact };
            MatchConfig config = new MatchConfig(0xC0FFEEUL, 0);
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();

            Run first = Drive(config, map, factions, tables);
            Run second = Drive(config, map, factions, tables);
            Assert.Equal(first.Hashes.Count, second.Hashes.Count);
            for (int i = 0; i < first.Hashes.Count; i++)
            {
                Assert.Equal(first.Hashes[i], second.Hashes[i]);
            }

            AssertSameEvents(first.Events, second.Events);
            Assert.True(first.SawContest);
            Assert.True(first.SawCapture);
            Assert.True(first.SawDepotLoss);
            Assert.True(first.SaveTick > 0);

            AiMatchHost resumed = AiMatchHost.ReplayTo(
                config,
                map,
                factions,
                AiConstants.DifficultyNormal,
                tables,
                first.Log,
                first.Blob,
                first.SaveTick);
            Assert.Equal(first.Hashes[first.SaveTick], resumed.World.HashState());
            AssertSameEvents(EventsUntil(first.Events, resumed.World.Tick), CopyEvents(resumed.World));

            int cursor = 0;
            while (cursor < first.Log.Length && first.Log[cursor].IssuedTick < resumed.World.Tick)
            {
                cursor++;
            }

            while (resumed.World.Tick < first.FinalTick)
            {
                while (cursor < first.Log.Length && first.Log[cursor].IssuedTick == resumed.World.Tick)
                {
                    resumed.World.Submit(first.Log[cursor]);
                    cursor++;
                }

                resumed.World.Step();
                Assert.Equal(first.Hashes[resumed.World.Tick], resumed.World.HashState());
            }

            AssertSameEvents(first.Events, CopyEvents(resumed.World));
            resumed.Shutdown();
        }

        private static Run Drive(
            MatchConfig config,
            byte[] map,
            int[] factions,
            AiBalanceTables tables)
        {
            AiMatchHost host = AiMatchHost.Start(
                config,
                map,
                factions,
                AiConstants.DifficultyNormal,
                tables);
            BalanceCatalog? maybeCatalog = tables.ScenarioCatalog;
            Assert.NotNull(maybeCatalog);
            BalanceCatalog catalog = maybeCatalog;
            int remnant = FastCapturer(host.World, catalog, Remnant);
            int compact = FastCapturer(host.World, catalog, Compact);
            int remnantEngineer = Engineer(host.World, catalog, Remnant);
            int compactEngineer = Engineer(host.World, catalog, Compact);
            int dallesId = host.Ai.Map.TheDallesTownId;
            Assert.True(host.World.TryGetTown(dallesId, out TownView dalles));
            Assert.Equal(0, dalles.OwnerFactionId);
            Assert.True(host.World.TryGetSeat(Remnant, out SeatView remnantSeat));
            Assert.True(host.World.TryGetSeat(Compact, out SeatView compactSeat));

            Run run = new Run();
            run.Hashes.Add(host.World.HashState());
            Phase phase = Phase.Claim;
            int meter = 0;
            int depotNode = 0;
            bool contestedNoted = false;

            for (int n = 0; n < MaxTicks && phase != Phase.Done; n++)
            {
                TownView town = Town(host.World, dallesId);
                if (phase == Phase.Claim)
                {
                    for (int i = 0; i < host.World.BattalionCount; i++)
                    {
                        SimBattalion battalion = host.World.Battalions[i];
                        if (battalion.OwnerId != Remnant || battalion.UnitIndex < 0)
                        {
                            continue;
                        }

                        if (!catalog.UnitCanCaptureAt(battalion.UnitIndex) || catalog.UnitCanBuildAt(battalion.UnitIndex))
                        {
                            continue;
                        }

                        Submit(host, SimCommand.ClaimTown(host.Tick, Remnant, battalion.Id, dallesId));
                    }

                    phase = Phase.WaitGarrison;
                }
                else if (phase == Phase.WaitGarrison)
                {
                    if (!NeutralAlive(host.World) && town.CaptureFactionId == Remnant)
                    {
                        meter = Remnant;
                        phase = Phase.WaitOwner;
                    }
                }
                else if (phase == Phase.WaitContest)
                {
                    if (town.Contested && town.CaptureFactionId != 0)
                    {
                        phase = Phase.PullIntruder;
                    }
                }
                else if (phase == Phase.PullIntruder)
                {
                    for (int i = 0; i < host.World.BattalionCount; i++)
                    {
                        SimBattalion battalion = host.World.Battalions[i];
                        if (battalion.OwnerId != Remnant || battalion.UnitIndex < 0)
                        {
                            continue;
                        }

                        if (!catalog.UnitCanContestAt(battalion.UnitIndex))
                        {
                            continue;
                        }

                        Submit(host, SimCommand.Move(host.Tick, Remnant, battalion.Id, remnantSeat.HqXCm, remnantSeat.HqYCm));
                    }

                    phase = Phase.WaitDepotLoss;
                }
                else if (phase == Phase.WaitOwner)
                {
                    if (town.OwnerFactionId == meter && meter != 0)
                    {
                        phase = Phase.BuildDepot;
                    }
                }
                else if (phase == Phase.BuildDepot)
                {
                    int engineer = meter == Remnant ? remnantEngineer : compactEngineer;
                    depotNode = LegalNode(host, meter, town.TerritoryId);
                    Submit(host, SimCommand.BuildDepot(host.Tick, meter, engineer, depotNode));
                    phase = Phase.ClearDefender;
                }
                else if (phase == Phase.ClearDefender)
                {
                    if (DepotCount(host.World) > 0)
                    {
                        for (int i = 0; i < host.World.BattalionCount; i++)
                        {
                            SimBattalion battalion = host.World.Battalions[i];
                            if (battalion.OwnerId != Remnant || battalion.Hp <= 0 || battalion.UnitIndex < 0)
                            {
                                continue;
                            }

                            if (!catalog.UnitCanContestAt(battalion.UnitIndex))
                            {
                                continue;
                            }

                            Submit(host, SimCommand.Move(host.Tick, Remnant, battalion.Id, remnantSeat.HqXCm, remnantSeat.HqYCm));
                        }

                        phase = Phase.WaitDefenderLeft;
                    }
                }
                else if (phase == Phase.WaitDefenderLeft)
                {
                    if (!FactionInZone(host.World, catalog, Remnant, town))
                    {
                        for (int i = 0; i < host.World.BattalionCount; i++)
                        {
                            SimBattalion battalion = host.World.Battalions[i];
                            if (battalion.OwnerId != Compact || battalion.Hp <= 0 || battalion.UnitIndex < 0)
                            {
                                continue;
                            }

                            if (!catalog.UnitCanCaptureAt(battalion.UnitIndex) || catalog.UnitCanBuildAt(battalion.UnitIndex))
                            {
                                continue;
                            }

                            Submit(host, SimCommand.ClaimTown(host.Tick, Compact, battalion.Id, dallesId));
                        }

                        phase = Phase.WaitCompactMeter;
                    }
                }
                else if (phase == Phase.WaitCompactMeter)
                {
                    if (town.CaptureFactionId == Compact)
                    {
                        Submit(host, SimCommand.Move(host.Tick, Remnant, remnant, town.XCm, town.YCm));
                        phase = Phase.WaitContest;
                    }
                }
                else if (phase == Phase.WaitDepotLoss)
                {
                    int attacker = meter == Remnant ? Compact : Remnant;
                    if (town.OwnerFactionId == attacker && DepotCount(host.World) == 0)
                    {
                        phase = Phase.Done;
                    }
                }

                if (phase == Phase.Done)
                {
                    break;
                }

                host.World.Step();
                run.Hashes.Add(host.World.HashState());
                if (!contestedNoted && Town(host.World, dallesId).Contested)
                {
                    contestedNoted = true;
                    run.SaveTick = host.World.Tick;
                    run.Blob = host.Ai.Save();
                    run.SawContest = true;
                }

                if (Town(host.World, dallesId).OwnerFactionId != 0)
                {
                    run.SawCapture = true;
                }
            }

            Assert.True(phase == Phase.Done, "script stopped in " + phase + " at tick " + host.World.Tick);
            run.SawDepotLoss = Saw(host.World, SimEventKind.DepotDestroyed);
            Assert.True(run.SawDepotLoss);
            run.FinalTick = host.World.Tick;
            run.Events = CopyEvents(host.World);
            run.Log = new SimCommand[host.CommandLog.Count];
            for (int i = 0; i < run.Log.Length; i++)
            {
                run.Log[i] = host.CommandLog[i];
            }

            host.Shutdown();
            return run;
        }

        private static void Submit(AiMatchHost host, SimCommand command)
        {
            host.Ai.Commands.Submit(command);
        }

        private static int LegalNode(AiMatchHost host, int factionId, int territoryId)
        {
            RoadGraph roads = host.Ai.Map.Roads;
            for (int i = 0; i < roads.NodeCount; i++)
            {
                if (roads.GetNodeTerritoryId(i) != territoryId)
                {
                    continue;
                }

                int nodeId = roads.GetNodeId(i);
                if (host.World.PreviewDepotPlacement(factionId, nodeId, out _, out _) == CommandRejectReason.None)
                {
                    return nodeId;
                }
            }

            throw new InvalidOperationException("No legal depot node in territory " + territoryId + ".");
        }

        private static int FastCapturer(SimWorld world, BalanceCatalog catalog, int factionId)
        {
            int best = 0;
            int bestSpeed = -1;
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != factionId || battalion.UnitIndex < 0)
                {
                    continue;
                }

                if (!catalog.UnitCanCaptureAt(battalion.UnitIndex) || catalog.UnitCanBuildAt(battalion.UnitIndex))
                {
                    continue;
                }

                if (battalion.SpeedCmPerSecond > bestSpeed
                    || (battalion.SpeedCmPerSecond == bestSpeed && (best == 0 || battalion.Id < best)))
                {
                    best = battalion.Id;
                    bestSpeed = battalion.SpeedCmPerSecond;
                }
            }

            if (best == 0)
            {
                throw new InvalidOperationException("No capturer for faction " + factionId + ".");
            }

            return best;
        }

        private static int Engineer(SimWorld world, BalanceCatalog catalog, int factionId)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == factionId
                    && battalion.UnitIndex >= 0
                    && catalog.UnitCanBuildAt(battalion.UnitIndex))
                {
                    return battalion.Id;
                }
            }

            throw new InvalidOperationException("No engineer for faction " + factionId + ".");
        }

        private static bool FactionInZone(SimWorld world, BalanceCatalog catalog, int factionId, TownView town)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != factionId || battalion.Hp <= 0 || battalion.UnitIndex < 0)
                {
                    continue;
                }

                if (!catalog.UnitCanContestAt(battalion.UnitIndex))
                {
                    continue;
                }

                if (InsideZone(world, battalion.Id, town))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NeutralAlive(SimWorld world)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == SimWorld.NeutralOwnerId && battalion.Hp > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InsideZone(SimWorld world, int battalionId, TownView town)
        {
            if (!world.TryGetBattalionView(battalionId, out BattalionView view))
            {
                return false;
            }

            long dx = (long)view.XCm - town.XCm;
            long dy = (long)view.YCm - town.YCm;
            long radius = town.ZoneRadiusCm;
            return (dx * dx) + (dy * dy) <= radius * radius;
        }

        private static TownView Town(SimWorld world, int townId)
        {
            Assert.True(world.TryGetTown(townId, out TownView town));
            return town;
        }

        private static int DepotCount(SimWorld world)
        {
            List<DepotView> depots = new List<DepotView>();
            world.CopyDepots(depots);
            return depots.Count;
        }

        private static bool Saw(SimWorld world, SimEventKind kind)
        {
            for (int i = 0; i < world.Events.Count; i++)
            {
                if (world.Events[i].Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<SimEvent> CopyEvents(SimWorld world)
        {
            List<SimEvent> copy = new List<SimEvent>(world.Events.Count);
            for (int i = 0; i < world.Events.Count; i++)
            {
                copy.Add(world.Events[i]);
            }

            return copy;
        }

        private static List<SimEvent> EventsUntil(List<SimEvent> events, int tick)
        {
            List<SimEvent> copy = new List<SimEvent>();
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Tick < tick)
                {
                    copy.Add(events[i]);
                }
            }

            return copy;
        }

        private static void AssertSameEvents(List<SimEvent> left, List<SimEvent> right)
        {
            Assert.Equal(left.Count, right.Count);
            for (int i = 0; i < left.Count; i++)
            {
                Assert.Equal(left[i].EventId, right[i].EventId);
                Assert.Equal(left[i].Tick, right[i].Tick);
                Assert.Equal(left[i].Kind, right[i].Kind);
                Assert.Equal(left[i].SubjectId, right[i].SubjectId);
                Assert.Equal(left[i].AuxId, right[i].AuxId);
                Assert.Equal(left[i].Param0, right[i].Param0);
                Assert.Equal(left[i].Param1, right[i].Param1);
                Assert.Equal(left[i].Key, right[i].Key);
            }
        }

        private enum Phase
        {
            Claim,
            WaitGarrison,
            WaitContest,
            PullIntruder,
            WaitOwner,
            BuildDepot,
            ClearDefender,
            WaitDefenderLeft,
            WaitCompactMeter,
            WaitDepotLoss,
            Done
        }

        private sealed class Run
        {
            public readonly List<ulong> Hashes = new List<ulong>();
            public List<SimEvent> Events = new List<SimEvent>();
            public SimCommand[] Log = Array.Empty<SimCommand>();
            public byte[] Blob = Array.Empty<byte>();
            public int SaveTick;
            public int FinalTick;
            public bool SawContest;
            public bool SawCapture;
            public bool SawDepotLoss;
        }
    }
}
