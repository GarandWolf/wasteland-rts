using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    public readonly struct AiHashSample
    {
        public readonly int Tick;
        public readonly ulong Hash;

        public AiHashSample(int tick, ulong hash)
        {
            Tick = tick;
            Hash = hash;
        }
    }

    public readonly struct AiTownHoldSample
    {
        public readonly int Tick;
        public readonly int FactionId;
        public readonly int TownsHeld;
        public readonly int Battalions;
        public readonly int Manpower;
        public readonly int Depots;
        public readonly int TheDallesOwnerId;

        public AiTownHoldSample(
            int tick,
            int factionId,
            int townsHeld,
            int battalions,
            int manpower,
            int depots,
            int theDallesOwnerId)
        {
            Tick = tick;
            FactionId = factionId;
            TownsHeld = townsHeld;
            Battalions = battalions;
            Manpower = manpower;
            Depots = depots;
            TheDallesOwnerId = theDallesOwnerId;
        }
    }

    /// <summary>
    /// Headless AI-vs-AI runner. Steps the foundation world, then the AI, and
    /// folds AI-owned state into the periodic hash. Debug logs are not hashed.
    /// </summary>
    public sealed class AiMatchHost
    {
        private readonly SimWorld _world;
        private readonly EnemyAi _ai;
        private readonly LoggingCommandSink _sink;
        private readonly List<string> _headlessLogs = new List<string>();
        private readonly List<AiHashSample> _hashes = new List<AiHashSample>();
        private readonly List<AiTownHoldSample> _holdSamples = new List<AiTownHoldSample>();
        private readonly int[] _factionIds;

        private AiMatchHost(SimWorld world, EnemyAi ai, LoggingCommandSink sink, int[] factionIds)
        {
            _world = world;
            _ai = ai;
            _sink = sink;
            _factionIds = factionIds;
            RecordHash();
            RecordHolds();
        }

        public SimWorld World
        {
            get { return _world; }
        }

        public EnemyAi Ai
        {
            get { return _ai; }
        }

        public IReadOnlyList<string> HeadlessLogs
        {
            get { return _headlessLogs; }
        }

        public IReadOnlyList<AiHashSample> Hashes
        {
            get { return _hashes; }
        }

        public IReadOnlyList<AiTownHoldSample> HoldSamples
        {
            get { return _holdSamples; }
        }

        public IReadOnlyList<SimCommand> CommandLog
        {
            get { return _sink.Log; }
        }

        public int Tick
        {
            get { return _world.Tick; }
        }

        public static AiMatchHost Start(
            MatchConfig match,
            byte[] mapBlob,
            int[] factionIds,
            string difficultyId,
            AiBalanceTables balance)
        {
            if (factionIds == null)
            {
                throw new ArgumentNullException(nameof(factionIds));
            }

            int[] copy = (int[])factionIds.Clone();
            SortIds(copy);
            MatchConfig withHashes = match.WithBalanceHashes(balance.PersonalityJsonHash, balance.DifficultyJsonHash);
            SimWorld world = new SimWorld(withHashes);
            LoggingCommandSink sink = new LoggingCommandSink(world);
            EnemyAi ai = EnemyAi.MatchInit(withHashes, mapBlob, copy, difficultyId, balance, sink, sink);
            return new AiMatchHost(world, ai, sink, copy);
        }

        public static AiMatchHost ReplayTo(
            MatchConfig match,
            byte[] mapBlob,
            int[] factionIds,
            string difficultyId,
            AiBalanceTables balance,
            IReadOnlyList<SimCommand> commands,
            byte[] aiBlob,
            int tick)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (aiBlob == null)
            {
                throw new ArgumentNullException(nameof(aiBlob));
            }

            int[] copy = (int[])factionIds.Clone();
            SortIds(copy);
            MatchConfig withHashes = match.WithBalanceHashes(balance.PersonalityJsonHash, balance.DifficultyJsonHash);
            SimWorld world = new SimWorld(withHashes);
            LoggingCommandSink sink = new LoggingCommandSink(world);
            EnemyAi ai = EnemyAi.MatchInit(withHashes, mapBlob, copy, difficultyId, balance, sink, sink);
            int cursor = 0;
            while (world.Tick < tick)
            {
                while (cursor < commands.Count && commands[cursor].IssuedTick == world.Tick)
                {
                    world.Submit(commands[cursor]);
                    cursor++;
                }

                world.Step();
            }

            ai.Load(aiBlob);
            return new AiMatchHost(world, ai, sink, copy);
        }

        public ulong Step()
        {
            _ai.OnTick(_world.Tick);
            _world.Step();
            ulong hash = HashWorld();
            if (_world.Tick % AiConstants.HashPeriodTicks == 0)
            {
                _hashes.Add(new AiHashSample(_world.Tick, hash));
                RecordHolds();
            }

            MaybeLog();
            return hash;
        }

        public ulong HashWorld()
        {
            return XxHash64.Mix(_world.HashState(), _ai.HashState());
        }

        public void Run(int ticks)
        {
            if (ticks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks));
            }

            for (int i = 0; i < ticks; i++)
            {
                Step();
            }
        }

        public void Shutdown()
        {
            _ai.MatchShutdown();
        }

        private void RecordHash()
        {
            _hashes.Add(new AiHashSample(_world.Tick, HashWorld()));
        }

        private void RecordHolds()
        {
            int tick = _world.Tick;
            int dalles = _ai.TheDallesOwnerId();
            for (int i = 0; i < _factionIds.Length; i++)
            {
                int factionId = _factionIds[i];
                _holdSamples.Add(new AiTownHoldSample(
                    tick,
                    factionId,
                    _ai.TownsHeldOf(factionId),
                    _ai.BattalionCountOf(factionId),
                    _ai.ManpowerOf(factionId),
                    _ai.DepotCountOf(factionId),
                    dalles));
            }
        }

        private void MaybeLog()
        {
            int tick = _world.Tick;
            if (tick < AiConstants.HeadlessLogFromTick)
            {
                return;
            }

            if (tick % AiConstants.HeadlessLogPeriodTicks != 0)
            {
                return;
            }

            for (int i = 0; i < _factionIds.Length; i++)
            {
                _headlessLogs.Add(_ai.CaptureHeadlessSnapshot(tick, _factionIds[i]).FormatLine());
            }
        }

        private static void SortIds(int[] ids)
        {
            for (int i = 1; i < ids.Length; i++)
            {
                int value = ids[i];
                int j = i - 1;
                while (j >= 0 && ids[j] > value)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }

                ids[j + 1] = value;
            }
        }
    }
}
