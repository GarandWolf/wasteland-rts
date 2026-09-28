namespace Wasteland.Sim
{
    /// <summary>
    /// Match-wide simulation constants. Presentation interpolates between ticks;
    /// nothing in this assembly reads a render clock.
    /// </summary>
    public static class SimConfig
    {
        /// <summary>
        /// Simulation steps per second. Locked at 20 Hz (50 ms) with the AI
        /// design contract. Presentation interpolates; lockstep delay is
        /// counted in these ticks.
        /// </summary>
        public const int TickRateHz = 20;

        /// <summary>Duration of one sim tick in milliseconds.</summary>
        public const int TickDurationMs = 1000 / TickRateHz;

        /// <summary>
        /// Default command delay. Shared by player and AI. A command issued
        /// at tick T executes at T + <see cref="DefaultCommandDelayTicks"/>.
        /// Overridable per match via <c>MatchConfig</c>.
        /// </summary>
        public const int DefaultCommandDelayTicks = 2;

        /// <summary>Parts-per-thousand scale for ratios (1000 = 1.0).</summary>
        public const int PptScale = 1000;

        /// <summary>Duration of one sim tick in seconds, Q32.32, for internal stepping math only.</summary>
        public static readonly Math.Fixed64 TickDuration = Math.Fixed64.One / Math.Fixed64.FromInt(TickRateHz);
    }
}
