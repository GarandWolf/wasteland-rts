using System.Collections.Generic;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// One-generic-AI MVP cut: every seat <c>aiPersonality</c> maps to the
    /// Normal parameter set. Unknown strings do not fail the load. The
    /// authored id stays on the seat so personalities can return later.
    /// </summary>
    public static class AiGenericPersonality
    {
        public const string ParameterSetId = AiConstants.DifficultyNormal;
        public const string GenericProfileId = AiConstants.PersonalitySettler;

        public static string Map(string? seatPersonalityId)
        {
            _ = seatPersonalityId;
            return ParameterSetId;
        }

        public static string FormatMappedFrom(string? seatPersonalityId)
        {
            if (seatPersonalityId == null || seatPersonalityId.Length == 0)
            {
                return "aiPersonality mappedFrom=null to=" + ParameterSetId;
            }

            return "aiPersonality mappedFrom=" + seatPersonalityId + " to=" + ParameterSetId;
        }

        public static void RecordMappedFrom(IList<string> diagnostics, string? seatPersonalityId)
        {
            if (diagnostics == null)
            {
                return;
            }

            diagnostics.Add(FormatMappedFrom(seatPersonalityId));
        }
    }
}
