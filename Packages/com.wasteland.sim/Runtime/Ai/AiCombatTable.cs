namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Integer combat knobs owned by the AI. Distances are centimetres.
    /// Ratios are parts-per-thousand. Ticks use the sim rate.
    /// </summary>
    internal static class AiCombatTable
    {
        /// <summary>
        /// Drop an Attack intent this many ticks after it was issued and back
        /// off that battalion/target pair. 80 s at 20 Hz.
        /// </summary>
        public const int AttackTimeoutTicks = 1600;

        /// <summary>
        /// Road-graph centimetres from the seat HQ node. A non-own battalion
        /// inside this distance is a threat. The HQ node is the nearest road
        /// node to the seat town, looked up at runtime.
        /// </summary>
        public const int HqThreatRadiusCm = 3 * AiConstants.MileCm;

        /// <summary>
        /// Recalled battalions = threat count * this / <see cref="SimConfig.PptScale"/>.
        /// 1000 recalls one battalion per threatening battalion.
        /// </summary>
        public const int HqRecallMarginPpt = 1000;

        public static int RecallCap(int threatStrength)
        {
            if (threatStrength < 1 || HqRecallMarginPpt < 1)
            {
                return 0;
            }

            long cap = ((long)threatStrength * HqRecallMarginPpt) / SimConfig.PptScale;
            if (cap < 1)
            {
                return 0;
            }

            if (cap > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)cap;
        }
    }
}
