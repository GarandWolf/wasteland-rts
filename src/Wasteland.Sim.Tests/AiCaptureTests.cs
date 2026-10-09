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
            int remnant = SpawnCapturer(host, AiConstants.DefaultAiFactionA, dalles.XCm, dalles.YCm);
            StepQuiet(host, 40, remnant, dalles.XCm, dalles.YCm);
            dalles = SeatTown(host, dalles.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionA, dalles.CaptureFactionId);
            int heldBeforeContest = dalles.CaptureProgressTicks;
            Assert.True(heldBeforeContest > 0);
            Assert.True(heldBeforeContest < dalles.CaptureNeededTicks);
            Assert.False(dalles.Contested);

            int compact = SpawnCapturer(host, AiConstants.DefaultAiFactionB, dalles.XCm, dalles.YCm);
            StepQuietKeep(host, 2, remnant, compact, dalles.XCm, dalles.YCm);
            dalles = SeatTown(host, dalles.TownId);
            Assert.True(dalles.Contested);
            int paused = dalles.CaptureProgressTicks;
            Assert.Equal(AiConstants.DefaultAiFactionA, dalles.CaptureFactionId);
            StepQuietKeep(host, 80, remnant, compact, dalles.XCm, dalles.YCm);
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
            WaitUntilProgress(host, town.TownId, 40, capturer, town.XCm, town.YCm);
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

            StepQuiet(host, 1);
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
            WaitUntilOwned(host, town.TownId, AiConstants.DefaultAiFactionA, firstId, town.XCm, town.YCm);

            host.World.PlaceBattalion(firstId, town.XCm + town.ZoneRadiusCm + 5, town.YCm);
            WaitUntilMeterClear(host, town.TownId);
            town = SeatTown(host, town.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionA, town.OwnerFactionId);
            Assert.Equal(0, town.CaptureFactionId);

            int secondId = SpawnCapturer(host, AiConstants.DefaultAiFactionB, town.XCm, town.YCm);
            WaitUntilProgress(host, town.TownId, 1, secondId, town.XCm, town.YCm);
            town = SeatTown(host, town.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionB, town.CaptureFactionId);
            int need = town.CaptureNeededTicks;
            Assert.True(need >= ExpectedHamletTicks);
            int held = town.CaptureProgressTicks;
            Assert.True(held < need);
            StepQuiet(host, need - held, secondId, town.XCm, town.YCm);
            town = SeatTown(host, town.TownId);
            Assert.Equal(need, town.CaptureProgressTicks);
            Assert.Equal(AiConstants.DefaultAiFactionA, town.OwnerFactionId);
            WaitUntilOwned(host, town.TownId, AiConstants.DefaultAiFactionB, secondId, town.XCm, town.YCm);
            town = SeatTown(host, town.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionB, town.OwnerFactionId);
            StepQuiet(host, 1, secondId, town.XCm, town.YCm);
            Assert.Equal(
                AiConstants.DefaultAiFactionB,
                host.Ai.Owned.TownOwnerAt(host.Ai.Map.IndexOfTown(town.TownId)));
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
            int capturer = SpawnCapturer(host, AiConstants.DefaultAiFactionA, town.XCm, town.YCm);
            WaitUntilProgress(host, town.TownId, 1, capturer, town.XCm, town.YCm);
            town = SeatTown(host, town.TownId);
            Assert.Equal(0, town.OwnerFactionId);
            int need = town.CaptureNeededTicks;
            Assert.True(need >= expectedTicks);
            int held = town.CaptureProgressTicks;
            StepQuiet(host, need - held, capturer, town.XCm, town.YCm);
            town = SeatTown(host, town.TownId);
            Assert.Equal(0, town.OwnerFactionId);
            Assert.Equal(need, town.CaptureProgressTicks);
            WaitUntilOwned(host, town.TownId, AiConstants.DefaultAiFactionA, capturer, town.XCm, town.YCm);
            town = SeatTown(host, town.TownId);
            Assert.Equal(AiConstants.DefaultAiFactionA, town.OwnerFactionId);
            StepQuiet(host, 1, capturer, town.XCm, town.YCm);
            Assert.Equal(
                AiConstants.DefaultAiFactionA,
                host.Ai.Owned.TownOwnerAt(host.Ai.Map.IndexOfTown(town.TownId)));
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

        private static void WaitUntilProgress(
            AiMatchHost host,
            int townId,
            int progress,
            int capturerId,
            int xCm,
            int yCm)
        {
            while (host.Tick < 20000)
            {
                QuietAll(host);
                PinCapturer(host, capturerId, xCm, yCm);
                SilenceExcept(host, capturerId, 0);
                TownView town = SeatTown(host, townId);
                if (town.CaptureProgressTicks >= progress)
                {
                    return;
                }

                host.Step();
            }

            throw new InvalidOperationException("capture timer did not start");
        }

        private static void WaitUntilOwned(
            AiMatchHost host,
            int townId,
            int factionId,
            int capturerId,
            int xCm,
            int yCm)
        {
            while (host.Tick < 20000)
            {
                QuietAll(host);
                PinCapturer(host, capturerId, xCm, yCm);
                SilenceExcept(host, capturerId, 0);
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
                if (town.CaptureAbsentTicks >= 1 || town.CaptureFactionId == 0)
                {
                    return;
                }

                host.Step();
            }

            throw new InvalidOperationException("capture zone did not empty.");
        }

        private static void WaitUntilMeterClear(AiMatchHost host, int townId)
        {
            while (host.Tick < 40000)
            {
                QuietAll(host);
                SilenceExcept(host, 0, 0);
                TownView town = SeatTown(host, townId);
                if (town.CaptureFactionId == 0 && town.CaptureProgressTicks == 0)
                {
                    return;
                }

                host.Step();
            }

            throw new InvalidOperationException("capture meter did not clear.");
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

        private static void StepQuiet(AiMatchHost host, int ticks)
        {
            StepQuiet(host, ticks, 0, 0, 0);
        }

        private static void StepQuiet(AiMatchHost host, int ticks, int capturerId, int xCm, int yCm)
        {
            for (int i = 0; i < ticks; i++)
            {
                QuietAll(host);
                PinCapturer(host, capturerId, xCm, yCm);
                SilenceExcept(host, capturerId, 0);
                host.Step();
            }
        }

        private static void StepQuietKeep(
            AiMatchHost host,
            int ticks,
            int firstId,
            int secondId,
            int xCm,
            int yCm)
        {
            for (int i = 0; i < ticks; i++)
            {
                QuietAll(host);
                PinCapturer(host, firstId, xCm, yCm);
                PinCapturer(host, secondId, xCm, yCm);
                SilenceExcept(host, firstId, secondId);
                host.Step();
            }
        }

        private static void PinCapturer(AiMatchHost host, int capturerId, int xCm, int yCm)
        {
            if (capturerId == 0)
            {
                return;
            }

            host.World.PlaceBattalion(capturerId, xCm, yCm);
        }

        private static void SilenceExcept(AiMatchHost host, int keepA, int keepB)
        {
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                int id = battalions[i].Id;
                if (id == keepA || id == keepB)
                {
                    continue;
                }

                host.World.SetBattalionStrength(id, 0);
            }
        }
    }
}
