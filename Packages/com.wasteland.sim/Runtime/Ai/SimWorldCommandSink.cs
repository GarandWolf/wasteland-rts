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
            if (command.IssuedTick != Tick)
            {
                command = command.WithIssuedTick(Tick);
            }

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

        public CaptureZoneOccupancyResult OccupancyOf(int xCm, int yCm, int radiusCm)
        {
            if (radiusCm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusCm));
            }

            long radiusSq = (long)radiusCm * radiusCm;
            int occupant = 0;
            IReadOnlyList<SimBattalion> battalions = _world.Battalions;
            for (int i = 0; i < battalions.Count; i++)
            {
                SimBattalion battalion = battalions[i];
                long dx = (long)battalion.PositionXCm - xCm;
                long dy = (long)battalion.PositionYCm - yCm;
                if ((dx * dx) + (dy * dy) > radiusSq)
                {
                    continue;
                }

                if (occupant == 0)
                {
                    occupant = battalion.OwnerId;
                }
                else if (occupant != battalion.OwnerId)
                {
                    return new CaptureZoneOccupancyResult(CaptureOccupancyKind.Contested, 0);
                }
            }

            if (occupant == 0)
            {
                return new CaptureZoneOccupancyResult(CaptureOccupancyKind.Empty, 0);
            }

            return new CaptureZoneOccupancyResult(CaptureOccupancyKind.OneFaction, occupant);
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
