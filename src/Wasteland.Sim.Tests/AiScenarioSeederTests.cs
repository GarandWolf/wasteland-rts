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
        public void Fixture_ReadsFactionHqTownAndCatalogs()
        {
            FixtureAiScenarioSeats seats = SyntheticSeats();
            Assert.Equal(2, seats.SeatCount);
            Assert.True(seats.TryGetByFaction("remnant_command", out AiScenarioSeatView human));
            Assert.Equal(11, human.HqTownId);
            Assert.Equal(100, human.StartingManpower);
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView ai));
            Assert.Equal(7, ai.HqTownId);
            Assert.Equal(250, ai.StartingManpower);
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindInfantry, 0));
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindEngineer, 0));
            Assert.False(ai.AllowsUnit(AiConstants.UnitKindTierII, 0));
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindTierII, 40));
            Assert.True(ai.AllowsBuilding(AiConstants.BuildingTagDepot));
            Assert.Equal("homestead_compact_settler", ai.PersonalityId);
            Assert.Equal(AiGenericPersonality.ParameterSetId, AiGenericPersonality.Map(ai.PersonalityId));
        }

        [Fact]
        public void Seed_SetsHomeHqAndManpowerFromSeatTownId()
        {
            FixtureAiScenarioSeats seats = SyntheticSeats();
            AiSeatHomeBinder binder = new AiSeatHomeBinder(seats);
            RoadGraph roads = Wrmb.ReadRoadGraph(new RoadGraphBuilder().AddNode(3, 50, 60).ToBlob());
            AiMapSnapshot map = AiMapSnapshot.ForTests(
                new[] { new AiTownRecord(7, 4, 0, 50, 60, 3, 1, 1, 100) },
                new[] { new AiTerritoryRecord(4, 7, 0UL) },
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
                () => FixtureAiScenarioSeats.Create(new AiScenarioSeatView(
                    string.Empty,
                    string.Empty,
                    7,
                    10,
                    FixtureAiScenarioSeats.DefaultRaisableUnits,
                    FixtureAiScenarioSeats.DefaultBuildableBuildings,
                    FixtureAiScenarioSeats.DefaultResearch)));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdFaction, faction.Message);

            InvalidDataException hq = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(new AiScenarioSeatView(
                    "beta",
                    string.Empty,
                    0,
                    10,
                    FixtureAiScenarioSeats.DefaultRaisableUnits,
                    FixtureAiScenarioSeats.DefaultBuildableBuildings,
                    FixtureAiScenarioSeats.DefaultResearch)));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdHqTownId, hq.Message);

            InvalidDataException manpower = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(new AiScenarioSeatView(
                    "beta",
                    string.Empty,
                    7,
                    -1,
                    FixtureAiScenarioSeats.DefaultRaisableUnits,
                    FixtureAiScenarioSeats.DefaultBuildableBuildings,
                    FixtureAiScenarioSeats.DefaultResearch)));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdStartingManpower, manpower.Message);

            InvalidDataException units = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(new AiScenarioSeatView(
                    "beta",
                    string.Empty,
                    7,
                    10,
                    new[] { 0, 1 },
                    FixtureAiScenarioSeats.DefaultBuildableBuildings,
                    FixtureAiScenarioSeats.DefaultResearch)));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdRaisableUnits, units.Message);

            InvalidDataException unlock = Assert.Throws<InvalidDataException>(
                () => FixtureAiScenarioSeats.Create(new AiScenarioSeatView(
                    "beta",
                    string.Empty,
                    7,
                    10,
                    FixtureAiScenarioSeats.DefaultRaisableUnits,
                    FixtureAiScenarioSeats.DefaultBuildableBuildings,
                    new AiAutoResearch(-1, 1))));
            Assert.Equal(FixtureAiScenarioSeats.RuleIdUnlockTick, unlock.Message);
        }

        [Fact]
        public void Seed_ThrowsWhenHqTownMissingFromMap()
        {
            FixtureAiScenarioSeats seats = SyntheticSeats();
            AiSeatHomeBinder binder = new AiSeatHomeBinder(seats);
            RoadGraph roads = Wrmb.ReadRoadGraph(new RoadGraphBuilder().AddNode(3, 50, 60).ToBlob());
            AiMapSnapshot map = AiMapSnapshot.ForTests(
                new[] { new AiTownRecord(99, 4, 0, 50, 60, 3, 1, 1, 100) },
                new[] { new AiTerritoryRecord(4, 99, 0UL) },
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
            Assert.Equal(AiSeatHomeBinder.RuleIdHqTownId, ex.Message);
        }

        [Fact]
        public void UnknownPersonality_MapsToNormalAndKeepsAuthoredId()
        {
            const string unknown = "not_a_real_personality";
            FixtureAiScenarioSeats seats = FixtureAiScenarioSeats.Create(new AiScenarioSeatView(
                "homestead_compact",
                unknown,
                7,
                250,
                FixtureAiScenarioSeats.DefaultRaisableUnits,
                FixtureAiScenarioSeats.DefaultBuildableBuildings,
                FixtureAiScenarioSeats.DefaultResearch));
            Assert.True(seats.TryGetByFaction("homestead_compact", out AiScenarioSeatView seat));
            Assert.Equal(unknown, seat.PersonalityId);
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
                        "alpha",
                        string.Empty,
                        11,
                        100,
                        new[] { "infantry_a" },
                        new[] { "depot" },
                        Array.Empty<AiMvpAutoResearchItem>()),
                    new AiMvpSeatShape(
                        "beta",
                        "unknown_raider_v9",
                        7,
                        250,
                        new[] { "infantry_b", "engineer_b", "tier2_b" },
                        new[] { "depot" },
                        new[] { new AiMvpAutoResearchItem("tier2", 40) })
                },
                new AiMvpMatchRulesShape(false, "draw"));
            Assert.False(seats.FuelEnabled);
            Assert.Equal(FixtureAiScenarioSeats.SimultaneousHqLossDraw, seats.SimultaneousHqLoss);
            Assert.True(seats.TryGetByFaction("beta", out AiScenarioSeatView ai));
            Assert.Equal("unknown_raider_v9", ai.PersonalityId);
            Assert.Equal(7, ai.HqTownId);
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindInfantry, 0));
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindEngineer, 0));
            Assert.False(ai.AllowsUnit(AiConstants.UnitKindTierII, 0));
            Assert.True(ai.AllowsUnit(AiConstants.UnitKindTierII, 40));
            Assert.True(ai.AllowsBuilding(AiConstants.BuildingTagDepot));
            Assert.Contains(
                AiGenericPersonality.FormatMappedFrom("unknown_raider_v9"),
                seats.PersonalityMapDiagnostics);
            Assert.Contains(
                AiGenericPersonality.FormatMappedFrom(string.Empty),
                seats.PersonalityMapDiagnostics);
        }

        internal static FixtureAiScenarioSeats SyntheticSeats()
        {
            return FixtureAiScenarioSeats.Create(
                new AiScenarioSeatView(
                    "remnant_command",
                    string.Empty,
                    11,
                    100,
                    new[] { AiConstants.UnitKindInfantry },
                    new[] { AiConstants.BuildingTagDepot },
                    new AiAutoResearch(0, 1)),
                new AiScenarioSeatView(
                    "homestead_compact",
                    "homestead_compact_settler",
                    7,
                    250,
                    new[]
                    {
                        AiConstants.UnitKindInfantry,
                        AiConstants.UnitKindEngineer,
                        AiConstants.UnitKindTierII
                    },
                    new[] { AiConstants.BuildingTagDepot },
                    new AiAutoResearch(40, 2)));
        }
    }
}
