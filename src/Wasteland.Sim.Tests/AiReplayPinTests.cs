using System;
using System.Collections.Generic;
using System.Text;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Hashing;
using Xunit;

namespace Wasteland.Sim.Tests
{
    public sealed class AiReplayPinTests
    {
        /// <summary>
        /// xxHash64 (seed 0) of the Compact raise-replay command buffer from
        /// <see cref="HashLog"/>. This is not <c>SimWorld.HashState</c>. The
        /// replay is 12 ticks and income first pays at tick 20, so accrual
        /// does not move this pin. Not the
        /// <see cref="AiBalanceTablesTests.ExpectedAiOwnedBalanceKey"/> data hash.
        /// </summary>
        public const ulong ExpectedCompactReplayKey = 0x54E7C602054A62A1UL;

        public const int ReplayTicks = 12;
        public const int ArmourFlipTick = 6;

        private static readonly string[] ExpectedUnseenRaiseRoles =
        {
            AiConstants.RoleLineInfantry,
            AiConstants.RoleLineInfantry,
            AiConstants.RoleAntiTank,
            AiConstants.RoleLineInfantry,
            AiConstants.RoleLineInfantry,
            AiConstants.RoleAntiTank
        };

        private static readonly string[] ExpectedAfterFlipRaiseRoles =
        {
            AiConstants.RoleLineInfantry,
            AiConstants.RoleLineInfantry,
            AiConstants.RoleAntiTank
        };

        [Fact]
        public void CompactRaiseReplay_MatchesPinnedCommandLogKey()
        {
            ReplayRun first = RunCompactReplay();
            ReplayRun second = RunCompactReplay();
            Assert.Equal(first.Key, second.Key);
            Assert.Equal(ExpectedCompactReplayKey, first.Key);
            Assert.Equal(
                AiBalanceTablesTests.ExpectedAiOwnedBalanceKey,
                0xE7008C30BE1FC2F0UL);
        }

        [Fact]
        public void HomeTown_MissingTown_ThrowsNamingTheId()
        {
            AiMvpAttackTests.Harness harness = CompactHarness();
            harness.Faction.HomeTownId = 999;
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => HomeTown(harness));
            Assert.Contains("999", ex.Message);
        }

        [Fact]
        public void CompactMilitia_RaisesGarrisonWhenManpowerShortAndTownBelowMin()
        {
            AiMvpAttackTests.Harness harness = CompactHarness();
            Assert.Equal(2, harness.MinGarrisonPerTown);
            Assert.True(harness.Catalog.TryGetManpowerCost(
                AiMvpAttackTests.FirstRoleOrTarget(
                    harness.Seat,
                    harness.Catalog,
                    AiConstants.RoleLineInfantry,
                    null),
                out int lineCost));
            harness.SeatView.Manpower = lineCost - 1;
            harness.Towns[10] = new AiTownReadback(1, false, 1, 1, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 3);
            harness.SetBattalions();
            harness.Tick(0);
            string unitId = string.Empty;
            for (int i = 0; i < harness.Sink.Log.Count; i++)
            {
                if (harness.Sink.Log[i].Kind != AiIssuedKind.RaiseBattalion)
                {
                    continue;
                }

                unitId = harness.Sink.Log[i].UnitId;
                break;
            }

            Assert.False(string.IsNullOrEmpty(unitId));
            Assert.True(harness.Catalog.TryGetRole(unitId, out string role));
            Assert.Equal(AiConstants.RoleGarrisonInfantry, role);
        }

        private static ReplayRun RunCompactReplay()
        {
            AiMvpAttackTests.Harness harness = CompactHarness();
            Assert.True(harness.Catalog.TryGetRole("compact_rifle_battalion", out string rifleRole));
            Assert.Equal(AiConstants.RoleLineInfantry, rifleRole);
            Assert.True(harness.Catalog.TryGetRole("compact_at_battalion", out string atRole));
            Assert.Equal(AiConstants.RoleAntiTank, atRole);

            List<AiBattalionPin> pins = new List<AiBattalionPin>
            {
                AiMvpAttackTests.PinWithRole(
                    100,
                    0,
                    0,
                    1,
                    AiConstants.RoleGarrisonInfantry,
                    true)
            };
            harness.Towns[10] = new AiTownReadback(1, false, 1, 2, 0, 0);
            harness.Towns[30] = new AiTownReadback(2, false, 2, 1, 1, 3);
            harness.SeatView.HasSeenEnemyArmourOrMech = false;

            List<string> raiseRoles = new List<string>();
            int raisesBeforeFlip = 0;
            bool atBeforeFlip = false;
            bool movedOrClaimed = false;

            for (int tick = 0; tick < ReplayTicks; tick++)
            {
                if (tick == ArmourFlipTick)
                {
                    harness.SeatView.HasSeenEnemyArmourOrMech = true;
                }

                if (tick == ReplayTicks - 1)
                {
                    UnassignFirstLine(pins, harness.Catalog);
                }

                harness.SetBattalions(pins.ToArray());
                int from = harness.Sink.Log.Count;
                harness.Tick(tick);
                for (int i = from; i < harness.Sink.Log.Count; i++)
                {
                    AiIssuedCommand cmd = harness.Sink.Log[i];
                    if (cmd.Kind == AiIssuedKind.Move || cmd.Kind == AiIssuedKind.ClaimTown)
                    {
                        movedOrClaimed = true;
                    }

                    if (cmd.Kind != AiIssuedKind.RaiseBattalion)
                    {
                        continue;
                    }

                    Assert.True(harness.Catalog.TryGetRole(cmd.UnitId, out string role));
                    raiseRoles.Add(role);
                    if (tick < ArmourFlipTick)
                    {
                        raisesBeforeFlip++;
                        if (role == AiConstants.RoleAntiTank)
                        {
                            atBeforeFlip = true;
                        }
                    }

                    AiAttackTownInfo home = HomeTown(harness);
                    pins.Add(new AiBattalionPin(
                        cmd.Id,
                        home.XCm,
                        home.YCm,
                        home.NodeId,
                        cmd.UnitId,
                        true));
                }
            }

            Assert.True(raiseRoles.Count >= 6);
            Assert.True(raisesBeforeFlip >= 6);
            Assert.True(atBeforeFlip);
            Assert.True(movedOrClaimed);
            Assert.Equal(ExpectedUnseenRaiseRoles.Length, raisesBeforeFlip);
            for (int i = 0; i < ExpectedUnseenRaiseRoles.Length; i++)
            {
                Assert.Equal(ExpectedUnseenRaiseRoles[i], raiseRoles[i]);
            }

            Assert.True(raiseRoles.Count > ExpectedUnseenRaiseRoles.Length);
            int afterFlip = raiseRoles.Count - ExpectedUnseenRaiseRoles.Length;
            Assert.True(afterFlip >= ExpectedAfterFlipRaiseRoles.Length);
            for (int i = 0; i < ExpectedAfterFlipRaiseRoles.Length; i++)
            {
                Assert.Equal(
                    ExpectedAfterFlipRaiseRoles[i],
                    raiseRoles[ExpectedUnseenRaiseRoles.Length + i]);
            }

            List<AiBattalionPin> afterLoss = new List<AiBattalionPin>(pins);
            DropFirstRole(afterLoss, harness.Catalog, AiConstants.RoleAntiTank);
            string lostRaise = AiAttackDefend.SelectRaiseUnit(
                harness.Seat,
                harness.Catalog,
                harness.SeatView,
                afterLoss.ToArray(),
                afterLoss.Count,
                harness.LineUnitsPerAntiArmour,
                false,
                false);
            Assert.True(harness.Catalog.TryGetRole(lostRaise, out string lostRole));
            Assert.Equal(AiConstants.RoleAntiTank, lostRole);

            ulong key = HashLog(harness.Sink.Log, harness.Seat.Slot);
            return new ReplayRun(key, raiseRoles.ToArray());
        }

        private static AiMvpAttackTests.Harness CompactHarness()
        {
            AiMvpAttackTests.Harness harness = AiMvpAttackTests.Harness.Line();
            harness.Seat = FixtureAiScenarioSeats.FromMvpShape(
                new[]
                {
                    new AiMvpSeatShape(
                        0,
                        "homestead_compact",
                        "homestead_compact_settler",
                        1UL,
                        300,
                        FixtureAiScenarioSeats.DefaultRaisableUnits,
                        new[] { "supply_depot" },
                        System.Array.Empty<AiMvpAutoResearchItem>())
                },
                new AiMvpMatchRulesShape(false, "draw")).SeatAt(0);
            harness.SeatView.Manpower = harness.Seat.StartingManpower;
            harness.SupplyReachCm = 100000;
            return harness;
        }

        private static AiAttackTownInfo HomeTown(AiMvpAttackTests.Harness harness)
        {
            for (int i = 0; i < harness.TownRows.Length; i++)
            {
                if (harness.TownRows[i].Id == harness.Faction.HomeTownId)
                {
                    return harness.TownRows[i];
                }
            }

            throw new InvalidOperationException(
                "missing home town id=" + harness.Faction.HomeTownId);
        }

        private static void DropFirstRole(List<AiBattalionPin> pins, IAiUnitCatalog catalog, string role)
        {
            for (int i = 0; i < pins.Count; i++)
            {
                if (!pins[i].HasRole(catalog, role))
                {
                    continue;
                }

                pins.RemoveAt(i);
                return;
            }
        }

        private static void UnassignFirstLine(List<AiBattalionPin> pins, IAiUnitCatalog catalog)
        {
            for (int i = 0; i < pins.Count; i++)
            {
                AiBattalionPin pin = pins[i];
                if (!pin.HasRole(catalog, AiConstants.RoleLineInfantry))
                {
                    continue;
                }

                pins[i] = new AiBattalionPin(
                    pin.Id,
                    pin.XCm,
                    pin.YCm,
                    pin.RoadNodeId,
                    pin.UnitId,
                    false);
                return;
            }
        }

        private static ulong HashLog(IReadOnlyList<AiIssuedCommand> log, int seatSlot)
        {
            List<byte> buffer = new List<byte>();
            for (int i = 0; i < log.Count; i++)
            {
                AiIssuedCommand cmd = log[i];
                WriteInt32(buffer, cmd.Tick);
                WriteInt32(buffer, seatSlot);
                WriteInt32(buffer, (int)cmd.Kind);
                WriteInt32(buffer, cmd.Arg0);
                WriteInt32(buffer, cmd.Arg1);
                WriteInt32(buffer, cmd.Arg2);
                WriteInt32(buffer, cmd.Arg3);
                byte[] unitBytes = Encoding.UTF8.GetBytes(cmd.UnitId);
                WriteInt32(buffer, unitBytes.Length);
                for (int b = 0; b < unitBytes.Length; b++)
                {
                    buffer.Add(unitBytes[b]);
                }
            }

            return XxHash64.Hash(buffer.ToArray(), 0UL);
        }

        private static void WriteInt32(List<byte> buffer, int value)
        {
            uint bits = (uint)value;
            buffer.Add((byte)bits);
            buffer.Add((byte)(bits >> 8));
            buffer.Add((byte)(bits >> 16));
            buffer.Add((byte)(bits >> 24));
        }

        private readonly struct ReplayRun
        {
            public readonly ulong Key;
            public readonly string[] RaiseRoles;

            public ReplayRun(ulong key, string[] raiseRoles)
            {
                Key = key;
                RaiseRoles = raiseRoles;
            }
        }
    }
}
