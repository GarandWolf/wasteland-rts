using System;
using System.Collections.Generic;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Random;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// M0 enemy AI seams: <c>AiMatchInit</c> / <c>AiOnTick</c> / <c>AiMatchShutdown</c>,
    /// empty order tables, PCG32 streams, hashed AI-owned state. Issues orders
    /// only through <see cref="ISimCommandSink"/>.
    /// </summary>
    public sealed class EnemyAi
    {
        private readonly ISimCommandSink _commands;
        private readonly ISimWorldView _world;
        private readonly AiBalanceTables _balance;
        private readonly AiMapSnapshot _map;
        private readonly AiOwnedState _owned;
        private readonly AiDifficultyProfile _difficulty;
        private readonly ulong _matchSeed;
        private readonly ulong _balanceKey;
        private readonly ulong _mapKey;
        private readonly ulong _adjacencyHash;
        private readonly int _commandDelayTicks;
        private bool _shutdown;
        private int _strategicSliceRuns;

        private EnemyAi(
            ISimCommandSink commands,
            ISimWorldView world,
            AiBalanceTables balance,
            AiMapSnapshot map,
            AiOwnedState owned,
            AiDifficultyProfile difficulty,
            ulong matchSeed)
        {
            _commands = commands;
            _world = world;
            _balance = balance;
            _map = map;
            _owned = owned;
            _difficulty = difficulty;
            _matchSeed = matchSeed;
            _balanceKey = balance.BalanceKey;
            _mapKey = map.MapKey;
            _commandDelayTicks = commands.CommandDelayTicks;
            _adjacencyHash = HashAdjacency(map.Adjacency);
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
                    personality.DefaultPosture,
                    SeedRng(factionStream, AiConstants.StreamStrategy),
                    SeedRng(factionStream, AiConstants.StreamTactical),
                    SeedRng(factionStream, AiConstants.StreamMistake),
                    SeedRng(factionStream, AiConstants.StreamScout));
            }

            return new EnemyAi(
                commands,
                world,
                balance,
                map,
                new AiOwnedState(factions),
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
                        RunEmptyStrategicSlice(faction);
                    }
                }

                if (phase >= 3 && phase <= 5 && faction.FactionId % 3 == phase - 3)
                {
                    RunEmptyTacticalSlice(faction);
                }
            }
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

            _owned.HashInto(hash);
            return hash.Digest();
        }

        public byte[] Save()
        {
            return _owned.Save(_matchSeed, _balanceKey, _mapKey);
        }

        public void Load(byte[] blob)
        {
            _owned.Load(blob, _matchSeed, _balanceKey, _mapKey);
        }

        public int ManpowerOf(int factionId)
        {
            _ = factionId;
            return 0;
        }

        public int ManpowerIncomePerSecond(int factionId)
        {
            _ = factionId;
            return 0;
        }

        public int ManpowerReserveOf(int factionId)
        {
            AiFactionState faction = _owned.GetFaction(factionId);
            int garrisonNeed = 0;
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

        public int TheDallesOwnerId()
        {
            return 0;
        }

        public AiHeadlessSnapshot CaptureHeadlessSnapshot(int tick, int factionId)
        {
            return new AiHeadlessSnapshot(
                tick,
                factionId,
                ManpowerOf(factionId),
                ManpowerReserveOf(factionId),
                TechSavingsOf(factionId),
                BattalionCountOf(factionId),
                _map.TheDallesTownId,
                TheDallesOwnerId());
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

        private void RunEmptyStrategicSlice(AiFactionState faction)
        {
            faction.OrderCount = 0;
            faction.TaskForceCount = 0;
            _strategicSliceRuns++;
        }

        private static void RunEmptyTacticalSlice(AiFactionState faction)
        {
            _ = faction;
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
    }

    public readonly struct AiHeadlessSnapshot
    {
        public readonly int Tick;
        public readonly int FactionId;
        public readonly int Manpower;
        public readonly int ManpowerReserve;
        public readonly int TechSavings;
        public readonly int BattalionCount;
        public readonly int TheDallesTownId;
        public readonly int TheDallesOwnerId;

        public AiHeadlessSnapshot(
            int tick,
            int factionId,
            int manpower,
            int manpowerReserve,
            int techSavings,
            int battalionCount,
            int theDallesTownId,
            int theDallesOwnerId)
        {
            Tick = tick;
            FactionId = factionId;
            Manpower = manpower;
            ManpowerReserve = manpowerReserve;
            TechSavings = techSavings;
            BattalionCount = battalionCount;
            TheDallesTownId = theDallesTownId;
            TheDallesOwnerId = theDallesOwnerId;
        }

        public string FormatLine()
        {
            return "t=" + Tick
                + " faction=" + FactionId
                + " manpower=" + Manpower
                + " reserve=" + ManpowerReserve
                + " techSavings=" + TechSavings
                + " battalions=" + BattalionCount
                + " theDallesTown=" + TheDallesTownId
                + " theDallesOwner=" + TheDallesOwnerId;
        }
    }
}
