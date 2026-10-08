using Wasteland.Sim.Movement;

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

        /// <summary>Catalog index of the raised unit. -1 for an untyped spawn.</summary>
        public int UnitIndex;

        public MovementClass MoveClass;

        /// <summary>Index into <see cref="PathXCm"/> while a road path is active.</summary>
        public int PathIndex;

        public int PathCount;
        public int[] PathXCm;
        public int[] PathYCm;
    }
}
