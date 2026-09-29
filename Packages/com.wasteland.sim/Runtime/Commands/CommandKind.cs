namespace Wasteland.Sim.Commands
{
    public enum CommandKind : byte
    {
        SpawnBattalion = 1,
        Move = 2,
        /// <summary>Future sim building command. M1 AI sink accepts it; SimWorld does not.</summary>
        Build = 3
    }
}
