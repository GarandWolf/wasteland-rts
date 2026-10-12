using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
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

        private const string MvpScenarioId = "columbia_gorge_100mi_mvp";
        private const string MvpMapFileId = "golden_ci_columbia_gorge_100_v1_marsh";

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
            return Start(match, mapBlob, factionIds, difficultyId, balance, null);
        }

        public static AiMatchHost Start(
            MatchConfig match,
            byte[] mapBlob,
            int[] factionIds,
            string difficultyId,
            AiBalanceTables balance,
            IAiScenarioSeats? seats)
        {
            if (factionIds == null)
            {
                throw new ArgumentNullException(nameof(factionIds));
            }

            int[] copy = (int[])factionIds.Clone();
            SortIds(copy);
            MatchConfig withKeys = BindKeys(match, mapBlob, balance);
            SimWorld world = new SimWorld(withKeys);
            LoggingCommandSink sink = new LoggingCommandSink(world);
            IAiScenarioSeats bound = seats ?? LaunchSeats(mapBlob, balance);
            EnemyAi ai = EnemyAi.MatchInit(withKeys, mapBlob, copy, difficultyId, balance, sink, sink, bound);
            BindMvpSeats(world, ai, balance);
            BindMvpRoads(world);
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
            return ReplayTo(match, mapBlob, factionIds, difficultyId, balance, commands, aiBlob, tick, null);
        }

        public static AiMatchHost ReplayTo(
            MatchConfig match,
            byte[] mapBlob,
            int[] factionIds,
            string difficultyId,
            AiBalanceTables balance,
            IReadOnlyList<SimCommand> commands,
            byte[] aiBlob,
            int tick,
            IAiScenarioSeats? seats)
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
            MatchConfig withKeys = BindKeys(match, mapBlob, balance);
            SimWorld world = new SimWorld(withKeys);
            LoggingCommandSink sink = new LoggingCommandSink(world);
            IAiScenarioSeats bound = seats ?? LaunchSeats(mapBlob, balance);
            EnemyAi ai = EnemyAi.MatchInit(withKeys, mapBlob, copy, difficultyId, balance, sink, sink, bound);
            BindMvpSeats(world, ai, balance);
            BindMvpRoads(world);
            ForwardingAiCommandSink? forwarding = ai.AiCommands as ForwardingAiCommandSink;
            if (forwarding != null)
            {
                forwarding.RestoreIssuedRaises(commands, balance.ScenarioCatalog);
            }

            // Event history is derived. Stepping from tick 0 rebuilds the last 64 ticks.
            // It is not part of the AI save.
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
            if (ai.AnyOverflowDropped())
            {
                throw new InvalidDataException(
                    "AI overflowed the command log or reaction queue; replay cannot continue.");
            }

            return new AiMatchHost(world, ai, sink, copy);
        }

        /// <summary>
        /// Spawn the MVP scenario's authored opening force onto the host factions.
        /// Seat ids come from balance faction bindings, matched to scenario seats
        /// by the personality economy faction. HQ is the seat town the map already
        /// assigned to that faction, not the origin.
        /// </summary>
        private static void BindMvpSeats(SimWorld world, EnemyAi ai, AiBalanceTables balance)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            string balanceDir = FindBalanceDirectory();
            BalanceCatalog catalog = balance.ScenarioCatalog ?? BalanceCatalog.LoadFromDirectory(balanceDir);

            string mapPath = MvpMapPath(balanceDir);
            string mapName = Path.GetFileName(mapPath);
            byte[] mapBytes = File.ReadAllBytes(mapPath);
            string scenarioPath = Path.Combine(balanceDir, "scenarios", MvpScenarioId + ".json");
            SimScenario scenario = SimScenario.Load(
                File.ReadAllText(scenarioPath),
                catalog,
                mapBytes,
                mapName,
                MvpScenarioId);
            world.BindCatalog(catalog);
            int[] resolvedIds = new int[balance.BindingCount];
            ScenarioSeat[] resolvedSeats = new ScenarioSeat[balance.BindingCount];
            int resolvedCount = 0;
            for (int i = 0; i < balance.BindingCount; i++)
            {
                int factionId = balance.BindingFactionIdAt(i);
                if (!TryScenarioSeatForFaction(balance, scenario, factionId, out ScenarioSeat scenarioSeat))
                {
                    continue;
                }

                resolvedIds[resolvedCount] = factionId;
                resolvedSeats[resolvedCount] = scenarioSeat;
                resolvedCount++;
            }

            if (resolvedCount < 1)
            {
                throw new InvalidOperationException("No scenario seat matched a bound faction.");
            }

            int[] worldFactions = new int[resolvedCount];
            for (int i = 0; i < resolvedCount; i++)
            {
                worldFactions[i] = resolvedIds[i];
            }

            ai.Owned.BindMatchSeatIds(worldFactions);
            for (int i = 0; i < resolvedCount; i++)
            {
                int factionId = worldFactions[i];
                ScenarioSeat scenarioSeat = resolvedSeats[i];
                int homeTownId = scenarioSeat.HomeTownId;
                AiFactionState aiFaction;
                if (ai.Owned.TryGetFaction(factionId, out aiFaction))
                {
                    homeTownId = aiFaction.HomeTownId;
                }

                if (!ai.Map.TryGetTown(homeTownId, out AiTownRecord town)
                    || (town.XCm == 0 && town.YCm == 0))
                {
                    throw new InvalidOperationException(
                        "MVP seat " + scenarioSeat.Faction + " has no seat-town position on this map.");
                }

                // BindHoldings replaces this with base + hqBonus + owned holding bonuses.
                // buildings.json hq.popCapBonus is not read.
                world.BindSeat(new SeatSetup(
                    factionId,
                    scenarioSeat.Slot,
                    scenarioSeat.Faction,
                    town.Id,
                    town.XCm,
                    town.YCm,
                    1,
                    -1,
                    scenarioSeat.StartingManpower,
                    catalog.StartingMateriel,
                    catalog.StartingFuel,
                    scenario.MatchRules.FuelEnabled,
                    catalog.StartingSeatPopCap,
                    scenarioSeat.RaisableUnits,
                    scenarioSeat.AutoResearch,
                    scenarioSeat.StartingUnits));
            }

            world.BindHoldings(scenario, ai.Map);
        }

        /// <summary>
        /// Move follows the frozen marsh road graph. Without this, ApplyMove
        /// clears the path and walks a straight line to the town point.
        /// </summary>
        private static void BindMvpRoads(SimWorld world)
        {
            string mapPath = MvpMapPath(FindBalanceDirectory());
            byte[] mapBlob = File.ReadAllBytes(mapPath);
            string? goldenDir = Path.GetDirectoryName(mapPath);
            if (goldenDir == null)
            {
                throw new DirectoryNotFoundException(mapPath);
            }

            string terrainPath = Path.Combine(goldenDir, TerrainSiblingFileName(Path.GetFileName(mapPath)));
            byte[] terrainBlob = File.ReadAllBytes(terrainPath);
            MovementService movement = MovementService.Create(mapBlob, terrainBlob, MovementTables.Default());
            world.BindRoads(movement, Wrmb.ReadRoadGraph(mapBlob));
        }

        private static string MvpMapPath(string balanceDir)
        {
            string mapName = SimScenario.ExpectedWrmbFileName(MvpMapFileId);
            DirectoryInfo balanceInfo = new DirectoryInfo(balanceDir);
            DirectoryInfo? dataDir = balanceInfo.Parent;
            DirectoryInfo? root = dataDir == null ? null : dataDir.Parent;
            if (root == null)
            {
                throw new DirectoryNotFoundException(balanceDir);
            }

            return Path.Combine(root.FullName, "data", "golden", mapName);
        }

        /// <summary>
        /// <c>golden-ci-columbia-gorge-100.v1-marsh.wrmb</c> becomes
        /// <c>golden-ci-columbia-gorge-100.terrain.v1-marsh.wrmb</c>.
        /// </summary>
        private static string TerrainSiblingFileName(string mapFileName)
        {
            int dot = mapFileName.IndexOf('.');
            if (dot <= 0)
            {
                throw new InvalidDataException("MVP map file name has no version suffix.");
            }

            return mapFileName.Substring(0, dot) + ".terrain" + mapFileName.Substring(dot);
        }

        /// <summary>
        /// Match a bound faction id to the scenario seat whose faction key equals
        /// that personality's economy faction. Ids are not assumed to be 1..N.
        /// </summary>
        internal static bool TryScenarioSeatForFaction(
            AiBalanceTables balance,
            SimScenario scenario,
            int factionId,
            out ScenarioSeat seat)
        {
            seat = default;
            if (balance == null || scenario == null || factionId == 0)
            {
                return false;
            }

            if (!balance.TryGetBoundPersonalityId(factionId, out string personalityId))
            {
                return false;
            }

            AiPersonalityProfile personality = balance.GetPersonality(personalityId);
            if (string.IsNullOrEmpty(personality.EconomyFactionId))
            {
                return false;
            }

            for (int i = 0; i < scenario.SeatCount; i++)
            {
                ScenarioSeat candidate = scenario.SeatAt(i);
                if (candidate.Faction == personality.EconomyFactionId)
                {
                    seat = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string FindBalanceDirectory()
        {
            string marker = Path.Combine("scenarios", MvpScenarioId + ".json");
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "data", "balance");
                if (File.Exists(Path.Combine(candidate, marker)))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            string cwd = Path.Combine(Directory.GetCurrentDirectory(), "data", "balance");
            if (File.Exists(Path.Combine(cwd, marker)))
            {
                return cwd;
            }

            throw new DirectoryNotFoundException(
                "Could not find data/balance/scenarios/" + MvpScenarioId + ".json.");
        }

        private static MatchConfig BindKeys(MatchConfig match, byte[] mapBlob, AiBalanceTables balance)
        {
            if (balance.BalanceKey == 0UL)
            {
                throw new ArgumentException(
                    "Balance tables must include a BalanceKey. Use AiBalanceTables.FromDirectory.",
                    nameof(balance));
            }

            WrmbFile mapFile = WrmbFile.Parse(mapBlob);
            ulong mapKey = MapKey.For(mapFile, AiConstants.MapKeySectionTags());
            if (match.BalanceKey != 0UL && match.BalanceKey != balance.BalanceKey)
            {
                throw new ArgumentException("match.BalanceKey does not match the balance tables.", nameof(match));
            }

            if (match.MapKey != 0UL && match.MapKey != mapKey)
            {
                throw new ArgumentException("match.MapKey does not match the map blob.", nameof(match));
            }

            return match.WithKeys(balance.BalanceKey, mapKey);
        }

        private static IAiScenarioSeats LaunchSeats(byte[] mapBlob, AiBalanceTables balance)
        {
            return FixtureAiScenarioSeats.ForLaunchPersonalities(AiMapSnapshot.FromWrmb(mapBlob), balance);
        }

        public ulong Step()
        {
            _ai.OnTick(_world.Tick);
            _world.Step();
            ulong hash = 0UL;
            if (_world.Tick % AiConstants.HashPeriodTicks == 0)
            {
                hash = HashWorld();
                _hashes.Add(new AiHashSample(_world.Tick, hash));
                RecordHolds();
            }

            MaybeLog();
            return hash;
        }

        public ulong HashWorld()
        {
            return AiWorldHash.Mix(_world.HashState(), _ai.HashState());
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
