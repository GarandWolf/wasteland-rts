using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Enemy AI: claim nearby towns; raise and place depots only through
    /// <see cref="AiAttackDefend"/>. Orders go only through
    /// <see cref="ISimCommandSink"/>.
    /// </summary>
    public sealed class EnemyAi
    {
        private readonly ISimCommandSink _commands;
        private readonly ISimWorldView _world;
        private readonly IAiEconomy _economy;
        private readonly IAiScenarioSeats _seats;
        private readonly AiSeatHomeBinder _homes;
        private readonly AiBalanceTables _balance;
        private readonly AiMapSnapshot _map;
        private readonly AiOwnedState _owned;
        private readonly AiDifficultyProfile _difficulty;
        private readonly ulong _matchSeed;
        private readonly ulong _balanceKey;
        private readonly ulong _mapKey;
        private readonly ulong _adjacencyHash;
        private readonly int _commandDelayTicks;
        private readonly List<int> _idScratch = new List<int>();
        private readonly List<int> _prizeIdScratch = new List<int>();
        private readonly List<int> _countIdScratch = new List<int>();
        private readonly List<TownView> _townScratch = new List<TownView>();
        private readonly List<SimEvent> _eventScratch = new List<SimEvent>();
        private readonly List<DepotView> _depotScratch = new List<DepotView>();
        private readonly Candidate[] _candidates;
        private readonly int[] _networkScratch;
        private readonly IAiCommandSink _aiCommands;
        // Friday: assign the Lead's live per-seat view here instead of the fixture.
        private IAiSeatView _seatView;
        private readonly FixtureAiSeatView _fixtureSeatView = new FixtureAiSeatView();
        private readonly IAiDepotQuery _depots;
        private readonly AiAttackDefend _attackDefend;
        private readonly AiAttackTownInfo[] _attackTowns;
        private AiBattalionPin[] _attackBattalions;
        private readonly List<ProductionQueueItem> _productionQueue = new List<ProductionQueueItem>();
        private readonly string[] _queuedUnitIds = new string[AiConstants.PendingCommandCapacity];
        private bool _shutdown;
        private bool _homesSeeded;
        private int _strategicSliceRuns;

        private struct Candidate
        {
            public int Id;
            public AiGoalType Type;
            public int TownId;
            public int TerritoryId;
            public int NodeId;
            public int Score;
        }

        private EnemyAi(
            ISimCommandSink commands,
            ISimWorldView world,
            ICaptureOccupancyView occupancy,
            AiBalanceTables balance,
            AiMapSnapshot map,
            AiOwnedState owned,
            AiDifficultyProfile difficulty,
            ulong matchSeed,
            IAiScenarioSeats seats)
        {
            _commands = commands;
            _world = world;
            _balance = balance;
            _map = map;
            _owned = owned;
            _economy = new AiEconomyStub(map, owned, balance, occupancy, world);
            _seats = seats ?? throw new ArgumentNullException(nameof(seats));
            _homes = new AiSeatHomeBinder(_seats);
            _aiCommands = new ForwardingAiCommandSink(commands, owned);
            _seatView = _fixtureSeatView;
            _depots = new OwnedDepotView(owned, world);
            _attackDefend = new AiAttackDefend(_aiCommands, new WorldTownQuery(this), _depots);
            _attackTowns = new AiAttackTownInfo[System.Math.Max(1, map.TownCount)];
            _attackBattalions = new AiBattalionPin[16];
            _difficulty = difficulty;
            _matchSeed = matchSeed;
            _balanceKey = balance.BalanceKey;
            _mapKey = map.MapKey;
            _commandDelayTicks = commands.CommandDelayTicks;
            _adjacencyHash = HashAdjacency(map.Adjacency);
            _candidates = new Candidate[System.Math.Max(1, difficulty.MaxCandidateGoals)];
            _networkScratch = new int[balance.ConnectivityScratchSize];
            _homes.Seed(_owned, _map, _balance);
            _homesSeeded = true;
            IssueOpeningForces();
        }

        public IAiScenarioSeats Seats
        {
            get { return _seats; }
        }

        public IAiCommandSink AiCommands
        {
            get { return _aiCommands; }
        }

        public ISimCommandSink Commands
        {
            get { return _commands; }
        }

        public AiBalanceTables Balance
        {
            get { return _balance; }
        }

        public AiDifficultyProfile Difficulty
        {
            get { return _difficulty; }
        }

        public AiMapSnapshot Map
        {
            get { return _map; }
        }

        public AiOwnedState Owned
        {
            get { return _owned; }
        }

        public ITerritoryAdjacency Adjacency
        {
            get { return _map.Adjacency; }
        }

        public ulong BalanceKey
        {
            get { return _balanceKey; }
        }

        public ulong MapKey
        {
            get { return _mapKey; }
        }

        public int StrategicSliceRuns
        {
            get { return _strategicSliceRuns; }
        }

        internal int NetworkScratchCapacity
        {
            get { return _networkScratch.Length; }
        }

        internal int CandidateCapacity
        {
            get { return _candidates.Length; }
        }

        public static EnemyAi MatchInit(
            MatchConfig match,
            byte[] mapBlob,
            int[] factionIds,
            string difficultyId,
            AiBalanceTables balance,
            ISimCommandSink commands,
            ISimWorldView world,
            IAiScenarioSeats seats)
        {
            if (mapBlob == null)
            {
                throw new ArgumentNullException(nameof(mapBlob));
            }

            if (factionIds == null || factionIds.Length < 1)
            {
                throw new ArgumentException("At least one AI faction is required.", nameof(factionIds));
            }

            if (difficultyId == null)
            {
                throw new ArgumentNullException(nameof(difficultyId));
            }

            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (seats == null)
            {
                throw new ArgumentNullException(nameof(seats));
            }

            if (!(world is ICaptureOccupancyView occupancy))
            {
                throw new ArgumentException(
                    "World view must implement ICaptureOccupancyView until the sim owns capture zones.",
                    nameof(world));
            }

            if (match.CommandDelayTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(match), "commandDelayTicks must be >= 0.");
            }

            if (commands.CommandDelayTicks != match.CommandDelayTicks)
            {
                throw new ArgumentException("AI command delay must equal match.CommandDelayTicks.", nameof(commands));
            }

            AiDifficultyProfile difficulty = balance.GetDifficulty(difficultyId);
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(mapBlob);
            if (balance.BalanceKey != match.BalanceKey)
            {
                throw new ArgumentException("BalanceKey on the tables must equal match.BalanceKey.", nameof(balance));
            }

            if (map.MapKey != match.MapKey)
            {
                throw new ArgumentException("MapKey on the snapshot must equal match.MapKey.", nameof(mapBlob));
            }

            if (!balance.HasCaptureTable)
            {
                throw new ArgumentException(
                    "Balance tables must include capture.timerTicksByTier from rules.json.",
                    nameof(balance));
            }

            if (!balance.HasEconomyTable)
            {
                throw new ArgumentException(
                    "Balance tables must include holding-tier and HQ income from economy.json.",
                    nameof(balance));
            }
            AiFactionState[] factions = new AiFactionState[factionIds.Length];
            for (int i = 0; i < factionIds.Length; i++)
            {
                int factionId = factionIds[i];
                string personalityId = balance.RequireBoundPersonalityId(factionId);
                AiPersonalityProfile personality = balance.GetPersonality(personalityId);
                ulong factionStream = XxHash64.Mix(match.Seed, (ulong)(uint)factionId, AiConstants.StreamAiRoot);
                factions[i] = new AiFactionState(
                    factionId,
                    personalityId,
                    difficultyId,
                    difficulty.ReactionExtraTicks,
                    difficulty.StrategicCadenceTicks,
                    difficulty.MaxActiveOrders,
                    personality.MinGarrisonPerTown,
                    difficulty.ManpowerReserveSeconds,
                    difficulty.MaxCandidateGoals,
                    personality.TaskForceTargetSize,
                    personality.ReservePpt,
                    balance.WorkUnitsPerTick,
                    balance.MaxDepotsPerFaction,
                    map.ChokepointCount,
                    personality.DefaultPosture,
                    SeedRng(factionStream, AiConstants.StreamStrategy),
                    SeedRng(factionStream, AiConstants.StreamTactical),
                    SeedRng(factionStream, AiConstants.StreamMistake),
                    SeedRng(factionStream, AiConstants.StreamScout));
            }

            return new EnemyAi(
                commands,
                world,
                occupancy,
                balance,
                map,
                new AiOwnedState(factions, map.TownCount),
                difficulty,
                match.Seed,
                seats);
        }

        public void OnTick(int tick)
        {
            if (_shutdown)
            {
                throw new InvalidOperationException("AiMatchShutdown already ran.");
            }

            if (tick != _world.Tick)
            {
                throw new InvalidOperationException("AiOnTick tick must match the sim tick.");
            }

            SyncCaptureFromSim();
            ConsumePendingIntents();
            AccrueIncome(tick);
            FlushPendingCommands();

            int workUnits = 0;
            int phase = tick % AiConstants.TimeSliceModulus;
            bool ranStrategic = false;

            for (int i = 0; i < _owned.FactionCount; i++)
            {
                AiFactionState faction = _owned.FactionAt(i);
                faction.PathRequestsThisTick = 0;
                faction.WorkUnitsThisTick = 0;

                if (workUnits >= _balance.WorkUnitsPerTick)
                {
                    faction.WorkResumeId = faction.FactionId;
                    continue;
                }

                workUnits++;
                faction.WorkUnitsThisTick++;

                if (faction.PlanThisTick(tick))
                {
                    faction.StrategicDeferred = true;
                }

                if (phase == AiConstants.PhaseStrategic && faction.StrategicDeferred)
                {
                    if (ranStrategic)
                    {
                        faction.StrategicDeferred = true;
                    }
                    else
                    {
                        ranStrategic = true;
                        faction.StrategicDeferred = false;
                        RunStrategicSlice(faction);
                    }
                }

                if (phase >= 3 && phase <= 5 && faction.FactionId % 3 == phase - 3)
                {
                    RunTacticalSlice(faction);
                }
            }

            FlushPendingCommands();
        }

        public void MatchShutdown()
        {
            _shutdown = true;
        }

        public ulong HashState()
        {
            XxHash64Builder hash = new XxHash64Builder();
            hash.AddULong(_matchSeed);
            hash.AddInt(_commandDelayTicks);
            hash.AddULong(_balanceKey);
            hash.AddULong(_mapKey);
            hash.AddULong(_map.ContentHash);
            hash.AddInt(_map.TheDallesTownId);
            hash.AddInt(_map.TheDallesTerritoryId);
            hash.AddULong(_adjacencyHash);
            hash.AddInt(_map.WheeledBlockedCount);
            hash.AddBool(_homesSeeded);
            hash.AddInt(DroppedFromLog);

            _owned.HashInto(hash);
            return hash.Digest();
        }

        public byte[] Save()
        {
            _owned.DroppedFromLog = DroppedFromLog;
            return _owned.Save(_matchSeed, _balanceKey, _mapKey);
        }

        public void Load(byte[] blob)
        {
            _owned.Load(blob, _matchSeed, _balanceKey, _mapKey);
            _homesSeeded = true;
            for (int i = 0; i < _owned.FactionCount; i++)
            {
                _owned.FactionAt(i).LastIssuedCommandId = 0;
            }
        }

        public int DroppedFromLog
        {
            get
            {
                int owned = _owned.DroppedFromLog;
                if (_commands is LoggingCommandSink log && log.DroppedFromLog > owned)
                {
                    return log.DroppedFromLog;
                }

                return owned;
            }
        }

        public bool AnyOverflowDropped()
        {
            if (DroppedFromLog != 0)
            {
                return true;
            }

            for (int i = 0; i < _owned.FactionCount; i++)
            {
                if (_owned.FactionAt(i).PendingDropped != 0)
                {
                    return true;
                }
            }

            return false;
        }

        public int ManpowerOf(int factionId)
        {
            return _owned.GetFaction(factionId).Manpower;
        }

        public int ManpowerIncomePerMinute(int factionId)
        {
            return _economy.IncomePerMinute(factionId);
        }

        public int ManpowerReserveOf(int factionId)
        {
            AiFactionState faction = _owned.GetFaction(factionId);
            int garrisonNeed = 0;
            for (int i = 0; i < _map.TownCount; i++)
            {
                if (_owned.TownOwnerAt(i) != factionId)
                {
                    continue;
                }

                AiTownRecord town = _map.TownAt(i);
                if (town.PopulationClass < 1)
                {
                    continue;
                }

                int present = CountOwnBattalionsInTown(factionId, town);
                int missing = faction.MinGarrisonPerTown - present;
                if (missing > 0)
                {
                    garrisonNeed += missing * _difficulty.BattalionManpowerCost;
                }
            }

            return garrisonNeed + ((faction.ManpowerReserveSeconds * ManpowerIncomePerMinute(factionId)) / 60);
        }

        public int TechSavingsOf(int factionId)
        {
            _ = factionId;
            return 0;
        }

        public int BattalionCountOf(int factionId)
        {
            return _world.CountOwnBattalions(factionId);
        }

        public int TownsHeldOf(int factionId)
        {
            int count = 0;
            for (int i = 0; i < _map.TownCount; i++)
            {
                if (_owned.TownOwnerAt(i) == factionId)
                {
                    count++;
                }
            }

            return count;
        }

        public int DepotCountOf(int factionId)
        {
            return _depots.CountSites(factionId);
        }

        public int PendingCountOf(int factionId)
        {
            return _owned.GetFaction(factionId).PendingCount;
        }

        public bool OwnsTerritory(int factionId, int territoryId)
        {
            if (!_map.TryGetTerritory(territoryId, out AiTerritoryRecord territory))
            {
                return false;
            }

            if (_world.TryGetTerritory(territory.StableKey, out TerritoryView view))
            {
                return view.OwnerFactionId == factionId;
            }

            int seatIndex = _map.IndexOfTown(territory.SeatTownId);
            return seatIndex >= 0 && _owned.TownOwnerAt(seatIndex) == factionId;
        }

        public int TheDallesOwnerId()
        {
            if (_world.TryGetTown(_map.TheDallesTownId, out TownView dalles))
            {
                return dalles.OwnerFactionId;
            }

            int index = _map.IndexOfTown(_map.TheDallesTownId);
            if (index < 0)
            {
                return 0;
            }

            return _owned.TownOwnerAt(index);
        }

        public AiHeadlessSnapshot CaptureHeadlessSnapshot(int tick, int factionId)
        {
            AiFactionState faction = _owned.GetFaction(factionId);
            return new AiHeadlessSnapshot(
                tick,
                factionId,
                ManpowerOf(factionId),
                ManpowerReserveOf(factionId),
                TechSavingsOf(factionId),
                BattalionCountOf(factionId),
                TownsHeldOf(factionId),
                DepotCountOf(factionId),
                faction.PickedPostCount,
                _map.TheDallesTownId,
                TheDallesOwnerId(),
                AiConstants.TierIITickNone);
        }

        public bool TryConsumePathRequest(AiFactionState faction)
        {
            int used = 0;
            for (int i = 0; i < _owned.FactionCount; i++)
            {
                used += _owned.FactionAt(i).PathRequestsThisTick;
            }

            if (used >= AiConstants.PathRequestsPerTick)
            {
                return false;
            }

            faction.PathRequestsThisTick++;
            return true;
        }

        public bool PickedPostMatchesMap(int factionId, int chokepointId)
        {
            if (!_map.TryGetChokepoint(chokepointId, out AiChokepointRecord choke))
            {
                return false;
            }

            if (!choke.HasDefenseGroup || !choke.IsBorder)
            {
                return false;
            }

            AiFactionState faction = _owned.GetFaction(factionId);
            for (int i = 0; i < faction.PickedPostCount; i++)
            {
                if (faction.PickedPostIds[i] == chokepointId)
                {
                    return choke.DefenseGroupId == (byte)faction.PickedGroupIds[i];
                }
            }

            return false;
        }

        private void AccrueIncome(int tick)
        {
            _economy.AccrueTick(tick);
        }

        private int ZoneRadiusCm(AiTownRecord town)
        {
            if (_world.TryGetTown(town.Id, out TownView view))
            {
                return view.ZoneRadiusCm;
            }

            if (!_map.TryGetTerritory(town.TerritoryId, out AiTerritoryRecord territory)
                || territory.SeatTownId != town.Id)
            {
                return 0;
            }

            return _balance.CaptureRadiusCmForClass(town.PopulationClass);
        }

        private void SyncCaptureFromSim()
        {
            _world.CopyTowns(_townScratch);
            for (int i = 0; i < _townScratch.Count; i++)
            {
                TownView town = _townScratch[i];
                int index = _map.IndexOfTown(town.TownId);
                if (index < 0)
                {
                    continue;
                }

                _owned.SetTownOwner(index, town.OwnerFactionId);
            }
        }

        private void ConsumePendingIntents()
        {
            int tick = _world.Tick;
            if (tick < 1)
            {
                return;
            }

            int fromTick = tick - 1;
            int historyStart = tick - SimWorld.EventHistoryTicks;
            if (fromTick < historyStart)
            {
                fromTick = historyStart < 0 ? 0 : historyStart;
            }

            _world.CopyEventsInRange(fromTick, tick - 1, _eventScratch);
            for (int i = 0; i < _eventScratch.Count; i++)
            {
                SimEvent ev = _eventScratch[i];
                if (ev.Kind == SimEventKind.CommandRejected)
                {
                    int issuer = (int)(ev.Key >> 32);
                    int sequence = (int)ev.Key;
                    RemoveIntentBySequence(issuer, sequence);
                    continue;
                }

                if (ev.Kind == SimEventKind.OrderRefunded)
                {
                    RemoveIntentByRefund(ev.SubjectId, ev.AuxId, ev.Param0);
                }
            }

            ClearResolvedIntents();
        }

        private void RemoveIntentBySequence(int factionId, int sequence)
        {
            AiFactionState faction;
            try
            {
                faction = _owned.GetFaction(factionId);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            for (int i = faction.IntentCount - 1; i >= 0; i--)
            {
                if (faction.Intents[i].Sequence == sequence)
                {
                    faction.RemoveIntentAt(i);
                }
            }
        }

        private void RemoveIntentByRefund(int factionId, int commandKind, int targetId)
        {
            AiIssuedKind kind;
            if (commandKind == (int)CommandKind.ClaimTown)
            {
                kind = AiIssuedKind.ClaimTown;
            }
            else if (commandKind == (int)CommandKind.BuildDepot)
            {
                kind = AiIssuedKind.BuildDepot;
            }
            else
            {
                return;
            }

            AiFactionState faction;
            try
            {
                faction = _owned.GetFaction(factionId);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            for (int i = faction.IntentCount - 1; i >= 0; i--)
            {
                AiPendingIntent intent = faction.Intents[i];
                if (intent.Kind == kind && intent.TargetId == targetId)
                {
                    faction.RemoveIntentAt(i);
                }
            }
        }

        private void ClearResolvedIntents()
        {
            _world.CopyDepots(_depotScratch);
            for (int f = 0; f < _owned.FactionCount; f++)
            {
                AiFactionState faction = _owned.FactionAt(f);
                for (int i = faction.IntentCount - 1; i >= 0; i--)
                {
                    AiPendingIntent intent = faction.Intents[i];
                    if (intent.Kind == AiIssuedKind.ClaimTown)
                    {
                        if (_world.TryGetTown(intent.TargetId, out TownView town)
                            && (town.OwnerFactionId == faction.FactionId
                                || town.CaptureFactionId == faction.FactionId))
                        {
                            faction.RemoveIntentAt(i);
                        }

                        continue;
                    }

                    if (intent.Kind != AiIssuedKind.BuildDepot)
                    {
                        continue;
                    }

                    for (int d = 0; d < _depotScratch.Count; d++)
                    {
                        DepotView depot = _depotScratch[d];
                        if (depot.OwnerFactionId == faction.FactionId && depot.NodeId == intent.TargetId)
                        {
                            faction.RemoveIntentAt(i);
                            break;
                        }
                    }
                }
            }
        }

        private int CountOwnBattalionsInTown(int factionId, AiTownRecord town)
        {
            int count = 0;
            long radiusSq = (long)ZoneRadiusCm(town) * ZoneRadiusCm(town);
            _world.CopyOwnBattalionIds(factionId, _countIdScratch);
            for (int i = 0; i < _countIdScratch.Count; i++)
            {
                if (!_world.TryGetOwnBattalion(factionId, _countIdScratch[i], out SimBattalion battalion))
                {
                    continue;
                }

                if (InRadius(battalion.PositionXCm, battalion.PositionYCm, town.XCm, town.YCm, radiusSq))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// A straight-line move stores the town on <see cref="SimBattalion.TargetXCm"/>.
        /// A road move stores the next node there and the town on the last waypoint.
        /// Either one is already the order this slice would issue.
        /// </summary>
        private static bool AlreadyMovingTo(SimBattalion battalion, int xCm, int yCm)
        {
            if (!battalion.HasTarget)
            {
                return false;
            }

            if (battalion.PathCount > 0 && battalion.PathXCm != null && battalion.PathYCm != null)
            {
                int last = battalion.PathCount - 1;
                if (last < battalion.PathXCm.Length && last < battalion.PathYCm.Length)
                {
                    return battalion.PathXCm[last] == xCm && battalion.PathYCm[last] == yCm;
                }
            }

            return battalion.TargetXCm == xCm && battalion.TargetYCm == yCm;
        }

        private static bool InRadius(int ax, int ay, int bx, int by, long radiusSq)
        {
            long dx = (long)ax - bx;
            long dy = (long)ay - by;
            return (dx * dx) + (dy * dy) <= radiusSq;
        }

        private void RunStrategicSlice(AiFactionState faction)
        {
            _strategicSliceRuns++;
            RebuildFrontlinePosts(faction);
            RunAttackDefend(faction);
            DropStaleOrders(faction);
            int candidateCount = GenerateCandidates(faction);
            AcceptCandidates(faction, candidateCount);
            faction.TaskForceCount = CountAssignedTaskForces(faction);
        }

        private void RunTacticalSlice(AiFactionState faction)
        {
            for (int i = 0; i < faction.OrderCount; i++)
            {
                AiOrder order = faction.Orders[i];
                if (order.Type != AiGoalType.ClaimTown || order.BattalionId == 0)
                {
                    continue;
                }

                if (!_map.TryGetTown(order.TargetTownId, out AiTownRecord town))
                {
                    continue;
                }

                if (!_world.TryGetOwnBattalion(faction.FactionId, order.BattalionId, out SimBattalion battalion))
                {
                    continue;
                }

                if (IsAttackingPrize(faction) && IsFootBattalion(order.BattalionId))
                {
                    continue;
                }

                if (IsEngineerBattalion(order.BattalionId))
                {
                    continue;
                }

                long radiusSq = (long)ZoneRadiusCm(town) * ZoneRadiusCm(town);
                if (InRadius(battalion.PositionXCm, battalion.PositionYCm, town.XCm, town.YCm, radiusSq))
                {
                    continue;
                }

                if (AlreadyMovingTo(battalion, town.XCm, town.YCm))
                {
                    continue;
                }

                if (IsAttackingPrize(faction)
                    && IsSeatTown(town)
                    && !IsPrizeSeatTown(town.Id)
                    && town.Id != faction.HomeTownId)
                {
                    continue;
                }

                Issue(faction, CommandKind.Move, order.BattalionId, town.XCm, town.YCm, 0);
            }
        }

        internal bool Issue(AiFactionState faction, CommandKind kind, int battalionId, int arg0, int arg1, int arg2)
        {
            int extra = faction.ReactionExtraTicks;
            if (extra < 0)
            {
                extra = 0;
            }

            AiPendingCommand pending = default;
            pending.ReadyTick = _world.Tick + extra;
            pending.BattalionId = battalionId;
            pending.Kind = kind;
            pending.Arg0 = arg0;
            pending.Arg1 = arg1;
            pending.Arg2 = arg2;
            PendingEnqueueResult result = faction.EnqueuePending(pending, out AiPendingCommand evicted);
            if (result == PendingEnqueueResult.Replaced)
            {
                DropAssociated(faction, evicted);
            }

            return result != PendingEnqueueResult.DroppedIncoming;
        }

        internal void DropAssociated(AiFactionState faction, AiPendingCommand pending)
        {
            if (pending.Kind == CommandKind.SpawnBattalion)
            {
                RemoveOrdersForBattalion(faction, pending.BattalionId);
                return;
            }

            if (pending.Kind == CommandKind.BuildDepot)
            {
                RemoveDepotAtNode(faction, pending.Arg0);
                return;
            }

            if (pending.Kind == CommandKind.Build)
            {
                RemoveDepotAt(faction, pending.Arg0, pending.Arg1);
            }
        }

        private static void RemoveOrdersForBattalion(AiFactionState faction, int battalionId)
        {
            int write = 0;
            for (int i = 0; i < faction.OrderCount; i++)
            {
                if (faction.Orders[i].BattalionId == battalionId)
                {
                    continue;
                }

                faction.Orders[write++] = faction.Orders[i];
            }

            faction.OrderCount = write;
        }

        private static void RemoveDepotAt(AiFactionState faction, int xCm, int yCm)
        {
            int write = 0;
            for (int i = 0; i < faction.DepotCount; i++)
            {
                if (faction.Depots[i].XCm == xCm && faction.Depots[i].YCm == yCm)
                {
                    continue;
                }

                faction.Depots[write++] = faction.Depots[i];
            }

            faction.DepotCount = write;
        }

        private static void RemoveDepotAtNode(AiFactionState faction, int nodeId)
        {
            int write = 0;
            for (int i = 0; i < faction.DepotCount; i++)
            {
                if (faction.Depots[i].NodeId == nodeId)
                {
                    continue;
                }

                faction.Depots[write++] = faction.Depots[i];
            }

            faction.DepotCount = write;
        }

        private void FlushPendingCommands()
        {
            int tick = _world.Tick;
            for (int f = 0; f < _owned.FactionCount; f++)
            {
                AiFactionState faction = _owned.FactionAt(f);
                int write = 0;
                for (int i = 0; i < faction.PendingCount; i++)
                {
                    AiPendingCommand pending = faction.Pending[i];
                    if (pending.ReadyTick <= tick)
                    {
                        SimCommand command = new SimCommand(
                            tick,
                            faction.FactionId,
                            pending.BattalionId,
                            pending.Kind,
                            pending.Arg0,
                            pending.Arg1,
                            pending.Arg2);
                        _commands.Submit(command);
                    }
                    else
                    {
                        faction.Pending[write] = pending;
                        write++;
                    }
                }

                faction.PendingCount = write;
            }
        }

        private void RebuildFrontlinePosts(AiFactionState faction)
        {
            faction.PickedPostCount = 0;
            for (int i = 0; i < _map.ChokepointCount; i++)
            {
                AiChokepointRecord choke = _map.ChokepointAt(i);
                if (!choke.HasDefenseGroup || !choke.IsBorder)
                {
                    continue;
                }

                bool ownA = OwnsTerritory(faction.FactionId, choke.SideATerritoryId);
                bool ownB = OwnsTerritory(faction.FactionId, choke.SideBTerritoryId);
                if (ownA == ownB)
                {
                    continue;
                }

                if (faction.PickedPostCount >= faction.PickedPostIds.Length)
                {
                    break;
                }

                int insert = faction.PickedPostCount;
                while (insert > 0 && faction.PickedPostIds[insert - 1] > choke.Id)
                {
                    faction.PickedPostIds[insert] = faction.PickedPostIds[insert - 1];
                    faction.PickedGroupIds[insert] = faction.PickedGroupIds[insert - 1];
                    insert--;
                }

                faction.PickedPostIds[insert] = choke.Id;
                faction.PickedGroupIds[insert] = choke.DefenseGroupId;
                faction.PickedPostCount++;
            }
        }

        private void DropStaleOrders(AiFactionState faction)
        {
            int write = 0;
            for (int i = 0; i < faction.OrderCount; i++)
            {
                AiOrder order = faction.Orders[i];
                bool keep = true;
                if (order.Type == AiGoalType.ClaimTown)
                {
                    int townIndex = _map.IndexOfTown(order.TargetTownId);
                    if (townIndex < 0 || _owned.TownOwnerAt(townIndex) != 0)
                    {
                        keep = false;
                    }
                    else if (IsPrizeSeatTown(order.TargetTownId) && !IsAttackingPrize(faction))
                    {
                        keep = false;
                    }
                }

                if (keep)
                {
                    faction.Orders[write++] = order;
                }
            }

            faction.OrderCount = write;
        }

        private int GenerateCandidates(AiFactionState faction)
        {
            int count = 0;
            int cap = faction.MaxCandidateGoals;
            if (cap > _candidates.Length)
            {
                throw new InvalidOperationException(AiBalanceTables.RuleIdAiMaxCandidateGoals);
            }

            AiPersonalityProfile personality = _balance.GetPersonalityOrGeneric(faction.PersonalityId);
            if (!_map.TryGetTown(faction.HomeTownId, out AiTownRecord home))
            {
                return 0;
            }

            for (int i = 0; i < _map.TownCount; i++)
            {
                if (_owned.TownOwnerAt(i) != 0)
                {
                    continue;
                }

                AiTownRecord town = _map.TownAt(i);
                if (!CanClaimFrom(faction, town.TerritoryId))
                {
                    continue;
                }

                bool isSeat = IsSeatTown(town);
                if (!isSeat)
                {
                    continue;
                }

                if (IsAttackingPrize(faction)
                    && !IsPrizeSeatTown(town.Id)
                    && town.Id != faction.HomeTownId)
                {
                    continue;
                }
                int distCm = ApproxDistCm(home.XCm, home.YCm, town.XCm, town.YCm);
                // Integer miles (cm / MileCm), not ppt. Caps so WeightEconomy/WeightDistance stay bounded.
                int distMiles = distCm / AiConstants.MileCm;
                if (distMiles > 1000)
                {
                    distMiles = 1000;
                }

                int score = (personality.WeightTownValue * town.StrategicValue) / SimConfig.PptScale;
                score += (personality.WeightAnchorSet * (isSeat ? AiConstants.SeatAnchorBonusPpt : personality.NonSeatBonusPpt)) / SimConfig.PptScale;
                score += (personality.WeightEconomy * (1000 - distMiles)) / SimConfig.PptScale;
                if (faction.Posture == AiPosture.BuildUp)
                {
                    score += personality.BuildUpScoreBonus;
                }

                if (town.TerritoryId == faction.HomeTerritoryId)
                {
                    score += 150;
                }

                score -= (personality.WeightDistance * distMiles) / SimConfig.PptScale;
                PushCandidate(ref count, cap, AiGoalType.ClaimTown, town.Id, town.TerritoryId, 0, score);
            }

            return count;
        }

        private void PushCandidate(ref int count, int cap, AiGoalType type, int townId, int territoryId, int nodeId, int score)
        {
            Candidate row = default;
            row.Id = (count + 1) * 17 + (int)type;
            row.Type = type;
            row.TownId = townId;
            row.TerritoryId = territoryId;
            row.NodeId = nodeId;
            row.Score = score;
            if (count < cap)
            {
                _candidates[count++] = row;
                return;
            }

            int worst = 0;
            for (int i = 1; i < count; i++)
            {
                if (CompareCandidates(_candidates[i], _candidates[worst]) < 0)
                {
                    worst = i;
                }
            }

            if (CompareCandidates(row, _candidates[worst]) > 0)
            {
                _candidates[worst] = row;
            }
        }

        private void AcceptCandidates(AiFactionState faction, int candidateCount)
        {
            SortCandidates(candidateCount);
            for (int i = 0; i < candidateCount; i++)
            {
                if (faction.OrderCount >= faction.MaxActiveOrders)
                {
                    break;
                }

                Candidate cand = _candidates[i];
                if (cand.Score < AiConstants.MinAcceptScore)
                {
                    continue;
                }

                if (cand.Type == AiGoalType.ClaimTown)
                {
                    if (HasOrderOnTown(faction, cand.TownId))
                    {
                        continue;
                    }

                    int battalionId = AssignClaimBattalion(faction, cand.TownId);
                    if (battalionId == 0)
                    {
                        continue;
                    }

                    AiOrder order = default;
                    order.Id = faction.NextOrderId++;
                    order.Type = AiGoalType.ClaimTown;
                    order.TargetTownId = cand.TownId;
                    order.TargetTerritoryId = cand.TerritoryId;
                    order.BattalionId = battalionId;
                    order.Score = cand.Score;
                    faction.Orders[faction.OrderCount++] = order;
                }
            }
        }

        private int AssignClaimBattalion(AiFactionState faction, int townId)
        {
            if (!_map.TryGetTown(townId, out AiTownRecord town)
                || !_map.TryGetTown(faction.HomeTownId, out AiTownRecord home))
            {
                return 0;
            }

            _world.CopyOwnBattalionIds(faction.FactionId, _idScratch);
            long radiusSq = (long)ZoneRadiusCm(town) * ZoneRadiusCm(town);
            for (int i = 0; i < _idScratch.Count; i++)
            {
                int id = _idScratch[i];
                if (IsBattalionAssigned(faction, id))
                {
                    continue;
                }

                if (!_world.TryGetOwnBattalion(faction.FactionId, id, out SimBattalion battalion))
                {
                    continue;
                }

                if (IsAttackingPrize(faction) && PrizeUncaptured() && IsPrizeForceBattalion(faction, id))
                {
                    continue;
                }

                if (IsEngineerBattalion(id))
                {
                    continue;
                }

                if (IsFootBattalion(id))
                {
                    if (IsAttackingPrize(faction))
                    {
                        continue;
                    }

                    if (InRadius(battalion.PositionXCm, battalion.PositionYCm, home.XCm, home.YCm, radiusSq)
                        && CountFootBattalionsInTown(faction.FactionId, home) <= faction.MinGarrisonPerTown)
                    {
                        continue;
                    }
                }

                return id;
            }

            return 0;
        }

        private bool CanClaimFrom(AiFactionState faction, int territoryId)
        {
            if (OwnsTerritory(faction.FactionId, territoryId) || territoryId == faction.HomeTerritoryId)
            {
                return true;
            }

            for (int i = 0; i < _map.TerritoryCount; i++)
            {
                AiTerritoryRecord territory = _map.TerritoryAt(i);
                if (!OwnsTerritory(faction.FactionId, territory.Id))
                {
                    continue;
                }

                if (_map.Adjacency.AreNeighbors(territory.Id, territoryId))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSeatTown(AiTownRecord town)
        {
            if (!_map.TryGetTerritory(town.TerritoryId, out AiTerritoryRecord territory))
            {
                return false;
            }

            return territory.SeatTownId == town.Id;
        }

        private bool IsHqTown(int townId)
        {
            if (townId == 0)
            {
                return false;
            }

            for (int i = 0; i < _seats.SeatCount; i++)
            {
                if (_seats.SeatAt(i).HomeTownId == townId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasOrderOnTown(AiFactionState faction, int townId)
        {
            for (int i = 0; i < faction.OrderCount; i++)
            {
                if (faction.Orders[i].TargetTownId == townId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsBattalionAssigned(AiFactionState faction, int battalionId)
        {
            for (int i = 0; i < faction.OrderCount; i++)
            {
                if (faction.Orders[i].BattalionId == battalionId)
                {
                    return true;
                }
            }

            return false;
        }

        private void IssueOpeningForces()
        {
            for (int i = 0; i < _owned.FactionCount; i++)
            {
                IssueOpeningForce(_owned.FactionAt(i));
            }
        }

        /// <summary>
        /// Fixture-only generic opening spawn. Real scenario seats with an
        /// authored <c>startingUnits</c> list are skipped so they do not stack
        /// on the Lead's spawn.
        /// </summary>
        public void IssueOpeningForce(AiFactionState faction)
        {
            if (faction == null)
            {
                throw new ArgumentNullException(nameof(faction));
            }

            AiPersonalityProfile personality = _balance.GetPersonalityOrGeneric(faction.PersonalityId);
            if (!_seats.TryGetByFaction(personality.EconomyFactionId, out AiScenarioSeatView seat))
            {
                return;
            }

            if (seat.HasAuthoredStartingUnits)
            {
                return;
            }

            if (!_map.TryGetTown(faction.HomeTownId, out AiTownRecord home))
            {
                return;
            }

            int battalionId = faction.NextBattalionId;
            faction.NextBattalionId = battalionId + 1;
            Issue(
                faction,
                CommandKind.SpawnBattalion,
                battalionId,
                home.XCm,
                home.YCm,
                AiConstants.ClaimMarchSpeedCmPerSecond);
        }

        private bool PrizeUncaptured()
        {
            ulong prize = _seats.NeutralPrizeTerritoryKey;
            if (prize == 0UL || !_map.TryGetTerritoryByStableKey(prize, out AiTerritoryRecord territory))
            {
                return false;
            }

            int index = _map.IndexOfTown(territory.SeatTownId);
            return index >= 0 && _owned.TownOwnerAt(index) == 0;
        }

        private bool IsPrizeSeatTown(int townId)
        {
            ulong prize = _seats.NeutralPrizeTerritoryKey;
            if (prize == 0UL || !_map.TryGetTown(townId, out AiTownRecord town))
            {
                return false;
            }

            if (!_map.TryGetTerritory(town.TerritoryId, out AiTerritoryRecord territory))
            {
                return false;
            }

            return territory.StableKey == prize && town.Id == territory.SeatTownId;
        }

        private bool IsPrizeForceBattalion(AiFactionState faction, int battalionId)
        {
            if (!_world.TryGetBattalionView(battalionId, out BattalionView view))
            {
                return false;
            }

            if (AiAttackDefend.IsPrizeScout(_balance.UnitCatalog, view.UnitId))
            {
                return true;
            }

            if (!AiAttackDefend.IsPrizeRifle(_balance.UnitCatalog, view.UnitId))
            {
                return false;
            }

            _world.CopyOwnBattalionIds(faction.FactionId, _prizeIdScratch);
            SortIds(_prizeIdScratch);
            int taken = 0;
            int need = faction.TaskForceTargetSize;
            if (need < 1)
            {
                need = 1;
            }

            for (int i = 0; i < _prizeIdScratch.Count; i++)
            {
                int id = _prizeIdScratch[i];
                if (!_world.TryGetBattalionView(id, out BattalionView own))
                {
                    continue;
                }

                if (!AiAttackDefend.IsPrizeRifle(_balance.UnitCatalog, own.UnitId))
                {
                    continue;
                }

                taken++;
                if (id == battalionId)
                {
                    return taken <= need;
                }

                if (taken >= need)
                {
                    return false;
                }
            }

            return false;
        }

        private int CountFootBattalionsInTown(int factionId, AiTownRecord town)
        {
            int count = 0;
            long radiusSq = (long)ZoneRadiusCm(town) * ZoneRadiusCm(town);
            _world.CopyOwnBattalionIds(factionId, _countIdScratch);
            for (int i = 0; i < _countIdScratch.Count; i++)
            {
                int id = _countIdScratch[i];
                if (!_world.TryGetOwnBattalion(factionId, id, out SimBattalion battalion))
                {
                    continue;
                }

                if (!IsFootBattalion(id))
                {
                    continue;
                }

                if (InRadius(battalion.PositionXCm, battalion.PositionYCm, town.XCm, town.YCm, radiusSq))
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsAttackingPrize(AiFactionState faction)
        {
            ulong prize = _seats.NeutralPrizeTerritoryKey;
            if (prize == 0UL || faction.AttackTargetTownId == 0)
            {
                return false;
            }

            if (!_map.TryGetTown(faction.AttackTargetTownId, out AiTownRecord town))
            {
                return false;
            }

            if (!_map.TryGetTerritory(town.TerritoryId, out AiTerritoryRecord territory))
            {
                return false;
            }

            return territory.StableKey == prize && town.Id == territory.SeatTownId;
        }

        private bool IsFootBattalion(int battalionId)
        {
            if (!_world.TryGetBattalionView(battalionId, out BattalionView view))
            {
                return false;
            }

            MovementClass moveClass;
            if (!_balance.UnitCatalog.TryGetMovementClass(view.UnitId, out moveClass))
            {
                return false;
            }

            return moveClass == MovementClass.Foot;
        }

        private bool IsEngineerBattalion(int battalionId)
        {
            if (!_world.TryGetBattalionView(battalionId, out BattalionView view))
            {
                return false;
            }

            string role;
            if (!_balance.UnitCatalog.TryGetRole(view.UnitId, out role))
            {
                return false;
            }

            return role == AiConstants.RoleEngineer;
        }

        private int CountAssignedTaskForces(AiFactionState faction)
        {
            int count = 0;
            for (int i = 0; i < faction.OrderCount; i++)
            {
                if (faction.Orders[i].BattalionId != 0)
                {
                    count++;
                }
            }

            return count;
        }

        private int CollectSupplyNetwork(AiFactionState faction)
        {
            int count = 0;
            if (faction.HqNodeId != 0)
            {
                _networkScratch[count++] = faction.HqNodeId;
            }

            for (int i = 0; i < faction.DepotCount && count < _networkScratch.Length; i++)
            {
                _networkScratch[count++] = faction.Depots[i].NodeId;
            }

            return count;
        }

        private static int ApproxDistCm(int ax, int ay, int bx, int by)
        {
            int dx = ax > bx ? ax - bx : bx - ax;
            int dy = ay > by ? ay - by : by - ay;
            int max = dx > dy ? dx : dy;
            int min = dx > dy ? dy : dx;
            return max + (min / 2);
        }

        private void SortCandidates(int count)
        {
            for (int i = 1; i < count; i++)
            {
                Candidate row = _candidates[i];
                int j = i - 1;
                while (j >= 0 && CompareCandidates(_candidates[j], row) < 0)
                {
                    _candidates[j + 1] = _candidates[j];
                    j--;
                }

                _candidates[j + 1] = row;
            }
        }

        private static int CompareCandidates(Candidate a, Candidate b)
        {
            int c = a.Score.CompareTo(b.Score);
            if (c != 0)
            {
                return c;
            }

            return a.Id.CompareTo(b.Id);
        }

        private void RunAttackDefend(AiFactionState faction)
        {
            int townCount = FillAttackTowns();
            int battalionCount = FillAttackBattalions(faction);
            int networkCount = CollectSupplyNetwork(faction);
            AiPersonalityProfile personality = _balance.GetPersonalityOrGeneric(faction.PersonalityId);
            if (!_seats.TryGetByFaction(personality.EconomyFactionId, out AiScenarioSeatView seat))
            {
                return;
            }

            _fixtureSeatView.Tier2ResearchTicks = _balance.Tier2ResearchTicks;
            _fixtureSeatView.Refresh(seat, _world.Tick);
            if (_world.TryGetSeat(faction.FactionId, out SeatView live))
            {
                _fixtureSeatView.Manpower = live.Manpower;
                _fixtureSeatView.Materials = live.Materials;
                _fixtureSeatView.PopUsed = live.PopUsed;
                _fixtureSeatView.PopCap = live.PopCap;
                _fixtureSeatView.CurrentTier = live.CurrentTier;
                _fixtureSeatView.TierResearchCompleteTick = live.TierResearchCompleteTick;
            }

            int queuedCount = 0;
            if (_world.TryCopyProductionQueue(faction.FactionId, _productionQueue))
            {
                int n = _productionQueue.Count;
                if (n > _queuedUnitIds.Length)
                {
                    n = _queuedUnitIds.Length;
                }

                for (int i = 0; i < n; i++)
                {
                    _queuedUnitIds[i] = _productionQueue[i].UnitId;
                }

                queuedCount = n;
            }

            _attackDefend.Tick(
                _world.Tick,
                faction,
                seat,
                _attackTowns,
                townCount,
                _attackBattalions,
                battalionCount,
                _networkScratch,
                networkCount,
                _map.Roads,
                _difficulty.AttackStrengthRatioPpt,
                _balance.DepotRangeCm,
                _seats.NeutralPrizeTerritoryKey,
                personality.PrizeTargetBonusPpt,
                _seatView,
                _balance.UnitCatalog,
                _balance.BuildingCatalog,
                personality.LineUnitsPerAntiArmour,
                personality.MinGarrisonPerTown,
                faction.TaskForceTargetSize,
                _queuedUnitIds,
                queuedCount);
        }

        private int FillAttackTowns()
        {
            int count = 0;
            for (int i = 0; i < _map.TownCount; i++)
            {
                AiTownRecord town = _map.TownAt(i);
                ulong territoryKey = 0UL;
                if (_map.TryGetTerritory(town.TerritoryId, out AiTerritoryRecord territory)
                    && town.Id == territory.SeatTownId)
                {
                    territoryKey = territory.StableKey;
                }

                int zoneRadiusCm = 0;
                if (_world.TryGetTown(town.Id, out TownView view))
                {
                    zoneRadiusCm = view.ZoneRadiusCm;
                }

                _attackTowns[count++] = new AiAttackTownInfo(
                    town.Id,
                    town.TerritoryId,
                    town.RoadNodeId,
                    town.XCm,
                    town.YCm,
                    IsHqTown(town.Id),
                    territoryKey,
                    zoneRadiusCm);
            }

            return count;
        }

        private int FillAttackBattalions(AiFactionState faction)
        {
            _world.CopyOwnBattalionIds(faction.FactionId, _idScratch);
            if (_idScratch.Count > _attackBattalions.Length)
            {
                _attackBattalions = new AiBattalionPin[_idScratch.Count];
            }

            int count = 0;
            for (int i = 0; i < _idScratch.Count; i++)
            {
                int id = _idScratch[i];
                if (!_world.TryGetOwnBattalion(faction.FactionId, id, out SimBattalion battalion))
                {
                    continue;
                }

                string unitId = string.Empty;
                if (_world.TryGetBattalionView(id, out BattalionView view))
                {
                    unitId = view.UnitId;
                }

                _attackBattalions[count++] = new AiBattalionPin(
                    id,
                    battalion.PositionXCm,
                    battalion.PositionYCm,
                    NearestRoadNode(battalion.PositionXCm, battalion.PositionYCm),
                    unitId,
                    IsBattalionAssigned(faction, id));
            }

            return count;
        }

        private int NearestRoadNode(int xCm, int yCm)
        {
            RoadGraph roads = _map.Roads;
            if (roads.NodeCount < 1)
            {
                return 0;
            }

            int best = roads.GetNodeId(0);
            long bestDist = long.MaxValue / 4;
            for (int i = 0; i < roads.NodeCount; i++)
            {
                long dx = (long)roads.GetNodeXCm(i) - xCm;
                long dy = (long)roads.GetNodeYCm(i) - yCm;
                long d = (dx * dx) + (dy * dy);
                if (d < bestDist || (d == bestDist && roads.GetNodeId(i) < best))
                {
                    bestDist = d;
                    best = roads.GetNodeId(i);
                }
            }

            return best;
        }

        private static void SortIds(List<int> ids)
        {
            if (ids == null)
            {
                return;
            }

            for (int i = 1; i < ids.Count; i++)
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

        private static SimRandom SeedRng(ulong factionStream, ulong streamTag)
        {
            ulong seed = XxHash64.Mix(factionStream, streamTag);
            return new SimRandom(seed, streamTag);
        }

        private static ulong HashAdjacency(ITerritoryAdjacency adjacency)
        {
            List<int> scratch = new List<int>();
            XxHash64Builder hash = new XxHash64Builder();
            hash.AddULong(AiBalanceTables.HashUtf8(adjacency.SourceName));
            hash.AddInt(adjacency.TerritoryCount);
            for (int i = 0; i < adjacency.TerritoryCount; i++)
            {
                int territoryId = adjacency.TerritoryIdAt(i);
                hash.AddInt(territoryId);
                adjacency.CopyNeighbors(territoryId, scratch);
                hash.AddInt(scratch.Count);
                for (int n = 0; n < scratch.Count; n++)
                {
                    hash.AddInt(scratch[n]);
                }
            }

            return hash.Digest();
        }

        private sealed class WorldTownQuery : IAiVisibleStrengthView
        {
            private readonly EnemyAi _ai;
            private readonly List<int> _factionIds = new List<int>();

            public WorldTownQuery(EnemyAi ai)
            {
                _ai = ai;
            }

            public bool TryGet(int townId, out AiTownReadback readback)
            {
                int index = _ai._map.IndexOfTown(townId);
                if (index < 0)
                {
                    readback = default;
                    return false;
                }

                AiTownRecord town = _ai._map.TownAt(index);
                int owner = _ai._owned.TownOwnerAt(index);
                bool contested = false;
                int captureFactionId = 0;
                if (_ai._world.TryGetTown(townId, out TownView view))
                {
                    owner = view.OwnerFactionId;
                    contested = view.Contested;
                    captureFactionId = view.CaptureFactionId;
                }

                CollectFactionIds(captureFactionId);
                int side0 = 0;
                int side0Str = 0;
                int side1 = 0;
                int side1Str = 0;
                for (int f = 0; f < _factionIds.Count; f++)
                {
                    int factionId = _factionIds[f];
                    int strength = _ai.CountOwnBattalionsInTown(factionId, town);
                    if (strength < 1)
                    {
                        continue;
                    }

                    if (side0 == 0)
                    {
                        side0 = factionId;
                        side0Str = strength;
                    }
                    else if (side1 == 0 || factionId < side1)
                    {
                        if (side1 != 0 && factionId > side0)
                        {
                            side1 = factionId;
                            side1Str = strength;
                        }
                        else if (factionId < side0)
                        {
                            side1 = side0;
                            side1Str = side0Str;
                            side0 = factionId;
                            side0Str = strength;
                        }
                        else
                        {
                            side1 = factionId;
                            side1Str = strength;
                        }
                    }
                }

                readback = new AiTownReadback(owner, contested, side0, side0Str, side1, side1Str);
                return true;
            }

            private void CollectFactionIds(int occupancyFactionId)
            {
                _factionIds.Clear();
                for (int i = 0; i < _ai._owned.FactionCount; i++)
                {
                    AddFactionId(_ai._owned.FactionAt(i).FactionId);
                }

                for (int i = 0; i < _ai._balance.BindingCount; i++)
                {
                    AddFactionId(_ai._balance.BindingFactionIdAt(i));
                }

                AddFactionId(occupancyFactionId);
                for (int i = 1; i < _factionIds.Count; i++)
                {
                    int value = _factionIds[i];
                    int j = i - 1;
                    while (j >= 0 && _factionIds[j] > value)
                    {
                        _factionIds[j + 1] = _factionIds[j];
                        j--;
                    }

                    _factionIds[j + 1] = value;
                }
            }

            private void AddFactionId(int factionId)
            {
                if (factionId == 0)
                {
                    return;
                }

                for (int i = 0; i < _factionIds.Count; i++)
                {
                    if (_factionIds[i] == factionId)
                    {
                        return;
                    }
                }

                _factionIds.Add(factionId);
            }
        }
    }

    public readonly struct AiHeadlessSnapshot
    {
        public readonly int Tick;
        public readonly int FactionId;
        public readonly int Manpower;
        public readonly int ManpowerReserve;
        public readonly int TechSavings;
        public readonly int BattalionCount;
        public readonly int TownsHeld;
        public readonly int DepotCount;
        public readonly int PickedPostCount;
        public readonly int TheDallesTownId;
        public readonly int TheDallesOwnerId;
        public readonly string TierIITick;

        public AiHeadlessSnapshot(
            int tick,
            int factionId,
            int manpower,
            int manpowerReserve,
            int techSavings,
            int battalionCount,
            int townsHeld,
            int depotCount,
            int pickedPostCount,
            int theDallesTownId,
            int theDallesOwnerId,
            string tierIITick)
        {
            Tick = tick;
            FactionId = factionId;
            Manpower = manpower;
            ManpowerReserve = manpowerReserve;
            TechSavings = techSavings;
            BattalionCount = battalionCount;
            TownsHeld = townsHeld;
            DepotCount = depotCount;
            PickedPostCount = pickedPostCount;
            TheDallesTownId = theDallesTownId;
            TheDallesOwnerId = theDallesOwnerId;
            TierIITick = tierIITick ?? AiConstants.TierIITickNone;
        }

        public string FormatLine()
        {
            return "t=" + Tick
                + " faction=" + FactionId
                + " manpower=" + Manpower
                + " reserve=" + ManpowerReserve
                + " techSavings=" + TechSavings
                + " battalions=" + BattalionCount
                + " townsHeld=" + TownsHeld
                + " depots=" + DepotCount
                + " posts=" + PickedPostCount
                + " theDallesTown=" + TheDallesTownId
                + " theDallesOwner=" + TheDallesOwnerId
                + " tierIITick=" + TierIITick;
        }
    }
}
