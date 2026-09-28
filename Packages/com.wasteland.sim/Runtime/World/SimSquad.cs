namespace Wasteland.Sim.World
{
    /// <summary>
    /// First-class sim entity. Move commands target squads, not units.
    /// Positions are integer centimeters.
    /// </summary>
    public struct SimSquad
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
