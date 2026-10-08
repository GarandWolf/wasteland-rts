using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.Scenario;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class ScenarioAiSeatsTests
    {
        private const string ScenarioId = "columbia_gorge_100mi_mvp";
        private const string ScenarioRelative = "data/balance/scenarios/columbia_gorge_100mi_mvp.json";

        [Fact]
        public void FromLoader_ReproducesSeatFieldsAndPrizeKey()
        {
            SimScenario scenario = LoadMvp();
            ScenarioAiSeats seats = ScenarioAiSeats.From(scenario);
            Assert.Equal(scenario.SeatCount, seats.SeatCount);
            Assert.Equal(scenario.MatchRules.FuelEnabled, seats.FuelEnabled);
            Assert.Equal(scenario.MatchRules.SimultaneousHqLoss, seats.SimultaneousHqLoss);
            Assert.Equal(scenario.MatchRules.NeutralPrizeTerritoryKey, seats.NeutralPrizeTerritoryKey);

            for (int i = 0; i < scenario.SeatCount; i++)
            {
                ScenarioSeat model = scenario.SeatAt(i);
                AiScenarioSeatView adapted = seats.SeatAt(i);
                Assert.Equal(model.Slot, adapted.Slot);
                Assert.Equal(i, adapted.Slot);
                Assert.Equal(model.Faction, adapted.Faction);
                Assert.Equal(model.AiPersonality, adapted.AiPersonality);
                Assert.Equal(model.HomeTerritoryKey, adapted.HomeTerritoryKey);
                Assert.Equal(model.StartingManpower, adapted.StartingManpower);
                Assert.Equal(model.AutoResearch.Length, adapted.AutoResearch.Length);
                for (int r = 0; r < model.AutoResearch.Length; r++)
                {
                    Assert.Equal(model.AutoResearch[r].Item, adapted.AutoResearch[r].Item);
                    Assert.Equal(model.AutoResearch[r].NotBeforeTick, adapted.AutoResearch[r].NotBeforeTick);
                }

                Assert.True(seats.TryGetByFaction(model.Faction, out AiScenarioSeatView byFaction));
                Assert.Equal(model.HomeTerritoryKey, byFaction.HomeTerritoryKey);
                AssertCatalogMatchesModel(model, adapted);
            }
        }

        [Fact]
        public void CompactSeat_EmptyResearch_NeverReachesTierII()
        {
            SimScenario scenario = LoadMvp();
            ScenarioAiSeats seats = ScenarioAiSeats.From(scenario);
            AiScenarioSeatView compact = seats.SeatAt(1);
            Assert.Equal(1, compact.Slot);
            Assert.Equal("homestead_compact", compact.Faction);
            Assert.Empty(compact.AutoResearch);
            FixtureAiSeatView view = new FixtureAiSeatView();
            view.Tier2ResearchTicks = AiBalanceTablesTests.LoadBalance().Tier2ResearchTicks;
            view.Refresh(compact, 0);
            Assert.Equal(1, view.CurrentTier);
            Assert.Equal(-1, view.TierResearchCompleteTick);
            view.Refresh(compact, 100000);
            Assert.Equal(1, view.CurrentTier);
            Assert.Equal(-1, view.TierResearchCompleteTick);
            IAiUnitCatalog catalog = AiBalanceTablesTests.LoadBalance().UnitCatalog;
            for (int i = 0; i < compact.RaisableUnits.Length; i++)
            {
                Assert.True(catalog.TryGetTier(compact.RaisableUnits[i], out int tier));
                Assert.Equal(1, tier);
                Assert.True(compact.AllowsUnitForTier(compact.RaisableUnits[i], view.CurrentTier, catalog));
            }
        }

        [Fact]
        public void CompactSeat_FourDistinctIdsAndRoles_TiersMatchCatalog()
        {
            SimScenario scenario = LoadMvp();
            ScenarioAiSeats seats = ScenarioAiSeats.From(scenario);
            ScenarioSeat model = scenario.SeatAt(1);
            AiScenarioSeatView compact = seats.SeatAt(1);
            Assert.Equal(model.RaisableUnits, compact.RaisableUnits);
            Assert.Equal(4, compact.RaisableUnits.Length);
            IAiUnitCatalog catalog = AiBalanceTablesTests.LoadBalance().UnitCatalog;
            string[] roles = new string[compact.RaisableUnits.Length];
            for (int i = 0; i < compact.RaisableUnits.Length; i++)
            {
                string id = compact.RaisableUnits[i];
                Assert.Equal(model.RaisableUnits[i], id);
                Assert.True(catalog.TryGetTier(id, out int tier));
                Assert.Equal(1, tier);
                Assert.True(catalog.TryGetRole(id, out string role));
                roles[i] = role;
            }

            for (int i = 0; i < roles.Length; i++)
            {
                for (int j = i + 1; j < roles.Length; j++)
                {
                    Assert.NotEqual(roles[i], roles[j]);
                }
            }
        }

        [Fact]
        public void Catalogs_KeepAuthoredOrder()
        {
            SimScenario scenario = LoadMvp();
            ScenarioAiSeats seats = ScenarioAiSeats.From(scenario);
            for (int i = 0; i < scenario.SeatCount; i++)
            {
                AssertCatalogMatchesModel(scenario.SeatAt(i), seats.SeatAt(i));
            }
        }

        [Fact]
        public void HumanSeat_AiPersonalityStaysNull()
        {
            SimScenario scenario = LoadMvp();
            ScenarioSeat human = scenario.SeatAt(0);
            Assert.Null(human.AiPersonality);
            AiScenarioSeatView adapted = ScenarioAiSeats.From(scenario).SeatAt(0);
            Assert.Equal(human.Faction, adapted.Faction);
            Assert.Null(adapted.AiPersonality);
        }

        [Fact]
        public void AiSeat_BindsHomeAndHqFromHomeTerritoryKey()
        {
            SimScenario scenario = LoadMvp();
            ScenarioAiSeats seats = ScenarioAiSeats.From(scenario);
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView compact));
            Assert.Equal(scenario.SeatAt(1).HomeTerritoryKey, compact.HomeTerritoryKey);

            byte[] mapBytes = ReadMarshMap();
            AiMapSnapshot map = AiMapSnapshot.FromWrmb(mapBytes);
            Assert.True(map.TryGetTerritoryByStableKey(compact.HomeTerritoryKey, out AiTerritoryRecord territory));
            int townIndex = map.IndexOfTown(territory.SeatTownId);
            Assert.True(townIndex >= 0);
            AiTownRecord town = map.TownAt(townIndex);

            AiFactionState faction = new AiFactionState(
                2,
                AiConstants.PersonalitySettler,
                AiConstants.DifficultyNormal,
                0,
                40,
                4,
                1,
                60,
                8,
                1,
                0,
                50,
                4,
                4,
                AiPosture.Turtle,
                new SimRandom(1UL),
                new SimRandom(3UL),
                new SimRandom(5UL),
                new SimRandom(7UL));
            AiOwnedState owned = new AiOwnedState(new[] { faction }, map.TownCount);
            new AiSeatHomeBinder(seats).Seed(owned, map, AiBalanceTablesTests.LoadBalance());

            ScenarioSeat model = scenario.SeatAt(1);
            Assert.Equal(model.StartingManpower, faction.Manpower);
            Assert.Equal(model.HomeTownId, faction.HomeTownId);
            Assert.Equal(model.HomeTerritoryId, faction.HomeTerritoryId);
            Assert.Equal(town.Id, faction.HomeTownId);
            Assert.Equal(territory.Id, faction.HomeTerritoryId);
            Assert.Equal(town.RoadNodeId, faction.HqNodeId);
            Assert.Equal(2, owned.TownOwnerAt(townIndex));
        }

        private static void AssertCatalogMatchesModel(ScenarioSeat model, AiScenarioSeatView adapted)
        {
            FixtureAiScenarioSeats expected = FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        model.Slot,
                        model.Faction,
                        model.AiPersonality,
                        model.HomeTerritoryKey,
                        model.StartingManpower,
                        model.RaisableUnits,
                        model.BuildableBuildings,
                        MapResearch(model.AutoResearch))
                },
                new AiMvpMatchRulesShape(false, "draw"));
            Assert.True(expected.TryGetByFaction(model.Faction, out AiScenarioSeatView mapped));
            Assert.Equal(mapped.RaisableUnits, adapted.RaisableUnits);
            Assert.Equal(mapped.BuildableBuildings, adapted.BuildableBuildings);
        }

        private static AiMvpAutoResearchItem[] MapResearch(ScenarioAutoResearch[] rows)
        {
            if (rows == null || rows.Length < 1)
            {
                return System.Array.Empty<AiMvpAutoResearchItem>();
            }

            AiMvpAutoResearchItem[] copy = new AiMvpAutoResearchItem[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                copy[i] = new AiMvpAutoResearchItem(rows[i].Item, rows[i].NotBeforeTick);
            }

            return copy;
        }

        private static SimScenario LoadMvp()
        {
            BalanceLoad.SharedPack pack = BalanceLoad.Shared();
            byte[] map = ReadMarshMap();
            return SimScenario.Load(
                GoldenMapFiles.ReadRepoText(ScenarioRelative),
                pack.Scenario,
                map,
                GoldenMapV1MarshTests.MapFileName,
                ScenarioId);
        }

        private static byte[] ReadMarshMap()
        {
            string path = Path.Combine(
                GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapFileName);
            return GoldenMapFiles.ReadWrmb(path, GoldenMapV1MarshTests.MapBytes);
        }
    }
}
