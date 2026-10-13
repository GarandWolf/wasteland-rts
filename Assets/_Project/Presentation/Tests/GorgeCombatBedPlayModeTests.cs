using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Wasteland.Presentation.Tests
{
    public sealed class GorgeCombatBedPlayModeTests
    {
        [UnityTest]
        [RequiresPlayMode]
        public IEnumerator CombatBed_IsActiveFromFirstCombatThroughGarrisonClear()
        {
            GameObject go = new GameObject("Gorge Listening Test");
            GorgeListeningHarness harness = go.AddComponent<GorgeListeningHarness>();
            harness.TicksPerFrame = 0;

            harness.RunTo(GorgeListeningHarness.FirstCombatTick);
            yield return null;
            Assert.That(harness.LoggerTick, Is.EqualTo(GorgeListeningHarness.FirstCombatTick));
            Assert.That(harness.CombatBedActive, Is.True, "Combat bed should be on at the first contest.");
            Assert.That(LoopPlaying(harness), Is.True, "A combat loop source should be playing at tick 3683.");

            harness.RunTo(GorgeListeningHarness.GarrisonClearedTick);
            yield return null;
            Assert.That(harness.LoggerTick, Is.EqualTo(GorgeListeningHarness.GarrisonClearedTick));
            Assert.That(harness.CombatBedActive, Is.True, "Combat bed should still be on after the garrison is cleared.");
            Assert.That(LoopPlaying(harness), Is.True, "A combat loop source should still be playing at tick 4896.");

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
