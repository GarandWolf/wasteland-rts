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

    public enum AiDepotBlacklistKind : byte
    {
        Transient = 0,
        Placement = 1
    }

    /// <summary>
    /// One skipped depot node on the shared 128-slot blacklist, keyed by
    /// (node, seat). Saved and hashed as a fixed-size array sorted by node,
    /// then seat. Seats do not own separate lists.
    /// </summary>
    public struct AiDepotBlacklistEntry
    {
        public int NodeId;
        public int SeatId;
        public int AddedTick;
        public AiDepotBlacklistKind Kind;
        public int TerritoryId;
        public int OwnerFactionId;
    }

    /// <summary>
    /// Accepted ClaimTowns for one (seat, town) in the current ownership
    /// period. Saved and hashed as a fixed-size array sorted by seat then town.
    /// </summary>
    public struct AiClaimTownCount
    {
        public int SeatId;
        public int TownId;
        public int Count;
    }

    /// <summary>
    /// One backed-off order key (seat, unit, kind, target). Saved and hashed
    /// as a fixed-size array sorted by those four fields.
    /// </summary>
    public struct AiOrderBackoffEntry
    {
        public int SeatId;
        public int BattalionId;
        public byte Kind;
        public int TargetId;
        public int UntilTick;
        public int Streak;
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
        // Shared 128-slot list for every seat, keyed by (node, seat).
        private readonly AiDepotBlacklistEntry[] _blacklist;
        private readonly AiOrderBackoffEntry[] _backoff;
        private readonly AiClaimTownCount[] _claimTowns;
        private readonly int[] _lastCaptureFaction;
        private readonly int[] _lastCaptureProgress;
        private int[] _matchSeatIds;

        public int DroppedFromLog;
        public int BlacklistCount;
        public int BackoffCount;
        public bool CaptureWatchReady;

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
            _blacklist = new AiDepotBlacklistEntry[AiConstants.DepotNodeBlacklistCapacity];
            _backoff = new AiOrderBackoffEntry[AiConstants.OrderBackoffCapacity];
            _claimTowns = new AiClaimTownCount[AiConstants.OrderBackoffCapacity];
            _lastCaptureFaction = new int[townCount];
            _lastCaptureProgress = new int[townCount];
            _matchSeatIds = new int[_factions.Length];
            for (int i = 0; i < _factions.Length; i++)
            {
                _matchSeatIds[i] = _factions[i].FactionId;
            }
        }

        public int ClaimTownCount;

        public AiDepotBlacklistEntry[] DepotBlacklist
        {
            get { return _blacklist; }
        }

        public AiOrderBackoffEntry[] OrderBackoff
        {
            get { return _backoff; }
        }

        public int FactionCount
        {
            get { return _factions.Length; }
        }

        public int TownSlotCount
        {
            get { return _townOwnerIds.Length; }
        }

        public AiClaimTownCount ClaimTownAt(int index)
        {
            if (index < 0 || index >= ClaimTownCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _claimTowns[index];
        }

        public int LastCaptureFactionAt(int index)
        {
            if (index < 0 || index >= _lastCaptureFaction.Length)
            {
                return 0;
            }

            return _lastCaptureFaction[index];
        }

        public int LastCaptureProgressAt(int index)
        {
            if (index < 0 || index >= _lastCaptureProgress.Length)
            {
                return 0;
            }

            return _lastCaptureProgress[index];
        }

        public void SetCaptureWatch(int index, int factionId, int progress)
        {
            if (index < 0 || index >= _lastCaptureFaction.Length)
            {
                return;
            }

            _lastCaptureFaction[index] = factionId;
            _lastCaptureProgress[index] = progress;
        }

        /// <summary>
        /// World seats in this match, including a human MVP seat that is not
        /// an AI faction. Town-owner load uses this list; backoff still
        /// requires <see cref="IsKnownFactionId"/>.
        /// </summary>
        public void BindMatchSeatIds(int[] seatIds)
        {
            if (seatIds == null || seatIds.Length < 1)
            {
                throw new ArgumentException("At least one match seat is required.", nameof(seatIds));
            }

            int[] copy = (int[])seatIds.Clone();
            SortIds(copy);
            int write = 0;
            for (int i = 0; i < copy.Length; i++)
            {
                if (copy[i] == 0)
                {
                    continue;
                }

                if (write > 0 && copy[i] == copy[write - 1])
                {
                    continue;
                }

                copy[write++] = copy[i];
            }

            if (write < 1)
            {
                throw new ArgumentException("At least one match seat is required.", nameof(seatIds));
            }

            int[] bound = new int[write];
            for (int i = 0; i < write; i++)
            {
                bound[i] = copy[i];
            }

            _matchSeatIds = bound;
        }

        public AiFactionState FactionAt(int index)
        {
            return _factions[index];
        }

        public AiFactionState GetFaction(int factionId)
        {
            AiFactionState faction;
            if (TryGetFaction(factionId, out faction))
            {
                return faction;
            }

            throw new InvalidOperationException("Unknown AI faction " + factionId + ".");
        }

        public bool TryGetFaction(int factionId, out AiFactionState faction)
        {
            for (int i = 0; i < _factions.Length; i++)
            {
                if (_factions[i].FactionId == factionId)
                {
                    faction = _factions[i];
                    return true;
                }
            }

            faction = null!;
            return false;
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

        public bool IsDepotNodeBlacklisted(int seatId, int nodeId)
        {
            return IndexOfBlacklist(seatId, nodeId) >= 0;
        }

        public void BlacklistDepotNode(
            int seatId,
            int nodeId,
            int tick,
            AiDepotBlacklistKind kind,
            int territoryId,
            int ownerFactionId)
        {
            if (nodeId == 0 || seatId == 0)
            {
                return;
            }

            if (IndexOfBlacklist(seatId, nodeId) >= 0)
            {
                return;
            }

            AiDepotBlacklistEntry entry = default;
            entry.NodeId = nodeId;
            entry.SeatId = seatId;
            entry.AddedTick = tick;
            entry.Kind = kind;
            entry.TerritoryId = territoryId;
            entry.OwnerFactionId = ownerFactionId;
            if (BlacklistCount < _blacklist.Length)
            {
                _blacklist[BlacklistCount] = entry;
                BlacklistCount++;
                SortBlacklist();
                return;
            }

            int victim = 0;
            for (int i = 1; i < BlacklistCount; i++)
            {
                if (CompareBlacklistAge(_blacklist[i], _blacklist[victim]) < 0)
                {
                    victim = i;
                }
            }

            _blacklist[victim] = entry;
            SortBlacklist();
        }

        public void RemoveBlacklistAt(int index)
        {
            if (index < 0 || index >= BlacklistCount)
            {
                return;
            }

            for (int i = index + 1; i < BlacklistCount; i++)
            {
                _blacklist[i - 1] = _blacklist[i];
            }

            BlacklistCount--;
            _blacklist[BlacklistCount] = default;
        }

        private int IndexOfBlacklist(int seatId, int nodeId)
        {
            for (int i = 0; i < BlacklistCount; i++)
            {
                if (_blacklist[i].NodeId == nodeId && _blacklist[i].SeatId == seatId)
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
                AiDepotBlacklistEntry row = _blacklist[i];
                int j = i;
                while (j > 0 && CompareBlacklistKey(_blacklist[j - 1], row) > 0)
                {
                    _blacklist[j] = _blacklist[j - 1];
                    j--;
                }

                _blacklist[j] = row;
            }
        }

        public static int CompareBlacklistKey(AiDepotBlacklistEntry a, AiDepotBlacklistEntry b)
        {
            int c = a.NodeId.CompareTo(b.NodeId);
            if (c != 0)
            {
                return c;
            }

            return a.SeatId.CompareTo(b.SeatId);
        }

        public static int CompareBlacklistAge(AiDepotBlacklistEntry a, AiDepotBlacklistEntry b)
        {
            int c = a.AddedTick.CompareTo(b.AddedTick);
            if (c != 0)
            {
                return c;
            }

            c = a.NodeId.CompareTo(b.NodeId);
            if (c != 0)
            {
                return c;
            }

            return a.SeatId.CompareTo(b.SeatId);
        }

        public bool IsOrderBackedOff(int seatId, int battalionId, CommandKind kind, int targetId, int tick)
        {
            int index = IndexOfBackoff(seatId, battalionId, (byte)kind, targetId);
            if (index < 0)
            {
                return false;
            }

            return tick < _backoff[index].UntilTick;
        }

        public void NoteOrderReject(int seatId, int battalionId, CommandKind kind, int targetId, int tick)
        {
            if (seatId == 0 || battalionId == 0)
            {
                return;
            }

            byte kindByte = (byte)kind;
            int index = IndexOfBackoff(seatId, battalionId, kindByte, targetId);
            int streak = 1;
            if (index >= 0)
            {
                streak = _backoff[index].Streak + 1;
                if (streak < 1)
                {
                    streak = 1;
                }
            }

            int delay = AiAttackDefend.RejectBackoffStartTicks;
            for (int i = 1; i < streak; i++)
            {
                if (delay >= AiAttackDefend.RejectBackoffMaxTicks / 2)
                {
                    delay = AiAttackDefend.RejectBackoffMaxTicks;
                    break;
                }

                delay *= 2;
            }

            AiOrderBackoffEntry entry = default;
            entry.SeatId = seatId;
            entry.BattalionId = battalionId;
            entry.Kind = kindByte;
            entry.TargetId = targetId;
            entry.UntilTick = tick + delay;
            entry.Streak = streak;
            if (index >= 0)
            {
                _backoff[index] = entry;
                SortBackoff();
                return;
            }

            if (BackoffCount < _backoff.Length)
            {
                _backoff[BackoffCount] = entry;
                BackoffCount++;
                SortBackoff();
                return;
            }

            int victim = 0;
            for (int i = 1; i < BackoffCount; i++)
            {
                if (CompareBackoffAge(_backoff[i], _backoff[victim]) < 0)
                {
                    victim = i;
                }
            }

            _backoff[victim] = entry;
            SortBackoff();
        }

        public void NoteOrderSuccess(int seatId, int battalionId, CommandKind kind, int targetId)
        {
            int index = IndexOfBackoff(seatId, battalionId, (byte)kind, targetId);
            if (index < 0)
            {
                return;
            }

            for (int i = index; i < BackoffCount - 1; i++)
            {
                _backoff[i] = _backoff[i + 1];
            }

            _backoff[BackoffCount - 1] = default;
            BackoffCount--;
        }

        /// <summary>
        /// Live backoff still uses <see cref="AiOrderBackoffEntry.UntilTick"/>.
        /// Expired rows stay so the next reject on the same key can double.
        /// Only <see cref="NoteOrderSuccess"/> clears a streak.
        /// </summary>
        public void ExpireOrderBackoff(int tick)
        {
            _ = tick;
        }

        public bool HasBackoffForBattalion(int seatId, int battalionId, int tick)
        {
            for (int i = 0; i < BackoffCount; i++)
            {
                AiOrderBackoffEntry row = _backoff[i];
                if (row.SeatId == seatId
                    && row.BattalionId == battalionId
                    && tick < row.UntilTick)
                {
                    return true;
                }
            }

            return false;
        }

        public int AcceptedClaimTowns(int seatId, int townId)
        {
            int index = IndexOfClaimTown(seatId, townId);
            if (index < 0)
            {
                return 0;
            }

            return _claimTowns[index].Count;
        }

        public bool NoteAcceptedClaimTown(int seatId, int townId)
        {
            if (seatId == 0 || townId == 0)
            {
                return false;
            }

            int index = IndexOfClaimTown(seatId, townId);
            if (index >= 0)
            {
                AiClaimTownCount have = _claimTowns[index];
                if (have.Count >= AiConstants.MaxClaimTownsPerSeatTown)
                {
                    return false;
                }

                have.Count++;
                _claimTowns[index] = have;
                SortClaimTowns();
                return true;
            }

            if (ClaimTownCount >= _claimTowns.Length)
            {
                return false;
            }

            AiClaimTownCount entry = default;
            entry.SeatId = seatId;
            entry.TownId = townId;
            entry.Count = 1;
            _claimTowns[ClaimTownCount] = entry;
            ClaimTownCount++;
            SortClaimTowns();
            return true;
        }

        public void ResetClaimTownsForTown(int townId)
        {
            if (townId == 0)
            {
                return;
            }

            int write = 0;
            for (int i = 0; i < ClaimTownCount; i++)
            {
                if (_claimTowns[i].TownId == townId)
                {
                    continue;
                }

                if (write != i)
                {
                    _claimTowns[write] = _claimTowns[i];
                }

                write++;
            }

            for (int i = write; i < ClaimTownCount; i++)
            {
                _claimTowns[i] = default;
            }

            ClaimTownCount = write;
        }

        public bool HasPendingClaimTown(int factionId, int townId)
        {
            AiFactionState faction;
            if (!TryGetFaction(factionId, out faction))
            {
                return false;
            }

            for (int i = 0; i < faction.IntentCount; i++)
            {
                AiPendingIntent intent = faction.Intents[i];
                if (intent.Kind == AiIssuedKind.ClaimTown && intent.TargetId == townId)
                {
                    return true;
                }
            }

            return false;
        }

        private int IndexOfClaimTown(int seatId, int townId)
        {
            for (int i = 0; i < ClaimTownCount; i++)
            {
                if (_claimTowns[i].SeatId == seatId && _claimTowns[i].TownId == townId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfBackoff(int seatId, int battalionId, byte kind, int targetId)
        {
            for (int i = 0; i < BackoffCount; i++)
            {
                AiOrderBackoffEntry row = _backoff[i];
                if (row.SeatId == seatId
                    && row.BattalionId == battalionId
                    && row.Kind == kind
                    && row.TargetId == targetId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SortBackoff()
        {
            for (int i = 1; i < BackoffCount; i++)
            {
                AiOrderBackoffEntry row = _backoff[i];
                int j = i;
                while (j > 0 && CompareBackoffKey(_backoff[j - 1], row) > 0)
                {
                    _backoff[j] = _backoff[j - 1];
                    j--;
                }

                _backoff[j] = row;
            }
        }

        public static int CompareBackoffKey(AiOrderBackoffEntry a, AiOrderBackoffEntry b)
        {
            int c = a.SeatId.CompareTo(b.SeatId);
            if (c != 0)
            {
                return c;
            }

            c = a.BattalionId.CompareTo(b.BattalionId);
            if (c != 0)
            {
                return c;
            }

            c = a.Kind.CompareTo(b.Kind);
            if (c != 0)
            {
                return c;
            }

            return a.TargetId.CompareTo(b.TargetId);
        }

        public static int CompareBackoffAge(AiOrderBackoffEntry a, AiOrderBackoffEntry b)
        {
            int c = a.UntilTick.CompareTo(b.UntilTick);
            if (c != 0)
            {
                return c;
            }

            return CompareBackoffKey(a, b);
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
            }

            HashBlacklist(hash);
            HashBackoff(hash);
            HashClaimTowns(hash);
            hash.AddInt(_townOwnerIds.Length);
            for (int i = 0; i < _townOwnerIds.Length; i++)
            {
                hash.AddInt(_townOwnerIds[i]);
            }

            hash.AddBool(CaptureWatchReady);
            for (int i = 0; i < _lastCaptureFaction.Length; i++)
            {
                hash.AddInt(_lastCaptureFaction[i]);
                hash.AddInt(_lastCaptureProgress[i]);
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
            }

            WriteBlacklist(writer);
            WriteBackoff(writer);
            writer.Write(_townOwnerIds.Length);
            for (int i = 0; i < _townOwnerIds.Length; i++)
            {
                writer.Write(_townOwnerIds[i]);
            }

            WriteClaimTowns(writer);
            writer.Write(CaptureWatchReady);
            for (int i = 0; i < _lastCaptureFaction.Length; i++)
            {
                writer.Write(_lastCaptureFaction[i]);
                writer.Write(_lastCaptureProgress[i]);
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

            AiDepotBlacklistEntry[] loadedBlacklist;
            int loadedBlacklistCount;
            ReadBlacklist(reader, out loadedBlacklist, out loadedBlacklistCount);

            AiOrderBackoffEntry[] loadedBackoff;
            int loadedBackoffCount;
            ReadBackoff(reader, out loadedBackoff, out loadedBackoffCount);

            int towns = reader.ReadInt32();
            if (towns != _townOwnerIds.Length)
            {
                throw new InvalidDataException("AI town slot count changed across save/load.");
            }

            int[] townOwners = new int[towns];
            for (int i = 0; i < towns; i++)
            {
                townOwners[i] = reader.ReadInt32();
                if (!IsKnownTownOwnerId(townOwners[i]))
                {
                    throw new InvalidDataException("AI faction id is not in this match.");
                }
            }

            AiClaimTownCount[] loadedClaims;
            int loadedClaimCount;
            ReadClaimTowns(reader, out loadedClaims, out loadedClaimCount);

            bool captureWatchReady = reader.ReadBoolean();
            int[] lastCaptureFaction = new int[towns];
            int[] lastCaptureProgress = new int[towns];
            for (int i = 0; i < towns; i++)
            {
                lastCaptureFaction[i] = reader.ReadInt32();
                lastCaptureProgress[i] = reader.ReadInt32();
                if (lastCaptureFaction[i] != 0 && !IsKnownTownOwnerId(lastCaptureFaction[i]))
                {
                    throw new InvalidDataException("AI faction id is not in this match.");
                }

                if (lastCaptureProgress[i] < 0)
                {
                    throw new InvalidDataException("AI capture progress is negative.");
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

            BlacklistCount = loadedBlacklistCount;
            for (int b = 0; b < _blacklist.Length; b++)
            {
                _blacklist[b] = loadedBlacklist[b];
            }

            BackoffCount = loadedBackoffCount;
            for (int k = 0; k < _backoff.Length; k++)
            {
                _backoff[k] = loadedBackoff[k];
            }

            DroppedFromLog = droppedFromLog;
            for (int i = 0; i < towns; i++)
            {
                _townOwnerIds[i] = townOwners[i];
            }

            ClaimTownCount = loadedClaimCount;
            for (int c = 0; c < _claimTowns.Length; c++)
            {
                _claimTowns[c] = loadedClaims[c];
            }

            CaptureWatchReady = captureWatchReady;
            for (int i = 0; i < towns; i++)
            {
                _lastCaptureFaction[i] = lastCaptureFaction[i];
                _lastCaptureProgress[i] = lastCaptureProgress[i];
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
        }

        private void HashBlacklist(XxHash64Builder hash)
        {
            for (int i = 0; i < _blacklist.Length; i++)
            {
                AiDepotBlacklistEntry entry = _blacklist[i];
                hash.AddInt(entry.NodeId);
                hash.AddInt(entry.SeatId);
                hash.AddInt(entry.AddedTick);
                hash.AddByte((byte)entry.Kind);
                hash.AddInt(entry.TerritoryId);
                hash.AddInt(entry.OwnerFactionId);
            }
        }

        private void WriteBlacklist(BinaryWriter writer)
        {
            for (int i = 0; i < _blacklist.Length; i++)
            {
                AiDepotBlacklistEntry entry = _blacklist[i];
                writer.Write(entry.NodeId);
                writer.Write(entry.SeatId);
                writer.Write(entry.AddedTick);
                writer.Write((byte)entry.Kind);
                writer.Write(entry.TerritoryId);
                writer.Write(entry.OwnerFactionId);
            }
        }

        private void HashBackoff(XxHash64Builder hash)
        {
            for (int i = 0; i < _backoff.Length; i++)
            {
                AiOrderBackoffEntry entry = _backoff[i];
                hash.AddInt(entry.SeatId);
                hash.AddInt(entry.BattalionId);
                hash.AddByte(entry.Kind);
                hash.AddInt(entry.TargetId);
                hash.AddInt(entry.UntilTick);
                hash.AddInt(entry.Streak);
            }
        }

        private void WriteBackoff(BinaryWriter writer)
        {
            for (int i = 0; i < _backoff.Length; i++)
            {
                AiOrderBackoffEntry entry = _backoff[i];
                writer.Write(entry.SeatId);
                writer.Write(entry.BattalionId);
                writer.Write(entry.Kind);
                writer.Write(entry.TargetId);
                writer.Write(entry.UntilTick);
                writer.Write(entry.Streak);
            }
        }

        private void ReadBackoff(
            BinaryReader reader,
            out AiOrderBackoffEntry[] loaded,
            out int count)
        {
            loaded = new AiOrderBackoffEntry[_backoff.Length];
            count = 0;
            for (int i = 0; i < loaded.Length; i++)
            {
                AiOrderBackoffEntry entry = default;
                entry.SeatId = reader.ReadInt32();
                entry.BattalionId = reader.ReadInt32();
                entry.Kind = reader.ReadByte();
                entry.TargetId = reader.ReadInt32();
                entry.UntilTick = reader.ReadInt32();
                entry.Streak = reader.ReadInt32();
                loaded[i] = entry;
                if (entry.SeatId == 0 && entry.BattalionId == 0 && entry.Kind == 0
                    && entry.TargetId == 0 && entry.UntilTick == 0 && entry.Streak == 0)
                {
                    continue;
                }

                if (entry.SeatId == 0 || entry.BattalionId == 0)
                {
                    throw new InvalidDataException("AI order backoff seat or battalion is zero.");
                }

                if (entry.Kind < (byte)CommandKind.Move || entry.Kind > (byte)CommandKind.BuildDepot)
                {
                    throw new InvalidDataException("AI order backoff kind byte is out of range.");
                }

                if (entry.UntilTick < 0 || entry.Streak < 0)
                {
                    throw new InvalidDataException("AI order backoff tick or streak is negative.");
                }

                if (!IsKnownFactionId(entry.SeatId))
                {
                    throw new InvalidDataException("AI faction id is not in this match.");
                }

                if (count != i)
                {
                    throw new InvalidDataException("AI order backoff has a hole before unused slots.");
                }

                if (count > 0 && CompareBackoffKey(loaded[count - 1], entry) >= 0)
                {
                    throw new InvalidDataException(
                        "AI order backoff is not sorted by seat, battalion, kind, then target.");
                }

                count++;
            }
        }

        private void ReadBlacklist(
            BinaryReader reader,
            out AiDepotBlacklistEntry[] loaded,
            out int count)
        {
            loaded = new AiDepotBlacklistEntry[_blacklist.Length];
            count = 0;
            for (int i = 0; i < loaded.Length; i++)
            {
                AiDepotBlacklistEntry entry = default;
                entry.NodeId = reader.ReadInt32();
                entry.SeatId = reader.ReadInt32();
                entry.AddedTick = reader.ReadInt32();
                byte kindByte = reader.ReadByte();
                if (kindByte > (byte)AiDepotBlacklistKind.Placement)
                {
                    throw new InvalidDataException("AI depot blacklist kind byte is out of range.");
                }

                entry.Kind = (AiDepotBlacklistKind)kindByte;
                entry.TerritoryId = reader.ReadInt32();
                entry.OwnerFactionId = reader.ReadInt32();
                loaded[i] = entry;
                if (entry.NodeId == 0 && entry.SeatId == 0 && entry.AddedTick == 0
                    && entry.Kind == AiDepotBlacklistKind.Transient
                    && entry.TerritoryId == 0 && entry.OwnerFactionId == 0)
                {
                    continue;
                }

                if (entry.NodeId == 0 || entry.SeatId == 0)
                {
                    throw new InvalidDataException("AI depot blacklist node or seat is zero.");
                }

                if (entry.AddedTick < 0)
                {
                    throw new InvalidDataException("AI depot blacklist tick is negative.");
                }

                if (!IsKnownFactionId(entry.SeatId))
                {
                    throw new InvalidDataException("AI faction id is not in this match.");
                }

                if (count != i)
                {
                    throw new InvalidDataException("AI depot blacklist has a hole before unused slots.");
                }

                if (count > 0 && CompareBlacklistKey(loaded[count - 1], entry) >= 0)
                {
                    throw new InvalidDataException("AI depot blacklist is not sorted by node then seat.");
                }

                count++;
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

        /// <summary>
        /// Town owners follow the world, including a human MVP seat that is
        /// not in <see cref="_factions"/>. Backoff and blacklist still require
        /// an AI faction so a human reject cannot load into this match.
        /// </summary>
        private bool IsKnownTownOwnerId(int factionId)
        {
            if (IsKnownFactionId(factionId))
            {
                return true;
            }

            for (int i = 0; i < _matchSeatIds.Length; i++)
            {
                if (_matchSeatIds[i] == factionId)
                {
                    return true;
                }
            }

            return false;
        }

        private void SortClaimTowns()
        {
            for (int i = 1; i < ClaimTownCount; i++)
            {
                AiClaimTownCount row = _claimTowns[i];
                int j = i;
                while (j > 0 && CompareClaimTownKey(_claimTowns[j - 1], row) > 0)
                {
                    _claimTowns[j] = _claimTowns[j - 1];
                    j--;
                }

                _claimTowns[j] = row;
            }
        }

        public static int CompareClaimTownKey(AiClaimTownCount a, AiClaimTownCount b)
        {
            int c = a.SeatId.CompareTo(b.SeatId);
            if (c != 0)
            {
                return c;
            }

            return a.TownId.CompareTo(b.TownId);
        }

        private void HashClaimTowns(XxHash64Builder hash)
        {
            for (int i = 0; i < _claimTowns.Length; i++)
            {
                AiClaimTownCount entry = _claimTowns[i];
                hash.AddInt(entry.SeatId);
                hash.AddInt(entry.TownId);
                hash.AddInt(entry.Count);
            }
        }

        private void WriteClaimTowns(BinaryWriter writer)
        {
            for (int i = 0; i < _claimTowns.Length; i++)
            {
                AiClaimTownCount entry = _claimTowns[i];
                writer.Write(entry.SeatId);
                writer.Write(entry.TownId);
                writer.Write(entry.Count);
            }
        }

        private void ReadClaimTowns(
            BinaryReader reader,
            out AiClaimTownCount[] loaded,
            out int count)
        {
            loaded = new AiClaimTownCount[_claimTowns.Length];
            count = 0;
            for (int i = 0; i < loaded.Length; i++)
            {
                AiClaimTownCount entry = default;
                entry.SeatId = reader.ReadInt32();
                entry.TownId = reader.ReadInt32();
                entry.Count = reader.ReadInt32();
                loaded[i] = entry;
                if (entry.SeatId == 0 && entry.TownId == 0 && entry.Count == 0)
                {
                    continue;
                }

                if (entry.SeatId == 0 || entry.TownId == 0)
                {
                    throw new InvalidDataException("AI claim-town seat or town is zero.");
                }

                if (entry.Count < 1 || entry.Count > AiConstants.MaxClaimTownsPerSeatTown)
                {
                    throw new InvalidDataException("AI claim-town count is out of range.");
                }

                if (!IsKnownFactionId(entry.SeatId))
                {
                    throw new InvalidDataException("AI faction id is not in this match.");
                }

                if (count != i)
                {
                    throw new InvalidDataException("AI claim-town list has a hole before unused slots.");
                }

                if (count > 0 && CompareClaimTownKey(loaded[count - 1], entry) >= 0)
                {
                    throw new InvalidDataException("AI claim-town list is not sorted by seat then town.");
                }

                count++;
            }
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
