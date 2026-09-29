namespace Wasteland.Sim.Ai
{
    /// <summary>M0 seams constants from the enemy AI design (Appendix A / §4.2 / §7 / §8).</summary>
    public static class AiConstants
    {
        public const int SchemaVersion = 1;
        public const int TickRateHz = SimConfig.TickRateHz;
        public const int FactionPlanOffsetTicks = 3;
        public const int AiWorkUnitsPerTick = 50000;
        public const int PathRequestsPerTick = 8;
        public const int HashPeriodTicks = 200;
        public const int HeadlessLogPeriodTicks = 200;
        public const int HeadlessLogFromTick = 2400;
        public const int HeadlessLogThroughTickMinimum = 6000;
        public const int M0DeterminismTicks = 10000;
        public const int TimeSliceModulus = 8;
        public const int PhaseStrategic = 6;
        public const int DefaultAiFactionA = 1;
        public const int DefaultAiFactionB = 2;
        public const string TheDallesTownName = "The Dalles";
        public const string RoadConnectedAdjacencySource = "RoadConnectedV0";

        public const ulong StreamAiRoot = 1UL;
        public const ulong StreamStrategy = 2UL;
        public const ulong StreamTactical = 3UL;
        public const ulong StreamMistake = 4UL;
        public const ulong StreamScout = 5UL;

        public const string FogCheatNone = "none";
        public const string DifficultyNormal = "normal";
        public const string DifficultyBrutal = "brutal";
        public const string PersonalityWarlord = "warlord";
        public const string PersonalitySettler = "homestead_compact_settler";
        public const string PersonalityRaider = "raider";
    }

    public enum AiPosture : byte
    {
        Turtle = 0,
        BuildUp = 1,
        Attack = 2,
        Counterattack = 3
    }
}
