namespace Wasteland.Sim.Scenario
{
    /// <summary>
    /// Who issues commands for a seat. A null <c>aiPersonality</c> is a player.
    /// A personality id is an AI controller.
    /// </summary>
    public enum ScenarioController : byte
    {
        Player = 0,
        Ai = 1
    }

    /// <summary>Holding counts in the four economy tiers. Stored by tier id, not object order.</summary>
    public readonly struct ScenarioPlaceCounts
    {
        public readonly int Hamlet;
        public readonly int Village;
        public readonly int Town;
        public readonly int City;

        internal ScenarioPlaceCounts(int hamlet, int village, int town, int city)
        {
            Hamlet = hamlet;
            Village = village;
            Town = town;
            City = city;
        }

        public int Total
        {
            get { return Hamlet + Village + Town + City; }
        }

        public bool EqualsCounts(ScenarioPlaceCounts other)
        {
            return Hamlet == other.Hamlet
                && Village == other.Village
                && Town == other.Town
                && City == other.City;
        }
    }

    /// <summary>Resource-site counts. Field order is the ordinal id order used by the content hash.</summary>
    public readonly struct ScenarioSiteCounts
    {
        public readonly int FuelStation;
        public readonly int FuelStorageDepot;
        public readonly int Refinery;
        public readonly int IndustrialArea;

        internal ScenarioSiteCounts(int fuelStation, int fuelStorageDepot, int refinery, int industrialArea)
        {
            FuelStation = fuelStation;
            FuelStorageDepot = fuelStorageDepot;
            Refinery = refinery;
            IndustrialArea = industrialArea;
        }

        public bool EqualsCounts(ScenarioSiteCounts other)
        {
            return FuelStation == other.FuelStation
                && FuelStorageDepot == other.FuelStorageDepot
                && Refinery == other.Refinery
                && IndustrialArea == other.IndustrialArea;
        }
    }

    public readonly struct ScenarioUnitCount
    {
        public readonly string UnitId;
        public readonly int Count;

        internal ScenarioUnitCount(string unitId, int count)
        {
            UnitId = unitId;
            Count = count;
        }
    }

    public readonly struct ScenarioHolding
    {
        public readonly ulong TerritoryKey;
        public readonly int TerritoryId;
        public readonly ScenarioPlaceCounts Places;

        internal ScenarioHolding(ulong territoryKey, int territoryId, ScenarioPlaceCounts places)
        {
            TerritoryKey = territoryKey;
            TerritoryId = territoryId;
            Places = places;
        }
    }

    public readonly struct ScenarioAutoResearch
    {
        public readonly string Item;
        public readonly int NotBeforeTick;

        internal ScenarioAutoResearch(string item, int notBeforeTick)
        {
            Item = item;
            NotBeforeTick = notBeforeTick;
        }
    }

    /// <summary>
    /// One scenario seat. Catalog arrays are sorted by id. Territory keys are u64
    /// values parsed from <c>0x</c> + 16 uppercase hex digits.
    /// </summary>
    public readonly struct ScenarioSeat
    {
        public readonly int Slot;
        public readonly string FactionId;
        public readonly ScenarioController Controller;
        public readonly string PersonalityId;
        public readonly ulong HomeTerritoryKey;
        public readonly int HomeTerritoryId;
        public readonly int HomeTownId;
        public readonly int StartingManpower;
        public readonly string[] StartingBuildings;
        public readonly ScenarioUnitCount[] StartingUnits;
        public readonly ScenarioHolding[] StartingHoldings;
        public readonly string[] RaisableUnits;
        public readonly ScenarioAutoResearch[] AutoResearch;
        public readonly string[] BuildableBuildings;

        internal ScenarioSeat(
            int slot,
            string factionId,
            ScenarioController controller,
            string personalityId,
            ulong homeTerritoryKey,
            int homeTerritoryId,
            int homeTownId,
            int startingManpower,
            string[] startingBuildings,
            ScenarioUnitCount[] startingUnits,
            ScenarioHolding[] startingHoldings,
            string[] raisableUnits,
            ScenarioAutoResearch[] autoResearch,
            string[] buildableBuildings)
        {
            Slot = slot;
            FactionId = factionId;
            Controller = controller;
            PersonalityId = personalityId;
            HomeTerritoryKey = homeTerritoryKey;
            HomeTerritoryId = homeTerritoryId;
            HomeTownId = homeTownId;
            StartingManpower = startingManpower;
            StartingBuildings = startingBuildings;
            StartingUnits = startingUnits;
            StartingHoldings = startingHoldings;
            RaisableUnits = raisableUnits;
            AutoResearch = autoResearch;
            BuildableBuildings = buildableBuildings;
        }

        public bool IsAi
        {
            get { return Controller == ScenarioController.Ai; }
        }
    }

    public readonly struct ScenarioMatchRules
    {
        public const string VictoryCaptureEnemyHq = "capture_enemy_hq_territory";
        public const string OwnershipHoldSeat = "hold_seat";
        public const string SimultaneousHqLossDraw = "draw";

        public readonly int AllowedFactionCount;
        public readonly int AllowedAiSeats;
        public readonly string Victory;
        public readonly string TerritoryOwnershipRule;
        public readonly bool HasNeutralPrize;
        public readonly ulong NeutralPrizeTerritoryKey;
        public readonly bool FuelEnabled;
        public readonly string SimultaneousHqLoss;

        internal ScenarioMatchRules(
            int allowedFactionCount,
            int allowedAiSeats,
            string victory,
            string territoryOwnershipRule,
            bool hasNeutralPrize,
            ulong neutralPrizeTerritoryKey,
            bool fuelEnabled,
            string simultaneousHqLoss)
        {
            AllowedFactionCount = allowedFactionCount;
            AllowedAiSeats = allowedAiSeats;
            Victory = victory;
            TerritoryOwnershipRule = territoryOwnershipRule;
            HasNeutralPrize = hasNeutralPrize;
            NeutralPrizeTerritoryKey = neutralPrizeTerritoryKey;
            FuelEnabled = fuelEnabled;
            SimultaneousHqLoss = simultaneousHqLoss;
        }
    }

    public readonly struct ScenarioMapTerritory
    {
        public readonly int TerritoryId;
        public readonly int SeatTownId;
        public readonly ulong StableKey;

        internal ScenarioMapTerritory(int territoryId, int seatTownId, ulong stableKey)
        {
            TerritoryId = territoryId;
            SeatTownId = seatTownId;
            StableKey = stableKey;
        }
    }

    public readonly struct ScenarioDerivedTerritory
    {
        public readonly string Id;
        public readonly int BakeIndex;
        public readonly ulong MapStableKey;
        public readonly int MapTerritoryId;
        public readonly int SeatTownId;
        public readonly string SeatTier;
        public readonly ScenarioPlaceCounts Places;
        public readonly int SceneryPlaces;
        public readonly int DesignerOverridePlaces;
        public readonly ScenarioSiteCounts Sites;

        internal ScenarioDerivedTerritory(
            string id,
            int bakeIndex,
            ulong mapStableKey,
            int mapTerritoryId,
            int seatTownId,
            string seatTier,
            ScenarioPlaceCounts places,
            int sceneryPlaces,
            int designerOverridePlaces,
            ScenarioSiteCounts sites)
        {
            Id = id;
            BakeIndex = bakeIndex;
            MapStableKey = mapStableKey;
            MapTerritoryId = mapTerritoryId;
            SeatTownId = seatTownId;
            SeatTier = seatTier;
            Places = places;
            SceneryPlaces = sceneryPlaces;
            DesignerOverridePlaces = designerOverridePlaces;
            Sites = sites;
        }
    }

    public readonly struct ScenarioPopulationPlace
    {
        public readonly string Place;
        public readonly int MapTownId;
        public readonly ulong TerritoryKey;
        public readonly int Population;
        public readonly string Tier;
        public readonly string PopulationSource;

        internal ScenarioPopulationPlace(
            string place,
            int mapTownId,
            ulong territoryKey,
            int population,
            string tier,
            string populationSource)
        {
            Place = place;
            MapTownId = mapTownId;
            TerritoryKey = territoryKey;
            Population = population;
            Tier = tier;
            PopulationSource = populationSource;
        }
    }

    /// <summary>
    /// Derived place and site totals from the scenario file, checked against the
    /// per-territory rows. Territories are sorted by stable key.
    /// </summary>
    public sealed class ScenarioDerived
    {
        public readonly ScenarioPlaceCounts HoldingCounts;
        public readonly int CapturablePlaces;
        public readonly int CapturablePlacesFromMap;
        public readonly int DesignerOverridePlaces;
        public readonly int SceneryPlaces;
        public readonly int SceneryPlacesPendingPopulationFix;
        public readonly ScenarioSiteCounts ResourceSiteCounts;
        public readonly ScenarioDerivedTerritory[] Territories;
        public readonly ScenarioPopulationPlace[] PopulationOverrides;
        public readonly ScenarioPopulationPlace[] CensusFallbackPlaces;

        internal ScenarioDerived(
            ScenarioPlaceCounts holdingCounts,
            int capturablePlaces,
            int capturablePlacesFromMap,
            int designerOverridePlaces,
            int sceneryPlaces,
            int sceneryPlacesPendingPopulationFix,
            ScenarioSiteCounts resourceSiteCounts,
            ScenarioDerivedTerritory[] territories,
            ScenarioPopulationPlace[] populationOverrides,
            ScenarioPopulationPlace[] censusFallbackPlaces)
        {
            HoldingCounts = holdingCounts;
            CapturablePlaces = capturablePlaces;
            CapturablePlacesFromMap = capturablePlacesFromMap;
            DesignerOverridePlaces = designerOverridePlaces;
            SceneryPlaces = sceneryPlaces;
            SceneryPlacesPendingPopulationFix = sceneryPlacesPendingPopulationFix;
            ResourceSiteCounts = resourceSiteCounts;
            Territories = territories;
            PopulationOverrides = populationOverrides;
            CensusFallbackPlaces = censusFallbackPlaces;
        }
    }
}
