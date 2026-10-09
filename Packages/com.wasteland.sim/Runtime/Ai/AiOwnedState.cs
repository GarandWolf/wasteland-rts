using System;
using System.IO;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Random;

namespace Wasteland.Sim.Ai
{
    public struct AiOrder
    {
        public int Id;
        public AiGoalType Type;
        public int TargetTownId;
        public int TargetTerritoryId;
        public int TargetNodeId;
        public int BattalionId;
        public int Score;
    }

    public struct AiDepot
    {
        public int TerritoryId;
        public ulong TerritoryStableKey;
        public int SeatTownId;
        public int NodeId;
        public int XCm;
        public int YCm;
    }

    /// <summary>
    /// One command waiting on <see cref="AiFactionState.ReactionExtraTicks"/>.
    /// <see cref="SimWorld.Submit"/> stamps <c>executeTick</c>; this queue
    /// must not.
    /// </summary>
    public struct AiPendingCommand
    {
        public int ReadyTick;
        public int BattalionId;
        public CommandKind Kind;
        public int Arg0;
        public int Arg1;
        public int Arg2;
    }

    /// <summary>
    /// A ClaimTown or BuildDepot still in the world queue. <see cref="Sequence"/>
    /// is the value <see cref="ISimCommandSink.LastSequence"/> returned at enqueue
    /// and matches CommandRejected.Key = (issuer &lt;&lt; 32) | sequence.
    /// </summary>
    public struct AiPendingIntent
    {
        public int Sequence;
        public AiIssuedKind Kind;
        public int BattalionId;
        public int TargetId;
        public int IssuedTick;
    }

    public enum PendingEnqueueResult : byte
    {
        Enqueued = 0,
        DroppedIncoming = 1,
        Replaced = 2
    }

    /// <summary>
    /// Per-faction AI memory. Other sim systems must not read this. Stored in
    /// faction-id order, hashed, and save/loadable.
    /// </summary>
    public sealed class AiFactionState
    {
        public readonly int FactionId;
        public readonly string PersonalityId;
        public readonly string DifficultyId;
        public readonly int ReactionExtraTicks;
        public readonly int StrategicCadenceTicks;
        public readonly int MaxActiveOrders;
        public readonly int MinGarrisonPerTown;
        public readonly int ManpowerReserveSeconds;
        public readonly int MaxCandidateGoals;
        public readonly int TaskForceTargetSize;
        public readonly int ReservePpt;
        public int WorkUnitsPerTick;

        public AiPosture Posture;
        public int NextOrderId;
        public int OrderCount;
        public int TaskForceCount;
        public int WorkResumeId;
        public int PathRequestsThisTick;
        public int WorkUnitsThisTick;
        public int DepotCapSkips;
        public bool StrategicDeferred;
        public int HomeTerritoryId;
        public int HomeTownId;
        public int HqNodeId;
        public int NextBattalionId;
        public int Manpower;
        public int ManpowerCarry;
        public int DepotCount;
        public int PickedPostCount;
        public int PendingCount;
        public int IntentCount;
        public int BlacklistCount;
        public int PendingDropped;
        public int AttackTargetTownId;
        public int DefendTownId;
        public int LastIssuedCommandId;
        public int LastRejectCode;
        public int RejectBackoffUntilTick;
        public int RejectStreak;
        public int DepotSiteTick;
        public int DepotActiveTick;
        public int SupplyBudgetDefers;
        public readonly AiOrder[] Orders;
        public readonly AiDepot[] Depots;
        public readonly int[] PickedPostIds;
        public readonly int[] PickedGroupIds;
        public readonly AiPendingCommand[] Pending;
        public readonly AiPendingIntent[] Intents;
        public readonly int[] BlacklistedNodeIds;
        public SimRandom StrategyRng;
        public SimRandom TacticalRng;
        public SimRandom MistakeRng;
        public SimRandom ScoutRng;

        public AiFactionState(
            int factionId,
            string personalityId,
            string difficultyId,
            int reactionExtraTicks,
            int strategicCadenceTicks,
            int maxActiveOrders,
            int minGarrisonPerTown,
            int manpowerReserveSeconds,
            int maxCandidateGoals,
            int taskForceTargetSize,
            int reservePpt,
            int workUnitsPerTick,
            int maxDepotsPerFaction,
            int pickedPostCapacity,
            AiPosture posture,
            SimRandom strategyRng,
            SimRandom tacticalRng,
            SimRandom mistakeRng,
            SimRandom scoutRng)
        {
            FactionId = factionId;
            PersonalityId = personalityId ?? throw new ArgumentNullException(nameof(personalityId));
            DifficultyId = difficultyId ?? throw new ArgumentNullException(nameof(difficultyId));
            ReactionExtraTicks = reactionExtraTicks;
            StrategicCadenceTicks = strategicCadenceTicks;
            MaxActiveOrders = maxActiveOrders;
            MinGarrisonPerTown = minGarrisonPerTown;
            ManpowerReserveSeconds = manpowerReserveSeconds;
            MaxCandidateGoals = maxCandidateGoals;
            TaskForceTargetSize = taskForceTargetSize;
            ReservePpt = reservePpt;
            WorkUnitsPerTick = workUnitsPerTick;
            if (WorkUnitsPerTick < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(workUnitsPerTick));
            }

            Posture = posture;
            NextOrderId = 1;
            NextBattalionId = factionId * AiConstants.BattalionIdStride + 1;
            Orders = new AiOrder[System.Math.Max(1, maxActiveOrders)];
            Depots = new AiDepot[System.Math.Max(1, maxDepotsPerFaction)];
            int postCap = System.Math.Max(1, pickedPostCapacity);
            PickedPostIds = new int[postCap];
            PickedGroupIds = new int[postCap];
            Pending = new AiPendingCommand[AiConstants.PendingCommandCapacity];
            Intents = new AiPendingIntent[AiConstants.PendingIntentCapacity];
            BlacklistedNodeIds = new int[AiConstants.DepotNodeBlacklistCapacity];
            StrategyRng = strategyRng;
            TacticalRng = tacticalRng;
            MistakeRng = mistakeRng;
            ScoutRng = scoutRng;
        }

        public void EnqueueIntent(AiPendingIntent intent)
        {
            for (int i = 0; i < IntentCount; i++)
            {
                if (Intents[i].Sequence == intent.Sequence)
                {
                    return;
                }

                if (Intents[i].Kind == intent.Kind
                    && Intents[i].BattalionId == intent.BattalionId
                    && Intents[i].TargetId == intent.TargetId)
                {
                    return;
                }
            }

            if (IntentCount < Intents.Length)
            {
                InsertIntent(intent);
                return;
            }

            int worst = 0;
            for (int i = 1; i < IntentCount; i++)
            {
                if (CompareIntents(Intents[i], Intents[worst]) < 0)
                {
                    worst = i;
                }
            }

            if (CompareIntents(intent, Intents[worst]) <= 0)
            {
                return;
            }

            Intents[worst] = intent;
            SortIntents();
        }

        public bool IsNodeBlacklisted(int nodeId)
        {
            return IndexOfBlacklistedNode(nodeId) >= 0;
        }

        public void BlacklistNode(int nodeId)
        {
            if (nodeId == 0 || IsNodeBlacklisted(nodeId))
            {
                return;
            }

            if (BlacklistCount < BlacklistedNodeIds.Length)
            {
                BlacklistedNodeIds[BlacklistCount] = nodeId;
                BlacklistCount++;
                SortBlacklist();
                return;
            }

            for (int i = 0; i < BlacklistCount - 1; i++)
            {
                BlacklistedNodeIds[i] = BlacklistedNodeIds[i + 1];
            }

            BlacklistedNodeIds[BlacklistCount - 1] = nodeId;
            SortBlacklist();
        }

        private int IndexOfBlacklistedNode(int nodeId)
        {
            for (int i = 0; i < BlacklistCount; i++)
            {
                if (BlacklistedNodeIds[i] == nodeId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SortBlacklist()
        {
            for (int i = 1; i < BlacklistCount; i++)
            {
                int row = BlacklistedNodeIds[i];
                int j = i;
                while (j > 0 && BlacklistedNodeIds[j - 1] > row)
                {
                    BlacklistedNodeIds[j] = BlacklistedNodeIds[j - 1];
                    j--;
                }

                BlacklistedNodeIds[j] = row;
            }
        }

        public bool RemoveIntentAt(int index)
        {
            if (index < 0 || index >= IntentCount)
            {
                return false;
            }

            for (int i = index + 1; i < IntentCount; i++)
            {
                Intents[i - 1] = Intents[i];
            }

            IntentCount--;
            return true;
        }

        private void InsertIntent(AiPendingIntent intent)
        {
            int insert = IntentCount;
            while (insert > 0 && CompareIntents(Intents[insert - 1], intent) > 0)
            {
                Intents[insert] = Intents[insert - 1];
                insert--;
            }

            Intents[insert] = intent;
            IntentCount++;
        }

        private void SortIntents()
        {
            for (int i = 1; i < IntentCount; i++)
            {
                AiPendingIntent row = Intents[i];
                int j = i;
                while (j > 0 && CompareIntents(Intents[j - 1], row) > 0)
                {
                    Intents[j] = Intents[j - 1];
                    j--;
                }

                Intents[j] = row;
            }
        }

        public static int CompareIntents(AiPendingIntent a, AiPendingIntent b)
        {
            int c = a.Sequence.CompareTo(b.Sequence);
            if (c != 0)
            {
                return c;
            }

            c = ((byte)a.Kind).CompareTo((byte)b.Kind);
            if (c != 0)
            {
                return c;
            }

            c = a.BattalionId.CompareTo(b.BattalionId);
            if (c != 0)
            {
                return c;
            }

            return a.TargetId.CompareTo(b.TargetId);
        }

        /// <summary>
        /// Enqueue a reaction-delayed command. If the 128-slot queue is full,
        /// drop the lowest-priority pending (or the incoming command if it is
        /// lowest). Priority is Kind rank (Spawn, Move, Build, other), then
        /// sooner <see cref="AiPendingCommand.ReadyTick"/>, then lower
        /// battalion id. Deterministic; increments <see cref="PendingDropped"/>.
        /// </summary>
        public PendingEnqueueResult EnqueuePending(AiPendingCommand pending, out AiPendingCommand evicted)
        {
            evicted = default;
            if (PendingCount < Pending.Length)
            {
                Pending[PendingCount] = pending;
                PendingCount++;
                return PendingEnqueueResult.Enqueued;
            }

            int worst = 0;
            for (int i = 1; i < PendingCount; i++)
            {
                if (ComparePriority(Pending[i], Pending[worst]) < 0)
                {
                    worst = i;
                }
            }

            if (ComparePriority(pending, Pending[worst]) <= 0)
            {
                PendingDropped++;
                return PendingEnqueueResult.DroppedIncoming;
            }

            evicted = Pending[worst];
            Pending[worst] = pending;
            PendingDropped++;
            return PendingEnqueueResult.Replaced;
        }

        public bool EnqueuePending(AiPendingCommand pending)
        {
            return EnqueuePending(pending, out _) != PendingEnqueueResult.DroppedIncoming;
        }

        public static int ComparePriority(AiPendingCommand a, AiPendingCommand b)
        {
            int kind = KindRank(a.Kind).CompareTo(KindRank(b.Kind));
            if (kind != 0)
            {
                return kind;
            }

            int ready = b.ReadyTick.CompareTo(a.ReadyTick);
            if (ready != 0)
            {
                return ready;
            }

            return b.BattalionId.CompareTo(a.BattalionId);
        }

        private static int KindRank(CommandKind kind)
        {
            if (kind == CommandKind.SpawnBattalion)
            {
                return 3;
            }

            if (kind == CommandKind.Move)
            {
                return 2;
            }

            if (kind == CommandKind.Build || kind == CommandKind.BuildDepot)
            {
                return 1;
            }

            return 0;
        }

        public bool PlanThisTick(int tick)
        {
            int cadence = StrategicCadenceTicks;
            int offset = checked(tick + (FactionId * AiConstants.FactionPlanOffsetTicks));
            return offset % cadence == 0;
        }
    }

    public sealed class AiOwnedState
    {
        private readonly AiFactionState[] _factions;
        private readonly int[] _townOwnerIds;

        public int DroppedFromLog;

        public AiOwnedState(AiFactionState[] factions, int townCount)
        {
            if (factions == null || factions.Length < 1)
            {
                throw new ArgumentException("At least one AI faction is required.", nameof(factions));
            }

            if (townCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(townCount));
            }

            _factions = (AiFactionState[])factions.Clone();
            SortByFactionId(_factions);
            for (int i = 1; i < _factions.Length; i++)
            {
                if (_factions[i].FactionId == _factions[i - 1].FactionId)
                {
                    throw new ArgumentException("Duplicate AI faction id.");
                }
            }

            _townOwnerIds = new int[townCount];
        }

        public int FactionCount
        {
            get { return _factions.Length; }
        }

        public int TownSlotCount
        {
            get { return _townOwnerIds.Length; }
        }

        public AiFactionState FactionAt(int index)
        {
            return _factions[index];
        }

        public AiFactionState GetFaction(int factionId)
        {
            for (int i = 0; i < _factions.Length; i++)
            {
                if (_factions[i].FactionId == factionId)
                {
                    return _factions[i];
                }
            }

            throw new InvalidOperationException("Unknown AI faction " + factionId + ".");
        }

        public int TownOwnerAt(int townIndex)
        {
            return _townOwnerIds[townIndex];
        }

        public void SetTownOwner(int townIndex, int ownerId)
        {
            _townOwnerIds[townIndex] = ownerId;
        }

        public void EnqueueIntent(int factionId, AiPendingIntent intent)
        {
            GetFaction(factionId).EnqueueIntent(intent);
        }

        public void HashInto(XxHash64Builder hash)
        {
            hash.AddInt(DroppedFromLog);
            hash.AddInt(_factions.Length);
            for (int i = 0; i < _factions.Length; i++)
            {
                AiFactionState faction = _factions[i];
                hash.AddInt(faction.FactionId);
                hash.AddULong(AiBalanceTables.HashUtf8(faction.PersonalityId));
                hash.AddULong(AiBalanceTables.HashUtf8(faction.DifficultyId));
                hash.AddInt(faction.ReactionExtraTicks);
                hash.AddInt(faction.StrategicCadenceTicks);
                hash.AddInt(faction.MaxActiveOrders);
                hash.AddInt(faction.MinGarrisonPerTown);
                hash.AddInt(faction.ManpowerReserveSeconds);
                hash.AddInt(faction.MaxCandidateGoals);
                hash.AddInt(faction.TaskForceTargetSize);
                hash.AddInt(faction.ReservePpt);
                hash.AddByte((byte)faction.Posture);
                hash.AddInt(faction.NextOrderId);
                hash.AddInt(faction.OrderCount);
                hash.AddInt(faction.TaskForceCount);
                hash.AddInt(faction.WorkResumeId);
                hash.AddBool(faction.StrategicDeferred);
                hash.AddInt(faction.HomeTerritoryId);
                hash.AddInt(faction.HomeTownId);
                hash.AddInt(faction.HqNodeId);
                hash.AddInt(faction.NextBattalionId);
                hash.AddInt(faction.Manpower);
                hash.AddInt(faction.ManpowerCarry);
                hash.AddInt(faction.DepotCount);
                hash.AddInt(faction.PickedPostCount);
                hash.AddInt(faction.DepotCapSkips);
                hash.AddInt(faction.PendingCount);
                hash.AddInt(faction.PendingDropped);
                hash.AddInt(faction.AttackTargetTownId);
                hash.AddInt(faction.DefendTownId);
                hash.AddInt(faction.LastRejectCode);
                hash.AddInt(faction.RejectBackoffUntilTick);
                hash.AddInt(faction.RejectStreak);
                hash.AddInt(faction.DepotSiteTick);
                hash.AddInt(faction.DepotActiveTick);
                hash.AddInt(faction.SupplyBudgetDefers);
                hash.AddULong(faction.StrategyRng.State);
                hash.AddULong(faction.StrategyRng.Increment);
                hash.AddULong(faction.TacticalRng.State);
                hash.AddULong(faction.TacticalRng.Increment);
                hash.AddULong(faction.MistakeRng.State);
                hash.AddULong(faction.MistakeRng.Increment);
                hash.AddULong(faction.ScoutRng.State);
                hash.AddULong(faction.ScoutRng.Increment);
                for (int o = 0; o < faction.OrderCount; o++)
                {
                    AiOrder order = faction.Orders[o];
                    hash.AddInt(order.Id);
                    hash.AddByte((byte)order.Type);
                    hash.AddInt(order.TargetTownId);
                    hash.AddInt(order.TargetTerritoryId);
                    hash.AddInt(order.TargetNodeId);
                    hash.AddInt(order.BattalionId);
                    hash.AddInt(order.Score);
                }

                for (int d = 0; d < faction.DepotCount; d++)
                {
                    AiDepot depot = faction.Depots[d];
                    hash.AddInt(depot.TerritoryId);
                    hash.AddULong(depot.TerritoryStableKey);
                    hash.AddInt(depot.SeatTownId);
                    hash.AddInt(depot.NodeId);
                    hash.AddInt(depot.XCm);
                    hash.AddInt(depot.YCm);
                }

                for (int p = 0; p < faction.PickedPostCount; p++)
                {
                    hash.AddInt(faction.PickedPostIds[p]);
                    hash.AddInt(faction.PickedGroupIds[p]);
                }

                for (int q = 0; q < faction.PendingCount; q++)
                {
                    AiPendingCommand pending = faction.Pending[q];
                    hash.AddInt(pending.ReadyTick);
                    hash.AddInt(pending.BattalionId);
                    hash.AddByte((byte)pending.Kind);
                    hash.AddInt(pending.Arg0);
                    hash.AddInt(pending.Arg1);
                    hash.AddInt(pending.Arg2);
                }

                hash.AddInt(faction.IntentCount);
                for (int n = 0; n < faction.IntentCount; n++)
                {
                    AiPendingIntent intent = faction.Intents[n];
                    hash.AddInt(intent.Sequence);
                    hash.AddByte((byte)intent.Kind);
                    hash.AddInt(intent.BattalionId);
                    hash.AddInt(intent.TargetId);
                    hash.AddInt(intent.IssuedTick);
                }

                hash.AddInt(faction.BlacklistCount);
                for (int b = 0; b < faction.BlacklistCount; b++)
                {
                    hash.AddInt(faction.BlacklistedNodeIds[b]);
                }
            }

            hash.AddInt(_townOwnerIds.Length);
            for (int i = 0; i < _townOwnerIds.Length; i++)
            {
                hash.AddInt(_townOwnerIds[i]);
            }
        }

        public byte[] Save(ulong matchSeed, ulong balanceKey, ulong mapKey)
        {
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write(AiConstants.StateSchemaVersion);
            writer.Write(matchSeed);
            writer.Write(balanceKey);
            writer.Write(mapKey);
            writer.Write(DroppedFromLog);
            writer.Write(_factions.Length);
            for (int i = 0; i < _factions.Length; i++)
            {
                AiFactionState faction = _factions[i];
                writer.Write(faction.FactionId);
                writer.Write(faction.PersonalityId);
                writer.Write(faction.DifficultyId);
                writer.Write(faction.ReactionExtraTicks);
                writer.Write(faction.StrategicCadenceTicks);
                writer.Write(faction.MaxActiveOrders);
                writer.Write(faction.MinGarrisonPerTown);
                writer.Write(faction.ManpowerReserveSeconds);
                writer.Write(faction.MaxCandidateGoals);
                writer.Write(faction.TaskForceTargetSize);
                writer.Write(faction.ReservePpt);
                writer.Write((byte)faction.Posture);
                writer.Write(faction.NextOrderId);
                writer.Write(faction.OrderCount);
                writer.Write(faction.TaskForceCount);
                writer.Write(faction.WorkResumeId);
                writer.Write(faction.StrategicDeferred);
                writer.Write(faction.HomeTerritoryId);
                writer.Write(faction.HomeTownId);
                writer.Write(faction.HqNodeId);
                writer.Write(faction.NextBattalionId);
                writer.Write(faction.Manpower);
                writer.Write(faction.ManpowerCarry);
                writer.Write(faction.DepotCount);
                writer.Write(faction.PickedPostCount);
                writer.Write(faction.PendingCount);
                writer.Write(faction.PendingDropped);
                writer.Write(faction.DepotCapSkips);
                writer.Write(faction.AttackTargetTownId);
                writer.Write(faction.DefendTownId);
                writer.Write(faction.LastIssuedCommandId);
                writer.Write(faction.LastRejectCode);
                writer.Write(faction.RejectBackoffUntilTick);
                writer.Write(faction.RejectStreak);
                writer.Write(faction.DepotSiteTick);
                writer.Write(faction.DepotActiveTick);
                writer.Write(faction.SupplyBudgetDefers);
                WriteRng(writer, faction.StrategyRng);
                WriteRng(writer, faction.TacticalRng);
                WriteRng(writer, faction.MistakeRng);
                WriteRng(writer, faction.ScoutRng);
                for (int o = 0; o < faction.OrderCount; o++)
                {
                    AiOrder order = faction.Orders[o];
                    writer.Write(order.Id);
                    writer.Write((byte)order.Type);
                    writer.Write(order.TargetTownId);
                    writer.Write(order.TargetTerritoryId);
                    writer.Write(order.TargetNodeId);
                    writer.Write(order.BattalionId);
                    writer.Write(order.Score);
                }

                for (int d = 0; d < faction.DepotCount; d++)
                {
                    AiDepot depot = faction.Depots[d];
                    writer.Write(depot.TerritoryId);
                    writer.Write(depot.TerritoryStableKey);
                    writer.Write(depot.SeatTownId);
                    writer.Write(depot.NodeId);
                    writer.Write(depot.XCm);
                    writer.Write(depot.YCm);
                }

                for (int p = 0; p < faction.PickedPostCount; p++)
                {
                    writer.Write(faction.PickedPostIds[p]);
                    writer.Write(faction.PickedGroupIds[p]);
                }

                for (int q = 0; q < faction.PendingCount; q++)
                {
                    AiPendingCommand pending = faction.Pending[q];
                    writer.Write(pending.ReadyTick);
                    writer.Write(pending.BattalionId);
                    writer.Write((byte)pending.Kind);
                    writer.Write(pending.Arg0);
                    writer.Write(pending.Arg1);
                    writer.Write(pending.Arg2);
                }

                writer.Write(faction.IntentCount);
                for (int n = 0; n < faction.IntentCount; n++)
                {
                    AiPendingIntent intent = faction.Intents[n];
                    writer.Write(intent.Sequence);
                    writer.Write((byte)intent.Kind);
                    writer.Write(intent.BattalionId);
                    writer.Write(intent.TargetId);
                    writer.Write(intent.IssuedTick);
                }

                writer.Write(faction.BlacklistCount);
                for (int b = 0; b < faction.BlacklistCount; b++)
                {
                    writer.Write(faction.BlacklistedNodeIds[b]);
                }
            }

            writer.Write(_townOwnerIds.Length);
            for (int i = 0; i < _townOwnerIds.Length; i++)
            {
                writer.Write(_townOwnerIds[i]);
            }

            writer.Flush();
            return stream.ToArray();
        }

        public void Load(byte[] blob, ulong matchSeed, ulong balanceKey, ulong mapKey)
        {
            if (blob == null)
            {
                throw new ArgumentNullException(nameof(blob));
            }

            BinaryReader reader = new BinaryReader(new MemoryStream(blob));
            int schema = reader.ReadInt32();
            if (schema != AiConstants.StateSchemaVersion)
            {
                throw new InvalidDataException(
                    "AI state schema version " + schema
                    + " is unsupported; expected " + AiConstants.StateSchemaVersion + ".");
            }

            ulong savedSeed = reader.ReadUInt64();
            ulong savedBalance = reader.ReadUInt64();
            ulong savedMap = reader.ReadUInt64();
            if (savedSeed != matchSeed || savedBalance != balanceKey || savedMap != mapKey)
            {
                throw new InvalidDataException("AI save header does not match current match seed/balance/map.");
            }

            int droppedFromLog = reader.ReadInt32();
            if (droppedFromLog < 0)
            {
                throw new InvalidDataException("AI DroppedFromLog is negative.");
            }

            int count = reader.ReadInt32();
            if (count != _factions.Length)
            {
                throw new InvalidDataException("AI faction count changed across save/load.");
            }

            LoadedFaction[] loaded = new LoadedFaction[count];
            for (int i = 0; i < count; i++)
            {
                loaded[i] = ReadLoadedFaction(reader, _factions[i]);
            }

            int towns = reader.ReadInt32();
            if (towns != _townOwnerIds.Length)
            {
                throw new InvalidDataException("AI town slot count changed across save/load.");
            }

            int[] townOwners = new int[towns];
            for (int i = 0; i < towns; i++)
            {
                townOwners[i] = reader.ReadInt32();
                if (!IsKnownFactionId(townOwners[i]))
                {
                    throw new InvalidDataException("AI faction id is not in this match.");
                }
            }

            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("AI save has trailing bytes.");
            }

            for (int i = 0; i < count; i++)
            {
                CommitLoadedFaction(_factions[i], loaded[i]);
            }

            DroppedFromLog = droppedFromLog;
            for (int i = 0; i < towns; i++)
            {
                _townOwnerIds[i] = townOwners[i];
            }
        }

        private static LoadedFaction ReadLoadedFaction(BinaryReader reader, AiFactionState current)
        {
            int factionId = reader.ReadInt32();
            if (factionId != current.FactionId)
            {
                throw new InvalidDataException("AI faction id order changed across save/load.");
            }

            string personalityId = reader.ReadString();
            string difficultyId = reader.ReadString();
            if (personalityId != current.PersonalityId || difficultyId != current.DifficultyId)
            {
                throw new InvalidDataException("AI personality/difficulty changed across save/load.");
            }

            RequireInt32(reader, current.ReactionExtraTicks, "ReactionExtraTicks");
            RequireInt32(reader, current.StrategicCadenceTicks, "StrategicCadenceTicks");
            RequireInt32(reader, current.MaxActiveOrders, "MaxActiveOrders");
            RequireInt32(reader, current.MinGarrisonPerTown, "MinGarrisonPerTown");
            RequireInt32(reader, current.ManpowerReserveSeconds, "ManpowerReserveSeconds");
            RequireInt32(reader, current.MaxCandidateGoals, "MaxCandidateGoals");
            RequireInt32(reader, current.TaskForceTargetSize, "TaskForceTargetSize");
            RequireInt32(reader, current.ReservePpt, "ReservePpt");

            LoadedFaction loaded = new LoadedFaction();
            loaded.Posture = ReadPosture(reader);
            loaded.NextOrderId = reader.ReadInt32();
            loaded.OrderCount = reader.ReadInt32();
            loaded.TaskForceCount = reader.ReadInt32();
            loaded.WorkResumeId = reader.ReadInt32();
            loaded.StrategicDeferred = reader.ReadBoolean();
            loaded.HomeTerritoryId = reader.ReadInt32();
            loaded.HomeTownId = reader.ReadInt32();
            loaded.HqNodeId = reader.ReadInt32();
            loaded.NextBattalionId = reader.ReadInt32();
            loaded.Manpower = reader.ReadInt32();
            loaded.ManpowerCarry = reader.ReadInt32();
            if (loaded.Manpower < 0)
            {
                throw new InvalidDataException("AI manpower must be >= 0.");
            }

            if (loaded.ManpowerCarry < 0)
            {
                throw new InvalidDataException("AI manpower carry must be >= 0.");
            }
            loaded.DepotCount = reader.ReadInt32();
            loaded.PickedPostCount = reader.ReadInt32();
            loaded.PendingCount = reader.ReadInt32();
            loaded.PendingDropped = reader.ReadInt32();
            loaded.DepotCapSkips = reader.ReadInt32();
            loaded.AttackTargetTownId = reader.ReadInt32();
            loaded.DefendTownId = reader.ReadInt32();
            loaded.LastIssuedCommandId = reader.ReadInt32();
            loaded.LastRejectCode = reader.ReadInt32();
            loaded.RejectBackoffUntilTick = reader.ReadInt32();
            loaded.RejectStreak = reader.ReadInt32();
            loaded.DepotSiteTick = reader.ReadInt32();
            loaded.DepotActiveTick = reader.ReadInt32();
            loaded.SupplyBudgetDefers = reader.ReadInt32();
            if (loaded.PendingDropped < 0)
            {
                throw new InvalidDataException("AI PendingDropped is negative.");
            }

            if (loaded.DepotCapSkips < 0)
            {
                throw new InvalidDataException("AI DepotCapSkips is negative.");
            }

            if (loaded.SupplyBudgetDefers < 0)
            {
                throw new InvalidDataException("AI SupplyBudgetDefers is negative.");
            }

            if (loaded.RejectStreak < 0 || loaded.LastRejectCode < 0)
            {
                throw new InvalidDataException("AI reject state must be >= 0.");
            }

            loaded.StrategyRng = ReadRng(reader);
            loaded.TacticalRng = ReadRng(reader);
            loaded.MistakeRng = ReadRng(reader);
            loaded.ScoutRng = ReadRng(reader);
            if (loaded.OrderCount < 0
                || loaded.DepotCount < 0
                || loaded.PickedPostCount < 0
                || loaded.PendingCount < 0
                || loaded.OrderCount > current.Orders.Length
                || loaded.DepotCount > current.Depots.Length
                || loaded.PickedPostCount > current.PickedPostIds.Length
                || loaded.PendingCount > current.Pending.Length)
            {
                throw new InvalidDataException("AI collection count exceeds slot capacity.");
            }

            loaded.Orders = new AiOrder[loaded.OrderCount];
            for (int o = 0; o < loaded.OrderCount; o++)
            {
                AiOrder order = default;
                order.Id = reader.ReadInt32();
                order.Type = ReadGoalType(reader);
                order.TargetTownId = reader.ReadInt32();
                order.TargetTerritoryId = reader.ReadInt32();
                order.TargetNodeId = reader.ReadInt32();
                order.BattalionId = reader.ReadInt32();
                order.Score = reader.ReadInt32();
                loaded.Orders[o] = order;
            }

            loaded.Depots = new AiDepot[loaded.DepotCount];
            for (int d = 0; d < loaded.DepotCount; d++)
            {
                AiDepot depot = default;
                depot.TerritoryId = reader.ReadInt32();
                depot.TerritoryStableKey = reader.ReadUInt64();
                depot.SeatTownId = reader.ReadInt32();
                depot.NodeId = reader.ReadInt32();
                depot.XCm = reader.ReadInt32();
                depot.YCm = reader.ReadInt32();
                loaded.Depots[d] = depot;
            }

            loaded.PickedPostIds = new int[loaded.PickedPostCount];
            loaded.PickedGroupIds = new int[loaded.PickedPostCount];
            for (int p = 0; p < loaded.PickedPostCount; p++)
            {
                loaded.PickedPostIds[p] = reader.ReadInt32();
                loaded.PickedGroupIds[p] = reader.ReadInt32();
            }

            loaded.Pending = new AiPendingCommand[loaded.PendingCount];
            for (int q = 0; q < loaded.PendingCount; q++)
            {
                AiPendingCommand pending = default;
                pending.ReadyTick = reader.ReadInt32();
                pending.BattalionId = reader.ReadInt32();
                pending.Kind = ReadCommandKind(reader);
                pending.Arg0 = reader.ReadInt32();
                pending.Arg1 = reader.ReadInt32();
                pending.Arg2 = reader.ReadInt32();
                loaded.Pending[q] = pending;
            }

            loaded.IntentCount = reader.ReadInt32();
            if (loaded.IntentCount < 0 || loaded.IntentCount > current.Intents.Length)
            {
                throw new InvalidDataException("AI collection count exceeds slot capacity.");
            }

            loaded.Intents = new AiPendingIntent[loaded.IntentCount];
            for (int n = 0; n < loaded.IntentCount; n++)
            {
                AiPendingIntent intent = default;
                intent.Sequence = reader.ReadInt32();
                intent.Kind = ReadIssuedKind(reader);
                intent.BattalionId = reader.ReadInt32();
                intent.TargetId = reader.ReadInt32();
                intent.IssuedTick = reader.ReadInt32();
                if (intent.Sequence < 0)
                {
                    throw new InvalidDataException("AI pending intent sequence is negative.");
                }

                if (intent.IssuedTick < 0)
                {
                    throw new InvalidDataException("AI pending intent issued tick is negative.");
                }

                loaded.Intents[n] = intent;
            }

            loaded.BlacklistCount = reader.ReadInt32();
            if (loaded.BlacklistCount < 0 || loaded.BlacklistCount > current.BlacklistedNodeIds.Length)
            {
                throw new InvalidDataException("AI collection count exceeds slot capacity.");
            }

            loaded.BlacklistedNodeIds = new int[loaded.BlacklistCount];
            for (int b = 0; b < loaded.BlacklistCount; b++)
            {
                int nodeId = reader.ReadInt32();
                if (nodeId == 0)
                {
                    throw new InvalidDataException("AI blacklisted node id is zero.");
                }

                loaded.BlacklistedNodeIds[b] = nodeId;
            }

            return loaded;
        }

        private static void CommitLoadedFaction(AiFactionState faction, LoadedFaction loaded)
        {
            faction.Posture = loaded.Posture;
            faction.NextOrderId = loaded.NextOrderId;
            faction.OrderCount = loaded.OrderCount;
            faction.TaskForceCount = loaded.TaskForceCount;
            faction.WorkResumeId = loaded.WorkResumeId;
            faction.StrategicDeferred = loaded.StrategicDeferred;
            faction.HomeTerritoryId = loaded.HomeTerritoryId;
            faction.HomeTownId = loaded.HomeTownId;
            faction.HqNodeId = loaded.HqNodeId;
            faction.NextBattalionId = loaded.NextBattalionId;
            faction.Manpower = loaded.Manpower;
            faction.ManpowerCarry = loaded.ManpowerCarry;
            faction.DepotCount = loaded.DepotCount;
            faction.PickedPostCount = loaded.PickedPostCount;
            faction.PendingCount = loaded.PendingCount;
            faction.IntentCount = loaded.IntentCount;
            faction.BlacklistCount = loaded.BlacklistCount;
            faction.PendingDropped = loaded.PendingDropped;
            faction.DepotCapSkips = loaded.DepotCapSkips;
            faction.AttackTargetTownId = loaded.AttackTargetTownId;
            faction.DefendTownId = loaded.DefendTownId;
            faction.LastIssuedCommandId = loaded.LastIssuedCommandId;
            faction.LastRejectCode = loaded.LastRejectCode;
            faction.RejectBackoffUntilTick = loaded.RejectBackoffUntilTick;
            faction.RejectStreak = loaded.RejectStreak;
            faction.DepotSiteTick = loaded.DepotSiteTick;
            faction.DepotActiveTick = loaded.DepotActiveTick;
            faction.SupplyBudgetDefers = loaded.SupplyBudgetDefers;
            faction.StrategyRng = loaded.StrategyRng;
            faction.TacticalRng = loaded.TacticalRng;
            faction.MistakeRng = loaded.MistakeRng;
            faction.ScoutRng = loaded.ScoutRng;
            faction.PathRequestsThisTick = 0;
            faction.WorkUnitsThisTick = 0;
            for (int o = 0; o < loaded.OrderCount; o++)
            {
                faction.Orders[o] = loaded.Orders[o];
            }

            for (int d = 0; d < loaded.DepotCount; d++)
            {
                faction.Depots[d] = loaded.Depots[d];
            }

            for (int p = 0; p < loaded.PickedPostCount; p++)
            {
                faction.PickedPostIds[p] = loaded.PickedPostIds[p];
                faction.PickedGroupIds[p] = loaded.PickedGroupIds[p];
            }

            for (int q = 0; q < loaded.PendingCount; q++)
            {
                faction.Pending[q] = loaded.Pending[q];
            }

            for (int n = 0; n < loaded.IntentCount; n++)
            {
                faction.Intents[n] = loaded.Intents[n];
            }

            for (int b = 0; b < loaded.BlacklistCount; b++)
            {
                faction.BlacklistedNodeIds[b] = loaded.BlacklistedNodeIds[b];
            }
        }

        private static AiPosture ReadPosture(BinaryReader reader)
        {
            byte postureByte = reader.ReadByte();
            if (postureByte > (byte)AiPosture.Counterattack)
            {
                throw new InvalidDataException("AI posture byte is out of range.");
            }

            return (AiPosture)postureByte;
        }

        private static AiGoalType ReadGoalType(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value > (byte)AiGoalType.DefendTown)
            {
                throw new InvalidDataException("AI order type byte is out of range.");
            }

            return (AiGoalType)value;
        }

        private static CommandKind ReadCommandKind(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value < (byte)CommandKind.SpawnBattalion || value > (byte)CommandKind.BuildDepot)
            {
                throw new InvalidDataException("AI pending command kind byte is out of range.");
            }

            return (CommandKind)value;
        }

        private static AiIssuedKind ReadIssuedKind(BinaryReader reader)
        {
            byte value = reader.ReadByte();
            if (value != (byte)AiIssuedKind.ClaimTown && value != (byte)AiIssuedKind.BuildDepot)
            {
                throw new InvalidDataException("AI pending intent kind byte is out of range.");
            }

            return (AiIssuedKind)value;
        }

        private sealed class LoadedFaction
        {
            public AiPosture Posture;
            public int NextOrderId;
            public int OrderCount;
            public int TaskForceCount;
            public int WorkResumeId;
            public bool StrategicDeferred;
            public int HomeTerritoryId;
            public int HomeTownId;
            public int HqNodeId;
            public int NextBattalionId;
            public int Manpower;
            public int ManpowerCarry;
            public int DepotCount;
            public int PickedPostCount;
            public int PendingCount;
            public int PendingDropped;
            public int DepotCapSkips;
            public int AttackTargetTownId;
            public int DefendTownId;
            public int LastIssuedCommandId;
            public int LastRejectCode;
            public int RejectBackoffUntilTick;
            public int RejectStreak;
            public int DepotSiteTick;
            public int DepotActiveTick;
            public int SupplyBudgetDefers;
            public SimRandom StrategyRng;
            public SimRandom TacticalRng;
            public SimRandom MistakeRng;
            public SimRandom ScoutRng;
            public AiOrder[] Orders = Array.Empty<AiOrder>();
            public AiDepot[] Depots = Array.Empty<AiDepot>();
            public int[] PickedPostIds = Array.Empty<int>();
            public int[] PickedGroupIds = Array.Empty<int>();
            public AiPendingCommand[] Pending = Array.Empty<AiPendingCommand>();
            public int IntentCount;
            public AiPendingIntent[] Intents = Array.Empty<AiPendingIntent>();
            public int BlacklistCount;
            public int[] BlacklistedNodeIds = Array.Empty<int>();
        }

        private static void RequireInt32(BinaryReader reader, int expected, string name)
        {
            int actual = reader.ReadInt32();
            if (actual != expected)
            {
                throw new InvalidDataException("AI save " + name + " does not match current config.");
            }
        }

        private static void WriteRng(BinaryWriter writer, SimRandom rng)
        {
            writer.Write(rng.State);
            writer.Write(rng.Increment);
        }

        private static SimRandom ReadRng(BinaryReader reader)
        {
            ulong state = reader.ReadUInt64();
            ulong increment = reader.ReadUInt64();
            if ((increment & 1UL) == 0UL)
            {
                throw new InvalidDataException("AI RNG increment must be odd.");
            }

            try
            {
                return SimRandom.FromPersisted(state, increment);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException("AI RNG blob is invalid.", ex);
            }
        }

        private bool IsKnownFactionId(int factionId)
        {
            if (factionId == 0)
            {
                return true;
            }

            for (int i = 0; i < _factions.Length; i++)
            {
                if (_factions[i].FactionId == factionId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SortByFactionId(AiFactionState[] factions)
        {
            for (int i = 1; i < factions.Length; i++)
            {
                AiFactionState row = factions[i];
                int j = i - 1;
                while (j >= 0 && factions[j].FactionId > row.FactionId)
                {
                    factions[j + 1] = factions[j];
                    j--;
                }

                factions[j + 1] = row;
            }
        }
    }
}
