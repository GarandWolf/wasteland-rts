namespace Wasteland.Sim
{
    /// <summary>
    /// Fixed simulation cadence. Presentation interpolates between ticks;
    /// nothing in this assembly reads a render clock.
    /// </summary>
    public static class SimConfig
    {
        /// <summary>
        /// Simulation steps per second. 20 Hz (50 ms) is a proven RTS lockstep
        /// rate: enough temporal resolution for movement/combat, cheap enough
        /// for large unit counts, and a natural command-delay quantum once
        /// multiplayer lands.
        /// </summary>
        public const int TickRateHz = 20;

        /// <summary>Duration of one sim tick in seconds, as Q31.32.</summary>
        public static readonly Math.Fix64 TickDuration = Math.Fix64.One / Math.Fix64.FromInt(TickRateHz);
    }
}
