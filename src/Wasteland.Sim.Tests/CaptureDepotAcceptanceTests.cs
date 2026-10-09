using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    /// <summary>
    /// Acceptance coverage from docs/design/mvp-acceptance.md for ClaimTown and BuildDepot.
    /// Towns, territories, and road nodes are read from the marsh map and the MVP scenario.
    /// Misfits: none.
    /// </summary>
    public sealed class CaptureDepotAcceptanceTests
    {
        private const int Remnant = AiConstants.DefaultAiFactionA;
        private const int Compact = AiConstants.DefaultAiFactionB;

        [Fact]
        public void D1_TheDallesForwardDepot_IsAcceptedAndSuppliesHoodRiver()
        {
            SimWorld world = MapWorld();
            int goldNode = HomeNode(world, Remnant);
            int hoodNode = HomeNode(world, Compact);
            TownView dalles = TownByName(world, "The Dalles");
            Capture(world, Remnant, dalles.TownId);
            int node = NearestCoveringNode(dalles.TerritoryId, goldNode, hoodNode, out long fromHq, out long toHood);
            Assert.Equal(4094466L, fromHq);
            Assert.Equal(4599410L, toHood);
            Assert.True(fromHq <= Rules().HqRangeCm);
            Assert.True(RoadDistance(goldNode, hoodNode) > Rules().HqRangeCm);
            Assert.True(toHood <= Rules().DepotRangeCm);

            int engineer = Engineer(world, Remnant);
            world.PlaceBattalion(engineer, 0, 0);
            int issued = world.Tick;
            int before = Materials(world, Remnant);
            int mark = world.Events.Count;
            world.Submit(SimCommand.BuildDepot(issued, Remnant, engineer, node));
            for (int i = 0; i < world.CommandDelayTicks; i++)
            {
                world.Step();
                Assert.Empty(Depots(world));
            }

            world.Step();
            Assert.False(TryRejectionAfter(world, mark, engineer, out _));
            DepotView reserved = OnlyDepot(world);
            Assert.Equal(DepotSiteState.Reserved, reserved.State);
            Assert.Equal(node, reserved.NodeId);
            Assert.Equal(issued + world.CommandDelayTicks, LastKind(world, SimEventKind.DepotStarted).Tick);
            Assert.Equal(before - Rules().DepotMateriel, Materials(world, Remnant));

            Assert.True(Roads().TryGetNodeIndex(node, out int index));
            world.PlaceBattalion(engineer, Roads().GetNodeXCm(index), Roads().GetNodeYCm(index));
            int start = world.Tick;
            world.Step();
            DepotView building = OnlyDepot(world);
            Assert.Equal(DepotSiteState.UnderConstruction, building.State);
            Assert.Equal(start, building.StartTick);
            Assert.Equal(start + Rules().DepotBuildTicks, building.ActiveTick);
            Assert.Equal(900, Rules().DepotBuildTicks);

            Step(world, Rules().DepotBuildTicks);
            DepotView active = OnlyDepot(world);
            Assert.Equal(DepotSiteState.Active, active.State);
            Assert.Equal(building.ActiveTick, active.ActiveTick);
            Assert.Equal(building.ActiveTick, LastKind(world, SimEventKind.DepotActivated).Tick);
            Assert.True(RoadDistance(node, hoodNode) <= Rules().DepotRangeCm);
        }

        [Fact]
        public void D2_WhiteSalmonDepot_IsAccepted()
        {
            SimWorld world = MapWorld();
            TownView salmon = TownByName(world, "White Salmon");
            Capture(world, Remnant, salmon.TownId);
            int node = NearestCoveringNode(
                salmon.TerritoryId,
                HomeNode(world, Remnant),
                HomeNode(world, Compact),
                out long fromHq,
                out long toHood);
            Assert.Equal(2885949L, fromHq);
            Assert.Equal(4785577L, toHood);
            Assert.True(fromHq <= Rules().HqRangeCm);
            Assert.True(toHood <= Rules().DepotRangeCm);

            int engineer = Engineer(world, Remnant);
            PlaceOnNode(world, engineer, node);
            int before = Materials(world, Remnant);
            int mark = world.Events.Count;
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, node));
            Execute(world);
            Assert.False(TryRejectionAfter(world, mark, engineer, out _));
            Assert.Equal(node, OnlyDepot(world).NodeId);
            Assert.Equal(before - Rules().DepotMateriel, Materials(world, Remnant));
        }

        [Fact]
        public void D3_UnownedTerritory_RejectsAndChargesNothing()
        {
            SimWorld neutral = MapWorld();
            TownView dalles = TownByName(neutral, "The Dalles");
            int node = NearestCoveringNode(
                dalles.TerritoryId,
                HomeNode(neutral, Remnant),
                HomeNode(neutral, Compact),
                out _,
                out _);
            AssertRejectedDepot(neutral, Remnant, node, CommandRejectReason.TerritoryNotOwned);

            SimWorld taken = MapWorld();
            TownView prize = TownByName(taken, "The Dalles");
            Capture(taken, Compact, prize.TownId);
            int compactNode = FirstNodeIn(prize.TerritoryId);
            AssertRejectedDepot(taken, Remnant, compactNode, CommandRejectReason.TerritoryNotOwned);
        }

        [Fact]
        public void D4_Arlington_IsOutsideHqSupply()
        {
            SimWorld world = MapWorld();
            TownView arlington = TownByName(world, "Arlington");
            long distance = RoadDistance(HomeNode(world, Remnant), arlington.RoadNodeId);
            Assert.Equal(7448013L, distance);
            Assert.True(distance > Rules().HqRangeCm);
            Capture(world, Remnant, arlington.TownId);
            Assert.Empty(Depots(world));
            AssertRejectedDepot(world, Remnant, arlington.RoadNodeId, CommandRejectReason.NotConnectedToSupply);
        }

        [Fact]
        public void D5_SpacingBoundary_ExactPasses_OneShortIsRejected()
        {
            Assert.Equal(804670, Rules().DepotMinSpacingCm);
            int spacing = Rules().DepotMinSpacingCm;
            int remnantHq = HomeNodeId(Remnant);
            int compactHq = HomeNodeId(Compact);
            int remnantTerritory = HomeTerritoryId(Remnant);
            int compactTerritory = HomeTerritoryId(Compact);

            SimWorld pass = SpacingWorld(
                remnantHq,
                compactHq,
                remnantTerritory,
                compactTerritory,
                spacing,
                out int site,
                out int blocker);
            AcceptDepot(pass, Compact, blocker);
            int before = Materials(pass, Remnant);
            int mark = pass.Events.Count;
            int engineer = Engineer(pass, Remnant);
            PlaceOnNode(pass, engineer, site);
            pass.Submit(SimCommand.BuildDepot(pass.Tick, Remnant, engineer, site));
            Execute(pass);
            Assert.False(TryRejectionAfter(pass, mark, engineer, out _));
            Assert.Equal(before - Rules().DepotMateriel, Materials(pass, Remnant));
            Assert.Equal(Compact, DepotOn(pass, blocker).OwnerFactionId);
            Assert.Equal(site, DepotOn(pass, site).NodeId);

            SimWorld fail = SpacingWorld(
                remnantHq,
                compactHq,
                remnantTerritory,
                compactTerritory,
                spacing - 1,
                out site,
                out blocker);
            AcceptDepot(fail, Compact, blocker);
            AssertRejectedDepot(fail, Remnant, site, CommandRejectReason.DepotTooClose);
            Assert.Equal(Compact, DepotOn(fail, blocker).OwnerFactionId);
        }

        [Fact]
        public void D6_SpacingIgnoresTheHq()
        {
            SimWorld atHq = MapWorld();
            int hq = HomeNode(atHq, Remnant);
            Assert.Equal(0L, RoadDistance(hq, hq));
            Assert.True(RoadDistance(hq, hq) < Rules().DepotMinSpacingCm);
            AcceptDepot(atHq, Remnant, hq);

            SimWorld near = MapWorld();
            int home = HomeNode(near, Remnant);
            int territory = Town(near, HomeTownId(near, Remnant)).TerritoryId;
            int node = ClosestOtherNode(territory, home, out long distance);
            Assert.True(distance > 0L);
            Assert.True(distance < Rules().DepotMinSpacingCm);
            AcceptDepot(near, Remnant, node);
        }

        [Fact]
        public void D7_OnePerNode_CountsEitherOwnerUnderConstruction()
        {
            AssertOnePerNode(Remnant);
            AssertOnePerNode(Compact);
        }

        [Fact]
        public void D8_SixteenthDepot_IsAccepted_SeventeenthIsRejected()
        {
            Assert.Equal(16, Rules().MaxDepotsPerFaction);
            int hq = HomeNodeId(Remnant);
            int territory = HomeTerritoryId(Remnant);
            int[] leaves = ExtraNodeIds(17, hq, HomeNodeId(Compact));
            GNode[] nodes = new GNode[leaves.Length + 1];
            GEdge[] edges = new GEdge[leaves.Length];
            nodes[0] = NodeAt(hq, territory);
            int spacing = Rules().DepotMinSpacingCm;
            for (int i = 0; i < leaves.Length; i++)
            {
                nodes[i + 1] = NodeAt(leaves[i], territory);
                edges[i] = Edge(i + 1, hq, leaves[i], spacing);
            }

            SimWorld world = Synthetic(MakeGraph(nodes, edges));
            int engineer = Engineer(world, Remnant);
            for (int i = 0; i < 16; i++)
            {
                int before = Materials(world, Remnant);
                PlaceOnNode(world, engineer, leaves[i]);
                int mark = world.Events.Count;
                world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, leaves[i]));
                Execute(world);
                Assert.False(TryRejectionAfter(world, mark, engineer, out _));
                Assert.Equal(i + 1, Depots(world).Count);
                Assert.Equal(before - Rules().DepotMateriel, Materials(world, Remnant));
                Assert.Equal(DepotSiteState.UnderConstruction, DepotOn(world, leaves[i]).State);
            }

            int held = Materials(world, Remnant);
            int events = world.Events.Count;
            PlaceOnNode(world, engineer, leaves[16]);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, leaves[16]));
            Execute(world);
            Assert.True(TryRejectionAfter(world, events, engineer, out CommandRejectReason reason));
            Assert.Equal(CommandRejectReason.DepotCapReached, reason);
            Assert.Equal(16, Depots(world).Count);
            Assert.Equal(held, Materials(world, Remnant));
        }

        [Fact]
        public void D9_PlacementIsCheckedWhenTheCommandExecutes()
        {
            Assert.Equal(2, RulesCommandDelay());
            int hq = HomeNodeId(Remnant);
            int territory = HomeTerritoryId(Remnant);
            int[] pair = ExtraNodeIds(2, hq, HomeNodeId(Compact));
            SimWorld raced = Synthetic(ClosePairGraph(hq, territory, pair[0], pair[1], Rules().DepotMinSpacingCm - 1));
            int engineerA = Engineer(raced, Remnant);
            int engineerB = SpawnBuilder(raced, Remnant);
            Assert.Equal(CommandRejectReason.None, raced.PreviewDepotPlacement(Remnant, pair[0], out _, out _));
            Assert.Equal(CommandRejectReason.None, raced.PreviewDepotPlacement(Remnant, pair[1], out _, out _));
            int issued = raced.Tick;
            int before = Materials(raced, Remnant);
            raced.Submit(SimCommand.BuildDepot(issued, Remnant, engineerA, pair[0]));
            raced.Submit(SimCommand.BuildDepot(issued, Remnant, engineerB, pair[1]));
            Execute(raced);
            Assert.Equal(pair[0], OnlyDepot(raced).NodeId);
            Assert.True(TryRejectionAfter(raced, 0, engineerB, out CommandRejectReason tooClose));
            Assert.Equal(CommandRejectReason.DepotTooClose, tooClose);
            Assert.Equal(before - Rules().DepotMateriel, Materials(raced, Remnant));

            SimWorld cleared = Synthetic(ClosePairGraph(hq, territory, pair[0], pair[1], Rules().DepotMinSpacingCm - 1));
            engineerA = Engineer(cleared, Remnant);
            PlaceFar(cleared, engineerA);
            cleared.Submit(SimCommand.BuildDepot(cleared.Tick, Remnant, engineerA, pair[0]));
            Execute(cleared);
            Assert.Equal(DepotSiteState.Reserved, OnlyDepot(cleared).State);
            Assert.Equal(CommandRejectReason.DepotTooClose, cleared.PreviewDepotPlacement(Remnant, pair[1], out _, out _));
            engineerB = SpawnBuilder(cleared, Remnant);
            int stock = Materials(cleared, Remnant);
            int tick = cleared.Tick;
            cleared.Submit(SimCommand.Move(tick, Remnant, engineerA, 1000, 1000));
            cleared.Submit(SimCommand.BuildDepot(tick, Remnant, engineerB, pair[1]));
            Execute(cleared);
            Assert.Equal(pair[1], OnlyDepot(cleared).NodeId);
            Assert.Equal(stock, Materials(cleared, Remnant));
            Assert.Equal(pair[0], LastKind(cleared, SimEventKind.OrderRefunded).Param0);

            SimWorld lost = MapWorld();
            TownView dalles = TownByName(lost, "The Dalles");
            Capture(lost, Remnant, dalles.TownId);
            Silence(lost);
            int forward = NearestCoveringNode(
                dalles.TerritoryId,
                HomeNode(lost, Remnant),
                HomeNode(lost, Compact),
                out _,
                out _);
            TownView seat = Town(lost, dalles.TownId);
            SpawnCapturer(lost, Compact, seat.XCm, seat.YCm);
            bool armed = false;
            for (int i = 0; i < seat.CaptureNeededTicks + 5; i++)
            {
                lost.Step();
                Assert.True(lost.TryGetTown(dalles.TownId, out seat));
                if (seat.CaptureProgressTicks >= seat.CaptureNeededTicks && seat.OwnerFactionId == Remnant)
                {
                    armed = true;
                    break;
                }
            }

            Assert.True(armed);
            Assert.Equal(CommandRejectReason.None, lost.PreviewDepotPlacement(Remnant, forward, out _, out _));
            int builder = Engineer(lost, Remnant);
            int materiel = Materials(lost, Remnant);
            int from = lost.Events.Count;
            lost.Submit(SimCommand.BuildDepot(lost.Tick, Remnant, builder, forward));
            Execute(lost);
            Assert.True(lost.TryGetTown(dalles.TownId, out seat));
            Assert.Equal(Compact, seat.OwnerFactionId);
            Assert.Empty(Depots(lost));
            Assert.True(TryRejectionAfter(lost, from, builder, out CommandRejectReason lostTerritory));
            Assert.Equal(CommandRejectReason.TerritoryNotOwned, lostTerritory);
            Assert.Equal(materiel, Materials(lost, Remnant));
        }

        [Fact]
        public void D10_NonBuilder_IsRejectedAndChargesNothing()
        {
            SimWorld world = MapWorld();
            int node = HomeNode(world, Remnant);
            int capturer = SpawnCapturer(world, Remnant, 0, 0);
            AssertRejectedDepot(world, Remnant, node, CommandRejectReason.NotBuilder, capturer);
        }

        [Fact]
        public void D13_ExactRange_IsAccepted_OneCentimeterFartherIsRejected()
        {
            CaptureBalance rules = Rules();
            Assert.Equal(6437376, rules.HqRangeCm);
            Assert.Equal(4828032, rules.DepotRangeCm);
            int hq = HomeNodeId(Remnant);
            int territory = HomeTerritoryId(Remnant);
            int[] ids = ExtraNodeIds(4, hq, HomeNodeId(Compact));
            int exactHq = ids[0];
            int overHq = ids[1];
            int exactDepot = ids[2];
            int overDepot = ids[3];
            RoadGraph graph = MakeGraph(
                new[]
                {
                    NodeAt(hq, territory),
                    NodeAt(exactHq, territory),
                    NodeAt(overHq, territory),
                    NodeAt(exactDepot, territory),
                    NodeAt(overDepot, territory)
                },
                new[]
                {
                    Edge(1, hq, exactHq, rules.HqRangeCm),
                    Edge(2, hq, overHq, rules.HqRangeCm + 1),
                    Edge(3, exactHq, exactDepot, rules.DepotRangeCm),
                    Edge(4, exactHq, overDepot, rules.DepotRangeCm + 1)
                });

            SimWorld hqRange = Synthetic(graph);
            AssertRejectedDepot(hqRange, Remnant, overHq, CommandRejectReason.NotConnectedToSupply);
            AcceptDepot(hqRange, Remnant, exactHq);

            SimWorld depotRange = Synthetic(graph);
            int engineer = Engineer(depotRange, Remnant);
            PlaceOnNode(depotRange, engineer, exactHq);
            depotRange.Submit(SimCommand.BuildDepot(depotRange.Tick, Remnant, engineer, exactHq));
            Execute(depotRange);
            Assert.Equal(DepotSiteState.UnderConstruction, DepotOn(depotRange, exactHq).State);
            Assert.Equal(
                DepotOn(depotRange, exactHq).StartTick + rules.DepotBuildTicks,
                DepotOn(depotRange, exactHq).ActiveTick);
            StepUntilActive(depotRange, exactHq);

            int before = Materials(depotRange, Remnant);
            PlaceOnNode(depotRange, engineer, exactDepot);
            int mark = depotRange.Events.Count;
            depotRange.Submit(SimCommand.BuildDepot(depotRange.Tick, Remnant, engineer, exactDepot));
            Execute(depotRange);
            Assert.False(TryRejectionAfter(depotRange, mark, engineer, out _));
            Assert.Equal(exactDepot, DepotOn(depotRange, exactDepot).NodeId);
            Assert.Equal(before - rules.DepotMateriel, Materials(depotRange, Remnant));

            int held = Materials(depotRange, Remnant);
            int second = SpawnBuilder(depotRange, Remnant);
            PlaceOnNode(depotRange, second, overDepot);
            int rejectFrom = depotRange.Events.Count;
            depotRange.Submit(SimCommand.BuildDepot(depotRange.Tick, Remnant, second, overDepot));
            Execute(depotRange);
            Assert.True(TryRejectionAfter(depotRange, rejectFrom, second, out CommandRejectReason reason));
            Assert.Equal(CommandRejectReason.NotConnectedToSupply, reason);
            Assert.Equal(held, Materials(depotRange, Remnant));
            Assert.Equal(DepotSiteState.Active, DepotOn(depotRange, exactHq).State);
        }

        [Fact]
        public void K2_DestroyedDepot_FreesSpacingNodeAndCap()
        {
            SimWorld world = MapWorld();
            TownView dalles = TownByName(world, "The Dalles");
            Capture(world, Remnant, dalles.TownId);
            int forward = NearestCoveringNode(
                dalles.TerritoryId,
                HomeNode(world, Remnant),
                HomeNode(world, Compact),
                out _,
                out _);
            int neighbor = ClosestOtherNode(dalles.TerritoryId, forward, out long spacing);
            Assert.True(spacing > 0L);
            Assert.True(spacing < Rules().DepotMinSpacingCm);
            AcceptDepot(world, Remnant, forward);
            Assert.Equal(
                CommandRejectReason.DepotTooClose,
                world.PreviewDepotPlacement(Remnant, neighbor, out _, out _));

            int materiel = Materials(world, Remnant);
            Silence(world);
            TownView seat = Town(world, dalles.TownId);
            SpawnCapturer(world, Compact, seat.XCm, seat.YCm);
            StepUntil(world, seat.CaptureNeededTicks + 5, delegate
            {
                return world.TryGetTown(dalles.TownId, out TownView now) && now.OwnerFactionId == Compact;
            });
            Assert.Empty(Depots(world));
            Assert.Equal(materiel, Materials(world, Remnant));
            Assert.NotEqual(
                CommandRejectReason.NodeHasDepot,
                world.PreviewDepotPlacement(Compact, forward, out _, out _));
            Assert.NotEqual(
                CommandRejectReason.DepotTooClose,
                world.PreviewDepotPlacement(Compact, neighbor, out _, out _));
            Assert.NotEqual(
                CommandRejectReason.DepotCapReached,
                world.PreviewDepotPlacement(Compact, forward, out _, out _));
            AcceptDepot(world, Compact, forward);
        }

        [Fact]
        public void C1a_GoldendaleFlipsAtAPlus1500()
        {
            SimWorld world = MapWorld();
            TownView gold = TownByName(world, "Goldendale");
            Assert.Equal(Remnant, gold.OwnerFactionId);
            Assert.Equal(1, gold.FortificationLevel);
            int needed = gold.CaptureNeededTicks;
            Assert.Equal(Rules().CaptureTicks(gold.Tier, gold.FortificationLevel), needed);
            Assert.Equal(1500, needed);
            Silence(world);
            int a = world.Tick;
            SpawnCapturer(world, Compact, gold.XCm, gold.YCm);
            while (world.Tick < a + needed)
            {
                world.Step();
            }

            Assert.Equal(a + needed, world.Tick);
            Assert.True(world.TryGetTown(gold.TownId, out TownView pending));
            Assert.Equal(Remnant, pending.OwnerFactionId);
            world.Step();
            SimEvent flip = LastKind(world, SimEventKind.TownOwnerChanged);
            Assert.Equal(a + needed, flip.Tick);
            Assert.Equal(gold.TownId, flip.SubjectId);
            Assert.Equal(Compact, flip.AuxId);
            Assert.Equal(Remnant, flip.Param0);
            Assert.True(world.TryGetTown(gold.TownId, out TownView owned));
            Assert.Equal(Compact, owned.OwnerFactionId);
        }

        [Fact]
        public void C4_ContestPause_Completes3400AfterStart()
        {
            SimWorld world = MapWorld();
            TownView hood = TownByName(world, "Hood River");
            Assert.Equal(Rules().CaptureTicks(hood.Tier, hood.FortificationLevel), hood.CaptureNeededTicks);
            Assert.Equal(3000, hood.CaptureNeededTicks);
            Silence(world);
            int a = world.Tick;
            SpawnCapturer(world, Remnant, hood.XCm, hood.YCm);
            Step(world, 2000);
            Assert.True(world.TryGetTown(hood.TownId, out TownView running));
            Assert.Equal(a + 2000, world.Tick);
            Assert.Equal(2000, running.CaptureProgressTicks);
            Assert.Equal(Remnant, running.CaptureFactionId);
            Assert.False(running.Contested);

            int pauser = SpawnContester(world, Compact, hood.XCm, hood.YCm);
            Step(world, 400);
            Assert.True(world.TryGetTown(hood.TownId, out TownView paused));
            Assert.Equal(2000, paused.CaptureProgressTicks);
            Assert.True(paused.Contested);
            Assert.Equal(Remnant, paused.CaptureFactionId);
            world.SetBattalionStrength(pauser, 0);
            while (world.Tick < a + 3400)
            {
                world.Step();
                Assert.True(world.TryGetTown(hood.TownId, out TownView waiting));
                Assert.Equal(Compact, waiting.OwnerFactionId);
            }

            Assert.Equal(a + 3400, world.Tick);
            world.Step();
            SimEvent flip = LastKind(world, SimEventKind.TownOwnerChanged);
            Assert.Equal(a + 3400, flip.Tick);
            Assert.Equal(hood.TownId, flip.SubjectId);
            Assert.Equal(Remnant, flip.AuxId);
            Assert.True(world.TryGetTown(hood.TownId, out TownView taken));
            Assert.Equal(Remnant, taken.OwnerFactionId);
        }

        [Fact]
        public void ContestOnlyUnit_PausesTheTimer()
        {
            SimWorld world = MapWorld();
            TownView dalles = TownByName(world, "The Dalles");
            Silence(world);
            SpawnCapturer(world, Compact, dalles.XCm, dalles.YCm);
            Step(world, 40);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView running));
            Assert.Equal(40, running.CaptureProgressTicks);
            Assert.Equal(Compact, running.CaptureFactionId);
            Assert.False(running.Contested);

            int drone = SpawnContestOnly(world, Remnant, dalles.XCm, dalles.YCm);
            Step(world, 15);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView paused));
            Assert.Equal(40, paused.CaptureProgressTicks);
            Assert.True(paused.Contested);
            Assert.Equal(Compact, paused.CaptureFactionId);
            world.SetBattalionStrength(drone, 0);
            world.Step();
            Assert.True(world.TryGetTown(dalles.TownId, out TownView resumed));
            Assert.Equal(41, resumed.CaptureProgressTicks);
            Assert.False(resumed.Contested);
            Assert.Equal(Compact, resumed.CaptureFactionId);
        }

        [Fact]
        public void ClaimTown_RejectsNonCapturer_AndTownAlreadyOwned()
        {
            SimWorld world = MapWorld();
            TownView dalles = TownByName(world, "The Dalles");
            int engineer = Engineer(world, Remnant);
            int mark = world.Events.Count;
            world.Submit(SimCommand.ClaimTown(world.Tick, Remnant, engineer, dalles.TownId));
            Execute(world);
            Assert.True(TryRejectionAfter(world, mark, engineer, out CommandRejectReason notCapturer));
            Assert.Equal(CommandRejectReason.NotCapturer, notCapturer);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView stillNeutral));
            Assert.Equal(0, stillNeutral.OwnerFactionId);

            TownView gold = TownByName(world, "Goldendale");
            int capturer = SpawnCapturer(world, Remnant, gold.XCm, gold.YCm);
            mark = world.Events.Count;
            world.Submit(SimCommand.ClaimTown(world.Tick, Remnant, capturer, gold.TownId));
            Execute(world);
            Assert.True(TryRejectionAfter(world, mark, capturer, out CommandRejectReason owned));
            Assert.Equal(CommandRejectReason.AlreadyOwned, owned);
            Assert.True(world.TryGetTown(gold.TownId, out TownView stillHome));
            Assert.Equal(Remnant, stillHome.OwnerFactionId);
        }

        [Fact]
        public void PopCap_DropsWhenAHoldingIsLost()
        {
            SimWorld world = MapWorld();
            Assert.Equal(37, Pop(world, Remnant));
            TownView dalles = TownByName(world, "The Dalles");
            int bonus = HoldingBonus(world, dalles.TerritoryId);
            Assert.Equal(10, bonus);
            Capture(world, Remnant, dalles.TownId);
            Assert.Equal(47, Pop(world, Remnant));
            Silence(world);
            TownView seat = Town(world, dalles.TownId);
            SpawnCapturer(world, Compact, seat.XCm, seat.YCm);
            StepUntil(world, seat.CaptureNeededTicks + 5, delegate
            {
                return world.TryGetTown(dalles.TownId, out TownView now) && now.OwnerFactionId == Compact;
            });
            Assert.Equal(37, Pop(world, Remnant));
            Assert.Equal(39 + bonus, Pop(world, Compact));
        }

        [Fact]
        public void AntelopeAndShaniko_HaveNoTier_AndDoNotCount()
        {
            Ensure();
            NamedTown antelope = FindTown("Antelope");
            NamedTown shaniko = FindTown("Shaniko");
            Assert.Equal(1, antelope.Id);
            Assert.Equal(47, antelope.Population);
            Assert.Equal(3, shaniko.Id);
            Assert.Equal(37, shaniko.Population);
            Assert.Equal(-1, Rules().TierForPopulation(antelope.Population));
            Assert.Equal(-1, Rules().TierForPopulation(shaniko.Population));

            SimWorld world = MapWorld();
            Assert.True(world.TryGetTown(antelope.Id, out TownView antelopeView));
            Assert.True(world.TryGetTown(shaniko.Id, out TownView shanikoView));
            Assert.Equal(-1, antelopeView.Tier);
            Assert.Equal(-1, shanikoView.Tier);
            Assert.False(antelopeView.IsSeatTown);
            Assert.False(shanikoView.IsSeatTown);
            Assert.Equal(antelopeView.TerritoryId, shanikoView.TerritoryId);
            AssertPlaceCounts(world);

            int territory = antelopeView.TerritoryId;
            int bonus = HoldingBonus(world, territory);
            int tiered = 0;
            List<TownView> towns = new List<TownView>();
            world.CopyTowns(towns);
            for (int i = 0; i < towns.Count; i++)
            {
                if (towns[i].TerritoryId == territory && towns[i].Tier >= 0)
                {
                    tiered++;
                }
            }

            Assert.True(tiered > 0);
            Assert.Equal(bonus, HoldingBonus(world, territory));
            int before = Pop(world, Remnant);
            TownView seat = SeatOf(world, territory);
            Capture(world, Remnant, seat.TownId);
            Assert.Equal(before + bonus, Pop(world, Remnant));
            Assert.True(world.TryGetTown(antelope.Id, out antelopeView));
            Assert.True(world.TryGetTown(shaniko.Id, out shanikoView));
            Assert.Equal(-1, antelopeView.Tier);
            Assert.Equal(-1, shanikoView.Tier);
            Assert.Equal(Remnant, antelopeView.OwnerFactionId);
        }

        [Fact]
        public void Host_TakingTheDalles_AddsTenToThePopCap()
        {
            Ensure();
            byte[] mapBytes = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { Remnant, Compact };
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(23UL),
                mapBytes,
                factions,
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            Assert.Equal(37, Pop(host.World, Remnant));
            Assert.Equal(39, Pop(host.World, Compact));
            int dallesId = host.Ai.Map.TheDallesTownId;
            Assert.True(host.World.TryGetTown(dallesId, out TownView dalles));
            int bonus = HoldingBonus(host.World, dalles.TerritoryId);
            Assert.Equal(10, bonus);
            int capturer = FirstCapturer(host.World, Remnant);
            host.World.PlaceBattalion(capturer, dalles.XCm, dalles.YCm);
            StepUntil(host.World, dalles.CaptureNeededTicks + 5, delegate
            {
                return host.World.TryGetTown(dallesId, out TownView now) && now.OwnerFactionId == Remnant;
            });
            Assert.Equal(37 + bonus, Pop(host.World, Remnant));
            Assert.Equal(47, Pop(host.World, Remnant));
            host.Shutdown();
        }

        [Fact]
        public void ReplayTo_RebuildsCopyEventsInRange()
        {
            Ensure();
            byte[] mapBytes = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { Remnant, Compact };
            MatchConfig config = MatchConfig.Default(29UL);
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            AiMatchHost host = AiMatchHost.Start(
                config,
                mapBytes,
                factions,
                AiConstants.DifficultyNormal,
                tables);
            int mover = FirstCapturer(host.World, Remnant);
            Assert.True(host.World.TryGetSeat(Remnant, out SeatView seat));
            for (int n = 0; n < 90; n++)
            {
                if (n % 15 == 0)
                {
                    int x = seat.HqXCm + (n * 1000);
                    int y = seat.HqYCm + (n * 500);
                    host.Ai.Commands.Submit(SimCommand.Move(host.Tick, Remnant, mover, x, y));
                }

                host.World.Step();
            }

            Assert.True(host.Tick > SimWorld.EventHistoryTicks);
            List<SimEvent> liveAll = CopyAll(host.World);
            List<SimEvent> liveWindow = new List<SimEvent>();
            host.World.CopyEventsInRange(host.Tick - SimWorld.EventHistoryTicks, host.Tick, liveWindow);
            Assert.True(liveWindow.Count < liveAll.Count);
            Assert.Contains(liveAll, ev => ev.Tick < host.Tick - SimWorld.EventHistoryTicks);
            for (int i = 0; i < liveWindow.Count; i++)
            {
                Assert.True(liveWindow[i].Tick >= host.Tick - SimWorld.EventHistoryTicks);
            }

            byte[] blob = host.Ai.Save();
            SimCommand[] log = new SimCommand[host.CommandLog.Count];
            for (int i = 0; i < log.Length; i++)
            {
                log[i] = host.CommandLog[i];
            }

            int tick = host.Tick;
            AiMatchHost replay = AiMatchHost.ReplayTo(
                config,
                mapBytes,
                factions,
                AiConstants.DifficultyNormal,
                tables,
                log,
                blob,
                tick);
            List<SimEvent> rebuiltAll = CopyAll(replay.World);
            List<SimEvent> rebuiltWindow = new List<SimEvent>();
            replay.World.CopyEventsInRange(replay.Tick - SimWorld.EventHistoryTicks, replay.Tick, rebuiltWindow);
            AssertSame(liveAll, rebuiltAll);
            AssertSame(liveWindow, rebuiltWindow);
            Assert.Equal(host.World.HashState(), replay.World.HashState());
            host.Shutdown();
            replay.Shutdown();
        }

        [Fact]
        public void SecondBuildDepot_DropsTheFirstSiteWithARefund()
        {
            SimWorld world = MapWorld();
            int engineer = Engineer(world, Remnant);
            int first = HomeNode(world, Remnant);
            int second = ClosestOtherNode(
                Town(world, HomeTownId(world, Remnant)).TerritoryId,
                first,
                out _);
            int started = Materials(world, Remnant);
            PlaceFar(world, engineer);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, first));
            Execute(world);
            Assert.Equal(first, OnlyDepot(world).NodeId);
            Assert.Equal(DepotSiteState.Reserved, OnlyDepot(world).State);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, second));
            Execute(world);
            Assert.Equal(second, OnlyDepot(world).NodeId);
            Assert.Equal(started - Rules().DepotMateriel, Materials(world, Remnant));
            SimEvent refund = LastKind(world, SimEventKind.OrderRefunded);
            Assert.Equal(Remnant, refund.SubjectId);
            Assert.Equal((int)CommandKind.BuildDepot, refund.AuxId);
            Assert.Equal(first, refund.Param0);
            Assert.Equal(Rules().DepotMateriel, refund.Param1);
        }

        private static void AssertOnePerNode(int faction)
        {
            SimWorld world = MapWorld();
            int node = HomeNode(world, faction);
            int first = Engineer(world, faction);
            PlaceOnNode(world, first, node);
            int before = Materials(world, faction);
            world.Submit(SimCommand.BuildDepot(world.Tick, faction, first, node));
            Execute(world);
            Assert.Equal(DepotSiteState.UnderConstruction, DepotOn(world, node).State);
            Assert.Equal(before - Rules().DepotMateriel, Materials(world, faction));
            int second = SpawnBuilder(world, faction);
            AssertRejectedDepot(world, faction, node, CommandRejectReason.NodeHasDepot, second);
            Assert.Equal(DepotSiteState.UnderConstruction, DepotOn(world, node).State);
        }

        private static void AcceptDepot(SimWorld world, int faction, int node)
        {
            int engineer = Engineer(world, faction);
            PlaceOnNode(world, engineer, node);
            int before = Materials(world, faction);
            int mark = world.Events.Count;
            world.Submit(SimCommand.BuildDepot(world.Tick, faction, engineer, node));
            Execute(world);
            Assert.False(TryRejectionAfter(world, mark, engineer, out CommandRejectReason reason), reason.ToString());
            Assert.Equal(node, DepotOn(world, node).NodeId);
            Assert.Equal(before - Rules().DepotMateriel, Materials(world, faction));
        }

        private static void AssertRejectedDepot(
            SimWorld world,
            int faction,
            int node,
            CommandRejectReason expected)
        {
            AssertRejectedDepot(world, faction, node, expected, Engineer(world, faction));
        }

        private static void AssertRejectedDepot(
            SimWorld world,
            int faction,
            int node,
            CommandRejectReason expected,
            int battalion)
        {
            int before = Materials(world, faction);
            int depots = Depots(world).Count;
            int mark = world.Events.Count;
            world.Submit(SimCommand.BuildDepot(world.Tick, faction, battalion, node));
            Execute(world);
            Assert.True(TryRejectionAfter(world, mark, battalion, out CommandRejectReason reason));
            Assert.Equal(expected, reason);
            Assert.Equal(before, Materials(world, faction));
            Assert.Equal(depots, Depots(world).Count);
        }

        private static SimWorld SpacingWorld(
            int remnantHq,
            int compactHq,
            int remnantTerritory,
            int compactTerritory,
            int between,
            out int site,
            out int blocker)
        {
            int[] ids = ExtraNodeIds(2, remnantHq, compactHq);
            site = ids[0];
            blocker = ids[1];
            return Synthetic(MakeGraph(
                new[]
                {
                    NodeAt(remnantHq, remnantTerritory),
                    NodeAt(compactHq, compactTerritory),
                    NodeAt(site, remnantTerritory),
                    NodeAt(blocker, compactTerritory)
                },
                new[]
                {
                    Edge(1, remnantHq, site, 1000),
                    Edge(2, compactHq, blocker, 1000),
                    Edge(3, site, blocker, between)
                }));
        }

        private static RoadGraph ClosePairGraph(int hq, int territory, int left, int right, int between)
        {
            return MakeGraph(
                new[]
                {
                    NodeAt(hq, territory),
                    NodeAt(left, territory),
                    NodeAt(right, territory)
                },
                new[]
                {
                    Edge(1, hq, left, 1000),
                    Edge(2, hq, right, 1000),
                    Edge(3, left, right, between)
                });
        }

        private static RoadGraph MakeGraph(GNode[] nodes, GEdge[] edges)
        {
            Array.Sort(nodes, delegate(GNode a, GNode b) { return a.Id.CompareTo(b.Id); });
            int count = nodes.Length;
            int[] ids = new int[count];
            int[] xs = new int[count];
            int[] ys = new int[count];
            ushort[] flags = new ushort[count];
            ushort[] territory = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                if (i > 0 && nodes[i].Id == nodes[i - 1].Id)
                {
                    throw new InvalidOperationException("Duplicate synthetic node.");
                }

                ids[i] = nodes[i].Id;
                xs[i] = nodes[i].X;
                ys[i] = nodes[i].Y;
                territory[i] = (ushort)nodes[i].Territory;
            }

            Array.Sort(edges, delegate(GEdge a, GEdge b) { return a.Id.CompareTo(b.Id); });
            int edgeCount = edges.Length;
            int[] edgeIds = new int[edgeCount];
            int[] from = new int[edgeCount];
            int[] to = new int[edgeCount];
            int[] length = new int[edgeCount];
            byte[] tier = new byte[edgeCount];
            byte[] surface = new byte[edgeCount];
            ushort[] edgeFlags = new ushort[edgeCount];
            for (int i = 0; i < edgeCount; i++)
            {
                int fromIndex = Array.BinarySearch(ids, edges[i].From);
                int toIndex = Array.BinarySearch(ids, edges[i].To);
                if (fromIndex < 0 || toIndex < 0)
                {
                    throw new InvalidOperationException("Synthetic edge is missing a node.");
                }

                edgeIds[i] = edges[i].Id;
                from[i] = fromIndex;
                to[i] = toIndex;
                length[i] = edges[i].Length;
            }

            RoadGraph.BuildAdjacency(
                count,
                from,
                to,
                edgeIds,
                ids,
                out int[] adjOffset,
                out int[] adjNeighbor,
                out int[] adjEdge);
            return new RoadGraph(
                ids,
                xs,
                ys,
                flags,
                territory,
                edgeIds,
                from,
                to,
                length,
                tier,
                surface,
                edgeFlags,
                adjOffset,
                adjNeighbor,
                adjEdge,
                0,
                0);
        }

        private static GNode NodeAt(int id, int territory)
        {
            Ensure();
            Assert.True(Roads().TryGetNodeIndex(id, out int index));
            return new GNode(id, Roads().GetNodeXCm(index), Roads().GetNodeYCm(index), territory);
        }

        private static GEdge Edge(int id, int from, int to, int length)
        {
            return new GEdge(id, from, to, length);
        }

        private static int[] ExtraNodeIds(int count, params int[] exclude)
        {
            Ensure();
            int[] ids = new int[count];
            int filled = 0;
            for (int i = 0; i < Roads().NodeCount && filled < count; i++)
            {
                int id = Roads().GetNodeId(i);
                bool skip = false;
                for (int e = 0; e < exclude.Length; e++)
                {
                    if (exclude[e] == id)
                    {
                        skip = true;
                        break;
                    }
                }

                if (skip)
                {
                    continue;
                }

                ids[filled] = id;
                filled++;
            }

            if (filled < count)
            {
                throw new InvalidOperationException("Map does not have enough road nodes.");
            }

            return ids;
        }

        private static int NearestCoveringNode(
            int territoryId,
            int hqNode,
            int hoodNode,
            out long fromHq,
            out long toHood)
        {
            Ensure();
            long[] hq = DistancesFrom(hqNode);
            long[] hood = DistancesFrom(hoodNode);
            int best = 0;
            long bestHq = long.MaxValue;
            long bestHood = 0L;
            bool found = false;
            for (int i = 0; i < Roads().NodeCount; i++)
            {
                if (Roads().GetNodeTerritoryId(i) != territoryId)
                {
                    continue;
                }

                if (hq[i] > Rules().HqRangeCm || hood[i] > Rules().DepotRangeCm || hq[i] >= bestHq)
                {
                    continue;
                }

                found = true;
                best = Roads().GetNodeId(i);
                bestHq = hq[i];
                bestHood = hood[i];
            }

            if (!found)
            {
                throw new InvalidOperationException("No covering node in territory " + territoryId + ".");
            }

            fromHq = bestHq;
            toHood = bestHood;
            return best;
        }

        private static int ClosestOtherNode(int territoryId, int fromNode, out long distance)
        {
            Ensure();
            long[] dist = DistancesFrom(fromNode);
            int best = 0;
            long bestDist = long.MaxValue;
            for (int i = 0; i < Roads().NodeCount; i++)
            {
                if (Roads().GetNodeTerritoryId(i) != territoryId)
                {
                    continue;
                }

                int id = Roads().GetNodeId(i);
                if (id == fromNode || dist[i] <= 0L || dist[i] >= bestDist)
                {
                    continue;
                }

                best = id;
                bestDist = dist[i];
            }

            if (best == 0)
            {
                throw new InvalidOperationException("No other node in territory " + territoryId + ".");
            }

            distance = bestDist;
            return best;
        }

        private static int FirstNodeIn(int territoryId)
        {
            Ensure();
            for (int i = 0; i < Roads().NodeCount; i++)
            {
                if (Roads().GetNodeTerritoryId(i) == territoryId)
                {
                    return Roads().GetNodeId(i);
                }
            }

            throw new InvalidOperationException("Territory has no road node.");
        }

        private static long RoadDistance(int fromNode, int toNode)
        {
            Ensure();
            if (!Roads().TryGetNodeIndex(toNode, out int index))
            {
                return long.MaxValue;
            }

            return DistancesFrom(fromNode)[index];
        }

        private static long[] DistancesFrom(int sourceId)
        {
            Ensure();
            RoadGraph roads = Roads();
            int count = roads.NodeCount;
            long[] dist = new long[count];
            for (int i = 0; i < count; i++)
            {
                dist[i] = long.MaxValue;
            }

            if (!roads.TryGetNodeIndex(sourceId, out int source))
            {
                return dist;
            }

            int[] heapIndex = new int[count + 8];
            long[] heapDist = new long[count + 8];
            int heapCount = 1;
            dist[source] = 0L;
            heapIndex[0] = source;
            heapDist[0] = 0L;
            while (heapCount > 0)
            {
                int best = 0;
                for (int i = 1; i < heapCount; i++)
                {
                    if (heapDist[i] < heapDist[best])
                    {
                        best = i;
                    }
                }

                int index = heapIndex[best];
                long distance = heapDist[best];
                heapCount--;
                heapIndex[best] = heapIndex[heapCount];
                heapDist[best] = heapDist[heapCount];
                if (distance != dist[index])
                {
                    continue;
                }

                int start = roads.AdjOffset[index];
                int end = roads.AdjOffset[index + 1];
                for (int edge = start; edge < end; edge++)
                {
                    int neighbor = roads.AdjNeighborIndex[edge];
                    int length = roads.EdgeLengthCm[roads.AdjEdgeIndex[edge]];
                    if (length < 0 || dist[index] > long.MaxValue - length)
                    {
                        continue;
                    }

                    long next = dist[index] + length;
                    if (next < dist[neighbor])
                    {
                        dist[neighbor] = next;
                        if (heapCount >= heapIndex.Length)
                        {
                            Array.Resize(ref heapIndex, heapIndex.Length * 2);
                            Array.Resize(ref heapDist, heapDist.Length * 2);
                        }

                        heapIndex[heapCount] = neighbor;
                        heapDist[heapCount] = next;
                        heapCount++;
                    }
                }
            }

            return dist;
        }

        private static void AssertPlaceCounts(SimWorld world)
        {
            Ensure();
            List<TownView> towns = new List<TownView>();
            world.CopyTowns(towns);
            ScenarioDerivedTerritory[] rows = _scenario.Derived.Territories;
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.True(world.TryGetTerritory(rows[i].MapStableKey, out TerritoryView territory));
                int[] counts = new int[CaptureBalance.TierCount];
                int scenery = 0;
                for (int n = 0; n < towns.Count; n++)
                {
                    if (towns[n].TerritoryId != territory.MapTerritoryId)
                    {
                        continue;
                    }

                    if (towns[n].Tier < 0)
                    {
                        scenery++;
                        continue;
                    }

                    counts[towns[n].Tier]++;
                }

                Assert.Equal(rows[i].Places.Hamlet, counts[0]);
                Assert.Equal(rows[i].Places.Village, counts[1]);
                Assert.Equal(rows[i].Places.Town, counts[2]);
                Assert.Equal(rows[i].Places.City, counts[3]);
                Assert.Equal(rows[i].SceneryPlaces, scenery);
            }
        }

        private static int HoldingBonus(SimWorld world, int territoryId)
        {
            List<TownView> towns = new List<TownView>();
            world.CopyTowns(towns);
            int bonus = 0;
            for (int i = 0; i < towns.Count; i++)
            {
                if (towns[i].TerritoryId == territoryId && towns[i].Tier >= 0)
                {
                    bonus += Rules().PopCapBonus(towns[i].Tier);
                }
            }

            return bonus;
        }

        private static void Capture(SimWorld world, int faction, int townId)
        {
            Assert.True(world.TryGetTown(townId, out TownView town));
            if (town.OwnerFactionId == faction)
            {
                return;
            }

            Silence(world);
            SpawnCapturer(world, faction, town.XCm, town.YCm);
            int guard = town.CaptureNeededTicks + 5;
            for (int i = 0; i < guard; i++)
            {
                world.Step();
                Assert.True(world.TryGetTown(townId, out town));
                if (i == 0)
                {
                    Assert.Equal(faction, town.CaptureFactionId);
                    Assert.False(town.Contested);
                }

                if (town.OwnerFactionId == faction)
                {
                    return;
                }
            }

            Assert.Equal(faction, town.OwnerFactionId);
        }

        private static void Silence(SimWorld world)
        {
            List<int> ids = new List<int>();
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.UnitIndex < 0)
                {
                    continue;
                }

                if (Catalog().UnitCanCaptureAt(battalion.UnitIndex) || Catalog().UnitCanContestAt(battalion.UnitIndex))
                {
                    ids.Add(battalion.Id);
                }
            }

            for (int i = 0; i < ids.Count; i++)
            {
                world.SetBattalionStrength(ids[i], 0);
            }
        }

        private static void StepUntilActive(SimWorld world, int node)
        {
            int guard = Rules().DepotBuildTicks + 5;
            for (int i = 0; i < guard; i++)
            {
                if (DepotOn(world, node).State == DepotSiteState.Active)
                {
                    Assert.Equal(Rules().DepotBuildTicks, DepotOn(world, node).ActiveTick - DepotOn(world, node).StartTick);
                    return;
                }

                world.Step();
            }

            Assert.Equal(DepotSiteState.Active, DepotOn(world, node).State);
        }

        private static void PlaceOnNode(SimWorld world, int battalion, int node)
        {
            Assert.True(Roads().TryGetNodeIndex(node, out int index));
            world.PlaceBattalion(battalion, Roads().GetNodeXCm(index), Roads().GetNodeYCm(index));
        }

        private static void PlaceFar(SimWorld world, int battalion)
        {
            int x = 0;
            int y = 0;
            for (int i = 0; i < Roads().NodeCount; i++)
            {
                if (Roads().GetNodeXCm(i) > x)
                {
                    x = Roads().GetNodeXCm(i);
                }

                if (Roads().GetNodeYCm(i) > y)
                {
                    y = Roads().GetNodeYCm(i);
                }
            }

            world.PlaceBattalion(battalion, x + 1000000, y + 1000000);
        }

        private static void Execute(SimWorld world)
        {
            Step(world, world.CommandDelayTicks + 1);
        }

        private static void Step(SimWorld world, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step();
            }
        }

        private static void StepUntil(SimWorld world, int limit, Func<bool> done)
        {
            for (int i = 0; i < limit && !done(); i++)
            {
                world.Step();
            }

            Assert.True(done());
        }

        private static SimWorld MapWorld()
        {
            Ensure();
            SimWorld world = new SimWorld(MatchConfig.Default(7UL));
            world.BindCatalog(Catalog());
            world.BindRoads(_movement, Roads());
            BindSeats(world);
            world.BindHoldings(_scenario, _snapshot);
            world.AddResources(Remnant, 100000, 100000, 0);
            world.AddResources(Compact, 100000, 100000, 0);
            return world;
        }

        private static SimWorld Synthetic(RoadGraph roads)
        {
            Ensure();
            SimWorld world = new SimWorld(MatchConfig.Default(7UL));
            world.BindCatalog(Catalog());
            world.BindRoads(_movement, roads);
            BindSeats(world);
            world.BindHoldings(_scenario, _snapshot);
            world.AddResources(Remnant, 100000, 100000, 0);
            world.AddResources(Compact, 100000, 100000, 0);
            return world;
        }

        private static void BindSeats(SimWorld world)
        {
            BindSeat(world, Remnant, _scenario.SeatAt(0));
            BindSeat(world, Compact, _scenario.SeatAt(1));
        }

        private static void BindSeat(SimWorld world, int factionId, ScenarioSeat seat)
        {
            Assert.True(_snapshot.TryGetTown(seat.HomeTownId, out AiTownRecord town));
            world.BindSeat(new SeatSetup(
                factionId,
                seat.Slot,
                seat.Faction,
                town.Id,
                town.XCm,
                town.YCm,
                1,
                -1,
                seat.StartingManpower,
                Catalog().StartingMateriel,
                Catalog().StartingFuel,
                _scenario.MatchRules.FuelEnabled,
                Catalog().StartingSeatPopCap,
                seat.RaisableUnits,
                seat.AutoResearch,
                seat.StartingUnits));
        }

        private static int SpawnCapturer(SimWorld world, int faction, int x, int y)
        {
            return SpawnRole(world, faction, false, true, true, x, y);
        }

        private static int SpawnContester(SimWorld world, int faction, int x, int y)
        {
            string factionId = FactionName(faction);
            string? found = null;
            for (int i = 0; i < Catalog().UnitCount; i++)
            {
                if (!Catalog().UnitCanContestAt(i) || Catalog().UnitCanBuildAt(i))
                {
                    continue;
                }

                string unitId = Catalog().UnitIdAt(i);
                if (!Catalog().TryGetUnitFaction(unitId, out string unitFaction) || unitFaction != factionId)
                {
                    continue;
                }

                found = unitId;
                if (!Catalog().UnitCanCaptureAt(i))
                {
                    break;
                }
            }

            if (found == null)
            {
                throw new InvalidOperationException("No contesting unit for " + factionId + ".");
            }

            return world.SpawnCatalogUnit(faction, found, x, y);
        }

        private static int SpawnContestOnly(SimWorld world, int faction, int x, int y)
        {
            string factionId = FactionName(faction);
            for (int i = 0; i < Catalog().UnitCount; i++)
            {
                if (Catalog().UnitCanCaptureAt(i) || !Catalog().UnitCanContestAt(i) || Catalog().UnitCanBuildAt(i))
                {
                    continue;
                }

                string unitId = Catalog().UnitIdAt(i);
                if (!Catalog().TryGetUnitFaction(unitId, out string unitFaction) || unitFaction != factionId)
                {
                    continue;
                }

                return world.SpawnCatalogUnit(faction, unitId, x, y);
            }

            throw new InvalidOperationException("No contest-only unit for " + factionId + ".");
        }

        private static int SpawnBuilder(SimWorld world, int faction)
        {
            return SpawnRole(world, faction, true, false, false, 0, 0);
        }

        private static int SpawnRole(
            SimWorld world,
            int faction,
            bool build,
            bool contest,
            bool capture,
            int x,
            int y)
        {
            string factionId = FactionName(faction);
            for (int i = 0; i < Catalog().UnitCount; i++)
            {
                if (Catalog().UnitCanBuildAt(i) != build
                    || Catalog().UnitCanContestAt(i) != contest
                    || Catalog().UnitCanCaptureAt(i) != capture)
                {
                    continue;
                }

                string unitId = Catalog().UnitIdAt(i);
                if (!Catalog().TryGetUnitFaction(unitId, out string unitFaction) || unitFaction != factionId)
                {
                    continue;
                }

                return world.SpawnCatalogUnit(faction, unitId, x, y);
            }

            throw new InvalidOperationException("No catalog unit matches the requested flags.");
        }

        private static string FactionName(int faction)
        {
            return faction == Remnant ? "remnant_command" : "homestead_compact";
        }

        private static int Engineer(SimWorld world, int faction)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == faction
                    && battalion.StrengthPpt > 0
                    && battalion.UnitIndex >= 0
                    && Catalog().UnitCanBuildAt(battalion.UnitIndex))
                {
                    return battalion.Id;
                }
            }

            throw new InvalidOperationException("No engineer.");
        }

        private static int FirstCapturer(SimWorld world, int faction)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == faction
                    && battalion.StrengthPpt > 0
                    && battalion.UnitIndex >= 0
                    && Catalog().UnitCanCaptureAt(battalion.UnitIndex))
                {
                    return battalion.Id;
                }
            }

            throw new InvalidOperationException("No capturer.");
        }

        private static int HomeNode(SimWorld world, int faction)
        {
            return Town(world, HomeTownId(world, faction)).RoadNodeId;
        }

        private static int HomeTownId(SimWorld world, int faction)
        {
            Assert.True(world.TryGetSeat(faction, out SeatView seat));
            return seat.HomeTownId;
        }

        private static int HomeNodeId(int faction)
        {
            Ensure();
            int townId = faction == Remnant ? _snapshot.GoldendaleTownId : _snapshot.HoodRiverTownId;
            Assert.True(_snapshot.TryGetTown(townId, out AiTownRecord town));
            return town.RoadNodeId;
        }

        private static int HomeTerritoryId(int faction)
        {
            Ensure();
            int townId = faction == Remnant ? _snapshot.GoldendaleTownId : _snapshot.HoodRiverTownId;
            Assert.True(_snapshot.TryGetTown(townId, out AiTownRecord town));
            return town.TerritoryId;
        }

        private static TownView TownByName(SimWorld world, string name)
        {
            return Town(world, FindTown(name).Id);
        }

        private static TownView Town(SimWorld world, int townId)
        {
            Assert.True(world.TryGetTown(townId, out TownView town));
            return town;
        }

        private static TownView SeatOf(SimWorld world, int territoryId)
        {
            List<TownView> towns = new List<TownView>();
            world.CopyTowns(towns);
            for (int i = 0; i < towns.Count; i++)
            {
                if (towns[i].TerritoryId == territoryId && towns[i].IsSeatTown)
                {
                    return towns[i];
                }
            }

            throw new InvalidOperationException("No seat town.");
        }

        private static int Pop(SimWorld world, int faction)
        {
            Assert.True(world.TryGetSeat(faction, out SeatView seat));
            return seat.PopCap;
        }

        private static int Materials(SimWorld world, int faction)
        {
            Assert.True(world.TryGetSeat(faction, out SeatView seat));
            return seat.Materials;
        }

        private static List<DepotView> Depots(SimWorld world)
        {
            List<DepotView> depots = new List<DepotView>();
            world.CopyDepots(depots);
            return depots;
        }

        private static DepotView OnlyDepot(SimWorld world)
        {
            List<DepotView> depots = Depots(world);
            Assert.Single(depots);
            return depots[0];
        }

        private static DepotView DepotOn(SimWorld world, int node)
        {
            List<DepotView> depots = Depots(world);
            for (int i = 0; i < depots.Count; i++)
            {
                if (depots[i].NodeId == node)
                {
                    return depots[i];
                }
            }

            throw new InvalidOperationException("No depot on node.");
        }

        private static bool TryRejectionAfter(
            SimWorld world,
            int eventMark,
            int subject,
            out CommandRejectReason reason)
        {
            for (int i = world.Events.Count - 1; i >= eventMark; i--)
            {
                SimEvent ev = world.Events[i];
                if (ev.Kind == SimEventKind.CommandRejected && ev.SubjectId == subject)
                {
                    reason = ev.RejectReason;
                    return true;
                }
            }

            reason = CommandRejectReason.None;
            return false;
        }

        private static SimEvent LastKind(SimWorld world, SimEventKind kind)
        {
            for (int i = world.Events.Count - 1; i >= 0; i--)
            {
                if (world.Events[i].Kind == kind)
                {
                    return world.Events[i];
                }
            }

            throw new InvalidOperationException("Missing event.");
        }

        private static List<SimEvent> CopyAll(SimWorld world)
        {
            List<SimEvent> copy = new List<SimEvent>(world.Events.Count);
            for (int i = 0; i < world.Events.Count; i++)
            {
                copy.Add(world.Events[i]);
            }

            return copy;
        }

        private static void AssertSame(List<SimEvent> left, List<SimEvent> right)
        {
            Assert.Equal(left.Count, right.Count);
            for (int i = 0; i < left.Count; i++)
            {
                Assert.Equal(left[i].EventId, right[i].EventId);
                Assert.Equal(left[i].Tick, right[i].Tick);
                Assert.Equal(left[i].Kind, right[i].Kind);
                Assert.Equal(left[i].SubjectId, right[i].SubjectId);
                Assert.Equal(left[i].AuxId, right[i].AuxId);
                Assert.Equal(left[i].Param0, right[i].Param0);
                Assert.Equal(left[i].Param1, right[i].Param1);
                Assert.Equal(left[i].Key, right[i].Key);
            }
        }

        private static int RulesCommandDelay()
        {
            return SimConfig.DefaultCommandDelayTicks;
        }

        private static CaptureBalance Rules()
        {
            return Catalog().Capture;
        }

        private static BalanceCatalog Catalog()
        {
            Ensure();
            return _catalog;
        }

        private static RoadGraph Roads()
        {
            Ensure();
            return _roads;
        }

        private static NamedTown FindTown(string name)
        {
            Ensure();
            for (int i = 0; i < _named.Length; i++)
            {
                if (_named[i].Name != name)
                {
                    continue;
                }

                Assert.True(_snapshot.TryGetTown(_named[i].Id, out AiTownRecord record));
                return new NamedTown(record.Id, record.Population, _named[i].Name);
            }

            throw new InvalidOperationException("Map has no town named " + name + ".");
        }

        private static void Ensure()
        {
            if (_ready)
            {
                return;
            }

            _catalog = BalanceCatalog.LoadFromDirectory(BalanceLoad.DirectoryPath());
            string dir = GoldenMapFiles.FindDirectory(GoldenMapV1MarshTests.MapFileName);
            byte[] mapBytes = GoldenMapFiles.ReadWrmb(
                Path.Combine(dir, GoldenMapV1MarshTests.MapFileName),
                GoldenMapV1MarshTests.MapBytes);
            byte[] terrain = GoldenMapFiles.ReadWrmb(
                Path.Combine(dir, GoldenMapV1MarshTests.TerrainFileName),
                GoldenMapV1MarshTests.TerrainBytes);
            _snapshot = AiMapSnapshot.FromWrmb(mapBytes);
            _scenario = SimScenario.Load(
                GoldenMapFiles.ReadRepoText("data/balance/scenarios/columbia_gorge_100mi_mvp.json"),
                _catalog,
                mapBytes,
                GoldenMapV1MarshTests.MapFileName,
                "columbia_gorge_100mi_mvp");
            _roads = Wrmb.ReadRoadGraph(mapBytes);
            _movement = MovementService.Create(mapBytes, terrain, MovementTables.Default());
            _named = ReadTownNames(mapBytes);
            _ready = true;
        }

        private static NamedTown[] ReadTownNames(byte[] mapBytes)
        {
            WrmbFile file = WrmbFile.Parse(mapBytes);
            WrmbDirEntry towns = file.RequireSection("TOWN");
            WrmbDirEntry strings = file.RequireSection("STRP");
            byte[] rows = file.SectionBytes(towns);
            byte[] pool = file.SectionBytes(strings);
            NamedTown[] named = new NamedTown[towns.Count];
            BlobReader reader = new BlobReader(rows);
            for (int i = 0; i < towns.Count; i++)
            {
                reader.Seek(i * towns.ElementSize);
                int id = reader.I32();
                reader.Seek((i * towns.ElementSize) + 52);
                int nameOff = (int)reader.U32();
                int nameLen = (int)reader.U32();
                named[i] = new NamedTown(id, 0, Encoding.UTF8.GetString(pool, nameOff, nameLen));
            }

            return named;
        }

        private static bool _ready;
        private static BalanceCatalog _catalog = null!;
        private static SimScenario _scenario = null!;
        private static AiMapSnapshot _snapshot = null!;
        private static RoadGraph _roads = null!;
        private static MovementService _movement = null!;
        private static NamedTown[] _named = null!;

        private readonly struct NamedTown
        {
            public readonly int Id;
            public readonly int Population;
            public readonly string Name;

            public NamedTown(int id, int population, string name)
            {
                Id = id;
                Population = population;
                Name = name;
            }
        }

        private readonly struct GNode
        {
            public readonly int Id;
            public readonly int X;
            public readonly int Y;
            public readonly int Territory;

            public GNode(int id, int x, int y, int territory)
            {
                Id = id;
                X = x;
                Y = y;
                Territory = territory;
            }
        }

        private readonly struct GEdge
        {
            public readonly int Id;
            public readonly int From;
            public readonly int To;
            public readonly int Length;

            public GEdge(int id, int from, int to, int length)
            {
                Id = id;
                From = from;
                To = to;
                Length = length;
            }
        }
    }
}
