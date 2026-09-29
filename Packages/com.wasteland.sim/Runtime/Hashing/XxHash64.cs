using System;

namespace Wasteland.Sim.Hashing
{
    /// <summary>
    /// Official xxHash64 (Yann Collet). Little-endian input, seed 0 unless specified.
    /// Named <c>hash64</c> in the AI design contract.
    /// </summary>
    public static class XxHash64
    {
        private const ulong Prime1 = 0x9E3779B185EBCA87UL;
        private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
        private const ulong Prime3 = 0x165667B19E3779F9UL;
        private const ulong Prime4 = 0x85EBCA77C2B2AE63UL;
        private const ulong Prime5 = 0x27D4EB2F165667C5UL;

        public static ulong Hash(byte[] data, ulong seed)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return Hash(data, 0, data.Length, seed);
        }

        public static ulong Hash(byte[] data, int offset, int length, ulong seed)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            int end = offset + length;
            ulong acc;
            int ptr = offset;

            if (length >= 32)
            {
                ulong acc1 = seed + Prime1 + Prime2;
                ulong acc2 = seed + Prime2;
                ulong acc3 = seed;
                ulong acc4 = seed - Prime1;
                int limit = end - 32;
                while (ptr <= limit)
                {
                    acc1 = Round(acc1, Read64(data, ptr));
                    acc2 = Round(acc2, Read64(data, ptr + 8));
                    acc3 = Round(acc3, Read64(data, ptr + 16));
                    acc4 = Round(acc4, Read64(data, ptr + 24));
                    ptr += 32;
                }

                acc = Rotl(acc1, 1) + Rotl(acc2, 7) + Rotl(acc3, 12) + Rotl(acc4, 18);
                acc = Merge(acc, acc1);
                acc = Merge(acc, acc2);
                acc = Merge(acc, acc3);
                acc = Merge(acc, acc4);
            }
            else
            {
                acc = seed + Prime5;
            }

            acc += (ulong)length;

            while (ptr + 8 <= end)
            {
                acc ^= Round(0, Read64(data, ptr));
                acc = Rotl(acc, 27) * Prime1 + Prime4;
                ptr += 8;
            }

            if (ptr + 4 <= end)
            {
                acc ^= Read32(data, ptr) * Prime1;
                acc = Rotl(acc, 23) * Prime2 + Prime3;
                ptr += 4;
            }

            while (ptr < end)
            {
                acc ^= data[ptr] * Prime5;
                acc = Rotl(acc, 11) * Prime1;
                ptr++;
            }

            acc ^= acc >> 33;
            acc *= Prime2;
            acc ^= acc >> 29;
            acc *= Prime3;
            acc ^= acc >> 32;
            return acc;
        }

        /// <summary>Convenience mixer used as <c>hash64(a, b, ...)</c> for PRNG stream derivation.</summary>
        public static ulong Mix(ulong a, ulong b)
        {
            byte[] bytes = new byte[16];
            Write64(bytes, 0, a);
            Write64(bytes, 8, b);
            return Hash(bytes, 0);
        }

        public static ulong Mix(ulong a, ulong b, ulong c)
        {
            byte[] bytes = new byte[24];
            Write64(bytes, 0, a);
            Write64(bytes, 8, b);
            Write64(bytes, 16, c);
            return Hash(bytes, 0);
        }

        private static ulong Round(ulong acc, ulong lane)
        {
            acc += lane * Prime2;
            acc = Rotl(acc, 31);
            return acc * Prime1;
        }

        private static ulong Merge(ulong acc, ulong accN)
        {
            acc ^= Round(0, accN);
            acc *= Prime1;
            return acc + Prime4;
        }

        private static ulong Rotl(ulong value, int bits)
        {
            return (value << bits) | (value >> (64 - bits));
        }

        private static ulong Read32(byte[] data, int offset)
        {
            return data[offset]
                | ((ulong)data[offset + 1] << 8)
                | ((ulong)data[offset + 2] << 16)
                | ((ulong)data[offset + 3] << 24);
        }

        private static ulong Read64(byte[] data, int offset)
        {
            return Read32(data, offset) | (Read32(data, offset + 4) << 32);
        }

        private static void Write64(byte[] data, int offset, ulong value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
            data[offset + 4] = (byte)(value >> 32);
            data[offset + 5] = (byte)(value >> 40);
            data[offset + 6] = (byte)(value >> 48);
            data[offset + 7] = (byte)(value >> 56);
        }
    }

    /// <summary>
    /// Streaming xxHash64 over little-endian integers. Buffers then digests
    /// with one-shot <see cref="XxHash64.Hash"/> so the result matches the spec.
    /// </summary>
    public sealed class XxHash64Builder
    {
        private byte[] _buffer = new byte[64];
        private int _length;
        private readonly ulong _seed;

        public XxHash64Builder(ulong seed = 0)
        {
            _seed = seed;
        }

        public void AddByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void AddBool(bool value)
        {
            AddByte(value ? (byte)1 : (byte)0);
        }

        public void AddInt(int value)
        {
            AddUInt((uint)value);
        }

        public void AddUInt(uint value)
        {
            Ensure(4);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 24);
        }

        public void AddLong(long value)
        {
            AddULong((ulong)value);
        }

        public void AddULong(ulong value)
        {
            Ensure(8);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 24);
            _buffer[_length++] = (byte)(value >> 32);
            _buffer[_length++] = (byte)(value >> 40);
            _buffer[_length++] = (byte)(value >> 48);
            _buffer[_length++] = (byte)(value >> 56);
        }

        public void AddBytes(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            AddBytes(data, 0, data.Length);
        }

        public void AddBytes(byte[] data, int offset, int length)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            Ensure(length);
            Array.Copy(data, offset, _buffer, _length, length);
            _length += length;
        }

        public ulong Digest()
        {
            return XxHash64.Hash(_buffer, 0, _length, _seed);
        }

        private void Ensure(int extra)
        {
            int needed = _length + extra;
            if (needed <= _buffer.Length)
            {
                return;
            }

            int cap = _buffer.Length;
            while (cap < needed)
            {
                cap *= 2;
            }

            byte[] next = new byte[cap];
            Array.Copy(_buffer, 0, next, 0, _length);
            _buffer = next;
        }
    }
}
