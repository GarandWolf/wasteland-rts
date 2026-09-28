namespace Wasteland.Sim.Random
{
    /// <summary>
    /// PCG-XSH-RR 32-bit generator (O'Neill). Integer-only, portable, and
    /// deterministic given the same seed and stream. Hash the 64-bit state
    /// when computing a tick checksum.
    /// </summary>
    public struct SimRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _inc;

        public SimRandom(ulong seed, ulong stream = 1UL)
        {
            _state = 0;
            _inc = (stream << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        /// <summary>Full internal state for desync hashes and save games.</summary>
        public ulong State
        {
            get { return _state; }
        }

        public ulong Stream
        {
            get { return _inc; }
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

            uint range = (uint)(maxExclusive - minInclusive);
            uint threshold = unchecked((uint)(-range)) % range;
            uint x;
            do
            {
                x = NextUInt();
            }
            while (x < threshold);

            return minInclusive + (int)(x % range);
        }

        public Math.Fix64 NextFix64(Math.Fix64 minInclusive, Math.Fix64 maxExclusive)
        {
            if (minInclusive.Raw >= maxExclusive.Raw)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
            }

            ulong span = (ulong)(maxExclusive.Raw - minInclusive.Raw);
            // 32 bits of randomness is enough for this prototype; map uniformly into [0, span).
            ulong scaled = ((ulong)NextUInt() * span) >> 32;
            return Math.Fix64.FromRaw(minInclusive.Raw + (long)scaled);
        }
    }
}
