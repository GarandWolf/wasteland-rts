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
        NoPath = 14
    }
}
