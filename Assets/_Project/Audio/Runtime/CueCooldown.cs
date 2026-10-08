using System;

namespace Wasteland.Audio
{
    /// <summary>
    /// Per-cue retrigger gate. The caller passes the clock, so EditMode tests
    /// do not need an <c>AudioSource</c>. A cooldown of 0 always allows playback.
    /// </summary>
    public sealed class CueCooldown
    {
        private readonly float[] _readyAt;

        public CueCooldown(int cueCount)
        {
            if (cueCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cueCount));
            }

            _readyAt = new float[cueCount];
        }

        /// <summary>
        /// True when <paramref name="cue"/> may play at <paramref name="now"/>.
        /// A successful acquire with a positive cooldown blocks that cue until
        /// <paramref name="now"/> reaches the end of the window.
        /// </summary>
        public bool TryAcquire(SoundCue cue, float now, float cooldownSeconds)
        {
            int index = (int)cue;
            if ((uint)index >= (uint)_readyAt.Length)
            {
                return false;
            }

            if (cooldownSeconds > 0f && now < _readyAt[index])
            {
                return false;
            }

            if (cooldownSeconds > 0f)
            {
                _readyAt[index] = now + cooldownSeconds;
            }

            return true;
        }
    }
}
