using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;

namespace Wasteland.Sim.Ai
{
    public sealed class AiPersonalityProfile
    {
        public readonly string Id;
        public readonly bool Stub;
        public readonly int WeightTownValue;
        public readonly int WeightAnchorSet;
        public readonly int WeightThreat;
        public readonly int WeightForceRatio;
        public readonly int WeightEconomy;
        public readonly int WeightChokepoint;
        public readonly int WeightSupplyCut;
        public readonly int WeightSupplyGuard;
        public readonly int WeightDistance;
        public readonly int WeightRace;
        public readonly int WeightVisionGround;
        public readonly int AggressionBias;
        public readonly int TurtleBias;
        public readonly int RaidBias;
        public readonly int ScoutBias;
        public readonly int ReservePpt;
        public readonly int SiegeInvestRatioPpt;
        public readonly int SiegeAssaultRatioPpt;
        public readonly int RetreatThresholdPpt;
        public readonly int PostureSwitchMarginPpt;
        public readonly int NonSeatBonusPpt;
        public readonly int BuildUpScoreBonus;
        public readonly int DepotBaseScore;
        public readonly int AwayDepotScoreBonus;

        /// <summary>
        /// Additive, like <see cref="NonSeatBonusPpt"/>: the prize wins when
        /// <c>prizeDist*1000 &lt; otherDist*(1000+bonus)</c> in long, so 300
        /// means it can be up to 30% farther; an exact tie goes to the prize;
        /// unreachable targets never enter the comparison; the HQ-in-supply
        /// override and defend still come first.
        /// </summary>
        public readonly int PrizeTargetBonusPpt;

        /// <summary>
        /// Line units kept per anti-armour unit (integer). 2 means raise
        /// one anti-armour after every two line units.
        /// </summary>
        public readonly int LineUnitsPerAntiArmour;
        public readonly string EconomyFactionId;
        public readonly int MinGarrisonPerTown;
        public readonly int TaskForceTargetSize;
        public readonly AiPosture DefaultPosture;

        internal AiPersonalityProfile(
            string id,
            bool stub,
            int weightTownValue,
            int weightAnchorSet,
            int weightThreat,
            int weightForceRatio,
            int weightEconomy,
            int weightChokepoint,
            int weightSupplyCut,
            int weightSupplyGuard,
            int weightDistance,
            int weightRace,
            int weightVisionGround,
            int aggressionBias,
            int turtleBias,
            int raidBias,
            int scoutBias,
            int reservePpt,
            int siegeInvestRatioPpt,
            int siegeAssaultRatioPpt,
            int retreatThresholdPpt,
            int postureSwitchMarginPpt,
            int nonSeatBonusPpt,
            int buildUpScoreBonus,
            int depotBaseScore,
            int awayDepotScoreBonus,
            int prizeTargetBonusPpt,
            int lineUnitsPerAntiArmour,
            string economyFactionId,
            int minGarrisonPerTown,
            int taskForceTargetSize,
            AiPosture defaultPosture)
        {
            Id = id;
            Stub = stub;
            WeightTownValue = weightTownValue;
            WeightAnchorSet = weightAnchorSet;
            WeightThreat = weightThreat;
            WeightForceRatio = weightForceRatio;
            WeightEconomy = weightEconomy;
            WeightChokepoint = weightChokepoint;
            WeightSupplyCut = weightSupplyCut;
            WeightSupplyGuard = weightSupplyGuard;
            WeightDistance = weightDistance;
            WeightRace = weightRace;
            WeightVisionGround = weightVisionGround;
            AggressionBias = aggressionBias;
            TurtleBias = turtleBias;
            RaidBias = raidBias;
            ScoutBias = scoutBias;
            ReservePpt = reservePpt;
            SiegeInvestRatioPpt = siegeInvestRatioPpt;
            SiegeAssaultRatioPpt = siegeAssaultRatioPpt;
            RetreatThresholdPpt = retreatThresholdPpt;
            PostureSwitchMarginPpt = postureSwitchMarginPpt;
            NonSeatBonusPpt = nonSeatBonusPpt;
            BuildUpScoreBonus = buildUpScoreBonus;
            DepotBaseScore = depotBaseScore;
            AwayDepotScoreBonus = awayDepotScoreBonus;
            PrizeTargetBonusPpt = prizeTargetBonusPpt;
            LineUnitsPerAntiArmour = lineUnitsPerAntiArmour;
            EconomyFactionId = economyFactionId ?? throw new ArgumentNullException(nameof(economyFactionId));
            MinGarrisonPerTown = minGarrisonPerTown;
            TaskForceTargetSize = taskForceTargetSize;
            DefaultPosture = defaultPosture;
        }
    }

    public sealed class AiDifficultyProfile
    {
        public readonly string Id;
        public readonly int ReactionExtraTicks;
        public readonly int StrategicCadenceTicks;
        public readonly int AggressionMulPpt;
        public readonly int ScoutCadenceTicks;
        public readonly string FogCheat;
        public readonly int MemoryDecayPerCadencePpt;
        public readonly int BuildSpeedBonusPpt;
        public readonly int VisionRadiusBonusCm;
        public readonly int MicroQuality;
        public readonly int MistakeRatePpt;
        public readonly int RetreatThresholdMulPpt;
        public readonly int MaxActiveOrders;
        public readonly int MaxCandidateGoals;
        public readonly int ManpowerReserveSeconds;
        public readonly int BattalionManpowerCost;
        public readonly int AttackStrengthRatioPpt;

        internal AiDifficultyProfile(
            string id,
            int reactionExtraTicks,
            int strategicCadenceTicks,
            int aggressionMulPpt,
            int scoutCadenceTicks,
            string fogCheat,
            int memoryDecayPerCadencePpt,
            int buildSpeedBonusPpt,
            int visionRadiusBonusCm,
            int microQuality,
            int mistakeRatePpt,
            int retreatThresholdMulPpt,
            int maxActiveOrders,
            int maxCandidateGoals,
            int manpowerReserveSeconds,
            int battalionManpowerCost,
            int attackStrengthRatioPpt)
        {
            Id = id;
            ReactionExtraTicks = reactionExtraTicks;
            StrategicCadenceTicks = strategicCadenceTicks;
            AggressionMulPpt = aggressionMulPpt;
            ScoutCadenceTicks = scoutCadenceTicks;
            FogCheat = fogCheat;
            MemoryDecayPerCadencePpt = memoryDecayPerCadencePpt;
            BuildSpeedBonusPpt = buildSpeedBonusPpt;
            VisionRadiusBonusCm = visionRadiusBonusCm;
            MicroQuality = microQuality;
            MistakeRatePpt = mistakeRatePpt;
            RetreatThresholdMulPpt = retreatThresholdMulPpt;
            MaxActiveOrders = maxActiveOrders;
            MaxCandidateGoals = maxCandidateGoals;
            ManpowerReserveSeconds = manpowerReserveSeconds;
            BattalionManpowerCost = battalionManpowerCost;
            AttackStrengthRatioPpt = attackStrengthRatioPpt;
        }
    }

    /// <summary>
    /// Strict integer JSON for personalities and difficulty. File hashes are
    /// match config (xxHash64 over UTF-8 bytes), not rebuilt per tick.
    /// Both files carry the same integer pair as the designer balance JSON:
    /// <c>schemaVersion</c> next to <c>dataVersion</c> (AI content is
    /// dataVersion 1; designer pack files are currently dataVersion 6).
        /// Parse requires <c>dataVersion</c> as an int32 — missing or non-int
        /// values are rejected. <see cref="FromDirectory"/> also loads
        /// <c>rules.json</c> capture, supply caps, AI goal ceiling, difficulty
        /// income multipliers, <c>economy.json</c> holding-tier / HQ /
        /// faction manpower plus <c>contestedHoldingPaysIncome</c>, and
        /// binds unit and building roles through
        /// <c>BalanceCatalog</c>.
        /// </summary>
    public sealed partial class AiBalanceTables
    {
        public const string CaptureRuleHamlet = "hamlet";
        public const string CaptureRuleVillage = "village";
        public const string CaptureRuleTown = "town";
        public const string CaptureRuleCity = "city";
        public const string RuleIdAiMaxCandidateGoals = "ai.maxCandidateGoals";
        public const string RuleIdSupplyMaxDepots = "supply.maxDepotsPerFaction";
        public const string RuleIdSupplyScratch = "supply.connectivityScratchSize";
        public const string RuleIdWorkUnitsPerTick = "workUnitsPerTick";
        public const string RuleIdContestedHoldingPaysIncome = "contestedHoldingPaysIncome";
        public const string RuleIdAttackStrengthRatioPpt = "attackStrengthRatioPpt";
        public const string RuleIdPrizeTargetBonusPpt = "prizeTargetBonusPpt";
        public const string RuleIdLineUnitsPerAntiArmour = "lineUnitsPerAntiArmour";
        public const string RuleIdTier2ResearchTicks = "tech.tier2.researchTicks";
        public const string RuleIdDepotRangeCm = "supply.depotRangeCm";

        private readonly AiPersonalityProfile[] _personalities;
        private readonly AiDifficultyProfile[] _difficulties;
        private readonly int[] _bindingFactionIds;
        private readonly string[] _bindingPersonalityIds;

        private readonly ulong _balanceKey;
        private readonly int _workUnitsPerTick;
        private readonly RulesState _rules;
        private readonly IAiUnitCatalog _unitCatalog;
        private readonly IAiBuildingCatalog _buildingCatalog;
        private readonly BalanceCatalog? _scenarioCatalog;

        private AiBalanceTables(
            ulong balanceKey,
            ulong personalityJsonHash,
            ulong difficultyJsonHash,
            int workUnitsPerTick,
            AiPersonalityProfile[] personalities,
            AiDifficultyProfile[] difficulties,
            int[] bindingFactionIds,
            string[] bindingPersonalityIds,
            RulesState rules,
            IAiUnitCatalog unitCatalog,
            IAiBuildingCatalog buildingCatalog,
            BalanceCatalog? scenarioCatalog = null)
        {
            _balanceKey = balanceKey;
            PersonalityJsonHash = personalityJsonHash;
            DifficultyJsonHash = difficultyJsonHash;
            _workUnitsPerTick = workUnitsPerTick;
            _personalities = personalities;
            _difficulties = difficulties;
            _bindingFactionIds = bindingFactionIds;
            _bindingPersonalityIds = bindingPersonalityIds;
            _rules = rules ?? new RulesState();
            _unitCatalog = unitCatalog ?? AiUnitCatalog.Empty;
            _buildingCatalog = buildingCatalog ?? AiBuildingCatalog.Empty;
            _scenarioCatalog = scenarioCatalog;
        }

        public ulong BalanceKey
        {
            get { return _balanceKey; }
        }

        public ulong PersonalityJsonHash { get; }

        public ulong DifficultyJsonHash { get; }

        public IAiUnitCatalog UnitCatalog
        {
            get { return _unitCatalog; }
        }

        public IAiBuildingCatalog BuildingCatalog
        {
            get { return _buildingCatalog; }
        }

        /// <summary>
        /// Designer pack used to back <see cref="UnitCatalog"/> and
        /// <see cref="BuildingCatalog"/> after <see cref="FromDirectory"/>.
        /// Null when tables were parsed without a balance directory.
        /// </summary>
        public BalanceCatalog? ScenarioCatalog
        {
            get { return _scenarioCatalog; }
        }

        public int BindingCount
        {
            get { return _bindingFactionIds.Length; }
        }

        public int BindingFactionIdAt(int index)
        {
            return _bindingFactionIds[index];
        }

        /// <summary>
        /// Copy these tables with one factionBindings row removed. Personalities,
        /// rules, catalogs, and the balance key stay. Used so match setup can be
        /// proven against a missing binding without editing data files.
        /// </summary>
        internal AiBalanceTables WithoutFactionBinding(int factionId)
        {
            int keep = 0;
            for (int i = 0; i < _bindingFactionIds.Length; i++)
            {
                if (_bindingFactionIds[i] != factionId)
                {
                    keep++;
                }
            }

            if (keep == _bindingFactionIds.Length)
            {
                return this;
            }

            int[] ids = new int[keep];
            string[] personalityIds = new string[keep];
            int write = 0;
            for (int i = 0; i < _bindingFactionIds.Length; i++)
            {
                if (_bindingFactionIds[i] == factionId)
                {
                    continue;
                }

                ids[write] = _bindingFactionIds[i];
                personalityIds[write] = _bindingPersonalityIds[i];
                write++;
            }

            return new AiBalanceTables(
                _balanceKey,
                PersonalityJsonHash,
                DifficultyJsonHash,
                _workUnitsPerTick,
                _personalities,
                _difficulties,
                ids,
                personalityIds,
                _rules,
                _unitCatalog,
                _buildingCatalog,
                _scenarioCatalog);
        }

        public int PersonalityCount
        {
            get { return _personalities.Length; }
        }

        public int DifficultyCount
        {
            get { return _difficulties.Length; }
        }

        public bool HasCaptureTable
        {
            get { return _rules.HasCapture; }
        }

        public bool HasEconomyTable
        {
            get { return _rules.HasEconomy; }
        }

        public int WorkUnitsPerTick
        {
            get { return _workUnitsPerTick; }
        }

        public int MaxDepotsPerFaction
        {
            get
            {
                RequireRulesLimits();
                return _rules.MaxDepotsPerFaction;
            }
        }

        public int ConnectivityScratchSize
        {
            get
            {
                RequireRulesLimits();
                return _rules.ConnectivityScratchSize;
            }
        }

        public int DepotRangeCm
        {
            get
            {
                RequireRulesLimits();
                return _rules.DepotRangeCm;
            }
        }

        public int MaxCandidateGoalsCeiling
        {
            get
            {
                RequireRulesLimits();
                return _rules.MaxCandidateGoalsCeiling;
            }
        }

        public int Tier2ResearchTicks
        {
            get
            {
                RequireRulesLimits();
                return _rules.Tier2ResearchTicks;
            }
        }

        public bool ContestedHoldingPaysIncome
        {
            get
            {
                RequireEconomy();
                return _rules.ContestedHoldingPaysIncome;
            }
        }

        public int DecayStartsAfterTicks
        {
            get
            {
                RequireCapture();
                return _rules.DecayStartsAfterTicks;
            }
        }

        public int DecayTicksPerTick
        {
            get
            {
                RequireCapture();
                return _rules.DecayTicksPerTick;
            }
        }

        public int AccrualIntervalTicks
        {
            get
            {
                RequireEconomy();
                return _rules.AccrualIntervalTicks;
            }
        }

        public int AccrualIntervalsPerMinute
        {
            get
            {
                RequireEconomy();
                return _rules.AccrualIntervalsPerMinute;
            }
        }

        public static AiBalanceTables FromDirectory(string balanceDirectory)
        {
            if (balanceDirectory == null)
            {
                throw new ArgumentNullException(nameof(balanceDirectory));
            }

            return FromDirectory(
                balanceDirectory,
                File.ReadAllText(Path.Combine(balanceDirectory, "units.json")),
                File.ReadAllText(Path.Combine(balanceDirectory, "buildings.json")));
        }

        /// <summary>
        /// Production pack load. Unit and building roles come from
        /// <see cref="BalanceCatalog"/>. A missing building <c>role</c>
        /// throws inside that parse; an unknown role throws on the closed
        /// allowlist. Fixture JSON without a designer header still uses
        /// <see cref="AiBuildingCatalog.Parse"/>.
        /// </summary>
        public static AiBalanceTables FromDirectory(
            string balanceDirectory,
            string? unitsJson,
            string buildingsJson)
        {
            if (balanceDirectory == null)
            {
                throw new ArgumentNullException(nameof(balanceDirectory));
            }

            if (buildingsJson == null)
            {
                throw new ArgumentNullException(nameof(buildingsJson));
            }

            string personalitiesPath = Path.Combine(balanceDirectory, "ai", "ai_personalities.json");
            string difficultyPath = Path.Combine(balanceDirectory, "ai", "ai_difficulty.json");
            string rulesPath = Path.Combine(balanceDirectory, "rules.json");
            string economyPath = Path.Combine(balanceDirectory, "economy.json");
            AiBalanceTables tables = Parse(
                File.ReadAllText(personalitiesPath),
                File.ReadAllText(difficultyPath));
            RulesState rules = ParseCapture(File.ReadAllText(rulesPath));
            ParseEconomy(File.ReadAllText(economyPath), tables._personalities, rules);
            tables = tables
                .WithRules(rules)
                .WithBalanceKey(global::Wasteland.Sim.Movement.BalanceKey.For(balanceDirectory));

            if (HasDesignerHeader(buildingsJson))
            {
                string units = unitsJson
                    ?? File.ReadAllText(Path.Combine(balanceDirectory, "units.json"));
                BalanceCatalog catalog = BalanceCatalog.Parse(
                    units,
                    buildingsJson,
                    File.ReadAllText(rulesPath),
                    File.ReadAllText(economyPath),
                    File.ReadAllText(personalitiesPath));
                BalanceAiCatalog.RequireClosedRoles(catalog, buildingsJson);
                int depotManpower;
                int depotMateriel;
                int depotFuel;
                IAiBuildingCatalog buildings = AiBuildingCatalog.TryReadDepotCost(
                    buildingsJson,
                    out depotManpower,
                    out depotMateriel,
                    out depotFuel)
                    ? new BalanceAiBuildingCatalog(catalog, depotManpower, depotMateriel, depotFuel)
                    : new BalanceAiBuildingCatalog(catalog);
                return tables
                    .WithScenarioCatalog(catalog)
                    .WithUnitCatalog(new BalanceAiUnitCatalog(catalog))
                    .WithBuildingCatalog(buildings);
            }

            BalanceCatalog disk = BalanceCatalog.LoadFromDirectory(balanceDirectory);
            BalanceAiCatalog.RequireClosedRoles(
                disk,
                File.ReadAllText(Path.Combine(balanceDirectory, "buildings.json")));
            return tables
                .WithScenarioCatalog(disk)
                .WithUnitCatalog(new BalanceAiUnitCatalog(disk))
                .WithBuildingCatalog(AiBuildingCatalog.Parse(buildingsJson));
        }

        private static bool HasDesignerHeader(string json)
        {
            JsonValue root = IntegerJsonParser.Parse(json);
            return JsonUtil.TryField(root, "schemaVersion", out _);
        }

        public AiBalanceTables WithBalanceKey(ulong balanceKey)
        {
            return Clone(balanceKey, _rules);
        }

        public AiBalanceTables WithCapture(
            int hamletTicks,
            int villageTicks,
            int townTicks,
            int cityTicks)
        {
            if (hamletTicks < 1 || villageTicks < 1 || townTicks < 1 || cityTicks < 1)
            {
                throw new FormatException("capture.timerTicksByTier values must be >= 1.");
            }

            RulesState rules = _rules.Clone();
            rules.HasCapture = true;
            rules.CaptureTicks[AiHoldingClass.Hamlet] = hamletTicks;
            rules.CaptureTicks[AiHoldingClass.Village] = villageTicks;
            rules.CaptureTicks[AiHoldingClass.Town] = townTicks;
            rules.CaptureTicks[AiHoldingClass.City] = cityTicks;
            return Clone(_balanceKey, rules);
        }

        internal AiBalanceTables WithRules(RulesState rules)
        {
            RequireCandidateGoalsWithinCeiling(rules);
            RequireDifficultiesHaveIncomeMultiplier(rules);
            return Clone(_balanceKey, rules);
        }

        public AiBalanceTables WithUnitCatalog(IAiUnitCatalog unitCatalog)
        {
            return new AiBalanceTables(
                _balanceKey,
                PersonalityJsonHash,
                DifficultyJsonHash,
                _workUnitsPerTick,
                _personalities,
                _difficulties,
                _bindingFactionIds,
                _bindingPersonalityIds,
                _rules,
                unitCatalog ?? AiUnitCatalog.Empty,
                _buildingCatalog,
                _scenarioCatalog);
        }

        public AiBalanceTables WithBuildingCatalog(IAiBuildingCatalog buildingCatalog)
        {
            return new AiBalanceTables(
                _balanceKey,
                PersonalityJsonHash,
                DifficultyJsonHash,
                _workUnitsPerTick,
                _personalities,
                _difficulties,
                _bindingFactionIds,
                _bindingPersonalityIds,
                _rules,
                _unitCatalog,
                buildingCatalog ?? AiBuildingCatalog.Empty,
                _scenarioCatalog);
        }

        internal AiBalanceTables WithScenarioCatalog(BalanceCatalog? catalog)
        {
            return new AiBalanceTables(
                _balanceKey,
                PersonalityJsonHash,
                DifficultyJsonHash,
                _workUnitsPerTick,
                _personalities,
                _difficulties,
                _bindingFactionIds,
                _bindingPersonalityIds,
                _rules,
                _unitCatalog,
                _buildingCatalog,
                catalog);
        }

        public int CaptureTicksForClass(int populationClass)
        {
            return CaptureTicksForClass(populationClass, 0);
        }

        public int CaptureTicksForClass(int populationClass, int fortificationLevel)
        {
            RequireCapture();
            string ruleId = AiHoldingClass.RuleId(populationClass);
            int ticks = _rules.CaptureTicks[AiHoldingClass.IndexOfRuleId(ruleId)];
            if (fortificationLevel < 0)
            {
                throw new FormatException("fortification level must be >= 0.");
            }

            int bonusPpt = fortificationLevel * _rules.FortPerLevelPpt;
            if (bonusPpt > _rules.FortMaxPpt)
            {
                bonusPpt = _rules.FortMaxPpt;
            }

            return (int)(((long)ticks * (SimConfig.PptScale + bonusPpt)) / SimConfig.PptScale);
        }

        public int CaptureRadiusCmForClass(int populationClass)
        {
            RequireCapture();
            string ruleId = AiHoldingClass.RuleId(populationClass);
            return _rules.ZoneRadiusCm[AiHoldingClass.IndexOfRuleId(ruleId)];
        }

        public int IncomePerMinForClass(int populationClass)
        {
            RequireEconomy();
            string ruleId = AiHoldingClass.RuleId(populationClass);
            return _rules.IncomePerMin[AiHoldingClass.IndexOfRuleId(ruleId)];
        }

        public int HqManpowerPerMin(string economyFactionId)
        {
            return LookupEconomy(economyFactionId, _rules.HqManpowerPerMin);
        }

        public int ManpowerModifierPpt(string economyFactionId)
        {
            return LookupEconomy(economyFactionId, _rules.ManpowerModifierPpt);
        }

        public int IncomeMultiplierPpt(string difficultyId)
        {
            RequireRulesLimits();
            if (difficultyId == null)
            {
                throw new ArgumentNullException(nameof(difficultyId));
            }

            for (int i = 0; i < _rules.DifficultyIds.Length; i++)
            {
                if (_rules.DifficultyIds[i] == difficultyId)
                {
                    return _rules.IncomeMultiplierPpt[i];
                }
            }

            throw new FormatException("difficulty." + difficultyId + ".incomeMultiplierPpt");
        }

        private int LookupEconomy(string economyFactionId, int[] values)
        {
            RequireEconomy();
            if (economyFactionId == null)
            {
                throw new ArgumentNullException(nameof(economyFactionId));
            }

            for (int i = 0; i < _rules.EconomyFactionIds.Length; i++)
            {
                if (_rules.EconomyFactionIds[i] == economyFactionId)
                {
                    return values[i];
                }
            }

            throw new FormatException("Missing economy faction key '" + economyFactionId + "'.");
        }

        private void RequireCapture()
        {
            if (!_rules.HasCapture)
            {
                throw new InvalidOperationException("Balance tables have no rules.json capture table.");
            }
        }

        private void RequireEconomy()
        {
            if (!_rules.HasEconomy)
            {
                throw new InvalidOperationException("Balance tables have no economy.json table.");
            }
        }

        private void RequireRulesLimits()
        {
            if (!_rules.HasLimits)
            {
                throw new InvalidOperationException("Balance tables have no rules.json AI/supply limits.");
            }
        }

        private AiBalanceTables Clone(ulong balanceKey, RulesState rules)
        {
            return new AiBalanceTables(
                balanceKey,
                PersonalityJsonHash,
                DifficultyJsonHash,
                _workUnitsPerTick,
                _personalities,
                _difficulties,
                _bindingFactionIds,
                _bindingPersonalityIds,
                rules,
                _unitCatalog,
                _buildingCatalog,
                _scenarioCatalog);
        }

        public static ulong HashUtf8(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return XxHash64.Hash(bytes, 0);
        }

        public static AiBalanceTables Parse(string personalitiesJson, string difficultyJson)
        {
            if (personalitiesJson == null)
            {
                throw new ArgumentNullException(nameof(personalitiesJson));
            }

            if (difficultyJson == null)
            {
                throw new ArgumentNullException(nameof(difficultyJson));
            }

            JsonValue personalityRoot = IntegerJsonParser.Parse(personalitiesJson);
            JsonValue difficultyRoot = IntegerJsonParser.Parse(difficultyJson);
            if (JsonUtil.Int32(JsonUtil.Field(personalityRoot, "schemaVersion"), "schemaVersion") != AiConstants.BalanceSchemaVersion)
            {
                throw new FormatException("Unsupported ai_personalities.json schema.");
            }

            RequireDataVersion(personalityRoot, "ai_personalities.json");

            if (JsonUtil.Int32(JsonUtil.Field(difficultyRoot, "schemaVersion"), "schemaVersion") != AiConstants.BalanceSchemaVersion)
            {
                throw new FormatException("Unsupported ai_difficulty.json schema.");
            }

            RequireDataVersion(difficultyRoot, "ai_difficulty.json");

            JsonValue personalityArray = JsonUtil.Field(personalityRoot, "personalities");
            if (personalityArray.Kind != JsonKind.Array || personalityArray.ArrayValue.Count < 1)
            {
                throw new FormatException("personalities must be a non-empty array.");
            }

            AiPersonalityProfile[] personalities = new AiPersonalityProfile[personalityArray.ArrayValue.Count];
            for (int i = 0; i < personalityArray.ArrayValue.Count; i++)
            {
                personalities[i] = ParsePersonality(personalityArray.ArrayValue[i]);
            }

            SortPersonalities(personalities);
            RequireUniquePersonalityIds(personalities);

            JsonValue difficultyArray = JsonUtil.Field(difficultyRoot, "difficulties");
            if (difficultyArray.Kind != JsonKind.Array || difficultyArray.ArrayValue.Count < 1)
            {
                throw new FormatException("difficulties must be a non-empty array.");
            }

            AiDifficultyProfile[] difficulties = new AiDifficultyProfile[difficultyArray.ArrayValue.Count];
            for (int i = 0; i < difficultyArray.ArrayValue.Count; i++)
            {
                difficulties[i] = ParseDifficulty(difficultyArray.ArrayValue[i]);
            }

            SortDifficulties(difficulties);
            RequireUniqueDifficultyIds(difficulties);

            JsonValue bindings = JsonUtil.Field(personalityRoot, "factionBindings");
            if (bindings.Kind != JsonKind.Array)
            {
                throw new FormatException("factionBindings must be an array.");
            }

            int[] bindingFactionIds = new int[bindings.ArrayValue.Count];
            string[] bindingPersonalityIds = new string[bindings.ArrayValue.Count];
            for (int i = 0; i < bindings.ArrayValue.Count; i++)
            {
                JsonValue row = bindings.ArrayValue[i];
                bindingFactionIds[i] = JsonUtil.Int32(JsonUtil.Field(row, "factionId"), "factionId");
                bindingPersonalityIds[i] = JsonUtil.String(JsonUtil.Field(row, "personalityId"), "personalityId");
            }

            SortBindings(bindingFactionIds, bindingPersonalityIds);
            RequireUniqueBindingFactionIds(bindingFactionIds);
            RequireBindingsReferencePersonalities(bindingPersonalityIds, personalities);

            int workUnitsPerTick = JsonUtil.Int32(
                JsonUtil.Field(difficultyRoot, RuleIdWorkUnitsPerTick),
                RuleIdWorkUnitsPerTick);
            if (workUnitsPerTick < 1)
            {
                throw new FormatException(RuleIdWorkUnitsPerTick + " must be >= 1.");
            }

            return new AiBalanceTables(
                0UL,
                HashUtf8(personalitiesJson),
                HashUtf8(difficultyJson),
                workUnitsPerTick,
                personalities,
                difficulties,
                bindingFactionIds,
                bindingPersonalityIds,
                new RulesState(),
                AiUnitCatalog.Empty,
                AiBuildingCatalog.Empty);
        }

        public static void ParseCaptureRules(
            string rulesJson,
            out int hamletTicks,
            out int villageTicks,
            out int townTicks,
            out int cityTicks)
        {
            RulesState rules = ParseCapture(rulesJson);
            hamletTicks = rules.CaptureTicks[AiHoldingClass.Hamlet];
            villageTicks = rules.CaptureTicks[AiHoldingClass.Village];
            townTicks = rules.CaptureTicks[AiHoldingClass.Town];
            cityTicks = rules.CaptureTicks[AiHoldingClass.City];
        }

        internal static RulesState ParseCapture(string rulesJson)
        {
            if (rulesJson == null)
            {
                throw new ArgumentNullException(nameof(rulesJson));
            }

            JsonValue root = IntegerJsonParser.Parse(rulesJson);
            if (JsonUtil.Int32(JsonUtil.Field(root, "schemaVersion"), "schemaVersion") != AiConstants.BalanceSchemaVersion)
            {
                throw new FormatException("Unsupported rules.json schema.");
            }

            JsonUtil.Int32(JsonUtil.Field(root, "dataVersion"), "dataVersion");
            JsonValue capture = JsonUtil.Field(root, "capture");
            RulesState rules = new RulesState();
            rules.HasCapture = true;
            JsonValue timer = JsonUtil.Field(capture, "timerTicksByTier");
            JsonValue radius = JsonUtil.Field(capture, "zoneRadiusCmByTier");
            for (int i = 0; i < AiHoldingClass.Count; i++)
            {
                string ruleId = AiHoldingClass.RuleId(i);
                rules.CaptureTicks[i] = RequireTierTicks(timer, ruleId);
                int cm = JsonUtil.Int32(JsonUtil.Field(radius, ruleId), "zoneRadiusCmByTier." + ruleId);
                if (cm < 1)
                {
                    throw new FormatException("zoneRadiusCmByTier rule '" + ruleId + "' must be >= 1.");
                }

                rules.ZoneRadiusCm[i] = cm;
            }

            rules.FortPerLevelPpt = RequireNamedPpt(capture, "fortificationTimerPerLevelPpt");
            rules.FortMaxPpt = RequireNamedPpt(capture, "fortificationTimerMaxPpt");
            string onContest = JsonUtil.String(JsonUtil.Field(capture, "onContest"), "onContest");
            if (onContest != "pause")
            {
                throw new FormatException("onContest must be 'pause'.");
            }

            bool reset = JsonUtil.Bool(JsonUtil.Field(capture, "resetOnContest"), "resetOnContest");
            if (reset)
            {
                throw new FormatException("resetOnContest must be false.");
            }

            string absent = JsonUtil.String(JsonUtil.Field(capture, "onCapturerAbsent"), "onCapturerAbsent");
            if (absent != "pause_then_decay")
            {
                throw new FormatException("onCapturerAbsent must be 'pause_then_decay'.");
            }

            rules.DecayStartsAfterTicks = JsonUtil.Int32(
                JsonUtil.Field(capture, "decayStartsAfterTicks"),
                "decayStartsAfterTicks");
            if (rules.DecayStartsAfterTicks < 0)
            {
                throw new FormatException("decayStartsAfterTicks must be >= 0.");
            }

            rules.DecayTicksPerTick = JsonUtil.Int32(
                JsonUtil.Field(capture, "decayTicksPerTick"),
                "decayTicksPerTick");
            if (rules.DecayTicksPerTick < 1)
            {
                throw new FormatException("decayTicksPerTick must be >= 1.");
            }

            JsonValue supply = JsonUtil.Field(root, "supply");
            rules.MaxDepotsPerFaction = JsonUtil.Int32(
                JsonUtil.Field(supply, "maxDepotsPerFaction"),
                RuleIdSupplyMaxDepots);
            if (rules.MaxDepotsPerFaction < 1)
            {
                throw new FormatException(RuleIdSupplyMaxDepots + " must be >= 1.");
            }

            rules.ConnectivityScratchSize = JsonUtil.Int32(
                JsonUtil.Field(supply, "connectivityScratchSize"),
                RuleIdSupplyScratch);
            if (rules.ConnectivityScratchSize < rules.MaxDepotsPerFaction)
            {
                throw new FormatException(
                    RuleIdSupplyScratch + " must be >= " + RuleIdSupplyMaxDepots + ".");
            }

            rules.DepotRangeCm = JsonUtil.Int32(
                JsonUtil.Field(supply, "depotRangeCm"),
                RuleIdDepotRangeCm);
            if (rules.DepotRangeCm < 1)
            {
                throw new FormatException(RuleIdDepotRangeCm + " must be >= 1.");
            }

            JsonValue ai = JsonUtil.Field(root, "ai");
            rules.MaxCandidateGoalsCeiling = JsonUtil.Int32(
                JsonUtil.Field(ai, "maxCandidateGoals"),
                RuleIdAiMaxCandidateGoals);
            if (rules.MaxCandidateGoalsCeiling < 1)
            {
                throw new FormatException(RuleIdAiMaxCandidateGoals + " must be >= 1.");
            }

            JsonValue difficulty = JsonUtil.Field(root, "difficulty");
            if (difficulty.Kind != JsonKind.Object)
            {
                throw new FormatException("difficulty must be an object.");
            }

            List<JsonField> difficultyFields = difficulty.ObjectValue;
            rules.DifficultyIds = new string[difficultyFields.Count];
            rules.IncomeMultiplierPpt = new int[difficultyFields.Count];
            for (int i = 0; i < difficultyFields.Count; i++)
            {
                string id = difficultyFields[i].Name;
                JsonValue row = difficultyFields[i].Value;
                string key = "difficulty." + id + ".incomeMultiplierPpt";
                if (!JsonUtil.TryField(row, "incomeMultiplierPpt", out JsonValue mul))
                {
                    throw new FormatException(key);
                }

                int ppt = JsonUtil.Int32(mul, key);
                if (ppt < 1)
                {
                    throw new FormatException(key + " must be >= 1.");
                }

                rules.DifficultyIds[i] = id;
                rules.IncomeMultiplierPpt[i] = ppt;
            }

            JsonValue tech = JsonUtil.Field(root, "tech");
            JsonValue tier2 = JsonUtil.Field(tech, "tier2");
            rules.Tier2ResearchTicks = JsonUtil.Int32(
                JsonUtil.Field(tier2, "researchTicks"),
                RuleIdTier2ResearchTicks);
            if (rules.Tier2ResearchTicks < 0)
            {
                throw new FormatException(RuleIdTier2ResearchTicks + " must be >= 0.");
            }

            rules.HasLimits = true;
            return rules;
        }

        internal static void ParseEconomy(string economyJson, AiPersonalityProfile[] personalities, RulesState rules)
        {
            if (economyJson == null)
            {
                throw new ArgumentNullException(nameof(economyJson));
            }

            JsonValue root = IntegerJsonParser.Parse(economyJson);
            if (JsonUtil.Int32(JsonUtil.Field(root, "schemaVersion"), "schemaVersion") != AiConstants.BalanceSchemaVersion)
            {
                throw new FormatException("Unsupported economy.json schema.");
            }

            JsonUtil.Int32(JsonUtil.Field(root, "dataVersion"), "dataVersion");
            JsonValue tiers = JsonUtil.Field(root, "holdingTiers");
            if (tiers.Kind != JsonKind.Array)
            {
                throw new FormatException("holdingTiers must be an array.");
            }

            bool[] seen = new bool[AiHoldingClass.Count];
            for (int i = 0; i < tiers.ArrayValue.Count; i++)
            {
                JsonValue row = tiers.ArrayValue[i];
                string id = JsonUtil.String(JsonUtil.Field(row, "id"), "id");
                int index;
                try
                {
                    index = AiHoldingClass.IndexOfRuleId(id);
                }
                catch (FormatException)
                {
                    continue;
                }

                JsonValue income = JsonUtil.Field(row, "incomePerMin");
                int manpower = JsonUtil.Int32(JsonUtil.Field(income, "manpower"), "holdingTiers." + id + ".incomePerMin.manpower");
                if (manpower < 0)
                {
                    throw new FormatException("holdingTiers." + id + ".incomePerMin.manpower must be >= 0.");
                }

                rules.IncomePerMin[index] = manpower;
                seen[index] = true;
            }

            for (int i = 0; i < seen.Length; i++)
            {
                if (!seen[i])
                {
                    throw new FormatException(
                        "Missing holdingTiers id '" + AiHoldingClass.RuleId(i) + "'.");
                }
            }

            JsonValue accrual = JsonUtil.Field(root, "incomeAccrual");
            rules.AccrualIntervalTicks = JsonUtil.Int32(
                JsonUtil.Field(accrual, "intervalTicks"),
                "incomeAccrual.intervalTicks");
            rules.AccrualIntervalsPerMinute = JsonUtil.Int32(
                JsonUtil.Field(accrual, "intervalsPerMinute"),
                "incomeAccrual.intervalsPerMinute");
            if (rules.AccrualIntervalTicks < 1)
            {
                throw new FormatException("incomeAccrual.intervalTicks must be >= 1.");
            }

            if (rules.AccrualIntervalsPerMinute < 1)
            {
                throw new FormatException("incomeAccrual.intervalsPerMinute must be >= 1.");
            }

            JsonValue hq = JsonUtil.Field(root, "hqIncomePerMinByFaction");
            JsonValue mods = JsonUtil.Field(root, "factionIncomeModifiersPpt");
            string[] ids = new string[personalities.Length];
            int[] hqManpower = new int[personalities.Length];
            int[] modifier = new int[personalities.Length];
            for (int i = 0; i < personalities.Length; i++)
            {
                string key = personalities[i].EconomyFactionId;
                ids[i] = key;
                JsonValue hqRow;
                if (!JsonUtil.TryField(hq, key, out hqRow))
                {
                    throw new FormatException("Missing hqIncomePerMinByFaction key '" + key + "'.");
                }

                hqManpower[i] = JsonUtil.Int32(
                    JsonUtil.Field(hqRow, "manpower"),
                    "hqIncomePerMinByFaction." + key + ".manpower");
                if (hqManpower[i] < 0)
                {
                    throw new FormatException("hqIncomePerMinByFaction." + key + ".manpower must be >= 0.");
                }

                JsonValue modRow;
                if (!JsonUtil.TryField(mods, key, out modRow))
                {
                    throw new FormatException("Missing factionIncomeModifiersPpt key '" + key + "'.");
                }

                modifier[i] = RequireNamedPpt(modRow, "manpower");
            }

            rules.HasEconomy = true;
            rules.EconomyFactionIds = ids;
            rules.HqManpowerPerMin = hqManpower;
            rules.ManpowerModifierPpt = modifier;
            rules.ContestedHoldingPaysIncome = JsonUtil.Bool(
                JsonUtil.Field(root, RuleIdContestedHoldingPaysIncome),
                RuleIdContestedHoldingPaysIncome);
        }

        private static int RequireNamedPpt(JsonValue row, string key)
        {
            int value = JsonUtil.Int32(JsonUtil.Field(row, key), key);
            if (value < 0)
            {
                throw new FormatException(key + " must be >= 0.");
            }

            return value;
        }

        public AiPersonalityProfile GetPersonality(string id)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            for (int i = 0; i < _personalities.Length; i++)
            {
                if (_personalities[i].Id == id)
                {
                    return _personalities[i];
                }
            }

            throw new KeyNotFoundException("Unknown personality '" + id + "'.");
        }

        /// <summary>
        /// MVP generic-AI fallback: unknown or empty ids use the settler
        /// profile. Seat <c>aiPersonality</c> strings must not fail the load.
        /// </summary>
        public AiPersonalityProfile GetPersonalityOrGeneric(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < _personalities.Length; i++)
                {
                    if (_personalities[i].Id == id)
                    {
                        return _personalities[i];
                    }
                }
            }

            return GetPersonality(AiGenericPersonality.GenericProfileId);
        }

        public AiDifficultyProfile GetDifficulty(string id)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            for (int i = 0; i < _difficulties.Length; i++)
            {
                if (_difficulties[i].Id == id)
                {
                    return _difficulties[i];
                }
            }

            throw new KeyNotFoundException("Unknown difficulty '" + id + "'.");
        }

        public bool TryGetBoundPersonalityId(int factionId, out string personalityId)
        {
            for (int i = 0; i < _bindingFactionIds.Length; i++)
            {
                if (_bindingFactionIds[i] == factionId)
                {
                    personalityId = _bindingPersonalityIds[i];
                    return true;
                }
            }

            personalityId = string.Empty;
            return false;
        }

        public string RequireBoundPersonalityId(int factionId)
        {
            if (TryGetBoundPersonalityId(factionId, out string personalityId))
            {
                return personalityId;
            }

            throw new KeyNotFoundException("No personality binding for faction " + factionId + ".");
        }

        private static void RequireDataVersion(JsonValue root, string fileName)
        {
            int dataVersion = JsonUtil.Int32(JsonUtil.Field(root, "dataVersion"), "dataVersion");
            if (dataVersion != AiConstants.DataVersion)
            {
                throw new FormatException(fileName + " dataVersion is required and must be " + AiConstants.DataVersion + ".");
            }
        }

        private static int RequireTierTicks(JsonValue table, string ruleId)
        {
            if (!JsonUtil.TryField(table, ruleId, out JsonValue value))
            {
                throw new FormatException("Missing capture.timerTicksByTier rule '" + ruleId + "'.");
            }

            if (value.Kind != JsonKind.Int)
            {
                throw new FormatException("capture.timerTicksByTier rule '" + ruleId + "' must be an integer.");
            }

            int ticks = JsonUtil.Int32(value, "capture.timerTicksByTier." + ruleId);
            if (ticks < 1)
            {
                throw new FormatException("capture.timerTicksByTier rule '" + ruleId + "' must be >= 1.");
            }

            return ticks;
        }

        private static AiPersonalityProfile ParsePersonality(JsonValue row)
        {
            string id = JsonUtil.String(JsonUtil.Field(row, "id"), "id");
            bool stub = JsonUtil.Bool(JsonUtil.Field(row, "stub"), "stub");
            int defaultPosture = JsonUtil.Int32(JsonUtil.Field(row, "defaultPosture"), "defaultPosture");
            if (defaultPosture < 0 || defaultPosture > 3)
            {
                throw new FormatException("defaultPosture must be 0..3.");
            }

            int minGarrison = JsonUtil.Int32(JsonUtil.Field(row, "minGarrisonPerTown"), "minGarrisonPerTown");
            if (minGarrison < 0)
            {
                throw new FormatException("minGarrisonPerTown must be >= 0.");
            }

            return new AiPersonalityProfile(
                id,
                stub,
                JsonUtil.Int32(JsonUtil.Field(row, "weightTownValue"), "weightTownValue"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightAnchorSet"), "weightAnchorSet"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightThreat"), "weightThreat"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightForceRatio"), "weightForceRatio"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightEconomy"), "weightEconomy"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightChokepoint"), "weightChokepoint"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightSupplyCut"), "weightSupplyCut"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightSupplyGuard"), "weightSupplyGuard"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightDistance"), "weightDistance"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightRace"), "weightRace"),
                JsonUtil.Int32(JsonUtil.Field(row, "weightVisionGround"), "weightVisionGround"),
                JsonUtil.Int32(JsonUtil.Field(row, "aggressionBias"), "aggressionBias"),
                JsonUtil.Int32(JsonUtil.Field(row, "turtleBias"), "turtleBias"),
                JsonUtil.Int32(JsonUtil.Field(row, "raidBias"), "raidBias"),
                JsonUtil.Int32(JsonUtil.Field(row, "scoutBias"), "scoutBias"),
                RequirePpt(row, "reservePpt"),
                RequirePpt(row, "siegeInvestRatioPpt"),
                RequirePpt(row, "siegeAssaultRatioPpt"),
                RequirePpt(row, "retreatThresholdPpt"),
                RequirePpt(row, "postureSwitchMarginPpt"),
                RequirePpt(row, "nonSeatBonusPpt"),
                RequireNonNegative(row, "buildUpScoreBonus"),
                RequireNonNegative(row, "depotBaseScore"),
                RequireNonNegative(row, "awayDepotScoreBonus"),
                RequirePpt(row, RuleIdPrizeTargetBonusPpt),
                RequireLineUnitsPerAntiArmour(row),
                RequireEconomyFactionId(row),
                minGarrison,
                JsonUtil.Int32(JsonUtil.Field(row, "taskForceTargetSize"), "taskForceTargetSize"),
                (AiPosture)defaultPosture);
        }

        private static AiDifficultyProfile ParseDifficulty(JsonValue row)
        {
            string fogCheat = JsonUtil.String(JsonUtil.Field(row, "fogCheat"), "fogCheat");
            if (fogCheat != AiConstants.FogCheatNone)
            {
                throw new FormatException("fogCheat must be 'none' at every difficulty.");
            }

            int cadence = JsonUtil.Int32(JsonUtil.Field(row, "strategicCadenceTicks"), "strategicCadenceTicks");
            if (cadence < 1)
            {
                throw new FormatException("strategicCadenceTicks must be >= 1.");
            }

            int reaction = JsonUtil.Int32(JsonUtil.Field(row, "reactionExtraTicks"), "reactionExtraTicks");
            if (reaction < 0)
            {
                throw new FormatException("reactionExtraTicks must be >= 0.");
            }

            int maxActiveOrders = JsonUtil.Int32(JsonUtil.Field(row, "maxActiveOrders"), "maxActiveOrders");
            if (maxActiveOrders < 1)
            {
                throw new FormatException("maxActiveOrders must be >= 1.");
            }

            int maxCandidateGoals = JsonUtil.Int32(JsonUtil.Field(row, "maxCandidateGoals"), "maxCandidateGoals");
            if (maxCandidateGoals < 1)
            {
                throw new FormatException("maxCandidateGoals must be >= 1.");
            }

            string id = JsonUtil.String(JsonUtil.Field(row, "id"), "id");
            int attackRatio = ParseAttackStrengthRatioPpt(row, id);
            return new AiDifficultyProfile(
                id,
                reaction,
                cadence,
                RequirePpt(row, "aggressionMulPpt"),
                JsonUtil.Int32(JsonUtil.Field(row, "scoutCadenceTicks"), "scoutCadenceTicks"),
                fogCheat,
                RequirePpt(row, "memoryDecayPerCadencePpt"),
                RequirePpt(row, "buildSpeedBonusPpt"),
                JsonUtil.Int32(JsonUtil.Field(row, "visionRadiusBonusCm"), "visionRadiusBonusCm"),
                JsonUtil.Int32(JsonUtil.Field(row, "microQuality"), "microQuality"),
                RequirePpt(row, "mistakeRatePpt"),
                RequirePpt(row, "retreatThresholdMulPpt"),
                maxActiveOrders,
                maxCandidateGoals,
                JsonUtil.Int32(JsonUtil.Field(row, "manpowerReserveSeconds"), "manpowerReserveSeconds"),
                RequireManpowerCost(row),
                attackRatio);
        }

        private static int ParseAttackStrengthRatioPpt(JsonValue row, string difficultyId)
        {
            if (!JsonUtil.TryField(row, RuleIdAttackStrengthRatioPpt, out JsonValue field))
            {
                throw new FormatException(RuleIdAttackStrengthRatioPpt);
            }

            int value = JsonUtil.Int32(field, RuleIdAttackStrengthRatioPpt);
            if (value < 1)
            {
                throw new FormatException(RuleIdAttackStrengthRatioPpt);
            }

            return value;
        }

        private static int RequireManpowerCost(JsonValue row)
        {
            int value = JsonUtil.Int32(JsonUtil.Field(row, "battalionManpowerCost"), "battalionManpowerCost");
            if (value < 1)
            {
                throw new FormatException("battalionManpowerCost must be >= 1.");
            }

            return value;
        }

        private static string RequireEconomyFactionId(JsonValue row)
        {
            string id = JsonUtil.String(JsonUtil.Field(row, "economyFactionId"), "economyFactionId");
            if (id.Length < 1)
            {
                throw new FormatException("economyFactionId must be a non-empty string.");
            }

            return id;
        }

        private static int RequireNonNegative(JsonValue row, string key)
        {
            int value = JsonUtil.Int32(JsonUtil.Field(row, key), key);
            if (value < 0)
            {
                throw new FormatException(key + " must be >= 0.");
            }

            return value;
        }

        private static int RequireLineUnitsPerAntiArmour(JsonValue row)
        {
            int value = JsonUtil.Int32(
                JsonUtil.Field(row, RuleIdLineUnitsPerAntiArmour),
                RuleIdLineUnitsPerAntiArmour);
            if (value < 1)
            {
                throw new FormatException(RuleIdLineUnitsPerAntiArmour + " must be >= 1.");
            }

            return value;
        }

        private static int RequirePpt(JsonValue row, string key)
        {
            int value = JsonUtil.Int32(JsonUtil.Field(row, key), key);
            if (value < 0)
            {
                throw new FormatException(key + " must be >= 0.");
            }

            return value;
        }

        private void RequireCandidateGoalsWithinCeiling(RulesState rules)
        {
            if (rules == null || !rules.HasLimits)
            {
                return;
            }

            for (int i = 0; i < _difficulties.Length; i++)
            {
                if (_difficulties[i].MaxCandidateGoals > rules.MaxCandidateGoalsCeiling)
                {
                    throw new FormatException(RuleIdAiMaxCandidateGoals);
                }
            }
        }

        private void RequireDifficultiesHaveIncomeMultiplier(RulesState rules)
        {
            if (rules == null || !rules.HasLimits)
            {
                return;
            }

            for (int i = 0; i < _difficulties.Length; i++)
            {
                string id = _difficulties[i].Id;
                bool found = false;
                for (int d = 0; d < rules.DifficultyIds.Length; d++)
                {
                    if (rules.DifficultyIds[d] == id)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new FormatException("difficulty." + id + ".incomeMultiplierPpt");
                }
            }
        }

        private static void RequireUniquePersonalityIds(AiPersonalityProfile[] rows)
        {
            for (int i = 1; i < rows.Length; i++)
            {
                if (rows[i].Id == rows[i - 1].Id)
                {
                    throw new FormatException("Duplicate personality id '" + rows[i].Id + "'.");
                }
            }
        }

        private static void RequireUniqueDifficultyIds(AiDifficultyProfile[] rows)
        {
            for (int i = 1; i < rows.Length; i++)
            {
                if (rows[i].Id == rows[i - 1].Id)
                {
                    throw new FormatException("Duplicate difficulty id '" + rows[i].Id + "'.");
                }
            }
        }

        private static void RequireUniqueBindingFactionIds(int[] factionIds)
        {
            for (int i = 1; i < factionIds.Length; i++)
            {
                if (factionIds[i] == factionIds[i - 1])
                {
                    throw new FormatException("Duplicate factionBindings factionId " + factionIds[i] + ".");
                }
            }
        }

        private static void RequireBindingsReferencePersonalities(string[] personalityIds, AiPersonalityProfile[] personalities)
        {
            for (int i = 0; i < personalityIds.Length; i++)
            {
                bool found = false;
                for (int p = 0; p < personalities.Length; p++)
                {
                    if (personalities[p].Id == personalityIds[i])
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    throw new FormatException("factionBindings personalityId '" + personalityIds[i] + "' is unknown.");
                }
            }
        }

        private static void SortPersonalities(AiPersonalityProfile[] rows)
        {
            for (int i = 1; i < rows.Length; i++)
            {
                AiPersonalityProfile row = rows[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(rows[j].Id, row.Id) > 0)
                {
                    rows[j + 1] = rows[j];
                    j--;
                }

                rows[j + 1] = row;
            }
        }

        private static void SortDifficulties(AiDifficultyProfile[] rows)
        {
            for (int i = 1; i < rows.Length; i++)
            {
                AiDifficultyProfile row = rows[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(rows[j].Id, row.Id) > 0)
                {
                    rows[j + 1] = rows[j];
                    j--;
                }

                rows[j + 1] = row;
            }
        }

        private static void SortBindings(int[] factionIds, string[] personalityIds)
        {
            for (int i = 1; i < factionIds.Length; i++)
            {
                int factionId = factionIds[i];
                string personalityId = personalityIds[i];
                int j = i - 1;
                while (j >= 0 && factionIds[j] > factionId)
                {
                    factionIds[j + 1] = factionIds[j];
                    personalityIds[j + 1] = personalityIds[j];
                    j--;
                }

                factionIds[j + 1] = factionId;
                personalityIds[j + 1] = personalityId;
            }
        }

        internal sealed class RulesState
        {
            public bool HasCapture;
            public readonly int[] CaptureTicks = new int[AiHoldingClass.Count];
            public readonly int[] ZoneRadiusCm = new int[AiHoldingClass.Count];
            public int FortPerLevelPpt;
            public int FortMaxPpt;
            public int DecayStartsAfterTicks;
            public int DecayTicksPerTick;
            public bool HasEconomy;
            public readonly int[] IncomePerMin = new int[AiHoldingClass.Count];
            public int AccrualIntervalTicks;
            public int AccrualIntervalsPerMinute;
            public string[] EconomyFactionIds = Array.Empty<string>();
            public int[] HqManpowerPerMin = Array.Empty<int>();
            public int[] ManpowerModifierPpt = Array.Empty<int>();
            public bool ContestedHoldingPaysIncome;
            public bool HasLimits;
            public int MaxDepotsPerFaction;
            public int ConnectivityScratchSize;
            public int DepotRangeCm;
            public int MaxCandidateGoalsCeiling;
            public int Tier2ResearchTicks;
            public string[] DifficultyIds = Array.Empty<string>();
            public int[] IncomeMultiplierPpt = Array.Empty<int>();

            public RulesState Clone()
            {
                RulesState copy = new RulesState();
                copy.HasCapture = HasCapture;
                Array.Copy(CaptureTicks, copy.CaptureTicks, CaptureTicks.Length);
                Array.Copy(ZoneRadiusCm, copy.ZoneRadiusCm, ZoneRadiusCm.Length);
                copy.FortPerLevelPpt = FortPerLevelPpt;
                copy.FortMaxPpt = FortMaxPpt;
                copy.DecayStartsAfterTicks = DecayStartsAfterTicks;
                copy.DecayTicksPerTick = DecayTicksPerTick;
                copy.HasEconomy = HasEconomy;
                Array.Copy(IncomePerMin, copy.IncomePerMin, IncomePerMin.Length);
                copy.AccrualIntervalTicks = AccrualIntervalTicks;
                copy.AccrualIntervalsPerMinute = AccrualIntervalsPerMinute;
                copy.EconomyFactionIds = (string[])EconomyFactionIds.Clone();
                copy.HqManpowerPerMin = (int[])HqManpowerPerMin.Clone();
                copy.ManpowerModifierPpt = (int[])ManpowerModifierPpt.Clone();
                copy.ContestedHoldingPaysIncome = ContestedHoldingPaysIncome;
                copy.HasLimits = HasLimits;
                copy.MaxDepotsPerFaction = MaxDepotsPerFaction;
                copy.ConnectivityScratchSize = ConnectivityScratchSize;
                copy.DepotRangeCm = DepotRangeCm;
                copy.MaxCandidateGoalsCeiling = MaxCandidateGoalsCeiling;
                copy.Tier2ResearchTicks = Tier2ResearchTicks;
                copy.DifficultyIds = (string[])DifficultyIds.Clone();
                copy.IncomeMultiplierPpt = (int[])IncomeMultiplierPpt.Clone();
                return copy;
            }
        }
    }
}
