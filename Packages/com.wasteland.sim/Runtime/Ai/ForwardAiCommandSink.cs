using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Forwards AI orders into the sim queue. A raise carries the rally
    /// point as its first Move. The battalion spawns at the seat HQ town,
    /// never at the rally.
    /// </summary>
    public sealed class ForwardAiCommandSink
    {
        private readonly SimWorld _world;
        private int _nextBattalionId;

        public ForwardAiCommandSink(SimWorld world)
        {
            if (world == null)
            {
                throw new System.ArgumentNullException(nameof(world));
            }

            _world = world;
            _nextBattalionId = 1;
        }

        public int Tick
        {
            get { return _world.Tick; }
        }

        public int SubmitMove(int factionId, int battalionId, int xCm, int yCm)
        {
            _world.Submit(SimCommand.Move(_world.Tick, factionId, battalionId, xCm, yCm));
            return battalionId;
        }

        /// <summary>
        /// <paramref name="xCm"/> and <paramref name="yCm"/> are the rally.
        /// Returns the battalion id the raise will spawn.
        /// </summary>
        public int SubmitRaiseBattalion(int factionId, string unitId, int xCm, int yCm)
        {
            int battalionId = _nextBattalionId;
            _nextBattalionId++;
            _world.SubmitRaiseBattalion(factionId, battalionId, unitId, xCm, yCm);
            return battalionId;
        }

        public CommandRejectReason RejectOf(int subjectId)
        {
            CommandRejectReason code;
            int param0;
            int param1;
            if (_world.TryGetRejection(subjectId, out code, out param0, out param1))
            {
                return code;
            }

            return CommandRejectReason.None;
        }
    }
}
