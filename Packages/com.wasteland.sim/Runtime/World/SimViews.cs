using System;

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
        {
            if (factionKey == null)
            {
                throw new ArgumentNullException(nameof(factionKey));
            }

            if (raisableUnits == null)
            {
                throw new ArgumentNullException(nameof(raisableUnits));
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
}
