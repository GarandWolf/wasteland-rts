using System;
using NUnit.Framework;
using UnityEngine;
using Wasteland.Audio;

namespace Wasteland.Audio.Tests
{
    public sealed class CombatLoopTests
    {
        [Test]
        public void SelectNearest_KeepsThreeNearest_NearestFirst()
        {
            int[] ids = { 1, 2, 3, 4 };
            Vector3[] positions =
            {
                new Vector3(10f, 0f, 0f),
                new Vector3(3f, 0f, 0f),
                new Vector3(7f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
            };
            int[] selected = new int[CombatVoiceSelector.MaxVoices];

            int count = CombatVoiceSelector.SelectNearest(ids, positions, Vector3.zero, selected);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(ids[selected[0]], Is.EqualTo(4));
            Assert.That(ids[selected[1]], Is.EqualTo(2));
            Assert.That(ids[selected[2]], Is.EqualTo(3));
        }

        [Test]
        public void SelectNearest_TieBreaksToTheLowerId()
        {
            int[] ids = { 10, 2, 7 };
            Vector3[] positions =
            {
                new Vector3(5f, 0f, 0f),
                new Vector3(5f, 0f, 0f),
                new Vector3(20f, 0f, 0f),
            };
            int[] selected = new int[CombatVoiceSelector.MaxVoices];

            int count = CombatVoiceSelector.SelectNearest(ids, positions, Vector3.zero, selected);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(ids[selected[0]], Is.EqualTo(2));
            Assert.That(ids[selected[1]], Is.EqualTo(10));
            Assert.That(ids[selected[2]], Is.EqualTo(7));
        }

        [Test]
        public void SelectNearest_FewerThanTheCap_ReturnsAll()
        {
            int[] ids = { 8 };
            Vector3[] positions = { new Vector3(0f, 4f, 0f) };
            int[] selected = new int[CombatVoiceSelector.MaxVoices];

            int count = CombatVoiceSelector.SelectNearest(ids, positions, Vector3.zero, selected);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(selected[0], Is.EqualTo(0));
        }

        [Test]
        public void SelectNearest_Empty_ReturnsZero()
        {
            int[] selected = new int[CombatVoiceSelector.MaxVoices];

            int count = CombatVoiceSelector.SelectNearest(
                Array.Empty<int>(),
                Array.Empty<Vector3>(),
                Vector3.zero,
                selected);

            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void GarrisonContest_StartsAndStopsABed_WithSeatMinusOne()
        {
            CombatBedLedger beds = new CombatBedLedger();
            const int townId = 14;
            const int garrisonSeat = -1;
            const int attackerSeat = 1;

            Assert.That(beds.Apply(CombatContestEvents.Started, townId, garrisonSeat, attackerSeat), Is.True);
            Assert.That(beds.Contains(townId), Is.True);
            Assert.That(beds.Count, Is.EqualTo(1));

            Assert.That(beds.Apply(CombatContestEvents.Ended, townId, garrisonSeat, attackerSeat), Is.True);
            Assert.That(beds.Contains(townId), Is.False);
            Assert.That(beds.Count, Is.EqualTo(0));
        }

        [Test]
        public void Fade_MovesTowardTheTargetWithoutOvershooting()
        {
            Assert.That(SoundFades.Advance(0f, 1f, 0.25f, 0.5f), Is.EqualTo(0.5f));
            Assert.That(SoundFades.Advance(0.75f, 1f, 0.5f, 0.5f), Is.EqualTo(1f));
            Assert.That(SoundFades.Advance(1f, 0f, 0.25f, 0.5f), Is.EqualTo(0.5f));
            Assert.That(SoundFades.Advance(0.25f, 0f, 0.5f, 0.5f), Is.EqualTo(0f));
            Assert.That(SoundFades.Advance(0.2f, 1f, 1f, 0f), Is.EqualTo(1f));
            Assert.That(SoundFades.Advance(0.2f, 1f, 0f, 0.5f), Is.EqualTo(0.2f));
        }
    }
}
