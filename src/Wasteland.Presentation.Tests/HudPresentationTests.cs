#nullable disable
using Wasteland.Presentation;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    public sealed class HudPresentationTests
    {
        [Fact]
        public void UnderAttack_ThrottlesPerTown_AndHqBypasses()
        {
            UnderAttackAlerts alerts = new UnderAttackAlerts();
            Rgb remnant = Rgb.FromHex(MarkerArt.Remnant);

            Assert.True(alerts.TryTown(10, 30, false, 100, 200, true, remnant, out UnderAttackAlert first));
            Assert.True(first.PlayCue);
            Assert.Equal(UnderAttackAlerts.Volume, first.Volume);
            Assert.False(first.Spatial);
            Assert.Equal(UnderAttackAlerts.CueFile, first.CueFile);
            Assert.Equal(100, first.XCm);
            Assert.Equal(200, first.YCm);
            Assert.True(first.ShowRing);
            Assert.Equal(MarkerArt.Remnant, Pack(first.Fill));
            Assert.Equal(MarkerArt.Ink, Pack(first.Outline));
            Assert.NotEqual(MarkerArt.Neutral, Pack(first.Fill));

            Assert.False(alerts.TryTown(10 + UnderAttackAlerts.CooldownTicks - 1, 30, false, 100, 200, true, remnant, out _));
            Assert.True(alerts.TryTown(10 + UnderAttackAlerts.CooldownTicks, 30, false, 140, 240, true, remnant, out UnderAttackAlert later));
            Assert.Equal(140, later.XCm);

            Assert.True(alerts.TryTown(11, 26, true, 9, 8, true, remnant, out UnderAttackAlert hq));
            Assert.True(alerts.TryTown(12, 26, true, 9, 8, true, remnant, out UnderAttackAlert hqAgain));
            Assert.True(hq.PlayCue);
            Assert.True(hqAgain.PlayCue);
            Assert.Equal(9, hqAgain.XCm);

            Assert.False(alerts.TryTown(50, 30, false, 1, 1, false, remnant, out UnderAttackAlert spectator));
            Assert.False(spectator.PlayCue);
            Assert.False(spectator.ShowRing);

            Rgb grey = Rgb.FromHex(MarkerArt.Neutral);
            Assert.True(alerts.TryTown(800, 4, false, 3, 4, true, grey, out UnderAttackAlert inked));
            Assert.Equal(MarkerArt.Ink, Pack(inked.Fill));
            Assert.Equal(MarkerArt.Ink, Pack(inked.Outline));
            Assert.NotEqual(MarkerArt.Neutral, Pack(inked.Fill));
        }

        [Fact]
        public void UnderAttack_BattalionCooldown_HidesTheEnemyOrder()
        {
            UnderAttackAlerts alerts = new UnderAttackAlerts();
            Rgb compact = Rgb.FromHex(MarkerArt.Compact);
            Assert.True(alerts.TryBattalion(0, 7, 40, 50, true, compact, out UnderAttackAlert hit));
            Assert.Equal(40, hit.XCm);
            Assert.Equal(50, hit.YCm);
            Assert.False(alerts.TryBattalion(100, 7, 40, 50, true, compact, out _));
            Assert.True(alerts.TryBattalion(UnderAttackAlerts.CooldownTicks, 7, 41, 51, true, compact, out _));
            Assert.False(alerts.TryBattalion(0, 7, 40, 50, false, compact, out _));
            Assert.False(EnemyIntent.ShowOrders);
            Assert.False(EnemyIntent.ShowTargets);
            Assert.False(hit.RevealsEnemyOrder);
        }

        [Fact]
        public void AlertRing_PulsesInFactionColour()
        {
            float low = AlertRingStyle.Pulse(0f);
            float high = AlertRingStyle.Pulse(0.25f);
            Assert.InRange(low, 0.69f, 1.01f);
            Assert.InRange(high, 0.69f, 1.01f);
            Assert.NotEqual(low, high);
            Assert.Equal(MarkerArt.Ink, Pack(AlertRingStyle.Outline));
            Assert.Equal(MarkerArt.Compact, Pack(AlertRingStyle.Fill(Rgb.FromHex(MarkerArt.Compact))));
            Assert.Equal(MarkerArt.Ink, Pack(AlertRingStyle.Fill(Rgb.FromHex(MarkerArt.Neutral))));
        }

        [Fact]
        public void HqOutcome_FollowsTheHumanSeat_AndDrawPlaysDefeat()
        {
            HqBanner remnantWins = HqOutcome.Read(true, HqOutcome.ReasonHqCaptured, 1, true, 1, "Homestead Compact");
            Assert.True(remnantWins.Show);
            Assert.Equal("Victory", remnantWins.Title);
            Assert.Equal(HqOutcome.WinFile, remnantWins.StingerFile);
            Assert.True(remnantWins.Play);

            HqBanner compactLoses = HqOutcome.Read(true, HqOutcome.ReasonHqCaptured, 1, true, 2, "Remnant Command");
            Assert.Equal("Defeat", compactLoses.Title);
            Assert.Equal(HqOutcome.DefeatFile, compactLoses.StingerFile);

            HqBanner compactWins = HqOutcome.Read(true, HqOutcome.ReasonHqCaptured, 2, true, 2, "Homestead Compact");
            Assert.Equal("Victory", compactWins.Title);
            Assert.Equal(HqOutcome.WinFile, compactWins.StingerFile);

            HqBanner draw = HqOutcome.Read(true, HqOutcome.ReasonDraw, -1, true, 1, "Remnant Command");
            Assert.Equal("Defeat", draw.Title);
            Assert.Equal(HqOutcome.DefeatFile, draw.StingerFile);
            Assert.DoesNotContain("draw_stinger", draw.StingerFile);

            HqBanner spectator = HqOutcome.Read(true, HqOutcome.ReasonHqCaptured, 2, false, 0, "Homestead Compact");
            Assert.Equal("Victory", spectator.Title);
            Assert.Contains("Homestead Compact", spectator.Detail);
            Assert.Equal(HqOutcome.WinFile, spectator.StingerFile);

            HqBanner spectatorDraw = HqOutcome.Read(true, HqOutcome.ReasonDraw, -1, false, 0, "Remnant Command");
            Assert.Equal("Defeat", spectatorDraw.Title);
            Assert.Equal(HqOutcome.DefeatFile, spectatorDraw.StingerFile);
            Assert.Equal(string.Empty, spectatorDraw.Detail);

            Assert.False(HqOutcome.Read(false, HqOutcome.ReasonHqCaptured, 1, true, 1, "Remnant Command").Show);
            Assert.False(HqOutcome.Read(true, 0, 1, true, 1, "Remnant Command").Show);
            Assert.False(HqOutcome.Read(true, 9, 1, true, 1, "Remnant Command").Show);

            HqOutcomeGate gate = new HqOutcomeGate();
            Assert.True(gate.Read(true, HqOutcome.ReasonHqCaptured, 1, true, 1, "Remnant Command").Play);
            Assert.False(gate.Read(true, HqOutcome.ReasonHqCaptured, 1, true, 1, "Remnant Command").Play);
        }

        [Fact]
        public void ResearchBar_IsClamped_OwnSeatOnly_AndHidesWhenTheTierLands()
        {
            ResearchBarView hidden = ResearchBar.Sample(false, 10, 1810, 100, false);
            Assert.False(hidden.Visible);

            ResearchBarView early = ResearchBar.Sample(true, 10, 1810, 10, false);
            Assert.True(early.Visible);
            Assert.Equal(0f, early.Fill);

            ResearchBarView mid = ResearchBar.Sample(true, 10, 1810, 910, false);
            Assert.Equal(0.5f, mid.Fill, 3);

            ResearchBarView before = ResearchBar.Sample(true, 10, 1810, 0, false);
            Assert.Equal(0f, before.Fill);

            ResearchBarView after = ResearchBar.Sample(true, 10, 1810, 5000, false);
            Assert.Equal(1f, after.Fill);

            ResearchBarView done = ResearchBar.Sample(true, 10, 1810, 1810, true);
            Assert.False(done.Visible);

            Assert.False(ResearchBar.Sample(true, -1, -1, 20, false).Visible);
        }

        [Fact]
        public void ResearchCue_PlaysOnEvent6_ForTheViewerRules()
        {
            Assert.Equal(6, (int)SimEventKind.TierReached);
            ResearchCueGate gate = new ResearchCueGate();
            Assert.True(gate.TryConsume(6, true, 1, 1, 2, out float human));
            Assert.Equal(ResearchCue.HumanVolume, human);
            Assert.False(gate.TryConsume(6, true, 1, 1, 2, out _));
            Assert.False(gate.TryConsume(6, true, 1, 2, 2, out _));
            Assert.False(gate.TryConsume(5, true, 1, 1, 2, out _));

            ResearchCueGate watching = new ResearchCueGate();
            Assert.True(watching.TryConsume(6, false, 0, 1, 2, out float first));
            Assert.True(watching.TryConsume(6, false, 0, 2, 2, out float second));
            Assert.Equal(ResearchCue.SpectatorVolume, first);
            Assert.Equal(ResearchCue.SpectatorVolume, second);
            Assert.False(watching.TryConsume(6, false, 0, 1, 2, out _));
            Assert.Equal(ResearchCue.FileName, ResearchCue.CueFile);
            Assert.False(ResearchCue.Spatial);
        }

        [Fact]
        public void RejectText_NamesTheTier_AndSpeaksPlainly()
        {
            Assert.Equal("Requires Tier II research.", PlayerRejectText.Describe(CommandRejectReason.TierLocked, 2, 1));
            Assert.Equal("Requires Tier I research.", PlayerRejectText.Describe(CommandRejectReason.TierLocked, 1, 0));
            Assert.Equal("Requires Tier III research.", PlayerRejectText.Describe(CommandRejectReason.TierLocked, 3, 2));
            Assert.Equal("That battalion is already in the fight.", PlayerRejectText.Describe(CommandRejectReason.DuplicateBattalion, 0, 0));
            Assert.Equal("A battalion cannot move backwards.", PlayerRejectText.Describe(CommandRejectReason.NegativeSpeed, 0, 0));
            Assert.DoesNotContain("Duplicate", PlayerRejectText.Describe(CommandRejectReason.DuplicateBattalion, 0, 0));
            Assert.DoesNotContain("Negative", PlayerRejectText.Describe(CommandRejectReason.NegativeSpeed, 0, 0));
        }

        [Fact]
        public void SoftDeny_IsADifferentKenneyClick_ForTheHumanSeatOnly()
        {
            Assert.Equal("order_deny.wav", OrderDeny.KenneyFileName);
            Assert.NotEqual(OrderClick.KenneyFileName, OrderDeny.KenneyFileName);
            Assert.Equal("click2.ogg", OrderDeny.KenneySource);
            Assert.True(OrderDeny.ForHumanSeat(true));
            Assert.False(OrderDeny.ForHumanSeat(false));
            Assert.False(OrderDeny.Spatial);

            PlayerOrderSession human = new PlayerOrderSession(new RejectingStream(CommandRejectReason.TierLocked, 2, 1), 1, true, null);
            PlayerOrderNotice denied = human.ResearchTierTwo();
            Assert.False(denied.Accepted);
            Assert.True(denied.SoftDeny);
            Assert.Equal("Requires Tier II research.", denied.Reason);

            PlayerOrderSession spectator = new PlayerOrderSession(new RejectingStream(CommandRejectReason.TierLocked, 2, 1), 1, false, null);
            spectator.SelectBattalion(4);
            PlayerOrderNotice silent = spectator.Move(0, 0, 1, 1, Rgb.FromHex(MarkerArt.Remnant));
            Assert.False(silent.SoftDeny);
            Assert.False(silent.PlayClick);
        }

        [Fact]
        public void DepotButton_StaysDisabledUntilARoadIsClicked()
        {
            Assert.False(DepotButton.Enabled(false));
            Assert.Equal("Click a road to place a depot.", DepotButton.Tooltip(false));
            Assert.True(DepotButton.Enabled(true));
            Assert.Equal(string.Empty, DepotButton.Tooltip(true));
        }

        [Fact]
        public void Raise_UsesTheClickedRoad_OrTheScenarioHqNode()
        {
            MapTown[] towns =
            {
                new MapTown { Id = 33, Name = "Goldendale", RoadNodeId = 44, XCm = 10, YCm = 20 },
                new MapTown { Id = 26, Name = "Hood River", RoadNodeId = 769, XCm = 90, YCm = 80 }
            };
            RoadNodePin[] nodes =
            {
                new RoadNodePin(44, 1000, 2000),
                new RoadNodePin(769, 9000, 8000),
                new RoadNodePin(1, 0, 0)
            };

            Assert.True(HqNode.TryFromHomeTown(33, towns, nodes, out int nodeId, out int x, out int y));
            Assert.Equal(44, nodeId);
            Assert.Equal(1000, x);
            Assert.Equal(2000, y);
            Assert.NotEqual(769, nodeId);

            Assert.True(OrderAnchor.TryPlace(true, 12, 50, 60, true, nodeId, x, y, out int placed, out int px, out int py));
            Assert.Equal(12, placed);
            Assert.Equal(50, px);
            Assert.Equal(60, py);

            Assert.True(OrderAnchor.TryPlace(false, 0, 0, 0, true, nodeId, x, y, out int hq, out int hx, out int hy));
            Assert.Equal(44, hq);
            Assert.Equal(1000, hx);
            Assert.Equal(2000, hy);

            Assert.False(OrderAnchor.TryPlace(false, 0, 0, 0, false, 0, 0, 0, out _, out _, out _));

            MapRoad road = new MapRoad
            {
                Id = 1,
                FromNodeId = 44,
                ToNodeId = 12,
                XCm = new[] { 1000, 5000 },
                YCm = new[] { 2000, 2000 }
            };
            Assert.True(RoadPick.TryHit(new[] { road }, 3000, 2100, RoadPick.ClickRadiusCm, out int hitNode, out int hitX, out int hitY));
            Assert.True(hitNode == 44 || hitNode == 12);
            Assert.True(hitX == 1000 || hitX == 5000);
            Assert.Equal(2000, hitY);
            Assert.False(RoadPick.TryHit(new[] { road }, 3000, 2000 + RoadPick.ClickRadiusCm + 50, RoadPick.ClickRadiusCm, out _, out _, out _));
        }

        [Fact]
        public void TowedArtillery_HasItsOwnGlyph()
        {
            BattalionGlyph towed;
            BattalionGlyph tracked;
            Assert.True(MarkerArt.TryGlyph("compact_towed_artillery", out towed));
            Assert.True(MarkerArt.TryGlyph("remnant_sp_artillery", out tracked));
            Assert.Equal(BattalionGlyph.Towed, towed);
            Assert.Equal(BattalionGlyph.Artillery, tracked);
            Assert.NotEqual(towed, tracked);
        }

        static int Pack(Rgb rgb)
        {
            return (rgb.R << 16) | (rgb.G << 8) | rgb.B;
        }

        sealed class RejectingStream : IPlayerCommandStream
        {
            readonly CommandRejectReason _reason;
            readonly int _param0;
            readonly int _param1;

            public RejectingStream(CommandRejectReason reason, int param0, int param1)
            {
                _reason = reason;
                _param0 = param0;
                _param1 = param1;
            }

            public int Tick
            {
                get { return 4; }
            }

            public int CommandDelayTicks
            {
                get { return 2; }
            }

            public SimCommand Submit(SimCommand command)
            {
                return command;
            }

            public SimCommand SubmitRaiseBattalion(int factionId, string unitId, int rallyXCm, int rallyYCm)
            {
                return default;
            }

            public SimCommand SubmitResearchTech(int factionId, string techId)
            {
                return default;
            }

            public bool TryGetRejection(int issuerId, int sequence, out CommandRejectReason code, out int param0, out int param1)
            {
                code = _reason;
                param0 = _param0;
                param1 = _param1;
                return true;
            }
        }
    }
}
