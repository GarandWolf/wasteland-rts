namespace Wasteland.Sim.Random
{
    /// <summary>
    /// PCG32 (XSH-RR, O'Neill). Integer-only, portable, deterministic given
    /// the same seed and stream. Matches pcg-c-basic <c>pcg32_srandom_r</c>.
    /// </summary>
    public struct SimRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _inc;

        /// <summary>
        /// Seeds the generator the same way as <c>pcg32_srandom_r(state, stream)</c>.
        /// Stream 54 with seed 42 is the published golden sequence.
        /// </summary>
        public SimRandom(ulong seed, ulong stream = 1UL)
        {
            _state = 0;
            _inc = (stream << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public ulong State
        {
            get { return _state; }
        }

        /// <summary>
        /// PCG increment register. Always odd. This is <c>(streamId &lt;&lt; 1) | 1</c>
        /// from the constructor, not the constructor's <c>stream</c> argument.
        /// Hashed and saved as-is; do not confuse with the constructor stream id.
        /// </summary>
        public ulong Increment
        {
            get { return _inc; }
        }

        /// <summary>
        /// Restores a generator from persisted <see cref="State"/> /
        /// <see cref="Increment"/>. Does not re-run the seed scramble.
        /// <paramref name="increment"/> must be odd.
        /// </summary>
        public static SimRandom FromPersisted(ulong state, ulong increment)
        {
            if ((increment & 1UL) == 0UL)
            {
                throw new System.ArgumentException("PCG increment must be odd.", nameof(increment));
            }

            SimRandom rng = default;
            rng._state = state;
            rng._inc = increment;
            return rng;
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _inc);
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
            }

            uint range = (uint)((long)maxExclusive - (long)minInclusive);
            uint threshold = unchecked(0u - range) % range;
            uint x;
            do
            {
                x = NextUInt();
            }
            while (x < threshold);

            return minInclusive + (int)(x % range);
        }
    }
}
