using System;
using Wasteland.Sim.Scenario;

namespace Wasteland.Sim.World
{
    /// <summary>
    /// Match setup for one seat. Raisable unit ids keep the caller's order
    /// until the world sorts them for lookup. Strings stay on this setup
    /// record; the sim stores catalog indexes.
    /// </summary>
    public readonly struct SeatSetup
    {
        public readonly int FactionId;
        public readonly int Slot;
        public readonly string FactionKey;
        public readonly int HomeTownId;
        public readonly int HqXCm;
        public readonly int HqYCm;
        public readonly int CurrentTier;
        public readonly int TierResearchCompleteTick;
        public readonly int Manpower;
        public readonly int Materials;
        public readonly int Fuel;
        public readonly bool FuelEnabled;
        public readonly int PopCap;
        public readonly string[] RaisableUnits;

        /// <summary>Authored auto-research order from the scenario. Empty when the seat has none.</summary>
        public readonly ScenarioAutoResearch[] AutoResearch;

        /// <summary>
        /// Authored opening force. Each count is one full-strength battalion
        /// spawned at the HQ when the seat binds. Empty when the seat has none.
        /// </summary>
        public readonly ScenarioUnitCount[] StartingUnits;

        public SeatSetup(
            int factionId,
            int slot,
            string factionKey,
            int homeTownId,
            int hqXCm,
            int hqYCm,
            int currentTier,
            int tierResearchCompleteTick,
            int manpower,
            int materials,
            int fuel,
            bool fuelEnabled,
            int popCap,
            string[] raisableUnits)
            : this(
                factionId,
                slot,
                factionKey,
                homeTownId,
                hqXCm,
                hqYCm,
                currentTier,
                tierResearchCompleteTick,
                manpower,
                materials,
                fuel,
                fuelEnabled,
                popCap,
                raisableUnits,
                Array.Empty<ScenarioAutoResearch>())
        {
        }

        public SeatSetup(
            int factionId,
            int slot,
            string factionKey,
            int homeTownId,
            int hqXCm,
            int hqYCm,
            int currentTier,
            int tierResearchCompleteTick,
            int manpower,
            int materials,
            int fuel,
            bool fuelEnabled,
            int popCap,
            string[] raisableUnits,
            ScenarioAutoResearch[] autoResearch)
            : this(
                factionId,
                slot,
                factionKey,
                homeTownId,
                hqXCm,
                hqYCm,
                currentTier,
                tierResearchCompleteTick,
                manpower,
                materials,
                fuel,
                fuelEnabled,
                popCap,
                raisableUnits,
                autoResearch,
                Array.Empty<ScenarioUnitCount>())
        {
        }

        public SeatSetup(
            int factionId,
            int slot,
            string factionKey,
            int homeTownId,
            int hqXCm,
            int hqYCm,
            int currentTier,
            int tierResearchCompleteTick,
            int manpower,
            int materials,
            int fuel,
            bool fuelEnabled,
            int popCap,
            string[] raisableUnits,
            ScenarioAutoResearch[] autoResearch,
            ScenarioUnitCount[] startingUnits)
        {
            if (factionKey == null)
            {
                throw new ArgumentNullException(nameof(factionKey));
            }

            if (raisableUnits == null)
            {
                throw new ArgumentNullException(nameof(raisableUnits));
            }

            if (autoResearch == null)
            {
                throw new ArgumentNullException(nameof(autoResearch));
            }

            if (startingUnits == null)
            {
                throw new ArgumentNullException(nameof(startingUnits));
            }

            if (popCap < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(popCap));
            }

            FactionId = factionId;
            Slot = slot;
            FactionKey = factionKey;
            HomeTownId = homeTownId;
            HqXCm = hqXCm;
            HqYCm = hqYCm;
            CurrentTier = currentTier;
            TierResearchCompleteTick = tierResearchCompleteTick;
            Manpower = manpower;
            Materials = materials;
            Fuel = fuel;
            FuelEnabled = fuelEnabled;
            PopCap = popCap;
            RaisableUnits = raisableUnits;
            AutoResearch = autoResearch;
            StartingUnits = startingUnits;
        }
    }

    /// <summary>
    /// Per-seat read view. <see cref="TierResearchCompleteTick"/> is -1 when
    /// no research is running. <see cref="VisibleStrength"/> is the sum of
    /// this seat's battalion strength (ppt). Fog is not applied yet.
    /// </summary>
    public readonly struct SeatView
    {
        public readonly int FactionId;
        public readonly int Slot;
        public readonly int HomeTownId;
        public readonly int HqXCm;
        public readonly int HqYCm;
        public readonly int CurrentTier;
        public readonly int TierResearchCompleteTick;
        public readonly int Manpower;
        public readonly int Materials;
        public readonly int PopUsed;
        public readonly int PopCap;
        public readonly int VisibleStrength;

        public SeatView(
            int factionId,
            int slot,
            int homeTownId,
            int hqXCm,
            int hqYCm,
            int currentTier,
            int tierResearchCompleteTick,
            int manpower,
            int materials,
            int popUsed,
            int popCap,
            int visibleStrength)
        {
            FactionId = factionId;
            Slot = slot;
            HomeTownId = homeTownId;
            HqXCm = hqXCm;
            HqYCm = hqYCm;
            CurrentTier = currentTier;
            TierResearchCompleteTick = tierResearchCompleteTick;
            Manpower = manpower;
            Materials = materials;
            PopUsed = popUsed;
            PopCap = popCap;
            VisibleStrength = visibleStrength;
        }
    }

    /// <summary>
    /// One living battalion. <see cref="UnitId"/> is the balance id, empty
    /// when the battalion was spawned without a catalog unit.
    /// </summary>
    public readonly struct BattalionView
    {
        public readonly int Id;
        public readonly int OwnerId;
        public readonly int XCm;
        public readonly int YCm;
        public readonly string UnitId;
        public readonly int StrengthPpt;

        public BattalionView(int id, int ownerId, int xCm, int yCm, string unitId, int strengthPpt)
        {
            Id = id;
            OwnerId = ownerId;
            XCm = xCm;
            YCm = yCm;
            UnitId = unitId ?? string.Empty;
            StrengthPpt = strengthPpt;
        }
    }

    /// <summary>
    /// One raise sitting in a seat's production queue. <see cref="UnitId"/> is the
    /// balance id. The battalion spawns at <see cref="CompleteTick"/>.
    /// </summary>
    public readonly struct ProductionQueueItem
    {
        public readonly string UnitId;
        public readonly int StartTick;
        public readonly int CompleteTick;

        public ProductionQueueItem(string unitId, int startTick, int completeTick)
        {
            UnitId = unitId ?? string.Empty;
            StartTick = startTick;
            CompleteTick = completeTick;
        }
    }

    /// <summary>Reserved, started, or finished supply depot.</summary>
    public enum DepotSiteState : byte
    {
        Reserved = 0,
        UnderConstruction = 1,
        Active = 2
    }

    /// <summary>
    /// One map town. <see cref="ZoneRadiusCm"/> is 0 for every town that is not
    /// its territory's seat. <see cref="Tier"/> is the sim population band, -1 for scenery.
    /// </summary>
    public readonly struct TownView
    {
        public readonly int TownId;
        public readonly int TerritoryId;
        public readonly ulong TerritoryKey;
        public readonly bool IsSeatTown;
        public readonly int XCm;
        public readonly int YCm;
        public readonly int RoadNodeId;
        public readonly int PopulationClass;
        public readonly int Tier;
        public readonly int ZoneRadiusCm;
        public readonly int FortificationLevel;
        public readonly int OwnerFactionId;
        public readonly int CaptureFactionId;
        public readonly int CaptureProgressTicks;
        public readonly int CaptureNeededTicks;
        public readonly int CaptureAbsentTicks;
        public readonly bool Contested;

        public TownView(
            int townId,
            int territoryId,
            ulong territoryKey,
            bool isSeatTown,
            int xCm,
            int yCm,
            int roadNodeId,
            int populationClass,
            int tier,
            int zoneRadiusCm,
            int fortificationLevel,
            int ownerFactionId,
            int captureFactionId,
            int captureProgressTicks,
            int captureNeededTicks,
            int captureAbsentTicks,
            bool contested)
        {
            TownId = townId;
            TerritoryId = territoryId;
            TerritoryKey = territoryKey;
            IsSeatTown = isSeatTown;
            XCm = xCm;
            YCm = yCm;
            RoadNodeId = roadNodeId;
            PopulationClass = populationClass;
            Tier = tier;
            ZoneRadiusCm = zoneRadiusCm;
            FortificationLevel = fortificationLevel;
            OwnerFactionId = ownerFactionId;
            CaptureFactionId = captureFactionId;
            CaptureProgressTicks = captureProgressTicks;
            CaptureNeededTicks = captureNeededTicks;
            CaptureAbsentTicks = captureAbsentTicks;
            Contested = contested;
        }
    }

    /// <summary>One map territory. <see cref="Key"/> is the map stable key.</summary>
    public readonly struct TerritoryView
    {
        public readonly ulong Key;
        public readonly int MapTerritoryId;
        public readonly int SeatTownId;
        public readonly int OwnerFactionId;

        public TerritoryView(ulong key, int mapTerritoryId, int seatTownId, int ownerFactionId)
        {
            Key = key;
            MapTerritoryId = mapTerritoryId;
            SeatTownId = seatTownId;
            OwnerFactionId = ownerFactionId;
        }
    }

    /// <summary>
    /// One depot site. <see cref="StartTick"/> and <see cref="ActiveTick"/> are -1
    /// until the build starts. Territory comes from the map node.
    /// </summary>
    public readonly struct DepotView
    {
        public readonly int DepotId;
        public readonly int OwnerFactionId;
        public readonly int NodeId;
        public readonly ulong TerritoryKey;
        public readonly int BuilderBattalionId;
        public readonly int SiteTick;
        public readonly int StartTick;
        public readonly int ActiveTick;
        public readonly DepotSiteState State;

        public DepotView(
            int depotId,
            int ownerFactionId,
            int nodeId,
            ulong territoryKey,
            int builderBattalionId,
            int siteTick,
            int startTick,
            int activeTick,
            DepotSiteState state)
        {
            DepotId = depotId;
            OwnerFactionId = ownerFactionId;
            NodeId = nodeId;
            TerritoryKey = territoryKey;
            BuilderBattalionId = builderBattalionId;
            SiteTick = siteTick;
            StartTick = startTick;
            ActiveTick = activeTick;
            State = state;
        }
    }

    /// <summary>
    /// Win/loss is reserved. <see cref="IsOver"/> stays false until that pass lands.
    /// </summary>
    public readonly struct MatchResultView
    {
        public readonly bool IsOver;

        public MatchResultView(bool isOver)
        {
            IsOver = isOver;
        }
    }
}
