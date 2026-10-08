using System;
using UnityEngine;

namespace Wasteland.Audio
{
    /// <summary>
    /// Picks the nearest contested towns for the combat loop. Pure: no sources.
    /// </summary>
    public static class CombatVoiceSelector
    {
        public const int MaxVoices = 3;

        /// <summary>
        /// Writes up to <see cref="MaxVoices"/> indices into <paramref name="selectedIndices"/>,
        /// nearest to <paramref name="listener"/> first. An equal distance keeps the lower id.
        /// Returns how many indices were written.
        /// </summary>
        public static int SelectNearest(
            ReadOnlySpan<int> ids,
            ReadOnlySpan<Vector3> positions,
            Vector3 listener,
            Span<int> selectedIndices)
        {
            int n = ids.Length < positions.Length ? ids.Length : positions.Length;
            int cap = selectedIndices.Length < MaxVoices ? selectedIndices.Length : MaxVoices;
            if (cap <= 0 || n <= 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < n; i++)
            {
                float distance = (positions[i] - listener).sqrMagnitude;
                int insertAt = count;
                for (int slot = 0; slot < count; slot++)
                {
                    int other = selectedIndices[slot];
                    float otherDistance = (positions[other] - listener).sqrMagnitude;
                    if (distance < otherDistance || (distance == otherDistance && ids[i] < ids[other]))
                    {
                        insertAt = slot;
                        break;
                    }
                }

                if (insertAt >= cap)
                {
                    continue;
                }

                int newCount = count < cap ? count + 1 : count;
                for (int slot = newCount - 1; slot > insertAt; slot--)
                {
                    selectedIndices[slot] = selectedIndices[slot - 1];
                }

                selectedIndices[insertAt] = i;
                count = newCount;
            }

            return count;
        }
    }

    /// <summary>Linear fade used by <see cref="CombatLoopManager"/>. 0 duration snaps.</summary>
    public static class SoundFades
    {
        public static float Advance(float current, float target, float deltaTime, float fadeSeconds)
        {
            if (fadeSeconds <= 0f)
            {
                return target;
            }

            if (deltaTime <= 0f)
            {
                return current;
            }

            float step = deltaTime / fadeSeconds;
            if (current < target)
            {
                float next = current + step;
                return next > target ? target : next;
            }

            float down = current - step;
            return down < target ? target : down;
        }
    }
}
