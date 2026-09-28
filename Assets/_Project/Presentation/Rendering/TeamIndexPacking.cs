using System;

namespace Wasteland.Presentation
{
    /// <summary>
    /// Packed <c>_TeamIndex</c> layout from team-color-spec-v2 §2.2.
    /// Pure integer math; safe to share with headless tests.
    /// GPU MaterialProperty slots are float: store <see cref="AsFloat"/> /
    /// <c>math.asfloat(packed)</c> and decode with shader <c>asuint</c>.
    /// </summary>
    public static class TeamIndexPacking
    {
        public const int FactionBits = 0;
        public const int ShadeShift = 2;
        public const int PipsShift = 4;
        public const int HatchBit = 6;
        public const int ValidBit = 7;

        public const uint FactionMask = 3u;
        public const uint ShadeMask = 3u;
        public const uint PipsMask = 3u;

        public const int FactionRemnant = 0;
        public const int FactionCompact = 1;
        public const int FactionClans = 2;
        public const int FactionNeutral = 3;

        public const int ShadeBase = 0;
        public const int ShadeLight = 1;
        public const int ShadeDark = 2;
        public const int ShadeFallback = 3;

        public static uint Pack(int faction, int shade, int pips, bool hatch, bool valid)
        {
            uint value = (uint)(faction & 3);
            value |= (uint)(shade & 3) << ShadeShift;
            value |= (uint)(pips & 3) << PipsShift;
            if (hatch)
            {
                value |= 1u << HatchBit;
            }

            if (valid)
            {
                value |= 1u << ValidBit;
            }

            return value;
        }

        /// <summary>
        /// IEEE bit copy matching <c>Unity.Mathematics.math.asfloat</c>.
        /// Numeric convert (<c>(float)packed</c>) would break shader <c>asuint</c>.
        /// </summary>
        public static float AsFloat(uint packed)
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)packed));
        }

        /// <summary>Inverse of <see cref="AsFloat"/>; matches <c>math.asuint</c>.</summary>
        public static uint AsUInt(float bits)
        {
            return unchecked((uint)BitConverter.SingleToInt32Bits(bits));
        }

        public static int GetFaction(uint packed)
        {
            return (int)(packed & FactionMask);
        }

        public static int GetShade(uint packed)
        {
            return (int)((packed >> ShadeShift) & ShadeMask);
        }

        public static int GetPips(uint packed)
        {
            return (int)((packed >> PipsShift) & PipsMask);
        }

        public static bool GetHatch(uint packed)
        {
            return ((packed >> HatchBit) & 1u) != 0u;
        }

        public static bool GetValid(uint packed)
        {
            return ((packed >> ValidBit) & 1u) != 0u;
        }

        public static int PaletteRow(int faction, int shade)
        {
            return faction * 4 + shade;
        }

        public static int PaletteRow(uint packed)
        {
            return PaletteRow(GetFaction(packed), GetShade(packed));
        }
    }
}
