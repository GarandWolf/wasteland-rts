namespace Wasteland.Sim.World
{
    /// <summary>Match-setup constants shared by player and AI.</summary>
    public readonly struct MatchConfig
    {
        public readonly ulong Seed;
        public readonly int CommandDelayTicks;

        public MatchConfig(ulong seed, int commandDelayTicks = SimConfig.DefaultCommandDelayTicks)
        {
            Seed = seed;
            CommandDelayTicks = commandDelayTicks;
        }

        public static MatchConfig Default(ulong seed)
        {
            return new MatchConfig(seed, SimConfig.DefaultCommandDelayTicks);
        }
    }
}
