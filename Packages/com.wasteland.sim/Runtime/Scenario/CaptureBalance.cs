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
            int depotBuildTicks,
            IncomeSchedule income)
        {
            if (income == null)
            {
                throw new ArgumentNullException(nameof(income));
            }

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
            _income = income;
        }

        private readonly IncomeSchedule _income;

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

        /// <summary><c>economy.json</c> <c>incomeAccrual.intervalTicks</c>.</summary>
        public int AccrualIntervalTicks
        {
            get { return _income.IntervalTicks; }
        }

        /// <summary><c>economy.json</c> <c>incomeAccrual.intervalsPerMinute</c>.</summary>
        public int AccrualIntervalsPerMinute
        {
            get { return _income.IntervalsPerMinute; }
        }

        /// <summary><c>economy.json</c> <c>contestedHoldingPaysIncome</c>.</summary>
        public bool ContestedHoldingPaysIncome
        {
            get { return _income.ContestedHoldingPays; }
        }

        public int HoldingManpowerPerMin(int tier)
        {
            return _income.Manpower[tier];
        }

        public int HoldingMaterielPerMin(int tier)
        {
            return _income.Materiel[tier];
        }

        public int HoldingFuelPerMin(int tier)
        {
            return _income.Fuel[tier];
        }

        /// <summary>
        /// HQ income and faction ppt modifiers. Missing factions report zero
        /// HQ income and a 1000 ppt modifier.
        /// </summary>
        public bool TryHqIncome(
            string factionId,
            out int manpower,
            out int materiel,
            out int fuel,
            out int manpowerPpt,
            out int materielPpt,
            out int fuelPpt)
        {
            manpower = 0;
            materiel = 0;
            fuel = 0;
            manpowerPpt = 1000;
            materielPpt = 1000;
            fuelPpt = 1000;
            if (factionId == null)
            {
                return false;
            }

            for (int i = 0; i < _income.FactionIds.Length; i++)
            {
                if (_income.FactionIds[i] != factionId)
                {
                    continue;
                }

                manpower = _income.HqManpower[i];
                materiel = _income.HqMateriel[i];
                fuel = _income.HqFuel[i];
                manpowerPpt = _income.ManpowerPpt[i];
                materielPpt = _income.MaterielPpt[i];
                fuelPpt = _income.FuelPpt[i];
                return true;
            }

            return false;
        }

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
            IncomeSchedule income = ReadIncome(economyRoot);
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
                buildTicks,
                income);
        }

        private static IncomeSchedule ReadIncome(JsonValue economyRoot)
        {
            IncomeSchedule income = new IncomeSchedule();
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
                JsonValue perMin = Child(row, "incomePerMin", path + ".incomePerMin");
                income.Manpower[index] = IntAt(
                    Child(perMin, "manpower", path + ".incomePerMin.manpower"),
                    path + ".incomePerMin.manpower",
                    0);
                income.Fuel[index] = IntAt(
                    Child(perMin, "fuel", path + ".incomePerMin.fuel"),
                    path + ".incomePerMin.fuel",
                    0);
                income.Materiel[index] = IntAt(
                    Child(perMin, "materiel", path + ".incomePerMin.materiel"),
                    path + ".incomePerMin.materiel",
                    0);
            }

            for (int i = 0; i < seen.Length; i++)
            {
                if (!seen[i])
                {
                    throw ScenarioErrors.At("economy.json $.holdingTiers", "missing tier '" + TierIds[i] + "'.");
                }
            }

            JsonValue accrual = Child(economyRoot, "incomeAccrual", "economy.json $.incomeAccrual");
            income.IntervalTicks = IntAt(
                Child(accrual, "intervalTicks", "economy.json $.incomeAccrual.intervalTicks"),
                "economy.json $.incomeAccrual.intervalTicks",
                1);
            income.IntervalsPerMinute = IntAt(
                Child(accrual, "intervalsPerMinute", "economy.json $.incomeAccrual.intervalsPerMinute"),
                "economy.json $.incomeAccrual.intervalsPerMinute",
                1);
            string method = TextAt(
                Child(accrual, "method", "economy.json $.incomeAccrual.method"),
                "economy.json $.incomeAccrual.method");
            if (method != "remainder_carry")
            {
                throw ScenarioErrors.At("economy.json $.incomeAccrual.method", "expected remainder_carry.");
            }

            JsonValue hq = Child(economyRoot, "hqIncomePerMinByFaction", "economy.json $.hqIncomePerMinByFaction");
            JsonValue mods = Child(economyRoot, "factionIncomeModifiersPpt", "economy.json $.factionIncomeModifiersPpt");
            if (hq.Kind != JsonKind.Object || mods.Kind != JsonKind.Object)
            {
                throw ScenarioErrors.At("economy.json $.hqIncomePerMinByFaction", "expected an object.");
            }

            int count = hq.ObjectValue.Count;
            income.FactionIds = new string[count];
            income.HqManpower = new int[count];
            income.HqMateriel = new int[count];
            income.HqFuel = new int[count];
            income.ManpowerPpt = new int[count];
            income.MaterielPpt = new int[count];
            income.FuelPpt = new int[count];
            for (int i = 0; i < count; i++)
            {
                string id = hq.ObjectValue[i].Name;
                string path = "economy.json $.hqIncomePerMinByFaction." + id;
                JsonValue row = hq.ObjectValue[i].Value;
                income.FactionIds[i] = id;
                income.HqManpower[i] = IntAt(Child(row, "manpower", path + ".manpower"), path + ".manpower", 0);
                income.HqFuel[i] = IntAt(Child(row, "fuel", path + ".fuel"), path + ".fuel", 0);
                income.HqMateriel[i] = IntAt(Child(row, "materiel", path + ".materiel"), path + ".materiel", 0);

                JsonValue modRow;
                if (!TryField(mods, id, out modRow))
                {
                    throw ScenarioErrors.At(
                        "economy.json $.factionIncomeModifiersPpt." + id,
                        "missing faction modifier.");
                }

                string modPath = "economy.json $.factionIncomeModifiersPpt." + id;
                income.ManpowerPpt[i] = IntAt(Child(modRow, "manpower", modPath + ".manpower"), modPath + ".manpower", 0);
                income.FuelPpt[i] = IntAt(Child(modRow, "fuel", modPath + ".fuel"), modPath + ".fuel", 0);
                income.MaterielPpt[i] = IntAt(Child(modRow, "materiel", modPath + ".materiel"), modPath + ".materiel", 0);
            }

            SortFactionIncome(income);
            income.ContestedHoldingPays = BoolAt(
                Child(economyRoot, "contestedHoldingPaysIncome", "economy.json $.contestedHoldingPaysIncome"),
                "economy.json $.contestedHoldingPaysIncome");
            return income;
        }

        private static void SortFactionIncome(IncomeSchedule income)
        {
            for (int i = 1; i < income.FactionIds.Length; i++)
            {
                string id = income.FactionIds[i];
                int hqManpower = income.HqManpower[i];
                int hqMateriel = income.HqMateriel[i];
                int hqFuel = income.HqFuel[i];
                int manpowerPpt = income.ManpowerPpt[i];
                int materielPpt = income.MaterielPpt[i];
                int fuelPpt = income.FuelPpt[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(income.FactionIds[j], id) > 0)
                {
                    income.FactionIds[j + 1] = income.FactionIds[j];
                    income.HqManpower[j + 1] = income.HqManpower[j];
                    income.HqMateriel[j + 1] = income.HqMateriel[j];
                    income.HqFuel[j + 1] = income.HqFuel[j];
                    income.ManpowerPpt[j + 1] = income.ManpowerPpt[j];
                    income.MaterielPpt[j + 1] = income.MaterielPpt[j];
                    income.FuelPpt[j + 1] = income.FuelPpt[j];
                    j--;
                }

                income.FactionIds[j + 1] = id;
                income.HqManpower[j + 1] = hqManpower;
                income.HqMateriel[j + 1] = hqMateriel;
                income.HqFuel[j + 1] = hqFuel;
                income.ManpowerPpt[j + 1] = manpowerPpt;
                income.MaterielPpt[j + 1] = materielPpt;
                income.FuelPpt[j + 1] = fuelPpt;
            }
        }

        private static bool BoolAt(JsonValue value, string path)
        {
            if (value.Kind != JsonKind.Bool)
            {
                throw ScenarioErrors.At(path, "expected a boolean.");
            }

            return value.BoolValue;
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

        internal sealed class IncomeSchedule
        {
            public int IntervalTicks;
            public int IntervalsPerMinute;
            public bool ContestedHoldingPays;
            public readonly int[] Manpower = new int[TierCount];
            public readonly int[] Materiel = new int[TierCount];
            public readonly int[] Fuel = new int[TierCount];
            public string[] FactionIds = Array.Empty<string>();
            public int[] HqManpower = Array.Empty<int>();
            public int[] HqMateriel = Array.Empty<int>();
            public int[] HqFuel = Array.Empty<int>();
            public int[] ManpowerPpt = Array.Empty<int>();
            public int[] MaterielPpt = Array.Empty<int>();
            public int[] FuelPpt = Array.Empty<int>();
        }
    }
}
