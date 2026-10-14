using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.Scenario;
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
            Assert.False(ties.IsDepotNodeBlacklisted(2, 8));
            Assert.True(ties.IsDepotNodeBlacklisted(1, 8));
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
        public void FilledTables_SaveLoad_KeepsInsertSeqEvictionAndIssuedIds()
        {
            AiMatchHost host = StartCompactOnly();
            int compact = AiConstants.DefaultAiFactionB;
            FillSharedTables(host.Ai.Owned, compact, 5, host.Tick);
            Assert.Equal(AiConstants.DepotNodeBlacklistCapacity, host.Ai.Owned.BlacklistCount);
            Assert.Equal(AiConstants.OrderBackoffCapacity, host.Ai.Owned.BackoffCount);
            host.Ai.Owned.BlacklistDepotNode(
                compact,
                90001,
                host.Tick,
                AiDepotBlacklistKind.Transient,
                1,
                0);
            host.Ai.Owned.NoteOrderReject(compact, 5, CommandKind.Move, 90001, host.Tick);
            host.Ai.Owned.EnqueueIntent(compact, new AiPendingIntent
            {
                Sequence = 90001,
                Kind = AiIssuedKind.Move,
                BattalionId = 5,
                TargetId = 90001,
                IssuedTick = host.Tick
            });
            Assert.Equal(AiConstants.DepotNodeBlacklistCapacity, host.Ai.Owned.BlacklistCount);
            Assert.Equal(AiConstants.OrderBackoffCapacity, host.Ai.Owned.BackoffCount);
            Assert.Equal(AiConstants.PendingIntentCapacity, host.Ai.Owned.GetFaction(compact).IntentCount);
            Assert.False(host.Ai.Owned.IsDepotNodeBlacklisted(compact, 10000));
            Assert.False(host.Ai.Owned.IsOrderBackedOff(compact, 5, CommandKind.Move, 20000, host.Tick + 1));
            Assert.True(host.Ai.Owned.IsDepotNodeBlacklisted(compact, 90001));
            Assert.Equal(0, IntentTarget(host.Ai.Owned.GetFaction(compact), 1));
            Assert.Equal(90001, IntentTarget(host.Ai.Owned.GetFaction(compact), 90001));

            ulong before = host.Ai.HashState();
            byte[] blob = host.Ai.Save();
            AiMatchHost restored = StartCompactOnly();
            restored.Ai.Load(blob);
            Assert.Equal(before, restored.Ai.HashState());
            Assert.Equal(host.Ai.Owned.NextInsertSeq, restored.Ai.Owned.NextInsertSeq);
            Assert.Equal(host.Ai.Owned.IssuedCommandNextId, restored.Ai.Owned.IssuedCommandNextId);
            Assert.Equal(
                host.Ai.Owned.GetFaction(compact).LastIssuedCommandId,
                restored.Ai.Owned.GetFaction(compact).LastIssuedCommandId);
            Assert.Equal(
                host.Ai.Owned.GetFaction(compact).LastIssuedSequence,
                restored.Ai.Owned.GetFaction(compact).LastIssuedSequence);
            Assert.False(restored.Ai.Owned.IsDepotNodeBlacklisted(compact, 10000));
            Assert.True(restored.Ai.Owned.IsDepotNodeBlacklisted(compact, 90001));

            host.Ai.Owned.BlacklistDepotNode(
                compact,
                90002,
                host.Tick,
                AiDepotBlacklistKind.Transient,
                1,
                0);
            restored.Ai.Owned.BlacklistDepotNode(
                compact,
                90002,
                host.Tick,
                AiDepotBlacklistKind.Transient,
                1,
                0);
            Assert.Equal(host.Ai.HashState(), restored.Ai.HashState());
            Assert.False(host.Ai.Owned.IsDepotNodeBlacklisted(compact, 10001));
            Assert.False(restored.Ai.Owned.IsDepotNodeBlacklisted(compact, 10001));
            restored.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void RejectOf_CommandIdEqualToBattalionId_DoesNotMisattribute()
        {
            const int factionId = AiConstants.DefaultAiFactionA;
            const int battalionId = 5;
            const int delay = 2;
            SimWorld world = new SimWorld(new MatchConfig(1UL, delay));
            world.BindCatalog(BalanceCatalog.LoadFromDirectory(BalanceLoad.DirectoryPath()));
            world.BindSeat(new SeatSetup(
                factionId,
                factionId,
                "homestead_compact",
                7,
                0,
                0,
                1,
                -1,
                1000,
                1000,
                400,
                false,
                10,
                new[] { "compact_rifle_battalion" }));
            world.Submit(SimCommand.SpawnBattalion(0, factionId, battalionId, 1000, 2000, 24000));
            world.Step();
            world.Step();
            world.ProcessQueue();
            Assert.True(world.TryGetBattalionView(battalionId, out _));

            SimWorldCommandSink inner = new SimWorldCommandSink(world);
            inner.Submit(SimCommand.ClaimTown(world.Tick, factionId, battalionId, 999002));
            world.Step();
            world.Step();
            world.ProcessQueue();
            Assert.True(world.TryGetRejection(battalionId, out CommandRejectReason bait, out _, out _));
            Assert.NotEqual(CommandRejectReason.None, bait);

            AiOwnedState owned = new AiOwnedState(
                new[]
                {
                    new AiFactionState(
                        factionId,
                        AiConstants.PersonalitySettler,
                        AiConstants.DifficultyNormal,
                        0,
                        40,
                        4,
                        1,
                        60,
                        8,
                        1,
                        0,
                        50000,
                        4,
                        4,
                        AiPosture.Attack,
                        new SimRandom(1UL),
                        new SimRandom(3UL),
                        new SimRandom(5UL),
                        new SimRandom(7UL))
                },
                1);
            ForwardingAiCommandSink forwarding = new ForwardingAiCommandSink(inner, owned);
            forwarding.NextIssuedId = battalionId;
            int moveId = forwarding.SubmitMove(factionId, battalionId, 1000, 2000);
            Assert.Equal(battalionId, moveId);
            Assert.Equal(AiCommandReject.None, forwarding.RejectOf(moveId));

            world.Step();
            world.Step();
            world.ProcessQueue();
            Assert.Equal(AiCommandReject.None, forwarding.RejectOf(moveId));
            Assert.True(world.TryGetRejection(battalionId, out CommandRejectReason stillBait, out _, out _));
            Assert.Equal(bait, stillBait);

            ForwardingAiCommandSink own = new ForwardingAiCommandSink(inner, owned);
            own.NextIssuedId = battalionId;
            int claimId = own.SubmitClaimTown(factionId, battalionId, 999003);
            Assert.Equal(battalionId, claimId);
            world.Step();
            world.Step();
            world.ProcessQueue();
            Assert.Equal(AiCommandReject.Rejected, own.RejectOf(claimId));
            Assert.Equal(AiCommandReject.None, forwarding.RejectOf(moveId));
        }

        [Fact]
        public void SaveLoad_AiVsAiAndHumanVsAi_MatchUninterruptedTo20000()
        {
            int[] saveTicks = { 1500, 4000, 12000 };
            AssertSaveLoadTo20000(
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                saveTicks,
                fillAtTick: 12000);
            AssertSaveLoadTo20000(
                new[] { AiConstants.DefaultAiFactionB },
                saveTicks,
                fillAtTick: 12000);
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
        public void InProgressCapture_DoesNotGetAnotherClaimTown()
        {
            TownView capturing = new TownView(
                30,
                3,
                3UL,
                true,
                1000,
                2000,
                3,
                1,
                1,
                500,
                0,
                0,
                2,
                10,
                40,
                0,
                false);
            Assert.True(AiAttackDefend.ClaimTownIsOccupiedBy(capturing, 2, 0, 0));
            Assert.True(AiAttackDefend.ClaimTownIsOccupiedBy(capturing, 2, 1000, 2000));
            Assert.False(AiAttackDefend.ClaimTownIsOccupiedBy(
                new TownView(30, 3, 3UL, true, 1000, 2000, 3, 1, 1, 500, 0, 0, 0, 0, 40, 0, false),
                2,
                0,
                0));
            Assert.True(AiAttackDefend.ClaimTownIsOccupiedBy(
                new TownView(30, 3, 3UL, true, 1000, 2000, 3, 1, 1, 500, 0, 0, 0, 0, 40, 0, false),
                2,
                1000,
                2000));

            AiOwnedState owned = new AiOwnedState(new[] { TestFaction(4) }, 1);
            owned.EnqueueIntent(1, new AiPendingIntent
            {
                Sequence = 1,
                Kind = AiIssuedKind.ClaimTown,
                BattalionId = 5,
                TargetId = 30,
                IssuedTick = 0
            });
            Assert.True(owned.HasPendingClaimTown(1, 30));
            Assert.True(owned.NoteAcceptedClaimTown(1, 30));
            Assert.True(owned.NoteAcceptedClaimTown(1, 30));
            Assert.True(owned.NoteAcceptedClaimTown(1, 30));
            Assert.False(owned.NoteAcceptedClaimTown(1, 30));
            Assert.Equal(3, owned.AcceptedClaimTowns(1, 30));
            owned.ResetClaimTownsForTown(30);
            Assert.Equal(0, owned.AcceptedClaimTowns(1, 30));
        }

        [Fact]
        public void NoPathMove_RetriedTwice_DoublesBackoffStreak()
        {
            AiMatchHost host = StartHost();
            QuietAi(host);
            int remnant = AiConstants.DefaultAiFactionA;
            int engineer = RemnantEngineer(host);
            Assert.True(engineer != 0);
            Assert.True(TryBattalion(host, engineer, out SimBattalion before));
            int destNode = ResolveUnreachableRoadTarget(
                host,
                before.PositionXCm,
                before.PositionYCm,
                out int destXCm,
                out int destYCm);
            host.Ai.AiCommands.SubmitMove(remnant, engineer, destXCm, destYCm);
            Assert.Equal(destNode, MoveIntentTarget(host, remnant, engineer));
            Assert.Equal(0, host.Ai.Owned.BackoffCount);
            int firstRejectTick = StepUntilBackoff(host, remnant, engineer, destNode, 1);
            AiOrderBackoffEntry first = BackoffOf(host.Ai.Owned, remnant, engineer, CommandKind.Move, destNode);
            Assert.Equal(1, first.Streak);
            Assert.Equal(firstRejectTick + AiAttackDefend.RejectBackoffStartTicks, first.UntilTick);

            host.Ai.AiCommands.SubmitMove(remnant, engineer, destXCm, destYCm);
            Assert.Equal(1, BackoffOf(host.Ai.Owned, remnant, engineer, CommandKind.Move, destNode).Streak);
            int secondRejectTick = StepUntilBackoff(host, remnant, engineer, destNode, 2);
            AiOrderBackoffEntry doubled = BackoffOf(host.Ai.Owned, remnant, engineer, CommandKind.Move, destNode);
            Assert.Equal(2, doubled.Streak);
            Assert.Equal(
                secondRejectTick + (AiAttackDefend.RejectBackoffStartTicks * 2),
                doubled.UntilTick);
            host.Shutdown();
        }

        [Fact]
        public void PendingMove_DoesNotRetriggerWhenBattalionAlreadyHasATarget()
        {
            AiMatchHost host = StartHost();
            QuietAi(host);
            int remnant = AiConstants.DefaultAiFactionA;
            int engineer = RemnantEngineer(host);
            Assert.True(engineer != 0);
            Assert.True(TryBattalion(host, engineer, out SimBattalion before));
            int destX = before.PositionXCm + 200000;
            int destY = before.PositionYCm;
            host.World.Submit(SimCommand.Move(host.Tick, remnant, engineer, destX, destY));
            host.Run(host.World.CommandDelayTicks + 2);
            Assert.True(TryBattalion(host, engineer, out SimBattalion marching));
            Assert.True(marching.HasTarget);
            Assert.True(TryLastWaypoint(marching, out destX, out destY));
            Assert.True(AiAttackDefend.IsMarchingTo(marching, destX, destY));
            int moves = CountMoves(host, engineer);
            AiFactionState faction = host.Ai.Owned.GetFaction(remnant);
            Assert.True(host.Ai.Issue(faction, CommandKind.Move, engineer, destX, destY, 0));
            host.Run(faction.ReactionExtraTicks + 2);
            Assert.Equal(moves, CountMoves(host, engineer));
            host.Shutdown();
        }

        [Fact]
        public void RedirectMove_ReachesLogWhenBattalionIsMarchingElsewhere()
        {
            AiMatchHost host = StartHost();
            int remnant = AiConstants.DefaultAiFactionA;
            const int battalionId = 3;
            Assert.True(TryBattalion(host, battalionId, out SimBattalion start));
            Assert.Equal(remnant, start.OwnerId);
            int dallesIndex = host.Ai.Map.IndexOfTown(host.Ai.Map.TheDallesTownId);
            AiTownRecord dalles = host.Ai.Map.TownAt(dallesIndex);
            host.World.Submit(SimCommand.Move(host.Tick, remnant, battalionId, dalles.XCm, dalles.YCm));
            while (host.Tick < 200)
            {
                QuietAi(host);
                host.Step();
            }

            QuietAi(host);
            Assert.Equal(200, host.Tick);
            Assert.True(TryBattalion(host, battalionId, out SimBattalion marching));
            Assert.True(marching.HasTarget);
            Assert.True(marching.PathCount > 1);
            Assert.True(TryLastWaypoint(marching, out int lastX, out int lastY));
            int redirectX = lastX + 400000;
            int redirectY = lastY + 400000;
            Assert.False(AiAttackDefend.IsMarchingTo(marching, redirectX, redirectY));
            int movesBefore = CountMoves(host, battalionId);
            AiFactionState faction = host.Ai.Owned.GetFaction(remnant);
            Assert.True(host.Ai.Issue(faction, CommandKind.Move, battalionId, redirectX, redirectY, 0));
            host.Run(faction.ReactionExtraTicks + host.World.CommandDelayTicks + 2);
            Assert.True(CountMoves(host, battalionId) > movesBefore);
            Assert.True(HasMoveTo(host, battalionId, redirectX, redirectY, 200));
            host.Shutdown();
        }

        [Fact]
        public void ClaimTownCounts_SurviveSaveLoadAndMatchUninterruptedReplay()
        {
            MatchConfig match = MatchConfig.Default(AiVsAiGorgeTests.MatchSeed);
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB };
            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();
            AiMatchHost original = AiMatchHost.Start(
                match,
                map,
                factions,
                AiConstants.DifficultyNormal,
                balance);
            int mid = AiConstants.SaveLoadMidRunTick;
            original.Run(mid);
            Assert.True(AnyAcceptedClaimTown(original), "Need a live claim-town count at save.");
            ulong atSave = original.HashWorld();
            byte[] blob = original.Ai.Save();
            SimCommand[] savedLog = CopyCommands(original.CommandLog);
            AiMatchHost restored = AiMatchHost.ReplayTo(
                match,
                map,
                factions,
                AiConstants.DifficultyNormal,
                balance,
                savedLog,
                blob,
                mid);
            Assert.Equal(original.Tick, restored.Tick);
            Assert.Equal(atSave, restored.HashWorld());
            Assert.True(AnyAcceptedClaimTown(restored));
            int remain = AiConstants.M1DeterminismTicks - mid;
            original.Run(remain);
            restored.Run(remain);
            Assert.Equal(original.HashWorld(), restored.HashWorld());
            List<SimCommand> replayed = new List<SimCommand>(savedLog.Length + restored.CommandLog.Count);
            for (int i = 0; i < savedLog.Length; i++)
            {
                replayed.Add(savedLog[i]);
            }

            for (int i = 0; i < restored.CommandLog.Count; i++)
            {
                replayed.Add(restored.CommandLog[i]);
            }

            AssertLogsEqual(original.CommandLog, replayed);
            original.Shutdown();
            restored.Shutdown();
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

        private static AiMatchHost StartSaveLoadHost(int[] factions)
        {
            return AiMatchHost.Start(
                MatchConfig.Default(AiVsAiGorgeTests.MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                factions,
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
        }

        private static void AssertSaveLoadTo20000(int[] factions, int[] saveTicks, int fillAtTick)
        {
            const int endTick = 20000;
            AiMatchHost original = StartSaveLoadHost(factions);
            int cursor = 0;
            for (int s = 0; s < saveTicks.Length; s++)
            {
                int saveTick = saveTicks[s];
                original.Run(saveTick - cursor);
                cursor = saveTick;
                if (saveTick == fillAtTick)
                {
                    FillSharedTables(
                        original.Ai.Owned,
                        original.Ai.Owned.FactionAt(0).FactionId,
                        5,
                        original.Tick);
                    Assert.Equal(AiConstants.DepotNodeBlacklistCapacity, original.Ai.Owned.BlacklistCount);
                    Assert.Equal(AiConstants.OrderBackoffCapacity, original.Ai.Owned.BackoffCount);
                }

                ulong worldAtSave = original.HashWorld();
                ulong aiAtSave = original.Ai.HashState();
                byte[] blob = original.Ai.Save();
                SimCommand[] savedLog = CopyCommands(original.CommandLog);
                AiMatchHost restored = AiMatchHost.ReplayTo(
                    MatchConfig.Default(AiVsAiGorgeTests.MatchSeed),
                    AiSeamsTests.LoadGorgeV1Bytes(),
                    factions,
                    AiConstants.DifficultyNormal,
                    AiBalanceTablesTests.LoadBalance(),
                    savedLog,
                    blob,
                    saveTick);
                Assert.Equal(original.Tick, restored.Tick);
                Assert.Equal(worldAtSave, restored.HashWorld());
                Assert.Equal(aiAtSave, restored.Ai.HashState());

                int remain = endTick - saveTick;
                original.Run(remain);
                restored.Run(remain);
                Assert.Equal(endTick, original.Tick);
                Assert.Equal(original.HashWorld(), restored.HashWorld());
                Assert.Equal(original.Ai.HashState(), restored.Ai.HashState());
                List<SimCommand> replayed = new List<SimCommand>(savedLog.Length + restored.CommandLog.Count);
                for (int i = 0; i < savedLog.Length; i++)
                {
                    replayed.Add(savedLog[i]);
                }

                for (int i = 0; i < restored.CommandLog.Count; i++)
                {
                    replayed.Add(restored.CommandLog[i]);
                }

                AssertLogsEqual(original.CommandLog, replayed);
                restored.Shutdown();

                if (s + 1 < saveTicks.Length)
                {
                    original.Shutdown();
                    original = StartSaveLoadHost(factions);
                    cursor = 0;
                }
            }

            original.Shutdown();
        }

        private static void FillSharedTables(AiOwnedState owned, int seatId, int battalionId, int tick)
        {
            int node = 10000;
            while (owned.BlacklistCount < AiConstants.DepotNodeBlacklistCapacity)
            {
                owned.BlacklistDepotNode(
                    seatId,
                    node,
                    tick,
                    AiDepotBlacklistKind.Transient,
                    1,
                    0);
                node++;
            }

            int target = 20000;
            while (owned.BackoffCount < AiConstants.OrderBackoffCapacity)
            {
                owned.NoteOrderReject(seatId, battalionId, CommandKind.Move, target, tick);
                target++;
            }

            AiFactionState faction = owned.GetFaction(seatId);
            int sequence = 1;
            while (CountIntents(faction, AiIssuedKind.Move) < AiConstants.MoveIntentCapacity)
            {
                owned.EnqueueIntent(seatId, new AiPendingIntent
                {
                    Sequence = sequence,
                    Kind = AiIssuedKind.Move,
                    BattalionId = battalionId,
                    TargetId = sequence,
                    IssuedTick = tick
                });
                sequence++;
            }

            while (faction.IntentCount < faction.Intents.Length)
            {
                owned.EnqueueIntent(seatId, new AiPendingIntent
                {
                    Sequence = sequence,
                    Kind = AiIssuedKind.ClaimTown,
                    BattalionId = battalionId,
                    TargetId = sequence,
                    IssuedTick = tick
                });
                sequence++;
            }

            int town = 1;
            while (owned.ClaimTownCount < AiConstants.OrderBackoffCapacity)
            {
                owned.NoteAcceptedClaimTown(seatId, town);
                town++;
            }
        }

        private static int CountIntents(AiFactionState faction, AiIssuedKind kind)
        {
            int count = 0;
            for (int i = 0; i < faction.IntentCount; i++)
            {
                if (faction.Intents[i].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static int IntentTarget(AiFactionState faction, int sequence)
        {
            for (int i = 0; i < faction.IntentCount; i++)
            {
                if (faction.Intents[i].Sequence == sequence)
                {
                    return faction.Intents[i].TargetId;
                }
            }

            return 0;
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

        private static bool TryBattalion(AiMatchHost host, int battalionId, out SimBattalion battalion)
        {
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                if (battalions[i].Id == battalionId)
                {
                    battalion = battalions[i];
                    return true;
                }
            }

            battalion = default;
            return false;
        }

        private static int ResolveUnreachableRoadTarget(
            AiMatchHost host,
            int startXCm,
            int startYCm,
            out int destXCm,
            out int destYCm)
        {
            RoadGraph roads = host.Ai.Map.Roads;
            int destNode = FirstUnreachableRoadNode(roads, NearestRoadNodeId(roads, startXCm, startYCm));
            if (destNode == 0)
            {
                roads = BindDisconnectedRoadFixture(host, startXCm, startYCm);
                destNode = FirstUnreachableRoadNode(roads, NearestRoadNodeId(roads, startXCm, startYCm));
            }

            Assert.True(destNode != 0);
            Assert.True(roads.TryGetNodeIndex(destNode, out int destIndex));
            destXCm = roads.GetNodeXCm(destIndex);
            destYCm = roads.GetNodeYCm(destIndex);
            return destNode;
        }

        private static RoadGraph BindDisconnectedRoadFixture(AiMatchHost host, int startXCm, int startYCm)
        {
            byte[] blob = new RoadGraphBuilder()
                .AddNode(1, startXCm, startYCm)
                .AddNode(2, startXCm + 200000, startYCm)
                .ToBlob();
            byte[] terrain = new TerrainGridBuilder(2, 2, 10000).Fill(0, LandCover.Field).ToBlob();
            MovementService movement = MovementService.Create(
                blob,
                terrain,
                MovementTables.Default(),
                MovementServiceConfig.Default);
            RoadGraph roads = Wrmb.ReadRoadGraph(blob);
            host.World.BindRoads(movement, roads);
            LoggingCommandSink logging = (LoggingCommandSink)host.Ai.Commands;
            logging.BindRoads(roads);
            return roads;
        }

        private static int NearestRoadNodeId(RoadGraph roads, int xCm, int yCm)
        {
            if (roads.NodeCount < 1)
            {
                return 0;
            }

            int bestIndex = 0;
            int bestId = roads.GetNodeId(0);
            long bestDist = long.MaxValue;
            for (int i = 0; i < roads.NodeCount; i++)
            {
                long dx = (long)roads.GetNodeXCm(i) - xCm;
                long dy = (long)roads.GetNodeYCm(i) - yCm;
                long dist = (dx * dx) + (dy * dy);
                int id = roads.GetNodeId(i);
                if (dist < bestDist || (dist == bestDist && id < bestId))
                {
                    bestDist = dist;
                    bestId = id;
                    bestIndex = i;
                }
            }

            return roads.GetNodeId(bestIndex);
        }

        private static int FirstUnreachableRoadNode(RoadGraph roads, int startNodeId)
        {
            if (startNodeId == 0 || !roads.TryGetNodeIndex(startNodeId, out int startIndex))
            {
                return 0;
            }

            bool[] reached = new bool[roads.NodeCount];
            int[] queue = new int[roads.NodeCount];
            int head = 0;
            int tail = 0;
            reached[startIndex] = true;
            queue[tail++] = startIndex;
            while (head < tail)
            {
                int index = queue[head++];
                int adjStart = roads.AdjOffset[index];
                int adjEnd = roads.AdjOffset[index + 1];
                for (int edge = adjStart; edge < adjEnd; edge++)
                {
                    if (roads.IsCut(roads.AdjEdgeIndex[edge]))
                    {
                        continue;
                    }

                    int neighbor = roads.AdjNeighborIndex[edge];
                    if (reached[neighbor])
                    {
                        continue;
                    }

                    reached[neighbor] = true;
                    queue[tail++] = neighbor;
                }
            }

            int bestId = 0;
            for (int i = 0; i < roads.NodeCount; i++)
            {
                if (reached[i])
                {
                    continue;
                }

                int id = roads.GetNodeId(i);
                if (bestId == 0 || id < bestId)
                {
                    bestId = id;
                }
            }

            return bestId;
        }

        private static int MoveIntentTarget(AiMatchHost host, int seatId, int battalionId)
        {
            AiFactionState faction = host.Ai.Owned.GetFaction(seatId);
            for (int i = 0; i < faction.IntentCount; i++)
            {
                AiPendingIntent intent = faction.Intents[i];
                if (intent.Kind == AiIssuedKind.Move && intent.BattalionId == battalionId)
                {
                    return intent.TargetId;
                }
            }

            return 0;
        }

        private static int StepUntilBackoff(
            AiMatchHost host,
            int seatId,
            int battalionId,
            int destNode,
            int streak)
        {
            for (int i = 0; i < 8; i++)
            {
                QuietAi(host);
                int at = host.Tick;
                host.Step();
                if (host.Ai.Owned.BackoffCount == 0)
                {
                    continue;
                }

                AiOrderBackoffEntry row = BackoffOf(
                    host.Ai.Owned,
                    seatId,
                    battalionId,
                    CommandKind.Move,
                    destNode);
                if (row.Streak >= streak)
                {
                    return at;
                }
            }

            throw new InvalidOperationException("NoPath Move never reached streak " + streak + ".");
        }

        private static int CountMoves(AiMatchHost host, int battalionId)
        {
            int count = 0;
            IReadOnlyList<SimCommand> log = host.CommandLog;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind == CommandKind.Move && log[i].BattalionId == battalionId)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool TryLastWaypoint(SimBattalion battalion, out int xCm, out int yCm)
        {
            xCm = 0;
            yCm = 0;
            if (battalion.PathCount > 0 && battalion.PathXCm != null && battalion.PathYCm != null)
            {
                int last = battalion.PathCount - 1;
                if (last < battalion.PathXCm.Length && last < battalion.PathYCm.Length)
                {
                    xCm = battalion.PathXCm[last];
                    yCm = battalion.PathYCm[last];
                    return true;
                }
            }

            if (battalion.HasTarget)
            {
                xCm = battalion.TargetXCm;
                yCm = battalion.TargetYCm;
                return true;
            }

            return false;
        }

        private static bool HasMoveTo(
            AiMatchHost host,
            int battalionId,
            int xCm,
            int yCm,
            int issuedOnOrAfter)
        {
            IReadOnlyList<SimCommand> log = host.CommandLog;
            for (int i = 0; i < log.Count; i++)
            {
                SimCommand command = log[i];
                if (command.Kind == CommandKind.Move
                    && command.BattalionId == battalionId
                    && command.Arg0 == xCm
                    && command.Arg1 == yCm
                    && command.IssuedTick >= issuedOnOrAfter)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyAcceptedClaimTown(AiMatchHost host)
        {
            for (int i = 0; i < host.Ai.Owned.ClaimTownCount; i++)
            {
                if (host.Ai.Owned.ClaimTownAt(i).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static SimCommand[] CopyCommands(IReadOnlyList<SimCommand> log)
        {
            SimCommand[] copy = new SimCommand[log.Count];
            for (int i = 0; i < log.Count; i++)
            {
                copy[i] = log[i];
            }

            return copy;
        }

        private static void AssertLogsEqual(IReadOnlyList<SimCommand> left, IReadOnlyList<SimCommand> right)
        {
            Assert.Equal(left.Count, right.Count);
            for (int i = 0; i < left.Count; i++)
            {
                Assert.Equal(left[i].Kind, right[i].Kind);
                Assert.Equal(left[i].IssuerId, right[i].IssuerId);
                Assert.Equal(left[i].BattalionId, right[i].BattalionId);
                Assert.Equal(left[i].Arg0, right[i].Arg0);
                Assert.Equal(left[i].Arg1, right[i].Arg1);
                Assert.Equal(left[i].Arg2, right[i].Arg2);
                Assert.Equal(left[i].IssuedTick, right[i].IssuedTick);
            }
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
                for (int i = 0; i < 22; i++)
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
            for (int i = 0; i < 11; i++)
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
                reader.ReadInt32();
            }

            reader.ReadInt32();
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
