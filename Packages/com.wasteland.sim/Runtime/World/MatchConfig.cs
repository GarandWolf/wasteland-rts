namespace Wasteland.Sim.World
{
    /// <summary>Match-setup constants shared by player and AI.</summary>
    public readonly struct MatchConfig
    {
        public readonly ulong Seed;
        public readonly int CommandDelayTicks;
        public readonly ulong BalanceKey;
        public readonly ulong MapKey;

        public MatchConfig(ulong seed, int commandDelayTicks = SimConfig.DefaultCommandDelayTicks)
            : this(seed, commandDelayTicks, 0UL, 0UL)
        {
        }

        public MatchConfig(
            ulong seed,
            int commandDelayTicks,
            ulong balanceKey,
            ulong mapKey)
        {
            Seed = seed;
            CommandDelayTicks = commandDelayTicks;
            BalanceKey = balanceKey;
            MapKey = mapKey;
        }

        public static MatchConfig Default(ulong seed)
        {
            return new MatchConfig(seed, SimConfig.DefaultCommandDelayTicks);
        }

        public MatchConfig WithKeys(ulong balanceKey, ulong mapKey)
        {
            return new MatchConfig(Seed, CommandDelayTicks, balanceKey, mapKey);
        }
    }
}
