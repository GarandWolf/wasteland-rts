namespace Wasteland.Sim.World
{
    /// <summary>Match-setup constants shared by player and AI.</summary>
    public readonly struct MatchConfig
    {
        public readonly ulong Seed;
        public readonly int CommandDelayTicks;
        public readonly ulong PersonalityJsonHash;
        public readonly ulong DifficultyJsonHash;

        public MatchConfig(ulong seed, int commandDelayTicks = SimConfig.DefaultCommandDelayTicks)
            : this(seed, commandDelayTicks, 0UL, 0UL)
        {
        }

        public MatchConfig(
            ulong seed,
            int commandDelayTicks,
            ulong personalityJsonHash,
            ulong difficultyJsonHash)
        {
            Seed = seed;
            CommandDelayTicks = commandDelayTicks;
            PersonalityJsonHash = personalityJsonHash;
            DifficultyJsonHash = difficultyJsonHash;
        }

        public static MatchConfig Default(ulong seed)
        {
            return new MatchConfig(seed, SimConfig.DefaultCommandDelayTicks);
        }

        public MatchConfig WithBalanceHashes(ulong personalityJsonHash, ulong difficultyJsonHash)
        {
            return new MatchConfig(Seed, CommandDelayTicks, personalityJsonHash, difficultyJsonHash);
        }
    }
}
