#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using Wasteland.Presentation;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Events;
using Wasteland.Sim.World;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    public sealed class CombatContactTests
    {
        static readonly Rgb Remnant = Rgb.FromHex(0x3A6EA5);

        [Fact]
        public void HitAlert_CooldownHoldsPerArea_AndIgnoresSpectators()
        {
            UnderAttackAlerts alerts = new UnderAttackAlerts();
            Assert.True(alerts.TryHit(100, 5, 10, 20, true, Remnant, out UnderAttackAlert first));
            Assert.True(first.PlayCue);
            Assert.False(first.Spatial);
            Assert.Equal(UnderAttackAlerts.CueFile, first.CueFile);
            Assert.False(alerts.TryHit(100 + UnderAttackAlerts.AreaCooldownTicks - 1, 5, 10, 20, true, Remnant, out _));
            Assert.True(alerts.TryHit(150, 6, 10, 20, true, Remnant, out _));
            Assert.True(alerts.TryHit(100 + UnderAttackAlerts.AreaCooldownTicks, 5, 10, 20, true, Remnant, out _));
            Assert.False(alerts.TryHit(5000, 7, 10, 20, false, Remnant, out _));
            Assert.Equal(200, UnderAttackAlerts.AreaCooldownTicks);
        }

        [Fact]
        public void CombatArea_UsesTerritory_ElseGridCell()
        {
            Assert.Equal(42, CombatArea.Key(42, 1, 1));
            int a = CombatArea.Key(0, 100, 100);
            Assert.True(a < 0);
            Assert.Equal(a, CombatArea.Key(0, CombatArea.GridCellCm - 1, 5));
            Assert.NotEqual(a, CombatArea.Key(0, CombatArea.GridCellCm, 5));
        }

        [Fact]
        public void HpLossWatch_FlashesBothSidesAndFades()
        {
            HpLossWatch watch = new HpLossWatch();
            watch.BeginScan();
            Assert.False(watch.Sample(10, 1, 100));
            watch.EndScan();
            watch.BeginScan();
            Assert.True(watch.Sample(11, 1, 90));
            Assert.False(watch.Sample(11, 2, 50));
            watch.EndScan();
            Assert.Equal(1f, watch.Flash(1, 11));
            Assert.True(watch.Flash(1, 14) > 0f);
            Assert.Equal(0f, watch.Flash(1, 11 + HpLossWatch.FlashTicks));
            Assert.Equal(0f, watch.Flash(2, 11));

            watch.BeginScan();
            Assert.True(watch.Sample(20, 1, 80));
            watch.EndScan();
            Assert.Equal(1f, watch.Flash(1, 60));
            Assert.Equal(0f, watch.Flash(1, 61));
        }

        [Fact]
        public void DeathCue_ReusesPlaceholder_LouderForOwnLoss()
        {
            Assert.Equal(0.7f, DeathCue.Volume(true, 1, 1));
            Assert.Equal(0f, DeathCue.Volume(true, 1, 2));
            Assert.Equal(0f, DeathCue.Volume(false, 1, 1));
            Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "Assets", "_Project", "Audio", "Placeholder", DeathCue.CueFile)));
        }

        /// <summary>
        /// Default Goldendale vs Hood River match with no human orders. The Compact column
        /// fights at the Remnant HQ around tick 12700-16600. The sim never sets
        /// AttackTargetId for auto-fire, yet the alert must still fire, and every
        /// BattalionDestroyed must map to a death cue.
        /// </summary>
        [Fact]
        public void DefaultMatch_HitAlertFiresWithoutAttackTarget_AndDeathsPlayCue()
        {
            PlayableMatchBoot boot = PlayableMatch.CreateHost(FindRepoRoot());
            AiMatchHost host = boot.Host;
            try
            {
                SimWorld world = host.World;
                int human = PlayableMatch.HumanFactionId;
                HpLossWatch watch = new HpLossWatch();
                UnderAttackAlerts alerts = new UnderAttackAlerts();
                int alertsWithoutTarget = 0;
                int suppressed = 0;
                int humanHits = 0;
                int deaths = 0;
                int cursor = 0;
                for (int t = 0; t < 16000; t++)
                {
                    host.Step();
                    IReadOnlyList<SimEvent> events = world.Events;
                    for (; cursor < events.Count; cursor++)
                    {
                        if (events[cursor].Kind == SimEventKind.BattalionDestroyed)
                        {
                            deaths++;
                            float v = DeathCue.Volume(true, human, events[cursor].AuxId);
                            Assert.True(events[cursor].AuxId == human ? v > 0f : v == 0f);
                        }
                    }

                    watch.BeginScan();
                    for (int i = 0; i < world.Battalions.Count; i++)
                    {
                        SimBattalion b = world.Battalions[i];
                        if (b.Hp <= 0 || !watch.Sample(world.Tick, b.Id, b.Hp) || b.OwnerId != human)
                        {
                            continue;
                        }

                        humanHits++;
                        bool targeted = false;
                        for (int j = 0; j < world.Battalions.Count; j++)
                        {
                            if (world.Battalions[j].AttackTargetId == b.Id)
                            {
                                targeted = true;
                            }
                        }

                        if (alerts.TryHit(world.Tick, CombatArea.Key(0, b.PositionXCm, b.PositionYCm), b.PositionXCm, b.PositionYCm, true, Remnant, out _))
                        {
                            if (!targeted)
                            {
                                alertsWithoutTarget++;
                            }
                        }
                        else
                        {
                            suppressed++;
                        }
                    }

                    watch.EndScan();
                }

                Assert.True(humanHits > 0, "no human battalion lost HP by tick 16000");
                Assert.True(alertsWithoutTarget > 0, "alert never fired for an untargeted (auto-fire) hit");
                Assert.True(suppressed > 0, "cooldown never suppressed a repeat hit in the same area");
                Assert.True(deaths > 0, "no BattalionDestroyed event by tick 16000");
            }
            finally
            {
                host.Shutdown();
            }
        }

        static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Wasteland.Sim.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Repo root not found.");
        }
    }
}
