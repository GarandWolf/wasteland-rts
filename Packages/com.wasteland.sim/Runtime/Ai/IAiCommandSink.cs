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
        BuildDepot = 4,
        Attack = 5
    }

    /// <summary>
    /// AI rejection codes. These numbers are not the sim
    /// <see cref="Wasteland.Sim.Events.CommandRejectReason"/> values.
    /// <see cref="InsufficientMaterials"/> is 8 here and 11 on the sim enum.
    /// Never cast one enum to the other. Convert only through the explicit
    /// mapping in <see cref="ForwardingAiCommandSink.MapReject"/>.
    /// </summary>
    public enum AiCommandReject : byte
    {
        None = 0,
        Rejected = 1,
        DepotOutOfReach = 2,
        UnitNotRaisable = 3,
        BuildingNotBuildable = 4,
        TierLocked = 5,
        PopCapReached = 6,
        InsufficientManpower = 7,
        InsufficientMaterials = 8,
        InsufficientFuel = 9,

        /// <summary>Attack target is gone. Not sim reject 27.</summary>
        UnknownTarget = 14,

        /// <summary>Attack target is not an enemy. Not sim reject 28.</summary>
        TargetNotEnemy = 15,

        /// <summary>Attacker has no weapon. Not sim reject 29.</summary>
        NotArmed = 16,

        /// <summary>Attacker and target are the same battalion. Not sim reject 30.</summary>
        TargetIsSelf = 17
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
        public readonly int Sequence;

        public AiIssuedCommand(
            int id,
            int tick,
            int factionId,
            AiIssuedKind kind,
            int arg0,
            int arg1,
            int arg2,
            int arg3)
            : this(id, tick, factionId, kind, arg0, arg1, arg2, arg3, string.Empty, 0)
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
            : this(id, tick, factionId, kind, arg0, arg1, arg2, arg3, unitId, 0)
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
            string unitId,
            int sequence)
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
            Sequence = sequence;
        }
    }

    public interface IAiCommandSink
    {
        int Tick { get; }

        int SubmitMove(int factionId, int battalionId, int xCm, int yCm);

        int SubmitClaimTown(int factionId, int battalionId, int townId);

        int SubmitRaiseBattalion(int factionId, string unitId, int xCm, int yCm);

        int SubmitBuildDepot(int factionId, int battalionId, int nodeId);

        /// <summary>
        /// Enqueue Attack. The return value is the submit sequence, so a
        /// rejection is read with <c>(issuer &lt;&lt; 32) | sequence</c>.
        /// </summary>
        int SubmitAttack(int factionId, int battalionId, int targetBattalionId);

        /// <summary>
        /// Sequence stamped on the last forwarded command. Pairs with
        /// <see cref="Wasteland.Sim.Events.SimEvent.Key"/> on CommandRejected:
        /// <c>(issuer &lt;&lt; 32) | sequence</c>.
        /// </summary>
        int LastSequence { get; }

        AiCommandReject RejectOf(int commandId);

        /// <summary>
        /// Rejection for the command stamped with <paramref name="sequence"/>
        /// by <paramref name="issuerId"/>. Looks up
        /// <c>(issuer &lt;&lt; 32) | sequence</c>, never a battalion id.
        /// </summary>
        AiCommandReject RejectOfSequence(int issuerId, int sequence);

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

        /// <summary>Reserved + under-construction + active sites for one faction.</summary>
        int CountSites(int factionId);

        /// <summary>
        /// Lowest node id among this seat's reserved or under-construction
        /// sites, or 0 when none are in flight.
        /// </summary>
        int InFlightNodeId(int factionId, int tick);
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
