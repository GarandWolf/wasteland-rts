using System;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiScenarioSeederTests
    {
        [Fact]
        public void Fixture_ReadsFactionHomeTerritoryAndCatalogs()
        {
            FixtureAiScenarioSeats seats = SyntheticSeats();
            Assert.Equal(2, seats.SeatCount);
            Assert.Equal(0UL, seats.NeutralPrizeTerritoryKey);
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView human));
            Assert.Equal(0, human.Slot);
            Assert.Equal(11UL, human.HomeTerritoryKey);
            Assert.Null(human.AiPersonality);
            Assert.Equal(100, human.StartingManpower);
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView ai));
            Assert.Equal(1, ai.Slot);
            Assert.Equal(7UL, ai.HomeTerritoryKey);
            Assert.Equal(250, ai.StartingManpower);
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindInfantry));
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindEngineer));
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindTierII));
            Assert.False(ai.AllowsUnitForTier(AiConstants.UnitKindTierII, 1));
            Assert.True(ai.AllowsUnitForTier(AiConstants.UnitKindTierII, 2));
            Assert.True(ai.AllowsBuilding(AiConstants.BuildingTagDepot));
            Assert.Equal("homestead_compact_settler", ai.AiPersonality);
            Assert.Equal(AiGenericPersonality.ParameterSetId, AiGenericPersonality.Map(ai.AiPersonality));
            Assert.Single(ai.AutoResearch);
            Assert.Equal("tier2", ai.AutoResearch[0].Item);
            Assert.Equal(40, ai.AutoResearch[0].NotBeforeTick);
        }

        [Fact]
        public void Seed_SetsHomeHqAndManpowerFromTerritorySeatTown()
        {
            FixtureAiScenarioSeats seats = SyntheticSeats();
            AiSeatHomeBinder binder = new AiSeatHomeBinder(seats);
            RoadGraph roads = Wrmb.ReadRoadGraph(new RoadGraphBuilder().AddNode(3, 50, 60).ToBlob());
            AiMapSnapshot map = AiMapSnapshot.ForTests(
                new[] { new AiTownRecord(7, 4, 0, 50, 60, 3, 1, 1, 100) },
                new[] { new AiTerritoryRecord(4, 7, 7UL) },
                roads);
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
            AiOwnedState owned = new AiOwnedState(new[] { faction }, 1);
            binder.Seed(owned, map, AiBalanceTablesTests.LoadBalance());
            Assert.Equal(250, faction.Manpower);
            Assert.Equal(4, faction.HomeTerritoryId);
            Assert.Equal(7, faction.HomeTownId);
            Assert.Equal(3, faction.HqNodeId);
            Assert.Equal(2, owned.TownOwnerAt(0));
        }

        [Fact]
        public void Fixture_RejectsOutOfRangeSeatFields()
        {
            InvalidDataException faction = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(Seat(faction: string.Empty)));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdFaction, faction.Message);

            InvalidDataException slot = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(Seat(slot: -1)));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdSlot, slot.Message);

            InvalidDataException home = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(Seat(homeTerritoryKey: 0UL)));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdHomeTerritoryKey, home.Message);

            InvalidDataException manpower = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(Seat(startingManpower: -1)));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdStartingManpower, manpower.Message);

            InvalidDataException units = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(Seat(raisableUnits: new[] { 0, 1 })));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdRaisableUnits, units.Message);

            InvalidDataException notBefore = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(Seat(
                    autoResearch: new[] { new AiAutoResearch("tier2", -1) })));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdNotBeforeTick, notBefore.Message);
        }

        [Fact]
        public void Seed_ThrowsWhenHomeTerritoryKeyMissingFromMap()
        {
            FixtureAiScenarioSeats seats = SyntheticSeats();
            AiSeatHomeBinder binder = new AiSeatHomeBinder(seats);
            RoadGraph roads = Wrmb.ReadRoadGraph(new RoadGraphBuilder().AddNode(3, 50, 60).ToBlob());
            AiMapSnapshot map = AiMapSnapshot.ForTests(
                new[] { new AiTownRecord(99, 4, 0, 50, 60, 3, 1, 1, 100) },
                new[] { new AiTerritoryRecord(4, 99, 11UL) },
                roads);
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
            AiOwnedState owned = new AiOwnedState(new[] { faction }, 1);
            InvalidDataException ex = Assert.Throws<InvalidDataException>(
                () => binder.Seed(owned, map, AiBalanceTablesTests.LoadBalance()));
            Assert.Equal(AiSeatHomeBinder.RuleIdHomeTerritoryKey, ex.Message);
        }

        [Fact]
        public void UnknownPersonality_MapsToNormalAndKeepsAuthoredId()
        {
            const string unknown = "not_a_real_personality";
            FixtureAiScenarioSeats seats = FixtureAiScenarioSeats.Create(Seat(
                faction: "homestead_compact",
                aiPersonality: unknown,
                startingManpower: 250));
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView seat));
            Assert.Equal(unknown, seat.AiPersonality);
            Assert.Equal(AiGenericPersonality.ParameterSetId, AiGenericPersonality.Map(unknown));
            Assert.Equal(AiGenericPersonality.ParameterSetId, AiGenericPersonality.Map("homestead_compact_settler"));
            Assert.Contains(
                AiGenericPersonality.FormatMappedFrom(unknown),
                seats.PersonalityMapDiagnostics);
            AiPersonalityProfile generic = AiBalanceTablesTests.LoadBalance().GetPersonalityOrGeneric(unknown);
            Assert.Equal(AiGenericPersonality.GenericProfileId, generic.Id);
        }

        [Fact]
        public void MvpShapeFixture_MirrorsSeatCatalogsAndMatchRules()
        {
            FixtureAiScenarioSeats seats = FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        0,
                        "alpha",
                        null,
                        11UL,
                        100,
                        new[] { "infantry_a" },
                        new[] { "depot" },
                        Array.Empty<AiMvpAutoResearchItem>()),
                    new AiMvpSeatShape(
                        1,
                        "beta",
                        "unknown_raider_v9",
                        7UL,
                        250,
                        new[] { "infantry_b", "engineer_b", "tier2_b" },
                        new[] { "depot" },
                        new[] { new AiMvpAutoResearchItem("tier2", 40) })
                },
                new AiMvpMatchRulesShape(false, "draw", 3UL));
            Assert.False(seats.FuelEnabled);
            Assert.Equal(FixtureAiScenarioSeats.SimultaneousHqLossDraw, seats.SimultaneousHqLoss);
            Assert.Equal(3UL, seats.NeutralPrizeTerritoryKey);
            Assert.True(seats.TryGetByFaction("beta", out AiScenarioSeatView ai));
            Assert.Equal("unknown_raider_v9", ai.AiPersonality);
            Assert.Equal(7UL, ai.HomeTerritoryKey);
            Assert.Equal(1, ai.Slot);
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindInfantry));
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindEngineer));
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindTierII));
            Assert.False(ai.AllowsUnitForTier(AiConstants.UnitKindTierII, 1));
            Assert.True(ai.AllowsBuilding(AiConstants.BuildingTagDepot));
            Assert.Equal("tier2", ai.AutoResearch[0].Item);
            Assert.Equal(40, ai.AutoResearch[0].NotBeforeTick);
            Assert.Contains(
                AiGenericPersonality.FormatMappedFrom("unknown_raider_v9"),
                seats.PersonalityMapDiagnostics);
            Assert.Contains(
                AiGenericPersonality.FormatMappedFrom(null),
                seats.PersonalityMapDiagnostics);
        }

        internal static FixtureAiScenarioSeats SyntheticSeats()
        {
            return FixtureAiScenarioSeats.Create(
                new AiScenarioSeatView(
                    0,
                    "remnant_command",
                    null,
                    11UL,
                    100,
                    new[] { AiConstants.UnitKindInfantry },
                    new[] { AiConstants.BuildingTagDepot },
                    Array.Empty<AiAutoResearch>()),
                new AiScenarioSeatView(
                    1,
                    "homestead_compact",
                    "homestead_compact_settler",
                    7UL,
                    250,
                    new[]
                    {
                        AiConstants.UnitKindInfantry,
                        AiConstants.UnitKindEngineer,
                        AiConstants.UnitKindTierII
                    },
                    new[] { AiConstants.BuildingTagDepot },
                    new[] { new AiAutoResearch("tier2", 40) }));
        }

        private static AiScenarioSeatView Seat(
            int slot = 0,
            string faction = "beta",
            string? aiPersonality = null,
            ulong homeTerritoryKey = 7UL,
            int startingManpower = 10,
            int[]? raisableUnits = null,
            int[]? buildableBuildings = null,
            AiAutoResearch[]? autoResearch = null)
        {
            return new AiScenarioSeatView(
                slot,
                faction,
                aiPersonality,
                homeTerritoryKey,
                startingManpower,
                raisableUnits ?? FixtureAiScenarioSeats.DefaultRaisableUnits,
                buildableBuildings ?? FixtureAiScenarioSeats.DefaultBuildableBuildings,
                autoResearch ?? FixtureAiScenarioSeats.DefaultResearch);
        }
    }
}
