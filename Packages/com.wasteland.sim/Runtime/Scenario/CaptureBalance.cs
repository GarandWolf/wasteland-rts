using System;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Scenario
{
    /// <summary>
    /// Capture timers, zone radii, depot placement, and holding pop bonuses
    /// read from rules.json, economy.json, and buildings.json.
    /// Zone radius is indexed by map population class (0 hamlet through 3 city).
    /// Timer and pop-cap bonus are indexed by the sim population band.
    /// </summary>
    public sealed class CaptureBalance
    {
        public const int TierCount = 4;

        private static readonly string[] TierIds = { "hamlet", "village", "town", "city" };

        private readonly int[] _popMin;
        private readonly int[] _popMax;
        private readonly int[] _popBonus;
        private readonly int[] _timerTicks;
        private readonly int[] _zoneRadiusCm;

        internal CaptureBalance(
            int sceneryBelow,
            int[] popMin,
            int[] popMax,
            int[] popBonus,
            int[] timerTicks,
            int[] zoneRadiusCm,
            int fortificationPerLevelPpt,
            int fortificationMaxPpt,
            int decayStartsAfterTicks,
            int decayTicksPerTick,
            int hqRangeCm,
            int depotRangeCm,
            int depotMinSpacingCm,
            int maxDepotsPerNode,
            int maxDepotsPerFaction,
            int nodeControlRadiusCm,
            int depotManpower,
            int depotMateriel,
            int depotFuel,
            int depotBuildTicks)
        {
            SceneryPopulationBelow = sceneryBelow;
            _popMin = popMin;
            _popMax = popMax;
            _popBonus = popBonus;
            _timerTicks = timerTicks;
            _zoneRadiusCm = zoneRadiusCm;
            FortificationPerLevelPpt = fortificationPerLevelPpt;
            FortificationMaxPpt = fortificationMaxPpt;
            DecayStartsAfterTicks = decayStartsAfterTicks;
            DecayTicksPerTick = decayTicksPerTick;
            HqRangeCm = hqRangeCm;
            DepotRangeCm = depotRangeCm;
            DepotMinSpacingCm = depotMinSpacingCm;
            MaxDepotsPerNode = maxDepotsPerNode;
            MaxDepotsPerFaction = maxDepotsPerFaction;
            NodeControlRadiusCm = nodeControlRadiusCm;
            DepotManpower = depotManpower;
            DepotMateriel = depotMateriel;
            DepotFuel = depotFuel;
            DepotBuildTicks = depotBuildTicks;
        }

        public int SceneryPopulationBelow { get; }

        public int FortificationPerLevelPpt { get; }

        public int FortificationMaxPpt { get; }

        public int DecayStartsAfterTicks { get; }

        public int DecayTicksPerTick { get; }

        public int HqRangeCm { get; }

        public int DepotRangeCm { get; }

        public int DepotMinSpacingCm { get; }

        public int MaxDepotsPerNode { get; }

        public int MaxDepotsPerFaction { get; }

        public int NodeControlRadiusCm { get; }

        public int DepotManpower { get; }

        public int DepotMateriel { get; }

        public int DepotFuel { get; }

        public int DepotBuildTicks { get; }

        public string TierId(int tier)
        {
            if (tier < 0 || tier >= TierCount)
            {
                return string.Empty;
            }

            return TierIds[tier];
        }

        public int TierIndex(string tierId)
        {
            for (int i = 0; i < TierIds.Length; i++)
            {
                if (TierIds[i] == tierId)
                {
                    return i;
                }
            }

            return -1;
        }

        public int PopCapBonus(int tier)
        {
            return _popBonus[tier];
        }

        /// <summary>Sim band for a population, or -1 when it is scenery or unmatched.</summary>
        public int TierForPopulation(int population)
        {
            if (population < SceneryPopulationBelow)
            {
                return -1;
            }

            for (int i = 0; i < TierCount; i++)
            {
                if (population >= _popMin[i] && population <= _popMax[i])
                {
                    return i;
                }
            }

            return -1;
        }

        public int ZoneRadiusCm(int populationClass)
        {
            if (populationClass < 0 || populationClass >= _zoneRadiusCm.Length)
            {
                return 0;
            }

            return _zoneRadiusCm[populationClass];
        }

        /// <summary>
        /// floor(baseTicks * (1000 + min(level * perLevel, max)) / 1000).
        /// </summary>
        public int CaptureTicks(int tier, int fortificationLevel)
        {
            int bonus = fortificationLevel * FortificationPerLevelPpt;
            if (bonus > FortificationMaxPpt)
            {
                bonus = FortificationMaxPpt;
            }

            if (bonus < 0)
            {
                bonus = 0;
            }

            return (int)(((long)_timerTicks[tier] * (1000 + bonus)) / 1000);
        }

        internal static CaptureBalance Read(JsonValue rulesRoot, JsonValue economyRoot, JsonValue buildingsRoot)
        {
            int scenery = ReadScenery(economyRoot);
            int[] popMin = new int[TierCount];
            int[] popMax = new int[TierCount];
            int[] popBonus = new int[TierCount];
            ReadHoldingBands(economyRoot, popMin, popMax, popBonus);
            int[] timers = new int[TierCount];
            int[] radii = new int[TierCount];
            int fortPer;
            int fortMax;
            int decayStarts;
            int decayPer;
            ReadCapture(rulesRoot, timers, radii, out fortPer, out fortMax, out decayStarts, out decayPer);
            int hqRange;
            int depotRange;
            int spacing;
            int perNode;
            int perFaction;
            int nodeControl;
            ReadSupply(rulesRoot, out hqRange, out depotRange, out spacing, out perNode, out perFaction, out nodeControl);
            int manpower;
            int materiel;
            int fuel;
            int buildTicks;
            ReadDepotCost(buildingsRoot, out manpower, out materiel, out fuel, out buildTicks);
            return new CaptureBalance(
                scenery,
                popMin,
                popMax,
                popBonus,
                timers,
                radii,
                fortPer,
                fortMax,
                decayStarts,
                decayPer,
                hqRange,
                depotRange,
                spacing,
                perNode,
                perFaction,
                nodeControl,
                manpower,
                materiel,
                fuel,
                buildTicks);
        }

        private static int ReadScenery(JsonValue economyRoot)
        {
            JsonValue scenery = Child(economyRoot, "scenery", "economy.json $.scenery");
            return IntAt(Child(scenery, "populationBelow", "economy.json $.scenery.populationBelow"), "economy.json $.scenery.populationBelow", 0);
        }

        private static void ReadHoldingBands(JsonValue economyRoot, int[] popMin, int[] popMax, int[] popBonus)
        {
            JsonValue tiers = Child(economyRoot, "holdingTiers", "economy.json $.holdingTiers");
            if (tiers.Kind != JsonKind.Array)
            {
                throw ScenarioErrors.At("economy.json $.holdingTiers", "expected an array.");
            }

            bool[] seen = new bool[TierCount];
            for (int i = 0; i < tiers.ArrayValue.Count; i++)
            {
                JsonValue row = tiers.ArrayValue[i];
                string path = "economy.json $.holdingTiers[" + i + "]";
                string id = TextAt(Child(row, "id", path + ".id"), path + ".id");
                int index = -1;
                for (int t = 0; t < TierIds.Length; t++)
                {
                    if (TierIds[t] == id)
                    {
                        index = t;
                        break;
                    }
                }

                if (index < 0)
                {
                    continue;
                }

                seen[index] = true;
                popMin[index] = IntAt(Child(row, "populationMin", path + ".populationMin"), path + ".populationMin", 0);
                popMax[index] = IntAt(Child(row, "populationMax", path + ".populationMax"), path + ".populationMax", 0);
                popBonus[index] = IntAt(Child(row, "popCapBonus", path + ".popCapBonus"), path + ".popCapBonus", 0);
            }

            for (int i = 0; i < seen.Length; i++)
            {
                if (!seen[i])
                {
                    throw ScenarioErrors.At("economy.json $.holdingTiers", "missing tier '" + TierIds[i] + "'.");
                }
            }
        }

        private static void ReadCapture(
            JsonValue rulesRoot,
            int[] timers,
            int[] radii,
            out int fortPer,
            out int fortMax,
            out int decayStarts,
            out int decayPer)
        {
            JsonValue capture = Child(rulesRoot, "capture", "rules.json $.capture");
            JsonValue timerObj = Child(capture, "timerTicksByTier", "rules.json $.capture.timerTicksByTier");
            JsonValue radiusObj = Child(capture, "zoneRadiusCmByTier", "rules.json $.capture.zoneRadiusCmByTier");
            for (int i = 0; i < TierIds.Length; i++)
            {
                string id = TierIds[i];
                timers[i] = IntAt(Child(timerObj, id, "rules.json $.capture.timerTicksByTier." + id), "rules.json $.capture.timerTicksByTier." + id, 1);
                radii[i] = IntAt(Child(radiusObj, id, "rules.json $.capture.zoneRadiusCmByTier." + id), "rules.json $.capture.zoneRadiusCmByTier." + id, 0);
            }

            fortPer = IntAt(Child(capture, "fortificationTimerPerLevelPpt", "rules.json $.capture.fortificationTimerPerLevelPpt"), "rules.json $.capture.fortificationTimerPerLevelPpt", 0);
            fortMax = IntAt(Child(capture, "fortificationTimerMaxPpt", "rules.json $.capture.fortificationTimerMaxPpt"), "rules.json $.capture.fortificationTimerMaxPpt", 0);
            decayStarts = IntAt(Child(capture, "decayStartsAfterTicks", "rules.json $.capture.decayStartsAfterTicks"), "rules.json $.capture.decayStartsAfterTicks", 0);
            decayPer = IntAt(Child(capture, "decayTicksPerTick", "rules.json $.capture.decayTicksPerTick"), "rules.json $.capture.decayTicksPerTick", 0);
        }

        private static void ReadSupply(
            JsonValue rulesRoot,
            out int hqRange,
            out int depotRange,
            out int spacing,
            out int perNode,
            out int perFaction,
            out int nodeControl)
        {
            JsonValue supply = Child(rulesRoot, "supply", "rules.json $.supply");
            hqRange = IntAt(Child(supply, "hqRangeCm", "rules.json $.supply.hqRangeCm"), "rules.json $.supply.hqRangeCm", 0);
            depotRange = IntAt(Child(supply, "depotRangeCm", "rules.json $.supply.depotRangeCm"), "rules.json $.supply.depotRangeCm", 0);
            spacing = IntAt(Child(supply, "depotMinSpacingCm", "rules.json $.supply.depotMinSpacingCm"), "rules.json $.supply.depotMinSpacingCm", 0);
            perNode = IntAt(Child(supply, "maxDepotsPerNode", "rules.json $.supply.maxDepotsPerNode"), "rules.json $.supply.maxDepotsPerNode", 0);
            perFaction = IntAt(Child(supply, "maxDepotsPerFaction", "rules.json $.supply.maxDepotsPerFaction"), "rules.json $.supply.maxDepotsPerFaction", 0);
            nodeControl = IntAt(Child(supply, "nodeControlRadiusCm", "rules.json $.supply.nodeControlRadiusCm"), "rules.json $.supply.nodeControlRadiusCm", 0);
        }

        private static void ReadDepotCost(JsonValue buildingsRoot, out int manpower, out int materiel, out int fuel, out int buildTicks)
        {
            JsonValue buildings = Child(buildingsRoot, "buildings", "buildings.json $.buildings");
            JsonValue row;
            if (!TryField(buildings, "supply_depot", out row))
            {
                throw ScenarioErrors.At("buildings.json $.buildings.supply_depot", "missing supply_depot.");
            }

            string path = "buildings.json $.buildings.supply_depot";
            JsonValue cost = Child(row, "cost", path + ".cost");
            manpower = IntAt(Child(cost, "manpower", path + ".cost.manpower"), path + ".cost.manpower", 0);
            fuel = IntAt(Child(cost, "fuel", path + ".cost.fuel"), path + ".cost.fuel", 0);
            materiel = IntAt(Child(cost, "materiel", path + ".cost.materiel"), path + ".cost.materiel", 0);
            buildTicks = IntAt(Child(row, "buildTicks", path + ".buildTicks"), path + ".buildTicks", 0);
        }

        private static bool TryField(JsonValue obj, string name, out JsonValue value)
        {
            value = null!;
            if (obj.Kind != JsonKind.Object)
            {
                return false;
            }

            for (int i = 0; i < obj.ObjectValue.Count; i++)
            {
                if (obj.ObjectValue[i].Name == name)
                {
                    value = obj.ObjectValue[i].Value;
                    return true;
                }
            }

            return false;
        }

        private static JsonValue Child(JsonValue obj, string name, string path)
        {
            JsonValue value;
            if (!TryField(obj, name, out value))
            {
                throw ScenarioErrors.At(path, "missing required key '" + name + "'.");
            }

            return value;
        }

        private static int IntAt(JsonValue value, string path, int minInclusive)
        {
            if (value.Kind != JsonKind.Int)
            {
                throw ScenarioErrors.At(path, "expected an integer.");
            }

            if (value.IntValue < minInclusive || value.IntValue > int.MaxValue)
            {
                throw ScenarioErrors.At(path, "integer out of range.");
            }

            return (int)value.IntValue;
        }

        private static string TextAt(JsonValue value, string path)
        {
            if (value.Kind != JsonKind.String || value.StringValue.Length == 0)
            {
                throw ScenarioErrors.At(path, "expected a non-empty string.");
            }

            return value.StringValue;
        }
    }
}
