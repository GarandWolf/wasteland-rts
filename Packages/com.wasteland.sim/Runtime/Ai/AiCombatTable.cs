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
        /// Parts-per-thousand margin on summed threat strength.
        /// Recall stops when held strength covers the threat the same way
        /// <see cref="AiAttackDefend.StrengthAllowsAttack"/> does:
        /// held * <see cref="SimConfig.PptScale"/> &gt;= threat * this.
        /// 1000 means held strength must reach threat strength.
        /// </summary>
        public const int HqRecallMarginPpt = 1000;

        /// <summary>
        /// Score stamped on an HQ-recall <see cref="AiGoalType.DefendTown"/> order.
        /// Town defence orders use any other score and are left in place.
        /// </summary>
        public const int HqHoldScore = 1;

        /// <summary>
        /// Strength the held battalions must reach for <paramref name="threatStrengthSum"/>.
        /// This is a strength total, not a battalion count. Zero when there is no threat.
        /// </summary>
        public static int RecallCap(int threatStrengthSum)
        {
            if (threatStrengthSum < 1 || HqRecallMarginPpt < 1)
            {
                return 0;
            }

            long need = ((long)threatStrengthSum * HqRecallMarginPpt) / SimConfig.PptScale;
            if (need > int.MaxValue)
            {
                return int.MaxValue;
            }

            if (need < 0)
            {
                return 0;
            }

            return (int)need;
        }

        /// <summary>
        /// How many leading rows to take. <paramref name="strength"/> is already
        /// in recall order (nearer road distance, then uncommitted, then lower id).
        /// Stop once <see cref="AiAttackDefend.StrengthAllowsAttack"/> says the
        /// held sum covers <paramref name="threatStrength"/>, and always take
        /// one row when <paramref name="count"/> is positive.
        /// </summary>
        public static int CountToCover(int[] strength, int count, int threatStrength)
        {
            if (strength == null || count < 1)
            {
                return 0;
            }

            if (count > strength.Length)
            {
                count = strength.Length;
            }

            int summed = 0;
            int taken = 0;
            for (int i = 0; i < count; i++)
            {
                if (taken > 0
                    && AiAttackDefend.StrengthAllowsAttack(summed, threatStrength, HqRecallMarginPpt))
                {
                    break;
                }

                int add = strength[i];
                if (add < 1)
                {
                    add = 1;
                }

                long next = (long)summed + add;
                summed = next > int.MaxValue ? int.MaxValue : (int)next;
                taken++;
            }

            return taken;
        }
    }
}
