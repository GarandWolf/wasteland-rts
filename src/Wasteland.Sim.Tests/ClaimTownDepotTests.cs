using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class ClaimTownDepotTests
    {
        private const int Remnant = AiConstants.DefaultAiFactionA;
        private const int Compact = AiConstants.DefaultAiFactionB;

        [Fact]
        public void StartingCaps_Are37And39_AndDallesCaptureAdds10()
        {
            SimWorld world = World();
            ClearNeutralGarrisons(world);
            Assert.True(world.TryGetSeat(Remnant, out SeatView remnant));
            Assert.True(world.TryGetSeat(Compact, out SeatView compact));
            Assert.Equal(37, remnant.PopCap);
            Assert.Equal(39, compact.PopCap);
            Assert.Equal(30, Catalog().StartingSeatPopCap);

            TownView dalles = SeatTown(world, Map().TheDallesTownId);
            Assert.Equal(0, dalles.OwnerFactionId);
            int needed = dalles.CaptureNeededTicks;
            int capturer = SpawnCapturer(world, Remnant, dalles.XCm, dalles.YCm);
            int flips = CountKind(world, SimEventKind.TownOwnerChanged);
            StepUntil(world, needed + 2, delegate
            {
                return CountKind(world, SimEventKind.TownOwnerChanged) > flips;
            });

            SimEvent flip = LastKind(world, SimEventKind.TownOwnerChanged);
            Assert.Equal(needed, flip.Tick);
            Assert.Equal(dalles.TownId, flip.SubjectId);
            Assert.Equal(Remnant, flip.AuxId);
            Assert.Equal(0, flip.Param0);
            Assert.Equal(1, flip.Param1);
            Assert.NotEqual(needed - 1, flip.Tick);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView after));
            Assert.Equal(Remnant, after.OwnerFactionId);
            Assert.True(world.TryGetSeat(Remnant, out remnant));
            Assert.Equal(47, remnant.PopCap);
            Assert.True(world.TryGetBattalionView(capturer, out _));
        }

        [Fact]
        public void ContestPauses_AndDoesNotReset()
        {
            SimWorld world = World();
            ClearNeutralGarrisons(world);
            TownView dalles = SeatTown(world, Map().TheDallesTownId);
            int attacker = SpawnCapturer(world, Remnant, dalles.XCm, dalles.YCm);
            Step(world, 40);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView running));
            Assert.Equal(40, running.CaptureProgressTicks);
            Assert.False(running.Contested);

            SpawnCapturer(world, Compact, dalles.XCm, dalles.YCm);
            Step(world, 25);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView paused));
            Assert.True(paused.Contested);
            Assert.Equal(40, paused.CaptureProgressTicks);
            Assert.Equal(Remnant, paused.CaptureFactionId);
            Assert.True(HasKind(world, SimEventKind.TownContestStarted));

            world.PlaceBattalion(attacker, dalles.XCm, dalles.YCm);
            RemoveFaction(world, Compact);
            Step(world, 10);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView resumed));
            Assert.False(resumed.Contested);
            Assert.Equal(50, resumed.CaptureProgressTicks);
            Assert.True(HasKind(world, SimEventKind.TownContestEnded));
        }

        [Fact]
        public void AbsentHoldThenDecay_NeverResetsProgress()
        {
            SimWorld world = World();
            ClearNeutralGarrisons(world);
            TownView dalles = SeatTown(world, Map().TheDallesTownId);
            int attacker = SpawnCapturer(world, Remnant, dalles.XCm, dalles.YCm);
            Step(world, 30);
            world.PlaceBattalion(attacker, dalles.XCm + dalles.ZoneRadiusCm + 5, dalles.YCm);
            int hold = Catalog().Capture.DecayStartsAfterTicks;
            Step(world, hold);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView held));
            Assert.Equal(30, held.CaptureProgressTicks);
            Assert.Equal(hold, held.CaptureAbsentTicks);

            Step(world, 4);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView decayed));
            Assert.Equal(26, decayed.CaptureProgressTicks);
            Assert.Equal(Remnant, decayed.CaptureFactionId);

            world.PlaceBattalion(attacker, dalles.XCm, dalles.YCm);
            Step(world, 3);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView returned));
            Assert.Equal(29, returned.CaptureProgressTicks);
            Assert.Equal(0, returned.CaptureAbsentTicks);
        }

        [Fact]
        public void TwoSeats_SecondStartsOnlyAfterMeterReachesZero()
        {
            SimWorld world = World();
            ClearNeutralGarrisons(world);
            TownView dalles = SeatTown(world, Map().TheDallesTownId);
            int first = SpawnCapturer(world, Remnant, dalles.XCm, dalles.YCm);
            Step(world, 10);
            SpawnCapturer(world, Compact, dalles.XCm, dalles.YCm);
            Step(world, 20);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView both));
            Assert.Equal(Remnant, both.CaptureFactionId);
            Assert.Equal(10, both.CaptureProgressTicks);
            Assert.True(both.Contested);

            world.SetBattalionStrength(first, 0);
            int hold = Catalog().Capture.DecayStartsAfterTicks;
            Step(world, hold);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView waiting));
            Assert.Equal(10, waiting.CaptureProgressTicks);
            Assert.Equal(Remnant, waiting.CaptureFactionId);

            Step(world, 10);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView cleared));
            Assert.Equal(0, cleared.CaptureFactionId);
            Assert.Equal(0, cleared.CaptureProgressTicks);

            Step(world, 1);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView second));
            Assert.Equal(Compact, second.CaptureFactionId);
            Assert.Equal(1, second.CaptureProgressTicks);
        }

        [Fact]
        public void Engineers_DoNotCaptureOrContest()
        {
            SimWorld world = World();
            ClearNeutralGarrisons(world);
            TownView dalles = SeatTown(world, Map().TheDallesTownId);
            int engineer = SpawnRole(world, Remnant, true, false, false, dalles.XCm, dalles.YCm);
            Assert.True(engineer > 0);
            Step(world, 8);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView idle));
            Assert.Equal(0, idle.CaptureFactionId);
            Assert.Equal(0, idle.CaptureProgressTicks);

            int attacker = SpawnCapturer(world, Compact, dalles.XCm, dalles.YCm);
            Step(world, 12);
            world.SetBattalionStrength(attacker, 0);
            SpawnRole(world, Remnant, true, false, false, dalles.XCm, dalles.YCm);
            int hold = Catalog().Capture.DecayStartsAfterTicks;
            Step(world, hold + 2);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView decayed));
            Assert.Equal(10, decayed.CaptureProgressTicks);
            Assert.False(decayed.Contested);
        }

        [Fact]
        public void HoodRiver_ZoneIs160934_AndTierIsTown()
        {
            SimWorld world = World();
            TownView hood = SeatTown(world, Map().HoodRiverTownId);
            Assert.True(hood.IsSeatTown);
            Assert.Equal(160934, hood.ZoneRadiusCm);
            Assert.Equal("town", Catalog().Capture.TierId(hood.Tier));
            Assert.Equal(Catalog().Capture.CaptureTicks(hood.Tier, hood.FortificationLevel), hood.CaptureNeededTicks);
            Assert.True(hood.FortificationLevel > 0);
        }

        [Fact]
        public void Dallesport_CountsForTheDalles_AndIsNotAZone()
        {
            SimWorld world = World();
            ClearNeutralGarrisons(world);
            NamedTown dallesport = FindTown("Dallesport");
            Assert.True(world.TryGetTown(dallesport.Id, out TownView minor));
            Assert.False(minor.IsSeatTown);
            Assert.Equal(0, minor.ZoneRadiusCm);

            TownView dalles = SeatTown(world, Map().TheDallesTownId);
            Assert.True(Inside(dallesport.XCm, dallesport.YCm, dalles));
            int claimant = CapturerBattalion(world, Remnant);
            world.Submit(SimCommand.ClaimTown(0, Remnant, claimant, dallesport.Id));
            world.Step();
            Assert.True(world.TryGetRejection(claimant, out CommandRejectReason reason, out int param0, out _));
            Assert.Equal(CommandRejectReason.NotSeatTown, reason);
            Assert.Equal(dallesport.Id, param0);

            SpawnCapturer(world, Remnant, dallesport.XCm, dallesport.YCm);
            world.Step();
            Assert.True(world.TryGetTown(dalles.TownId, out TownView seat));
            Assert.Equal(1, seat.CaptureProgressTicks);
            Assert.Equal(Remnant, seat.CaptureFactionId);
            Assert.True(world.TryGetTown(dallesport.Id, out minor));
            Assert.Equal(0, minor.CaptureProgressTicks);
            Assert.Equal(0, minor.ZoneRadiusCm);
        }

        [Fact]
        public void StartingUnits_AreOutsideNeutralSeatZones()
        {
            SimWorld world = World();
            world.CopyTowns(_towns);
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == SimWorld.NeutralOwnerId)
                {
                    continue;
                }

                for (int t = 0; t < _towns.Count; t++)
                {
                    TownView town = _towns[t];
                    if (!town.IsSeatTown || town.OwnerFactionId != 0 || town.ZoneRadiusCm <= 0)
                    {
                        continue;
                    }

                    Assert.False(Inside(battalion.PositionXCm, battalion.PositionYCm, town));
                }
            }
        }

        [Fact]
        public void Overlap_HoodRiverAndWhiteSalmon_CountsInBothZones()
        {
            SimWorld world = World();
            world.CopyTowns(_towns);
            int pairA = -1;
            int pairB = -1;
            int pairs = 0;
            for (int i = 0; i < _towns.Count; i++)
            {
                TownView left = _towns[i];
                if (!left.IsSeatTown || left.ZoneRadiusCm <= 0)
                {
                    continue;
                }

                for (int j = i + 1; j < _towns.Count; j++)
                {
                    TownView right = _towns[j];
                    if (!right.IsSeatTown || right.ZoneRadiusCm <= 0)
                    {
                        continue;
                    }

                    if (!DisksOverlap(left, right))
                    {
                        continue;
                    }

                    pairs++;
                    pairA = i;
                    pairB = j;
                }
            }

            Assert.Equal(1, pairs);
            SilenceCombat(world, Remnant);
            SilenceCombat(world, Compact);
            TownView a = _towns[pairA];
            TownView b = _towns[pairB];
            int hood = Map().HoodRiverTownId;
            int salmon = Map().WhiteSalmonTownId;
            Assert.True(
                (a.TownId == hood && b.TownId == salmon) || (a.TownId == salmon && b.TownId == hood));
            Assert.True(PointInBoth(a, b, out int x, out int y));
            SpawnCapturer(world, Remnant, x, y);
            world.Step();
            Assert.True(world.TryGetTown(a.TownId, out TownView leftAfter));
            Assert.True(world.TryGetTown(b.TownId, out TownView rightAfter));
            Assert.Equal(Remnant, leftAfter.CaptureFactionId);
            Assert.Equal(Remnant, rightAfter.CaptureFactionId);
            Assert.Equal(1, leftAfter.CaptureProgressTicks);
            Assert.Equal(1, rightAfter.CaptureProgressTicks);

            SpawnCapturer(world, Compact, x, y);
            int before = world.Events.Count;
            world.Step();
            List<SimEvent> started = new List<SimEvent>();
            for (int i = before; i < world.Events.Count; i++)
            {
                if (world.Events[i].Kind == SimEventKind.TownContestStarted)
                {
                    started.Add(world.Events[i]);
                }
            }

            Assert.Equal(2, started.Count);
            Assert.True(started[0].SubjectId < started[1].SubjectId);
            Assert.Equal(a.TownId < b.TownId ? a.TownId : b.TownId, started[0].SubjectId);
            Assert.Equal(a.TownId < b.TownId ? b.TownId : a.TownId, started[1].SubjectId);
        }

        [Fact]
        public void ReservedDepot_CountsForSpacing_BeforeStart()
        {
            SimWorld world = RoadWorld();
            int engineer = Engineer(world, Remnant);
            int node = FirstLegalNode(world, Remnant);
            int materials = Materials(world, Remnant);
            world.PlaceBattalion(engineer, 0, 0);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, node));
            world.Step();
            List<DepotView> depots = new List<DepotView>();
            world.CopyDepots(depots);
            Assert.Single(depots);
            Assert.Equal(DepotSiteState.Reserved, depots[0].State);
            Assert.Equal(-1, depots[0].StartTick);
            Assert.Equal(node, depots[0].NodeId);
            Assert.Equal(materials - Catalog().Capture.DepotMateriel, Materials(world, Remnant));
            Assert.NotEqual(0UL, depots[0].TerritoryKey);

            int tooClose = NeighborIfTooClose(world, Remnant, node);
            Assert.True(tooClose > 0);
            Assert.Equal(
                CommandRejectReason.DepotTooClose,
                world.PreviewDepotPlacement(Remnant, tooClose, out _, out _));
        }

        [Fact]
        public void BuildStartsAtNodeControlRadius_AndFinishesAlone()
        {
            SimWorld world = RoadWorld();
            int engineer = Engineer(world, Remnant);
            int node = FirstLegalNode(world, Remnant);
            Assert.True(Roads().TryGetNodeIndex(node, out int index));
            int nodeX = Roads().GetNodeXCm(index);
            int nodeY = Roads().GetNodeYCm(index);
            int radius = Catalog().Capture.NodeControlRadiusCm;
            Assert.Equal(80467, radius);
            world.PlaceBattalion(engineer, 0, 0);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, node));
            world.Step();
            Assert.Equal(DepotSiteState.Reserved, OnlyDepot(world).State);
            world.PlaceBattalion(engineer, nodeX + radius + 1, nodeY);
            world.Step();
            Assert.Equal(DepotSiteState.Reserved, OnlyDepot(world).State);

            world.PlaceBattalion(engineer, nodeX + radius, nodeY);
            int startTick = world.Tick;
            world.Step();
            DepotView started = OnlyDepot(world);
            Assert.Equal(DepotSiteState.UnderConstruction, started.State);
            Assert.Equal(startTick, started.StartTick);
            Assert.Equal(startTick + Catalog().Capture.DepotBuildTicks, started.ActiveTick);

            world.PlaceBattalion(engineer, nodeX + radius + 100000, nodeY);
            world.SetBattalionStrength(engineer, 0);
            Step(world, Catalog().Capture.DepotBuildTicks);
            DepotView active = OnlyDepot(world);
            Assert.Equal(DepotSiteState.Active, active.State);
            Assert.Equal(started.ActiveTick, active.ActiveTick);
        }

        [Fact]
        public void BeforeStart_Death_NoPath_AndReorder_Refund200()
        {
            int cost = Catalog().Capture.DepotMateriel;
            Assert.Equal(200, cost);

            SimWorld died = RoadWorld();
            int engineer = Engineer(died, Remnant);
            int node = FirstLegalNode(died, Remnant);
            died.PlaceBattalion(engineer, 0, 0);
            died.Submit(SimCommand.BuildDepot(died.Tick, Remnant, engineer, node));
            died.Step();
            Assert.Equal(DepotSiteState.Reserved, OnlyDepot(died).State);
            died.SetBattalionStrength(engineer, 0);
            int before = Materials(died, Remnant);
            died.Step();
            Assert.Empty(Depots(died));
            Assert.Equal(before + cost, Materials(died, Remnant));
            SimEvent refund = LastKind(died, SimEventKind.OrderRefunded);
            Assert.Equal(Remnant, refund.SubjectId);
            Assert.Equal(cost, refund.Param1);
            Assert.Equal(node, refund.Param0);

            SimWorld blocked = RoadWorld();
            engineer = Engineer(blocked, Remnant);
            node = FirstLegalNode(blocked, Remnant);
            int full = Materials(blocked, Remnant);
            blocked.ForceNextRoadNoPath = true;
            blocked.Submit(SimCommand.BuildDepot(blocked.Tick, Remnant, engineer, node));
            blocked.Step();
            Assert.Empty(Depots(blocked));
            Assert.Equal(full, Materials(blocked, Remnant));
            Assert.True(HasKind(blocked, SimEventKind.DepotStarted));
            Assert.True(HasKind(blocked, SimEventKind.OrderRefunded));
            Assert.True(blocked.TryGetRejection(engineer, out CommandRejectReason noPath, out _, out _));
            Assert.Equal(CommandRejectReason.NoPath, noPath);

            SimWorld moved = RoadWorld();
            engineer = Engineer(moved, Remnant);
            node = FirstLegalNode(moved, Remnant);
            moved.PlaceBattalion(engineer, 0, 0);
            moved.Submit(SimCommand.BuildDepot(moved.Tick, Remnant, engineer, node));
            moved.Step();
            Assert.Equal(DepotSiteState.Reserved, OnlyDepot(moved).State);
            before = Materials(moved, Remnant);
            moved.Submit(SimCommand.Move(moved.Tick, Remnant, engineer, 1000, 1000));
            moved.Step();
            Assert.Empty(Depots(moved));
            Assert.Equal(before + cost, Materials(moved, Remnant));
            Assert.Equal((int)CommandKind.BuildDepot, LastKind(moved, SimEventKind.OrderRefunded).AuxId);
        }

        [Fact]
        public void CapturedTerritory_DestroysReservedStartedAndActive_WithNoRefund()
        {
            SimWorld world = RoadWorld();
            ClearNeutralGarrisons(world);
            TownView dalles = SeatTown(world, Map().TheDallesTownId);
            SpawnCapturer(world, Remnant, dalles.XCm, dalles.YCm);
            int needed = dalles.CaptureNeededTicks;
            StepUntil(world, needed + 2, delegate
            {
                return world.TryGetTown(dalles.TownId, out TownView owned) && owned.OwnerFactionId == Remnant;
            });

            world.AddResources(Remnant, 0, 2000, 0);
            int engineer = Engineer(world, Remnant);
            int territory = dalles.TerritoryId;
            int nodeA = FirstLegalNodeIn(world, Remnant, territory);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, nodeA));
            world.Step();
            Assert.True(Roads().TryGetNodeIndex(nodeA, out int indexA));
            world.PlaceBattalion(engineer, Roads().GetNodeXCm(indexA), Roads().GetNodeYCm(indexA));
            world.Step();
            Assert.Equal(DepotSiteState.UnderConstruction, DepotOn(world, nodeA).State);

            world.PlaceBattalion(engineer, 0, 0);
            Step(world, Catalog().Capture.DepotBuildTicks);
            Assert.Equal(DepotSiteState.Active, DepotOn(world, nodeA).State);

            SilenceCombat(world, Remnant);
            Assert.True(world.TryGetTown(dalles.TownId, out TownView ownedDalles));
            SpawnCapturer(world, Compact, ownedDalles.XCm, ownedDalles.YCm);
            int captureNeeded = ownedDalles.CaptureNeededTicks;
            StepUntil(world, captureNeeded, delegate
            {
                return world.TryGetTown(dalles.TownId, out TownView partial)
                    && partial.CaptureProgressTicks >= captureNeeded - 20;
            });

            int nodeB = FirstLegalNodeIn(world, Remnant, territory);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, nodeB));
            world.Step();
            Assert.True(Roads().TryGetNodeIndex(nodeB, out int indexB));
            world.PlaceBattalion(engineer, Roads().GetNodeXCm(indexB), Roads().GetNodeYCm(indexB));
            world.Step();
            Assert.Equal(DepotSiteState.UnderConstruction, DepotOn(world, nodeB).State);

            world.PlaceBattalion(engineer, 0, 0);
            int nodeC = FirstLegalNodeIn(world, Remnant, territory);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, nodeC));
            world.Step();
            world.PlaceBattalion(engineer, 0, 0);
            Assert.Equal(DepotSiteState.Active, DepotOn(world, nodeA).State);
            Assert.Equal(DepotSiteState.UnderConstruction, DepotOn(world, nodeB).State);
            Assert.Equal(DepotSiteState.Reserved, DepotOn(world, nodeC).State);

            int materiel = Materials(world, Remnant);
            List<int> ids = new List<int>();
            List<DepotView> before = Depots(world);
            for (int i = 0; i < before.Count; i++)
            {
                ids.Add(before[i].DepotId);
            }

            StepUntil(world, 40, delegate
            {
                return world.TryGetTown(dalles.TownId, out TownView now) && now.OwnerFactionId == Compact;
            });

            Assert.Empty(Depots(world));
            Assert.Equal(materiel, Materials(world, Remnant));
            List<int> destroyed = new List<int>();
            int destroyTick = -1;
            for (int i = 0; i < world.Events.Count; i++)
            {
                SimEvent ev = world.Events[i];
                if (ev.Kind != SimEventKind.DepotDestroyed)
                {
                    continue;
                }

                destroyed.Add(ev.SubjectId);
                destroyTick = ev.Tick;
            }

            for (int i = 0; i < world.Events.Count; i++)
            {
                SimEvent ev = world.Events[i];
                if (ev.Tick == destroyTick)
                {
                    Assert.NotEqual(SimEventKind.OrderRefunded, ev.Kind);
                    Assert.NotEqual(SimEventKind.DepotActivated, ev.Kind);
                }
            }

            Assert.Equal(ids, destroyed);
            for (int i = 1; i < destroyed.Count; i++)
            {
                Assert.True(destroyed[i - 1] < destroyed[i]);
            }
        }

        [Fact]
        public void EventOrder_IsStable_AndIndependentOfSubmitOrder()
        {
            List<SimEvent> first = RunScript();
            List<SimEvent> second = RunScript();
            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Kind, second[i].Kind);
                Assert.Equal(first[i].Tick, second[i].Tick);
                Assert.Equal(first[i].SubjectId, second[i].SubjectId);
                Assert.Equal(first[i].AuxId, second[i].AuxId);
                Assert.Equal(first[i].Param0, second[i].Param0);
                Assert.Equal(first[i].Param1, second[i].Param1);
                Assert.Equal(first[i].Key, second[i].Key);
            }

            List<SimEvent> lowFirst = RejectPair(true);
            List<SimEvent> highFirst = RejectPair(false);
            Assert.Equal(lowFirst.Count, highFirst.Count);
            for (int i = 0; i < lowFirst.Count; i++)
            {
                Assert.Equal(lowFirst[i].Kind, highFirst[i].Kind);
                Assert.Equal(lowFirst[i].SubjectId, highFirst[i].SubjectId);
                Assert.Equal(lowFirst[i].AuxId, highFirst[i].AuxId);
                Assert.Equal(lowFirst[i].Key, highFirst[i].Key);
            }

            Assert.Equal((ulong)Remnant, lowFirst[0].Key >> 32);
            Assert.Equal((ulong)Compact, lowFirst[1].Key >> 32);
        }

        [Fact]
        public void RejectKey_CarriesIssuerAndSequence_AndHistoryKeeps64Ticks()
        {
            SimWorld world = World();
            SimWorldCommandSink sink = new SimWorldCommandSink(world);
            SimCommand stamped = sink.Submit(SimCommand.ClaimTown(0, Remnant, 999001, 999002));
            Assert.Equal(stamped.Sequence, sink.LastSequence);
            world.Step();
            SimEvent reject = LastKind(world, SimEventKind.CommandRejected);
            ulong key = ((ulong)(uint)Remnant << 32) | (uint)stamped.Sequence;
            Assert.Equal(key, reject.Key);
            Assert.Equal(CommandRejectReason.UnknownBattalion, reject.RejectReason);

            ForwardingAiCommandSink forwarding = new ForwardingAiCommandSink(sink, EmptyOwned());
            forwarding.SubmitClaimTown(Remnant, 999001, 999003);
            Assert.Equal(sink.LastSequence, forwarding.LastSequence);

            for (int i = 0; i < 80; i++)
            {
                world.Step();
            }

            List<SimEvent> recent = new List<SimEvent>();
            world.CopyEventsOfTick(0, recent);
            Assert.Empty(recent);
            Assert.Contains(world.Events, ev => ev.Tick == 0 && ev.Kind == SimEventKind.CommandRejected);
            world.CopyEventsInRange(world.Tick - 64, world.Tick - 1, recent);
            Assert.True(recent.Count < world.Events.Count);
        }

        [Fact]
        public void SecondBuildDepot_DropsTheFirstSite_AndRefunds()
        {
            SimWorld world = RoadWorld();
            int engineer = Engineer(world, Remnant);
            int first = FirstLegalNode(world, Remnant);
            int started = Materials(world, Remnant);
            world.PlaceBattalion(engineer, 0, 0);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, first));
            world.Step();
            Assert.Equal(DepotSiteState.Reserved, OnlyDepot(world).State);
            int second = FirstLegalNode(world, Remnant);
            Assert.NotEqual(first, second);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, second));
            world.Step();
            Assert.Equal(second, OnlyDepot(world).NodeId);
            Assert.Equal(started - Catalog().Capture.DepotMateriel, Materials(world, Remnant));
            SimEvent refund = LastKind(world, SimEventKind.OrderRefunded);
            Assert.Equal((int)CommandKind.BuildDepot, refund.AuxId);
            Assert.Equal(first, refund.Param0);
            Assert.Equal(Catalog().Capture.DepotMateriel, refund.Param1);
        }

        [Fact]
        public void OrderRefunded_RestoresFuel_WhenFuelIsEnabled()
        {
            SimWorld world = RoadWorld(true);
            int engineer = Engineer(world, Remnant);
            int node = FirstLegalNode(world, Remnant);
            int fuel = world.FuelOf(Remnant);
            int materiel = Materials(world, Remnant);
            world.PlaceBattalion(engineer, 0, 0);
            world.Submit(SimCommand.BuildDepot(world.Tick, Remnant, engineer, node));
            world.Step();
            Assert.Equal(fuel - Catalog().Capture.DepotFuel, world.FuelOf(Remnant));
            Assert.Equal(materiel - Catalog().Capture.DepotMateriel, Materials(world, Remnant));
            world.Submit(SimCommand.Move(world.Tick, Remnant, engineer, 1000, 1000));
            world.Step();
            Assert.Empty(Depots(world));
            Assert.Equal(fuel, world.FuelOf(Remnant));
            Assert.Equal(materiel, Materials(world, Remnant));
            Assert.Equal(Catalog().Capture.DepotMateriel, LastKind(world, SimEventKind.OrderRefunded).Param1);
        }

        [Fact]
        public void Host_Gorge_BindsHoldings_AcceptsClaimAndDepot()
        {
            Ensure();
            byte[] mapBytes = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB };
            AiMatchHost host = AiMatchHost.Start(
                MatchConfig.Default(11UL),
                mapBytes,
                factions,
                AiConstants.DifficultyNormal,
                AiBalanceTablesTests.LoadBalance());
            Assert.True(host.World.TryGetSeat(Remnant, out SeatView remnant));
            Assert.True(host.World.TryGetSeat(Compact, out SeatView compact));
            Assert.Equal(37, remnant.PopCap);
            Assert.Equal(39, compact.PopCap);
            BalanceCatalog? scenarioCatalog = AiBalanceTablesTests.LoadBalance().ScenarioCatalog;
            Assert.NotNull(scenarioCatalog);
            Assert.Equal(30, scenarioCatalog.StartingSeatPopCap);

            int dallesId = host.Ai.Map.TheDallesTownId;
            Assert.True(host.World.TryGetTown(dallesId, out TownView dalles));
            Assert.True(dalles.IsSeatTown);
            Assert.Equal(0, dalles.OwnerFactionId);
            Assert.True(dalles.ZoneRadiusCm > 0);
            int capturer = FastCapturer(host.World, Remnant);
            host.Ai.Commands.Submit(SimCommand.ClaimTown(host.Tick, Remnant, capturer, dallesId));
            Step(host.World, host.World.CommandDelayTicks + 1);
            Assert.False(Rejected(host.World, capturer, CommandRejectReason.UnknownTown));
            Assert.True(host.World.GetBattalion(capturer).HasTarget);

            Assert.True(host.World.TryGetTown(remnant.HomeTownId, out TownView home));
            int node = FirstOwnedNode(host, Remnant, home.TerritoryId);
            int engineer = Engineer(host.World, Remnant);
            host.Ai.Commands.Submit(SimCommand.BuildDepot(host.Tick, Remnant, engineer, node));
            Step(host.World, host.World.CommandDelayTicks + 1);
            Assert.False(Rejected(host.World, engineer, CommandRejectReason.TerritoryNotOwned));
            Assert.False(Rejected(host.World, engineer, CommandRejectReason.UnknownNode));
            List<DepotView> depots = Depots(host.World);
            Assert.Single(depots);
            Assert.Equal(node, depots[0].NodeId);
            Assert.Equal(Remnant, depots[0].OwnerFactionId);
            host.Shutdown();
        }

        [Fact]
        public void ReplayTo_RebuildsEventHistory()
        {
            Ensure();
            byte[] mapBytes = AiSeamsTests.LoadGorgeV1Bytes();
            int[] factions = { AiConstants.DefaultAiFactionA, AiConstants.DefaultAiFactionB };
            MatchConfig config = MatchConfig.Default(19UL);
            AiBalanceTables tables = AiBalanceTablesTests.LoadBalance();
            AiMatchHost host = AiMatchHost.Start(
                config,
                mapBytes,
                factions,
                AiConstants.DifficultyNormal,
                tables);
            int dallesId = host.Ai.Map.TheDallesTownId;
            int capturer = FastCapturer(host.World, Remnant);
            host.Ai.Commands.Submit(SimCommand.ClaimTown(host.Tick, Remnant, capturer, dallesId));
            Step(host.World, 4);
            List<SimEvent> live = new List<SimEvent>();
            host.World.CopyEventsInRange(0, host.Tick, live);
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
            List<SimEvent> rebuilt = new List<SimEvent>();
            replay.World.CopyEventsInRange(0, replay.Tick, rebuilt);
            Assert.Equal(live.Count, rebuilt.Count);
            for (int i = 0; i < live.Count; i++)
            {
                Assert.Equal(live[i].EventId, rebuilt[i].EventId);
                Assert.Equal(live[i].Tick, rebuilt[i].Tick);
                Assert.Equal(live[i].Kind, rebuilt[i].Kind);
                Assert.Equal(live[i].SubjectId, rebuilt[i].SubjectId);
                Assert.Equal(live[i].AuxId, rebuilt[i].AuxId);
                Assert.Equal(live[i].Param0, rebuilt[i].Param0);
                Assert.Equal(live[i].Param1, rebuilt[i].Param1);
                Assert.Equal(live[i].Key, rebuilt[i].Key);
            }

            Assert.Equal(host.World.HashState(), replay.World.HashState());
            host.Shutdown();
            replay.Shutdown();
        }

        private static List<SimEvent> RunScript()
        {
            SimWorld world = World();
            TownView dalles = SeatTown(world, Map().TheDallesTownId);
            SpawnCapturer(world, Remnant, dalles.XCm, dalles.YCm);
            Step(world, 6);
            List<SimEvent> copy = new List<SimEvent>();
            for (int i = 0; i < world.Events.Count; i++)
            {
                copy.Add(world.Events[i]);
            }

            return copy;
        }

        private static List<SimEvent> RejectPair(bool remnantFirst)
        {
            SimWorld world = World();
            if (remnantFirst)
            {
                world.Submit(SimCommand.ClaimTown(0, Remnant, 999011, 999111));
                world.Submit(SimCommand.ClaimTown(0, Compact, 999022, 999222));
            }
            else
            {
                world.Submit(SimCommand.ClaimTown(0, Compact, 999022, 999222));
                world.Submit(SimCommand.ClaimTown(0, Remnant, 999011, 999111));
            }

            world.Step();
            List<SimEvent> rejects = new List<SimEvent>();
            for (int i = 0; i < world.Events.Count; i++)
            {
                if (world.Events[i].Kind == SimEventKind.CommandRejected
                    && world.Events[i].RejectReason == CommandRejectReason.UnknownBattalion)
                {
                    rejects.Add(world.Events[i]);
                }
            }

            return rejects;
        }

        private readonly List<TownView> _towns = new List<TownView>();

        private static SimWorld World()
        {
            Ensure();
            SimWorld world = new SimWorld(new MatchConfig(7UL, 0));
            world.BindCatalog(_catalog);
            BindSeats(world);
            world.BindHoldings(_scenario, _snapshot);
            return world;
        }

        private static SimWorld RoadWorld()
        {
            return RoadWorld(false);
        }

        private static SimWorld RoadWorld(bool fuelEnabled)
        {
            Ensure();
            SimWorld world = new SimWorld(new MatchConfig(7UL, 0));
            world.BindCatalog(_catalog);
            world.BindRoads(_movement, _roads);
            BindSeats(world, fuelEnabled);
            world.BindHoldings(_scenario, _snapshot);
            return world;
        }

        private static void BindSeats(SimWorld world)
        {
            BindSeats(world, _scenario.MatchRules.FuelEnabled);
        }

        private static void BindSeats(SimWorld world, bool fuelEnabled)
        {
            BindSeat(world, Remnant, _scenario.SeatAt(0), fuelEnabled);
            BindSeat(world, Compact, _scenario.SeatAt(1), fuelEnabled);
        }

        private static void BindSeat(SimWorld world, int factionId, ScenarioSeat seat)
        {
            BindSeat(world, factionId, seat, _scenario.MatchRules.FuelEnabled);
        }

        private static void BindSeat(SimWorld world, int factionId, ScenarioSeat seat, bool fuelEnabled)
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
                _catalog.StartingMateriel,
                _catalog.StartingFuel,
                fuelEnabled,
                _catalog.StartingSeatPopCap,
                seat.RaisableUnits,
                seat.AutoResearch,
                seat.StartingUnits));
        }

        private static int SpawnCapturer(SimWorld world, int factionId, int xCm, int yCm)
        {
            return SpawnRole(world, factionId, false, true, true, xCm, yCm);
        }

        private static int SpawnRole(
            SimWorld world,
            int factionId,
            bool build,
            bool contest,
            bool capture,
            int xCm,
            int yCm)
        {
            string faction = factionId == Remnant ? "remnant_command" : "homestead_compact";
            for (int i = 0; i < _catalog.UnitCount; i++)
            {
                if (_catalog.UnitCanBuildAt(i) != build
                    || _catalog.UnitCanContestAt(i) != contest
                    || _catalog.UnitCanCaptureAt(i) != capture)
                {
                    continue;
                }

                string unitId = _catalog.UnitIdAt(i);
                if (!_catalog.TryGetUnitFaction(unitId, out string unitFaction) || unitFaction != faction)
                {
                    continue;
                }

                return world.SpawnCatalogUnit(factionId, unitId, xCm, yCm);
            }

            throw new InvalidOperationException("No catalog unit matches the requested flags.");
        }

        private static void SilenceCombat(SimWorld world, int factionId)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != factionId || battalion.UnitIndex < 0)
                {
                    continue;
                }

                if (_catalog.UnitCanCaptureAt(battalion.UnitIndex) || _catalog.UnitCanContestAt(battalion.UnitIndex))
                {
                    world.SetBattalionStrength(battalion.Id, 0);
                }
            }
        }

        private static void RemoveFaction(SimWorld world, int factionId)
        {
            List<int> ids = new List<int>();
            for (int i = 0; i < world.BattalionCount; i++)
            {
                if (world.Battalions[i].OwnerId == factionId)
                {
                    ids.Add(world.Battalions[i].Id);
                }
            }

            for (int i = 0; i < ids.Count; i++)
            {
                world.SetBattalionStrength(ids[i], 0);
            }
        }

        private static int FastCapturer(SimWorld world, int factionId)
        {
            int best = 0;
            int bestSpeed = -1;
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != factionId || battalion.UnitIndex < 0)
                {
                    continue;
                }

                if (!_catalog.UnitCanCaptureAt(battalion.UnitIndex) || _catalog.UnitCanBuildAt(battalion.UnitIndex))
                {
                    continue;
                }

                if (battalion.SpeedCmPerSecond > bestSpeed
                    || (battalion.SpeedCmPerSecond == bestSpeed && (best == 0 || battalion.Id < best)))
                {
                    best = battalion.Id;
                    bestSpeed = battalion.SpeedCmPerSecond;
                }
            }

            if (best == 0)
            {
                throw new InvalidOperationException("No capturer.");
            }

            return best;
        }

        private static int FirstOwnedNode(AiMatchHost host, int factionId, int territoryId)
        {
            RoadGraph roads = host.Ai.Map.Roads;
            for (int i = 0; i < roads.NodeCount; i++)
            {
                if (roads.GetNodeTerritoryId(i) != territoryId)
                {
                    continue;
                }

                int nodeId = roads.GetNodeId(i);
                if (host.World.PreviewDepotPlacement(factionId, nodeId, out _, out _) == CommandRejectReason.None)
                {
                    return nodeId;
                }
            }

            throw new InvalidOperationException("No legal depot node in owned territory " + territoryId + ".");
        }

        private static bool Rejected(SimWorld world, int subjectId, CommandRejectReason reason)
        {
            for (int i = 0; i < world.Events.Count; i++)
            {
                SimEvent ev = world.Events[i];
                if (ev.Kind == SimEventKind.CommandRejected && ev.SubjectId == subjectId && ev.RejectReason == reason)
                {
                    return true;
                }
            }

            return false;
        }

        private static int Engineer(SimWorld world, int factionId)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != factionId || battalion.UnitIndex < 0)
                {
                    continue;
                }

                if (_catalog.UnitCanBuildAt(battalion.UnitIndex))
                {
                    return battalion.Id;
                }
            }

            throw new InvalidOperationException("No engineer.");
        }

        private static int CapturerBattalion(SimWorld world, int factionId)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId != factionId || battalion.UnitIndex < 0)
                {
                    continue;
                }

                if (_catalog.UnitCanCaptureAt(battalion.UnitIndex))
                {
                    return battalion.Id;
                }
            }

            throw new InvalidOperationException("No capturer.");
        }

        private static int FirstLegalNode(SimWorld world, int factionId)
        {
            Assert.True(world.TryGetSeat(factionId, out SeatView seat));
            Assert.True(world.TryGetTown(seat.HomeTownId, out TownView home));
            if (world.PreviewDepotPlacement(factionId, home.RoadNodeId, out _, out _) == CommandRejectReason.None)
            {
                return home.RoadNodeId;
            }

            return FirstLegalNodeIn(world, factionId, home.TerritoryId);
        }

        private static int FirstLegalNodeIn(SimWorld world, int factionId, int territoryId)
        {
            RoadGraph roads = Roads();
            for (int i = 0; i < roads.NodeCount; i++)
            {
                if (roads.GetNodeTerritoryId(i) != territoryId)
                {
                    continue;
                }

                int nodeId = roads.GetNodeId(i);
                if (world.PreviewDepotPlacement(factionId, nodeId, out _, out _) == CommandRejectReason.None)
                {
                    return nodeId;
                }
            }

            throw new InvalidOperationException("No legal depot node in territory " + territoryId + ".");
        }

        private static int NeighborIfTooClose(SimWorld world, int factionId, int nodeId)
        {
            RoadGraph roads = Roads();
            Assert.True(roads.TryGetNodeIndex(nodeId, out int index));
            int start = roads.AdjOffset[index];
            int end = roads.AdjOffset[index + 1];
            for (int edge = start; edge < end; edge++)
            {
                int neighbor = roads.AdjNeighborIndex[edge];
                int neighborId = roads.GetNodeId(neighbor);
                if (world.PreviewDepotPlacement(factionId, neighborId, out _, out _) == CommandRejectReason.DepotTooClose)
                {
                    return neighborId;
                }
            }

            for (int i = 0; i < roads.NodeCount; i++)
            {
                int candidate = roads.GetNodeId(i);
                if (candidate == nodeId)
                {
                    continue;
                }

                if (world.PreviewDepotPlacement(factionId, candidate, out _, out _) == CommandRejectReason.DepotTooClose)
                {
                    return candidate;
                }
            }

            return 0;
        }

        private static TownView SeatTown(SimWorld world, int townId)
        {
            Assert.True(world.TryGetTown(townId, out TownView town));
            Assert.True(town.IsSeatTown);
            return town;
        }

        private static bool Inside(int xCm, int yCm, TownView town)
        {
            long dx = (long)xCm - town.XCm;
            long dy = (long)yCm - town.YCm;
            long radius = town.ZoneRadiusCm;
            return (dx * dx) + (dy * dy) <= radius * radius;
        }

        private static bool DisksOverlap(TownView a, TownView b)
        {
            long dx = (long)a.XCm - b.XCm;
            long dy = (long)a.YCm - b.YCm;
            long reach = (long)a.ZoneRadiusCm + b.ZoneRadiusCm;
            return (dx * dx) + (dy * dy) <= reach * reach;
        }

        private static bool PointInBoth(TownView a, TownView b, out int x, out int y)
        {
            for (int step = 0; step <= 1024; step++)
            {
                int px = a.XCm + (int)(((long)(b.XCm - a.XCm) * step) / 1024);
                int py = a.YCm + (int)(((long)(b.YCm - a.YCm) * step) / 1024);
                if (Inside(px, py, a) && Inside(px, py, b))
                {
                    x = px;
                    y = py;
                    return true;
                }
            }

            x = 0;
            y = 0;
            return false;
        }

        private static void ClearNeutralGarrisons(SimWorld world)
        {
            for (int i = 0; i < world.BattalionCount; i++)
            {
                SimBattalion battalion = world.Battalions[i];
                if (battalion.OwnerId == SimWorld.NeutralOwnerId)
                {
                    world.SetBattalionStrength(battalion.Id, 0);
                }
            }
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

        private static int Materials(SimWorld world, int factionId)
        {
            Assert.True(world.TryGetSeat(factionId, out SeatView seat));
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

        private static DepotView DepotOn(SimWorld world, int nodeId)
        {
            List<DepotView> depots = Depots(world);
            for (int i = 0; i < depots.Count; i++)
            {
                if (depots[i].NodeId == nodeId)
                {
                    return depots[i];
                }
            }

            throw new InvalidOperationException("No depot on node.");
        }

        private static int CountKind(SimWorld world, SimEventKind kind)
        {
            int count = 0;
            for (int i = 0; i < world.Events.Count; i++)
            {
                if (world.Events[i].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasKind(SimWorld world, SimEventKind kind)
        {
            return CountKind(world, kind) > 0;
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

        private static NamedTown FindTown(string name)
        {
            Ensure();
            for (int i = 0; i < _named.Length; i++)
            {
                if (_named[i].Name == name)
                {
                    return _named[i];
                }
            }

            throw new InvalidOperationException("Map has no town named " + name + ".");
        }

        private static BalanceCatalog Catalog()
        {
            Ensure();
            return _catalog;
        }

        private static AiMapSnapshot Map()
        {
            Ensure();
            return _snapshot;
        }

        private static RoadGraph Roads()
        {
            Ensure();
            return _roads;
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
                int x = reader.I32();
                int y = reader.I32();
                reader.Seek((i * towns.ElementSize) + 52);
                int nameOff = (int)reader.U32();
                int nameLen = (int)reader.U32();
                named[i] = new NamedTown(id, x, y, Encoding.UTF8.GetString(pool, nameOff, nameLen));
            }

            return named;
        }

        private static AiOwnedState EmptyOwned()
        {
            AiFactionState faction = new AiFactionState(
                Remnant,
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
                50000,
                4,
                4,
                AiPosture.Attack,
                new SimRandom(1UL),
                new SimRandom(3UL),
                new SimRandom(5UL),
                new SimRandom(7UL));
            return new AiOwnedState(new[] { faction }, 1);
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
            public readonly int XCm;
            public readonly int YCm;
            public readonly string Name;

            public NamedTown(int id, int xCm, int yCm, string name)
            {
                Id = id;
                XCm = xCm;
                YCm = yCm;
                Name = name;
            }
        }
    }
}
