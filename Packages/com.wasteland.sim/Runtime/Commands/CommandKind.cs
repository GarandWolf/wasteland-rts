namespace Wasteland.Sim.Commands
{
    public enum CommandKind : byte
    {
        SpawnBattalion = 1,
        Move = 2,
        /// <summary>Reserved. The sim does not execute this kind.</summary>
        Build = 3,

        /// <summary>
        /// Raise a catalog unit for a seat. Args are unit index, rally x cm, rally y cm.
        /// The battalion spawns at the seat HQ. The rally is its first Move.
        /// </summary>
        RaiseBattalion = 4,

        /// <summary>
        /// Order a canCapture battalion onto a seat town. Arg0 is the town id.
        /// Capture itself is a world process, not this command.
        /// </summary>
        ClaimTown = 5,

        /// <summary>
        /// Reserve a supply depot on a road node. Arg0 is the node id.
        /// Arg1 and Arg2 stay 0.
        /// </summary>
        BuildDepot = 6
    }
}
