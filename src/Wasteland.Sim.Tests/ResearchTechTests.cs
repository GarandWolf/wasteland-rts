using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    /// <summary>
    /// Player-issued Tier II research (CommandKind.ResearchTech = 8) on the Gorge MVP scenario.
    /// </summary>
    public sealed class ResearchTechTests
    {
        private const int Remnant = AiConstants.DefaultAiFactionA;
        private const int Compact = AiConstants.DefaultAiFactionB;
        private const string TierTwo = "tier2";
        private const string TowedArtillery = "compact_towed_artillery";

        [Fact]
        public void CommandAndRejectIds_MatchPostedSignature()
        {
            Assert.Equal(8, (int)CommandKind.ResearchTech);
            Assert.Equal(31, (int)CommandRejectReason.ResearchInProgress);
            Assert.Equal(32, (int)CommandRejectReason.TierAlreadyReached);
            Assert.Equal(33, (int)CommandRejectReason.NoQualifyingTier);
            Assert.Equal(5, (int)SimEventKind.TierResearchStarted);
            Assert.Equal(6, (int)SimEventKind.TierReached);
        }

        [Fact]
        public void Research_CompletesExactly1800TicksAfterTheCommand_AndChargesOnExecute()
        {
            SimWorld world = World(fuelEnabled: true);
            SeatView before = Seat(world, Compact);
            int fuelBefore = world.FuelOf(Compact);
            int issuedAt = world.Tick;
            world.SubmitResearchTech(Compact, TierTwo);
            world.Step();

            SimEvent started = Last(world, SimEventKind.TierResearchStarted, Compact);
            Assert.Equal(issuedAt, started.Tick);
            Assert.Equal(2, started.AuxId);
            Assert.Equal(issuedAt, started.Param0);
            Assert.Equal(issuedAt + 1800, started.Param1);
            SeatView running = Seat(world, Compact);
            Assert.Equal(before.Materials - 300, running.Materials);
            Assert.Equal(fuelBefore - 200, world.FuelOf(Compact));
            Assert.Equal(issuedAt + 1800, running.TierResearchCompleteTick);

            while (world.Tick < issuedAt + 1800)
            {
                world.Step();
            }

            Assert.Equal(1, Seat(world, Compact).CurrentTier);
            world.Step();
            SimEvent reached = Last(world, SimEventKind.TierReached, Compact);
            Assert.Equal(issuedAt + 1800, reached.Tick);
            Assert.Equal(2, reached.AuxId);
            Assert.Equal(2, Seat(world, Compact).CurrentTier);
            Assert.Equal(-1, Seat(world, Compact).TierResearchCompleteTick);
        }

        [Fact]
        public void Reject_InProgress()
        {
            SimWorld world = World(fuelEnabled: true);
            world.SubmitResearchTech(Compact, TierTwo);
            world.Step();
            world.SubmitResearchTech(Compact, TierTwo);
            world.Step();
            Assert.Equal(CommandRejectReason.ResearchInProgress, LastReject(world, Compact));
            Assert.Equal(1, Count(world, SimEventKind.TierResearchStarted, Compact));
        }

        [Fact]
        public void Reject_TierAlreadyReached()
        {
            SimWorld world = World(fuelEnabled: true);
            world.SubmitResearchTech(Compact, TierTwo);
            for (int i = 0; i < 1802; i++)
            {
                world.Step();
            }

            Assert.Equal(2, Seat(world, Compact).CurrentTier);
            SeatView before = Seat(world, Compact);
            world.SubmitResearchTech(Compact, TierTwo);
            world.Step();
            Assert.Equal(CommandRejectReason.TierAlreadyReached, LastReject(world, Compact));
            Assert.True(Seat(world, Compact).Materials >= before.Materials);
        }

        [Fact]
        public void Reject_Unaffordable_ChargesNothing()
        {
            SimWorld world = World(fuelEnabled: true, materials: 299);
            int fuel = world.FuelOf(Compact);
            world.SubmitResearchTech(Compact, TierTwo);
            world.Step();
            Assert.Equal(CommandRejectReason.InsufficientMaterials, LastReject(world, Compact));
            Assert.Equal(-1, Seat(world, Compact).TierResearchCompleteTick);
            Assert.Equal(0, Count(world, SimEventKind.TierResearchStarted, Compact));
            Assert.Equal(299, Seat(world, Compact).Materials);
            Assert.Equal(fuel, world.FuelOf(Compact));
        }

        [Fact]
        public void Reject_NoQualifyingTier_WhenSeatOwnsNoTownOrHq()
        {
            SimWorld world = World(fuelEnabled: true);
            world.ReassignTownsForTests(Compact, 0);
            world.SubmitResearchTech(Compact, TierTwo);
            world.Step();
            Assert.Equal(CommandRejectReason.NoQualifyingTier, LastReject(world, Compact));
            Assert.Equal(0, Count(world, SimEventKind.TierResearchStarted, Compact));
        }

        [Fact]
        public void TowedArtillery_NotRaisableBeforeTierTwo_RaisableAfter()
        {
            SimWorld world = World(fuelEnabled: true);
            world.AddResources(Compact, 1000, 2000, 2000);
            SeatView seat = Seat(world, Compact);
            world.SubmitRaiseBattalion(Compact, 9001, TowedArtillery, seat.HqXCm, seat.HqYCm);
            world.SubmitResearchTech(Compact, TierTwo);
            world.Step();
            Assert.True(world.TryGetRejection(9001, out CommandRejectReason early, out int need, out int have));
            Assert.Equal(CommandRejectReason.TierLocked, early);
            Assert.Equal(2, need);
            Assert.Equal(1, have);

            while (Seat(world, Compact).CurrentTier < 2)
            {
                world.Step();
            }

            world.SubmitRaiseBattalion(Compact, 9002, TowedArtillery, seat.HqXCm, seat.HqYCm);
            world.Step();
            Assert.False(world.TryGetRejection(9002, out _, out _, out _));
            Assert.True(Count(world, SimEventKind.BattalionRaiseStarted, 9002) == 1);
        }

        [Fact]
        public void MidResearchSaveLoad_GivesTheSameHash()
        {
            SimWorld a = World(fuelEnabled: true);
            SimWorld b = World(fuelEnabled: true);
            a.SubmitResearchTech(Compact, TierTwo);
            b.SubmitResearchTech(Compact, TierTwo);
            for (int i = 0; i < 900; i++)
            {
                a.Step();
                b.Step();
            }

            byte[] blob = a.Save();
            b.Load(blob);
            Assert.Equal(a.HashState(), b.HashState());
            Assert.Equal(blob, b.Save());
            for (int i = 0; i < 905; i++)
            {
                Assert.Equal(a.Step(), b.Step());
            }

            Assert.Equal(2, Seat(b, Compact).CurrentTier);
        }

        [Fact]
        public void LoadOverridesSeatResearchState()
        {
            SimWorld a = World(fuelEnabled: true);
            a.SubmitResearchTech(Compact, TierTwo);
            for (int i = 0; i < 10; i++)
            {
                a.Step();
            }

            SimWorld c = World(fuelEnabled: true);
            for (int i = 0; i < 10; i++)
            {
                c.Step();
            }

            c.Load(a.Save());
            Assert.Equal(Seat(a, Compact).TierResearchCompleteTick, Seat(c, Compact).TierResearchCompleteTick);
        }

        [Fact]
        public void HumanRemnantSeat_IgnoresAutoResearch_AndCanUseTheCommand()
        {
            MatchConfig match = MatchConfig.Default(AiVsAiGorgeTests.MatchSeed);
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            AiMatchHost host = AiMatchHost.Start(match, map, new[] { Compact }, AiConstants.DifficultyNormal, BalanceLoad.Tables());
            SimWorld world = host.World;
            while (world.Tick < 1300)
            {
                host.Step();
            }

            Assert.Equal(0, Count(world, SimEventKind.TierResearchStarted, Remnant));
            Assert.Equal(-1, Seat(world, Remnant).TierResearchCompleteTick);
            SimCommand queued = world.SubmitResearchTech(Remnant, TierTwo);
            while (world.Tick <= queued.ExecuteTick)
            {
                host.Step();
            }

            Assert.Equal(CommandRejectReason.None, LastReject(world, Remnant));
            Assert.Equal(queued.ExecuteTick, Last(world, SimEventKind.TierResearchStarted, Remnant).Tick);
            Assert.Equal(1, Count(world, SimEventKind.TierResearchStarted, Remnant));
        }

        [Fact]
        public void AiRemnantSeat_StillAutoResearchesAtTick1200()
        {
            MatchConfig match = MatchConfig.Default(AiVsAiGorgeTests.MatchSeed);
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            AiMatchHost host = AiMatchHost.Start(match, map, new[] { Remnant, Compact }, AiConstants.DifficultyNormal, BalanceLoad.Tables());
            SimWorld world = host.World;
            while (world.Tick < 1201)
            {
                host.Step();
            }

            SimEvent started = Last(world, SimEventKind.TierResearchStarted, Remnant);
            Assert.Equal(1200, started.Tick);
        }

        private static SimWorld World(bool fuelEnabled, int materials = -1)
        {
            Ensure();
            SimWorld world = new SimWorld(new MatchConfig(7UL, 0));
            world.BindCatalog(_catalog);
            BindSeat(world, Remnant, _scenario.SeatAt(0), fuelEnabled, -1);
            BindSeat(world, Compact, _scenario.SeatAt(1), fuelEnabled, materials);
            world.BindHoldings(_scenario, _snapshot);
            return world;
        }

        private static void BindSeat(SimWorld world, int factionId, ScenarioSeat seat, bool fuelEnabled, int materials)
        {
            Assert.True(_snapshot.TryGetTown(seat.HomeTownId, out AiTownRecord town));
            world.BindSeat(new SeatSetup(
                factionId,
                seat.Slot,
                seat.Faction,
                town.Id,
                town.XCm,
                town.YCm,
                1,
                -1,
                seat.StartingManpower,
                materials < 0 ? _catalog.StartingMateriel : materials,
                _catalog.StartingFuel,
                fuelEnabled,
                _catalog.StartingSeatPopCap,
                seat.RaisableUnits,
                Array.Empty<ScenarioAutoResearch>(),
                Array.Empty<ScenarioUnitCount>()));
        }

        private static SeatView Seat(SimWorld world, int factionId)
        {
            Assert.True(world.TryGetSeat(factionId, out SeatView seat));
            return seat;
        }

        private static CommandRejectReason LastReject(SimWorld world, int issuer)
        {
            for (int i = world.Events.Count - 1; i >= 0; i--)
            {
                SimEvent ev = world.Events[i];
                if (ev.Kind == SimEventKind.CommandRejected && (int)(ev.Key >> 32) == issuer)
                {
                    return ev.RejectReason;
                }
            }

            return CommandRejectReason.None;
        }

        private static SimEvent Last(SimWorld world, SimEventKind kind, int subject)
        {
            IReadOnlyList<SimEvent> history = world.Events;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].Kind == kind && history[i].SubjectId == subject)
                {
                    return history[i];
                }
            }

            throw new InvalidOperationException("No " + kind + " for " + subject + ".");
        }

        private static int Count(SimWorld world, SimEventKind kind, int subject)
        {
            int n = 0;
            IReadOnlyList<SimEvent> history = world.Events;
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Kind == kind && history[i].SubjectId == subject)
                {
                    n++;
                }
            }

            return n;
        }

        private static void Ensure()
        {
            if (_ready)
            {
                return;
            }

            _catalog = BalanceCatalog.LoadFromDirectory(BalanceLoad.DirectoryPath());
            string dir = GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName);
            byte[] mapBytes = GoldenMapFiles.ReadWrmb(
                Path.Combine(dir, GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapBytes);
            _snapshot = AiMapSnapshot.FromWrmb(mapBytes);
            _scenario = SimScenario.Load(
                GoldenMapFiles.ReadRepoText("data/balance/scenarios/columbia_gorge_100mi_mvp.json"),
                _catalog,
                mapBytes,
                GoldenMapV1MarshTests.MapFileName,
                "columbia_gorge_100mi_mvp");
            _ready = true;
        }

        private static bool _ready;
        private static BalanceCatalog _catalog = null!;
        private static SimScenario _scenario = null!;
        private static AiMapSnapshot _snapshot = null!;
    }
}
