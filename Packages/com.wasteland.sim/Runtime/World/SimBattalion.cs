namespace Wasteland.Sim.World
{
    /// <summary>
    /// First-class sim entity. Move commands target battalions, not units.
    /// Positions are integer centimeters. A group of battalions managed by the
    /// AI is a task force: AI-owned deterministic state (other sim systems
    /// cannot read it; it is saved and hashed in a fixed order).
    /// </summary>
    public struct SimBattalion
    {
        public int Id;
        public int OwnerId;
        public int PositionXCm;
        public int PositionYCm;
        public int TargetXCm;
        public int TargetYCm;
        public int SpeedCmPerSecond;
        public int StrengthPpt;
        public bool HasTarget;
    }
}
