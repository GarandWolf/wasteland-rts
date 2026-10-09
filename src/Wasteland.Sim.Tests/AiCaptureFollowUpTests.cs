using System;
using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    /// <summary>
    /// AI capture follow-up. Two Gorge AI runs must match through a ClaimTown,
    /// a BuildDepot reserve, a reserved-site refund, and a mid-run Save/ReplayTo.
    /// Each run must have a non-empty command log, a Move from every AI seat, a
    /// RaiseBattalion in the log, and at least one ClaimTown and one BuildDepot.
    /// Remnant's authored prize group does not raise in this window; Compact does.
    /// Lead's 4000-tick hash/event identity belongs in DeterminismTests; scripted
    /// capture belongs in GorgeCaptureDeterminismTests. This file does not add
    /// to either.
    /// </summary>
    public sealed class AiCaptureFollowUpTests
    {
        [Fact]
        public void GorgeAi_ClaimDepotAndLoss_IsDeterministicAndSurvivesSaveLoad()
        {
            MatchConfig match = MatchConfig.Default(AiVsAiGorgeTests.MatchSeed);
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB };
            AiBalanceTables balance = BalanceLoad.Tables();

            AiMatchHost first = AiMatchHost.Start(match, map, factions, AiConstants.DifficultyNormal, balance);
            AiMatchHost second = AiMatchHost.Start(match, map, factions, AiConstants.DifficultyNormal, balance);
            bool claimed = false;
            bool depotBuilt = false;
            bool depotLost = false;
            List<ulong> firstHashes = new List<ulong>();
            List<ulong> secondHashes = new List<ulong>();
            List<int> firstEventKinds = new List<int>();
            List<int> secondEventKinds = new List<int>();

            for (int i = 0; i < AiConstants.M1DeterminismTicks; i++)
            {
                if (!depotLost && HasReservedBuilder(first))
                {
                    ForceDepotLoss(first);
                    ForceDepotLoss(second);
                }

                first.Step();
                second.Step();
                firstHashes.Add(first.HashWorld());
                secondHashes.Add(second.HashWorld());
                FingerprintEvents(first.World, firstEventKinds);
                FingerprintEvents(second.World, secondEventKinds);
                Assert.Equal(firstHashes[i], secondHashes[i]);
                Assert.Equal(firstEventKinds.Count, secondEventKinds.Count);
                Assert.Equal(firstEventKinds[firstEventKinds.Count - 1], secondEventKinds[secondEventKinds.Count - 1]);

                if (!claimed)
                {
                    claimed = SawClaim(first);
                }

                if (!depotBuilt
                    && (HasKind(first.World, SimEventKind.DepotStarted) || HasReservedDepot(first)))
                {
                    depotBuilt = true;
                }

                if (!depotLost)
                {
                    depotLost = HasKind(first.World, SimEventKind.OrderRefunded)
                        || HasKind(first.World, SimEventKind.DepotDestroyed);
                }
            }

            Assert.True(claimed, "Gorge AI never claimed a town.");
            Assert.True(depotBuilt, "Gorge AI never reserved a depot.");
            Assert.True(depotLost, "Gorge AI never lost a depot (refund or destroy).");
            AssertAiActed(first, factions);
            AssertAiActed(second, factions);
            Assert.Equal(firstHashes, secondHashes);
            Assert.Equal(firstEventKinds, secondEventKinds);

            first.Shutdown();
            second.Shutdown();

            AiMatchHost original = AiMatchHost.Start(match, map, factions, AiConstants.DifficultyNormal, balance);
            int mid = AiConstants.SaveLoadMidRunTick;
            original.Run(mid);
            ulong atSave = original.HashWorld();
            byte[] blob = original.Ai.Save();
            SimCommand[] log = CopyLog(original.CommandLog);
            AiMatchHost restored = AiMatchHost.ReplayTo(
                match,
                map,
                factions,
                AiConstants.DifficultyNormal,
                balance,
                log,
                blob,
                mid);
            Assert.Equal(original.Tick, restored.Tick);
            Assert.Equal(atSave, restored.HashWorld());
            int remain = AiConstants.M1DeterminismTicks - mid;
            original.Run(remain);
            restored.Run(remain);
            Assert.Equal(original.HashWorld(), restored.HashWorld());
            AssertAiActed(original, factions);
            restored.Shutdown();
            original.Shutdown();
        }

        [Fact]
        public void PendingIntent_ClearsOnCommandRejectedKey()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(AiVsAiGorgeTests.MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            AiFactionState remnant = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionA);
            int before = remnant.IntentCount;
            int battalionId = 11;
            if (host.World.BattalionCount > 0)
            {
                battalionId = host.World.Battalions[0].Id;
            }

            SimCommand stamped = host.World.Submit(
                SimCommand.ClaimTown(host.Tick, remnant.FactionId, battalionId, 0));
            remnant.EnqueueIntent(new AiPendingIntent
            {
                Sequence = stamped.Sequence,
                Kind = AiIssuedKind.ClaimTown,
                BattalionId = battalionId,
                TargetId = 0
            });
            Assert.True(remnant.IntentCount > before);
            host.Run(host.World.CommandDelayTicks + 2);
            bool stillPending = false;
            for (int i = 0; i < remnant.IntentCount; i++)
            {
                if (remnant.Intents[i].Sequence == stamped.Sequence)
                {
                    stillPending = true;
                }
            }

            Assert.False(stillPending);
            host.Shutdown();
        }

        [Fact]
        public void PendingClaimTown_DropsAfterNoProgressTicks()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(AiVsAiGorgeTests.MatchSeed),
                AiSeamsTests.LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            AiFactionState remnant = host.Ai.Owned.GetFaction(AiConstants.DefaultAiFactionA);
            remnant.EnqueueIntent(new AiPendingIntent
            {
                Sequence = 77,
                Kind = AiIssuedKind.ClaimTown,
                BattalionId = 11,
                TargetId = host.Ai.Map.TheDallesTownId,
                IssuedTick = host.Tick - AiConstants.ClaimTownNoProgressTicks
            });
            Assert.True(remnant.IntentCount > 0);
            host.Run(1);
            bool stillPending = false;
            for (int i = 0; i < remnant.IntentCount; i++)
            {
                if (remnant.Intents[i].Sequence == 77)
                {
                    stillPending = true;
                }
            }

            Assert.False(stillPending);
            host.Shutdown();
        }

        [Fact]
        public void GorgeAi_RealBuildings_DoesNotSpamRejectsOrMoves()
        {
            AssertOrderSpamGuard(
                AiConstants.DepotRejectSpamTicks,
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB });
        }

        [Fact]
        public void GorgeAi_RealBuildings_HumanVsAi_DoesNotSpamRejectsOrMoves()
        {
            AssertOrderSpamGuard(
                AiConstants.DepotRejectSpamTicks,
                new[] { AiConstants.DefaultAiFactionB });
        }

        private static void AssertOrderSpamGuard(int ticks, int[] factions)
        {
            MatchConfig match = MatchConfig.Default(AiVsAiGorgeTests.MatchSeed);
            byte[] map = AiSeamsTests.LoadGorgeV1Bytes();
            AiMatchHost host = AiMatchHost.Start(
                match,
                map,
                factions,
                AiConstants.DifficultyNormal,
                BalanceLoad.Tables());
            int remnantId = AiConstants.DefaultAiFactionA;
            int compactId = AiConstants.DefaultAiFactionB;
            int depotNode = 0;
            int chosenSite = 0;
            int depotX = 0;
            int depotY = 0;
            bool haveDepotNode = false;

            int dallesIndex = host.Ai.Map.IndexOfTown(host.Ai.Map.TheDallesTownId);
            AiTownRecord dalles = host.Ai.Map.TownAt(dallesIndex);
            int hoodX = 0;
            int hoodY = 0;
            if (host.Ai.Map.TryGetTown(host.Ai.Map.HoodRiverTownId, out AiTownRecord hood))
            {
                hoodX = hood.XCm;
                hoodY = hood.YCm;
            }

            long dallesRadiusSq = 0;
            if (host.World.TryGetTown(dalles.Id, out TownView dallesView))
            {
                dallesRadiusSq = (long)dallesView.ZoneRadiusCm * dallesView.ZoneRadiusCm;
            }

            long remnantSpawnDistSq = MinFactionDistSq(host, remnantId, dalles.XCm, dalles.YCm);
            long compactSpawnDistSq = MinFactionDistSq(host, compactId, dalles.XCm, dalles.YCm);
            bool remnantLeft = false;
            bool compactLeft = false;
            bool remnantArrived = false;
            bool compactArrived = false;
            int remnantLeave = -1;
            int compactLeave = -1;
            int remnantArrive = -1;
            int compactArrive = -1;
            int remnantEngineerId = 0;
            int remnantMoveTick = -1;
            int remnantSpeed = -1;
            int remnantPathCm = -1;
            int remnantWithinTick = -1;
            int remnantPathCount = -1;
            int spawnX = 0;
            int spawnY = 0;
            int nearestNode = 0;
            int nearestDistCm = -1;
            int firstWpX = 0;
            int firstWpY = 0;
            int firstWpNode = 0;
            bool remnantDepotStarted = false;
            bool haveSpawn = TryRemnantEngineerSpawn(host, remnantId, out spawnX, out spawnY);
            if (haveSpawn)
            {
                nearestNode = NearestRoadNode(host, spawnX, spawnY, out nearestDistCm);
            }

            List<int> compactRaises = new List<int>();
            int eventCursor = 0;
            int logCursor = 0;

            for (int step = 0; step < ticks; step++)
            {
                host.Step();
                int tick = host.Tick - 1;
                if (tick < 0)
                {
                    tick = 0;
                }

                IReadOnlyList<SimCommand> liveLog = host.CommandLog;
                for (int i = logCursor; i < liveLog.Count; i++)
                {
                    SimCommand command = liveLog[i];
                    if (command.Kind == CommandKind.BuildDepot
                        && command.IssuerId == remnantId
                        && remnantEngineerId == 0)
                    {
                        remnantEngineerId = command.BattalionId;
                        chosenSite = command.Arg0;
                        if (!remnantDepotStarted)
                        {
                            BindDepotNode(host, chosenSite, ref depotNode, ref depotX, ref depotY, ref haveDepotNode);
                        }
                    }

                    if (command.Kind == CommandKind.Move
                        && remnantEngineerId != 0
                        && command.BattalionId == remnantEngineerId
                        && remnantMoveTick < 0)
                    {
                        remnantMoveTick = command.IssuedTick;
                    }
                }

                logCursor = liveLog.Count;

                IReadOnlyList<SimEvent> liveEvents = host.World.Events;
                for (int i = eventCursor; i < liveEvents.Count; i++)
                {
                    SimEvent ev = liveEvents[i];
                    if (ev.Kind == SimEventKind.DepotStarted
                        && ev.AuxId == remnantId
                        && !remnantDepotStarted)
                    {
                        remnantDepotStarted = true;
                        BindDepotNode(host, ev.Param0, ref depotNode, ref depotX, ref depotY, ref haveDepotNode);
                    }

                    if (ev.Kind != SimEventKind.BattalionSpawned)
                    {
                        continue;
                    }

                    if (host.World.TryGetBattalionView(ev.SubjectId, out BattalionView spawned)
                        && spawned.OwnerId == compactId
                        && ev.Tick > 0)
                    {
                        compactRaises.Add(ev.Tick);
                    }
                }

                eventCursor = liveEvents.Count;

                if (remnantEngineerId != 0
                    && TryBattalion(host, remnantEngineerId, out SimBattalion engineer))
                {
                    if (remnantSpeed < 0)
                    {
                        remnantSpeed = engineer.SpeedCmPerSecond;
                    }

                    if (remnantPathCm < 0 && engineer.PathCount > 1 && engineer.PathXCm != null && engineer.PathYCm != null)
                    {
                        remnantPathCount = engineer.PathCount;
                        remnantPathCm = PathLengthCm(engineer);
                        firstWpX = engineer.PathXCm[0];
                        firstWpY = engineer.PathYCm[0];
                        firstWpNode = NearestRoadNode(host, firstWpX, firstWpY, out _);
                    }

                    if (haveDepotNode && remnantWithinTick < 0)
                    {
                        long dx = (long)engineer.PositionXCm - depotX;
                        long dy = (long)engineer.PositionYCm - depotY;
                        if ((dx * dx) + (dy * dy)
                            <= (long)AiConstants.NodeControlRadiusCm * AiConstants.NodeControlRadiusCm)
                        {
                            remnantWithinTick = tick;
                        }
                    }
                }

                if (!compactLeft && MinFactionDistSq(host, compactId, dalles.XCm, dalles.YCm) < compactSpawnDistSq)
                {
                    compactLeft = true;
                    compactLeave = tick;
                }

                if (!remnantLeft && MinFactionDistSq(host, remnantId, dalles.XCm, dalles.YCm) < remnantSpawnDistSq)
                {
                    remnantLeft = true;
                    remnantLeave = tick;
                }

                if (!compactArrived && FactionInRadius(host, compactId, dalles.XCm, dalles.YCm, dallesRadiusSq))
                {
                    compactArrived = true;
                    compactArrive = tick;
                }

                if (!remnantArrived && FactionInRadius(host, remnantId, dalles.XCm, dalles.YCm, dallesRadiusSq))
                {
                    remnantArrived = true;
                    remnantArrive = tick;
                }
            }

            bool remnantIsAi = IndexOfId(factions, remnantId) >= 0;
            if (remnantIsAi)
            {
                Assert.True(
                    depotNode != 0,
                    "Remnant never started a depot and never chose a BuildDepot site.");
            }

            int delay = host.World.CommandDelayTicks;
            IReadOnlyList<SimEvent> events = host.World.Events;
            IReadOnlyList<SimCommand> log = host.CommandLog;
            if (remnantEngineerId != 0 && remnantMoveTick < 0)
            {
                for (int i = 0; i < log.Count; i++)
                {
                    if (log[i].Kind == CommandKind.Move && log[i].BattalionId == remnantEngineerId)
                    {
                        remnantMoveTick = log[i].IssuedTick;
                        break;
                    }
                }
            }
            List<OrderRejectCount> rejects = new List<OrderRejectCount>();
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind != SimEventKind.CommandRejected)
                {
                    continue;
                }

                int issuer = (int)(ev.Key >> 32);
                int battalionId = ev.SubjectId;
                int issuedTick = ev.Tick - delay;
                if (issuedTick < 0)
                {
                    issuedTick = 0;
                }

                CommandKind kind;
                int targetId;
                if (!TryMatchRejectedCommand(log, issuer, battalionId, issuedTick, ev, out kind, out targetId))
                {
                    kind = KindFromRejectReason(ev.RejectReason);
                    targetId = ev.Param0 != 0 ? ev.Param0 : ev.Param1;
                }

                int row = IndexOfOrderReject(rejects, issuer, battalionId, kind, targetId);
                if (row < 0)
                {
                    OrderRejectCount next = default;
                    next.SeatId = issuer;
                    next.BattalionId = battalionId;
                    next.Kind = kind;
                    next.TargetId = targetId;
                    next.Count = 1;
                    next.Reason = ev.RejectReason;
                    rejects.Add(next);
                }
                else
                {
                    OrderRejectCount have = rejects[row];
                    have.Count++;
                    rejects[row] = have;
                }
            }

            for (int i = 0; i < rejects.Count; i++)
            {
                Assert.True(
                    rejects[i].Count <= AiConstants.MaxRejectsPerOrderKey,
                    "seat " + rejects[i].SeatId
                    + " unit " + rejects[i].BattalionId
                    + " " + rejects[i].Kind
                    + " target " + rejects[i].TargetId
                    + " rejected " + rejects[i].Count
                    + " times (" + rejects[i].Reason + ").");
            }

            List<MoveCount> moves = new List<MoveCount>();
            List<SeatCount> depots = new List<SeatCount>();
            for (int i = 0; i < log.Count; i++)
            {
                SimCommand command = log[i];
                if (command.Kind == CommandKind.Move)
                {
                    int row = IndexOfMove(moves, command.BattalionId);
                    if (row < 0)
                    {
                        MoveCount next = default;
                        next.BattalionId = command.BattalionId;
                        next.Count = 1;
                        moves.Add(next);
                    }
                    else
                    {
                        MoveCount have = moves[row];
                        have.Count++;
                        moves[row] = have;
                    }
                }

                if (command.Kind == CommandKind.BuildDepot)
                {
                    int row = IndexOfSeat(depots, command.IssuerId);
                    if (row < 0)
                    {
                        SeatCount next = default;
                        next.SeatId = command.IssuerId;
                        next.Count = 1;
                        depots.Add(next);
                    }
                    else
                    {
                        SeatCount have = depots[row];
                        have.Count++;
                        depots[row] = have;
                    }
                }
            }

            for (int i = 0; i < moves.Count; i++)
            {
                Assert.True(
                    moves[i].Count <= AiConstants.MaxMovesPerBattalion,
                    "battalion " + moves[i].BattalionId
                    + " received " + moves[i].Count + " Move commands.");
            }

            for (int i = 0; i < depots.Count; i++)
            {
                Assert.True(
                    depots[i].Count <= AiConstants.MaxBuildDepotsPerSeat,
                    "seat " + depots[i].SeatId
                    + " issued " + depots[i].Count + " BuildDepot commands.");
            }

            Assert.Equal(0, host.Ai.DroppedFromLog);

            int claimTowns = 0;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind == CommandKind.ClaimTown)
                {
                    claimTowns++;
                }
            }

            EnforceClaimTownCap(host, log, events, delay);

            int maxMoves = 0;
            int maxMoveBn = 0;
            for (int i = 0; i < moves.Count; i++)
            {
                if (moves[i].Count > maxMoves)
                {
                    maxMoves = moves[i].Count;
                    maxMoveBn = moves[i].BattalionId;
                }
            }

            int[] rejectReasons = new int[32];
            int rejectTotal = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind != SimEventKind.CommandRejected)
                {
                    continue;
                }

                rejectTotal++;
                int reason = (int)events[i].RejectReason;
                if (reason >= 0 && reason < rejectReasons.Length)
                {
                    rejectReasons[reason]++;
                }
            }

            int remnantIntents = remnantIsAi ? host.Ai.Owned.GetFaction(remnantId).IntentCount : 0;
            int compactIntents = host.Ai.Owned.GetFaction(compactId).IntentCount;
            int dallesMargin = -1;
            if (remnantArrive >= 0 && compactArrive >= 0)
            {
                dallesMargin = remnantArrive - compactArrive;
            }

            Console.WriteLine("B4 ticks=" + ticks
                + " seats=" + factions.Length
                + " schema=" + AiConstants.StateSchemaVersion
                + " commands=" + log.Count
                + " rejects=" + rejectTotal
                + " blacklist=" + host.Ai.Owned.BlacklistCount
                + "/" + AiConstants.DepotNodeBlacklistCapacity
                + " backoff=" + host.Ai.Owned.BackoffCount
                + "/" + AiConstants.OrderBackoffCapacity
                + " remnantIntents=" + remnantIntents
                + " compactIntents=" + compactIntents
                + " remnantLeave=" + remnantLeave
                + " compactLeave=" + compactLeave
                + " remnantArrive=" + remnantArrive
                + " compactArrive=" + compactArrive
                + " dallesMargin=" + dallesMargin)
                + " claimTowns=" + claimTowns
                + " maxMoves bn=" + maxMoveBn + " n=" + maxMoves
                + " remnantEngineer=" + remnantEngineerId
                + " moveTick=" + remnantMoveTick
                + " speed=" + remnantSpeed
                + " pathCm=" + remnantPathCm
                + " pathCount=" + remnantPathCount
                + " depotNode=" + depotNode
                + " spawn=" + spawnX + "," + spawnY
                + " nearestNode=" + nearestNode
                + " nearestDistCm=" + nearestDistCm
                + " firstWp=" + firstWpX + "," + firstWpY
                + " firstWpNode=" + firstWpNode
                + " firstLegOffRoad=" + (nearestDistCm > 0)
                + " withinDepot=" + remnantWithinTick);
            for (int i = 0; i < rejectReasons.Length; i++)
            {
                if (rejectReasons[i] == 0)
                {
                    continue;
                }

                Console.WriteLine("rejectReason " + (CommandRejectReason)i + " n=" + rejectReasons[i]);
            }
            for (int i = 0; i < rejects.Count; i++)
            {
                Console.WriteLine(
                    "reject seat=" + rejects[i].SeatId
                    + " unit=" + rejects[i].BattalionId
                    + " kind=" + rejects[i].Kind
                    + " target=" + rejects[i].TargetId
                    + " n=" + rejects[i].Count
                    + " reason=" + rejects[i].Reason);
            }

            for (int i = 0; i < depots.Count; i++)
            {
                Console.WriteLine("buildDepot seat=" + depots[i].SeatId + " n=" + depots[i].Count);
            }

            Console.Write("compactRaises=");
            for (int i = 0; i < compactRaises.Count; i++)
            {
                if (i > 0)
                {
                    Console.Write(",");
                }

                Console.Write(compactRaises[i]);
            }

            Console.WriteLine();
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind != SimEventKind.TownOwnerChanged)
                {
                    continue;
                }

                Console.WriteLine(
                    "TownOwnerChanged tick=" + ev.Tick
                    + " town=" + ev.SubjectId
                    + " name=" + TownNameOf(host, ev.SubjectId)
                    + " old=" + ev.Param0
                    + " new=" + ev.AuxId);
            }

            for (int i = 0; i < log.Count; i++)
            {
                SimCommand command = log[i];
                if (command.Kind != CommandKind.ClaimTown)
                {
                    continue;
                }

                Console.WriteLine(
                    "ClaimTown tick=" + command.IssuedTick
                    + " seat=" + command.IssuerId
                    + " battalion=" + command.BattalionId
                    + " town=" + command.Arg0
                    + " name=" + TownNameOf(host, command.Arg0)
                    + " "
                    + (ClaimWasRejected(events, command, delay) ? "rejected" : "accepted"));
            }

            SnapshotCompactStops(host, compactId, dalles.XCm, dalles.YCm, hoodX, hoodY);
            host.Shutdown();
        }

        private static void SnapshotCompactStops(
            AiMatchHost host,
            int compactId,
            int dallesX,
            int dallesY,
            int hoodX,
            int hoodY)
        {
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            int[] watch = { 6, 8, 7, 9, 13, 14 };
            for (int w = 0; w < watch.Length; w++)
            {
                int id = watch[w];
                for (int i = 0; i < battalions.Count; i++)
                {
                    SimBattalion battalion = battalions[i];
                    if (battalion.Id != id || battalion.OwnerId != compactId)
                    {
                        continue;
                    }

                    string unitId = string.Empty;
                    if (host.World.TryGetBattalionView(id, out BattalionView view))
                    {
                        unitId = view.UnitId;
                    }

                    Console.WriteLine(
                        "compactBn=" + id
                        + " unit=" + unitId
                        + " hasTarget=" + battalion.HasTarget
                        + " pathCount=" + battalion.PathCount
                        + " pathIndex=" + battalion.PathIndex
                        + " distDalles=" + DistCm(battalion.PositionXCm, battalion.PositionYCm, dallesX, dallesY)
                        + " distHood=" + DistCm(battalion.PositionXCm, battalion.PositionYCm, hoodX, hoodY)
                        + " x=" + battalion.PositionXCm
                        + " y=" + battalion.PositionYCm);
                }
            }

            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != compactId)
                {
                    continue;
                }

                string unitId = string.Empty;
                if (host.World.TryGetBattalionView(battalion.Id, out BattalionView view))
                {
                    unitId = view.UnitId;
                }

                Console.WriteLine(
                    "compactAll bn=" + battalion.Id
                    + " unit=" + unitId
                    + " hasTarget=" + battalion.HasTarget
                    + " pathCount=" + battalion.PathCount
                    + " distDalles=" + DistCm(battalion.PositionXCm, battalion.PositionYCm, dallesX, dallesY)
                    + " distHood=" + DistCm(battalion.PositionXCm, battalion.PositionYCm, hoodX, hoodY));
            }
        }

        private static void EnforceClaimTownCap(
            AiMatchHost host,
            IReadOnlyList<SimCommand> log,
            IReadOnlyList<SimEvent> events,
            int delay)
        {
            List<ClaimCapRow> rows = new List<ClaimCapRow>();
            int commandIndex = 0;
            int eventIndex = 0;
            while (commandIndex < log.Count || eventIndex < events.Count)
            {
                int commandTick = commandIndex < log.Count ? log[commandIndex].IssuedTick : int.MaxValue;
                int eventTick = eventIndex < events.Count ? events[eventIndex].Tick : int.MaxValue;
                bool takeCommand = commandTick < eventTick
                    || (commandTick == eventTick && commandIndex < log.Count);
                if (takeCommand)
                {
                    SimCommand command = log[commandIndex++];
                    if (command.Kind != CommandKind.ClaimTown
                        || ClaimWasRejected(events, command, delay))
                    {
                        continue;
                    }

                    int row = IndexOfClaimCap(rows, command.IssuerId, command.Arg0);
                    if (row < 0)
                    {
                        ClaimCapRow next = default;
                        next.SeatId = command.IssuerId;
                        next.TownId = command.Arg0;
                        next.Count = 1;
                        rows.Add(next);
                    }
                    else
                    {
                        ClaimCapRow have = rows[row];
                        have.Count++;
                        rows[row] = have;
                    }

                    Assert.True(
                        rows[row < 0 ? rows.Count - 1 : row].Count
                        <= AiConstants.MaxClaimTownsPerSeatTown,
                        "seat " + command.IssuerId
                        + " town " + command.Arg0
                        + " accepted "
                        + (row < 0 ? rows[rows.Count - 1].Count : rows[row].Count)
                        + " ClaimTowns in one ownership period.");
                    continue;
                }

                SimEvent ev = events[eventIndex++];
                if (ev.Kind != SimEventKind.TownOwnerChanged)
                {
                    continue;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].TownId == ev.SubjectId)
                    {
                        ClaimCapRow have = rows[i];
                        have.Count = 0;
                        rows[i] = have;
                    }
                }
            }
        }

        private static int IndexOfClaimCap(List<ClaimCapRow> rows, int seatId, int townId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].SeatId == seatId && rows[i].TownId == townId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool ClaimWasRejected(
            IReadOnlyList<SimEvent> events,
            SimCommand command,
            int delay)
        {
            for (int i = 0; i < events.Count; i++)
            {
                SimEvent ev = events[i];
                if (ev.Kind != SimEventKind.CommandRejected
                    || ev.SubjectId != command.BattalionId)
                {
                    continue;
                }

                int issuer = (int)(ev.Key >> 32);
                if (issuer != command.IssuerId)
                {
                    continue;
                }

                int issuedTick = ev.Tick - delay;
                int delta = issuedTick - command.IssuedTick;
                if (delta < 0)
                {
                    delta = -delta;
                }

                if (delta <= 2 && KindFromRejectReason(ev.RejectReason) == CommandKind.ClaimTown)
                {
                    return true;
                }
            }

            return false;
        }

        private static string TownNameOf(AiMatchHost host, int townId)
        {
            if (townId == host.Ai.Map.TheDallesTownId)
            {
                return AiConstants.TheDallesTownName;
            }

            if (townId == host.Ai.Map.GoldendaleTownId)
            {
                return AiConstants.GoldendaleTownName;
            }

            if (townId == host.Ai.Map.HoodRiverTownId)
            {
                return AiConstants.HoodRiverTownName;
            }

            if (townId == host.Ai.Map.WhiteSalmonTownId)
            {
                return AiConstants.WhiteSalmonTownName;
            }

            if (townId == host.Ai.Map.KlickitatTownId)
            {
                return AiConstants.KlickitatTownName;
            }

            if (townId == 5)
            {
                return "Condon";
            }

            if (townId == 13)
            {
                return "Wasco";
            }

            if (townId == 20)
            {
                return "Cascade Locks";
            }

            if (townId == 29)
            {
                return "Carson";
            }

            return "town-" + townId;
        }

        private struct ClaimCapRow
        {
            public int SeatId;
            public int TownId;
            public int Count;
        }

        private static void BindDepotNode(
            AiMatchHost host,
            int nodeId,
            ref int depotNode,
            ref int depotX,
            ref int depotY,
            ref bool haveDepotNode)
        {
            depotNode = nodeId;
            haveDepotNode = host.Ai.Map.Roads.TryGetNodeIndex(nodeId, out int depotIndex);
            if (haveDepotNode)
            {
                depotX = host.Ai.Map.Roads.GetNodeXCm(depotIndex);
                depotY = host.Ai.Map.Roads.GetNodeYCm(depotIndex);
            }
        }

        private static bool TryRemnantEngineerSpawn(AiMatchHost host, int remnantId, out int xCm, out int yCm)
        {
            xCm = 0;
            yCm = 0;
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != remnantId)
                {
                    continue;
                }

                if (host.World.TryGetBattalionView(battalion.Id, out BattalionView view)
                    && view.UnitId == "remnant_engineers")
                {
                    xCm = battalion.PositionXCm;
                    yCm = battalion.PositionYCm;
                    return true;
                }
            }

            return false;
        }

        private static int NearestRoadNode(AiMatchHost host, int xCm, int yCm, out int distCm)
        {
            distCm = -1;
            int bestId = 0;
            int bestX = 0;
            int bestY = 0;
            long bestDistSq = long.MaxValue;
            int nodeCount = host.Ai.Map.Roads.NodeCount;
            for (int i = 0; i < nodeCount; i++)
            {
                int nx = host.Ai.Map.Roads.GetNodeXCm(i);
                int ny = host.Ai.Map.Roads.GetNodeYCm(i);
                long dx = (long)nx - xCm;
                long dy = (long)ny - yCm;
                long distSq = (dx * dx) + (dy * dy);
                int id = host.Ai.Map.Roads.GetNodeId(i);
                if (distSq < bestDistSq || (distSq == bestDistSq && id < bestId))
                {
                    bestDistSq = distSq;
                    bestId = id;
                    bestX = nx;
                    bestY = ny;
                }
            }

            if (bestId != 0)
            {
                distCm = DistCm(xCm, yCm, bestX, bestY);
            }

            return bestId;
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

        private static int PathLengthCm(SimBattalion battalion)
        {
            if (battalion.PathXCm == null || battalion.PathYCm == null || battalion.PathCount < 2)
            {
                return 0;
            }

            int sum = 0;
            int n = battalion.PathCount;
            if (n > battalion.PathXCm.Length)
            {
                n = battalion.PathXCm.Length;
            }

            if (n > battalion.PathYCm.Length)
            {
                n = battalion.PathYCm.Length;
            }

            for (int i = 1; i < n; i++)
            {
                sum += DistCm(
                    battalion.PathXCm[i - 1],
                    battalion.PathYCm[i - 1],
                    battalion.PathXCm[i],
                    battalion.PathYCm[i]);
            }

            return sum;
        }

        private static int DistCm(int ax, int ay, int bx, int by)
        {
            long dx = (long)ax - bx;
            long dy = (long)ay - by;
            return (int)global::System.Math.Round(global::System.Math.Sqrt((dx * dx) + (dy * dy)));
        }

        private static long MinFactionDistSq(AiMatchHost host, int factionId, int xCm, int yCm)
        {
            long best = long.MaxValue;
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != factionId)
                {
                    continue;
                }

                long dx = (long)battalion.PositionXCm - xCm;
                long dy = (long)battalion.PositionYCm - yCm;
                long distSq = (dx * dx) + (dy * dy);
                if (distSq < best)
                {
                    best = distSq;
                }
            }

            return best;
        }

        private static bool FactionInRadius(
            AiMatchHost host,
            int factionId,
            int xCm,
            int yCm,
            long radiusSq)
        {
            IReadOnlyList<SimBattalion> battalions = host.World.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                if (battalion.OwnerId != factionId)
                {
                    continue;
                }

                long dx = (long)battalion.PositionXCm - xCm;
                long dy = (long)battalion.PositionYCm - yCm;
                if ((dx * dx) + (dy * dy) <= radiusSq)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryMatchRejectedCommand(
            IReadOnlyList<SimCommand> log,
            int issuer,
            int battalionId,
            int issuedTick,
            SimEvent ev,
            out CommandKind kind,
            out int targetId)
        {
            kind = CommandKind.Move;
            targetId = 0;
            CommandKind expected = KindFromRejectReason(ev.RejectReason);
            int best = -1;
            int bestDelta = int.MaxValue;
            for (int i = 0; i < log.Count; i++)
            {
                SimCommand command = log[i];
                if (command.IssuerId != issuer || command.BattalionId != battalionId)
                {
                    continue;
                }

                if (command.Kind != expected)
                {
                    continue;
                }

                int delta = command.IssuedTick - issuedTick;
                if (delta < 0)
                {
                    delta = -delta;
                }

                if (delta > 2)
                {
                    continue;
                }

                if (delta < bestDelta || (delta == bestDelta && (best < 0 || i < best)))
                {
                    bestDelta = delta;
                    best = i;
                }
            }

            if (best < 0)
            {
                return false;
            }

            kind = log[best].Kind;
            targetId = log[best].Arg0;
            return true;
        }

        private static CommandKind KindFromRejectReason(CommandRejectReason reason)
        {
            if (reason == CommandRejectReason.NotCapturer
                || reason == CommandRejectReason.AlreadyOwned
                || reason == CommandRejectReason.UnknownTown
                || reason == CommandRejectReason.NotSeatTown)
            {
                return CommandKind.ClaimTown;
            }

            if (reason == CommandRejectReason.NotBuilder
                || reason == CommandRejectReason.BuildingNotBuildable
                || reason == CommandRejectReason.UnknownNode
                || reason == CommandRejectReason.TerritoryNotOwned
                || reason == CommandRejectReason.NotConnectedToSupply
                || reason == CommandRejectReason.DepotTooClose
                || reason == CommandRejectReason.NodeHasDepot
                || reason == CommandRejectReason.DepotCapReached)
            {
                return CommandKind.BuildDepot;
            }

            if (reason == CommandRejectReason.UnitNotRaisable
                || reason == CommandRejectReason.TierLocked
                || reason == CommandRejectReason.PopCapReached
                || reason == CommandRejectReason.InsufficientManpower
                || reason == CommandRejectReason.InsufficientMaterials
                || reason == CommandRejectReason.InsufficientFuel)
            {
                return CommandKind.RaiseBattalion;
            }

            return CommandKind.Move;
        }

        private static int IndexOfOrderReject(
            List<OrderRejectCount> rows,
            int seatId,
            int battalionId,
            CommandKind kind,
            int targetId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].SeatId == seatId
                    && rows[i].BattalionId == battalionId
                    && rows[i].Kind == kind
                    && rows[i].TargetId == targetId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOfMove(List<MoveCount> rows, int battalionId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].BattalionId == battalionId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOfSeat(List<SeatCount> rows, int seatId)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].SeatId == seatId)
                {
                    return i;
                }
            }

            return -1;
        }

        private struct OrderRejectCount
        {
            public int SeatId;
            public int BattalionId;
            public CommandKind Kind;
            public int TargetId;
            public int Count;
            public CommandRejectReason Reason;
        }

        private struct MoveCount
        {
            public int BattalionId;
            public int Count;
        }

        private struct SeatCount
        {
            public int SeatId;
            public int Count;
        }

        private static void AssertAiActed(AiMatchHost host, int[] factions)
        {
            IReadOnlyList<SimCommand> log = host.CommandLog;
            Assert.True(log.Count > 0, "AI command log is empty.");
            bool claimed = false;
            bool built = false;
            bool raised = false;
            int seatCount = factions.Length;
            int[] moves = new int[seatCount];
            int[] orders = new int[seatCount];
            int[] seatIds = new int[seatCount];
            for (int s = 0; s < seatCount; s++)
            {
                seatIds[s] = factions[s];
            }

            SortIds(seatIds);
            for (int i = 0; i < log.Count; i++)
            {
                SimCommand command = log[i];
                if (command.Kind == CommandKind.ClaimTown)
                {
                    claimed = true;
                }

                if (command.Kind == CommandKind.BuildDepot)
                {
                    built = true;
                }

                if (command.Kind == CommandKind.RaiseBattalion)
                {
                    raised = true;
                }

                int seat = IndexOfId(seatIds, command.IssuerId);
                if (seat < 0)
                {
                    continue;
                }

                if (command.Kind == CommandKind.RaiseBattalion
                    || command.Kind == CommandKind.ClaimTown
                    || command.Kind == CommandKind.BuildDepot)
                {
                    orders[seat]++;
                }

                if (command.Kind == CommandKind.Move)
                {
                    moves[seat]++;
                }
            }

            Assert.True(claimed, "AI never issued ClaimTown.");
            Assert.True(built, "AI never issued BuildDepot.");
            Assert.True(raised, "AI never issued RaiseBattalion.");
            for (int s = 0; s < seatCount; s++)
            {
                Assert.True(
                    moves[s] > 0,
                    "AI seat " + seatIds[s] + " never issued Move.");
                Assert.True(
                    orders[s] > 0,
                    "AI seat " + seatIds[s] + " never issued RaiseBattalion, ClaimTown, or BuildDepot.");
            }
        }

        private static int IndexOfId(int[] ids, int value)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SortIds(int[] ids)
        {
            for (int i = 1; i < ids.Length; i++)
            {
                int row = ids[i];
                int j = i - 1;
                while (j >= 0 && ids[j] > row)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }

                ids[j + 1] = row;
            }
        }

        private static bool HasReservedBuilder(AiMatchHost host)
        {
            List<DepotView> depots = new List<DepotView>();
            host.World.CopyDepots(depots);
            for (int i = 0; i < depots.Count; i++)
            {
                DepotView depot = depots[i];
                if (depot.State == DepotSiteState.Reserved && depot.BuilderBattalionId != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ForceDepotLoss(AiMatchHost host)
        {
            List<DepotView> depots = new List<DepotView>();
            host.World.CopyDepots(depots);
            for (int i = 0; i < depots.Count; i++)
            {
                DepotView depot = depots[i];
                if (depot.State != DepotSiteState.Reserved || depot.BuilderBattalionId == 0)
                {
                    continue;
                }

                host.World.Submit(SimCommand.Move(
                    host.Tick,
                    depot.OwnerFactionId,
                    depot.BuilderBattalionId,
                    0,
                    0));
                return;
            }
        }

        private static void FingerprintEvents(SimWorld world, List<int> into)
        {
            IReadOnlyList<SimEvent> events = world.Events;
            int mix = events.Count;
            for (int i = 0; i < events.Count; i++)
            {
                mix = (mix * 33) + (int)events[i].Kind;
                mix = (mix * 33) + events[i].SubjectId;
                mix = (mix * 33) + events[i].AuxId;
            }

            into.Add(mix);
        }

        private static bool HasReservedDepot(AiMatchHost host)
        {
            IReadOnlyList<SimCommand> log = host.CommandLog;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind == CommandKind.BuildDepot)
                {
                    return true;
                }
            }

            List<DepotView> depots = new List<DepotView>();
            host.World.CopyDepots(depots);
            return depots.Count > 0;
        }

        private static bool SawClaim(AiMatchHost host)
        {
            if (HasKind(host.World, SimEventKind.TownOwnerChanged)
                || HasKind(host.World, SimEventKind.TownContestStarted))
            {
                return true;
            }

            IReadOnlyList<SimCommand> log = host.CommandLog;
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].Kind == CommandKind.ClaimTown)
                {
                    return true;
                }
            }

            List<TownView> towns = new List<TownView>();
            host.World.CopyTowns(towns);
            for (int i = 0; i < towns.Count; i++)
            {
                TownView town = towns[i];
                if (!town.IsSeatTown)
                {
                    continue;
                }

                if (town.CaptureFactionId != 0 || town.CaptureProgressTicks > 0 || town.Contested)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasKind(SimWorld world, SimEventKind kind)
        {
            IReadOnlyList<SimEvent> events = world.Events;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == kind)
                {
                    return true;
                }
            }

            return false;
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
