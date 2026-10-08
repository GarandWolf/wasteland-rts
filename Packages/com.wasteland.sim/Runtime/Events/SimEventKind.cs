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
        TierReached = 6
    }
}
