using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;

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
        public readonly int EconomyBonusPpt;
        public readonly int BuildSpeedBonusPpt;
        public readonly int VisionRadiusBonusCm;
        public readonly int MicroQuality;
        public readonly int MistakeRatePpt;
        public readonly int RetreatThresholdMulPpt;
        public readonly int MaxActiveOrders;
        public readonly int MaxCandidateGoals;
        public readonly int ManpowerReserveSeconds;

        internal AiDifficultyProfile(
            string id,
            int reactionExtraTicks,
            int strategicCadenceTicks,
            int aggressionMulPpt,
            int scoutCadenceTicks,
            string fogCheat,
            int memoryDecayPerCadencePpt,
            int economyBonusPpt,
            int buildSpeedBonusPpt,
            int visionRadiusBonusCm,
            int microQuality,
            int mistakeRatePpt,
            int retreatThresholdMulPpt,
            int maxActiveOrders,
            int maxCandidateGoals,
            int manpowerReserveSeconds)
        {
            Id = id;
            ReactionExtraTicks = reactionExtraTicks;
            StrategicCadenceTicks = strategicCadenceTicks;
            AggressionMulPpt = aggressionMulPpt;
            ScoutCadenceTicks = scoutCadenceTicks;
            FogCheat = fogCheat;
            MemoryDecayPerCadencePpt = memoryDecayPerCadencePpt;
            EconomyBonusPpt = economyBonusPpt;
            BuildSpeedBonusPpt = buildSpeedBonusPpt;
            VisionRadiusBonusCm = visionRadiusBonusCm;
            MicroQuality = microQuality;
            MistakeRatePpt = mistakeRatePpt;
            RetreatThresholdMulPpt = retreatThresholdMulPpt;
            MaxActiveOrders = maxActiveOrders;
            MaxCandidateGoals = maxCandidateGoals;
            ManpowerReserveSeconds = manpowerReserveSeconds;
        }
    }

    /// <summary>
    /// Strict integer JSON for personalities and difficulty. File hashes are
    /// match config (xxHash64 over UTF-8 bytes), not rebuilt per tick.
    /// Both files carry the same integer pair as the designer balance JSON:
    /// <c>schemaVersion</c> next to <c>dataVersion</c> (AI content is
    /// dataVersion 1; designer pack files are currently dataVersion 6).
    /// Parse requires <c>dataVersion</c> as an int32 — missing or non-integer
    /// values are rejected. <see cref="FromDirectory"/> also loads
    /// <c>rules.json</c> <c>capture.timerTicksByTier</c> (hamlet / village /
    /// town / city). The rest of <c>rules.json</c> is not read.
    /// </summary>
    public sealed class AiBalanceTables
    {
        public const string CaptureRuleHamlet = "hamlet";
        public const string CaptureRuleVillage = "village";
        public const string CaptureRuleTown = "town";
        public const string CaptureRuleCity = "city";

        private readonly AiPersonalityProfile[] _personalities;
        private readonly AiDifficultyProfile[] _difficulties;
        private readonly int[] _bindingFactionIds;
        private readonly string[] _bindingPersonalityIds;

        private readonly ulong _balanceKey;
        private readonly bool _hasCaptureTable;
        private readonly int _captureHamletTicks;
        private readonly int _captureVillageTicks;
        private readonly int _captureTownTicks;
        private readonly int _captureCityTicks;

        private AiBalanceTables(
            ulong balanceKey,
            ulong personalityJsonHash,
            ulong difficultyJsonHash,
            AiPersonalityProfile[] personalities,
            AiDifficultyProfile[] difficulties,
            int[] bindingFactionIds,
            string[] bindingPersonalityIds,
            bool hasCaptureTable,
            int captureHamletTicks,
            int captureVillageTicks,
            int captureTownTicks,
            int captureCityTicks)
        {
            _balanceKey = balanceKey;
            PersonalityJsonHash = personalityJsonHash;
            DifficultyJsonHash = difficultyJsonHash;
            _personalities = personalities;
            _difficulties = difficulties;
            _bindingFactionIds = bindingFactionIds;
            _bindingPersonalityIds = bindingPersonalityIds;
            _hasCaptureTable = hasCaptureTable;
            _captureHamletTicks = captureHamletTicks;
            _captureVillageTicks = captureVillageTicks;
            _captureTownTicks = captureTownTicks;
            _captureCityTicks = captureCityTicks;
        }

        public ulong BalanceKey
        {
            get { return _balanceKey; }
        }

        public ulong PersonalityJsonHash { get; }

        public ulong DifficultyJsonHash { get; }

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
            get { return _hasCaptureTable; }
        }

        public static AiBalanceTables FromDirectory(string balanceDirectory)
        {
            if (balanceDirectory == null)
            {
                throw new ArgumentNullException(nameof(balanceDirectory));
            }

            string personalitiesPath = Path.Combine(balanceDirectory, "ai", "ai_personalities.json");
            string difficultyPath = Path.Combine(balanceDirectory, "ai", "ai_difficulty.json");
            string rulesPath = Path.Combine(balanceDirectory, "rules.json");
            AiBalanceTables tables = Parse(
                File.ReadAllText(personalitiesPath),
                File.ReadAllText(difficultyPath));
            ParseCaptureRules(
                File.ReadAllText(rulesPath),
                out int hamletTicks,
                out int villageTicks,
                out int townTicks,
                out int cityTicks);
            return tables
                .WithCapture(hamletTicks, villageTicks, townTicks, cityTicks)
                .WithBalanceKey(global::Wasteland.Sim.Movement.BalanceKey.For(balanceDirectory));
        }

        public AiBalanceTables WithBalanceKey(ulong balanceKey)
        {
            return Clone(
                balanceKey,
                _hasCaptureTable,
                _captureHamletTicks,
                _captureVillageTicks,
                _captureTownTicks,
                _captureCityTicks);
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

            return Clone(
                _balanceKey,
                true,
                hamletTicks,
                villageTicks,
                townTicks,
                cityTicks);
        }

        public int CaptureTicksForClass(int populationClass)
        {
            if (!_hasCaptureTable)
            {
                throw new InvalidOperationException("Balance tables have no capture.timerTicksByTier.");
            }

            if (populationClass == 0)
            {
                return _captureHamletTicks;
            }

            if (populationClass == 1)
            {
                return _captureVillageTicks;
            }

            if (populationClass == 2)
            {
                return _captureTownTicks;
            }

            if (populationClass == 3)
            {
                return _captureCityTicks;
            }

            throw new FormatException(
                "No capture.timerTicksByTier rule for populationClass " + populationClass + ".");
        }

        private AiBalanceTables Clone(
            ulong balanceKey,
            bool hasCaptureTable,
            int captureHamletTicks,
            int captureVillageTicks,
            int captureTownTicks,
            int captureCityTicks)
        {
            return new AiBalanceTables(
                balanceKey,
                PersonalityJsonHash,
                DifficultyJsonHash,
                _personalities,
                _difficulties,
                _bindingFactionIds,
                _bindingPersonalityIds,
                hasCaptureTable,
                captureHamletTicks,
                captureVillageTicks,
                captureTownTicks,
                captureCityTicks);
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

            return new AiBalanceTables(
                0UL,
                HashUtf8(personalitiesJson),
                HashUtf8(difficultyJson),
                personalities,
                difficulties,
                bindingFactionIds,
                bindingPersonalityIds,
                false,
                0,
                0,
                0,
                0);
        }

        public static void ParseCaptureRules(
            string rulesJson,
            out int hamletTicks,
            out int villageTicks,
            out int townTicks,
            out int cityTicks)
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
            JsonValue table = JsonUtil.Field(capture, "timerTicksByTier");
            hamletTicks = RequireTierTicks(table, CaptureRuleHamlet);
            villageTicks = RequireTierTicks(table, CaptureRuleVillage);
            townTicks = RequireTierTicks(table, CaptureRuleTown);
            cityTicks = RequireTierTicks(table, CaptureRuleCity);
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
                JsonUtil.Int32(JsonUtil.Field(row, "ReservePpt"), "ReservePpt"),
                JsonUtil.Int32(JsonUtil.Field(row, "SiegeInvestRatioPpt"), "SiegeInvestRatioPpt"),
                JsonUtil.Int32(JsonUtil.Field(row, "SiegeAssaultRatioPpt"), "SiegeAssaultRatioPpt"),
                JsonUtil.Int32(JsonUtil.Field(row, "RetreatThresholdPpt"), "RetreatThresholdPpt"),
                JsonUtil.Int32(JsonUtil.Field(row, "PostureSwitchMarginPpt"), "PostureSwitchMarginPpt"),
                minGarrison,
                JsonUtil.Int32(JsonUtil.Field(row, "taskForceTargetSize"), "taskForceTargetSize"),
                (AiPosture)defaultPosture);
        }

        private static AiDifficultyProfile ParseDifficulty(JsonValue row)
        {
            string fogCheat = JsonUtil.String(JsonUtil.Field(row, "FogCheat"), "FogCheat");
            if (fogCheat != AiConstants.FogCheatNone)
            {
                throw new FormatException("FogCheat must be 'none' at every difficulty.");
            }

            int cadence = JsonUtil.Int32(JsonUtil.Field(row, "StrategicCadenceTicks"), "StrategicCadenceTicks");
            if (cadence < 1)
            {
                throw new FormatException("StrategicCadenceTicks must be >= 1.");
            }

            int reaction = JsonUtil.Int32(JsonUtil.Field(row, "ReactionExtraTicks"), "ReactionExtraTicks");
            if (reaction < 0)
            {
                throw new FormatException("ReactionExtraTicks must be >= 0.");
            }

            int economyBonus = JsonUtil.Int32(JsonUtil.Field(row, "EconomyBonusPpt"), "EconomyBonusPpt");
            if (economyBonus < 0)
            {
                throw new FormatException("EconomyBonusPpt must be >= 0.");
            }

            int maxActiveOrders = JsonUtil.Int32(JsonUtil.Field(row, "MaxActiveOrders"), "MaxActiveOrders");
            if (maxActiveOrders < 1)
            {
                throw new FormatException("MaxActiveOrders must be >= 1.");
            }

            return new AiDifficultyProfile(
                JsonUtil.String(JsonUtil.Field(row, "id"), "id"),
                reaction,
                cadence,
                JsonUtil.Int32(JsonUtil.Field(row, "AggressionMulPpt"), "AggressionMulPpt"),
                JsonUtil.Int32(JsonUtil.Field(row, "ScoutCadenceTicks"), "ScoutCadenceTicks"),
                fogCheat,
                JsonUtil.Int32(JsonUtil.Field(row, "MemoryDecayPerCadencePpt"), "MemoryDecayPerCadencePpt"),
                economyBonus,
                JsonUtil.Int32(JsonUtil.Field(row, "BuildSpeedBonusPpt"), "BuildSpeedBonusPpt"),
                JsonUtil.Int32(JsonUtil.Field(row, "VisionRadiusBonusCm"), "VisionRadiusBonusCm"),
                JsonUtil.Int32(JsonUtil.Field(row, "MicroQuality"), "MicroQuality"),
                JsonUtil.Int32(JsonUtil.Field(row, "MistakeRatePpt"), "MistakeRatePpt"),
                JsonUtil.Int32(JsonUtil.Field(row, "RetreatThresholdMulPpt"), "RetreatThresholdMulPpt"),
                maxActiveOrders,
                JsonUtil.Int32(JsonUtil.Field(row, "MaxCandidateGoals"), "MaxCandidateGoals"),
                JsonUtil.Int32(JsonUtil.Field(row, "manpowerReserveSeconds"), "manpowerReserveSeconds"));
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
    }
}
