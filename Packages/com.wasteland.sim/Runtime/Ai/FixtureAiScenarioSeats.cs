using System;
using System.Collections.Generic;
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
        public const string SimultaneousHqLossDraw = "draw";
        public const string AutoResearchTier2 = "tier2";

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
        private readonly string[] _mapDiagnostics;
        private readonly bool _fuelEnabled;
        private readonly string _simultaneousHqLoss;

        private FixtureAiScenarioSeats(
            AiScenarioSeatView[] seats,
            string[] mapDiagnostics,
            bool fuelEnabled,
            string simultaneousHqLoss)
        {
            _seats = seats;
            _mapDiagnostics = mapDiagnostics;
            _fuelEnabled = fuelEnabled;
            _simultaneousHqLoss = simultaneousHqLoss;
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
            return Create(seats, false, SimultaneousHqLossDraw);
        }

        public static FixtureAiScenarioSeats Create(
            AiScenarioSeatView[] seats,
            bool fuelEnabled,
            string simultaneousHqLoss)
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
                string mapped = AiGenericPersonality.Map(copy[i].PersonalityId);
                if (mapped != AiGenericPersonality.ParameterSetId)
                {
                    throw new InvalidDataException("aiPersonality");
                }

                AiGenericPersonality.RecordMappedFrom(diagnostics, copy[i].PersonalityId);
            }

            SortByFaction(copy);
            for (int i = 1; i < copy.Length; i++)
            {
                if (copy[i].FactionId == copy[i - 1].FactionId)
                {
                    throw new InvalidDataException(RuleIdFaction);
                }
            }

            string loss = string.IsNullOrEmpty(simultaneousHqLoss)
                ? SimultaneousHqLossDraw
                : simultaneousHqLoss;
            return new FixtureAiScenarioSeats(copy, diagnostics.ToArray(), fuelEnabled, loss);
        }

        /// <summary>
        /// Adapter from the PR #17 seat/matchRules shape. Catalog strings are
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
                AiAutoResearch research = MapAutoResearch(row.AutoResearch);
                int[] units = MapUnitCatalog(row.RaisableUnits);
                if (research.Tier >= 2)
                {
                    units = AppendUnique(units, AiConstants.UnitKindTierII);
                }

                views[i] = new AiScenarioSeatView(
                    row.Faction,
                    row.AiPersonality,
                    row.HqTownId,
                    row.StartingManpower,
                    units,
                    MapBuildingCatalog(row.BuildableBuildings),
                    research);
            }

            return Create(views, matchRules.FuelEnabled, matchRules.SimultaneousHqLoss);
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

            AiPersonalityProfile remnant = balance.GetPersonalityOrGeneric(AiConstants.PersonalityWarlord);
            AiPersonalityProfile compact = balance.GetPersonalityOrGeneric(AiConstants.PersonalitySettler);
            return Create(
                LaunchSeat(remnant.EconomyFactionId, remnant.Id, map.GoldendaleTownId),
                LaunchSeat(compact.EconomyFactionId, compact.Id, map.HoodRiverTownId));
        }

        public static AiScenarioSeatView LaunchSeat(string factionId, int hqTownId)
        {
            return LaunchSeat(factionId, string.Empty, hqTownId);
        }

        public static AiScenarioSeatView LaunchSeat(string factionId, string personalityId, int hqTownId)
        {
            return new AiScenarioSeatView(
                factionId,
                personalityId,
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
                seat.PersonalityId,
                seat.HqTownId,
                seat.StartingManpower,
                units,
                buildings,
                seat.AutoResearch);
        }

        private static int[] MapUnitCatalog(string[] names)
        {
            if (names == null || names.Length < 1)
            {
                throw new InvalidDataException(RuleIdRaisableUnits);
            }

            int[] ids = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                ids[i] = MapUnitName(names[i]);
            }

            return DedupePositive(ids, RuleIdRaisableUnits);
        }

        private static int MapUnitName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException(RuleIdRaisableUnits);
            }

            if (ContainsToken(name, "tier3"))
            {
                return AiConstants.UnitKindTierIII;
            }

            if (ContainsToken(name, "engineer"))
            {
                return AiConstants.UnitKindEngineer;
            }

            if (ContainsToken(name, "tier2"))
            {
                return AiConstants.UnitKindTierII;
            }

            return AiConstants.UnitKindInfantry;
        }

        private static int[] MapBuildingCatalog(string[] names)
        {
            if (names == null || names.Length < 1)
            {
                throw new InvalidDataException(RuleIdBuildableBuildings);
            }

            int[] ids = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                ids[i] = MapBuildingName(names[i]);
            }

            return DedupePositive(ids, RuleIdBuildableBuildings);
        }

        private static int MapBuildingName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException(RuleIdBuildableBuildings);
            }

            if (ContainsToken(name, "l2") || ContainsToken(name, "level2"))
            {
                return AiConstants.BuildingTagFortL2;
            }

            if (ContainsToken(name, "l1") || ContainsToken(name, "level1") || ContainsToken(name, "wall"))
            {
                return AiConstants.BuildingTagFortL1;
            }

            return AiConstants.BuildingTagDepot;
        }

        private static AiAutoResearch MapAutoResearch(AiMvpAutoResearchItem[] rows)
        {
            if (rows == null || rows.Length < 1)
            {
                return new AiAutoResearch(0, 1);
            }

            int unlock = int.MaxValue;
            int tier = 1;
            for (int i = 0; i < rows.Length; i++)
            {
                AiMvpAutoResearchItem row = rows[i];
                if (row.NotBeforeTick < 0)
                {
                    throw new InvalidDataException(RuleIdUnlockTick);
                }

                if (row.Item == AutoResearchTier2)
                {
                    tier = 2;
                    if (row.NotBeforeTick < unlock)
                    {
                        unlock = row.NotBeforeTick;
                    }
                }
            }

            if (tier < 2)
            {
                return new AiAutoResearch(0, 1);
            }

            return new AiAutoResearch(unlock, tier);
        }

        private static int[] AppendUnique(int[] source, int value)
        {
            if (source == null)
            {
                return new[] { value };
            }

            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == value)
                {
                    return source;
                }
            }

            int[] next = new int[source.Length + 1];
            for (int i = 0; i < source.Length; i++)
            {
                next[i] = source[i];
            }

            next[source.Length] = value;
            return next;
        }

        private static int[] DedupePositive(int[] ids, string ruleId)
        {
            if (ids == null || ids.Length < 1)
            {
                throw new InvalidDataException(ruleId);
            }

            for (int i = 1; i < ids.Length; i++)
            {
                int row = ids[i];
                int j = i - 1;
                while (j >= 0 && ids[j] > row)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }

                ids[j + 1] = row;
            }

            int written = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] < 1)
                {
                    throw new InvalidDataException(ruleId);
                }

                if (written > 0 && ids[i] == ids[written - 1])
                {
                    continue;
                }

                ids[written] = ids[i];
                written++;
            }

            if (written < 1)
            {
                throw new InvalidDataException(ruleId);
            }

            if (written == ids.Length)
            {
                return ids;
            }

            int[] unique = new int[written];
            for (int i = 0; i < written; i++)
            {
                unique[i] = ids[i];
            }

            return unique;
        }

        private static bool ContainsToken(string name, string token)
        {
            int start = 0;
            while (start + token.Length <= name.Length)
            {
                int i = 0;
                while (i < token.Length && name[start + i] == token[i])
                {
                    i++;
                }

                if (i == token.Length)
                {
                    return true;
                }

                start++;
            }

            return false;
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
