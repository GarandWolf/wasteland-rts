using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiSeamsTests
    {
        [Fact]
        public void RoadConnectedAdjacency_TreatsGoldendaleAndHoodRiverAsNeighborsOnV0()
        {
            AiMapSnapshot map = LoadGorgeMap();
            Assert.Equal(AiConstants.RoadConnectedAdjacencySource, map.Adjacency.SourceName);
            Assert.Equal(14, map.TheDallesTownId);
            Assert.True(map.Adjacency.AreNeighbors(11, 6));
            Assert.True(map.Adjacency.AreNeighbors(6, 11));
            Assert.NotEqual(0UL, map.MapKey);

            List<int> neighbors = new List<int>();
            map.Adjacency.CopyNeighbors(11, neighbors);
            Assert.Contains(6, neighbors);
            for (int i = 1; i < neighbors.Count; i++)
            {
                Assert.True(neighbors[i - 1] < neighbors[i]);
            }
        }

        [Fact]
        public void FromWrmb_LoadsV1Gorge()
        {
            string dir = GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName);
            byte[] bytes = GoldenMapFiles.ReadWrmb(
                Path.Combine(dir, GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapBytes);
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(bytes);
            Assert.Equal(14, map.TheDallesTownId);
            Assert.NotEqual(0UL, map.MapKey);
            Assert.Equal(
                MapKey.For(WrmbFile.Parse(bytes), AiConstants.MapKeySectionTags()),
                map.MapKey);
        }

        [Fact]
        public void SaveLoad_RestoresHashedAiOwnedState()
        {
            AiMatchHost host = StartHost();
            host.Run(80);
            ulong before = host.Ai.HashState();
            byte[] blob = host.Ai.Save();

            AiMatchHost restored = StartHost();
            Assert.NotEqual(before, restored.Ai.HashState());
            restored.Ai.Load(blob);
            Assert.Equal(before, restored.Ai.HashState());
            restored.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void Load_RejectsMatchSeedMismatch()
        {
            AiMatchHost host = StartHost();
            host.Run(16);
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            AiMatchHost other = AiMatchHost.Start(
                MatchConfig.Default(0xBEEFUL),
                LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            Assert.Throws<InvalidDataException>(() => other.Ai.Load(blob));
            other.Shutdown();
        }

        [Fact]
        public void Load_RejectsBalanceAndMapHeaderMismatch()
        {
            AiMatchHost host = StartHost();
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            byte[] flippedBalance = (byte[])blob.Clone();
            flippedBalance[12] ^= 1;
            AiMatchHost other = StartHost();
            Assert.Throws<InvalidDataException>(() => other.Ai.Load(flippedBalance));

            byte[] flippedMap = (byte[])blob.Clone();
            flippedMap[20] ^= 1;
            Assert.Throws<InvalidDataException>(() => other.Ai.Load(flippedMap));
            other.Shutdown();
        }

        [Fact]
        public void Load_RejectsConfigIntMismatch()
        {
            AiMatchHost host = StartHost();
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            int offset;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(blob)))
            {
                reader.ReadInt32();
                reader.ReadUInt64();
                reader.ReadUInt64();
                reader.ReadUInt64();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadString();
                reader.ReadString();
                offset = (int)reader.BaseStream.Position;
            }

            byte[] mutated = (byte[])blob.Clone();
            mutated[offset] ^= 1;
            AiMatchHost other = StartHost();
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => other.Ai.Load(mutated));
            Assert.Contains("ReactionExtraTicks", ex.Message);
            other.Shutdown();
        }

        [Fact]
        public void Load_RejectsOutOfRangePosture_AndLeavesHashUnchanged()
        {
            AiMatchHost host = StartHost();
            host.Run(80);
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            int postureOffset = FirstFactionIntOffset(blob) + (8 * sizeof(int));
            byte[] mutated = (byte[])blob.Clone();
            mutated[postureOffset] = (byte)((int)AiPosture.Counterattack + 1);

            AiMatchHost other = StartHost();
            ulong before = other.Ai.HashState();
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => other.Ai.Load(mutated));
            Assert.Contains("posture", ex.Message);
            Assert.Equal(before, other.Ai.HashState());
            other.Shutdown();
        }

        [Fact]
        public void Load_FailedCollectionCountLeavesHashUnchanged()
        {
            AiMatchHost host = StartHost();
            host.Run(80);
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            int orderCountOffset = FirstFactionIntOffset(blob) + (8 * sizeof(int)) + 1 + sizeof(int);
            byte[] mutated = (byte[])blob.Clone();
            mutated[orderCountOffset] = 0xFF;
            mutated[orderCountOffset + 1] = 0xFF;
            mutated[orderCountOffset + 2] = 0xFF;
            mutated[orderCountOffset + 3] = 0x7F;

            AiMatchHost other = StartHost();
            ulong before = other.Ai.HashState();
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => other.Ai.Load(mutated));
            Assert.Contains("slot capacity", ex.Message);
            Assert.Equal(before, other.Ai.HashState());
            other.Shutdown();
        }

        private static int FirstFactionIntOffset(byte[] blob)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(blob)))
            {
                reader.ReadInt32();
                reader.ReadUInt64();
                reader.ReadUInt64();
                reader.ReadUInt64();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadString();
                reader.ReadString();
                return (int)reader.BaseStream.Position;
            }
        }

        [Fact]
        public void StrategicSlice_RunsOnPhaseSixAfterCadence()
        {
            AiMatchHost host = StartHost();
            Assert.Equal(0, host.Ai.StrategicSliceRuns);
            host.Run(50);
            Assert.True(host.Ai.StrategicSliceRuns > 0);
            host.Shutdown();
        }

        [Fact]
        public void OnTick_DoesNotBypassTheCommandQueue()
        {
            AiMatchHost host = StartHost();
            host.Run(200);
            Assert.IsType<LoggingCommandSink>(host.Ai.Commands);
            Assert.Equal(host.World.CommandDelayTicks, host.Ai.Commands.CommandDelayTicks);
            Assert.Equal(SimConfig.DefaultCommandDelayTicks, host.World.CommandDelayTicks);
            for (int i = 0; i < host.World.Events.Count; i++)
            {
                SimEvent ev = host.World.Events[i];
                if (ev.Kind == SimEventKind.CommandRejected)
                {
                    Assert.NotEqual(CommandRejectReason.UnknownKind, ev.RejectReason);
                }
            }

            Assert.True(host.CommandLog.Count > 0);
            for (int i = 0; i < host.CommandLog.Count; i++)
            {
                Assert.Equal(0, host.CommandLog[i].ExecuteTick);
            }

            host.Shutdown();
        }

        [Fact]
        public void ReactionDelayQueue_SubmitsOnlyAfterExtraTicks()
        {
            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();
            int extra = balance.GetDifficulty(AiConstants.DifficultyNormal).ReactionExtraTicks;
            Assert.Equal(12, extra);

            AiMatchHost host = StartHost();
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            int ready = host.Tick + extra;
            AiPendingCommand pending = default;
            pending.ReadyTick = ready;
            pending.BattalionId = 9001;
            pending.Kind = CommandKind.Move;
            pending.Arg0 = 1;
            pending.Arg1 = 2;
            Assert.True(faction.EnqueuePending(pending));
            Assert.True(HasPending(host.Ai, 9001, ready));
            Assert.True(ready > host.Tick);
            int logAtEnqueue = host.CommandLog.Count;
            while (host.Tick < ready)
            {
                Assert.True(HasPending(host.Ai, 9001, ready));
                host.Step();
            }

            host.Step();
            Assert.False(HasPending(host.Ai, 9001, ready));
            Assert.True(host.CommandLog.Count > logAtEnqueue);
            bool found = false;
            for (int i = 0; i < host.CommandLog.Count; i++)
            {
                if (host.CommandLog[i].IssuedTick == ready
                    && host.CommandLog[i].BattalionId == 9001
                    && host.CommandLog[i].Kind == CommandKind.Move)
                {
                    Assert.Equal(0, host.CommandLog[i].ExecuteTick);
                    found = true;
                }
            }

            Assert.True(found);
            host.Shutdown();
        }

        [Fact]
        public void ReactionDelayQueue_IsHashedAndRestoredOnSaveLoad()
        {
            AiMatchHost host = StartHost();
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            AiPendingCommand pending = default;
            pending.ReadyTick = host.Tick + 12;
            pending.BattalionId = 9002;
            pending.Kind = CommandKind.Move;
            pending.Arg0 = 3;
            pending.Arg1 = 4;
            Assert.True(faction.EnqueuePending(pending));
            Assert.Equal(1, TotalPending(host.Ai));
            ulong before = host.Ai.HashState();
            byte[] blob = host.Ai.Save();
            int readyTick = faction.Pending[0].ReadyTick;

            AiMatchHost restored = StartHost();
            restored.Ai.Load(blob);
            Assert.Equal(before, restored.Ai.HashState());
            Assert.Equal(1, TotalPending(restored.Ai));
            Assert.Equal(readyTick, FirstReadyTick(restored.Ai));
            restored.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void Brutal_HasNoReactionDelayOnSubmit()
        {
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(0xA11EEDUL),
                LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyBrutal,
                AiBalanceTablesTests.LoadBalance());
            Assert.Equal(0, host.Ai.Difficulty.ReactionExtraTicks);
            host.Run(80);
            Assert.True(host.CommandLog.Count > 0);
            Assert.Equal(0, host.CommandLog[0].ExecuteTick);
            host.Shutdown();
        }

        private static int TotalPending(EnemyAi ai)
        {
            int total = 0;
            for (int i = 0; i < ai.Owned.FactionCount; i++)
            {
                total += ai.Owned.FactionAt(i).PendingCount;
            }

            return total;
        }

        private static bool HasPending(EnemyAi ai, int battalionId, int readyTick)
        {
            for (int i = 0; i < ai.Owned.FactionCount; i++)
            {
                AiFactionState faction = ai.Owned.FactionAt(i);
                for (int p = 0; p < faction.PendingCount; p++)
                {
                    if (faction.Pending[p].BattalionId == battalionId
                        && faction.Pending[p].ReadyTick == readyTick)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int FirstReadyTick(EnemyAi ai)
        {
            int best = int.MaxValue;
            for (int i = 0; i < ai.Owned.FactionCount; i++)
            {
                AiFactionState faction = ai.Owned.FactionAt(i);
                for (int p = 0; p < faction.PendingCount; p++)
                {
                    if (faction.Pending[p].ReadyTick < best)
                    {
                        best = faction.Pending[p].ReadyTick;
                    }
                }
            }

            return best;
        }

        [Fact]
        public void PathBudget_IsRespected()
        {
            AiMatchHost host = StartHost();
            host.Ai.OnTick(host.World.Tick);
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            int accepted = 0;
            for (int i = 0; i < 16; i++)
            {
                if (host.Ai.TryConsumePathRequest(faction))
                {
                    accepted++;
                }
            }

            Assert.Equal(AiConstants.PathRequestsPerTick, accepted);
            host.Shutdown();
        }

        [Fact]
        public void SubmitThroughSink_IsTheSameQueueHumansUse()
        {
            AiMatchHost host = StartHost();
            int before = host.World.BattalionCount;
            const int battalionId = 9000007;
            host.Ai.Commands.Submit(SimCommand.SpawnBattalion(0, 1, battalionId, 0, 0, 100));
            host.World.Step();
            host.World.Step();
            host.World.Step();
            Assert.Equal(before + 1, host.World.BattalionCount);
            Assert.Equal(1, host.World.GetBattalion(battalionId).OwnerId);
            host.Shutdown();
        }

        [Fact]
        public void SimWorld_RejectsBuildAsUnknownKind()
        {
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            world.Submit(SimCommand.Build(0, 1, 0, 0, AiConstants.BuildingTagDepot));
            world.Step();
            world.Step();
            world.Step();
            Assert.Single(world.Events);
            Assert.Equal(SimEventKind.CommandRejected, world.Events[0].Kind);
            Assert.Equal(CommandRejectReason.UnknownKind, world.Events[0].RejectReason);
        }

        [Fact]
        public void AiCommandSink_AcceptsBuildWithoutForwardingToSimWorld()
        {
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            sink.Submit(SimCommand.Build(0, 1, 100, 200, AiConstants.BuildingTagDepot));
            world.Step();
            world.Step();
            world.Step();
            Assert.Empty(world.Events);
            Assert.Equal(0, world.BattalionCount);
        }

        [Fact]
        public void HomeSeeder_MapsLaunchPersonalities()
        {
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(LoadGorgeV1Bytes());
            IAiScenarioSeats seats = FixtureAiScenarioSeats.ForLaunchPersonalities(
                map,
                AiBalanceTablesTests.LoadBalance());
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView settler));
            Assert.Equal(HomeTerritoryKeyOf(map, map.HoodRiverTownId), settler.HomeTerritoryKey);
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView remnant));
            Assert.Equal(HomeTerritoryKeyOf(map, map.GoldendaleTownId), remnant.HomeTerritoryKey);
            Assert.Equal("remnant_command", remnant.Faction);
            Assert.Equal(HomeTerritoryKeyOf(map, map.TheDallesTownId), seats.NeutralPrizeTerritoryKey);
            Assert.False(seats.TryGetByFaction("missing_faction", out _));
        }

        [Fact]
        public void Submit_OverwritesIssuedTickToWorldTick()
        {
            AiMatchHost host = StartHost();
            host.Run(5);
            SimCommand stamped = host.Ai.Commands.Submit(SimCommand.SpawnBattalion(0, 1, 9, 0, 0, 100));
            Assert.Equal(host.World.Tick, stamped.IssuedTick);
            host.Shutdown();
        }

        [Fact]
        public void MatchInit_RejectsCommandDelayMismatch()
        {
            AiBalanceTables balance = AiBalanceTablesTests.LoadBalance();
            byte[] map = LoadGorgeV1Bytes();
            MatchConfig match = new MatchConfig(0xA11EEDUL, 5).WithKeys(
                balance.BalanceKey,
                MapKey.For(WrmbFile.Parse(map), AiConstants.MapKeySectionTags()));
            SimWorld world = new SimWorld(new MatchConfig(0xA11EEDUL, 2));
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            Assert.Throws<System.ArgumentException>(() => EnemyAi.MatchInit(
                match,
                map,
                new[] { AiConstants.DefaultAiFactionA },
                AiConstants.DifficultyNormal,
                balance,
                sink,
                sink,
                FixtureAiScenarioSeats.ForLaunchPersonalities(AiMapSnapshot.FromWrmb(map), balance)));
        }

        [Fact]
        public void AiRngDraws_DoNotMoveWorldRng()
        {
            AiMatchHost host = StartHost();
            ulong beforeState = host.World.Rng.State;
            ulong beforeInc = host.World.Rng.Increment;
            host.Ai.Owned.FactionAt(0).StrategyRng.NextUInt();
            host.Ai.Owned.FactionAt(0).TacticalRng.NextUInt();
            host.Ai.Owned.FactionAt(1).ScoutRng.NextUInt();
            Assert.Equal(beforeState, host.World.Rng.State);
            Assert.Equal(beforeInc, host.World.Rng.Increment);
            Assert.NotEqual(host.World.Rng.Increment, host.Ai.Owned.FactionAt(0).StrategyRng.Increment);
            Assert.NotEqual(
                host.Ai.Owned.FactionAt(0).StrategyRng.Increment,
                host.Ai.Owned.FactionAt(0).TacticalRng.Increment);
            host.Shutdown();
        }

        [Fact]
        public void ReactionDelayQueue_DropsLowestPriorityWhenFull()
        {
            AiMatchHost host = StartHost();
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            for (int i = 0; i < AiConstants.PendingCommandCapacity; i++)
            {
                AiPendingCommand move = default;
                move.ReadyTick = 1000;
                move.BattalionId = 100 + i;
                move.Kind = CommandKind.Move;
                Assert.True(faction.EnqueuePending(move));
            }

            Assert.Equal(AiConstants.PendingCommandCapacity, faction.PendingCount);
            ulong full = host.Ai.HashState();

            AiPendingCommand spawn = default;
            spawn.ReadyTick = 1;
            spawn.BattalionId = 1;
            spawn.Kind = CommandKind.SpawnBattalion;
            Assert.True(faction.EnqueuePending(spawn));
            Assert.Equal(AiConstants.PendingCommandCapacity, faction.PendingCount);
            Assert.Equal(1, faction.PendingDropped);
            Assert.NotEqual(full, host.Ai.HashState());

            bool foundSpawn = false;
            bool foundReplacedMove = false;
            for (int i = 0; i < faction.PendingCount; i++)
            {
                if (faction.Pending[i].Kind == CommandKind.SpawnBattalion
                    && faction.Pending[i].BattalionId == 1)
                {
                    foundSpawn = true;
                }

                if (faction.Pending[i].BattalionId == 100 + AiConstants.PendingCommandCapacity - 1)
                {
                    foundReplacedMove = true;
                }
            }

            Assert.True(foundSpawn);
            Assert.False(foundReplacedMove);

            AiPendingCommand build = default;
            build.ReadyTick = 9999;
            build.BattalionId = 9999;
            build.Kind = CommandKind.Build;
            Assert.False(faction.EnqueuePending(build));
            Assert.Equal(2, faction.PendingDropped);
            Assert.Equal(AiConstants.PendingCommandCapacity, faction.PendingCount);

            byte[] blob = host.Ai.Save();
            AiMatchHost restored = StartHost();
            restored.Ai.Load(blob);
            Assert.Equal(2, restored.Ai.Owned.FactionAt(0).PendingDropped);
            Assert.Equal(host.Ai.HashState(), restored.Ai.HashState());
            restored.Shutdown();
            host.Shutdown();
        }

        [Fact]
        public void CaptureOccupancy_ReturnsOnlyEmptyOneFactionOrContested()
        {
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            CaptureZoneOccupancyResult empty = sink.OccupancyOf(0, 0, 80000);
            Assert.Equal(CaptureOccupancyKind.Empty, empty.Kind);
            Assert.Equal(0, empty.FactionId);

            sink.Submit(SimCommand.SpawnBattalion(0, 1, 11, 0, 0, 100));
            sink.Submit(SimCommand.SpawnBattalion(0, 2, 12, 0, 0, 100));
            world.Step();
            world.Step();
            world.Step();

            CaptureZoneOccupancyResult contested = sink.OccupancyOf(0, 0, 80000);
            Assert.Equal(CaptureOccupancyKind.Contested, contested.Kind);
            Assert.Equal(0, contested.FactionId);

            CaptureZoneOccupancyResult far = sink.OccupancyOf(5_000_000, 5_000_000, 100);
            Assert.Equal(CaptureOccupancyKind.Empty, far.Kind);
        }

        [Fact]
        public void LoggingCommandSink_CapsTheRecordedLog()
        {
            Assert.Equal(4096, AiConstants.CommandLogCapacity);
            SimWorld world = new SimWorld(MatchConfig.Default(1UL));
            LoggingCommandSink sink = new LoggingCommandSink(world);
            for (int i = 0; i < 4; i++)
            {
                sink.Submit(SimCommand.SpawnBattalion(0, 1, 20 + i, i, 0, 100));
            }

            Assert.Equal(4, sink.Log.Count);
            Assert.Equal(0, sink.DroppedFromLog);
            Assert.Equal(world.Tick, sink.Log[0].IssuedTick);

            for (int i = 0; i < AiConstants.CommandLogCapacity + 2; i++)
            {
                sink.Submit(SimCommand.Build(0, 1, i, 0, AiConstants.BuildingTagDepot));
            }

            Assert.Equal(AiConstants.CommandLogCapacity, sink.Log.Count);
            Assert.Equal(6, sink.DroppedFromLog);
        }

        [Fact]
        public void Load_RejectsTrailingBytes_AndLeavesLiveStateUntouched()
        {
            AiMatchHost host = StartHost();
            host.Run(16);
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            byte[] trailing = new byte[blob.Length + 1];
            Buffer.BlockCopy(blob, 0, trailing, 0, blob.Length);
            trailing[trailing.Length - 1] = 0x7F;

            AiMatchHost other = StartHost();
            ulong before = other.Ai.HashState();
            int live = other.Ai.Owned.FactionAt(0).Manpower;
            other.Ai.Owned.FactionAt(0).Manpower = live + 12345;
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => other.Ai.Load(trailing));
            Assert.Contains("trailing", ex.Message);
            Assert.Equal(live + 12345, other.Ai.Owned.FactionAt(0).Manpower);
            Assert.NotEqual(before, other.Ai.HashState());
            other.Shutdown();
        }

        [Fact]
        public void Load_CorruptSchema_LeavesLiveStateUntouched()
        {
            AiMatchHost host = StartHost();
            byte[] blob = host.Ai.Save();
            host.Shutdown();

            byte[] mutated = (byte[])blob.Clone();
            mutated[0] ^= 0xFF;
            AiMatchHost other = StartHost();
            int live = other.Ai.Owned.FactionAt(0).Manpower;
            other.Ai.Owned.FactionAt(0).Manpower = live + 77;
            Assert.Throws<InvalidDataException>(() => other.Ai.Load(mutated));
            Assert.Equal(live + 77, other.Ai.Owned.FactionAt(0).Manpower);
            other.Shutdown();
        }

        [Fact]
        public void AiSaveLoad_RejectsSchema8_WithVersionMismatch()
        {
            AiMatchHost host = StartHost();
            int sentinel = host.Ai.Owned.FactionAt(0).Manpower;
            host.Ai.Owned.FactionAt(0).Manpower = sentinel + 1;
            byte[] schema8 = { 8, 0, 0, 0 };
            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => host.Ai.Load(schema8));
            Assert.Equal(
                "AI state schema version 8 is unsupported; expected 9.",
                ex.Message);
            Assert.Equal(sentinel + 1, host.Ai.Owned.FactionAt(0).Manpower);
            host.Shutdown();
        }

        [Fact]
        public void OverflowDrop_RemovesWholeSpawnOrderAndBuildDepot()
        {
            AiMatchHost host = StartHost();
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            AiOrder order = default;
            order.Id = 9;
            order.Type = AiGoalType.ClaimTown;
            order.BattalionId = 77;
            faction.Orders[0] = order;
            faction.OrderCount = 1;

            AiDepot depot = default;
            depot.XCm = 11;
            depot.YCm = 22;
            depot.NodeId = 4;
            faction.Depots[0] = depot;
            faction.DepotCount = 1;

            for (int i = 0; i < AiConstants.PendingCommandCapacity; i++)
            {
                AiPendingCommand fill = default;
                fill.ReadyTick = 1000;
                fill.BattalionId = 50 + i;
                fill.Kind = CommandKind.Build;
                fill.Arg0 = 11;
                fill.Arg1 = 22;
                Assert.True(faction.EnqueuePending(fill));
            }

            AiPendingCommand incoming = default;
            incoming.ReadyTick = 1;
            incoming.BattalionId = 1;
            incoming.Kind = CommandKind.SpawnBattalion;
            PendingEnqueueResult result = faction.EnqueuePending(incoming, out AiPendingCommand evicted);
            Assert.Equal(PendingEnqueueResult.Replaced, result);
            Assert.Equal(CommandKind.Build, evicted.Kind);
            host.Ai.DropAssociated(faction, evicted);
            Assert.Equal(0, faction.DepotCount);
            Assert.Equal(1, faction.OrderCount);

            faction.PendingCount = 0;
            faction.PendingDropped = 0;
            for (int i = 0; i < AiConstants.PendingCommandCapacity; i++)
            {
                AiPendingCommand fill = default;
                fill.ReadyTick = 1000;
                fill.BattalionId = 77;
                fill.Kind = CommandKind.SpawnBattalion;
                Assert.True(faction.EnqueuePending(fill));
            }

            incoming.BattalionId = 2;
            incoming.ReadyTick = 1;
            result = faction.EnqueuePending(incoming, out evicted);
            Assert.Equal(PendingEnqueueResult.Replaced, result);
            Assert.Equal(CommandKind.SpawnBattalion, evicted.Kind);
            host.Ai.DropAssociated(faction, evicted);
            Assert.Equal(0, faction.OrderCount);
            host.Shutdown();
        }

        [Fact]
        public void ReplayTo_FailsWhenDroppedFromLogIsNonzero()
        {
            AiMatchHost host = StartHost();
            LoggingCommandSink sink = (LoggingCommandSink)host.Ai.Commands;
            while (sink.DroppedFromLog == 0)
            {
                host.Ai.Commands.Submit(SimCommand.Build(host.Tick, 1, 0, 0, AiConstants.BuildingTagDepot));
            }

            Assert.True(host.Ai.DroppedFromLog > 0);
            ulong hashed = host.Ai.HashState();
            host.Ai.Commands.Submit(SimCommand.Build(host.Tick, 1, 1, 0, AiConstants.BuildingTagDepot));
            Assert.NotEqual(hashed, host.Ai.HashState());

            byte[] blob = host.Ai.Save();
            SimCommand[] log = new SimCommand[host.CommandLog.Count];
            for (int i = 0; i < log.Length; i++)
            {
                log[i] = host.CommandLog[i];
            }

            InvalidDataException ex = Assert.Throws<InvalidDataException>(() => AiMatchHost.ReplayTo(
                MatchConfig.Default(0xA11EEDUL),
                LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance(),
                log,
                blob,
                host.Tick));
            Assert.Contains("overflow", ex.Message);
            host.Shutdown();
        }

        [Fact]
        public void RoadSupply_ReusesScratchAndChargesWorkUnits()
        {
            AiMatchHost host = StartHost();
            RoadGraph roads = host.Ai.Map.Roads;
            int from = roads.GetNodeId(0);
            int to = roads.GetNodeId(roads.NodeCount > 8 ? 8 : roads.NodeCount - 1);
            AiFactionState faction = host.Ai.Owned.FactionAt(0);
            faction.WorkUnitsThisTick = 0;
            AiRoadSupply supply = new AiRoadSupply();
            AiSupplyResult first = supply.DistanceCm(roads, from, to, faction);
            Assert.True(faction.WorkUnitsThisTick > 0);
            Assert.True(faction.WorkUnitsThisTick <= faction.WorkUnitsPerTick);
            int charged = faction.WorkUnitsThisTick;
            AiSupplyResult second = supply.DistanceCm(roads, from, to, faction);
            Assert.Equal(charged * 2, faction.WorkUnitsThisTick);
            Assert.Equal(first.Status, second.Status);
            Assert.Equal(first.DistanceCm, second.DistanceCm);
            host.Shutdown();
        }

        private static AiMatchHost StartHost()
        {
            return AiMatchHost.Start(
                MatchConfig.Default(0xA11EEDUL),
                LoadGorgeV1Bytes(),
                new[] { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB },
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
        }

        internal static byte[] LoadGorgeBytes()
        {
            string dir = GoldenMapFiles.FindDirectory(GoldenMapTests.MapFileName);
            return GoldenMapFiles.ReadWrmb(Path.Combine(dir, GoldenMapTests.MapFileName), GoldenMapTests.MapBytes);
        }

        internal static byte[] LoadGorgeV1Bytes()
        {
            string dir = GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName);
            return GoldenMapFiles.ReadWrmb(Path.Combine(dir, GoldenMapV1MarshTests.MapFileName), GoldenMapV1MarshTests.MapBytes);
        }

        internal static AiMapSnapshot LoadGorgeMap()
        {
            return AiMapSnapshot.FromWrmb(LoadGorgeBytes());
        }

        private static ulong HomeTerritoryKeyOf(AiMapSnapshot map, int townId)
        {
            Assert.True(map.TryGetTown(townId, out AiTownRecord town));
            Assert.True(map.TryGetTerritory(town.TerritoryId, out AiTerritoryRecord territory));
            Assert.NotEqual(0UL, territory.StableKey);
            return territory.StableKey;
        }
    }
}
