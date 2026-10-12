namespace Wasteland.Sim.Events
{
    /// <summary>
    /// Rejection code stored on the sim event. Params live beside the code
    /// (<see cref="SimEvent.Param0"/>, <see cref="SimEvent.Param1"/>). The sim
    /// never stores a rejection string.
    /// </summary>
    public enum CommandRejectReason : byte
    {
        None = 0,
        TooLate = 1,
        UnknownBattalion = 2,
        DuplicateBattalion = 3,
        NotOwner = 4,
        NegativeSpeed = 5,
        UnknownKind = 6,

        /// <summary>Param0 = required tier, Param1 = seat current tier. Wins over <see cref="UnitNotRaisable"/>.</summary>
        TierLocked = 7,

        /// <summary>Param0 = catalog unit index, or -1 when the id is unknown. Param1 = 0.</summary>
        UnitNotRaisable = 8,

        /// <summary>Param0 = pop cap, Param1 = pop used.</summary>
        PopCapReached = 9,

        /// <summary>Param0 = manpower cost, Param1 = manpower on hand.</summary>
        InsufficientManpower = 10,

        /// <summary>Param0 = materiel cost, Param1 = materials on hand.</summary>
        InsufficientMaterials = 11,

        /// <summary>Param0 = fuel cost, Param1 = fuel on hand. Skipped when the seat has fuel disabled.</summary>
        InsufficientFuel = 12,

        /// <summary>No seat is bound for the issuing faction. Params are 0.</summary>
        UnknownSeat = 13,

        /// <summary>Road A* found no path. Param0 = from node id, Param1 = to node id.</summary>
        NoPath = 14,

        /// <summary>Param0 = town id.</summary>
        UnknownTown = 15,

        /// <summary>The battalion cannot capture.</summary>
        NotCapturer = 16,

        /// <summary>The town is already owned by the issuer.</summary>
        AlreadyOwned = 17,

        /// <summary>The battalion cannot build.</summary>
        NotBuilder = 18,

        /// <summary>supply_depot is not in the seat buildable list.</summary>
        BuildingNotBuildable = 19,

        /// <summary>Param0 = node id.</summary>
        UnknownNode = 20,

        /// <summary>The node is not in a territory the builder owns. Param0 = node id, Param1 = territory id.</summary>
        TerritoryNotOwned = 21,

        /// <summary>The node is outside own HQ and depot supply range.</summary>
        NotConnectedToSupply = 22,

        /// <summary>Another depot is closer than the minimum road spacing.</summary>
        DepotTooClose = 23,

        /// <summary>The node already has a depot. Param0 = node id.</summary>
        NodeHasDepot = 24,

        /// <summary>Param0 = cap, Param1 = depots already counted.</summary>
        DepotCapReached = 25,

        /// <summary>The town is not a territory seat. Param0 = town id.</summary>
        NotSeatTown = 26,

        /// <summary>Attack target is not a living battalion. Param0 = target id. Depots and walls use this too.</summary>
        UnknownTarget = 27,

        /// <summary>Target exists but is not an enemy. Param0 = target id, Param1 = target owner.</summary>
        TargetNotEnemy = 28,

        /// <summary>The attacker has no weapon. Param0 = catalog unit index.</summary>
        NotArmed = 29,

        /// <summary>The attacker and the target are the same battalion. Param0 = target id.</summary>
        TargetIsSelf = 30
    }
}
