using NUnit.Framework;
using Wasteland.Audio;

namespace Wasteland.Audio.Tests
{
    public sealed class CueCooldownTests
    {
        [Test]
        public void PositiveCooldown_BlocksUntilTheWindowElapses()
        {
            CueCooldown gate = new CueCooldown(SoundCues.Count);

            Assert.That(gate.TryAcquire(SoundCue.OrderAck, 1f, 0.25f), Is.True);
            Assert.That(gate.TryAcquire(SoundCue.OrderAck, 1.2f, 0.25f), Is.False);
            Assert.That(gate.TryAcquire(SoundCue.OrderAck, 1.25f, 0.25f), Is.True);
        }

        [Test]
        public void ZeroCooldown_AlwaysAllows()
        {
            CueCooldown gate = new CueCooldown(SoundCues.Count);

            Assert.That(gate.TryAcquire(SoundCue.UiClick, 0f, 0f), Is.True);
            Assert.That(gate.TryAcquire(SoundCue.UiClick, 0f, 0f), Is.True);
        }

        [Test]
        public void NegativeCooldown_DoesNotBlock()
        {
            CueCooldown gate = new CueCooldown(SoundCues.Count);

            Assert.That(gate.TryAcquire(SoundCue.UiClick, 0f, -1f), Is.True);
            Assert.That(gate.TryAcquire(SoundCue.UiClick, 0f, -1f), Is.True);
        }

        [Test]
        public void Cooldown_IsPerCue()
        {
            CueCooldown gate = new CueCooldown(SoundCues.Count);

            Assert.That(gate.TryAcquire(SoundCue.OrderAck, 0f, 1f), Is.True);
            Assert.That(gate.TryAcquire(SoundCue.UiClick, 0f, 0f), Is.True);
            Assert.That(gate.TryAcquire(SoundCue.OrderAck, 0.5f, 1f), Is.False);
        }

        [Test]
        public void UnknownCue_IsRejected()
        {
            CueCooldown gate = new CueCooldown(SoundCues.Count);

            Assert.That(gate.TryAcquire((SoundCue)99, 0f, 0f), Is.False);
        }
    }
}
