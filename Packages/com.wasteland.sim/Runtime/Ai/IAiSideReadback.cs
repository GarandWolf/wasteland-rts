namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Per-side research and pop-cap readback. Fixture-backed until the Lead's
    /// real currentTier / pop-cap query is wired.
    /// </summary>
    public interface IAiSideReadback
    {
        int CurrentTier(int side, int tick);

        bool TryGetPopCap(int side, out int used, out int cap);
    }

    /// <summary>
    /// Seat-derived current tier: available tick is autoResearch start plus
    /// that tier's research duration. Pop cap is optional fixture state.
    /// </summary>
    public sealed class FixtureAiSideReadback : IAiSideReadback
    {
        private AiScenarioSeatView _seat;
        private bool _hasSeat;
        public int PopUsed;
        public int PopCap;

        public void Bind(AiScenarioSeatView seat)
        {
            _seat = seat;
            _hasSeat = true;
        }

        public int CurrentTier(int side, int tick)
        {
            if (!_hasSeat)
            {
                return 1;
            }

            return _seat.CurrentTierAt(tick);
        }

        public bool TryGetPopCap(int side, out int used, out int cap)
        {
            if (PopCap < 1)
            {
                used = 0;
                cap = 0;
                return false;
            }

            used = PopUsed;
            cap = PopCap;
            return true;
        }
    }
}
