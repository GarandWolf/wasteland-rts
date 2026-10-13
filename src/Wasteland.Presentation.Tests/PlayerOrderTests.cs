#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    public sealed class PlayerOrderTests
    {
        [Fact]
        public void RejectText_CoversEverySimReason_IncludingResearch()
        {
            Assert.Equal(31, (int)CommandRejectReason.ResearchInProgress);
            Assert.Equal(32, (int)CommandRejectReason.TierAlreadyReached);
            Assert.Equal(33, (int)CommandRejectReason.NoQualifyingTier);
            Assert.Equal(string.Empty, PlayerRejectText.Describe(CommandRejectReason.None, 0, 0));

            CommandRejectReason[] reasons = (CommandRejectReason[])Enum.GetValues(typeof(CommandRejectReason));
            var seen = new HashSet<string>();
            for (int i = 0; i < reasons.Length; i++)
            {
                CommandRejectReason reason = reasons[i];
                string text = PlayerRejectText.Describe(reason, 4, 9);
                if (reason == CommandRejectReason.None)
                {
                    Assert.Equal(string.Empty, text);
                    continue;
                }

                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.DoesNotContain("squad", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Order rejected (", text, StringComparison.Ordinal);
                Assert.True(seen.Add(text));
            }

            Assert.Contains("already in progress", PlayerRejectText.Describe(CommandRejectReason.ResearchInProgress, 0, 0), StringComparison.Ordinal);
            Assert.Contains("already has that tier", PlayerRejectText.Describe(CommandRejectReason.TierAlreadyReached, 0, 0), StringComparison.Ordinal);
            Assert.Contains("qualifies", PlayerRejectText.Describe(CommandRejectReason.NoQualifyingTier, 0, 0), StringComparison.Ordinal);
        }

        [Fact]
        public void GroundPick_ClampsToTheMapEdge_ThroughMapSpace()
        {
            const int origin = 1000;
            const int extent = 8000;
            const int cells = 4;
            int x;
            int y;
            int column;
            int row;
            MapSpace.PickGround(-50.0, 1000.0, origin, origin, cells, cells, extent, extent, out x, out y, out column, out row);
            Assert.Equal(origin, x);
            Assert.Equal(0, column);
            Assert.Equal(MapSpace.Column(x, origin, cells, extent), column);

            MapSpace.PickGround(500.0, -1.0, origin, origin, cells, cells, extent, extent, out x, out y, out column, out row);
            Assert.Equal(origin + extent, x);
            Assert.Equal(cells - 1, column);
            Assert.Equal(origin, y);
            Assert.Equal(0, row);
            Assert.Equal(MapSpace.CellIndex(x, y, origin, origin, cells, cells, extent, extent), row * cells + column);
        }

        [Fact]
        public void Move_SnapsToTheNearestRuntimeNode()
        {
            RoadGraph east = Graph(17, 0, 0, 44, 10000, 0);
            RoadGraph west = Graph(17, 10000, 0, 44, 0, 0);
            SimWorld world = new SimWorld(new MatchConfig(3UL, 2));
            PlayerOrderSession eastSession = Session(world, east, 1, true);
            eastSession.SelectBattalion(8);
            PlayerOrderNotice toEast = eastSession.Move(0, 0, 9000, 10, Rgb.FromHex(MarkerArt.Remnant));
            Assert.True(toEast.Accepted, toEast.Reason);
            Assert.Equal(CommandKind.Move, toEast.Command.Kind);
            Assert.Equal(10000, toEast.Command.Arg0);
            Assert.Equal(0, toEast.Command.Arg1);
            Assert.Equal(world.Tick + world.CommandDelayTicks, toEast.Command.ExecuteTick);

            PlayerOrderSession westSession = Session(world, west, 1, true);
            westSession.SelectBattalion(8);
            PlayerOrderNotice toWest = westSession.Move(0, 0, 100, 10, Rgb.FromHex(MarkerArt.Neutral));
            Assert.True(toWest.Accepted, toWest.Reason);
            Assert.Equal(0, toWest.Command.Arg0);
            PlayerOrderNotice eastDepot = eastSession.BuildDepot(0, 0, 9000, 0, Rgb.FromHex(MarkerArt.Remnant));
            PlayerOrderNotice westDepot = westSession.BuildDepot(0, 0, 9000, 0, Rgb.FromHex(MarkerArt.Remnant));
            Assert.Equal(44, eastDepot.Command.Arg0);
            Assert.Equal(17, westDepot.Command.Arg0);
            Assert.NotEqual(eastDepot.Command.Arg0, westDepot.Command.Arg0);
            OrderLine line = westSession.Lines()[0];
            Assert.Equal(MarkerArt.Ink, Pack(line.Color));
            Assert.NotEqual(MarkerArt.Neutral, Pack(line.Color));
        }

        [Fact]
        public void DisallowedKinds_DoNotEnterTheCommandStream()
        {
            CountingStream stream = new CountingStream();
            PlayerOrderSession session = new PlayerOrderSession(stream, 1, true, Graph(3, 0, 0, 5, 10, 0));
            PlayerOrderNotice spawn = session.Refuse(CommandKind.SpawnBattalion);
            PlayerOrderNotice build = session.Refuse(CommandKind.Build);
            Assert.False(spawn.Accepted);
            Assert.False(string.IsNullOrWhiteSpace(spawn.Reason));
            Assert.False(build.Accepted);
            Assert.False(string.IsNullOrWhiteSpace(build.Reason));
            Assert.Equal(0, stream.Submits);
            Assert.False(PlayerOrderSet.IsAllowed(CommandKind.SpawnBattalion));
            Assert.False(PlayerOrderSet.IsAllowed(CommandKind.Build));
            Assert.True(PlayerOrderSet.IsAllowed(CommandKind.Move));
            Assert.True(PlayerOrderSet.IsAllowed(CommandKind.Attack));
            Assert.True(PlayerOrderSet.IsAllowed(CommandKind.ClaimTown));
            Assert.True(PlayerOrderSet.IsAllowed(CommandKind.BuildDepot));
            Assert.True(PlayerOrderSet.IsAllowed(CommandKind.RaiseBattalion));
            Assert.True(PlayerOrderSet.IsAllowed(CommandKind.ResearchTech));
        }

        [Fact]
        public void OrderClick_IsHumanSeatOnly()
        {
            Assert.True(OrderClick.ForHumanSeat(true));
            Assert.False(OrderClick.ForHumanSeat(false));
            Assert.Equal("ui_click.wav", OrderClick.KenneyFileName);
            PlayerOrderSession silent = Session(new SimWorld(new MatchConfig(1UL, 0)), Graph(3, 0, 0, 5, 10, 0), 1, false);
            silent.SelectBattalion(4);
            PlayerOrderNotice notice = silent.Move(0, 0, 1, 1, Rgb.FromHex(MarkerArt.Remnant));
            Assert.False(notice.PlayClick);
            Assert.Contains("human seat", notice.Reason, StringComparison.Ordinal);
        }

        [Fact]
        public void SelectionOutline_ReusesBoneAndInk()
        {
            Assert.Equal(2f, SelectionOutline.BonePx);
            Assert.Equal(1f, SelectionOutline.InkPx);
            Assert.Equal(3f, SelectionOutline.PadPx);
            Assert.Equal(MarkerArt.Bone, Pack(SelectionOutline.Bone));
            Assert.Equal(MarkerArt.Ink, Pack(SelectionOutline.Ink));
            Assert.NotEqual(MarkerArt.Neutral, Pack(SelectionOutline.Ink));
            Assert.Equal(MarkerArt.Ink, Pack(OrderLineColor.Resolve(Rgb.FromHex(MarkerArt.Neutral))));
            Assert.Equal(MarkerArt.Remnant, Pack(OrderLineColor.Resolve(Rgb.FromHex(MarkerArt.Remnant))));
            Assert.Equal(MarkerArt.Compact, Pack(OrderLineColor.Resolve(Rgb.FromHex(MarkerArt.Compact))));
        }

        [Fact]
        public void TierTwoLabel_IsTwoHundredFuelAndEighteenHundredTicks()
        {
            BalanceCatalog catalog = BalanceCatalog.LoadFromDirectory(BalanceDirectory());
            Assert.Equal("Research Tier II (200 fuel, 1800 ticks)", PlayerOrderSession.TierTwoLabel(catalog));
        }

        [Fact]
        public void Research_UsesTheCommandStream_AndSurfacesInProgress()
        {
            BalanceCatalog catalog = BalanceCatalog.LoadFromDirectory(BalanceDirectory());
            SimWorld world = new SimWorld(new MatchConfig(11UL, 2));
            world.BindCatalog(catalog);
            world.BindSeat(Seat(1, "remnant_command", new[] { "remnant_motor_rifles" }, 1, true));
            PlayerOrderSession session = new PlayerOrderSession(new SimPlayerCommandStream(world), 1, true, Graph(3, 0, 0, 5, 40, 0));
            PlayerOrderNotice first = session.ResearchTierTwo();
            Assert.True(first.Accepted, first.Reason);
            Assert.Equal(CommandKind.ResearchTech, first.Command.Kind);
            Assert.Equal(0, first.Command.BattalionId);
            Assert.Equal(world.CommandDelayTicks, first.Command.ExecuteTick - first.Command.IssuedTick);
            StepThrough(world, first.Command.ExecuteTick);
            Assert.Equal(first.Command.ExecuteTick + 1800, SeatOf(world, 1).TierResearchCompleteTick);

            PlayerOrderNotice second = session.ResearchTierTwo();
            StepThrough(world, second.Command.ExecuteTick);
            string reason = session.PollRejection();
            Assert.Equal(PlayerRejectText.Describe(CommandRejectReason.ResearchInProgress, 0, 0), reason);
            Assert.Contains("already in progress", reason, StringComparison.Ordinal);
        }

        [Fact]
        public void Research_AlreadyAtTier_SurfacesTierAlreadyReached()
        {
            BalanceCatalog catalog = BalanceCatalog.LoadFromDirectory(BalanceDirectory());
            SimWorld world = new SimWorld(new MatchConfig(12UL, 0));
            world.BindCatalog(catalog);
            world.BindSeat(Seat(1, "remnant_command", new[] { "remnant_motor_rifles" }, 2, true));
            PlayerOrderSession session = new PlayerOrderSession(new SimPlayerCommandStream(world), 1, true, null);
            PlayerOrderNotice notice = session.ResearchTierTwo();
            StepThrough(world, notice.Command.ExecuteTick);
            string reason = notice.Reason;
            if (string.IsNullOrEmpty(reason))
            {
                reason = session.PollRejection();
            }

            Assert.Equal(PlayerRejectText.Describe(CommandRejectReason.TierAlreadyReached, 2, 2), reason);
        }

        [Fact]
        public void Attack_WaitsForCommandDelay_AndMatchesTheAiView()
        {
            BalanceCatalog catalog = BalanceCatalog.LoadFromDirectory(BalanceDirectory());
            SimWorld world = new SimWorld(new MatchConfig(13UL, 4));
            world.BindCatalog(catalog);
            world.BindSeat(Seat(1, "remnant_command", new[] { "remnant_motor_rifles" }, 1, false));
            world.BindSeat(Seat(2, "homestead_compact", new[] { "compact_militia" }, 1, false));
            RoadGraph roads = Graph(17, 0, 0, 44, 5000, 0);
            SimPlayerCommandStream playerStream = new SimPlayerCommandStream(world);
            PlayerOrderSession player = new PlayerOrderSession(playerStream, 1, true, roads);
            PlayerOrderNotice raised = player.Raise("remnant_motor_rifles", 100, 0, Rgb.FromHex(MarkerArt.Remnant));
            Assert.True(raised.Accepted, raised.Reason);
            SimWorldCommandSink ai = new SimWorldCommandSink(world);
            SimCommand aiRaised = ai.SubmitRaiseBattalion(2, "compact_militia", 5000, 0);
            WaitForBattalion(world, raised.Command.BattalionId);
            WaitForBattalion(world, aiRaised.BattalionId);

            player.SelectBattalion(raised.Command.BattalionId);
            int tick = world.Tick;
            PlayerOrderNotice attack = player.Attack(0, 0, aiRaised.BattalionId, 5000, 0, Rgb.FromHex(MarkerArt.Remnant));
            Assert.True(attack.Accepted, attack.Reason);
            Assert.Equal(tick + world.CommandDelayTicks, attack.Command.ExecuteTick);
            Assert.Equal(CommandKind.Attack, attack.Command.Kind);
            while (world.Tick < attack.Command.ExecuteTick)
            {
                world.Step();
            }

            Assert.Equal(0, View(world, raised.Command.BattalionId).AttackTargetId);
            world.Step();
            Assert.Equal(aiRaised.BattalionId, View(world, raised.Command.BattalionId).AttackTargetId);

            SimCommand aiAttack = ai.Submit(SimCommand.Attack(world.Tick, 2, aiRaised.BattalionId, raised.Command.BattalionId));
            Assert.Equal(world.CommandDelayTicks, aiAttack.ExecuteTick - aiAttack.IssuedTick);
            while (world.Tick <= aiAttack.ExecuteTick)
            {
                world.Step();
            }

            SimWorldCommandSink reader = new SimWorldCommandSink(world);
            reader.BindRoads(roads);
            List<AiCombatantView> enemiesOfAi = new List<AiCombatantView>();
            reader.CopyCombatants(2, enemiesOfAi);
            Assert.Contains(enemiesOfAi, view => view.Id == raised.Command.BattalionId && view.AttackTargetId == aiRaised.BattalionId);

            List<AiCombatantView> enemiesOfPlayer = new List<AiCombatantView>();
            reader.CopyCombatants(1, enemiesOfPlayer);
            Assert.Contains(enemiesOfPlayer, view => view.Id == aiRaised.BattalionId && view.AttackTargetId == raised.Command.BattalionId);
            Assert.Contains(enemiesOfPlayer, view => view.OwnerSeat != 1);
        }

        [Fact]
        public void NotOwner_IsShownAfterTheDelay()
        {
            BalanceCatalog catalog = BalanceCatalog.LoadFromDirectory(BalanceDirectory());
            SimWorld world = new SimWorld(new MatchConfig(14UL, 3));
            world.BindCatalog(catalog);
            world.BindSeat(Seat(1, "remnant_command", new[] { "remnant_motor_rifles" }, 1, false));
            world.BindSeat(Seat(2, "homestead_compact", new[] { "compact_militia" }, 1, false));
            RoadGraph roads = Graph(9, 0, 0, 12, 800, 0);
            SimWorldCommandSink ai = new SimWorldCommandSink(world);
            SimCommand enemy = ai.SubmitRaiseBattalion(2, "compact_militia", 800, 0);
            WaitForBattalion(world, enemy.BattalionId);
            PlayerOrderSession player = new PlayerOrderSession(new SimPlayerCommandStream(world), 1, true, roads);
            player.SelectBattalion(enemy.BattalionId);
            PlayerOrderNotice move = player.Move(800, 0, 0, 0, Rgb.FromHex(MarkerArt.Compact));
            Assert.True(string.IsNullOrEmpty(move.Reason), move.Reason);
            StepThrough(world, move.Command.ExecuteTick);
            Assert.Equal(PlayerRejectText.Describe(CommandRejectReason.NotOwner, 0, 0), player.PollRejection());
        }

        static int Pack(Rgb rgb)
        {
            return (rgb.R << 16) | (rgb.G << 8) | rgb.B;
        }

        static BattalionView View(SimWorld world, int battalionId)
        {
            Assert.True(world.TryGetBattalionView(battalionId, out BattalionView view));
            return view;
        }

        static SeatView SeatOf(SimWorld world, int factionId)
        {
            Assert.True(world.TryGetSeat(factionId, out SeatView seat));
            return seat;
        }

        static void StepThrough(SimWorld world, int executeTick)
        {
            int guard = 0;
            while (world.Tick <= executeTick)
            {
                world.Step();
                guard++;
                Assert.True(guard < 10000);
            }
        }

        static void WaitForBattalion(SimWorld world, int battalionId)
        {
            int guard = 0;
            while (!world.TryGetBattalionView(battalionId, out _))
            {
                world.Step();
                guard++;
                Assert.True(guard < 5000, "Battalion " + battalionId + " was not raised.");
            }
        }

        static PlayerOrderSession Session(SimWorld world, RoadGraph roads, int seat, bool human)
        {
            return new PlayerOrderSession(new SimPlayerCommandStream(world), seat, human, roads);
        }

        static SeatSetup Seat(int factionId, string factionKey, string[] raisable, int tier, bool fuel)
        {
            return new SeatSetup(
                factionId,
                factionId,
                factionKey,
                1,
                0,
                0,
                tier,
                -1,
                800,
                800,
                800,
                fuel,
                8,
                raisable,
                Array.Empty<ScenarioAutoResearch>(),
                Array.Empty<ScenarioUnitCount>());
        }

        static RoadGraph Graph(int leftId, int leftX, int leftY, int rightId, int rightX, int rightY)
        {
            byte[] blob = new RoadGraphBuilder()
                .AddNode(leftId, leftX, leftY)
                .AddNode(rightId, rightX, rightY)
                .AddEdge(1, leftId, rightId, 100, RoadTier.Dirt, RoadSurface.Dirt)
                .ToBlob();
            return Wrmb.ReadRoadGraph(blob);
        }

        static string BalanceDirectory()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "data", "balance");
                if (File.Exists(Path.Combine(candidate, "units.json")))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            string cwd = Path.Combine(Directory.GetCurrentDirectory(), "data", "balance");
            if (File.Exists(Path.Combine(cwd, "units.json")))
            {
                return cwd;
            }

            throw new InvalidOperationException("Could not find data/balance/units.json.");
        }

        sealed class CountingStream : IPlayerCommandStream
        {
            public int Submits;

            public int Tick
            {
                get { return 0; }
            }

            public int CommandDelayTicks
            {
                get { return 2; }
            }

            public SimCommand Submit(SimCommand command)
            {
                Submits++;
                return command;
            }

            public SimCommand SubmitRaiseBattalion(int factionId, string unitId, int rallyXCm, int rallyYCm)
            {
                Submits++;
                return default(SimCommand);
            }

            public SimCommand SubmitResearchTech(int factionId, string techId)
            {
                Submits++;
                return default(SimCommand);
            }

            public bool TryGetRejection(int issuerId, int sequence, out CommandRejectReason code, out int param0, out int param1)
            {
                code = CommandRejectReason.None;
                param0 = 0;
                param1 = 0;
                return false;
            }
        }
    }
}
