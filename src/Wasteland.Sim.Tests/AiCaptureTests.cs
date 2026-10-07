using System;
using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiCaptureTests
    {
        public const int ExpectedHamletTicks = 600;
        public const int ExpectedVillageTicks = 1200;
        public const int ExpectedTownTicks = 2400;
        public const int ExpectedCityTicks = 3600;

        [Fact]
        public void CaptureTicks_AreReadFromRulesTimerTicksByTier()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            Assert.True(tables.HasCaptureTable);
            Assert.Equal(ExpectedHamletTicks, tables.CaptureTicksForClass(0));
            Assert.Equal(ExpectedVillageTicks, tables.CaptureTicksForClass(1));
            Assert.Equal(ExpectedTownTicks, tables.CaptureTicksForClass(2));
            Assert.Equal(ExpectedCityTicks, tables.CaptureTicksForClass(3));
            FormatException ex = Assert.Throws<FormatException>(() => tables.CaptureTicksForClass(4));
            Assert.Contains("populationClass 4", ex.Message);
        }

        [Fact]
        public void CaptureLoader_FailsWithRuleIdWhenTierMissing()
        {
            string rules = GoldenMapFiles.ReadRepoText("data/balance/rules.json")
                .Replace("      \"hamlet\": 600,\n", string.Empty);
            FormatException ex = Assert.Throws<FormatException>(() =>
            {
                AiBalanceTables.ParseCaptureRules(rules, out _, out _, out _, out _);
            });
            Assert.Contains(AiBalanceTables.CaptureRuleHamlet, ex.Message);
        }

        [Fact]
        public void CaptureLoader_FailsWithRuleIdWhenTierIsNotInteger()
        {
            string rules = GoldenMapFiles.ReadRepoText("data/balance/rules.json")
                .Replace(
                    "\"timerTicksByTier\": {\n      \"hamlet\": 600",
                    "\"timerTicksByTier\": {\n      \"hamlet\": \"600\"");
            FormatException ex = Assert.Throws<FormatException>(() =>
            {
                AiBalanceTables.ParseCaptureRules(rules, out _, out _, out _, out _);
            });
            Assert.Contains(AiBalanceTables.CaptureRuleHamlet, ex.Message);
        }

        [Fact]
        public void GorgeTowns_UsePopulationClassZeroThroughTwo()
        {
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(AiSeamsTests.LoadGorgeV1Bytes());
            int[] counts = new int[4];
            int maxClass = 0;
            for (int i = 0; i < map.TownCount; i++)
            {
                int populationClass = map.TownAt(i).PopulationClass;
                Assert.InRange(populationClass, 0, 3);
                counts[populationClass]++;
                if (populationClass > maxClass)
                {
                    maxClass = populationClass;
                }
            }

            Assert.Equal(2, maxClass);
            Assert.True(counts[0] > 0);
            Assert.True(counts[1] > 0);
            Assert.True(counts[2] > 0);
            Assert.Equal(0, counts[3]);
        }

        [Fact]
        public void MixedOccupancy_PausesCaptureTimer()
        {
            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                balance);

            int townIndex = FindNeutralTown(host);
            AiTownRecord town = host.Ai.Map.TownAt(townIndex);
            int delay = host.World.CommandDelayTicks;

            host.World.Submit(SimCommand.SpawnBattalion(
                host.Tick,
                AiConstants.DefaultAiFactionA,
                9000001,
                town.XCm,
                town.YCm,
                0));
            host.Run(delay + 8);

            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(townIndex));
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            int heldBeforeContest = host.Ai.Owned.CaptureHeldTicksAt(townIndex);
            Assert.True(heldBeforeContest > 0);
            Assert.True(heldBeforeContest < balance.CaptureTicksForClass(town.PopulationClass));

            host.World.Submit(SimCommand.SpawnBattalion(
                host.Tick,
                AiConstants.DefaultAiFactionB,
                9000002,
                town.XCm,
                town.YCm,
                0));
            host.Run(delay + 1);

            int paused = host.Ai.Owned.CaptureHeldTicksAt(townIndex);
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            host.Run(80);

            Assert.Equal(paused, host.Ai.Owned.CaptureHeldTicksAt(townIndex));
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(townIndex));

            host.Shutdown();
        }

        [Fact]
        public void CaptureRadiiAndFortification_ComeFromRulesJson()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            Assert.Equal(80467, tables.CaptureRadiusCmForClass(0));
            Assert.Equal(160934, tables.CaptureRadiusCmForClass(1));
            Assert.Equal(241402, tables.CaptureRadiusCmForClass(2));
            Assert.Equal(402336, tables.CaptureRadiusCmForClass(3));
            Assert.Equal(1200, tables.DecayStartsAfterTicks);
            Assert.Equal(1, tables.DecayTicksPerTick);
            Assert.Equal(ExpectedTownTicks, tables.CaptureTicksForClass(2, 0));
            Assert.Equal((ExpectedTownTicks * 1250) / 1000, tables.CaptureTicksForClass(2, 1));
            Assert.Equal((ExpectedTownTicks * 1500) / 1000, tables.CaptureTicksForClass(2, 2));
            Assert.Equal((ExpectedTownTicks * 1500) / 1000, tables.CaptureTicksForClass(2, 3));
            Assert.Equal((ExpectedCityTicks * 1500) / 1000, tables.CaptureTicksForClass(3, 2));
        }

        [Fact]
        public void Claim_CompletesAtExactTimerTicksByTier()
        {
            AssertClaimAt(AiHoldingClass.Hamlet, ExpectedHamletTicks);
            AssertClaimAt(AiHoldingClass.Village, ExpectedVillageTicks);
            AssertClaimAt(AiHoldingClass.Town, ExpectedTownTicks);
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            Assert.Equal(ExpectedCityTicks, tables.CaptureTicksForClass(AiHoldingClass.City));
        }

        [Fact]
        public void Occupancy_OneFactionIsReported()
        {
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            sink.Submit(SimCommand.SpawnBattalion(0, 1, 31, 0, 0, 0));
            world.Step();
            world.Step();
            world.Step();
            CaptureZoneOccupancyResult one = sink.OccupancyOf(0, 0, 80000);
            Assert.Equal(CaptureOccupancyKind.OneFaction, one.Kind);
            Assert.Equal(1, one.FactionId);
        }

        [Fact]
        public void Capture_PausesWhileCapturerIsAbsent_ThenDecaysFromRulesJson()
        {
            AiMatchHost host = StartOneFaction();
            AiBalanceTables balance = host.Ai.Balance;
            int townIndex = FindIsolatedTownOfClass(host, AiHoldingClass.Hamlet);
            FreezeOtherUnownedTowns(host, townIndex);
            AiTownRecord town = host.Ai.Map.TownAt(townIndex);
            const int battalionId = 9000301;
            SpawnAt(host, AiConstants.DefaultAiFactionA, battalionId, town.XCm, town.YCm, 25000);
            WaitUntilHeld(host, townIndex, 40);
            int held = host.Ai.Owned.CaptureHeldTicksAt(townIndex);
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));

            host.World.Submit(SimCommand.Move(
                host.Tick,
                AiConstants.DefaultAiFactionA,
                battalionId,
                town.XCm + 5_000_000,
                town.YCm));
            EvacuateFactionFromTown(host, AiConstants.DefaultAiFactionA, town);
            WaitUntilEmpty(host, town, AiConstants.DefaultAiFactionA);

            int heldAtLeave = host.Ai.Owned.CaptureHeldTicksAt(townIndex);
            int absent = host.Ai.Owned.CaptureAbsentTicksAt(townIndex);
            Assert.True(absent >= 1);
            int decayStarts = balance.DecayStartsAfterTicks;
            Assert.True(absent <= decayStarts);
            host.Run(decayStarts - absent);
            Assert.Equal(heldAtLeave, host.Ai.Owned.CaptureHeldTicksAt(townIndex));
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.CaptureFactionAt(townIndex));
            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(townIndex));
            Assert.Equal(decayStarts, host.Ai.Owned.CaptureAbsentTicksAt(townIndex));

            host.Run(1);
            Assert.Equal(heldAtLeave - balance.DecayTicksPerTick, host.Ai.Owned.CaptureHeldTicksAt(townIndex));
            host.Shutdown();
        }

        [Fact]
        public void Recapture_ClearsTheZoneThenHoldsTheTimer()
        {
            AiMatchHost host = StartOneFaction();
            int townIndex = FindIsolatedTownOfClass(host, AiHoldingClass.Hamlet);
            FreezeOtherUnownedTowns(host, townIndex);
            AiTownRecord town = host.Ai.Map.TownAt(townIndex);
            const int firstId = 9000401;
            const int secondId = 9000402;
            SpawnAt(host, AiConstants.DefaultAiFactionA, firstId, town.XCm, town.YCm, 25000);
            WaitUntilOwned(host, townIndex, AiConstants.DefaultAiFactionA);

            host.World.Submit(SimCommand.Move(
                host.Tick,
                AiConstants.DefaultAiFactionA,
                firstId,
                town.XCm + 5_000_000,
                town.YCm));
            EvacuateFactionFromTown(host, AiConstants.DefaultAiFactionA, town);
            WaitUntilEmpty(host, town, AiConstants.DefaultAiFactionA);
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.TownOwnerAt(townIndex));

            SpawnAt(host, AiConstants.DefaultAiFactionB, secondId, town.XCm, town.YCm, 0);
            WaitUntilHeld(host, townIndex, 1);
            int need = host.Ai.Balance.CaptureTicksForClass(town.PopulationClass);
            Assert.Equal(AiCaptureTests.ExpectedHamletTicks, need);
            int held = host.Ai.Owned.CaptureHeldTicksAt(townIndex);
            Assert.True(held < need);
            host.Run(need - held - 1);
            Assert.Equal(need - 1, host.Ai.Owned.CaptureHeldTicksAt(townIndex));
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.TownOwnerAt(townIndex));
            host.Run(1);
            Assert.Equal(AiConstants.DefaultAiFactionB, host.Ai.Owned.TownOwnerAt(townIndex));
            host.Shutdown();
        }

        [Fact]
        public void EconomyAccrual_MatchesEconomyJsonPerMinute()
        {
            AiMatchHost host = StartOneFaction();
            AiEconomyStub economy = new AiEconomyStub(host.Ai.Map, host.Ai.Owned, host.Ai.Balance, (ICaptureOccupancyView)host.Ai.Commands);
            int remnant = AiConstants.DefaultAiFactionA;
            int start = host.Ai.ManpowerOf(remnant);
            Assert.Equal(300, start);
            int perMin = economy.IncomePerMinute(remnant);
            Assert.Equal(host.Ai.ManpowerIncomePerMinute(remnant), perMin);
            Assert.Equal(18, perMin);

            AiBalanceTables tables = host.Ai.Balance;
            Assert.Equal(2, tables.IncomePerMinForClass(0));
            Assert.Equal(8, tables.IncomePerMinForClass(1));
            Assert.Equal(24, tables.IncomePerMinForClass(2));
            Assert.Equal(48, tables.IncomePerMinForClass(3));
            Assert.Equal(10, tables.HqManpowerPerMin("remnant_command"));
            Assert.Equal(1150, tables.ManpowerModifierPpt("homestead_compact"));
            Assert.Equal(1000, tables.ManpowerModifierPpt("remnant_command"));

            for (int tick = 1; tick <= 1200; tick++)
            {
                economy.AccrueTick(tick);
            }

            Assert.Equal(start + perMin, host.Ai.ManpowerOf(remnant));
            host.Shutdown();
        }

        private static void AssertClaimAt(int populationClass, int expectedTicks)
        {
            AiMatchHost host = StartOneFaction();
            Assert.Equal(expectedTicks, host.Ai.Balance.CaptureTicksForClass(populationClass));
            int townIndex = FindUnownedTownOfClass(host, populationClass);
            FreezeOtherUnownedTowns(host, townIndex);
            AiTownRecord town = host.Ai.Map.TownAt(townIndex);
            int battalionId = 9000200 + populationClass;
            SpawnAt(host, AiConstants.DefaultAiFactionA, battalionId, town.XCm, town.YCm, 0);
            WaitUntilHeld(host, townIndex, 1);
            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(townIndex));
            host.Run(expectedTicks - 2);
            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(townIndex));
            Assert.Equal(expectedTicks - 1, host.Ai.Owned.CaptureHeldTicksAt(townIndex));
            host.Run(1);
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.TownOwnerAt(townIndex));
            host.Shutdown();
        }

        private static AiMatchHost StartOneFaction()
        {
            return AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
        }

        private static void SpawnAt(AiMatchHost host, int factionId, int battalionId, int xCm, int yCm, int speed)
        {
            host.World.Submit(SimCommand.SpawnBattalion(host.Tick, factionId, battalionId, xCm, yCm, speed));
            host.Run(host.World.CommandDelayTicks + 1);
        }

        private static void WaitUntilHeld(AiMatchHost host, int townIndex, int held)
        {
            while (host.Tick < 20000 && host.Ai.Owned.CaptureHeldTicksAt(townIndex) < held)
            {
                host.Step();
            }

            Assert.True(host.Ai.Owned.CaptureHeldTicksAt(townIndex) >= held, "capture timer did not start");
        }

        private static void WaitUntilOwned(AiMatchHost host, int townIndex, int factionId)
        {
            while (host.Tick < 20000 && host.Ai.Owned.TownOwnerAt(townIndex) != factionId)
            {
                host.Step();
            }

            Assert.Equal(factionId, host.Ai.Owned.TownOwnerAt(townIndex));
        }

        private static void WaitUntilEmpty(AiMatchHost host, AiTownRecord town, int evacuateFactionId)
        {
            LoggingCommandSink sink = (LoggingCommandSink)host.Ai.Commands;
            int radius = host.Ai.Balance.CaptureRadiusCmForClass(town.PopulationClass);
            while (host.Tick < 20000)
            {
                if (evacuateFactionId != 0)
                {
                    EvacuateFactionFromTown(host, evacuateFactionId, town);
                }

                CaptureZoneOccupancyResult zone = sink.OccupancyOf(town.XCm, town.YCm, radius);
                if (zone.Kind == CaptureOccupancyKind.Empty)
                {
                    host.Step();
                    return;
                }

                host.Step();
            }

            throw new InvalidOperationException("capture zone did not empty.");
        }

        private static void EvacuateFactionFromTown(AiMatchHost host, int factionId, AiTownRecord town)
        {
            LoggingCommandSink sink = (LoggingCommandSink)host.Ai.Commands;
            List<int> ids = new List<int>();
            sink.CopyOwnBattalionIds(factionId, ids);
            int radius = host.Ai.Balance.CaptureRadiusCmForClass(town.PopulationClass);
            long radiusSq = (long)radius * radius;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!sink.TryGetOwnBattalion(factionId, ids[i], out SimBattalion battalion))
                {
                    continue;
                }

                long dx = (long)battalion.PositionXCm - town.XCm;
                long dy = (long)battalion.PositionYCm - town.YCm;
                if ((dx * dx) + (dy * dy) > radiusSq)
                {
                    continue;
                }

                host.World.Submit(SimCommand.Move(
                    host.Tick,
                    factionId,
                    ids[i],
                    town.XCm + 8_000_000,
                    town.YCm + 8_000_000));
            }
        }

        private static int FindUnownedTownOfClass(AiMatchHost host, int populationClass)
        {
            AiMapSnapshot map = host.Ai.Map;
            int fallback = -1;
            Assert.True(map.TryGetTown(map.GoldendaleTownId, out AiTownRecord goldendale));
            long minSepSq = 3L * AiConstants.MileCm * AiConstants.MileCm;
            for (int i = 0; i < map.TownCount; i++)
            {
                if (host.Ai.Owned.TownOwnerAt(i) != 0)
                {
                    continue;
                }

                AiTownRecord town = map.TownAt(i);
                if (town.PopulationClass != populationClass)
                {
                    continue;
                }

                if (fallback < 0)
                {
                    fallback = i;
                }

                if (DistSq(town, goldendale) >= minSepSq)
                {
                    return i;
                }
            }

            if (fallback >= 0)
            {
                return fallback;
            }

            throw new InvalidOperationException("No unowned town of class " + populationClass + ".");
        }

        private static int FindIsolatedTownOfClass(AiMatchHost host, int populationClass)
        {
            AiMapSnapshot map = host.Ai.Map;
            AiFactionState faction = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionA);
            for (int i = 0; i < map.TownCount; i++)
            {
                if (host.Ai.Owned.TownOwnerAt(i) != 0)
                {
                    continue;
                }

                AiTownRecord town = map.TownAt(i);
                if (town.PopulationClass != populationClass)
                {
                    continue;
                }

                if (town.TerritoryId == faction.HomeTerritoryId)
                {
                    continue;
                }

                if (map.Adjacency.AreNeighbors(faction.HomeTerritoryId, town.TerritoryId))
                {
                    continue;
                }

                return i;
            }

            throw new InvalidOperationException("No isolated town of class " + populationClass + ".");
        }

        private static void FreezeOtherUnownedTowns(AiMatchHost host, int keepIndex)
        {
            for (int i = 0; i < host.Ai.Map.TownCount; i++)
            {
                if (i == keepIndex || host.Ai.Owned.TownOwnerAt(i) != 0)
                {
                    continue;
                }

                host.Ai.Owned.SetTownOwner(i, 99);
            }
        }

        private static int FindNeutralTown(AiMatchHost host)
        {
            AiMapSnapshot map = host.Ai.Map;
            Assert.True(map.TryGetTown(map.GoldendaleTownId, out AiTownRecord goldendale));
            Assert.True(map.TryGetTown(map.HoodRiverTownId, out AiTownRecord hoodRiver));
            long minSepSq = 3L * AiConstants.MileCm * AiConstants.MileCm;
            for (int i = 0; i < map.TownCount; i++)
            {
                if (host.Ai.Owned.TownOwnerAt(i) != 0)
                {
                    continue;
                }

                AiTownRecord town = map.TownAt(i);
                if (DistSq(town, goldendale) < minSepSq || DistSq(town, hoodRiver) < minSepSq)
                {
                    continue;
                }

                return i;
            }

            throw new InvalidOperationException("No far unowned town on Gorge marsh.");
        }

        private static long DistSq(AiTownRecord a, AiTownRecord b)
        {
            long dx = (long)a.XCm - b.XCm;
            long dy = (long)a.YCm - b.YCm;
            return (dx * dx) + (dy * dy);
        }
    }
}
