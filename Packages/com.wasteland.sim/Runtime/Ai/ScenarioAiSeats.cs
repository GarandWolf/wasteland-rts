using System;
using System.Collections.Generic;
using Wasteland.Sim.Scenario;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// <see cref="IAiScenarioSeats"/> from a loaded <see cref="SimScenario"/>.
    /// Field mapping is one-to-one with the loader model. Seats are stored
    /// in slot order. AI code does not parse scenario JSON.
    /// </summary>
    public sealed class ScenarioAiSeats : IAiScenarioSeats
    {
        private readonly AiScenarioSeatView[] _seats;
        private readonly string[] _mapDiagnostics;
        private readonly bool _fuelEnabled;
        private readonly string _simultaneousHqLoss;
        private readonly ulong _neutralPrizeTerritoryKey;

        private ScenarioAiSeats(
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

        public static ScenarioAiSeats From(SimScenario scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            ScenarioSeat[] source = scenario.Seats;
            if (source == null || source.Length < 1)
            {
                throw new InvalidOperationException(FixtureAiScenarioSeats.RuleIdSeats);
            }

            AiMvpSeatShape[] shapes = new AiMvpSeatShape[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                shapes[i] = MapShape(source[i]);
            }

            FixtureAiScenarioSeats mapped = FixtureAiScenarioSeats.FromMvpShape(
                shapes,
                new AiMvpMatchRulesShape(
                    scenario.MatchRules.FuelEnabled,
                    scenario.MatchRules.SimultaneousHqLoss,
                    scenario.MatchRules.NeutralPrizeTerritoryKey));

            AiScenarioSeatView[] bySlot = new AiScenarioSeatView[mapped.SeatCount];
            for (int i = 0; i < mapped.SeatCount; i++)
            {
                bySlot[i] = mapped.SeatAt(i);
            }

            SortBySlot(bySlot);
            string[] diagnostics = new string[mapped.PersonalityMapDiagnostics.Count];
            for (int i = 0; i < diagnostics.Length; i++)
            {
                diagnostics[i] = mapped.PersonalityMapDiagnostics[i];
            }

            return new ScenarioAiSeats(
                bySlot,
                diagnostics,
                mapped.FuelEnabled,
                mapped.SimultaneousHqLoss,
                mapped.NeutralPrizeTerritoryKey);
        }

        public bool TryGetByFaction(string faction, out AiScenarioSeatView seat)
        {
            seat = default;
            if (faction == null)
            {
                return false;
            }

            for (int i = 0; i < _seats.Length; i++)
            {
                if (_seats[i].Faction == faction)
                {
                    seat = _seats[i];
                    return true;
                }
            }

            return false;
        }

        private static AiMvpSeatShape MapShape(ScenarioSeat seat)
        {
            ScenarioAutoResearch[] rows = seat.AutoResearch;
            AiMvpAutoResearchItem[] research = Array.Empty<AiMvpAutoResearchItem>();
            if (rows != null && rows.Length > 0)
            {
                research = new AiMvpAutoResearchItem[rows.Length];
                for (int i = 0; i < rows.Length; i++)
                {
                    research[i] = new AiMvpAutoResearchItem(rows[i].Item, rows[i].NotBeforeTick);
                }
            }

            return new AiMvpSeatShape(
                seat.Slot,
                seat.Faction,
                seat.AiPersonality,
                seat.HomeTerritoryKey,
                seat.StartingManpower,
                seat.RaisableUnits,
                seat.BuildableBuildings,
                research,
                seat.HomeTownId);
        }

        private static void SortBySlot(AiScenarioSeatView[] seats)
        {
            for (int i = 1; i < seats.Length; i++)
            {
                AiScenarioSeatView row = seats[i];
                int j = i - 1;
                while (j >= 0 && seats[j].Slot > row.Slot)
                {
                    seats[j + 1] = seats[j];
                    j--;
                }

                seats[j + 1] = row;
            }
        }
    }
}
