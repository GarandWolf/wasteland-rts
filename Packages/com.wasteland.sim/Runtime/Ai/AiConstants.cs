namespace Wasteland.Sim.Ai
{
    /// <summary>Enemy AI constants from the design (Appendix A / §4.2 / §7 / §8) plus M1 economy/claim pins.</summary>
    public static class AiConstants
    {
        public const int SchemaVersion = 1;
        public const int BalanceSchemaVersion = 1;
        public const int DataVersion = 1;
        public const int StateSchemaVersion = 12;
        public const int PendingCommandCapacity = 128;
        public const int PendingIntentCapacity = 32;

        /// <summary>
        /// Slots <see cref="AiIssuedKind.Move"/> cannot occupy. This only holds
        /// space against Moves. It does not reserve the rows for ClaimTown or
        /// BuildDepot, and those orders can fill the whole table.
        /// </summary>
        public const int MoveExcludedIntentSlots = 8;

        /// <summary>
        /// Move intents stop at this count. The other
        /// <see cref="MoveExcludedIntentSlots"/> stay out of Move occupancy.
        /// </summary>
        public const int MoveIntentCapacity = PendingIntentCapacity - MoveExcludedIntentSlots;
        /// <summary>
        /// Shared 128-slot depot-node blacklist for every seat, keyed by
        /// (node, seat). Seats do not own separate lists.
        /// </summary>
        public const int DepotNodeBlacklistCapacity = 128;
        public const int OrderBackoffCapacity = 128;

        /// <summary>
        /// Drop a pending ClaimTown when the town still has no capture progress
        /// after this many ticks (60 s at 20 Hz). Long enough that a command
        /// delay plus a short walk is not treated as stuck; a claim that never
        /// starts occupancy is released so the seat can retarget.
        /// </summary>
        public const int ClaimTownNoProgressTicks = 1200;

        /// <summary>
        /// NoPath, DepotTooClose, and builder-death blacklist entries drop after
        /// this many ticks (120 s at 20 Hz). DepotTooClose expires because the
        /// blocking depot can be destroyed. Placement rejects stay until the
        /// territory owner changes.
        /// </summary>
        public const int BlacklistTransientExpiryTicks = 2400;

        public const int DepotRejectSpamTicks = 20000;
        public const int DepotRejectSpamTicksLong = 20000;
        public const int MaxDepotRejectsPerNode = 3;
        public const int MaxRejectsPerOrderKey = 3;
        public const int MaxClaimTownsPerSeatTown = 3;
        public const int MaxBuildDepotsPerSeat = 5;
        public const int MaxMovesPerBattalion = 50;
        public const int NodeControlRadiusCm = 80467;
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

        public const string RoleLineInfantry = "line_infantry";
        public const string RoleAntiTank = "anti_tank";
        public const string RoleGarrisonInfantry = "garrison_infantry";
        public const string RoleEngineer = "engineer";
        public const string RoleReconSpotter = "recon_spotter";
        public const string RoleReconStrike = "recon_strike";
        public const string TargetClassArmored = "armored";
        public const string TargetClassSoft = "soft";
        public const string BuildingRoleHq = "hq";
        public const string BuildingRoleProduction = "production";
        public const string BuildingRoleSupplyDepot = "supply_depot";
        public const string BuildingRoleWall = "wall";
        public const string BuildingRoleFortification = "fortification";
        public const string BuildingRoleResource = "resource";
        public const string BuildingRoleRecruitment = "recruitment";
        public const string BuildingIdSupplyDepot = "supply_depot";

        /// <summary>
        /// Closed unit-role list. Mix keys are the first four; the rest load
        /// from the shared pack and are never treated as rifle.
        /// </summary>
        public static readonly string[] UnitRoles =
        {
            RoleLineInfantry,
            RoleAntiTank,
            RoleGarrisonInfantry,
            RoleEngineer,
            "assault_infantry",
            "breakthrough",
            "indirect_fire",
            RoleReconSpotter,
            RoleReconStrike,
            "supply"
        };

        /// <summary>
        /// Closed building-role list from Designer PR #22, in authored order.
        /// </summary>
        public static readonly string[] BuildingRoles =
        {
            BuildingRoleHq,
            BuildingRoleProduction,
            BuildingRoleSupplyDepot,
            BuildingRoleWall,
            BuildingRoleFortification,
            BuildingRoleResource,
            BuildingRoleRecruitment
        };

        public const string TierIITickNone = "none";
        public const int ClaimMarchSpeedCmPerSecond = 25000;
        public const int MinAcceptScore = 1000;
        public const int SeatAnchorBonusPpt = 1000;
        public const int BattalionIdStride = 100000;
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
