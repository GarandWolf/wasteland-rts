using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiLeadReviewTests
    {
        [Fact]
        public void IsHq_OnlySeatHomeTown_NotOtherTownsInHomeTerritory()
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            AiAttackTownInfo[] towns =
            {
                new AiAttackTownInfo(10, 1, 1, 0, 0, true, 1UL),
                new AiAttackTownInfo(11, 1, 1, 100, 0, false, 1UL),
                new AiAttackTownInfo(12, 1, 1, 200, 0, false, 1UL),
                new AiAttackTownInfo(30, 3, 3, 2000, 0, true, 3UL)
            };
            harness.Towns[10] = new AiTownReadback(1, false, 1, 1, 0, 0);
            harness.Towns[11] = new AiTownReadback(2, false, 2, 1, 0, 0);
            harness.Towns[12] = new AiTownReadback(2, false, 2, 1, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);

            int target = harness.Planner.PickAttackTarget(
                harness.Faction,
                towns,
                towns.Length,
                harness.Roads,
                1,
                harness.Network,
                1,
                100000,
                0UL,
                0);
            Assert.Equal(30, target);

            towns[1] = new AiAttackTownInfo(11, 1, 1, 100, 0, true, 1UL);
            int ifHomeTownsWereHq = harness.Planner.PickAttackTarget(
                harness.Faction,
                towns,
                towns.Length,
                harness.Roads,
                1,
                harness.Network,
                1,
                100000,
                0UL,
                0);
            Assert.Equal(11, ifHomeTownsWereHq);
        }

        [Fact]
        public void ReplayTo_UsesTheSeatsStartReceived()
        {
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            IAiScenarioSeats launch = FixtureAiScenarioSeats.ForLaunchPersonalities(
                AiMapSnapshot.FromWrmb(map),
                tables);
            IAiScenarioSeats custom = FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        0,
                        launch.SeatAt(0).Faction,
                        launch.SeatAt(0).AiPersonality,
                        launch.SeatAt(0).HomeTerritoryKey,
                        launch.SeatAt(0).StartingManpower + 17,
                        launch.SeatAt(0).RaisableUnits,
                        launch.SeatAt(0).BuildableBuildings,
                        System.Array.Empty<AiMvpAutoResearchItem>(),
                        launch.SeatAt(0).HomeTownId),
                    new AiMvpSeatShape(
                        1,
                        launch.SeatAt(1).Faction,
                        launch.SeatAt(1).AiPersonality,
                        launch.SeatAt(1).HomeTerritoryKey,
                        launch.SeatAt(1).StartingManpower + 19,
                        launch.SeatAt(1).RaisableUnits,
                        launch.SeatAt(1).BuildableBuildings,
                        System.Array.Empty<AiMvpAutoResearchItem>(),
                        launch.SeatAt(1).HomeTownId)
                },
                new AiMvpMatchRulesShape(false, "draw"));
            Assert.NotEqual(launch.SeatAt(0).StartingManpower, custom.SeatAt(0).StartingManpower);

            int[] factions = { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB };
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0x51A15EUL),
                map,
                factions,
                AiConstants.DifficultyNormal,
                tables,
                custom);
            host.Run(40);
            byte[] blob = host.Ai.Save();
            SimCommand[] log = CopyLog(host.CommandLog);
            int tick = host.Tick;
            int depotSite = host.Ai.Owned.FactionAt(0).DepotSiteTick;

            AiMatchHost restored = AiMatchHost.ReplayTo(
                MatchConfig.Default(0x51A15EUL),
                map,
                factions,
                AiConstants.DifficultyNormal,
                tables,
                log,
                blob,
                tick,
                custom);
            Assert.Equal(custom.SeatAt(0).StartingManpower, restored.Ai.Seats.SeatAt(0).StartingManpower);
            Assert.Equal(custom.SeatAt(1).StartingManpower, restored.Ai.Seats.SeatAt(1).StartingManpower);
            Assert.Equal(host.Ai.HashState(), restored.Ai.HashState());
            Assert.Equal(depotSite, restored.Ai.Owned.FactionAt(0).DepotSiteTick);

            host.Run(20);
            restored.Run(20);
            Assert.Equal(host.Ai.HashState(), restored.Ai.HashState());
            restored.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void SupplyBudgetDefers_SaturatesAndLoadAcceptsMax()
        {
            AiFactionState faction = new AiFactionState(
                1,
                "test",
                AiConstants.DifficultyNormal,
                0,
                40,
                4,
                1,
                60,
                8,
                1,
                0,
                1,
                4,
                4,
                AiPosture.Turtle,
                new SimRandom(1UL),
                new SimRandom(3UL),
                new SimRandom(5UL),
                new SimRandom(7UL));
            faction.SupplyBudgetDefers = int.MaxValue;
            RoadGraph line = Wrmb.ReadRoadGraph(
                new RoadGraphBuilder()
                    .AddNode(1, 0, 0)
                    .AddNode(2, 1000, 0)
                    .AddNode(3, 2000, 0)
                    .AddEdge(1, 1, 2, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                    .AddEdge(2, 2, 3, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                    .ToBlob());
            AiSupplyResult result = new AiRoadSupply().DistanceToNearestCm(line, 1, new[] { 3 }, 1, faction);
            Assert.Equal(AiSupplyStatus.BudgetExhausted, result.Status);
            Assert.Equal(int.MaxValue, faction.SupplyBudgetDefers);

            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0x5A7UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            host.Ai.Owned.FactionAt(0).SupplyBudgetDefers = int.MaxValue;
            byte[] blob = host.Ai.Save();
            AiMatchHost other = AiMatchHost.Start(
                MatchConfig.Default(0x5A7UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            other.Ai.Load(blob);
            Assert.Equal(int.MaxValue, other.Ai.Owned.FactionAt(0).SupplyBudgetDefers);
            other.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void KnownEngineerId_DoesNotRaiseEngineerEveryPass()
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 1, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            AiBattalionPin engineer = AiMvpAttackTests.PinWithRole(7, 0, 0, 1, AiConstants.RoleEngineer);
            Assert.Equal("compact_engineers", engineer.UnitId);
            harness.SetBattalions(
                AiMvpAttackTests.PinWithRole(5, 0, 0, 1, AiConstants.RoleLineInfantry),
                engineer);
            int raises = 0;
            for (int tick = 0; tick < 8; tick++)
            {
                int from = harness.Sink.Log.Count;
                harness.Tick(tick);
                for (int i = from; i < harness.Sink.Log.Count; i++)
                {
                    if (harness.Sink.Log[i].Kind != AiIssuedKind.RaiseBattalion)
                    {
                        continue;
                    }

                    Assert.True(harness.Catalog.TryGetRole(harness.Sink.Log[i].UnitId, out string role));
                    Assert.NotEqual(AiConstants.RoleEngineer, role);
                    raises++;
                }
            }

            Assert.Equal(0, raises);
        }

        [Fact]
        public void HumanRemnantVisibleBattalions_MakeDefendFire()
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Towns[10] = new AiTownReadback(1, true, 1, 1, 3, 2);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            harness.SetBattalions(AiMvpAttackTests.PinWithRole(5, 0, 0, 1, AiConstants.RoleLineInfantry));
            harness.Tick(0);
            Assert.NotEmpty(harness.Sink.Log);
            Assert.Equal(AiIssuedKind.Move, harness.Sink.Log[0].Kind);
            Assert.Equal(10, harness.Faction.DefendTownId);
        }

        [Fact]
        public void CanAffordAndPopCap_ReadSeatView()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            AiScenarioSeatView seat = FixtureAiScenarioSeats.LaunchSeat("homestead_compact", 1UL);
            IAiUnitCatalog catalog = tables.UnitCatalog;
            int ratio = tables.GetPersonality(AiConstants.PersonalitySettler).LineUnitsPerAntiArmour;
            string lineId = AiMvpAttackTests.FirstRoleOrTarget(seat, catalog, AiConstants.RoleLineInfantry, null);
            Assert.True(catalog.TryGetManpowerCost(lineId, out int lineCost));
            Assert.True(lineCost > 0);

            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Manpower = lineCost - 1;
            Assert.Equal(
                AiConstants.RoleGarrisonInfantry,
                RoleOf(
                    AiAttackDefend.SelectRaiseUnit(seat, catalog, view, Array.Empty<AiBattalionPin>(), 0, ratio, false, true),
                    catalog));

            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Towns[10] = new AiTownReadback(1, false, 1, 1, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 3);
            harness.SeatView.PopCap = 2;
            harness.SeatView.PopUsed = 2;
            harness.SetBattalions();
            harness.Tick(0);
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                Assert.NotEqual(AiIssuedKind.RaiseBattalion, harness.Sink.Log[i].Kind);
            }

            Assert.Equal((int)AiCommandReject.PopCapReached, harness.Faction.LastRejectCode);
        }

        [Fact]
        public void Binder_RejectsNonAiSeat()
        {
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            AiMapSnapshot snapshot = AiMapSnapshot.FromWrmb(map);
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            Assert.True(snapshot.TryGetTown(snapshot.GoldendaleTownId, out AiTownRecord goldendale));
            Assert.True(snapshot.TryGetTerritory(goldendale.TerritoryId, out AiTerritoryRecord territory));
            FixtureAiScenarioSeats seats = FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        0,
                        "remnant_command",
                        null,
                        territory.StableKey,
                        100,
                        FixtureAiScenarioSeats.RemnantRaisableUnits,
                        FixtureAiScenarioSeats.DefaultBuildableBuildings,
                        System.Array.Empty<AiMvpAutoResearchItem>(),
                        goldendale.Id)
                },
                new AiMvpMatchRulesShape(false, "draw"));
            AiFactionState faction = new AiFactionState(
                1,
                AiConstants.PersonalityWarlord,
                AiConstants.DifficultyNormal,
                0,
                40,
                4,
                1,
                60,
                8,
                1,
                0,
                50,
                4,
                4,
                AiPosture.Turtle,
                new SimRandom(1UL),
                new SimRandom(3UL),
                new SimRandom(5UL),
                new SimRandom(7UL));
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
                new AiSeatHomeBinder(seats).Seed(
                    new AiOwnedState(new[] { faction }, snapshot.TownCount),
                    snapshot,
                    tables));
            Assert.Equal(AiSeatHomeBinder.RuleIdFaction, ex.Message);
        }

        [Fact]
        public void FirstRaisable_UsesAuthoredOrderNotAlphabetical()
        {
            IAiUnitCatalog catalog = AiUnitCatalog.FromRows(
                new AiUnitCatalogRow("a_line", 1, AiConstants.RoleLineInfantry, AiConstants.TargetClassSoft, 10),
                new AiUnitCatalogRow("z_line", 1, AiConstants.RoleLineInfantry, AiConstants.TargetClassSoft, 10));
            AiScenarioSeatView seat = new AiScenarioSeatView(
                0,
                "homestead_compact",
                "homestead_compact_settler",
                1UL,
                300,
                new[] { "z_line", "a_line" },
                FixtureAiScenarioSeats.DefaultBuildableBuildings,
                Array.Empty<AiAutoResearch>());
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Manpower = 300;
            string picked = AiAttackDefend.SelectRaiseUnit(
                seat,
                catalog,
                view,
                Array.Empty<AiBattalionPin>(),
                0,
                2,
                false,
                false);
            Assert.Equal("z_line", picked);
        }

        [Fact]
        public void UnitCatalog_RejectsNegativeManpower()
        {
            FormatException rows = Assert.Throws<FormatException>(() =>
                AiUnitCatalog.FromRows(
                    new AiUnitCatalogRow(
                        "probe",
                        1,
                        AiConstants.RoleLineInfantry,
                        AiConstants.TargetClassSoft,
                        -1)));
            Assert.Contains("manpower", rows.Message);

            FormatException json = Assert.Throws<FormatException>(() =>
                AiUnitCatalog.Parse(
                    "{\"units\":{\"probe\":{\"techTier\":1,\"role\":\"line_infantry\",\"targetClass\":\"soft\",\"cost\":{\"manpower\":-1}}}}"));
            Assert.Contains("manpower", json.Message);
        }

        [Fact]
        public void DepotRange_ComesFromLoadedRules()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            Assert.Equal(4828032, tables.DepotRangeCm);
            Assert.NotEqual(AiConstants.DepotRangeCm, tables.DepotRangeCm);
        }

        [Fact]
        public void AttackStrengthRatio_RequiredForEveryDifficulty()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json");
            int hard = difficulty.IndexOf("\"id\": \"hard\"", StringComparison.Ordinal);
            int brutal = difficulty.IndexOf("\"id\": \"brutal\"", StringComparison.Ordinal);
            Assert.True(hard >= 0 && brutal > hard);
            string hardBlock = difficulty.Substring(hard, brutal - hard)
                .Replace(
                    "      \"battalionManpowerCost\": 20,\n      \"attackStrengthRatioPpt\": 1500",
                    "      \"battalionManpowerCost\": 20");
            string stripped = difficulty.Substring(0, hard) + hardBlock + difficulty.Substring(brutal);
            FormatException ex = Assert.Throws<FormatException>(() =>
                AiBalanceTables.Parse(personalities, stripped));
            Assert.Contains(AiBalanceTables.RuleIdAttackStrengthRatioPpt, ex.Message);
        }

        private static string RoleOf(string unitId, IAiUnitCatalog catalog)
        {
            Assert.True(catalog.TryGetRole(unitId, out string role));
            return role;
        }

        private static SimCommand[] CopyLog(IReadOnlyList<SimCommand> log)
        {
            SimCommand[] copy = new SimCommand[log.Count];
            for (int i = 0; i < log.Count; i++)
            {
                copy[i] = log[i];
            }

            return copy;
        }
    }
}
