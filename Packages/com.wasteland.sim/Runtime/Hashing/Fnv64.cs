namespace Wasteland.Sim.Hashing
{
    /// <summary>
    /// FNV-1a 64-bit. Bytes are always little-endian so hashes match across
    /// host endianness.
    /// </summary>
    public struct Fnv64
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong _hash;

        public static Fnv64 Create()
        {
            return new Fnv64 { _hash = Offset };
        }

        public ulong Digest
        {
            get { return _hash; }
        }

        public void AddByte(byte value)
        {
            _hash ^= value;
            _hash *= Prime;
        }

        public void AddBool(bool value)
        {
            AddByte(value ? (byte)1 : (byte)0);
        }

        public void AddInt(int value)
        {
            AddLong(value);
        }

        public void AddUInt(uint value)
        {
            AddByte((byte)value);
            AddByte((byte)(value >> 8));
            AddByte((byte)(value >> 16));
            AddByte((byte)(value >> 24));
        }

        public void AddLong(long value)
        {
            AddULong((ulong)value);
        }

        public void AddULong(ulong value)
        {
            AddByte((byte)value);
            AddByte((byte)(value >> 8));
            AddByte((byte)(value >> 16));
            AddByte((byte)(value >> 24));
            AddByte((byte)(value >> 32));
            AddByte((byte)(value >> 40));
            AddByte((byte)(value >> 48));
            AddByte((byte)(value >> 56));
        }
    }
}
