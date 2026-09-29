using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// M1 enemy AI: claim nearby towns, spend income by priority, place depots
    /// after a seat is held. Orders go only through <see cref="ISimCommandSink"/>.
    /// No assault, hold-chokepoint, raid, or guard-supply layer.
    /// </summary>
    public sealed class EnemyAi
    {
        private readonly ISimCommandSink _commands;
        private readonly ISimWorldView _world;
        private readonly ICaptureOccupancyView _occupancy;
        private readonly IAiEconomy _economy;
        private readonly IAiHomeSeeder _homes;
        private readonly AiBalanceTables _balance;
        private readonly AiMapSnapshot _map;
        private readonly AiOwnedState _owned;
        private readonly AiDifficultyProfile _difficulty;
        private readonly ulong _matchSeed;
        private readonly ulong _personalityJsonHash;
        private readonly ulong _difficultyJsonHash;
        private readonly ulong _adjacencyHash;
        private readonly int _commandDelayTicks;
        private readonly List<SimBattalion> _battalionScratch = new List<SimBattalion>();
        private readonly List<int> _idScratch = new List<int>();
        private readonly Candidate[] _candidates = new Candidate[64];
        private readonly int[] _networkScratch = new int[32];
        private bool _shutdown;
        private bool _homesSeeded;

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
            ulong matchSeed)
        {
            _commands = commands;
            _world = world;
            _occupancy = occupancy;
            _balance = balance;
            _map = map;
            _owned = owned;
            _economy = new AiEconomyStub(map, owned);
            _homes = new AiHomeSeeder();
            _difficulty = difficulty;
            _matchSeed = matchSeed;
            _personalityJsonHash = balance.PersonalityJsonHash;
            _difficultyJsonHash = balance.DifficultyJsonHash;
            _commandDelayTicks = commands.CommandDelayTicks;
            _adjacencyHash = HashAdjacency(map.Adjacency);
            _homes.Seed(_owned, _map);
            _homesSeeded = true;
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

        public ulong PersonalityJsonHash
        {
            get { return _personalityJsonHash; }
        }

        public ulong DifficultyJsonHash
        {
            get { return _difficultyJsonHash; }
        }

        public static EnemyAi MatchInit(
            MatchConfig match,
            byte[] mapBlob,
            int[] factionIds,
            string difficultyId,
            AiBalanceTables balance,
            ISimCommandSink commands,
            ISimWorldView world)
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

            AiDifficultyProfile difficulty = balance.GetDifficulty(difficultyId);
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(mapBlob);
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
                    difficulty.EconomyBonusPpt,
                    difficulty.MaxCandidateGoals,
                    personality.TaskForceTargetSize,
                    personality.ReservePpt,
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
                match.Seed);
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

            AccrueIncome();
            StepCaptureOccupancy();
            FlushPendingCommands();

            int workUnits = 0;
            int phase = tick % AiConstants.TimeSliceModulus;
            bool ranStrategic = false;

            for (int i = 0; i < _owned.FactionCount; i++)
            {
                AiFactionState faction = _owned.FactionAt(i);
                faction.PathRequestsThisTick = 0;
                faction.WorkUnitsThisTick = 0;

                if (workUnits >= AiConstants.AiWorkUnitsPerTick)
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
            hash.AddULong(_personalityJsonHash);
            hash.AddULong(_difficultyJsonHash);
            hash.AddULong(_map.ContentHash);
            hash.AddInt(_map.TheDallesTownId);
            hash.AddInt(_map.TheDallesTerritoryId);
            hash.AddULong(_adjacencyHash);
            hash.AddInt(_map.WheeledBlockedCount);
            hash.AddBool(_homesSeeded);

            _owned.HashInto(hash);
            return hash.Digest();
        }

        public byte[] Save()
        {
            return _owned.Save();
        }

        public void Load(byte[] blob)
        {
            _owned.Load(blob);
            _homesSeeded = true;
        }

        public int ManpowerOf(int factionId)
        {
            return _owned.GetFaction(factionId).Manpower;
        }

        public int ManpowerIncomePerSecond(int factionId)
        {
            return _economy.IncomePerSecond(factionId);
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
                    garrisonNeed += missing * AiConstants.BattalionManpowerCost;
                }
            }

            return garrisonNeed + (faction.ManpowerReserveSeconds * ManpowerIncomePerSecond(factionId));
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
            return _owned.GetFaction(factionId).DepotCount;
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

            int seatIndex = _map.IndexOfTown(territory.SeatTownId);
            return seatIndex >= 0 && _owned.TownOwnerAt(seatIndex) == factionId;
        }

        public int TheDallesOwnerId()
        {
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
                faction.DepotCount,
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

        private void AccrueIncome()
        {
            _economy.AccrueTick();
        }

        private void StepCaptureOccupancy()
        {
            _occupancy.CopyOccupancyBattalions(_battalionScratch);
            for (int i = 0; i < _map.TownCount; i++)
            {
                AiTownRecord town = _map.TownAt(i);
                int owner = _owned.TownOwnerAt(i);
                CaptureZoneOccupancy occupancy = ClassifyTownOccupancy(town, out int occupant);
                if (occupancy != CaptureZoneOccupancy.Unique)
                {
                    continue;
                }

                if (owner != 0 && occupant != owner)
                {
                    continue;
                }

                if (owner == occupant)
                {
                    continue;
                }

                int current = _owned.CaptureFactionAt(i);
                int held = _owned.CaptureHeldTicksAt(i);
                if (current != occupant)
                {
                    current = occupant;
                    held = 0;
                }

                held++;
                int need = AiMapSnapshot.CaptureTicksForClass(town.PopulationClass);
                if (held >= need)
                {
                    _owned.SetTownOwner(i, occupant);
                }
                else
                {
                    _owned.SetCapture(i, current, held);
                }
            }
        }

        private CaptureZoneOccupancy ClassifyTownOccupancy(AiTownRecord town, out int occupant)
        {
            occupant = 0;
            long radiusSq = (long)AiConstants.CaptureZoneRadiusCm * AiConstants.CaptureZoneRadiusCm;
            for (int i = 0; i < _battalionScratch.Count; i++)
            {
                SimBattalion battalion = _battalionScratch[i];
                if (!InRadius(battalion.PositionXCm, battalion.PositionYCm, town.XCm, town.YCm, radiusSq))
                {
                    continue;
                }

                if (occupant == 0)
                {
                    occupant = battalion.OwnerId;
                }
                else if (occupant != battalion.OwnerId)
                {
                    occupant = 0;
                    return CaptureZoneOccupancy.Contested;
                }
            }

            return occupant == 0 ? CaptureZoneOccupancy.Empty : CaptureZoneOccupancy.Unique;
        }

        private int CountOwnBattalionsInTown(int factionId, AiTownRecord town)
        {
            int count = 0;
            long radiusSq = (long)AiConstants.CaptureZoneRadiusCm * AiConstants.CaptureZoneRadiusCm;
            _world.CopyOwnBattalionIds(factionId, _idScratch);
            for (int i = 0; i < _idScratch.Count; i++)
            {
                if (!_world.TryGetOwnBattalion(factionId, _idScratch[i], out SimBattalion battalion))
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

        private static bool InRadius(int ax, int ay, int bx, int by, long radiusSq)
        {
            long dx = (long)ax - bx;
            long dy = (long)ay - by;
            return (dx * dx) + (dy * dy) <= radiusSq;
        }

        private void RunStrategicSlice(AiFactionState faction)
        {
            RebuildFrontlinePosts(faction);
            DropStaleOrders(faction);
            IssueOpeningForce(faction);
            int candidateCount = GenerateCandidates(faction);
            AcceptCandidates(faction, candidateCount);
            SpendProduction(faction);
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

                long radiusSq = (long)AiConstants.CaptureZoneRadiusCm * AiConstants.CaptureZoneRadiusCm;
                if (InRadius(battalion.PositionXCm, battalion.PositionYCm, town.XCm, town.YCm, radiusSq))
                {
                    continue;
                }

                if (battalion.HasTarget && battalion.TargetXCm == town.XCm && battalion.TargetYCm == town.YCm)
                {
                    continue;
                }

                Issue(faction, CommandKind.Move, order.BattalionId, town.XCm, town.YCm, 0);
            }
        }

        private void IssueOpeningForce(AiFactionState faction)
        {
            if (faction.OpeningForceIssued)
            {
                return;
            }

            if (!_map.TryGetTown(faction.HomeTownId, out AiTownRecord home))
            {
                faction.OpeningForceIssued = true;
                return;
            }

            int garrison = System.Math.Max(1, faction.MinGarrisonPerTown);
            int total = garrison + AiConstants.OpeningClaimBattalions;
            for (int i = 0; i < total; i++)
            {
                SpawnBattalionAt(faction, home.XCm, home.YCm);
            }

            faction.OpeningForceIssued = true;
        }

        private int SpawnBattalionAt(AiFactionState faction, int xCm, int yCm)
        {
            int id = faction.NextBattalionId;
            faction.NextBattalionId++;
            Issue(
                faction,
                CommandKind.SpawnBattalion,
                id,
                xCm,
                yCm,
                AiConstants.ClaimMarchSpeedCmPerSecond);
            return id;
        }

        private void Issue(AiFactionState faction, CommandKind kind, int battalionId, int arg0, int arg1, int arg2)
        {
            if (faction.PendingCount >= faction.Pending.Length)
            {
                throw new InvalidOperationException("AI reaction-delay queue is full.");
            }

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
            faction.Pending[faction.PendingCount] = pending;
            faction.PendingCount++;
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
                }
                else if (order.Type == AiGoalType.PlaceDepot)
                {
                    if (!OwnsTerritory(faction.FactionId, order.TargetTerritoryId) || HasDepotInTerritory(faction, order.TargetTerritoryId))
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
                cap = _candidates.Length;
            }

            AiPersonalityProfile personality = _balance.GetPersonality(faction.PersonalityId);
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
                int dist = ApproxDistCm(home.XCm, home.YCm, town.XCm, town.YCm);
                int distPpt = dist / AiConstants.MileCm;
                if (distPpt > 1000)
                {
                    distPpt = 1000;
                }

                int score = (personality.WeightTownValue * town.StrategicValue) / SimConfig.PptScale;
                score += (personality.WeightAnchorSet * (isSeat ? AiConstants.SeatAnchorBonusPpt : AiConstants.NonSeatBonusPpt)) / SimConfig.PptScale;
                score += (personality.WeightEconomy * (1000 - distPpt)) / SimConfig.PptScale;
                if (faction.Posture == AiPosture.BuildUp)
                {
                    score += 200;
                }

                if (town.TerritoryId == faction.HomeTerritoryId)
                {
                    score += 150;
                }

                score -= (personality.WeightDistance * distPpt) / SimConfig.PptScale;
                PushCandidate(ref count, cap, AiGoalType.ClaimTown, town.Id, town.TerritoryId, 0, score);
            }

            for (int t = 0; t < _map.TerritoryCount; t++)
            {
                AiTerritoryRecord territory = _map.TerritoryAt(t);
                if (!OwnsTerritory(faction.FactionId, territory.Id))
                {
                    continue;
                }

                if (HasDepotInTerritory(faction, territory.Id) || HasOrder(faction, AiGoalType.PlaceDepot, territory.Id))
                {
                    continue;
                }

                if (!TryPickDepotSite(faction, territory, out AiDepot site))
                {
                    continue;
                }

                int score = 1800 + personality.WeightEconomy;
                if (territory.Id != faction.HomeTerritoryId)
                {
                    score += 400;
                }

                PushCandidate(ref count, cap, AiGoalType.PlaceDepot, territory.SeatTownId, territory.Id, site.NodeId, score);
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
                    AiOrder order = default;
                    order.Id = faction.NextOrderId++;
                    order.Type = AiGoalType.ClaimTown;
                    order.TargetTownId = cand.TownId;
                    order.TargetTerritoryId = cand.TerritoryId;
                    order.BattalionId = battalionId;
                    order.Score = cand.Score;
                    faction.Orders[faction.OrderCount++] = order;
                }
                else if (cand.Type == AiGoalType.PlaceDepot)
                {
                    if (!OwnsTerritory(faction.FactionId, cand.TerritoryId))
                    {
                        continue;
                    }

                    if (!_map.TryGetTerritory(cand.TerritoryId, out AiTerritoryRecord territory))
                    {
                        continue;
                    }

                    if (!TryPickDepotSite(faction, territory, out AiDepot site))
                    {
                        continue;
                    }

                    if (faction.DepotCount >= faction.Depots.Length)
                    {
                        continue;
                    }

                    Issue(faction, CommandKind.Build, 0, site.XCm, site.YCm, AiConstants.BuildingTagDepot);
                    faction.Depots[faction.DepotCount++] = site;
                    AiOrder order = default;
                    order.Id = faction.NextOrderId++;
                    order.Type = AiGoalType.PlaceDepot;
                    order.TargetTownId = territory.SeatTownId;
                    order.TargetTerritoryId = territory.Id;
                    order.TargetNodeId = site.NodeId;
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
            long radiusSq = (long)AiConstants.CaptureZoneRadiusCm * AiConstants.CaptureZoneRadiusCm;
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

                if (InRadius(battalion.PositionXCm, battalion.PositionYCm, home.XCm, home.YCm, radiusSq)
                    && CountOwnBattalionsInTown(faction.FactionId, home) <= faction.MinGarrisonPerTown)
                {
                    continue;
                }

                return id;
            }

            return SpawnBattalionAt(faction, home.XCm, home.YCm);
        }

        private void SpendProduction(AiFactionState faction)
        {
            int ownedTowns = TownsHeldOf(faction.FactionId);
            int target = faction.TaskForceTargetSize + ownedTowns;
            int garrisonSlots = 0;
            for (int i = 0; i < _map.TownCount; i++)
            {
                if (_owned.TownOwnerAt(i) == faction.FactionId && _map.TownAt(i).PopulationClass >= 1)
                {
                    garrisonSlots += faction.MinGarrisonPerTown;
                }
            }

            if (target < garrisonSlots + AiConstants.OpeningClaimBattalions)
            {
                target = garrisonSlots + AiConstants.OpeningClaimBattalions;
            }

            int reserve = ManpowerReserveOf(faction.FactionId);
            int issued = faction.NextBattalionId - (faction.FactionId * AiConstants.BattalionIdStride + 1);
            if (issued >= target)
            {
                return;
            }

            int after = faction.Manpower - AiConstants.BattalionManpowerCost;
            if (after < reserve)
            {
                return;
            }

            if (!_map.TryGetTown(faction.HomeTownId, out AiTownRecord home))
            {
                return;
            }

            faction.Manpower = after;
            SpawnBattalionAt(faction, home.XCm, home.YCm);
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

        private bool HasDepotInTerritory(AiFactionState faction, int territoryId)
        {
            for (int i = 0; i < faction.DepotCount; i++)
            {
                if (faction.Depots[i].TerritoryId == territoryId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasOrder(AiFactionState faction, AiGoalType type, int territoryId)
        {
            for (int i = 0; i < faction.OrderCount; i++)
            {
                if (faction.Orders[i].Type == type && faction.Orders[i].TargetTerritoryId == territoryId)
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

        private bool TryPickDepotSite(AiFactionState faction, AiTerritoryRecord territory, out AiDepot site)
        {
            site = default;
            if (!OwnsTerritory(faction.FactionId, territory.Id))
            {
                return false;
            }

            if (!_map.TryGetTown(territory.SeatTownId, out AiTownRecord seat))
            {
                return false;
            }

            int networkCount = CollectSupplyNetwork(faction);
            if (networkCount < 1)
            {
                return false;
            }

            RoadGraph roads = _map.Roads;
            if (TryAcceptDepotNode(roads, seat.RoadNodeId, territory.Id, networkCount))
            {
                if (!roads.TryGetNodeIndex(seat.RoadNodeId, out int seatIndex))
                {
                    return false;
                }

                site.TerritoryId = territory.Id;
                site.TerritoryStableKey = territory.StableKey;
                site.SeatTownId = territory.SeatTownId;
                site.NodeId = seat.RoadNodeId;
                site.XCm = roads.GetNodeXCm(seatIndex);
                site.YCm = roads.GetNodeYCm(seatIndex);
                return true;
            }

            for (int i = 0; i < roads.NodeCount; i++)
            {
                if (roads.GetNodeTerritoryId(i) != territory.Id)
                {
                    continue;
                }

                int nodeId = roads.GetNodeId(i);
                if (!TryAcceptDepotNode(roads, nodeId, territory.Id, networkCount))
                {
                    continue;
                }

                site.TerritoryId = territory.Id;
                site.TerritoryStableKey = territory.StableKey;
                site.SeatTownId = territory.SeatTownId;
                site.NodeId = nodeId;
                site.XCm = roads.GetNodeXCm(i);
                site.YCm = roads.GetNodeYCm(i);
                return true;
            }

            return false;
        }

        private bool TryAcceptDepotNode(RoadGraph roads, int nodeId, int territoryId, int networkCount)
        {
            if (!roads.TryGetNodeIndex(nodeId, out int index))
            {
                return false;
            }

            if (roads.GetNodeTerritoryId(index) != territoryId)
            {
                return false;
            }

            if (DepotCountOnNode(nodeId) >= AiConstants.MaxDepotsPerNode)
            {
                return false;
            }

            if (!AiRoadSupply.ConnectedToNetwork(roads, nodeId, _networkScratch, networkCount))
            {
                return false;
            }

            return SpacingOk(nodeId);
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

        private int DepotCountOnNode(int nodeId)
        {
            int count = 0;
            for (int f = 0; f < _owned.FactionCount; f++)
            {
                AiFactionState faction = _owned.FactionAt(f);
                for (int d = 0; d < faction.DepotCount; d++)
                {
                    if (faction.Depots[d].NodeId == nodeId)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private bool SpacingOk(int nodeId)
        {
            for (int f = 0; f < _owned.FactionCount; f++)
            {
                AiFactionState faction = _owned.FactionAt(f);
                for (int d = 0; d < faction.DepotCount; d++)
                {
                    int other = faction.Depots[d].NodeId;
                    int dist = AiRoadSupply.DistanceCm(_map.Roads, nodeId, other);
                    if (dist < AiConstants.DepotMinSpacingCm)
                    {
                        return false;
                    }
                }
            }

            return true;
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

        private static SimRandom SeedRng(ulong factionStream, ulong streamTag)
        {
            ulong seed = XxHash64.Mix(factionStream, streamTag);
            return new SimRandom(seed);
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

    internal enum CaptureZoneOccupancy : byte
    {
        Empty = 0,
        Unique = 1,
        Contested = 2
    }
}
