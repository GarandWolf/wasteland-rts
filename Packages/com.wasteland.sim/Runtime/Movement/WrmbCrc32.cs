namespace Wasteland.Sim.Movement
{
    /// <summary>zlib / ITU-T V.42 CRC-32 (polynomial 0xEDB88320). Matches Python <c>zlib.crc32</c>.</summary>
    internal static class WrmbCrc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(byte[] data)
        {
            if (data == null)
            {
                throw new System.ArgumentNullException(nameof(data));
            }

            return Compute(data, 0, data.Length);
        }

        public static uint Compute(byte[] data, int offset, int length)
        {
            if (data == null)
            {
                throw new System.ArgumentNullException(nameof(data));
            }

            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(offset));
            }

            uint crc = 0xFFFFFFFF;
            int end = offset + length;
            for (int i = offset; i < end; i++)
            {
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFF;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    if ((c & 1) != 0)
                    {
                        c = 0xEDB88320u ^ (c >> 1);
                    }
                    else
                    {
                        c >>= 1;
                    }
                }

                table[i] = c;
            }

            return table;
        }
    }
}
