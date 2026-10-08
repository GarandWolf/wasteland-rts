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
    /// Fixture-backed <see cref="IAiSeatView"/>. Research start/complete
    /// simulation lives only here: start at the earliest tier2
    /// <c>NotBeforeTick</c>, complete after <see cref="Tier2ResearchTicks"/>.
    /// AI decisions must read <see cref="CurrentTier"/>, not this schedule.
    /// </summary>
    public sealed class FixtureAiSeatView : IAiSeatView
    {
        public int CurrentTier { get; set; }

        public int TierResearchCompleteTick { get; set; }

        public int Manpower { get; set; }

        public int Materials { get; set; }

        public int PopUsed { get; set; }

        public int PopCap { get; set; }

        public int Tier2ResearchTicks { get; set; }

        public FixtureAiSeatView()
        {
            CurrentTier = 1;
            TierResearchCompleteTick = -1;
        }

        public void Refresh(AiScenarioSeatView seat, int tick)
        {
            int notBefore = 0;
            bool hasTier2 = false;
            AiAutoResearch[] rows = seat.AutoResearch;
            if (rows != null)
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i].Item != FixtureAiScenarioSeats.AutoResearchTier2)
                    {
                        continue;
                    }

                    if (!hasTier2 || rows[i].NotBeforeTick < notBefore)
                    {
                        notBefore = rows[i].NotBeforeTick;
                    }

                    hasTier2 = true;
                }
            }

            if (!hasTier2)
            {
                CurrentTier = 1;
                TierResearchCompleteTick = -1;
                return;
            }

            if (tick < notBefore)
            {
                CurrentTier = 1;
                TierResearchCompleteTick = -1;
                return;
            }

            int complete = checked(notBefore + Tier2ResearchTicks);
            if (tick < complete)
            {
                CurrentTier = 1;
                TierResearchCompleteTick = complete;
                return;
            }

            CurrentTier = 2;
            TierResearchCompleteTick = -1;
        }
    }
}
