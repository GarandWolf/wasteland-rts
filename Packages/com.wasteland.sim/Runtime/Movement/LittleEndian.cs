using System;
using System.IO;
using System.Text;

namespace Wasteland.Sim.Movement
{
    /// <summary>Explicit little-endian blob cursor. Does not use <see cref="BitConverter"/>.</summary>
    internal sealed class BlobWriter
    {
        private byte[] _buffer;
        private int _length;

        public BlobWriter(int capacity)
        {
            _buffer = new byte[System.Math.Max(capacity, 32)];
        }

        public int Length
        {
            get { return _length; }
        }

        public void Magic(string ascii4)
        {
            if (ascii4 == null || ascii4.Length != 4)
            {
                throw new ArgumentException("Magic must be four ASCII characters.", nameof(ascii4));
            }

            byte[] bytes = Encoding.ASCII.GetBytes(ascii4);
            if (bytes.Length != 4)
            {
                throw new ArgumentException("Magic must be four ASCII bytes.", nameof(ascii4));
            }

            Ensure(4);
            _buffer[_length++] = bytes[0];
            _buffer[_length++] = bytes[1];
            _buffer[_length++] = bytes[2];
            _buffer[_length++] = bytes[3];
        }

        public void U8(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void U16(ushort value)
        {
            Ensure(2);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
        }

        public void U32(uint value)
        {
            Ensure(4);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 24);
        }

        public void I32(int value)
        {
            U32((uint)value);
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[_length];
            Array.Copy(_buffer, 0, result, 0, _length);
            return result;
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

    internal sealed class BlobReader
    {
        private readonly byte[] _data;
        private int _offset;

        public BlobReader(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public int Offset
        {
            get { return _offset; }
        }

        public int Remaining
        {
            get { return _data.Length - _offset; }
        }

        public int Length
        {
            get { return _data.Length; }
        }

        public void ExpectMagic(string ascii4)
        {
            byte[] expected = Encoding.ASCII.GetBytes(ascii4);
            if (Remaining < 4)
            {
                throw new InvalidDataException("Blob truncated at magic.");
            }

            if (_data[_offset] != expected[0]
                || _data[_offset + 1] != expected[1]
                || _data[_offset + 2] != expected[2]
                || _data[_offset + 3] != expected[3])
            {
                throw new InvalidDataException("Unexpected blob magic.");
            }

            _offset += 4;
        }

        public byte U8()
        {
            if (Remaining < 1)
            {
                throw new InvalidDataException("Blob truncated.");
            }

            return _data[_offset++];
        }

        public ushort U16()
        {
            if (Remaining < 2)
            {
                throw new InvalidDataException("Blob truncated.");
            }

            ushort value = (ushort)(_data[_offset] | (_data[_offset + 1] << 8));
            _offset += 2;
            return value;
        }

        public uint U32()
        {
            if (Remaining < 4)
            {
                throw new InvalidDataException("Blob truncated.");
            }

            uint value = _data[_offset]
                | ((uint)_data[_offset + 1] << 8)
                | ((uint)_data[_offset + 2] << 16)
                | ((uint)_data[_offset + 3] << 24);
            _offset += 4;
            return value;
        }

        public int I32()
        {
            return (int)U32();
        }

        public void Seek(int offset)
        {
            if (offset < 0 || offset > _data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            _offset = offset;
        }
    }
}
