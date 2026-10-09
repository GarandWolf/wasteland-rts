using System;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiFollowUpTests
    {
        [Fact]
        public void RoadSupply_MultiSourceMatchesPerTargetOnGorge_AndHonoursWorkBudget()
        {
            AiMatchHost host = StartHost();
            RoadGraph roads = host.Ai.Map.Roads;
            int from = roads.GetNodeId(0);
            int[] network = new int[4];
            int n = 0;
            for (int i = 1; i < roads.NodeCount && n < network.Length; i += System.Math.Max(1, roads.NodeCount / 8))
            {
                network[n++] = roads.GetNodeId(i);
            }

            Assert.True(n >= 2);
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            faction.WorkUnitsThisTick = 0;
            AiRoadSupply supply = new AiRoadSupply();
            AiSupplyResult multi = supply.DistanceToNearestCm(roads, from, network, n, faction);
            int best = AiConstants.UnreachableCm;
            for (int i = 0; i < n; i++)
            {
                AiSupplyResult d = supply.DistanceCm(roads, from, network[i], faction);
                if (d.IsReached && d.DistanceCm < best)
                {
                    best = d.DistanceCm;
                }
            }

            Assert.True(multi.IsReached);
            Assert.Equal(best, multi.DistanceCm);
            host.Shutdown();

            RoadGraph line = Wrmb.ReadRoadGraph(
                new RoadGraphBuilder()
                    .AddNode(1, 0, 0)
                    .AddNode(2, 1000, 0)
                    .AddNode(3, 2000, 0)
                    .AddEdge(1, 1, 2, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                    .AddEdge(2, 2, 3, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                    .ToBlob());
            AiFactionState tight = TestFaction(1);
            AiRoadSupply budgeted = new AiRoadSupply();
            int[] far = { 3 };
            AiSupplyResult tightResult = budgeted.DistanceToNearestCm(line, 1, far, 1, tight);
            Assert.Equal(AiSupplyStatus.BudgetExhausted, tightResult.Status);
            Assert.Equal(1, tight.WorkUnitsThisTick);
            Assert.True(tight.WorkUnitsThisTick <= tight.WorkUnitsPerTick);
            Assert.Equal(1, tight.SupplyBudgetDefers);

            tight.WorkUnitsThisTick = 0;
            AiFactionState roomy = TestFaction(50000);
            AiSupplyResult along = budgeted.DistanceToNearestCm(line, 1, far, 1, roomy);
            Assert.True(along.IsReached);
            Assert.Equal(2000, along.DistanceCm);
            Assert.True(roomy.WorkUnitsThisTick > 1);
        }

        [Fact]
        public void BrutalIncome_EqualsNormalTimesIncomeMultiplierPpt()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            Assert.Equal(1100, tables.IncomeMultiplierPpt(AiConstants.DifficultyBrutal));
            FormatException missing = Assert.Throws<FormatException>(() =>
            {
                string rules = GoldenMapFiles.ReadRepoText("data/balance/rules.json")
                    .Replace("      \"incomeMultiplierPpt\": 1100,\n", string.Empty);
                AiBalanceTables.ParseCapture(rules);
            });
            Assert.Contains("difficulty.brutal.incomeMultiplierPpt", missing.Message);

            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            AiMatchHost normal = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                map,
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                tables);
            AiMatchHost brutal = AiMatchHost.Start(
                MatchConfig.Default(0xC01055A1UL),
                map,
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyBrutal,
                tables);
            int normalIncome = normal.Ai.ManpowerIncomePerMinute(AiConstants.DefaultAiFactionA);
            int brutalIncome = brutal.Ai.ManpowerIncomePerMinute(AiConstants.DefaultAiFactionA);
            Assert.Equal((int)(((long)normalIncome * 1100) / 1000), brutalIncome);
            normal.Shutdown();
            brutal.Shutdown();
        }

        [Fact]
        public void ContestedHoldingPaysIncome_HonoursBothValues()
        {
            AiMatchHost host = StartHost();
            int remnant = AiConstants.DefaultAiFactionA;
            int townIndex = 0;
            for (int i = 0; i < host.Ai.Map.TownCount; i++)
            {
                if (host.Ai.Owned.TownOwnerAt(i) == remnant)
                {
                    townIndex = i;
                    break;
                }
            }

            AiTownRecord town = host.Ai.Map.TownAt(townIndex);
            FixedOccupancy empty = new FixedOccupancy(CaptureOccupancyKind.Empty, 0);
            FixedOccupancy contested = new FixedOccupancy(CaptureOccupancyKind.Contested, 0);
            AiEconomyStub pays = new AiEconomyStub(host.Ai.Map, host.Ai.Owned, WithContestedPays(true), empty);
            AiEconomyStub paysContested = new AiEconomyStub(host.Ai.Map, host.Ai.Owned, WithContestedPays(true), contested);
            Assert.Equal(pays.IncomePerMinute(remnant), paysContested.IncomePerMinute(remnant));

            AiEconomyStub skip = new AiEconomyStub(host.Ai.Map, host.Ai.Owned, WithContestedPays(false), contested);
            Assert.True(skip.IncomePerMinute(remnant) < pays.IncomePerMinute(remnant));

            FormatException ex = Assert.Throws<FormatException>(() =>
            {
                string economy = GoldenMapFiles.ReadRepoText("data/balance/economy.json")
                    .Replace("\"contestedHoldingPaysIncome\": false", "\"contestedHoldingPaysIncome\": 0");
                AiBalanceTables.ParseEconomy(
                    economy,
                    PersonalityRows(),
                    new AiBalanceTables.RulesState());
            });
            Assert.Contains(AiBalanceTables.RuleIdContestedHoldingPaysIncome, ex.Message);
            host.Shutdown();
            Assert.NotEqual(0, town.Id);
        }

        [Fact]
        public void Load_RejectsNegativeManpowerCarryAndUnknownFactionId_AndEvenRng()
        {
            AiMatchHost host = StartHost();
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            int manpowerOffset = ManpowerOffset(blob);
            byte[] badManpower = (byte[])blob.Clone();
            WriteInt32(badManpower, manpowerOffset, -1);
            AiMatchHost other = StartHost();
            InvalidDataException manpowerEx = Assert.Throws<InvalidDataException>(() => other.Ai.Load(badManpower));
            Assert.Contains("manpower", manpowerEx.Message);
            other.Shutdown();

            byte[] badCarry = (byte[])blob.Clone();
            WriteInt32(badCarry, manpowerOffset + sizeof(int), -1);
            other = StartHost();
            InvalidDataException carryEx = Assert.Throws<InvalidDataException>(() => other.Ai.Load(badCarry));
            Assert.Contains("carry", carryEx.Message);
            other.Shutdown();

            byte[] badFaction = (byte[])blob.Clone();
            WriteInt32(badFaction, FirstTownOwnerOffset(blob), 99);
            other = StartHost();
            InvalidDataException factionEx = Assert.Throws<InvalidDataException>(() => other.Ai.Load(badFaction));
            Assert.Contains("faction id", factionEx.Message);
            other.Shutdown();

            byte[] badRng = (byte[])blob.Clone();
            int rngInc = FirstRngIncrementOffset(blob);
            badRng[rngInc] = (byte)(badRng[rngInc] & 0xFE);
            other = StartHost();
            InvalidDataException rngEx = Assert.Throws<InvalidDataException>(() => other.Ai.Load(badRng));
            Assert.Contains("RNG", rngEx.Message);
            other.Shutdown();
        }

        [Fact]
        public void DepotBlacklist_EvictsOldestThenNodeThenSeat_AndExpires()
        {
            AiOwnedState owned = new AiOwnedState(new[] { TestFaction(4), TestFactionB() }, 1);
            for (int i = 0; i < AiConstants.DepotNodeBlacklistCapacity; i++)
            {
                owned.BlacklistDepotNode(
                    1,
                    100 + i,
                    i,
                    AiDepotBlacklistKind.Transient,
                    1,
                    1);
            }

            Assert.Equal(AiConstants.DepotNodeBlacklistCapacity, owned.BlacklistCount);
            owned.BlacklistDepotNode(1, 50, 500, AiDepotBlacklistKind.Transient, 1, 1);
            Assert.False(owned.IsDepotNodeBlacklisted(1, 100));
            Assert.True(owned.IsDepotNodeBlacklisted(1, 50));
            Assert.True(owned.IsDepotNodeBlacklisted(1, 100 + AiConstants.DepotNodeBlacklistCapacity - 1));

            AiOwnedState ties = new AiOwnedState(new[] { TestFaction(4), TestFactionB() }, 1);
            ties.BlacklistDepotNode(2, 8, 3, AiDepotBlacklistKind.Transient, 1, 1);
            ties.BlacklistDepotNode(1, 9, 3, AiDepotBlacklistKind.Transient, 1, 1);
            ties.BlacklistDepotNode(1, 8, 3, AiDepotBlacklistKind.Transient, 1, 1);
            Assert.Equal(8, ties.DepotBlacklist[0].NodeId);
            Assert.Equal(1, ties.DepotBlacklist[0].SeatId);
            Assert.Equal(8, ties.DepotBlacklist[1].NodeId);
            Assert.Equal(2, ties.DepotBlacklist[1].SeatId);
            Assert.Equal(9, ties.DepotBlacklist[2].NodeId);

            for (int i = ties.BlacklistCount; i < AiConstants.DepotNodeBlacklistCapacity; i++)
            {
                ties.BlacklistDepotNode(
                    1,
                    200 + i,
                    10,
                    AiDepotBlacklistKind.Transient,
                    1,
                    1);
            }

            ties.BlacklistDepotNode(2, 7, 3, AiDepotBlacklistKind.Transient, 1, 1);
            Assert.False(ties.IsDepotNodeBlacklisted(1, 8));
            Assert.True(ties.IsDepotNodeBlacklisted(2, 8));
            Assert.True(ties.IsDepotNodeBlacklisted(2, 7));

            AiMatchHost host = StartHost();
            int remnant = AiConstants.DefaultAiFactionA;
            AiFactionState faction = host.Ai.Owned.GetFaction(remnant);
            host.Ai.Owned.BlacklistDepotNode(
                remnant,
                77,
                0,
                AiDepotBlacklistKind.Transient,
                faction.HomeTerritoryId,
                remnant);
            host.Run(AiConstants.BlacklistTransientExpiryTicks + 1);
            Assert.False(host.Ai.Owned.IsDepotNodeBlacklisted(remnant, 77));

            host.Ai.Owned.BlacklistDepotNode(
                remnant,
                88,
                host.Tick,
                AiDepotBlacklistKind.Placement,
                faction.HomeTerritoryId,
                remnant);
            host.Run(AiConstants.BlacklistTransientExpiryTicks);
            Assert.True(host.Ai.Owned.IsDepotNodeBlacklisted(remnant, 88));
            host.Ai.Owned.BlacklistDepotNode(
                remnant,
                89,
                host.Tick,
                AiDepotBlacklistKind.Placement,
                faction.HomeTerritoryId,
                remnant + 1);
            host.Run(1);
            Assert.False(host.Ai.Owned.IsDepotNodeBlacklisted(remnant, 89));
            host.Shutdown();
        }

        [Fact]
        public void HashInto_IncludesDroppedFromLog()
        {
            AiMatchHost host = StartHost();
            XxHash64Builder before = new XxHash64Builder();
            host.Ai.Owned.HashInto(before);
            ulong first = before.Digest();
            host.Ai.Owned.DroppedFromLog = host.Ai.Owned.DroppedFromLog + 1;
            XxHash64Builder after = new XxHash64Builder();
            host.Ai.Owned.HashInto(after);
            Assert.NotEqual(first, after.Digest());
            host.Shutdown();
        }

        [Fact(Skip = "TODO: towns do not store fortification level on main; CaptureTicksForClass(level) exists but there is no per-town fortification state to drive a capture.")]
        public void FortificationAboveLevelZero_LengthensCaptureTimer()
        {
            throw new InvalidOperationException("unreachable when skipped");
        }

        [Fact]
        public void MaxCandidateGoalsAboveCeiling_FailsLoadWithRuleId()
        {
            string personalities = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json");
            string difficulty = GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json")
                .Replace("\"maxCandidateGoals\": 64", "\"maxCandidateGoals\": 65");
            AiBalanceTables tables = AiBalanceTables.Parse(personalities, difficulty);
            AiBalanceTables.RulesState rules = AiBalanceTables.ParseCapture(
                GoldenMapFiles.ReadRepoText("data/balance/rules.json"));
            FormatException ex = Assert.Throws<FormatException>(() => tables.WithRules(rules));
            Assert.Equal(AiBalanceTables.RuleIdAiMaxCandidateGoals, ex.Message);
        }

        [Fact]
        public void DepotCapAndScratch_ComeFromRules_AndFrontlinePostsUseChokepointCount()
        {
            AiMatchHost host = StartHost();
            AiBalanceTables tables = host.Ai.Balance;
            Assert.Equal(16, tables.MaxDepotsPerFaction);
            Assert.Equal(32, tables.ConnectivityScratchSize);
            Assert.Equal(tables.ConnectivityScratchSize, host.Ai.NetworkScratchCapacity);
            Assert.Equal(host.Ai.Difficulty.MaxCandidateGoals, host.Ai.CandidateCapacity);
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            Assert.Equal(tables.MaxDepotsPerFaction, faction.Depots.Length);
            Assert.Equal(host.Ai.Map.ChokepointCount, faction.PickedPostIds.Length);
            host.Shutdown();
        }

        [Fact]
        public void DesignerFiles_LoadAndValidate_WithoutAiOwnedPin()
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            Assert.True(tables.HasCaptureTable);
            Assert.True(tables.HasEconomyTable);
            Assert.Equal(16, tables.MaxDepotsPerFaction);
            Assert.Equal(32, tables.ConnectivityScratchSize);
            Assert.Equal(64, tables.MaxCandidateGoalsCeiling);
            Assert.False(tables.ContestedHoldingPaysIncome);
            Assert.Equal(1100, tables.IncomeMultiplierPpt(AiConstants.DifficultyBrutal));
            Assert.Equal(1000, tables.IncomeMultiplierPpt(AiConstants.DifficultyNormal));
            Assert.Equal(AiCaptureTests.ExpectedHamletTicks, tables.CaptureTicksForClass(0));
        }

        private static AiMatchHost StartHost()
        {
            return AiMatchHost.Start(
                MatchConfig.Default(0xA11EEDUL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
        }

        private static AiFactionState TestFactionB()
        {
            return new AiFactionState(
                2,
                "test-b",
                AiConstants.DifficultyNormal,
                0,
                40,
                4,
                1,
                60,
                8,
                1,
                0,
                4,
                4,
                4,
                AiPosture.Turtle,
                new SimRandom(5UL),
                new SimRandom(6UL),
                new SimRandom(7UL),
                new SimRandom(8UL));
        }

        private static AiFactionState TestFaction(int workUnitsPerTick)
        {
            return new AiFactionState(
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
                workUnitsPerTick,
                4,
                4,
                AiPosture.Turtle,
                new SimRandom(1UL),
                new SimRandom(2UL),
                new SimRandom(3UL),
                new SimRandom(4UL));
        }

        private static AiBalanceTables WithContestedPays(bool pays)
        {
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            AiBalanceTables.RulesState rules = AiBalanceTables.ParseCapture(
                GoldenMapFiles.ReadRepoText("data/balance/rules.json"));
            string economy = GoldenMapFiles.ReadRepoText("data/balance/economy.json")
                .Replace(
                    "\"contestedHoldingPaysIncome\": false",
                    "\"contestedHoldingPaysIncome\": " + (pays ? "true" : "false"));
            AiBalanceTables.ParseEconomy(economy, PersonalityRows(), rules);
            return tables.WithRules(rules).WithBalanceKey(AiBalanceTablesTests.LoadBalance().BalanceKey);
        }

        private static AiPersonalityProfile[] PersonalityRows()
        {
            AiBalanceTables tables = AiBalanceTables.Parse(
                GoldenMapFiles.ReadRepoText("data/balance/ai/ai_personalities.json"),
                GoldenMapFiles.ReadRepoText("data/balance/ai/ai_difficulty.json"));
            return new[]
            {
                tables.GetPersonality(AiConstants.PersonalityWarlord),
                tables.GetPersonality(AiConstants.PersonalitySettler),
                tables.GetPersonality(AiConstants.PersonalityRaider)
            };
        }

        private sealed class FixedOccupancy : ICaptureOccupancyView
        {
            private readonly CaptureZoneOccupancyResult _result;

            public FixedOccupancy(CaptureOccupancyKind kind, int factionId)
            {
                _result = new CaptureZoneOccupancyResult(kind, factionId);
            }

            public CaptureZoneOccupancyResult OccupancyOf(int xCm, int yCm, int radiusCm)
            {
                return _result;
            }
        }

        private static int ManpowerOffset(byte[] blob)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(blob)))
            {
                SkipHeader(reader);
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadString();
                reader.ReadString();
                for (int i = 0; i < 8; i++)
                {
                    reader.ReadInt32();
                }

                reader.ReadByte();
                for (int i = 0; i < 4; i++)
                {
                    reader.ReadInt32();
                }

                reader.ReadBoolean();
                for (int i = 0; i < 4; i++)
                {
                    reader.ReadInt32();
                }

                return (int)reader.BaseStream.Position;
            }
        }

        private static int FirstTownOwnerOffset(byte[] blob)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(blob)))
            {
                SkipHeader(reader);
                int factions = reader.ReadInt32();
                for (int f = 0; f < factions; f++)
                {
                    SkipFaction(reader);
                }

                SkipDepotBlacklist(reader);
                reader.ReadInt32();
                return (int)reader.BaseStream.Position;
            }
        }

        private static int FirstRngIncrementOffset(byte[] blob)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(blob)))
            {
                SkipHeader(reader);
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadString();
                reader.ReadString();
                for (int i = 0; i < 8; i++)
                {
                    reader.ReadInt32();
                }

                reader.ReadByte();
                for (int i = 0; i < 4; i++)
                {
                    reader.ReadInt32();
                }

                reader.ReadBoolean();
                for (int i = 0; i < 20; i++)
                {
                    reader.ReadInt32();
                }

                reader.ReadUInt64();
                return (int)reader.BaseStream.Position;
            }
        }

        private static void SkipDepotBlacklist(BinaryReader reader)
        {
            for (int i = 0; i < AiConstants.DepotNodeBlacklistCapacity; i++)
            {
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadByte();
                reader.ReadInt32();
                reader.ReadInt32();
            }
        }

        private static void SkipHeader(BinaryReader reader)
        {
            reader.ReadInt32();
            reader.ReadUInt64();
            reader.ReadUInt64();
            reader.ReadUInt64();
            reader.ReadInt32();
        }

        private static void SkipFaction(BinaryReader reader)
        {
            reader.ReadInt32();
            reader.ReadString();
            reader.ReadString();
            for (int i = 0; i < 8; i++)
            {
                reader.ReadInt32();
            }

            reader.ReadByte();
            reader.ReadInt32();
            int orderCount = reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadBoolean();
            for (int i = 0; i < 6; i++)
            {
                reader.ReadInt32();
            }

            int depotCount = reader.ReadInt32();
            int pickedCount = reader.ReadInt32();
            int pendingCount = reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            for (int i = 0; i < 9; i++)
            {
                reader.ReadInt32();
            }

            for (int r = 0; r < 4; r++)
            {
                reader.ReadUInt64();
                reader.ReadUInt64();
            }

            for (int o = 0; o < orderCount; o++)
            {
                reader.ReadInt32();
                reader.ReadByte();
                for (int k = 0; k < 5; k++)
                {
                    reader.ReadInt32();
                }
            }

            for (int d = 0; d < depotCount; d++)
            {
                reader.ReadInt32();
                reader.ReadUInt64();
                for (int k = 0; k < 4; k++)
                {
                    reader.ReadInt32();
                }
            }

            for (int p = 0; p < pickedCount; p++)
            {
                reader.ReadInt32();
                reader.ReadInt32();
            }

            for (int q = 0; q < pendingCount; q++)
            {
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadByte();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
            }

            int intentCount = reader.ReadInt32();
            for (int n = 0; n < intentCount; n++)
            {
                reader.ReadInt32();
                reader.ReadByte();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
            }
        }

        private static void WriteInt32(byte[] blob, int offset, int value)
        {
            blob[offset] = (byte)value;
            blob[offset + 1] = (byte)(value >> 8);
            blob[offset + 2] = (byte)(value >> 16);
            blob[offset + 3] = (byte)(value >> 24);
        }
    }
}
