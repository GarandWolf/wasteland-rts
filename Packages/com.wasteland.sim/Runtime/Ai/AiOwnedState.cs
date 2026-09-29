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
        public readonly int EconomyBonusPpt;
        public readonly int MaxCandidateGoals;
        public readonly int TaskForceTargetSize;
        public readonly int ReservePpt;

        public AiPosture Posture;
        public int NextOrderId;
        public int OrderCount;
        public int TaskForceCount;
        public int WorkResumeId;
        public int PathRequestsThisTick;
        public int WorkUnitsThisTick;
        public bool StrategicDeferred;
        public bool OpeningForceIssued;
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
        public readonly AiOrder[] Orders;
        public readonly AiDepot[] Depots;
        public readonly int[] PickedPostIds;
        public readonly int[] PickedGroupIds;
        public readonly AiPendingCommand[] Pending;
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
            int economyBonusPpt,
            int maxCandidateGoals,
            int taskForceTargetSize,
            int reservePpt,
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
            EconomyBonusPpt = economyBonusPpt;
            MaxCandidateGoals = maxCandidateGoals;
            TaskForceTargetSize = taskForceTargetSize;
            ReservePpt = reservePpt;
            Posture = posture;
            NextOrderId = 1;
            NextBattalionId = factionId * AiConstants.BattalionIdStride + 1;
            Orders = new AiOrder[System.Math.Max(1, maxActiveOrders)];
            Depots = new AiDepot[16];
            PickedPostIds = new int[64];
            PickedGroupIds = new int[64];
            Pending = new AiPendingCommand[AiConstants.PendingCommandCapacity];
            StrategyRng = strategyRng;
            TacticalRng = tacticalRng;
            MistakeRng = mistakeRng;
            ScoutRng = scoutRng;
        }

        /// <summary>
        /// Enqueue a reaction-delayed command. If the 128-slot queue is full,
        /// drop the lowest-priority pending (or the incoming command if it is
        /// lowest). Priority is Kind rank (Spawn, Move, Build, other), then
        /// sooner <see cref="AiPendingCommand.ReadyTick"/>, then lower
        /// battalion id. Deterministic; increments <see cref="PendingDropped"/>.
        /// </summary>
        public bool EnqueuePending(AiPendingCommand pending)
        {
            if (PendingCount < Pending.Length)
            {
                Pending[PendingCount] = pending;
                PendingCount++;
                return true;
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
                return false;
            }

            Pending[worst] = pending;
            PendingDropped++;
            return true;
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

            if (kind == CommandKind.Build)
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
        private readonly int[] _captureFactionIds;
        private readonly int[] _captureHeldTicks;

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
            _captureFactionIds = new int[townCount];
            _captureHeldTicks = new int[townCount];
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
            _captureFactionIds[townIndex] = 0;
            _captureHeldTicks[townIndex] = 0;
        }

        public int CaptureFactionAt(int townIndex)
        {
            return _captureFactionIds[townIndex];
        }

        public int CaptureHeldTicksAt(int townIndex)
        {
            return _captureHeldTicks[townIndex];
        }

        public void SetCapture(int townIndex, int factionId, int heldTicks)
        {
            _captureFactionIds[townIndex] = factionId;
            _captureHeldTicks[townIndex] = heldTicks;
        }

        public void HashInto(XxHash64Builder hash)
        {
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
                hash.AddInt(faction.EconomyBonusPpt);
                hash.AddInt(faction.MaxCandidateGoals);
                hash.AddInt(faction.TaskForceTargetSize);
                hash.AddInt(faction.ReservePpt);
                hash.AddByte((byte)faction.Posture);
                hash.AddInt(faction.NextOrderId);
                hash.AddInt(faction.OrderCount);
                hash.AddInt(faction.TaskForceCount);
                hash.AddInt(faction.WorkResumeId);
                hash.AddBool(faction.StrategicDeferred);
                hash.AddBool(faction.OpeningForceIssued);
                hash.AddInt(faction.HomeTerritoryId);
                hash.AddInt(faction.HomeTownId);
                hash.AddInt(faction.HqNodeId);
                hash.AddInt(faction.NextBattalionId);
                hash.AddInt(faction.Manpower);
                hash.AddInt(faction.ManpowerCarry);
                hash.AddInt(faction.DepotCount);
                hash.AddInt(faction.PickedPostCount);
                hash.AddInt(faction.PendingCount);
                hash.AddInt(faction.PendingDropped);
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
            }

            hash.AddInt(_townOwnerIds.Length);
            for (int i = 0; i < _townOwnerIds.Length; i++)
            {
                hash.AddInt(_townOwnerIds[i]);
                hash.AddInt(_captureFactionIds[i]);
                hash.AddInt(_captureHeldTicks[i]);
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
                writer.Write(faction.EconomyBonusPpt);
                writer.Write(faction.MaxCandidateGoals);
                writer.Write(faction.TaskForceTargetSize);
                writer.Write(faction.ReservePpt);
                writer.Write((byte)faction.Posture);
                writer.Write(faction.NextOrderId);
                writer.Write(faction.OrderCount);
                writer.Write(faction.TaskForceCount);
                writer.Write(faction.WorkResumeId);
                writer.Write(faction.StrategicDeferred);
                writer.Write(faction.OpeningForceIssued);
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
            }

            writer.Write(_townOwnerIds.Length);
            for (int i = 0; i < _townOwnerIds.Length; i++)
            {
                writer.Write(_townOwnerIds[i]);
                writer.Write(_captureFactionIds[i]);
                writer.Write(_captureHeldTicks[i]);
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
                throw new InvalidDataException("Unsupported AI state schema.");
            }

            ulong savedSeed = reader.ReadUInt64();
            ulong savedBalance = reader.ReadUInt64();
            ulong savedMap = reader.ReadUInt64();
            if (savedSeed != matchSeed || savedBalance != balanceKey || savedMap != mapKey)
            {
                throw new InvalidDataException("AI save header does not match current match seed/balance/map.");
            }

            int count = reader.ReadInt32();
            if (count != _factions.Length)
            {
                throw new InvalidDataException("AI faction count changed across save/load.");
            }

            for (int i = 0; i < count; i++)
            {
                int factionId = reader.ReadInt32();
                if (factionId != _factions[i].FactionId)
                {
                    throw new InvalidDataException("AI faction id order changed across save/load.");
                }

                string personalityId = reader.ReadString();
                string difficultyId = reader.ReadString();
                if (personalityId != _factions[i].PersonalityId || difficultyId != _factions[i].DifficultyId)
                {
                    throw new InvalidDataException("AI personality/difficulty changed across save/load.");
                }

                RequireInt32(reader, _factions[i].ReactionExtraTicks, "ReactionExtraTicks");
                RequireInt32(reader, _factions[i].StrategicCadenceTicks, "StrategicCadenceTicks");
                RequireInt32(reader, _factions[i].MaxActiveOrders, "MaxActiveOrders");
                RequireInt32(reader, _factions[i].MinGarrisonPerTown, "MinGarrisonPerTown");
                RequireInt32(reader, _factions[i].ManpowerReserveSeconds, "ManpowerReserveSeconds");
                RequireInt32(reader, _factions[i].EconomyBonusPpt, "EconomyBonusPpt");
                RequireInt32(reader, _factions[i].MaxCandidateGoals, "MaxCandidateGoals");
                RequireInt32(reader, _factions[i].TaskForceTargetSize, "TaskForceTargetSize");
                RequireInt32(reader, _factions[i].ReservePpt, "ReservePpt");
                _factions[i].Posture = (AiPosture)reader.ReadByte();
                _factions[i].NextOrderId = reader.ReadInt32();
                _factions[i].OrderCount = reader.ReadInt32();
                _factions[i].TaskForceCount = reader.ReadInt32();
                _factions[i].WorkResumeId = reader.ReadInt32();
                _factions[i].StrategicDeferred = reader.ReadBoolean();
                _factions[i].OpeningForceIssued = reader.ReadBoolean();
                _factions[i].HomeTerritoryId = reader.ReadInt32();
                _factions[i].HomeTownId = reader.ReadInt32();
                _factions[i].HqNodeId = reader.ReadInt32();
                _factions[i].NextBattalionId = reader.ReadInt32();
                _factions[i].Manpower = reader.ReadInt32();
                _factions[i].ManpowerCarry = reader.ReadInt32();
                _factions[i].DepotCount = reader.ReadInt32();
                _factions[i].PickedPostCount = reader.ReadInt32();
                _factions[i].PendingCount = reader.ReadInt32();
                _factions[i].PendingDropped = reader.ReadInt32();
                _factions[i].StrategyRng = ReadRng(reader);
                _factions[i].TacticalRng = ReadRng(reader);
                _factions[i].MistakeRng = ReadRng(reader);
                _factions[i].ScoutRng = ReadRng(reader);
                _factions[i].PathRequestsThisTick = 0;
                _factions[i].WorkUnitsThisTick = 0;
                if (_factions[i].OrderCount > _factions[i].Orders.Length
                    || _factions[i].DepotCount > _factions[i].Depots.Length
                    || _factions[i].PickedPostCount > _factions[i].PickedPostIds.Length
                    || _factions[i].PendingCount > _factions[i].Pending.Length)
                {
                    throw new InvalidDataException("AI collection count exceeds slot capacity.");
                }

                for (int o = 0; o < _factions[i].OrderCount; o++)
                {
                    AiOrder order = default;
                    order.Id = reader.ReadInt32();
                    order.Type = (AiGoalType)reader.ReadByte();
                    order.TargetTownId = reader.ReadInt32();
                    order.TargetTerritoryId = reader.ReadInt32();
                    order.TargetNodeId = reader.ReadInt32();
                    order.BattalionId = reader.ReadInt32();
                    order.Score = reader.ReadInt32();
                    _factions[i].Orders[o] = order;
                }

                for (int d = 0; d < _factions[i].DepotCount; d++)
                {
                    AiDepot depot = default;
                    depot.TerritoryId = reader.ReadInt32();
                    depot.TerritoryStableKey = reader.ReadUInt64();
                    depot.SeatTownId = reader.ReadInt32();
                    depot.NodeId = reader.ReadInt32();
                    depot.XCm = reader.ReadInt32();
                    depot.YCm = reader.ReadInt32();
                    _factions[i].Depots[d] = depot;
                }

                for (int p = 0; p < _factions[i].PickedPostCount; p++)
                {
                    _factions[i].PickedPostIds[p] = reader.ReadInt32();
                    _factions[i].PickedGroupIds[p] = reader.ReadInt32();
                }

                for (int q = 0; q < _factions[i].PendingCount; q++)
                {
                    AiPendingCommand pending = default;
                    pending.ReadyTick = reader.ReadInt32();
                    pending.BattalionId = reader.ReadInt32();
                    pending.Kind = (CommandKind)reader.ReadByte();
                    pending.Arg0 = reader.ReadInt32();
                    pending.Arg1 = reader.ReadInt32();
                    pending.Arg2 = reader.ReadInt32();
                    _factions[i].Pending[q] = pending;
                }
            }

            int towns = reader.ReadInt32();
            if (towns != _townOwnerIds.Length)
            {
                throw new InvalidDataException("AI town slot count changed across save/load.");
            }

            for (int i = 0; i < towns; i++)
            {
                _townOwnerIds[i] = reader.ReadInt32();
                _captureFactionIds[i] = reader.ReadInt32();
                _captureHeldTicks[i] = reader.ReadInt32();
            }
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
            return SimRandom.FromPersisted(state, increment);
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
