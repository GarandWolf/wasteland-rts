using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Wasteland.Presentation.Tests
{
    public sealed class GorgeCombatBedPlayModeTests
    {
        const int SearchLimit = 12000;

        [UnityTest]
        [RequiresPlayMode]
        public IEnumerator CombatBed_IsActiveFromFirstContestThroughFirstBattalionLoss()
        {
            GameObject go = new GameObject("Gorge Listening Test");
            GorgeListeningHarness harness = go.AddComponent<GorgeListeningHarness>();
            harness.TicksPerFrame = 0;

            int steps = 0;
            while (harness.FirstContestTick < 0)
            {
                harness.StepOne();
                steps++;
                Assert.That(steps, Is.LessThan(SearchLimit), "The match produced no contest start.");
            }

            Debug.Log("Gorge first contest start (event 10) tick " + harness.FirstContestTick);
            yield return null;
            Assert.That(harness.LoggerTick, Is.EqualTo(harness.FirstContestTick));
            Assert.That(harness.CombatBedActive, Is.True, "Combat bed should be on at the first contest.");
            Assert.That(LoopPlaying(harness), Is.True, "A combat loop source should be playing at the first contest.");

            while (harness.FirstBattalionDestroyedTick < 0)
            {
                harness.StepOne();
                steps++;
                Assert.That(steps, Is.LessThan(SearchLimit), "The match produced no battalion destroyed event.");
            }

            Debug.Log("Gorge first battalion destroyed (event 15) tick " + harness.FirstBattalionDestroyedTick);
            yield return null;
            Assert.That(harness.LoggerTick, Is.EqualTo(harness.FirstBattalionDestroyedTick));
            Assert.That(harness.CombatBedActive, Is.True, "Combat bed should still be on after the first battalion loss.");
            Assert.That(LoopPlaying(harness), Is.True, "A combat loop source should still be playing after the first battalion loss.");

            Object.Destroy(go);
        }

        static bool LoopPlaying(GorgeListeningHarness harness)
        {
            AudioSource[] sources = harness.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = sources[i];
                if (source.isPlaying && source.loop && source.clip != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
