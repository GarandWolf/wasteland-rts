using System;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Movement;

namespace Wasteland.Sim.Scenario
{
    /// <summary>
    /// Ids the scenario loader cross-checks. Designer files
    /// (<c>units.json</c>, <c>buildings.json</c>, <c>rules.json</c>, <c>economy.json</c>)
    /// share one <c>dataVersion</c> of at least <see cref="SimScenario.MinDataVersion"/>.
    /// Personalities come from <c>ai/ai_personalities.json</c> and use that file's own version.
    /// </summary>
    public sealed class BalanceCatalog
    {
        private readonly string[] _unitIds;
        private readonly string[] _unitFactions;
        private readonly int[] _unitTiers;
        private readonly string[] _unitRoles;
        private readonly string[] _unitTargetClasses;
        private readonly int[] _unitManpower;
        private readonly int[] _unitMateriel;
        private readonly int[] _unitFuel;
        private readonly int[] _unitPop;
        private readonly byte[] _unitMoveClass;
        private readonly int[] _unitBuildTicks;
        private readonly int[] _unitProducedAt;
        private readonly string[] _buildingIds;
        private readonly string[][] _buildingFactions;
        private readonly string[] _buildingRoles;
        private readonly string[] _techIds;
        private readonly int[] _techResearchTicks;
        private readonly int[] _techManpower;
        private readonly int[] _techFuel;
        private readonly int[] _techMateriel;
        private readonly int[] _techTargetTier;
        private readonly string[] _factionIds;
        private readonly string[] _personalityIds;
        private readonly string[] _personalityFactions;
        private readonly string[] _holdingTiers;
        private readonly string[] _resourceSites;
        private readonly int[] _speedCmPerTick;

        private BalanceCatalog(
            int dataVersion,
            int startingManpower,
            int startingFuel,
            int startingMateriel,
            string[] unitIds,
            string[] unitFactions,
            int[] unitTiers,
            string[] unitRoles,
            string[] unitTargetClasses,
            int[] unitManpower,
            int[] unitMateriel,
            int[] unitFuel,
            int[] unitPop,
            byte[] unitMoveClass,
            int[] unitBuildTicks,
            int[] unitProducedAt,
            string[] buildingIds,
            string[][] buildingFactions,
            string[] buildingRoles,
            string[] techIds,
            int[] techResearchTicks,
            int[] techManpower,
            int[] techFuel,
            int[] techMateriel,
            int[] techTargetTier,
            string[] factionIds,
            string[] personalityIds,
            string[] personalityFactions,
            string[] holdingTiers,
            string[] resourceSites,
            int[] speedCmPerTick)
        {
            DataVersion = dataVersion;
            StartingManpower = startingManpower;
            StartingFuel = startingFuel;
            StartingMateriel = startingMateriel;
            _unitIds = unitIds;
            _unitFactions = unitFactions;
            _unitTiers = unitTiers;
            _unitRoles = unitRoles;
            _unitTargetClasses = unitTargetClasses;
            _unitManpower = unitManpower;
            _unitMateriel = unitMateriel;
            _unitFuel = unitFuel;
            _unitPop = unitPop;
            _unitMoveClass = unitMoveClass;
            _unitBuildTicks = unitBuildTicks;
            _unitProducedAt = unitProducedAt;
            _buildingIds = buildingIds;
            _buildingFactions = buildingFactions;
            _buildingRoles = buildingRoles;
            _techIds = techIds;
            _techResearchTicks = techResearchTicks;
            _techManpower = techManpower;
            _techFuel = techFuel;
            _techMateriel = techMateriel;
            _techTargetTier = techTargetTier;
            _factionIds = factionIds;
            _personalityIds = personalityIds;
            _personalityFactions = personalityFactions;
            _holdingTiers = holdingTiers;
            _resourceSites = resourceSites;
            _speedCmPerTick = speedCmPerTick;
        }

        public int DataVersion { get; }

        public int StartingManpower { get; }

        public int StartingFuel { get; }

        public int StartingMateriel { get; }

        public static BalanceCatalog LoadFromDirectory(string balanceDirectory)
        {
            if (balanceDirectory == null)
            {
                throw new ArgumentNullException(nameof(balanceDirectory));
            }

            if (!Directory.Exists(balanceDirectory))
            {
                throw new DirectoryNotFoundException(balanceDirectory);
            }

            return Parse(
                File.ReadAllText(Path.Combine(balanceDirectory, "units.json")),
                File.ReadAllText(Path.Combine(balanceDirectory, "buildings.json")),
                File.ReadAllText(Path.Combine(balanceDirectory, "rules.json")),
                File.ReadAllText(Path.Combine(balanceDirectory, "economy.json")),
                File.ReadAllText(Path.Combine(balanceDirectory, "ai", "ai_personalities.json")));
        }

        public static BalanceCatalog Parse(
            string unitsJson,
            string buildingsJson,
            string rulesJson,
            string economyJson,
            string personalitiesJson)
        {
            if (unitsJson == null)
            {
                throw new ArgumentNullException(nameof(unitsJson));
            }

            if (buildingsJson == null)
            {
                throw new ArgumentNullException(nameof(buildingsJson));
            }

            if (rulesJson == null)
            {
                throw new ArgumentNullException(nameof(rulesJson));
            }

            if (economyJson == null)
            {
                throw new ArgumentNullException(nameof(economyJson));
            }

            if (personalitiesJson == null)
            {
                throw new ArgumentNullException(nameof(personalitiesJson));
            }

            JsonValue unitsRoot = IntegerJsonParser.Parse(unitsJson);
            JsonValue buildingsRoot = IntegerJsonParser.Parse(buildingsJson);
            JsonValue rulesRoot = IntegerJsonParser.Parse(rulesJson);
            JsonValue economyRoot = IntegerJsonParser.Parse(economyJson);
            JsonValue personalityRoot = IntegerJsonParser.Parse(personalitiesJson);

            int unitsVersion = RequireDesignerHeader(unitsRoot, "units.json");
            int buildingsVersion = RequireDesignerHeader(buildingsRoot, "buildings.json");
            int rulesVersion = RequireDesignerHeader(rulesRoot, "rules.json");
            int economyVersion = RequireDesignerHeader(economyRoot, "economy.json");
            if (unitsVersion != buildingsVersion || unitsVersion != rulesVersion || unitsVersion != economyVersion)
            {
                throw new FormatException(
                    "balance dataVersion must match across units.json (" + unitsVersion
                    + "), buildings.json (" + buildingsVersion
                    + "), rules.json (" + rulesVersion
                    + "), and economy.json (" + economyVersion + ").");
            }

            RequireAiHeader(personalityRoot);

            string[] factionIds = ReadObjectKeys(Child(economyRoot, "hqIncomePerMinByFaction", "economy.json $.hqIncomePerMinByFaction"), "economy.json $.hqIncomePerMinByFaction");
            RequireSnakeIds(factionIds, "economy.json $.hqIncomePerMinByFaction");
            ScenarioSort.Ordinal(factionIds);
            ScenarioSort.RequireUnique(factionIds, "economy.json $.hqIncomePerMinByFaction");

            string[] holdingTiers = ReadHoldingTiers(economyRoot);
            string[] resourceSites = ReadResourceSites(economyRoot);
            int startingManpower;
            int startingFuel;
            int startingMateriel;
            ReadStartingResources(economyRoot, out startingManpower, out startingFuel, out startingMateriel);

            string[] unitIds;
            string[] unitFactions;
            int[] unitTiers;
            string[] unitRoles;
            string[] unitTargetClasses;
            int[] unitManpower;
            int[] unitMateriel;
            int[] unitFuel;
            int[] unitPop;
            byte[] unitMoveClass;
            int[] unitBuildTicks;
            string[] unitProducedAt;
            ReadUnits(
                unitsRoot,
                out unitIds,
                out unitFactions,
                out unitTiers,
                out unitRoles,
                out unitTargetClasses,
                out unitManpower,
                out unitMateriel,
                out unitFuel,
                out unitPop,
                out unitMoveClass,
                out unitBuildTicks,
                out unitProducedAt);
            string[] buildingIds;
            string[][] buildingFactions;
            string[] buildingRoles;
            ReadBuildings(buildingsRoot, out buildingIds, out buildingFactions, out buildingRoles);
            int[] unitProducedAtIndex = ResolveProducedAt(unitIds, unitProducedAt, buildingIds);
            string[] techIds;
            int[] techResearchTicks;
            int[] techManpower;
            int[] techFuel;
            int[] techMateriel;
            int[] techTargetTier;
            ReadTech(
                rulesRoot,
                out techIds,
                out techResearchTicks,
                out techManpower,
                out techFuel,
                out techMateriel,
                out techTargetTier);
            int[] speedCmPerTick = ReadSpeeds(rulesRoot);
            string[] personalityIds;
            string[] personalityFactions;
            ReadPersonalities(personalityRoot, out personalityIds, out personalityFactions);

            BalanceCatalog catalog = new BalanceCatalog(
                unitsVersion,
                startingManpower,
                startingFuel,
                startingMateriel,
                unitIds,
                unitFactions,
                unitTiers,
                unitRoles,
                unitTargetClasses,
                unitManpower,
                unitMateriel,
                unitFuel,
                unitPop,
                unitMoveClass,
                unitBuildTicks,
                unitProducedAtIndex,
                buildingIds,
                buildingFactions,
                buildingRoles,
                techIds,
                techResearchTicks,
                techManpower,
                techFuel,
                techMateriel,
                techTargetTier,
                factionIds,
                personalityIds,
                personalityFactions,
                holdingTiers,
                resourceSites,
                speedCmPerTick);

            for (int i = 0; i < unitIds.Length; i++)
            {
                if (!catalog.HasFaction(unitFactions[i]))
                {
                    throw ScenarioErrors.At(
                        "units.json $.units." + unitIds[i] + ".faction",
                        "unknown faction '" + unitFactions[i] + "'.");
                }
            }

            for (int i = 0; i < personalityIds.Length; i++)
            {
                if (!catalog.HasFaction(personalityFactions[i]))
                {
                    throw ScenarioErrors.At(
                        "ai_personalities.json $.personalities",
                        "personality '" + personalityIds[i] + "' names unknown faction '" + personalityFactions[i] + "'.");
                }
            }

            return catalog;
        }

        public bool TryGetUnitFaction(string unitId, out string factionId)
        {
            if (unitId == null)
            {
                throw new ArgumentNullException(nameof(unitId));
            }

            int index = IndexOf(_unitIds, unitId);
            if (index < 0)
            {
                factionId = string.Empty;
                return false;
            }

            factionId = _unitFactions[index];
            return true;
        }

        public bool TryGetUnitTier(string unitId, out int tier)
        {
            int index = UnitIndex(unitId);
            if (index < 0)
            {
                tier = 0;
                return false;
            }

            tier = _unitTiers[index];
            return true;
        }

        public bool TryGetUnitRole(string unitId, out string role)
        {
            int index = UnitIndex(unitId);
            if (index < 0)
            {
                role = string.Empty;
                return false;
            }

            role = _unitRoles[index];
            return true;
        }

        public bool TryGetUnitTargetClass(string unitId, out string targetClass)
        {
            int index = UnitIndex(unitId);
            if (index < 0)
            {
                targetClass = string.Empty;
                return false;
            }

            targetClass = _unitTargetClasses[index];
            return true;
        }

        public bool TryGetManpowerCost(string unitId, out int manpower)
        {
            int index = UnitIndex(unitId);
            if (index < 0)
            {
                manpower = 0;
                return false;
            }

            manpower = _unitManpower[index];
            return true;
        }

        public bool TryGetBuildingRole(string buildingId, out string role)
        {
            if (buildingId == null)
            {
                throw new ArgumentNullException(nameof(buildingId));
            }

            int index = IndexOf(_buildingIds, buildingId);
            if (index < 0)
            {
                role = string.Empty;
                return false;
            }

            role = _buildingRoles[index];
            return true;
        }

        internal bool TryGetUnitIndex(string unitId, out int index)
        {
            if (unitId == null)
            {
                index = -1;
                return false;
            }

            index = IndexOf(_unitIds, unitId);
            return index >= 0;
        }

        internal int UnitCount
        {
            get { return _unitIds.Length; }
        }

        internal string UnitIdAt(int index)
        {
            return _unitIds[index];
        }

        internal int UnitTierAt(int index)
        {
            return _unitTiers[index];
        }

        internal int UnitManpowerAt(int index)
        {
            return _unitManpower[index];
        }

        internal int UnitMaterielAt(int index)
        {
            return _unitMateriel[index];
        }

        internal int UnitFuelAt(int index)
        {
            return _unitFuel[index];
        }

        internal int UnitPopAt(int index)
        {
            return _unitPop[index];
        }

        internal int UnitBuildTicksAt(int index)
        {
            return _unitBuildTicks[index];
        }

        internal int UnitProducedAtIndex(int index)
        {
            return _unitProducedAt[index];
        }

        internal MovementClass UnitMoveClassAt(int index)
        {
            return (MovementClass)_unitMoveClass[index];
        }

        internal int SpeedCmPerSecond(MovementClass clas)
        {
            return _speedCmPerTick[(int)clas] * Wasteland.Sim.SimConfig.TickRateHz;
        }

        public bool HasBuilding(string buildingId)
        {
            if (buildingId == null)
            {
                throw new ArgumentNullException(nameof(buildingId));
            }

            return IndexOf(_buildingIds, buildingId) >= 0;
        }

        public bool BuildingAllowsFaction(string buildingId, string factionId)
        {
            if (buildingId == null)
            {
                throw new ArgumentNullException(nameof(buildingId));
            }

            if (factionId == null)
            {
                throw new ArgumentNullException(nameof(factionId));
            }

            int index = IndexOf(_buildingIds, buildingId);
            if (index < 0)
            {
                return false;
            }

            return IndexOf(_buildingFactions[index], factionId) >= 0;
        }

        public bool HasTech(string techId)
        {
            if (techId == null)
            {
                throw new ArgumentNullException(nameof(techId));
            }

            return IndexOf(_techIds, techId) >= 0;
        }

        /// <summary>
        /// Research duration in ticks for a tech id such as <c>tier2</c>.
        /// </summary>
        public bool TryGetTechResearchTicks(string techId, out int researchTicks)
        {
            int index = TechIndex(techId);
            if (index < 0)
            {
                researchTicks = 0;
                return false;
            }

            researchTicks = _techResearchTicks[index];
            return true;
        }

        /// <summary>
        /// Manpower, fuel, and materiel charged when research in <paramref name="techId"/> starts.
        /// </summary>
        public bool TryGetTechCost(string techId, out int manpower, out int fuel, out int materiel)
        {
            int index = TechIndex(techId);
            if (index < 0)
            {
                manpower = 0;
                fuel = 0;
                materiel = 0;
                return false;
            }

            manpower = _techManpower[index];
            fuel = _techFuel[index];
            materiel = _techMateriel[index];
            return true;
        }

        internal int TechResearchTicksAt(int index)
        {
            return _techResearchTicks[index];
        }

        internal int TechManpowerAt(int index)
        {
            return _techManpower[index];
        }

        internal int TechFuelAt(int index)
        {
            return _techFuel[index];
        }

        internal int TechMaterielAt(int index)
        {
            return _techMateriel[index];
        }

        internal int TechTargetTierAt(int index)
        {
            return _techTargetTier[index];
        }

        internal bool TryGetTechIndex(string techId, out int index)
        {
            index = TechIndex(techId);
            return index >= 0;
        }

        public bool HasFaction(string factionId)
        {
            if (factionId == null)
            {
                throw new ArgumentNullException(nameof(factionId));
            }

            return IndexOf(_factionIds, factionId) >= 0;
        }

        public bool TryGetPersonalityFaction(string personalityId, out string factionId)
        {
            if (personalityId == null)
            {
                throw new ArgumentNullException(nameof(personalityId));
            }

            int index = IndexOf(_personalityIds, personalityId);
            if (index < 0)
            {
                factionId = string.Empty;
                return false;
            }

            factionId = _personalityFactions[index];
            return true;
        }

        public bool HasHoldingTier(string tierId)
        {
            if (tierId == null)
            {
                throw new ArgumentNullException(nameof(tierId));
            }

            return IndexOf(_holdingTiers, tierId) >= 0;
        }

        public bool HasResourceSite(string siteId)
        {
            if (siteId == null)
            {
                throw new ArgumentNullException(nameof(siteId));
            }

            return IndexOf(_resourceSites, siteId) >= 0;
        }

        private static int RequireDesignerHeader(JsonValue root, string fileName)
        {
            if (root.Kind != JsonKind.Object)
            {
                throw ScenarioErrors.At(fileName + " $", "expected an object.");
            }

            int schema = IntAt(Child(root, "schemaVersion", fileName + " $.schemaVersion"), fileName + " $.schemaVersion", 0);
            if (schema != SimScenario.SchemaVersion)
            {
                throw ScenarioErrors.At(fileName + " $.schemaVersion", "expected " + SimScenario.SchemaVersion + ".");
            }

            int dataVersion = IntAt(Child(root, "dataVersion", fileName + " $.dataVersion"), fileName + " $.dataVersion", 0);
            if (dataVersion < SimScenario.MinDataVersion)
            {
                throw ScenarioErrors.At(
                    fileName + " $.dataVersion",
                    "must be >= " + SimScenario.MinDataVersion + ".");
            }

            return dataVersion;
        }

        private static void RequireAiHeader(JsonValue root)
        {
            const string fileName = "ai_personalities.json";
            if (root.Kind != JsonKind.Object)
            {
                throw ScenarioErrors.At(fileName + " $", "expected an object.");
            }

            int schema = IntAt(Child(root, "schemaVersion", fileName + " $.schemaVersion"), fileName + " $.schemaVersion", 0);
            if (schema != AiConstants.BalanceSchemaVersion)
            {
                throw ScenarioErrors.At(fileName + " $.schemaVersion", "expected " + AiConstants.BalanceSchemaVersion + ".");
            }

            int dataVersion = IntAt(Child(root, "dataVersion", fileName + " $.dataVersion"), fileName + " $.dataVersion", 0);
            if (dataVersion != AiConstants.DataVersion)
            {
                throw ScenarioErrors.At(
                    fileName + " $.dataVersion",
                    "is required and must be " + AiConstants.DataVersion + ".");
            }
        }

        private static string[] ReadHoldingTiers(JsonValue economyRoot)
        {
            JsonValue tiers = Child(economyRoot, "holdingTiers", "economy.json $.holdingTiers");
            if (tiers.Kind != JsonKind.Array)
            {
                throw ScenarioErrors.At("economy.json $.holdingTiers", "expected an array.");
            }

            string[] ids = new string[tiers.ArrayValue.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                string path = "economy.json $.holdingTiers[" + i + "]";
                JsonValue row = tiers.ArrayValue[i];
                if (row.Kind != JsonKind.Object)
                {
                    throw ScenarioErrors.At(path, "expected an object.");
                }

                ids[i] = TextAt(Child(row, "id", path + ".id"), path + ".id");
                ScenarioText.RequireSnake(ids[i], path + ".id");
            }

            ScenarioSort.Ordinal(ids);
            ScenarioSort.RequireUnique(ids, "economy.json $.holdingTiers");
            RequireId(ids, "hamlet", "economy.json $.holdingTiers");
            RequireId(ids, "village", "economy.json $.holdingTiers");
            RequireId(ids, "town", "economy.json $.holdingTiers");
            RequireId(ids, "city", "economy.json $.holdingTiers");
            return ids;
        }

        private static string[] ReadResourceSites(JsonValue economyRoot)
        {
            JsonValue sites = Child(economyRoot, "resourceSites", "economy.json $.resourceSites");
            if (sites.Kind != JsonKind.Array)
            {
                throw ScenarioErrors.At("economy.json $.resourceSites", "expected an array.");
            }

            string[] ids = new string[sites.ArrayValue.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                string path = "economy.json $.resourceSites[" + i + "]";
                JsonValue row = sites.ArrayValue[i];
                if (row.Kind != JsonKind.Object)
                {
                    throw ScenarioErrors.At(path, "expected an object.");
                }

                ids[i] = TextAt(Child(row, "id", path + ".id"), path + ".id");
                ScenarioText.RequireSnake(ids[i], path + ".id");
            }

            ScenarioSort.Ordinal(ids);
            ScenarioSort.RequireUnique(ids, "economy.json $.resourceSites");
            RequireId(ids, "fuel_station", "economy.json $.resourceSites");
            RequireId(ids, "fuel_storage_depot", "economy.json $.resourceSites");
            RequireId(ids, "refinery", "economy.json $.resourceSites");
            RequireId(ids, "industrial_area", "economy.json $.resourceSites");
            return ids;
        }

        private static void ReadStartingResources(
            JsonValue economyRoot,
            out int manpower,
            out int fuel,
            out int materiel)
        {
            JsonValue resources = Child(economyRoot, "startingResources", "economy.json $.startingResources");
            manpower = IntAt(
                Child(resources, "manpower", "economy.json $.startingResources.manpower"),
                "economy.json $.startingResources.manpower",
                0);
            fuel = IntAt(
                Child(resources, "fuel", "economy.json $.startingResources.fuel"),
                "economy.json $.startingResources.fuel",
                0);
            materiel = IntAt(
                Child(resources, "materiel", "economy.json $.startingResources.materiel"),
                "economy.json $.startingResources.materiel",
                0);
        }

        private static void ReadUnits(
            JsonValue root,
            out string[] ids,
            out string[] factions,
            out int[] tiers,
            out string[] roles,
            out string[] targetClasses,
            out int[] manpower,
            out int[] materiel,
            out int[] fuel,
            out int[] pop,
            out byte[] moveClass,
            out int[] buildTicks,
            out string[] producedAt)
        {
            JsonValue units = Child(root, "units", "units.json $.units");
            if (units.Kind != JsonKind.Object || units.ObjectValue.Count < 1)
            {
                throw ScenarioErrors.At("units.json $.units", "expected a non-empty object.");
            }

            int count = units.ObjectValue.Count;
            ids = new string[count];
            factions = new string[count];
            tiers = new int[count];
            roles = new string[count];
            targetClasses = new string[count];
            manpower = new int[count];
            materiel = new int[count];
            fuel = new int[count];
            pop = new int[count];
            moveClass = new byte[count];
            buildTicks = new int[count];
            producedAt = new string[count];
            for (int i = 0; i < count; i++)
            {
                string id = units.ObjectValue[i].Name;
                string path = "units.json $.units." + id;
                ScenarioText.RequireSnake(id, path);
                JsonValue row = units.ObjectValue[i].Value;
                factions[i] = TextAt(Child(row, "faction", path + ".faction"), path + ".faction");
                ScenarioText.RequireSnake(factions[i], path + ".faction");
                ids[i] = id;
                tiers[i] = IntAt(Child(row, "techTier", path + ".techTier"), path + ".techTier", 1);
                roles[i] = TextAt(Child(row, "role", path + ".role"), path + ".role");
                ScenarioText.RequireSnake(roles[i], path + ".role");
                targetClasses[i] = TextAt(Child(row, "targetClass", path + ".targetClass"), path + ".targetClass");
                ScenarioText.RequireSnake(targetClasses[i], path + ".targetClass");
                JsonValue cost = Child(row, "cost", path + ".cost");
                manpower[i] = IntAt(Child(cost, "manpower", path + ".cost.manpower"), path + ".cost.manpower", 0);
                fuel[i] = IntAt(Child(cost, "fuel", path + ".cost.fuel"), path + ".cost.fuel", 0);
                materiel[i] = IntAt(Child(cost, "materiel", path + ".cost.materiel"), path + ".cost.materiel", 0);
                pop[i] = IntAt(Child(row, "popCost", path + ".popCost"), path + ".popCost", 0);
                moveClass[i] = ParseMoveClass(
                    TextAt(Child(row, "movementClass", path + ".movementClass"), path + ".movementClass"),
                    path + ".movementClass");
                buildTicks[i] = IntAt(Child(row, "buildTicks", path + ".buildTicks"), path + ".buildTicks", 0);
                producedAt[i] = TextAt(Child(row, "producedAt", path + ".producedAt"), path + ".producedAt");
                ScenarioText.RequireSnake(producedAt[i], path + ".producedAt");
            }

            SortUnits(ids, factions, tiers, roles, targetClasses, manpower, materiel, fuel, pop, moveClass, buildTicks, producedAt);
            ScenarioSort.RequireUnique(ids, "units.json $.units");
        }

        private static int[] ReadSpeeds(JsonValue rulesRoot)
        {
            string path = "rules.json $.movement.baseSpeedCmPerTick";
            JsonValue movement = Child(rulesRoot, "movement", "rules.json $.movement");
            JsonValue speeds = Child(movement, "baseSpeedCmPerTick", path);
            int[] result = new int[4];
            result[(int)MovementClass.Wheeled] = IntAt(Child(speeds, "wheeled", path + ".wheeled"), path + ".wheeled", 0);
            result[(int)MovementClass.Tracked] = IntAt(Child(speeds, "tracked", path + ".tracked"), path + ".tracked", 0);
            result[(int)MovementClass.Foot] = IntAt(Child(speeds, "foot", path + ".foot"), path + ".foot", 0);
            result[(int)MovementClass.Air] = IntAt(Child(speeds, "air", path + ".air"), path + ".air", 0);
            return result;
        }

        private static byte ParseMoveClass(string text, string path)
        {
            if (text == "wheeled")
            {
                return (byte)MovementClass.Wheeled;
            }

            if (text == "tracked")
            {
                return (byte)MovementClass.Tracked;
            }

            if (text == "foot")
            {
                return (byte)MovementClass.Foot;
            }

            if (text == "air")
            {
                return (byte)MovementClass.Air;
            }

            throw ScenarioErrors.At(path, "unknown movement class '" + text + "'.");
        }

        private static void ReadBuildings(JsonValue root, out string[] ids, out string[][] factions, out string[] roles)
        {
            JsonValue buildings = Child(root, "buildings", "buildings.json $.buildings");
            if (buildings.Kind != JsonKind.Object || buildings.ObjectValue.Count < 1)
            {
                throw ScenarioErrors.At("buildings.json $.buildings", "expected a non-empty object.");
            }

            int count = buildings.ObjectValue.Count;
            ids = new string[count];
            factions = new string[count][];
            roles = new string[count];
            for (int i = 0; i < count; i++)
            {
                string id = buildings.ObjectValue[i].Name;
                string path = "buildings.json $.buildings." + id;
                ScenarioText.RequireSnake(id, path);
                JsonValue row = buildings.ObjectValue[i].Value;
                JsonValue factionArray = Child(row, "factions", path + ".factions");
                if (factionArray.Kind != JsonKind.Array || factionArray.ArrayValue.Count < 1)
                {
                    throw ScenarioErrors.At(path + ".factions", "expected a non-empty array.");
                }

                string[] allowed = new string[factionArray.ArrayValue.Count];
                for (int f = 0; f < allowed.Length; f++)
                {
                    string factionPath = path + ".factions[" + f + "]";
                    allowed[f] = TextAt(factionArray.ArrayValue[f], factionPath);
                    ScenarioText.RequireSnake(allowed[f], factionPath);
                }

                ScenarioSort.Ordinal(allowed);
                ScenarioSort.RequireUnique(allowed, path + ".factions");
                ids[i] = id;
                factions[i] = allowed;
                roles[i] = TextAt(Child(row, "role", path + ".role"), path + ".role");
                ScenarioText.RequireSnake(roles[i], path + ".role");
            }

            SortBuildings(ids, factions, roles);
            ScenarioSort.RequireUnique(ids, "buildings.json $.buildings");
        }

        private static void ReadTech(
            JsonValue rulesRoot,
            out string[] ids,
            out int[] researchTicks,
            out int[] manpower,
            out int[] fuel,
            out int[] materiel,
            out int[] targetTier)
        {
            JsonValue tech = Child(rulesRoot, "tech", "rules.json $.tech");
            if (tech.Kind != JsonKind.Object || tech.ObjectValue.Count < 1)
            {
                throw ScenarioErrors.At("rules.json $.tech", "expected a non-empty object.");
            }

            int count = tech.ObjectValue.Count;
            ids = new string[count];
            researchTicks = new int[count];
            manpower = new int[count];
            fuel = new int[count];
            materiel = new int[count];
            targetTier = new int[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = tech.ObjectValue[i].Name;
                string path = "rules.json $.tech." + ids[i];
                ScenarioText.RequireSnake(ids[i], path);
                JsonValue row = tech.ObjectValue[i].Value;
                if (row.Kind != JsonKind.Object)
                {
                    throw ScenarioErrors.At(path, "expected an object.");
                }

                researchTicks[i] = IntAt(Child(row, "researchTicks", path + ".researchTicks"), path + ".researchTicks", 0);
                JsonValue cost = Child(row, "cost", path + ".cost");
                manpower[i] = IntAt(Child(cost, "manpower", path + ".cost.manpower"), path + ".cost.manpower", 0);
                fuel[i] = IntAt(Child(cost, "fuel", path + ".cost.fuel"), path + ".cost.fuel", 0);
                materiel[i] = IntAt(Child(cost, "materiel", path + ".cost.materiel"), path + ".cost.materiel", 0);
                targetTier[i] = TargetTierFromId(ids[i], path);
            }

            int[] order = OrderOf(ids);
            ids = Permute(ids, order);
            researchTicks = Permute(researchTicks, order);
            manpower = Permute(manpower, order);
            fuel = Permute(fuel, order);
            materiel = Permute(materiel, order);
            targetTier = Permute(targetTier, order);
            ScenarioSort.RequireUnique(ids, "rules.json $.tech");
        }

        private static int TargetTierFromId(string id, string path)
        {
            const string prefix = "tier";
            if (id.Length <= prefix.Length || string.CompareOrdinal(id, 0, prefix, 0, prefix.Length) != 0)
            {
                throw ScenarioErrors.At(path, "tech id must be tier followed by a positive integer.");
            }

            int value = 0;
            for (int i = prefix.Length; i < id.Length; i++)
            {
                char c = id[i];
                if (c < '0' || c > '9')
                {
                    throw ScenarioErrors.At(path, "tech id must be tier followed by a positive integer.");
                }

                value = checked((value * 10) + (c - '0'));
            }

            if (value < 1)
            {
                throw ScenarioErrors.At(path, "tech id must be tier followed by a positive integer.");
            }

            return value;
        }

        private static string[] Permute(string[] values, int[] order)
        {
            string[] sorted = new string[values.Length];
            for (int i = 0; i < order.Length; i++)
            {
                sorted[i] = values[order[i]];
            }

            return sorted;
        }

        private static int[] Permute(int[] values, int[] order)
        {
            int[] sorted = new int[values.Length];
            for (int i = 0; i < order.Length; i++)
            {
                sorted[i] = values[order[i]];
            }

            return sorted;
        }

        private static void ReadPersonalities(JsonValue root, out string[] ids, out string[] factions)
        {
            JsonValue personalities = Child(root, "personalities", "ai_personalities.json $.personalities");
            if (personalities.Kind != JsonKind.Array || personalities.ArrayValue.Count < 1)
            {
                throw ScenarioErrors.At("ai_personalities.json $.personalities", "expected a non-empty array.");
            }

            ids = new string[personalities.ArrayValue.Count];
            factions = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                string path = "ai_personalities.json $.personalities[" + i + "]";
                JsonValue row = personalities.ArrayValue[i];
                if (row.Kind != JsonKind.Object)
                {
                    throw ScenarioErrors.At(path, "expected an object.");
                }

                ids[i] = TextAt(Child(row, "id", path + ".id"), path + ".id");
                ScenarioText.RequireSnake(ids[i], path + ".id");
                factions[i] = TextAt(Child(row, "economyFactionId", path + ".economyFactionId"), path + ".economyFactionId");
                ScenarioText.RequireSnake(factions[i], path + ".economyFactionId");
            }

            SortParallel(ids, factions);
            ScenarioSort.RequireUnique(ids, "ai_personalities.json $.personalities");
        }

        private static string[] ReadObjectKeys(JsonValue obj, string path)
        {
            if (obj.Kind != JsonKind.Object || obj.ObjectValue.Count < 1)
            {
                throw ScenarioErrors.At(path, "expected a non-empty object.");
            }

            string[] ids = new string[obj.ObjectValue.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = obj.ObjectValue[i].Name;
            }

            return ids;
        }

        private static void RequireSnakeIds(string[] ids, string path)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                ScenarioText.RequireSnake(ids[i], path + "." + ids[i]);
            }
        }

        private static void RequireId(string[] sortedIds, string id, string path)
        {
            if (IndexOf(sortedIds, id) < 0)
            {
                throw ScenarioErrors.At(path, "missing id '" + id + "'.");
            }
        }

        private static void SortParallel(string[] ids, string[] factions)
        {
            int[] order = OrderOf(ids);
            string[] sortedIds = new string[ids.Length];
            string[] sortedFactions = new string[ids.Length];
            for (int i = 0; i < order.Length; i++)
            {
                sortedIds[i] = ids[order[i]];
                sortedFactions[i] = factions[order[i]];
            }

            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = sortedIds[i];
                factions[i] = sortedFactions[i];
            }
        }

        private static void SortUnits(
            string[] ids,
            string[] factions,
            int[] tiers,
            string[] roles,
            string[] targetClasses,
            int[] manpower,
            int[] materiel,
            int[] fuel,
            int[] pop,
            byte[] moveClass,
            int[] buildTicks,
            string[] producedAt)
        {
            int[] order = OrderOf(ids);
            string[] sortedIds = new string[ids.Length];
            string[] sortedFactions = new string[ids.Length];
            int[] sortedTiers = new int[ids.Length];
            string[] sortedRoles = new string[ids.Length];
            string[] sortedTargets = new string[ids.Length];
            int[] sortedManpower = new int[ids.Length];
            int[] sortedMateriel = new int[ids.Length];
            int[] sortedFuel = new int[ids.Length];
            int[] sortedPop = new int[ids.Length];
            byte[] sortedMove = new byte[ids.Length];
            int[] sortedBuild = new int[ids.Length];
            string[] sortedProduced = new string[ids.Length];
            for (int i = 0; i < order.Length; i++)
            {
                int from = order[i];
                sortedIds[i] = ids[from];
                sortedFactions[i] = factions[from];
                sortedTiers[i] = tiers[from];
                sortedRoles[i] = roles[from];
                sortedTargets[i] = targetClasses[from];
                sortedManpower[i] = manpower[from];
                sortedMateriel[i] = materiel[from];
                sortedFuel[i] = fuel[from];
                sortedPop[i] = pop[from];
                sortedMove[i] = moveClass[from];
                sortedBuild[i] = buildTicks[from];
                sortedProduced[i] = producedAt[from];
            }

            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = sortedIds[i];
                factions[i] = sortedFactions[i];
                tiers[i] = sortedTiers[i];
                roles[i] = sortedRoles[i];
                targetClasses[i] = sortedTargets[i];
                manpower[i] = sortedManpower[i];
                materiel[i] = sortedMateriel[i];
                fuel[i] = sortedFuel[i];
                pop[i] = sortedPop[i];
                moveClass[i] = sortedMove[i];
                buildTicks[i] = sortedBuild[i];
                producedAt[i] = sortedProduced[i];
            }
        }

        private static int[] ResolveProducedAt(string[] unitIds, string[] producedAt, string[] buildingIds)
        {
            int[] index = new int[producedAt.Length];
            for (int i = 0; i < producedAt.Length; i++)
            {
                int building = IndexOf(buildingIds, producedAt[i]);
                if (building < 0)
                {
                    throw ScenarioErrors.At(
                        "units.json $.units." + unitIds[i] + ".producedAt",
                        "unknown building '" + producedAt[i] + "'.");
                }

                index[i] = building;
            }

            return index;
        }

        private static void SortBuildings(string[] ids, string[][] factions, string[] roles)
        {
            int[] order = OrderOf(ids);
            string[] sortedIds = new string[ids.Length];
            string[][] sortedFactions = new string[ids.Length][];
            string[] sortedRoles = new string[ids.Length];
            for (int i = 0; i < order.Length; i++)
            {
                sortedIds[i] = ids[order[i]];
                sortedFactions[i] = factions[order[i]];
                sortedRoles[i] = roles[order[i]];
            }

            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = sortedIds[i];
                factions[i] = sortedFactions[i];
                roles[i] = sortedRoles[i];
            }
        }

        private int UnitIndex(string unitId)
        {
            if (unitId == null)
            {
                throw new ArgumentNullException(nameof(unitId));
            }

            return IndexOf(_unitIds, unitId);
        }

        private int TechIndex(string techId)
        {
            if (techId == null)
            {
                throw new ArgumentNullException(nameof(techId));
            }

            return IndexOf(_techIds, techId);
        }

        private static int[] OrderOf(string[] ids)
        {
            int[] order = new int[ids.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, delegate (int a, int b)
            {
                return string.CompareOrdinal(ids[a], ids[b]);
            });
            return order;
        }

        private static int IndexOf(string[] sorted, string id)
        {
            int lo = 0;
            int hi = sorted.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int cmp = string.CompareOrdinal(sorted[mid], id);
                if (cmp == 0)
                {
                    return mid;
                }

                if (cmp < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return -1;
        }

        private static JsonValue Child(JsonValue obj, string name, string path)
        {
            if (obj.Kind != JsonKind.Object)
            {
                throw ScenarioErrors.At(path, "expected an object.");
            }

            if (!JsonUtil.TryField(obj, name, out JsonValue value))
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
            if (value.Kind != JsonKind.String)
            {
                throw ScenarioErrors.At(path, "expected a string.");
            }

            if (value.StringValue.Length == 0)
            {
                throw ScenarioErrors.At(path, "expected a non-empty string.");
            }

            return value.StringValue;
        }
    }
}
