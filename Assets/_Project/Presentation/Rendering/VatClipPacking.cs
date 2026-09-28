namespace Wasteland.Presentation
{
    /// <summary>
    /// Packed <c>_AnimIndex</c> layout from team-color-spec-v2 §2.4.1.
    /// Pure integer math; safe to share with headless tests.
    /// </summary>
    public static class VatClipPacking
    {
        public const int ClipBits = 6;
        public const int HoldBit = 6;
        public const int ValidBit = 7;
        public const int SpeedShift = 8;
        public const int SpeedBits = 8;

        public const uint ClipMask = 63u;
        public const uint SpeedMask = 255u;
        public const float SpeedUnit = 64f;
        public const float WrapSeconds = 3600f;

        public const int ClipIdle = 0;
        public const int ClipWalk = 1;
        public const int ClipFire = 2;

        public static uint Pack(int clip, bool hold, bool valid, float speed)
        {
            uint value = (uint)(clip & 63);
            if (hold)
            {
                value |= 1u << HoldBit;
            }

            if (valid)
            {
                value |= 1u << ValidBit;
            }

            int quantized = (int)(speed * SpeedUnit + 0.5f);
            if (quantized < 0)
            {
                quantized = 0;
            }
            else if (quantized > 255)
            {
                quantized = 255;
            }

            value |= (uint)quantized << SpeedShift;
            return value;
        }

        public static int GetClip(uint packed)
        {
            return (int)(packed & ClipMask);
        }

        public static bool GetHold(uint packed)
        {
            return ((packed >> HoldBit) & 1u) != 0u;
        }

        public static bool GetValid(uint packed)
        {
            return ((packed >> ValidBit) & 1u) != 0u;
        }

        public static float GetSpeed(uint packed)
        {
            return ((packed >> SpeedShift) & SpeedMask) / SpeedUnit;
        }

        public static float WrapLocalTime(float globalTime, float startTime)
        {
            float elapsed = globalTime - startTime + WrapSeconds;
            return elapsed - WrapSeconds * (float)System.Math.Floor(elapsed / WrapSeconds);
        }
    }
}
