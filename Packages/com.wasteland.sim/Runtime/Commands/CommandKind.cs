namespace Wasteland.Sim.Commands
{
    public enum CommandKind : byte
    {
        SpawnBattalion = 1,
        Move = 2,
        /// <summary>Future sim building command. M1 AI sink accepts it; SimWorld does not.</summary>
        Build = 3,

        /// <summary>
        /// Raise a catalog unit for a seat. Args are unit index, rally x cm, rally y cm.
        /// The battalion spawns at the seat HQ. The rally is its first Move.
        /// </summary>
        RaiseBattalion = 4
    }
}
