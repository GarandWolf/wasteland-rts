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
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            QuietAi(host, AiConstants.DefaultAiFactionA);
            QuietAi(host, AiConstants.DefaultAiFactionB);

            TownView dalles = SeatTown(host, host.Ai.Map.TheDallesTownId);
            Assert.Equal(0, dalles.OwnerFactionId);
            SpawnCapturer(host, AiConstants.DefaultAiFactionA, dalles.XCm, dalles.YCm);
            host.Run(40);
            dalles = SeatTown(host, dalles.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionA, dalles.CaptureFactionId);
            int heldBeforeContest = dalles.CaptureProgressTicks;
            Assert.True(heldBeforeContest > 0);
            Assert.True(heldBeforeContest < dalles.CaptureNeededTicks);
            Assert.False(dalles.Contested);

            SpawnCapturer(host, AiConstants.DefaultAiFactionB, dalles.XCm, dalles.YCm);
            host.Run(2);
            dalles = SeatTown(host, dalles.TownId);
            Assert.True(dalles.Contested);
            int paused = dalles.CaptureProgressTicks;
            Assert.Equal(AiConstants.DefaultAiFactionA, dalles.CaptureFactionId);
            host.Run(80);
            dalles = SeatTown(host, dalles.TownId);
            Assert.Equal(paused, dalles.CaptureProgressTicks);
            Assert.Equal(AiConstants.DefaultAiFactionA, dalles.CaptureFactionId);
            Assert.Equal(0, dalles.OwnerFactionId);
            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(host.Ai.Map.IndexOfTown(dalles.TownId)));

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
            QuietAi(host, AiConstants.DefaultAiFactionA);
            TownView town = FindUnownedSeatOfClass(host, AiHoldingClass.Hamlet);
            int capturer = SpawnCapturer(host, AiConstants.DefaultAiFactionA, town.XCm, town.YCm);
            WaitUntilProgress(host, town.TownId, 40);
            town = SeatTown(host, town.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionA, town.CaptureFactionId);
            int held = town.CaptureProgressTicks;

            host.World.PlaceBattalion(capturer, town.XCm + town.ZoneRadiusCm + 5, town.YCm);
            WaitUntilEmptySeat(host, town.TownId);

            town = SeatTown(host, town.TownId);
            int heldAtLeave = town.CaptureProgressTicks;
            Assert.Equal(held, heldAtLeave);
            int absent = town.CaptureAbsentTicks;
            Assert.True(absent >= 1);
            int decayStarts = host.Ai.Balance.DecayStartsAfterTicks;
            Assert.True(absent <= decayStarts);
            int remain = decayStarts - absent;
            for (int i = 0; i < remain; i++)
            {
                QuietAi(host, AiConstants.DefaultAiFactionA);
                host.Step();
            }

            town = SeatTown(host, town.TownId);
            Assert.Equal(heldAtLeave, town.CaptureProgressTicks);
            Assert.Equal(AiConstants.DefaultAiFactionA, town.CaptureFactionId);
            Assert.Equal(0, town.OwnerFactionId);
            Assert.Equal(decayStarts, town.CaptureAbsentTicks);

            host.Run(1);
            town = SeatTown(host, town.TownId);
            Assert.Equal(heldAtLeave - host.Ai.Balance.DecayTicksPerTick, town.CaptureProgressTicks);
            host.Shutdown();
        }

        [Fact]
        public void Recapture_ClearsTheZoneThenHoldsTheTimer()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            QuietAi(host, AiConstants.DefaultAiFactionA);
            QuietAi(host, AiConstants.DefaultAiFactionB);
            TownView town = FindUnownedSeatOfClass(host, AiHoldingClass.Hamlet);
            int firstId = SpawnCapturer(host, AiConstants.DefaultAiFactionA, town.XCm, town.YCm);
            WaitUntilOwned(host, town.TownId, AiConstants.DefaultAiFactionA);

            host.World.PlaceBattalion(firstId, town.XCm + town.ZoneRadiusCm + 5, town.YCm);
            WaitUntilEmptySeat(host, town.TownId);
            town = SeatTown(host, town.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionA, town.OwnerFactionId);

            SpawnCapturer(host, AiConstants.DefaultAiFactionB, town.XCm, town.YCm);
            WaitUntilProgress(host, town.TownId, 1);
            town = SeatTown(host, town.TownId);
            int need = town.CaptureNeededTicks;
            Assert.True(need >= ExpectedHamletTicks);
            int held = town.CaptureProgressTicks;
            Assert.True(held < need);
            host.Run(need - held - 1);
            town = SeatTown(host, town.TownId);
            Assert.Equal(need - 1, town.CaptureProgressTicks);
            Assert.Equal(AiConstants.DefaultAiFactionA, town.OwnerFactionId);
            host.Run(1);
            town = SeatTown(host, town.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionB, town.OwnerFactionId);
            Assert.Equal(AiConstants.DefaultAiFactionB, host.Ai.Owned.TownOwnerAt(host.Ai.Map.IndexOfTown(town.TownId)));
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
            QuietAi(host, AiConstants.DefaultAiFactionA);
            Assert.Equal(expectedTicks, host.Ai.Balance.CaptureTicksForClass(populationClass));
            TownView town = FindUnownedSeatOfClass(host, populationClass);
            SpawnCapturer(host, AiConstants.DefaultAiFactionA, town.XCm, town.YCm);
            WaitUntilProgress(host, town.TownId, 1);
            town = SeatTown(host, town.TownId);
            Assert.Equal(0, town.OwnerFactionId);
            int need = town.CaptureNeededTicks;
            Assert.True(need >= expectedTicks);
            host.Run(need - 2);
            town = SeatTown(host, town.TownId);
            Assert.Equal(0, town.OwnerFactionId);
            Assert.Equal(need - 1, town.CaptureProgressTicks);
            host.Run(1);
            town = SeatTown(host, town.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionA, town.OwnerFactionId);
            Assert.Equal(AiConstants.DefaultAiFactionA, host.Ai.Owned.TownOwnerAt(host.Ai.Map.IndexOfTown(town.TownId)));
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

        private static TownView SeatTown(AiMatchHost host, int townId)
        {
            Assert.True(host.World.TryGetTown(townId, out TownView town));
            Assert.True(town.IsSeatTown);
            return town;
        }

        private static TownView FindUnownedSeatOfClass(AiMatchHost host, int populationClass)
        {
            List<TownView> towns = new List<TownView>();
            host.World.CopyTowns(towns);
            for (int i = 0; i < towns.Count; i++)
            {
                TownView town = towns[i];
                if (!town.IsSeatTown || town.ZoneRadiusCm <= 0 || town.OwnerFactionId != 0)
                {
                    continue;
                }

                if (town.PopulationClass != populationClass)
                {
                    continue;
                }

                return town;
            }

            throw new InvalidOperationException("No unowned seat of class " + populationClass + ".");
        }

        private static int SpawnCapturer(AiMatchHost host, int factionId, int xCm, int yCm)
        {
            string unitId = factionId == AiConstants.DefaultAiFactionB
                ? "compact_rifle_battalion"
                : "remnant_motor_rifles";
            int id = host.World.SpawnCatalogUnit(factionId, unitId, xCm, yCm);
            QuietAi(host, factionId);
            return id;
        }

        private static void WaitUntilProgress(AiMatchHost host, int townId, int progress)
        {
            while (host.Tick < 20000)
            {
                QuietAll(host);
                TownView town = SeatTown(host, townId);
                if (town.CaptureProgressTicks >= progress)
                {
                    return;
                }

                host.Step();
            }

            throw new InvalidOperationException("capture timer did not start");
        }

        private static void WaitUntilOwned(AiMatchHost host, int townId, int factionId)
        {
            while (host.Tick < 20000)
            {
                QuietAll(host);
                TownView town = SeatTown(host, townId);
                if (town.OwnerFactionId == factionId)
                {
                    return;
                }

                host.Step();
            }

            Assert.Equal(factionId, SeatTown(host, townId).OwnerFactionId);
        }

        private static void WaitUntilEmptySeat(AiMatchHost host, int townId)
        {
            while (host.Tick < 20000)
            {
                QuietAll(host);
                TownView town = SeatTown(host, townId);
                if (town.CaptureFactionId == 0 || town.CaptureAbsentTicks >= 1)
                {
                    host.Step();
                    return;
                }

                host.Step();
            }

            throw new InvalidOperationException("capture zone did not empty.");
        }

        private static void QuietAll(AiMatchHost host)
        {
            for (int i = 0; i < host.Ai.Owned.FactionCount; i++)
            {
                QuietAi(host, host.Ai.Owned.FactionAt(i).FactionId);
            }
        }

        private static void QuietAi(AiMatchHost host, int factionId)
        {
            AiFactionState faction = host.Ai.Owned.GetFaction(factionId);
            faction.OrderCount = 0;
            faction.PendingCount = 0;
            faction.RejectBackoffUntilTick = int.MaxValue;
        }
    }
}
