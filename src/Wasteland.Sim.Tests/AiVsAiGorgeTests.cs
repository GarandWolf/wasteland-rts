using System;
using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiVsAiGorgeTests
    {
        internal const ulong MatchSeed = 0xC01055A1UL;

        [Fact]
        public void TwoAiFactions_OnGorgeV1_SameSeed_SameHashStream()
        {
            List<ulong> first = Run(MatchSeed, out List<string> logsA, out int theDallesTownId, out AiMatchHost hostA);
            List<ulong> second = Run(MatchSeed, out List<string> logsB, out int theDallesTownIdB, out AiMatchHost hostB);

            Assert.Equal(14, theDallesTownId);
            Assert.Equal(theDallesTownId, theDallesTownIdB);
            Assert.Equal(first, second);
            Assert.Equal(logsA, logsB);
            Assert.True(first.Count >= 1 + (AiConstants.M1DeterminismTicks / AiConstants.HashPeriodTicks));

            AssertRequiredHeadlessLogs(logsA);
            Assert.Contains("theDallesTown=14", logsA[0]);
            Assert.True(hostA.Ai.Map.UsesMapNeighborLists);
            Assert.Equal(AiMapV1Tests.ExpectedWheeledBlockedChokepoints, hostA.Ai.Map.WheeledBlockedCount);
            AssertDallesOwnerAfterSerialProduction(hostA);

            hostA.Shutdown();
            hostB.Shutdown();
        }

        [Fact]
        public void CompactAttackTarget_IsTheDallesSeatTown()
        {
            AiMatchHost host = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(80);
            AiFactionState compact = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionB);
            Assert.Equal(host.Ai.Map.TheDallesTownId, compact.AttackTargetTownId);
            Assert.True(host.Ai.Map.TryGetTown(host.Ai.Map.TheDallesTownId, out AiTownRecord dalles));
            Assert.True(host.Ai.Map.TryGetTerritory(dalles.TerritoryId, out AiTerritoryRecord prize));
            Assert.Equal(prize.StableKey, host.Ai.Seats.NeutralPrizeTerritoryKey);
            host.Shutdown();
        }

        [Fact]
        public void CompactPrizeMarch_DoesNotMoveToTownIdAsCoordinates()
        {
            AiMatchHost host = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(900);
            Assert.True(host.Ai.Map.TryGetTown(host.Ai.Map.TheDallesTownId, out AiTownRecord dalles));
            int compactId = AiConstants.DefaultAiFactionB;
            int compactCount = 0;
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != compactId)
                {
                    continue;
                }

                compactCount++;
                Assert.False(
                    battalion.HasTarget && battalion.TargetXCm == dalles.Id && battalion.TargetYCm == 0,
                    "ClaimTown stub must not Move(townId, 0).");
            }

            Assert.True(compactCount >= 1);
            host.Shutdown();
        }

        [Fact]
        public void CompactFootAntiTank_StaysHomeOnPrizeMarch()
        {
            AiMatchHost host = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(2100);
            Assert.True(host.Ai.Map.TryGetTown(host.Ai.Map.TheDallesTownId, out AiTownRecord dalles));
            Assert.True(host.Ai.Map.TryGetTown(host.Ai.Map.HoodRiverTownId, out AiTownRecord hood));
            int compactId = AiConstants.DefaultAiFactionB;
            int atId = 0;
            SimBattalion at = default;
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != compactId)
                {
                    continue;
                }

                if (!host.World.TryGetBattalionView(battalion.Id, out BattalionView view))
                {
                    continue;
                }

                if (view.UnitId != "compact_at_battalion")
                {
                    continue;
                }

                if (atId == 0 || battalion.Id < atId)
                {
                    atId = battalion.Id;
                    at = battalion;
                }
            }

            Assert.True(atId != 0, "compact_at_battalion should have spawned by tick 2100.");
            Assert.False(at.HasTarget && at.TargetXCm == dalles.XCm && at.TargetYCm == dalles.YCm);
            long dx = (long)at.PositionXCm - hood.XCm;
            long dy = (long)at.PositionYCm - hood.YCm;
            const long homeRadiusCm = 500000;
            Assert.True(
                (dx * dx) + (dy * dy) <= homeRadiusCm * homeRadiusCm,
                "foot AT must remain near Hood River.");
            host.Shutdown();
        }

        [Fact]
        public void CompactPrizeForce_IsNotReassignedToClaimWhilePrizeUncaptured()
        {
            AiMatchHost host = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(700);
            AssertPrizeForceStaysOnPrize(host, AiConstants.DefaultAiFactionB, 2, false);
            host.Run(700);
            AssertPrizeForceStaysOnPrize(host, AiConstants.DefaultAiFactionB, 2, true);
            host.Shutdown();
        }

        [Fact]
        public void RemnantPrizeForce_IsNotReassignedToClaimWhilePrizeUncaptured()
        {
            AiMatchHost host = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(2200);
            AiFactionState remnant = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionA);
            Assert.Equal(host.Ai.Map.TheDallesTownId, remnant.AttackTargetTownId);
            AssertPrizeForceStaysOnPrize(host, AiConstants.DefaultAiFactionA, 3, true);
            host.Shutdown();
        }

        [Fact]
        public void CompactHomeGarrison_PrefersFootOverPrizeRifles()
        {
            AiMatchHost host = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(2100);
            Assert.True(host.Ai.Map.TryGetTown(host.Ai.Map.TheDallesTownId, out AiTownRecord dalles));
            Assert.True(host.Ai.Map.TryGetTown(host.Ai.Map.HoodRiverTownId, out AiTownRecord hood));
            int compactId = AiConstants.DefaultAiFactionB;
            int rifleOnPrize = 0;
            int footAtHome = 0;
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != compactId)
                {
                    continue;
                }

                if (!host.World.TryGetBattalionView(battalion.Id, out BattalionView view))
                {
                    continue;
                }

                if (AiAttackDefend.IsPrizeRifle(host.Ai.Balance.UnitCatalog, view.UnitId)
                    && Targeting(battalion, dalles))
                {
                    rifleOnPrize++;
                }

                if (AiAttackDefend.IsFootUnit(host.Ai.Balance.UnitCatalog, view.UnitId)
                    && InHome(battalion, hood))
                {
                    footAtHome++;
                }
            }

            Assert.True(rifleOnPrize >= 2, "prize rifles must leave; foot fills garrison");
            Assert.True(footAtHome >= 1, "foot AT/militia stay home to fill garrison");
            host.Shutdown();
        }

        [Fact]
        public void AuthoredScenarioSeats_DoNotGetGenericOpeningBattalion()
        {
            AiMatchHost host = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            Assert.True(host.Ai.Seats.SeatAt(0).HasAuthoredStartingUnits);
            Assert.True(host.Ai.Seats.SeatAt(1).HasAuthoredStartingUnits);
            const int authoredCount = 10;
            Assert.Equal(authoredCount, host.World.BattalionCount);
            host.Run(30);
            Assert.Equal(authoredCount, host.World.BattalionCount);
            host.Shutdown();
        }

        [Fact]
        public void MarshHoldTable_AtTwoThousandTickMarks()
        {
            AiMatchHost host = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(AiConstants.M1DeterminismTicks);
            int[] marks = { 0, 2000, 4000, 6000, 8000, 10000 };
            for (int m = 0; m < marks.Length; m++)
            {
                int tick = marks[m];
                AiTownHoldSample remnant = FindHold(host.HoldSamples, tick, AiConstants.DefaultAiFactionA);
                AiTownHoldSample compact = FindHold(host.HoldSamples, tick, AiConstants.DefaultAiFactionB);
                Assert.Equal(tick, remnant.Tick);
                Assert.Equal(tick, compact.Tick);
                Assert.Equal(remnant.TheDallesOwnerId, compact.TheDallesOwnerId);
                Console.WriteLine(
                    "t=" + tick
                    + " remnant towns=" + remnant.TownsHeld
                    + " battalions=" + remnant.Battalions
                    + " manpower=" + remnant.Manpower
                    + " depots=" + remnant.Depots
                    + " | compact towns=" + compact.TownsHeld
                    + " battalions=" + compact.Battalions
                    + " manpower=" + compact.Manpower
                    + " depots=" + compact.Depots
                    + " | dallesOwner=" + remnant.TheDallesOwnerId);
            }

            host.Shutdown();
        }

        [Fact]
        public void SaveLoadMidRun_ReplaysCommandsAndRestoresAiHash()
        {
            AiMatchHost original = Start(MatchSeed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            original.Run(AiConstants.SaveLoadMidRunTick);
            ulong atSave = original.HashWorld();
            byte[] blob = original.Ai.Save();
            SimCommand[] log = CopyLog(original.CommandLog);

            AiMatchHost restored = AiMatchHost.ReplayTo(
                MatchConfig.Default(MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance(),
                log,
                blob,
                AiConstants.SaveLoadMidRunTick);

            Assert.Equal(original.Tick, restored.Tick);
            Assert.Equal(atSave, restored.HashWorld());
            Assert.Equal(
                original.Ai.PendingCountOf(AiConstants.DefaultAiFactionA)
                    + original.Ai.PendingCountOf(AiConstants.DefaultAiFactionB),
                restored.Ai.PendingCountOf(AiConstants.DefaultAiFactionA)
                    + restored.Ai.PendingCountOf(AiConstants.DefaultAiFactionB));

            int remain = AiConstants.M1DeterminismTicks - AiConstants.SaveLoadMidRunTick;
            original.Run(remain);
            restored.Run(remain);

            List<AiHashSample> originalTail = TailFrom(original.Hashes, AiConstants.SaveLoadMidRunTick);
            List<AiHashSample> restoredTail = TailFrom(restored.Hashes, AiConstants.SaveLoadMidRunTick);
            Assert.Equal(originalTail.Count, restoredTail.Count);
            for (int i = 0; i < originalTail.Count; i++)
            {
                Assert.Equal(originalTail[i].Tick, restoredTail[i].Tick);
                Assert.Equal(originalTail[i].Hash, restoredTail[i].Hash);
            }

            restored.Shutdown();
            original.Shutdown();
        }

        [Fact]
        public void DifferentDifficultyId_ChangesWorldHash()
        {
            AiBalanceTables baseline = AiBalanceTablesTests.LoadBalance();
            AiMatchHost normal = Start(MatchSeed, AiConstants.DifficultyNormal, baseline);
            normal.Run(40);
            ulong normalHash = normal.HashWorld();
            normal.Shutdown();

            AiMatchHost brutal = Start(MatchSeed, AiConstants.DifficultyBrutal, baseline);
            brutal.Run(40);
            Assert.NotEqual(normalHash, brutal.HashWorld());
            Assert.Equal(1100, brutal.Ai.Balance.IncomeMultiplierPpt(AiConstants.DifficultyBrutal));
            brutal.Shutdown();
        }

        [Fact]
        public void DifferentSeed_ProducesDifferentHashStream()
        {
            List<ulong> first = Run(MatchSeed, out _, out _, out AiMatchHost hostA);
            List<ulong> other = Run(MatchSeed ^ 1UL, out _, out _, out AiMatchHost hostB);
            Assert.NotEqual(first, other);
            hostA.Shutdown();
            hostB.Shutdown();
        }

        private static List<ulong> Run(ulong seed, out List<string> logs, out int theDallesTownId, out AiMatchHost host)
        {
            host = Start(seed, AiConstants.DifficultyNormal, AiBalanceTablesTests.LoadBalance());
            host.Run(AiConstants.M1DeterminismTicks);
            logs = new List<string>(host.HeadlessLogs);
            theDallesTownId = host.Ai.Map.TheDallesTownId;
            List<ulong> hashes = new List<ulong>(host.Hashes.Count);
            for (int i = 0; i < host.Hashes.Count; i++)
            {
                hashes.Add(host.Hashes[i].Hash);
            }

            return hashes;
        }

        private static AiMatchHost Start(ulong seed, string difficultyId, AiBalanceTables balance)
        {
            return AiMatchHost.Start(
                MatchConfig.Default(seed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                difficultyId,
                balance);
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

        private static List<AiHashSample> TailFrom(IReadOnlyList<AiHashSample> hashes, int tick)
        {
            List<AiHashSample> tail = new List<AiHashSample>();
            for (int i = 0; i < hashes.Count; i++)
            {
                if (hashes[i].Tick >= tick)
                {
                    tail.Add(hashes[i]);
                }
            }

            return tail;
        }

        private static void AssertRequiredHeadlessLogs(List<string> logs)
        {
            HashSet<int> ticks = new HashSet<int>();
            for (int i = 0; i < logs.Count; i++)
            {
                string line = logs[i];
                Assert.Contains("manpower=", line);
                Assert.Contains("tierIITick=" + AiConstants.TierIITickNone, line);
                int eq = line.IndexOf('=');
                int space = line.IndexOf(' ');
                Assert.True(eq > 0 && space > eq);
                int tick = int.Parse(line.Substring(eq + 1, space - eq - 1));
                ticks.Add(tick);
            }

            for (int tick = AiConstants.HeadlessLogFromTick;
                 tick <= AiConstants.HeadlessLogThroughTickMinimum;
                 tick += AiConstants.HeadlessLogPeriodTicks)
            {
                Assert.True(ticks.Contains(tick), "missing headless log at tick " + tick);
            }
        }

        // Compact marches on the unowned prize. With authored starting units
        // both sides can occupy The Dalles inside 10k; owner is sampled here.
        private static void AssertDallesOwnerAfterSerialProduction(AiMatchHost host)
        {
            int index = host.Ai.Map.IndexOfTown(host.Ai.Map.TheDallesTownId);
            int remnantHome = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionA).HomeTerritoryId;
            int compactHome = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionB).HomeTerritoryId;
            Assert.True(host.Ai.Map.Adjacency.AreNeighbors(remnantHome, host.Ai.Map.TheDallesTerritoryId));
            Assert.True(host.Ai.Map.Adjacency.AreNeighbors(compactHome, host.Ai.Map.TheDallesTerritoryId));
            Assert.Equal(0, host.Ai.TheDallesOwnerId());
            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(index));
        }

        private static AiTownHoldSample FindHold(IReadOnlyList<AiTownHoldSample> samples, int tick, int factionId)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].Tick == tick && samples[i].FactionId == factionId)
                {
                    return samples[i];
                }
            }

            throw new InvalidOperationException("missing hold sample tick=" + tick + " faction=" + factionId);
        }

        private static void AssertPrizeForceStaysOnPrize(
            AiMatchHost host,
            int factionId,
            int rifleNeed,
            bool mustHaveLeft)
        {
            Assert.True(host.Ai.Map.TryGetTown(host.Ai.Map.TheDallesTownId, out AiTownRecord dalles));
            AiFactionState faction = host.Ai.Owned.GetFaction(factionId);
            Assert.True(host.Ai.Map.TryGetTown(faction.HomeTownId, out AiTownRecord home));
            int dallesIndex = host.Ai.Map.IndexOfTown(dalles.Id);
            Assert.Equal(0, host.Ai.Owned.TownOwnerAt(dallesIndex));
            List<int> rifles = new List<int>();
            List<int> scouts = new List<int>();
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != factionId)
                {
                    continue;
                }

                if (!host.World.TryGetBattalionView(battalion.Id, out BattalionView view))
                {
                    continue;
                }

                if (AiAttackDefend.IsPrizeRifle(host.Ai.Balance.UnitCatalog, view.UnitId))
                {
                    rifles.Add(battalion.Id);
                }
                else if (AiAttackDefend.IsPrizeScout(host.Ai.Balance.UnitCatalog, view.UnitId))
                {
                    scouts.Add(battalion.Id);
                }
            }

            rifles.Sort();
            int prizeRifles = rifles.Count;
            if (prizeRifles > rifleNeed)
            {
                prizeRifles = rifleNeed;
            }

            int onPrize = 0;
            for (int i = 0; i < prizeRifles; i++)
            {
                onPrize += AssertNotStolen(host, rifles[i], dalles, home, mustHaveLeft);
            }

            for (int i = 0; i < scouts.Count; i++)
            {
                onPrize += AssertNotStolen(host, scouts[i], dalles, home, mustHaveLeft);
            }

            if (mustHaveLeft)
            {
                Assert.True(rifles.Count >= rifleNeed, "prize rifle group is not ready");
                Assert.True(onPrize >= rifleNeed, "prize force was not sent to The Dalles");
            }
        }

        private static int AssertNotStolen(
            AiMatchHost host,
            int battalionId,
            AiTownRecord dalles,
            AiTownRecord home,
            bool mustHaveLeft)
        {
            SimBattalion battalion = host.World.GetBattalion(battalionId);
            if (!battalion.HasTarget)
            {
                Assert.False(mustHaveLeft, "prize-force battalion " + battalionId + " has no dest");
                return 0;
            }

            if (Targeting(battalion, dalles))
            {
                return 1;
            }

            Assert.True(
                Targeting(battalion, home) && !mustHaveLeft,
                "prize-force battalion " + battalionId + " was reassigned off The Dalles");
            return 0;
        }

        private static bool Targeting(SimBattalion battalion, AiTownRecord town)
        {
            return battalion.HasTarget
                && battalion.TargetXCm == town.XCm
                && battalion.TargetYCm == town.YCm;
        }

        private static bool InHome(SimBattalion battalion, AiTownRecord home)
        {
            long dx = (long)battalion.PositionXCm - home.XCm;
            long dy = (long)battalion.PositionYCm - home.YCm;
            const long homeRadiusCm = 500000;
            return (dx * dx) + (dy * dy) <= homeRadiusCm * homeRadiusCm;
        }
    }
}
