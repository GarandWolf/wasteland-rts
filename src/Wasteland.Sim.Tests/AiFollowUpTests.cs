using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
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
        public void DepotBlacklist_TerritoryFromRejectedNode_AndDepotTooCloseExpires()
        {
            AiMatchHost host = StartHost();
            int remnant = AiConstants.DefaultAiFactionA;
            QuietAi(host);
            int engineer = RemnantEngineer(host);
            Assert.True(engineer != 0);

            int unowned = FirstPreviewNode(host, remnant, CommandRejectReason.TerritoryNotOwned);
            Assert.True(unowned != 0);
            SimEvent unownedReject = RejectBuildDepot(host, remnant, engineer, unowned);
            Assert.Equal(CommandRejectReason.TerritoryNotOwned, unownedReject.RejectReason);
            Assert.Equal(unowned, unownedReject.Param0);
            int unownedTerritory = NodeTerritory(host, unowned);
            Assert.Equal(unownedTerritory, BlacklistTerritory(host, remnant, unowned));
            Assert.Equal(AiDepotBlacklistKind.Placement, BlacklistKind(host, remnant, unowned));

            int legal = FirstPreviewNode(host, remnant, CommandRejectReason.None);
            Assert.True(legal != 0);
            host.World.Submit(SimCommand.BuildDepot(host.Tick, remnant, engineer, legal));
            host.Run(host.World.CommandDelayTicks + 2);
            Assert.Equal(
                CommandRejectReason.NodeHasDepot,
                host.World.PreviewDepotPlacement(remnant, legal, out _, out _));
            RoadGraph roads = host.Ai.Map.Roads;
            Assert.True(roads.TryGetNodeIndex(legal, out int legalIndex));
            host.World.PlaceBattalion(
                engineer,
                roads.GetNodeXCm(legalIndex),
                roads.GetNodeYCm(legalIndex));
            int waited = 0;
            while (!DepotIsActive(host, legal) && waited < 1200)
            {
                host.Run(1);
                waited++;
            }

            Assert.True(DepotIsActive(host, legal));

            int tooClose = FirstPreviewNode(host, remnant, CommandRejectReason.DepotTooClose);
            Assert.True(tooClose != 0);
            SimEvent closeReject = RejectBuildDepot(host, remnant, engineer, tooClose);
            Assert.Equal(CommandRejectReason.DepotTooClose, closeReject.RejectReason);
            Assert.Equal(tooClose, closeReject.Param0);
            int closeTerritory = NodeTerritory(host, tooClose);
            Assert.NotEqual(0, closeTerritory);
            Assert.NotEqual(closeReject.Param1, closeTerritory);
            Assert.Equal(closeTerritory, BlacklistTerritory(host, remnant, tooClose));
            Assert.Equal(AiDepotBlacklistKind.Transient, BlacklistKind(host, remnant, tooClose));
            Assert.True(host.Ai.Owned.IsDepotNodeBlacklisted(remnant, tooClose));

            host.Run(AiConstants.BlacklistTransientExpiryTicks + 1);
            Assert.False(host.Ai.Owned.IsDepotNodeBlacklisted(remnant, tooClose));
            Assert.True(host.Ai.Owned.IsDepotNodeBlacklisted(remnant, unowned));
            host.Shutdown();
        }

        [Fact]
        public void HumanRejectAndDeadBuilder_DoNotEnterAiLists_AndSaveLoadHashes()
        {
            int remnant = AiConstants.DefaultAiFactionA;
            int compact = AiConstants.DefaultAiFactionB;
            AiMatchHost host = StartCompactOnly();
            Assert.Equal(1, host.Ai.Owned.FactionCount);
            Assert.Equal(compact, host.Ai.Owned.FactionAt(0).FactionId);
            Assert.True(host.World.TryGetSeat(remnant, out _));
            QuietAi(host);

            int engineer = EngineerOf(host, remnant);
            Assert.True(engineer != 0);
            host.World.Submit(SimCommand.ClaimTown(host.Tick, remnant, engineer, 0));
            host.Run(host.World.CommandDelayTicks + 2);
            Assert.False(HasSeatBackoff(host, remnant));
            Assert.False(HasSeatBlacklist(host, remnant));

            int legal = FirstPreviewNode(host, remnant, CommandRejectReason.None);
            Assert.True(legal != 0);
            host.World.Submit(SimCommand.BuildDepot(host.Tick, remnant, engineer, legal));
            host.Run(host.World.CommandDelayTicks + 2);
            Assert.True(HasReservedDepot(host, remnant, legal, engineer));
            host.World.SetBattalionStrength(engineer, 0);
            host.Run(1);
            Assert.False(HasSeatBackoff(host, remnant));
            Assert.False(HasSeatBlacklist(host, remnant));

            ulong before = host.Ai.HashState();
            byte[] blob = host.Ai.Save();
            AiMatchHost restored = StartCompactOnly();
            restored.Ai.Load(blob);
            Assert.Equal(before, restored.Ai.HashState());
            restored.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void IsMarchingTo_ComparesLastWaypointSoAUnitCanRedirect()
        {
            SimBattalion marching = default;
            marching.HasTarget = true;
            marching.PathCount = 2;
            marching.PathXCm = new[] { 1000, 2000 };
            marching.PathYCm = new[] { 0, 0 };
            Assert.True(AiAttackDefend.IsMarchingTo(marching, 2000, 0));
            Assert.False(AiAttackDefend.IsMarchingTo(marching, 0, 0));
            Assert.False(AiAttackDefend.IsMarchingTo(marching, 1000, 0));

            SimBattalion straight = default;
            straight.HasTarget = true;
            straight.TargetXCm = 500;
            straight.TargetYCm = 700;
            Assert.True(AiAttackDefend.IsMarchingTo(straight, 500, 700));
            Assert.False(AiAttackDefend.IsMarchingTo(straight, 0, 0));
        }

        [Fact]
        public void MoveBackoff_KeysWritesAndLookupsOnDestinationNode()
        {
            Assert.Equal(44, EnemyAi.RejectTargetId(CommandKind.Move, 11, 44));
            Assert.Equal(11, EnemyAi.RejectTargetId(CommandKind.Move, 11, 0));

            AiMvpAttackTests.Harness townKey = ContestedHq();
            AiOwnedState townOwned = new AiOwnedState(new[] { townKey.Faction }, 1);
            AiAttackDefend townPlanner = new AiAttackDefend(
                townKey.Sink,
                townKey,
                townKey.Sink,
                null,
                townOwned);
            townOwned.NoteOrderReject(townKey.Faction.FactionId, 5, CommandKind.Move, 10, 0);
            AiBattalionPin[] townPins =
            {
                AiMvpAttackTests.PinWithRole(5, 2000, 0, 2, AiConstants.RoleLineInfantry)
            };
            townKey.SetBattalions(townPins);
            TickPlanner(townKey, townPlanner, townPins);
            Assert.True(IssuedMove(townKey.Sink, 5));

            AiMvpAttackTests.Harness nodeKey = ContestedHq();
            AiOwnedState nodeOwned = new AiOwnedState(new[] { nodeKey.Faction }, 1);
            AiAttackDefend nodePlanner = new AiAttackDefend(
                nodeKey.Sink,
                nodeKey,
                nodeKey.Sink,
                null,
                nodeOwned);
            nodeOwned.NoteOrderReject(nodeKey.Faction.FactionId, 5, CommandKind.Move, 1, 0);
            AiBattalionPin[] nodePins =
            {
                AiMvpAttackTests.PinWithRole(5, 2000, 0, 2, AiConstants.RoleLineInfantry)
            };
            nodeKey.SetBattalions(nodePins);
            TickPlanner(nodeKey, nodePlanner, nodePins);
            Assert.False(IssuedMove(nodeKey.Sink, 5));
        }

        [Fact]
        public void OrderBackoffStreak_SurvivesExpiry_AndResetsOnlyOnSuccess()
        {
            AiOwnedState owned = new AiOwnedState(new[] { TestFaction(4) }, 1);
            owned.NoteOrderReject(1, 5, CommandKind.Move, 44, 0);
            AiOrderBackoffEntry first = BackoffOf(owned, 1, 5, CommandKind.Move, 44);
            Assert.Equal(1, first.Streak);
            Assert.Equal(AiAttackDefend.RejectBackoffStartTicks, first.UntilTick);
            owned.ExpireOrderBackoff(first.UntilTick);
            Assert.False(owned.IsOrderBackedOff(1, 5, CommandKind.Move, 44, first.UntilTick));
            Assert.Equal(1, BackoffOf(owned, 1, 5, CommandKind.Move, 44).Streak);

            owned.NoteOrderReject(1, 5, CommandKind.Move, 44, first.UntilTick);
            AiOrderBackoffEntry doubled = BackoffOf(owned, 1, 5, CommandKind.Move, 44);
            Assert.Equal(2, doubled.Streak);
            Assert.Equal(first.UntilTick + (AiAttackDefend.RejectBackoffStartTicks * 2), doubled.UntilTick);

            owned.NoteOrderSuccess(1, 5, CommandKind.Move, 44);
            Assert.Equal(0, owned.BackoffCount);
            owned.NoteOrderReject(1, 5, CommandKind.Move, 44, doubled.UntilTick);
            Assert.Equal(1, BackoffOf(owned, 1, 5, CommandKind.Move, 44).Streak);
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

        private static AiMatchHost StartCompactOnly()
        {
            return AiMatchHost.Start(
                MatchConfig.Default(0xA11EEDUL),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
        }

        private static int EngineerOf(AiMatchHost host, int factionId)
        {
            for (int i = 0; i < host.World.BattalionCount; i++)
            {
                SimBattalion battalion = host.World.Battalions[i];
                if (battalion.OwnerId != factionId)
                {
                    continue;
                }

                if (host.World.TryGetBattalionView(battalion.Id, out BattalionView view)
                    && view.UnitId != null
                    && view.UnitId.IndexOf("engineer", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return battalion.Id;
                }
            }

            return 0;
        }

        private static bool HasSeatBackoff(AiMatchHost host, int seatId)
        {
            for (int i = 0; i < host.Ai.Owned.BackoffCount; i++)
            {
                if (host.Ai.Owned.OrderBackoff[i].SeatId == seatId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSeatBlacklist(AiMatchHost host, int seatId)
        {
            for (int i = 0; i < host.Ai.Owned.BlacklistCount; i++)
            {
                if (host.Ai.Owned.DepotBlacklist[i].SeatId == seatId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasReservedDepot(AiMatchHost host, int seatId, int nodeId, int builderId)
        {
            List<DepotView> depots = new List<DepotView>();
            host.World.CopyDepots(depots);
            for (int i = 0; i < depots.Count; i++)
            {
                DepotView depot = depots[i];
                if (depot.OwnerFactionId == seatId
                    && depot.NodeId == nodeId
                    && depot.State != DepotSiteState.Active
                    && depot.BuilderBattalionId == builderId)
                {
                    return true;
                }
            }

            return false;
        }

        private static AiMvpAttackTests.Harness ContestedHq()
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Towns[10] = new AiTownReadback(1, true, 1, 1, 2, 4);
            return harness;
        }

        private static void TickPlanner(
            AiMvpAttackTests.Harness harness,
            AiAttackDefend planner,
            AiBattalionPin[] pins)
        {
            harness.Faction.WorkUnitsThisTick = 0;
            harness.Sink.Tick = 0;
            harness.SeatView.Refresh(harness.Seat, 0);
            planner.Tick(
                0,
                harness.Faction,
                harness.Seat,
                harness.TownRows,
                harness.TownRows.Length,
                pins,
                pins.Length,
                harness.Network,
                1,
                harness.Roads,
                1500,
                harness.SupplyReachCm,
                harness.PrizeTerritoryKey,
                harness.PrizeTargetBonusPpt,
                harness.SeatView,
                harness.Catalog,
                harness.Buildings,
                harness.LineUnitsPerAntiArmour,
                harness.MinGarrisonPerTown,
                harness.TaskForceTargetSize);
        }

        private static bool IssuedMove(RecordingAiCommandSink sink, int battalionId)
        {
            for (int i = 0; i < sink.Log.Count; i++)
            {
                if (sink.Log[i].Kind == AiIssuedKind.Move && sink.Log[i].Arg0 == battalionId)
                {
                    return true;
                }
            }

            return false;
        }

        private static AiOrderBackoffEntry BackoffOf(
            AiOwnedState owned,
            int seatId,
            int battalionId,
            CommandKind kind,
            int targetId)
        {
            for (int i = 0; i < owned.BackoffCount; i++)
            {
                AiOrderBackoffEntry row = owned.OrderBackoff[i];
                if (row.SeatId == seatId
                    && row.BattalionId == battalionId
                    && row.Kind == (byte)kind
                    && row.TargetId == targetId)
                {
                    return row;
                }
            }

            throw new InvalidOperationException("Missing backoff row.");
        }

        private static void QuietAi(AiMatchHost host)
        {
            for (int i = 0; i < host.Ai.Owned.FactionCount; i++)
            {
                AiFactionState faction = host.Ai.Owned.FactionAt(i);
                faction.OrderCount = 0;
                faction.PendingCount = 0;
                faction.RejectBackoffUntilTick = int.MaxValue;
            }
        }

        private static int RemnantEngineer(AiMatchHost host)
        {
            int remnant = AiConstants.DefaultAiFactionA;
            for (int i = 0; i < host.World.BattalionCount; i++)
            {
                SimBattalion battalion = host.World.Battalions[i];
                if (battalion.OwnerId != remnant)
                {
                    continue;
                }

                if (host.World.TryGetBattalionView(battalion.Id, out BattalionView view)
                    && view.UnitId == "remnant_engineers")
                {
                    return battalion.Id;
                }
            }

            return 0;
        }

        private static int NodeTerritory(AiMatchHost host, int nodeId)
        {
            RoadGraph roads = host.Ai.Map.Roads;
            if (!roads.TryGetNodeIndex(nodeId, out int index))
            {
                return 0;
            }

            return roads.GetNodeTerritoryId(index);
        }

        private static bool DepotIsActive(AiMatchHost host, int nodeId)
        {
            List<DepotView> depots = new List<DepotView>();
            host.World.CopyDepots(depots);
            for (int i = 0; i < depots.Count; i++)
            {
                if (depots[i].NodeId == nodeId && depots[i].State == DepotSiteState.Active)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FirstPreviewNode(AiMatchHost host, int seat, CommandRejectReason reason)
        {
            RoadGraph roads = host.Ai.Map.Roads;
            for (int i = 0; i < roads.NodeCount; i++)
            {
                int nodeId = roads.GetNodeId(i);
                if (host.World.PreviewDepotPlacement(seat, nodeId, out _, out _) == reason)
                {
                    return nodeId;
                }
            }

            return 0;
        }

        private static SimEvent RejectBuildDepot(AiMatchHost host, int seat, int engineer, int node)
        {
            SimCommand stamped = host.World.Submit(
                SimCommand.BuildDepot(host.Tick, seat, engineer, node));
            host.Ai.Owned.EnqueueIntent(seat, new AiPendingIntent
            {
                Sequence = stamped.Sequence,
                Kind = AiIssuedKind.BuildDepot,
                BattalionId = engineer,
                TargetId = node,
                IssuedTick = host.Tick
            });
            int mark = host.World.Events.Count;
            host.Run(host.World.CommandDelayTicks + 2);
            for (int i = mark; i < host.World.Events.Count; i++)
            {
                SimEvent ev = host.World.Events[i];
                if (ev.Kind == SimEventKind.CommandRejected && ev.Param0 == node)
                {
                    return ev;
                }
            }

            throw new InvalidOperationException("No CommandRejected for node " + node + ".");
        }

        private static int BlacklistTerritory(AiMatchHost host, int seat, int node)
        {
            return BlacklistEntry(host, seat, node).TerritoryId;
        }

        private static AiDepotBlacklistKind BlacklistKind(AiMatchHost host, int seat, int node)
        {
            return BlacklistEntry(host, seat, node).Kind;
        }

        private static AiDepotBlacklistEntry BlacklistEntry(AiMatchHost host, int seat, int node)
        {
            for (int i = 0; i < host.Ai.Owned.BlacklistCount; i++)
            {
                AiDepotBlacklistEntry entry = host.Ai.Owned.DepotBlacklist[i];
                if (entry.NodeId == node && entry.SeatId == seat)
                {
                    return entry;
                }
            }

            throw new InvalidOperationException("Node " + node + " is not on the shared blacklist for seat " + seat + ".");
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
                SkipOrderBackoff(reader);
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

        private static void SkipOrderBackoff(BinaryReader reader)
        {
            for (int i = 0; i < AiConstants.OrderBackoffCapacity; i++)
            {
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadByte();
                reader.ReadInt32();
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
