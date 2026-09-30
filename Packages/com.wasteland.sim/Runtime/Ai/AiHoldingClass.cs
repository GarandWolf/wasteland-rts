using System;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// WRMB <c>populationClass</c> 0..3 maps to designer holding tiers.
    /// Capture timers, zone radii, and manpower income all use this table.
    /// </summary>
    public static class AiHoldingClass
    {
        public const int Hamlet = 0;
        public const int Village = 1;
        public const int Town = 2;
        public const int City = 3;
        public const int Count = 4;

        public static string RuleId(int populationClass)
        {
            if (populationClass == Hamlet)
            {
                return AiBalanceTables.CaptureRuleHamlet;
            }

            if (populationClass == Village)
            {
                return AiBalanceTables.CaptureRuleVillage;
            }

            if (populationClass == Town)
            {
                return AiBalanceTables.CaptureRuleTown;
            }

            if (populationClass == City)
            {
                return AiBalanceTables.CaptureRuleCity;
            }

            throw new FormatException("No holding-tier rule for populationClass " + populationClass + ".");
        }

        public static int IndexOfRuleId(string ruleId)
        {
            if (ruleId == AiBalanceTables.CaptureRuleHamlet)
            {
                return Hamlet;
            }

            if (ruleId == AiBalanceTables.CaptureRuleVillage)
            {
                return Village;
            }

            if (ruleId == AiBalanceTables.CaptureRuleTown)
            {
                return Town;
            }

            if (ruleId == AiBalanceTables.CaptureRuleCity)
            {
                return City;
            }

            throw new FormatException("Unknown holding-tier id '" + ruleId + "'.");
        }
    }
}
