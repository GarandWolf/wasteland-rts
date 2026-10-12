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

        /// <summary>Absolute hit points. 0 means dead and waiting for removal.</summary>
        public int Hp;

        /// <summary>Catalog max hp. Untyped spawns use 1000.</summary>
        public int MaxHp;

        /// <summary><c>Hp * 1000 / MaxHp</c> while alive, else 0.</summary>
        public int StrengthPpt;

        /// <summary>Ticks until the next volley. 0 means ready.</summary>
        public int Cooldown;

        /// <summary>Ordered attack target. 0 means auto-pick.</summary>
        public int AttackTargetId;

        /// <summary>Tick the supply cut was first seen. -1 when in supply.</summary>
        public int CutDetectedTick;

        /// <summary>True after the supply grace has elapsed.</summary>
        public bool Penalized;

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
