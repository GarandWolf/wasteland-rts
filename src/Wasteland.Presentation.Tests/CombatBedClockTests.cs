#nullable disable
using System.Collections.Generic;
using Wasteland.Audio;
using Xunit;

namespace Wasteland.Presentation.Tests
{
    public sealed class CombatBedClockTests
    {
        [Fact]
        public void EndAndStartOnTheSameTick_KeepOneContinuousBed()
        {
            CombatBedClock clock = new CombatBedClock();
            clock.ApplyTick(0, new[] { Start(7, 1000, 2000) });
            int generation = clock.Generation(7);

            clock.ApplyTick(4, new[] { End(7, 1000, 2000), Start(7, 1100, 2100) });

            Assert.Equal(generation, clock.Generation(7));
            Assert.True(clock.ContinuedWithoutRestart(7));
            Assert.Equal(1, clock.Layers(7));
            Assert.True(clock.IsAudible(7));

            List<AudibleBed> beds = new List<AudibleBed>();
            clock.CopyAudible(beds);
            Assert.Single(beds);
            Assert.Equal(1, beds[0].MemberCount);
            Assert.False(beds[0].AnyHeld);
        }

        [Fact]
        public void StartThenEndOnTheSameTick_StaysOneBed()
        {
            CombatBedClock clock = new CombatBedClock();
            clock.ApplyTick(0, new[] { Start(7, 1, 1) });
            int generation = clock.Generation(7);
            clock.ApplyTick(1, new[] { Start(7, 1, 1), End(7, 1, 1) });
            Assert.Equal(generation, clock.Generation(7));
            Assert.Equal(1, clock.Layers(7));
            Assert.True(clock.IsAudible(7));
        }

        [Fact]
        public void HoldLastsFortyTicks_ThenTheBedLeavesForTheFade()
        {
            CombatBedClock clock = new CombatBedClock();
            clock.ApplyTick(11, new[] { Start(3, 0, 0) });
            clock.ApplyTick(11, new[] { End(3, 0, 0) });
            Assert.True(clock.IsAudible(3));

            clock.ApplyTick(50, new BedEvent[0]);
            Assert.True(clock.IsAudible(3));
            clock.ApplyTick(51, new BedEvent[0]);
            Assert.False(clock.IsAudible(3));
            Assert.Equal(1.5f, CombatBedClock.FadeSeconds(1f, 0f, 0.2f));
            Assert.Equal(0.2f, CombatBedClock.FadeSeconds(0f, 1f, 0.2f));
        }

        [Fact]
        public void StartInsideTheHold_ContinuesTheSameBed()
        {
            CombatBedClock clock = new CombatBedClock();
            clock.ApplyTick(0, new[] { Start(3, 0, 0) });
            int generation = clock.Generation(3);
            clock.ApplyTick(1, new[] { End(3, 0, 0) });
            clock.ApplyTick(40, new[] { Start(3, 5, 5) });
            Assert.Equal(generation, clock.Generation(3));
            Assert.True(clock.ContinuedWithoutRestart(3));
            Assert.Equal(1, clock.Layers(3));
            Assert.True(clock.IsAudible(3));
        }

        [Fact]
        public void HoodRiverAndWhiteSalmonDistances_ShareOneBed()
        {
            CombatBedClock clock = new CombatBedClock();
            clock.ApplyTick(0, new[]
            {
                Start(1, 0, 0),
                Start(2, 290000, 0),
                Start(3, 3000000, 0)
            });
            List<AudibleBed> beds = new List<AudibleBed>();
            clock.CopyAudible(beds);
            Assert.Equal(2, beds.Count);
            AudibleBed shared = beds[0].MemberCount == 2 ? beds[0] : beds[1];
            Assert.Equal(2, shared.MemberCount);
            Assert.Equal(1, shared.VoiceId);
        }

        [Fact]
        public void JoiningANearbyTown_KeepsTheSameBed()
        {
            CombatBedClock clock = new CombatBedClock();
            clock.ApplyTick(0, new[] { Start(2, 0, 0) });
            List<AudibleBed> beds = new List<AudibleBed>();
            clock.CopyAudible(beds);
            Assert.Equal(2, beds[0].VoiceId);

            clock.ApplyTick(1, new[] { Start(1, 1000, 0) });
            clock.CopyAudible(beds);
            Assert.Single(beds);
            Assert.Equal(2, beds[0].MemberCount);
            Assert.Equal(2, beds[0].VoiceId);
            Assert.Equal(1, clock.Layers(1));
            Assert.Equal(1, clock.Layers(2));
        }

        [Fact]
        public void HeldTown_CountsTowardTheCap_AndTheMergedBed()
        {
            CombatBedClock clock = new CombatBedClock();
            clock.ApplyTick(0, new[]
            {
                Start(1, 0, 0),
                Start(2, 200000, 0),
                Start(3, 1000000, 0),
                Start(4, 2000000, 0),
                Start(5, 3000000, 0)
            });
            clock.ApplyTick(1, new[] { End(1, 0, 0) });

            List<AudibleBed> beds = new List<AudibleBed>();
            clock.CopyAudible(beds);
            Assert.Equal(4, beds.Count);
            bool mergedHeld = false;
            for (int i = 0; i < beds.Count; i++)
            {
                if (beds[i].MemberCount == 2)
                {
                    mergedHeld = beds[i].AnyHeld;
                }
            }

            Assert.True(mergedHeld);

            AudibleBed[] copy = beds.ToArray();
            int[] selected = new int[CombatBedClock.VoiceCap];
            int count = CombatBedClock.SelectNearest(copy, 0, 0, CombatBedClock.VoiceCap, selected);
            Assert.Equal(3, count);
            bool sawMerged = false;
            bool sawFar = false;
            for (int i = 0; i < count; i++)
            {
                if (copy[selected[i]].MemberCount == 2)
                {
                    sawMerged = true;
                }

                if (copy[selected[i]].VoiceId == 5)
                {
                    sawFar = true;
                }
            }

            Assert.True(sawMerged);
            Assert.False(sawFar);
        }

        static BedEvent Start(int town, int x, int y)
        {
            return new BedEvent(CombatContestEvents.Started, town, x, y);
        }

        static BedEvent End(int town, int x, int y)
        {
            return new BedEvent(CombatContestEvents.Ended, town, x, y);
        }
    }
}
