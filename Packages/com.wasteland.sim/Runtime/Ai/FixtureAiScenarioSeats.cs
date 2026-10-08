using System;
using System.Collections.Generic;
using System.IO;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Synthetic seat adapter for tests. Production MatchInit takes
    /// <see cref="IAiScenarioSeats"/> from <see cref="ScenarioAiSeats"/>.
    /// Construction range checks throw <see cref="InvalidDataException"/>.
    /// </summary>
    public sealed class FixtureAiScenarioSeats : IAiScenarioSeats
    {
        public const string RuleIdSeats = "seats";
        public const string RuleIdFaction = "faction";
        public const string RuleIdSlot = "slot";
        public const string RuleIdHomeTerritoryKey = "homeTerritoryKey";
        public const string RuleIdStartingManpower = "startingManpower";
        public const string RuleIdRaisableUnits = "raisableUnits";
        public const string RuleIdBuildableBuildings = "buildableBuildings";
        public const string RuleIdNotBeforeTick = "notBeforeTick";
        public const string SimultaneousHqLossDraw = "draw";
        public const string AutoResearchTier2 = "tier2";

        public static readonly string[] DefaultRaisableUnits =
        {
            "compact_militia",
            "compact_rifle_battalion",
            "compact_at_battalion",
            "compact_engineers"
        };

        public static readonly string[] RemnantRaisableUnits =
        {
            "remnant_motor_rifles",
            "remnant_mech_battalion",
            "remnant_armoured_battalion",
            "remnant_engineers"
        };

        public static readonly string[] DefaultBuildableBuildings =
        {
            AiConstants.BuildingIdSupplyDepot
        };

        public static readonly AiAutoResearch[] DefaultResearch = Array.Empty<AiAutoResearch>();

        public static readonly AiStartingUnit[] CompactAuthoredStartingUnits =
        {
            new AiStartingUnit("compact_engineers", 1),
            new AiStartingUnit("compact_militia", 4),
            new AiStartingUnit("compact_scouts", 1)
        };

        public static readonly AiStartingUnit[] RemnantAuthoredStartingUnits =
        {
            new AiStartingUnit("remnant_engineers", 1),
            new AiStartingUnit("remnant_motor_rifles", 2),
            new AiStartingUnit("remnant_recon", 1)
        };

        private readonly AiScenarioSeatView[] _seats;
        private readonly string[] _mapDiagnostics;
        private readonly bool _fuelEnabled;
        private readonly string _simultaneousHqLoss;
        private readonly ulong _neutralPrizeTerritoryKey;

        private FixtureAiScenarioSeats(
            AiScenarioSeatView[] seats,
            string[] mapDiagnostics,
            bool fuelEnabled,
            string simultaneousHqLoss,
            ulong neutralPrizeTerritoryKey)
        {
            _seats = seats;
            _mapDiagnostics = mapDiagnostics;
            _fuelEnabled = fuelEnabled;
            _simultaneousHqLoss = simultaneousHqLoss;
            _neutralPrizeTerritoryKey = neutralPrizeTerritoryKey;
        }

        public int SeatCount
        {
            get { return _seats.Length; }
        }

        public bool FuelEnabled
        {
            get { return _fuelEnabled; }
        }

        public string SimultaneousHqLoss
        {
            get { return _simultaneousHqLoss; }
        }

        public ulong NeutralPrizeTerritoryKey
        {
            get { return _neutralPrizeTerritoryKey; }
        }

        public IReadOnlyList<string> PersonalityMapDiagnostics
        {
            get { return _mapDiagnostics; }
        }

        public AiScenarioSeatView SeatAt(int index)
        {
            return _seats[index];
        }

        public static FixtureAiScenarioSeats Create(params AiScenarioSeatView[] seats)
        {
            return Create(seats, false, SimultaneousHqLossDraw, 0UL);
        }

        public static FixtureAiScenarioSeats Create(
            AiScenarioSeatView[] seats,
            bool fuelEnabled,
            string simultaneousHqLoss)
        {
            return Create(seats, fuelEnabled, simultaneousHqLoss, 0UL);
        }

        public static FixtureAiScenarioSeats Create(
            AiScenarioSeatView[] seats,
            bool fuelEnabled,
            string simultaneousHqLoss,
            ulong neutralPrizeTerritoryKey)
        {
            if (seats == null || seats.Length < 1)
            {
                throw new InvalidDataException(RuleIdSeats);
            }

            AiScenarioSeatView[] copy = new AiScenarioSeatView[seats.Length];
            List<string> diagnostics = new List<string>();
            for (int i = 0; i < seats.Length; i++)
            {
                copy[i] = CopyValidated(seats[i]);
                string mapped = AiGenericPersonality.Map(copy[i].AiPersonality);
                if (mapped != AiGenericPersonality.ParameterSetId)
                {
                    throw new InvalidDataException("aiPersonality");
                }

                AiGenericPersonality.RecordMappedFrom(diagnostics, copy[i].AiPersonality);
            }

            SortByFaction(copy);
            for (int i = 1; i < copy.Length; i++)
            {
                if (copy[i].Faction == copy[i - 1].Faction)
                {
                    throw new InvalidDataException(RuleIdFaction);
                }
            }

            string loss = string.IsNullOrEmpty(simultaneousHqLoss)
                ? SimultaneousHqLossDraw
                : simultaneousHqLoss;
            return new FixtureAiScenarioSeats(
                copy,
                diagnostics.ToArray(),
                fuelEnabled,
                loss,
                neutralPrizeTerritoryKey);
        }

        /// <summary>
        /// Adapter from the Designer seat/matchRules shape. Catalog strings are
        /// synthetic; fuel is stored and ignored.
        /// </summary>
        public static FixtureAiScenarioSeats FromMvpShape(
            AiMvpSeatShape[] seats,
            AiMvpMatchRulesShape matchRules)
        {
            if (seats == null || seats.Length < 1)
            {
                throw new InvalidDataException(RuleIdSeats);
            }

            AiScenarioSeatView[] views = new AiScenarioSeatView[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                AiMvpSeatShape row = seats[i];
                AiAutoResearch[] research = MapAutoResearch(row.AutoResearch);
                views[i] = new AiScenarioSeatView(
                    row.Slot,
                    row.Faction,
                    row.AiPersonality,
                    row.HomeTerritoryKey,
                    row.StartingManpower,
                    CopyAuthoredIds(row.RaisableUnits, RuleIdRaisableUnits),
                    CopyAuthoredIds(row.BuildableBuildings, RuleIdBuildableBuildings),
                    research,
                    row.HomeTownId,
                    CopyStartingUnits(row.StartingUnits));
            }

            return Create(
                views,
                matchRules.FuelEnabled,
                matchRules.SimultaneousHqLoss,
                matchRules.NeutralPrizeTerritoryKey);
        }

        /// <summary>
        /// Temporary launch seats from the loaded map's named towns until the
        /// Lead's loader is wired. Territory keys come from the snapshot.
        /// </summary>
        public static FixtureAiScenarioSeats ForLaunchPersonalities(AiMapSnapshot map, AiBalanceTables balance)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            AiPersonalityProfile remnant = balance.GetPersonalityOrGeneric(AiConstants.PersonalityWarlord);
            AiPersonalityProfile compact = balance.GetPersonalityOrGeneric(AiConstants.PersonalitySettler);
            return Create(
                new[]
                {
                    LaunchSeat(
                        0,
                        remnant.EconomyFactionId,
                        remnant.Id,
                        HomeKeyOf(map, map.GoldendaleTownId),
                        RemnantRaisableUnits,
                        map.GoldendaleTownId,
                        new[] { new AiAutoResearch(AutoResearchTier2, 1200) },
                        RemnantAuthoredStartingUnits),
                    LaunchSeat(
                        1,
                        compact.EconomyFactionId,
                        compact.Id,
                        HomeKeyOf(map, map.HoodRiverTownId),
                        DefaultRaisableUnits,
                        map.HoodRiverTownId,
                        DefaultResearch,
                        CompactAuthoredStartingUnits)
                },
                false,
                SimultaneousHqLossDraw,
                HomeKeyOf(map, map.TheDallesTownId));
        }

        public static AiScenarioSeatView LaunchSeat(string faction, ulong homeTerritoryKey)
        {
            return LaunchSeat(0, faction, string.Empty, homeTerritoryKey);
        }

        public static AiScenarioSeatView LaunchSeat(
            int slot,
            string faction,
            string? aiPersonality,
            ulong homeTerritoryKey)
        {
            return LaunchSeat(
                slot,
                faction,
                aiPersonality,
                homeTerritoryKey,
                DefaultRaisableUnits,
                0);
        }

        public static AiScenarioSeatView LaunchSeat(
            int slot,
            string faction,
            string? aiPersonality,
            ulong homeTerritoryKey,
            string[] raisableUnits)
        {
            return LaunchSeat(slot, faction, aiPersonality, homeTerritoryKey, raisableUnits, 0);
        }

        public static AiScenarioSeatView LaunchSeat(
            int slot,
            string faction,
            string? aiPersonality,
            ulong homeTerritoryKey,
            string[] raisableUnits,
            int homeTownId)
        {
            return LaunchSeat(
                slot,
                faction,
                aiPersonality,
                homeTerritoryKey,
                raisableUnits,
                homeTownId,
                DefaultResearch,
                Array.Empty<AiStartingUnit>());
        }

        public static AiScenarioSeatView LaunchSeat(
            int slot,
            string faction,
            string? aiPersonality,
            ulong homeTerritoryKey,
            string[] raisableUnits,
            int homeTownId,
            AiAutoResearch[] research)
        {
            return LaunchSeat(
                slot,
                faction,
                aiPersonality,
                homeTerritoryKey,
                raisableUnits,
                homeTownId,
                research,
                Array.Empty<AiStartingUnit>());
        }

        public static AiScenarioSeatView LaunchSeat(
            int slot,
            string faction,
            string? aiPersonality,
            ulong homeTerritoryKey,
            string[] raisableUnits,
            int homeTownId,
            AiAutoResearch[] research,
            AiStartingUnit[] startingUnits)
        {
            return new AiScenarioSeatView(
                slot,
                faction,
                aiPersonality,
                homeTerritoryKey,
                300,
                raisableUnits,
                DefaultBuildableBuildings,
                research ?? DefaultResearch,
                homeTownId,
                startingUnits);
        }

        public bool TryGetByFaction(string faction, out AiScenarioSeatView seat)
        {
            seat = default;
            if (faction == null)
            {
                return false;
            }

            int lo = 0;
            int hi = _seats.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int cmp = string.CompareOrdinal(_seats[mid].Faction, faction);
                if (cmp == 0)
                {
                    seat = _seats[mid];
                    return true;
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

            return false;
        }

        private static ulong HomeKeyOf(AiMapSnapshot map, int townId)
        {
            if (!map.TryGetTown(townId, out AiTownRecord town))
            {
                throw new InvalidDataException(RuleIdHomeTerritoryKey);
            }

            if (!map.TryGetTerritory(town.TerritoryId, out AiTerritoryRecord territory))
            {
                throw new InvalidDataException(RuleIdHomeTerritoryKey);
            }

            return territory.StableKey;
        }

        private static AiScenarioSeatView CopyValidated(AiScenarioSeatView seat)
        {
            if (string.IsNullOrEmpty(seat.Faction))
            {
                throw new InvalidDataException(RuleIdFaction);
            }

            if (seat.Slot < 0)
            {
                throw new InvalidDataException(RuleIdSlot);
            }

            if (seat.HomeTerritoryKey == 0UL)
            {
                throw new InvalidDataException(RuleIdHomeTerritoryKey);
            }

            if (seat.StartingManpower < 0)
            {
                throw new InvalidDataException(RuleIdStartingManpower);
            }

            AiAutoResearch[] research = CopyResearch(seat.AutoResearch);
            string[] units = CopyAuthoredIds(seat.RaisableUnits, RuleIdRaisableUnits);
            string[] buildings = CopyAuthoredIds(seat.BuildableBuildings, RuleIdBuildableBuildings);
            return new AiScenarioSeatView(
                seat.Slot,
                seat.Faction,
                seat.AiPersonality,
                seat.HomeTerritoryKey,
                seat.StartingManpower,
                units,
                buildings,
                research,
                seat.HomeTownId,
                CopyStartingUnits(seat.StartingUnits));
        }

        private static AiAutoResearch[] CopyResearch(AiAutoResearch[] source)
        {
            if (source == null || source.Length < 1)
            {
                return Array.Empty<AiAutoResearch>();
            }

            AiAutoResearch[] copy = new AiAutoResearch[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i].NotBeforeTick < 0)
                {
                    throw new InvalidDataException(RuleIdNotBeforeTick);
                }

                copy[i] = new AiAutoResearch(source[i].Item, source[i].NotBeforeTick);
            }

            return copy;
        }

        private static AiAutoResearch[] MapAutoResearch(AiMvpAutoResearchItem[] rows)
        {
            if (rows == null || rows.Length < 1)
            {
                return Array.Empty<AiAutoResearch>();
            }

            AiAutoResearch[] copy = new AiAutoResearch[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].NotBeforeTick < 0)
                {
                    throw new InvalidDataException(RuleIdNotBeforeTick);
                }

                copy[i] = new AiAutoResearch(rows[i].Item, rows[i].NotBeforeTick);
            }

            return copy;
        }

        private static AiStartingUnit[] CopyStartingUnits(AiStartingUnit[] source)
        {
            if (source == null || source.Length < 1)
            {
                return Array.Empty<AiStartingUnit>();
            }

            AiStartingUnit[] copy = new AiStartingUnit[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                copy[i] = new AiStartingUnit(source[i].UnitId, source[i].Count);
            }

            return copy;
        }

        private static string[] CopyAuthoredIds(string[] source, string ruleId)
        {
            if (source == null || source.Length < 1)
            {
                throw new InvalidDataException(ruleId);
            }

            string[] copy = new string[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (string.IsNullOrEmpty(source[i]))
                {
                    throw new InvalidDataException(ruleId);
                }

                copy[i] = source[i];
            }

            return copy;
        }

        private static int[] CopyAuthoredPositive(int[] source, string ruleId)
        {
            if (source == null || source.Length < 1)
            {
                throw new InvalidDataException(ruleId);
            }

            int[] copy = new int[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] < 1)
                {
                    throw new InvalidDataException(ruleId);
                }

                copy[i] = source[i];
            }

            return copy;
        }

        private static void SortByFaction(AiScenarioSeatView[] seats)
        {
            for (int i = 1; i < seats.Length; i++)
            {
                AiScenarioSeatView row = seats[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(seats[j].Faction, row.Faction) > 0)
                {
                    seats[j + 1] = seats[j];
                    j--;
                }

                seats[j + 1] = row;
            }
        }
    }
}
