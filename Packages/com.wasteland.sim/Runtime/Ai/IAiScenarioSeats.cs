using System;
using System.IO;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Authored auto-research row. <see cref="NotBeforeTick"/> is the earliest
    /// the item may start, not the actual start or the available tick.
    /// </summary>
    public readonly struct AiAutoResearch
    {
        public readonly string Item;
        public readonly int NotBeforeTick;

        public AiAutoResearch(string item, int notBeforeTick)
        {
            Item = item ?? string.Empty;
            NotBeforeTick = notBeforeTick;
        }
    }

    /// <summary>
    /// One scenario seat. Home is a territory key; the AI finds HQ through
    /// that territory's seat town. Catalogs stay in authored order.
    /// </summary>
    public readonly struct AiScenarioSeatView
    {
        public readonly int Slot;
        public readonly string Faction;
        public readonly string? AiPersonality;
        public readonly ulong HomeTerritoryKey;
        public readonly int StartingManpower;
        public readonly int[] RaisableUnits;
        public readonly int[] BuildableBuildings;
        public readonly AiAutoResearch[] AutoResearch;

        public AiScenarioSeatView(
            int slot,
            string faction,
            string? aiPersonality,
            ulong homeTerritoryKey,
            int startingManpower,
            int[] raisableUnits,
            int[] buildableBuildings,
            AiAutoResearch[] autoResearch)
        {
            Slot = slot;
            Faction = faction;
            AiPersonality = aiPersonality;
            HomeTerritoryKey = homeTerritoryKey;
            StartingManpower = startingManpower;
            RaisableUnits = raisableUnits;
            BuildableBuildings = buildableBuildings;
            AutoResearch = autoResearch ?? Array.Empty<AiAutoResearch>();
        }

        public bool AllowsUnit(int unitKind)
        {
            return Contains(RaisableUnits, unitKind);
        }

        public bool AllowsUnitForTier(int unitKind, int currentTier)
        {
            if (!AllowsUnit(unitKind))
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
            return Contains(BuildableBuildings, buildingKind);
        }

        internal static bool Contains(int[] ids, int value)
        {
            if (ids == null)
            {
                return false;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == value)
                {
                    return true;
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

        ulong NeutralPrizeTerritoryKey { get; }

        System.Collections.Generic.IReadOnlyList<string> PersonalityMapDiagnostics { get; }

        AiScenarioSeatView SeatAt(int index);

        bool TryGetByFaction(string faction, out AiScenarioSeatView seat);
    }

    /// <summary>
    /// Binds each AI faction to its home territory and that territory's seat
    /// town. Load range checks throw <see cref="InvalidDataException"/>.
    /// </summary>
    public sealed class AiSeatHomeBinder
    {
        public const string RuleIdFaction = "faction";
        public const string RuleIdHomeTerritoryKey = "homeTerritoryKey";

        private readonly IAiScenarioSeats _seats;

        public AiSeatHomeBinder(IAiScenarioSeats seats)
        {
            _seats = seats ?? throw new ArgumentNullException(nameof(seats));
        }

        public void Seed(AiOwnedState owned, AiMapSnapshot map, AiBalanceTables balance)
        {
            if (owned == null)
            {
                throw new ArgumentNullException(nameof(owned));
            }

            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            for (int i = 0; i < owned.FactionCount; i++)
            {
                AiFactionState faction = owned.FactionAt(i);
                AiPersonalityProfile personality = balance.GetPersonalityOrGeneric(faction.PersonalityId);
                if (!_seats.TryGetByFaction(personality.EconomyFactionId, out AiScenarioSeatView seat))
                {
                    throw new InvalidDataException(RuleIdFaction);
                }

                if (!map.TryGetTerritoryByStableKey(seat.HomeTerritoryKey, out AiTerritoryRecord territory))
                {
                    throw new InvalidDataException(RuleIdHomeTerritoryKey);
                }

                int townIndex = map.IndexOfTown(territory.SeatTownId);
                if (townIndex < 0)
                {
                    throw new InvalidDataException(RuleIdHomeTerritoryKey);
                }

                AiTownRecord town = map.TownAt(townIndex);
                faction.Manpower = seat.StartingManpower;
                faction.HomeTownId = town.Id;
                faction.HomeTerritoryId = territory.Id;
                faction.HqNodeId = town.RoadNodeId;
                owned.SetTownOwner(townIndex, faction.FactionId);
            }
        }
    }
}
