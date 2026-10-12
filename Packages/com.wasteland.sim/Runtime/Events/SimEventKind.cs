namespace Wasteland.Sim.Events
{
    public enum SimEventKind : byte
    {
        CommandRejected = 1,
        BattalionSpawned = 2,
        BattalionArrived = 3,

        /// <summary>Raise passed validation and entered the production queue. Spawn is later.</summary>
        BattalionRaiseStarted = 4,

        /// <summary>
        /// Auto research started. Subject is the faction id, aux is the target tier,
        /// param0 is the start tick, param1 is the complete tick.
        /// </summary>
        TierResearchStarted = 5,

        /// <summary>
        /// Current tier incremented. Subject is the faction id, aux is the tier just reached.
        /// </summary>
        TierReached = 6,

        /// <summary>Depot site reserved. Subject depot, aux owner, param0 node, param1 active tick or -1.</summary>
        DepotStarted = 7,

        /// <summary>Depot finished. Subject depot, aux owner, param0 node. Tick is the activation tick.</summary>
        DepotActivated = 8,

        /// <summary>Depot removed. Param1 is 1 when the site was reserved or still under construction.</summary>
        DepotDestroyed = 9,

        /// <summary>
        /// A zone became contested. Subject is the town. Aux is seat A and
        /// param0 is seat B (the lower id first, -1 when a garrison is a side).
        /// Param1 is the town owner. Key is the territory key.
        /// </summary>
        TownContestStarted = 10,

        /// <summary>Contest ended. Payload matches <see cref="TownContestStarted"/>.</summary>
        TownContestEnded = 11,

        /// <summary>Seat town owner changed. Aux is the new owner, param0 the old owner, param1 is 1 for a seat town.</summary>
        TownOwnerChanged = 12,

        /// <summary>
        /// Match is over. Subject is the winner seat, or -1 for a draw.
        /// Aux is 1 when an HQ was captured and 2 for a draw.
        /// Param0 is the losing seat, or -1 for a draw.
        /// </summary>
        MatchEnded = 13,

        /// <summary>A dropped order returned materiel. Subject seat, aux command kind, param0 target, param1 materiel.</summary>
        OrderRefunded = 14,

        /// <summary>
        /// A battalion reached 0 hp. Subject is the battalion, aux is the owner
        /// seat (-1 for a garrison), param0 is the catalog unit index.
        /// </summary>
        BattalionDestroyed = 15
    }
}
