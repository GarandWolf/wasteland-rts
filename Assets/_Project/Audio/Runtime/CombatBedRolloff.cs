namespace Wasteland.Audio
{
    /// <summary>
    /// Pure gain curve for combat beds heard from the orthographic tactical camera.
    /// Full gain out to the view half-width, then linear to zero at reach.
    /// Distances are 3D metres from the listener (camera) to the bed.
    /// #49 used Logarithmic rolloff with minDistance 1 m, so a bed directly below a
    /// camera 40 m up was already 1/40 (about -32 dB), and quieter anywhere else.
    /// </summary>
    public static class CombatBedRolloff
    {
        public const float ReachMargin = 80f;

        public static void Compute(float halfWidth, float halfHeight, float listenerHeight, out float fullGainDistance, out float reach)
        {
            if (!(halfWidth > 0f)) halfWidth = 1f;
            if (!(halfHeight > 0f)) halfHeight = halfWidth;
            if (!(listenerHeight > 0f)) listenerHeight = 0f;
            fullGainDistance = halfWidth;
            float halfDiagonal = (float)System.Math.Sqrt(halfWidth * (double)halfWidth + halfHeight * (double)halfHeight);
            reach = halfDiagonal + listenerHeight + ReachMargin;
            if (reach <= fullGainDistance) reach = fullGainDistance + ReachMargin;
        }

        public static float Gain(float distance, float fullGainDistance, float reach)
        {
            if (distance <= fullGainDistance) return 1f;
            if (distance >= reach) return 0f;
            return 1f - (distance - fullGainDistance) / (reach - fullGainDistance);
        }

        /// <summary>Knee of the custom curve as a fraction of maxDistance (= reach).</summary>
        public static float NormalizedKnee(float fullGainDistance, float reach)
        {
            return reach > 0f ? fullGainDistance / reach : 0f;
        }
    }
}
