using Wasteland.Sim.Ai;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiSupplyBudgetTests
    {
        [Fact]
        public void ReachableNodeBeyondBudget_ReturnsBudgetExhausted()
        {
            RoadGraph line = LineGraph();
            AiFactionState tight = TestFaction(1);
            AiRoadSupply supply = new AiRoadSupply();
            AiSupplyResult result = supply.DistanceToNearestCm(line, 1, new[] { 3 }, 1, tight);
            Assert.Equal(AiSupplyStatus.BudgetExhausted, result.Status);
            Assert.NotEqual(AiSupplyStatus.Unreachable, result.Status);
            Assert.Equal(1, tight.SupplyBudgetDefers);
        }

        [Fact]
        public void DisconnectedNode_ReturnsUnreachableWithAmpleBudget()
        {
            RoadGraph split = Wrmb.ReadRoadGraph(
                new RoadGraphBuilder()
                    .AddNode(1, 0, 0)
                    .AddNode(2, 1000, 0)
                    .AddNode(4, 9000, 0)
                    .AddEdge(1, 1, 2, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                    .ToBlob());
            AiFactionState roomy = TestFaction(50000);
            AiRoadSupply supply = new AiRoadSupply();
            AiSupplyResult result = supply.DistanceCm(split, 1, 4, roomy);
            Assert.Equal(AiSupplyStatus.Unreachable, result.Status);
            Assert.Equal(0, roomy.SupplyBudgetDefers);
        }

        [Fact]
        public void Attack_DefersDepotWhenSupplySearchBudgetExhausted_ThenPlacesWhenBudgetAllows()
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line(3);
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 1, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            harness.SetBattalions(
                new AiBattalionPin(5, 1000, 0, 1, AiConstants.UnitKindInfantry, false),
                new AiBattalionPin(7, 1000, 0, 1, AiConstants.UnitKindEngineer, false));
            harness.Tick(0);
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                Assert.NotEqual(AiIssuedKind.BuildDepot, harness.Sink.Log[i].Kind);
            }

            Assert.True(harness.Faction.SupplyBudgetDefers > 0);
            int defers = harness.Faction.SupplyBudgetDefers;
            harness.Faction.WorkUnitsPerTick = 50000;
            harness.Tick(1);
            bool built = false;
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                if (harness.Sink.Log[i].Kind == AiIssuedKind.BuildDepot)
                {
                    built = true;
                    break;
                }
            }

            Assert.True(built);
            Assert.Equal(defers, harness.Faction.SupplyBudgetDefers);
        }

        [Fact]
        public void SupplyBudgetDefer_HashIsDeterministic()
        {
            ulong a = RunAndHash(3);
            ulong b = RunAndHash(3);
            Assert.Equal(a, b);
            ulong roomy = RunAndHash(50000);
            Assert.NotEqual(a, roomy);
        }

        private static ulong RunAndHash(int workUnitsPerTick)
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line(workUnitsPerTick);
            harness.SupplyReachCm = 1500;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 1, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 0, 0);
            harness.SetBattalions(
                new AiBattalionPin(5, 1000, 0, 1, AiConstants.UnitKindInfantry, false),
                new AiBattalionPin(7, 1000, 0, 1, AiConstants.UnitKindEngineer, false));
            harness.Tick(0);
            AiOwnedState owned = new AiOwnedState(new[] { harness.Faction }, 1);
            XxHash64Builder hash = new XxHash64Builder();
            owned.HashInto(hash);
            return hash.Digest();
        }

        private static RoadGraph LineGraph()
        {
            return Wrmb.ReadRoadGraph(
                new RoadGraphBuilder()
                    .AddNode(1, 0, 0)
                    .AddNode(2, 1000, 0)
                    .AddNode(3, 2000, 0)
                    .AddEdge(1, 1, 2, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                    .AddEdge(2, 2, 3, 1000, RoadTier.Paved, RoadSurface.Asphalt)
                    .ToBlob());
        }

        private static AiFactionState TestFaction(int workUnitsPerTick)
        {
            return new AiFactionState(
                1,
                "test",
                AiConstants.DifficultyNormal,
                0,
                40,
                4,
                1,
                60,
                8,
                1,
                0,
                workUnitsPerTick,
                4,
                4,
                AiPosture.Turtle,
                new SimRandom(1UL),
                new SimRandom(3UL),
                new SimRandom(5UL),
                new SimRandom(7UL));
        }
    }
}
