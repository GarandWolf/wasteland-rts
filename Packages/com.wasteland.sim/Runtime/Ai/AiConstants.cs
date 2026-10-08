namespace Wasteland.Sim.Ai
{
    /// <summary>Enemy AI constants from the design (Appendix A / §4.2 / §7 / §8) plus M1 economy/claim pins.</summary>
    public static class AiConstants
    {
        public const int SchemaVersion = 1;
        public const int BalanceSchemaVersion = 1;
        public const int DataVersion = 1;
        public const int StateSchemaVersion = 8;
        public const int PendingCommandCapacity = 128;
        public const int CommandLogCapacity = 4096;
        public const int TickRateHz = SimConfig.TickRateHz;
        public const int FactionPlanOffsetTicks = 3;
        public const int PathRequestsPerTick = 8;
        public const int HashPeriodTicks = 200;
        public const int HeadlessLogPeriodTicks = 200;
        public const int HeadlessLogFromTick = 2400;
        public const int HeadlessLogThroughTickMinimum = 6000;
        public const int M0DeterminismTicks = 10000;
        public const int M1DeterminismTicks = 10000;
        public const int SaveLoadMidRunTick = 4000;
        public const int TimeSliceModulus = 8;
        public const int PhaseStrategic = 6;
        public const int DefaultAiFactionA = 1;
        public const int DefaultAiFactionB = 2;
        public const string TheDallesTownName = "The Dalles";
        public const string GoldendaleTownName = "Goldendale";
        public const string HoodRiverTownName = "Hood River";
        public const string WhiteSalmonTownName = "White Salmon";
        public const string KlickitatTownName = "Klickitat";
        public const string RoadConnectedAdjacencySource = "RoadConnectedV0";
        public const string MapNeighborListAdjacencySource = "MapNeighborListV1";
        public const string MapKeyTagChok = "CHOK";
        public const string MapKeyTagTerr = "TERR";
        public const string MapKeyTagRnod = "RNOD";
        public const string MapKeyTagRedg = "REDG";
        public const string MapKeyTagBrdg = "BRDG";
        public const string MapKeyTagTown = "TOWN";

        public const int WhiteSalmonTerritoryId = 9;

        public const int MileCm = 160934;
        public const int HqRangeCm = 40 * MileCm;
        public const int DepotRangeCm = 30 * MileCm;
        public const int DepotMinSpacingCm = 804670;
        public const int MaxDepotsPerNode = 1;
        public const int BuildingTagDepot = 1;
        public const int BuildingTagFortL1 = 2;
        public const int BuildingTagFortL2 = 3;
        public const int UnitKindInfantry = 1;
        public const int UnitKindEngineer = 2;
        public const int UnitKindTierII = 3;
        public const int UnitKindTierIII = 4;

        public const string TierIITickNone = "none";
        public const int ClaimMarchSpeedCmPerSecond = 25000;
        public const int MinAcceptScore = 1000;
        public const int SeatAnchorBonusPpt = 1000;
        public const int BattalionIdStride = 100000;
        public const int OpeningClaimBattalions = 1;
        public const int UnreachableCm = int.MaxValue / 4;

        public static string[] MapKeySectionTags()
        {
            return new[]
            {
                MapKeyTagChok,
                MapKeyTagTerr,
                MapKeyTagRnod,
                MapKeyTagRedg,
                MapKeyTagBrdg,
                MapKeyTagTown
            };
        }

        public const ulong StreamSimWorld = 1UL;
        public const ulong StreamAiRoot = 6UL;
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

    public enum AiGoalType : byte
    {
        None = 0,
        ClaimTown = 1,
        PlaceDepot = 2,
        ExpandEconomy = 3,
        AttackTown = 4,
        DefendTown = 5
    }
}
