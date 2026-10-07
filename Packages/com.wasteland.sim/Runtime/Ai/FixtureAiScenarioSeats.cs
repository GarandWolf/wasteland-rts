using System;
using System.IO;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Synthetic seat adapter for tests and for MatchInit until the Lead's
    /// scenario loader is wired. Construction range checks throw
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    public sealed class FixtureAiScenarioSeats : IAiScenarioSeats
    {
        public const string RuleIdSeats = "seats";
        public const string RuleIdFaction = "faction";
        public const string RuleIdHqTownId = "hqTownId";
        public const string RuleIdStartingManpower = "startingManpower";
        public const string RuleIdRaisableUnits = "raisableUnits";
        public const string RuleIdBuildableBuildings = "buildableBuildings";
        public const string RuleIdUnlockTick = "unlockTick";
        public const string RuleIdTier = "tier";

        public static readonly int[] DefaultRaisableUnits =
        {
            AiConstants.UnitKindInfantry,
            AiConstants.UnitKindEngineer,
            AiConstants.UnitKindTierII
        };

        public static readonly int[] DefaultBuildableBuildings =
        {
            AiConstants.BuildingTagDepot
        };

        public static readonly AiAutoResearch DefaultResearch = new AiAutoResearch(0, 1);

        private readonly AiScenarioSeatView[] _seats;

        private FixtureAiScenarioSeats(AiScenarioSeatView[] seats)
        {
            _seats = seats;
        }

        public int SeatCount
        {
            get { return _seats.Length; }
        }

        public AiScenarioSeatView SeatAt(int index)
        {
            return _seats[index];
        }

        public static FixtureAiScenarioSeats Create(params AiScenarioSeatView[] seats)
        {
            if (seats == null || seats.Length < 1)
            {
                throw new InvalidDataException(RuleIdSeats);
            }

            AiScenarioSeatView[] copy = new AiScenarioSeatView[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                copy[i] = CopyValidated(seats[i]);
            }

            SortByFaction(copy);
            for (int i = 1; i < copy.Length; i++)
            {
                if (copy[i].FactionId == copy[i - 1].FactionId)
                {
                    throw new InvalidDataException(RuleIdFaction);
                }
            }

            return new FixtureAiScenarioSeats(copy);
        }

        /// <summary>
        /// Temporary launch seats from the loaded map's named towns until the
        /// Lead's loader is wired. Town ids come from the map snapshot, not
        /// from Gorge literals in this adapter.
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

            AiPersonalityProfile remnant = balance.GetPersonality(AiConstants.PersonalityWarlord);
            AiPersonalityProfile compact = balance.GetPersonality(AiConstants.PersonalitySettler);
            return Create(
                LaunchSeat(remnant.EconomyFactionId, map.GoldendaleTownId),
                LaunchSeat(compact.EconomyFactionId, map.HoodRiverTownId));
        }

        public static AiScenarioSeatView LaunchSeat(string factionId, int hqTownId)
        {
            return new AiScenarioSeatView(
                factionId,
                hqTownId,
                300,
                DefaultRaisableUnits,
                DefaultBuildableBuildings,
                DefaultResearch);
        }

        public bool TryGetByFaction(string factionId, out AiScenarioSeatView seat)
        {
            seat = default;
            if (factionId == null)
            {
                return false;
            }

            int lo = 0;
            int hi = _seats.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int cmp = string.CompareOrdinal(_seats[mid].FactionId, factionId);
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

        private static AiScenarioSeatView CopyValidated(AiScenarioSeatView seat)
        {
            if (string.IsNullOrEmpty(seat.FactionId))
            {
                throw new InvalidDataException(RuleIdFaction);
            }

            if (seat.HqTownId < 1)
            {
                throw new InvalidDataException(RuleIdHqTownId);
            }

            if (seat.StartingManpower < 0)
            {
                throw new InvalidDataException(RuleIdStartingManpower);
            }

            if (seat.AutoResearch.UnlockTick < 0)
            {
                throw new InvalidDataException(RuleIdUnlockTick);
            }

            if (seat.AutoResearch.Tier < 0)
            {
                throw new InvalidDataException(RuleIdTier);
            }

            int[] units = CopySortedUnique(seat.RaisableUnits, RuleIdRaisableUnits);
            int[] buildings = CopySortedUnique(seat.BuildableBuildings, RuleIdBuildableBuildings);
            return new AiScenarioSeatView(
                seat.FactionId,
                seat.HqTownId,
                seat.StartingManpower,
                units,
                buildings,
                seat.AutoResearch);
        }

        private static int[] CopySortedUnique(int[] source, string ruleId)
        {
            if (source == null || source.Length < 1)
            {
                throw new InvalidDataException(ruleId);
            }

            int[] copy = (int[])source.Clone();
            for (int i = 1; i < copy.Length; i++)
            {
                int row = copy[i];
                int j = i - 1;
                while (j >= 0 && copy[j] > row)
                {
                    copy[j + 1] = copy[j];
                    j--;
                }

                copy[j + 1] = row;
            }

            for (int i = 0; i < copy.Length; i++)
            {
                if (copy[i] < 1)
                {
                    throw new InvalidDataException(ruleId);
                }

                if (i > 0 && copy[i] == copy[i - 1])
                {
                    throw new InvalidDataException(ruleId);
                }
            }

            return copy;
        }

        private static void SortByFaction(AiScenarioSeatView[] seats)
        {
            for (int i = 1; i < seats.Length; i++)
            {
                AiScenarioSeatView row = seats[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(seats[j].FactionId, row.FactionId) > 0)
                {
                    seats[j + 1] = seats[j];
                    j--;
                }

                seats[j + 1] = row;
            }
        }
    }
}
