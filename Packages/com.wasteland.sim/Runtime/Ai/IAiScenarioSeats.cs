using System.IO;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Auto-research grant from the scenario seat. Tier II is usable only
    /// after <see cref="UnlockTick"/> plus <see cref="DurationTicks"/> (the
    /// research duration from balance/seat data, not a map constant).
    /// </summary>
    public readonly struct AiAutoResearch
    {
        public readonly int UnlockTick;
        public readonly int Tier;
        public readonly int DurationTicks;

        public AiAutoResearch(int unlockTick, int tier)
            : this(unlockTick, tier, 0)
        {
        }

        public AiAutoResearch(int unlockTick, int tier, int durationTicks)
        {
            UnlockTick = unlockTick;
            Tier = tier;
            DurationTicks = durationTicks;
        }

        public int AvailableTick
        {
            get
            {
                if (Tier < 2)
                {
                    return int.MaxValue;
                }

                return checked(UnlockTick + DurationTicks);
            }
        }
    }

    /// <summary>
    /// One scenario seat. HQ is a town id on the loaded map. Catalogs are
    /// sorted unique positive ids. Wiring to the Lead's loader comes later.
    /// </summary>
    public readonly struct AiScenarioSeatView
    {
        public readonly string FactionId;
        public readonly string PersonalityId;
        public readonly int HqTownId;
        public readonly int StartingManpower;
        public readonly int[] RaisableUnits;
        public readonly int[] BuildableBuildings;
        public readonly AiAutoResearch AutoResearch;

        public AiScenarioSeatView(
            string factionId,
            string personalityId,
            int hqTownId,
            int startingManpower,
            int[] raisableUnits,
            int[] buildableBuildings,
            AiAutoResearch autoResearch)
        {
            FactionId = factionId;
            PersonalityId = personalityId ?? string.Empty;
            HqTownId = hqTownId;
            StartingManpower = startingManpower;
            RaisableUnits = raisableUnits;
            BuildableBuildings = buildableBuildings;
            AutoResearch = autoResearch;
        }

        public int CurrentTierAt(int tick)
        {
            if (AutoResearch.Tier < 2)
            {
                return 1;
            }

            if (tick >= AutoResearch.AvailableTick)
            {
                return AutoResearch.Tier >= 3 ? 3 : 2;
            }

            return 1;
        }

        public bool AllowsUnit(int unitKind, int tick)
        {
            return AllowsUnitForTier(unitKind, CurrentTierAt(tick));
        }

        public bool AllowsUnitForTier(int unitKind, int currentTier)
        {
            if (!ContainsSorted(RaisableUnits, unitKind))
            {
                return false;
            }

            if (unitKind == AiConstants.UnitKindTierII)
            {
                return currentTier >= 2;
            }

            if (unitKind == AiConstants.UnitKindTierIII)
            {
                return currentTier >= 3;
            }

            return true;
        }

        public bool AllowsBuilding(int buildingKind)
        {
            return ContainsSorted(BuildableBuildings, buildingKind);
        }

        internal static bool ContainsSorted(int[] ids, int value)
        {
            if (ids == null)
            {
                return false;
            }

            int lo = 0;
            int hi = ids.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int current = ids[mid];
                if (current == value)
                {
                    return true;
                }

                if (current < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Read-only scenario seats. Replaces <c>IAiHomeSeeder</c>. The Lead's
    /// strict loader will feed this seam; AI code does not parse scenario JSON.
    /// </summary>
    public interface IAiScenarioSeats
    {
        int SeatCount { get; }

        bool FuelEnabled { get; }

        string SimultaneousHqLoss { get; }

        System.Collections.Generic.IReadOnlyList<string> PersonalityMapDiagnostics { get; }

        AiScenarioSeatView SeatAt(int index);

        bool TryGetByFaction(string factionId, out AiScenarioSeatView seat);
    }

    /// <summary>
    /// Binds each AI faction to its seat HQ town. Load range checks throw
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    public sealed class AiSeatHomeBinder
    {
        public const string RuleIdFaction = "faction";
        public const string RuleIdHqTownId = "hqTownId";

        private readonly IAiScenarioSeats _seats;

        public AiSeatHomeBinder(IAiScenarioSeats seats)
        {
            _seats = seats ?? throw new System.ArgumentNullException(nameof(seats));
        }

        public void Seed(AiOwnedState owned, AiMapSnapshot map, AiBalanceTables balance)
        {
            if (owned == null)
            {
                throw new System.ArgumentNullException(nameof(owned));
            }

            if (map == null)
            {
                throw new System.ArgumentNullException(nameof(map));
            }

            if (balance == null)
            {
                throw new System.ArgumentNullException(nameof(balance));
            }

            for (int i = 0; i < owned.FactionCount; i++)
            {
                AiFactionState faction = owned.FactionAt(i);
                AiPersonalityProfile personality = balance.GetPersonalityOrGeneric(faction.PersonalityId);
                if (!_seats.TryGetByFaction(personality.EconomyFactionId, out AiScenarioSeatView seat))
                {
                    throw new InvalidDataException(RuleIdFaction);
                }

                int townIndex = map.IndexOfTown(seat.HqTownId);
                if (townIndex < 0)
                {
                    throw new InvalidDataException(RuleIdHqTownId);
                }

                AiTownRecord town = map.TownAt(townIndex);
                faction.Manpower = seat.StartingManpower;
                faction.HomeTownId = town.Id;
                faction.HomeTerritoryId = town.TerritoryId;
                faction.HqNodeId = town.RoadNodeId;
                owned.SetTownOwner(townIndex, faction.FactionId);
            }
        }
    }
}
