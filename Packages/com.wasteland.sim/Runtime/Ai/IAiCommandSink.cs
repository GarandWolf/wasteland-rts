namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Thin AI command path until the Lead posts the real player-queue
    /// commands (Move, ClaimTown, RaiseBattalion, BuildDepot). Each submit
    /// returns a command id; <see cref="RejectOf"/> is the rejection-code
    /// readback for that id.
    /// </summary>
    public enum AiIssuedKind : byte
    {
        Move = 1,
        ClaimTown = 2,
        RaiseBattalion = 3,
        BuildDepot = 4
    }

    public enum AiCommandReject : byte
    {
        None = 0,
        Rejected = 1,
        DepotOutOfReach = 2,
        UnitNotRaisable = 3,
        BuildingNotBuildable = 4,
        TierLocked = 5,
        PopCapReached = 6,
        InsufficientManpower = 7
    }

    public readonly struct AiIssuedCommand
    {
        public readonly int Id;
        public readonly int Tick;
        public readonly int FactionId;
        public readonly AiIssuedKind Kind;
        public readonly int Arg0;
        public readonly int Arg1;
        public readonly int Arg2;
        public readonly int Arg3;
        public readonly string UnitId;

        public AiIssuedCommand(
            int id,
            int tick,
            int factionId,
            AiIssuedKind kind,
            int arg0,
            int arg1,
            int arg2,
            int arg3)
            : this(id, tick, factionId, kind, arg0, arg1, arg2, arg3, string.Empty)
        {
        }

        public AiIssuedCommand(
            int id,
            int tick,
            int factionId,
            AiIssuedKind kind,
            int arg0,
            int arg1,
            int arg2,
            int arg3,
            string unitId)
        {
            Id = id;
            Tick = tick;
            FactionId = factionId;
            Kind = kind;
            Arg0 = arg0;
            Arg1 = arg1;
            Arg2 = arg2;
            Arg3 = arg3;
            UnitId = unitId ?? string.Empty;
        }
    }

    public interface IAiCommandSink
    {
        int Tick { get; }

        int SubmitMove(int factionId, int battalionId, int xCm, int yCm);

        int SubmitClaimTown(int factionId, int battalionId, int townId);

        int SubmitRaiseBattalion(int factionId, string unitId, int xCm, int yCm);

        int SubmitBuildDepot(int factionId, int battalionId, int nodeId);

        AiCommandReject RejectOf(int commandId);

        int CommandDelayTicks { get; }

        System.Collections.Generic.IReadOnlyList<AiIssuedCommand> IssuedCommands { get; }

        bool RaiseHasStarted(int commandId);
    }

    /// <summary>
    /// Read-only per-town query: owner, contested flag, and each side's
    /// integer strength.
    /// </summary>
    public readonly struct AiTownReadback
    {
        public readonly int OwnerFactionId;
        public readonly bool Contested;
        public readonly int Side0FactionId;
        public readonly int Side0Strength;
        public readonly int Side1FactionId;
        public readonly int Side1Strength;

        public AiTownReadback(
            int ownerFactionId,
            bool contested,
            int side0FactionId,
            int side0Strength,
            int side1FactionId,
            int side1Strength)
        {
            OwnerFactionId = ownerFactionId;
            Contested = contested;
            Side0FactionId = side0FactionId;
            Side0Strength = side0Strength;
            Side1FactionId = side1FactionId;
            Side1Strength = side1Strength;
        }

        public int StrengthOf(int factionId)
        {
            if (Side0FactionId == factionId)
            {
                return Side0Strength;
            }

            if (Side1FactionId == factionId)
            {
                return Side1Strength;
            }

            return 0;
        }

        public int EnemyStrength(int ownFactionId)
        {
            int best = 0;
            if (Side0FactionId != 0 && Side0FactionId != ownFactionId && Side0Strength > best)
            {
                best = Side0Strength;
            }

            if (Side1FactionId != 0 && Side1FactionId != ownFactionId && Side1Strength > best)
            {
                best = Side1Strength;
            }

            return best;
        }
    }

    public interface IAiTownQuery
    {
        bool TryGet(int townId, out AiTownReadback readback);
    }

    /// <summary>
    /// BuildDepot readback: site tick and active tick.
    /// </summary>
    public readonly struct AiDepotReadback
    {
        public readonly int SiteTick;
        public readonly int ActiveTick;

        public AiDepotReadback(int siteTick, int activeTick)
        {
            SiteTick = siteTick;
            ActiveTick = activeTick;
        }

        public bool IsActive(int tick)
        {
            return ActiveTick > 0 && tick >= ActiveTick;
        }
    }

    public interface IAiDepotQuery
    {
        bool TryGet(int factionId, int nodeId, out AiDepotReadback readback);

        void RecordSite(int factionId, int nodeId, int siteTick, int activeTick);
    }

    public readonly struct AiBattalionPin
    {
        public readonly int Id;
        public readonly int XCm;
        public readonly int YCm;
        public readonly int RoadNodeId;
        public readonly string UnitId;
        public readonly bool Assigned;

        public AiBattalionPin(int id, int xCm, int yCm, int roadNodeId, string unitId, bool assigned)
        {
            Id = id;
            XCm = xCm;
            YCm = yCm;
            RoadNodeId = roadNodeId;
            UnitId = unitId ?? string.Empty;
            Assigned = assigned;
        }

        public bool HasRole(IAiUnitCatalog catalog, string role)
        {
            if (catalog == null || string.IsNullOrEmpty(UnitId) || role == null)
            {
                return false;
            }

            return catalog.TryGetRole(UnitId, out string have) && have == role;
        }
    }
}
