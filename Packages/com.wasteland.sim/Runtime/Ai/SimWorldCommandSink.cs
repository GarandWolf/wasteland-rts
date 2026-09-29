using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.World;

namespace Wasteland.Sim.Ai
{
    /// <summary>
    /// Adapter: spawn/move go through <see cref="SimWorld.Submit"/>. Build is
    /// accepted here so SimWorld does not stub buildings.
    /// </summary>
    public sealed class SimWorldCommandSink : ISimCommandSink, ISimWorldView, ICaptureOccupancyView
    {
        private readonly SimWorld _world;

        public SimWorldCommandSink(SimWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public int Tick
        {
            get { return _world.Tick; }
        }

        public int CommandDelayTicks
        {
            get { return _world.CommandDelayTicks; }
        }

        public int BattalionCount
        {
            get { return _world.BattalionCount; }
        }

        public SimCommand Submit(SimCommand command)
        {
            if (command.Kind == CommandKind.Build)
            {
                return command;
            }

            return _world.Submit(command);
        }

        public void CopyOwnBattalionIds(int factionId, List<int> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                if (battalions[i].OwnerId == factionId)
                {
                    into.Add(battalions[i].Id);
                }
            }
        }

        public void CopyVisibleEnemyBattalionIds(int factionId, List<int> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            // Fog-honest: no vision / LoS hook on main yet. Never leak live enemies.
            into.Clear();
        }

        public void CopyOccupancyBattalions(List<SimBattalion> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                into.Add(battalions[i]);
            }
        }

        public int CountOwnBattalions(int factionId)
        {
            int count = 0;
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                if (battalions[i].OwnerId == factionId)
                {
                    count++;
                }
            }

            return count;
        }

        public bool TryGetOwnBattalion(int factionId, int battalionId, out SimBattalion battalion)
        {
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                if (battalions[i].Id == battalionId)
                {
                    if (battalions[i].OwnerId != factionId)
                    {
                        battalion = default;
                        return false;
                    }

                    battalion = battalions[i];
                    return true;
                }
            }

            battalion = default;
            return false;
        }
    }
}
