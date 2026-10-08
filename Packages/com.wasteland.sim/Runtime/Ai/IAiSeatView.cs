namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Per-seat live readback. Field names match the Lead's Friday view so
    /// swapping the fixture for the real binding is one assignment.
    /// <see cref="TierResearchCompleteTick"/> is -1 when no research is running.
    /// </summary>
    public interface IAiSeatView
    {
        int CurrentTier { get; }

        int TierResearchCompleteTick { get; }

        int Manpower { get; }

        int Materials { get; }

        int PopUsed { get; }

        int PopCap { get; }
    }

    /// <summary>
    /// Fixture-backed <see cref="IAiSeatView"/>. Refresh fills tier fields
    /// from a scenario seat and tick; pop/resources are set by the test.
    /// </summary>
    public sealed class FixtureAiSeatView : IAiSeatView
    {
        public int CurrentTier { get; set; }

        public int TierResearchCompleteTick { get; set; }

        public int Manpower { get; set; }

        public int Materials { get; set; }

        public int PopUsed { get; set; }

        public int PopCap { get; set; }

        public FixtureAiSeatView()
        {
            CurrentTier = 1;
            TierResearchCompleteTick = -1;
        }

        public void Refresh(AiScenarioSeatView seat, int tick)
        {
            if (seat.AutoResearch.Tier < 2)
            {
                CurrentTier = 1;
                TierResearchCompleteTick = -1;
                return;
            }

            int available = seat.AutoResearch.AvailableTick;
            if (tick < seat.AutoResearch.UnlockTick)
            {
                CurrentTier = 1;
                TierResearchCompleteTick = -1;
                return;
            }

            if (tick < available)
            {
                CurrentTier = 1;
                TierResearchCompleteTick = available;
                return;
            }

            CurrentTier = seat.AutoResearch.Tier >= 3 ? 3 : 2;
            TierResearchCompleteTick = -1;
        }
    }
}
