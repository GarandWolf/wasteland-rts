namespace Wasteland.Sim.Events
{
    public enum SimEventKind : byte
    {
        CommandRejected = 1,
        BattalionSpawned = 2,
        BattalionArrived = 3,

        /// <summary>Raise passed validation and entered the production queue. Spawn is later.</summary>
        BattalionRaiseStarted = 4
    }
}
