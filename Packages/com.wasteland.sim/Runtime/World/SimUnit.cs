using Wasteland.Sim.Math;

namespace Wasteland.Sim.World
{
    public struct SimUnit
    {
        public int Id;
        public int PlayerId;
        public Fix64Vec2 Position;
        public Fix64Vec2 Target;
        public Fix64 Speed;
        public bool HasTarget;
    }
}
