using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Math;
using Wasteland.Sim.Random;

namespace Wasteland.Sim.World
{
    /// <summary>
    /// Minimal deterministic world: spawn units, issue Move commands, step at
    /// a fixed tick. All mutation goes through the command queue. Designed so
    /// a later lockstep netcode layer only has to exchange <see cref="SimCommand"/>
    /// batches per tick.
    /// </summary>
    public sealed class SimWorld
    {
        private readonly List<SimUnit> _units = new List<SimUnit>();
        private readonly Dictionary<int, int> _indexById = new Dictionary<int, int>();
        private readonly CommandQueue _commands = new CommandQueue();
        private SimRandom _rng;
        private readonly ulong _seed;

        public SimWorld(ulong seed)
        {
            _seed = seed;
            _rng = new SimRandom(seed);
            Tick = 0;
        }

        public int Tick { get; private set; }

        public ulong Seed
        {
            get { return _seed; }
        }

        public int UnitCount
        {
            get { return _units.Count; }
        }

        public SimRandom Rng
        {
            get { return _rng; }
        }

        public SimUnit GetUnit(int id)
        {
            return _units[IndexOf(id)];
        }

        public IReadOnlyList<SimUnit> Units
        {
            get { return _units; }
        }

        /// <summary>
        /// Schedule a command for a tick at or after the current tick.
        /// Commands in the past are rejected so a lockstep peer cannot rewrite history.
        /// </summary>
        public void Submit(SimCommand command)
        {
            if (command.Tick < Tick)
            {
                throw new InvalidOperationException(
                    "Cannot submit a command for tick " + command.Tick + " after the world has reached tick " + Tick + ".");
            }

            _commands.Enqueue(command);
        }

        /// <summary>
        /// Apply commands for the current tick, integrate motion, advance tick,
        /// return the new state hash.
        /// </summary>
        public ulong Step()
        {
            ApplyCommands(Tick);
            IntegrateMovement();
            Tick++;
            return HashState();
        }

        public ulong HashState()
        {
            Fnv64 hash = Fnv64.Create();
            hash.AddInt(Tick);
            hash.AddULong(_seed);
            hash.AddULong(_rng.State);
            hash.AddULong(_rng.Stream);
            hash.AddInt(_units.Count);

            for (int i = 0; i < _units.Count; i++)
            {
                SimUnit unit = _units[i];
                hash.AddInt(unit.Id);
                hash.AddInt(unit.PlayerId);
                hash.AddLong(unit.Position.X.Raw);
                hash.AddLong(unit.Position.Y.Raw);
                hash.AddLong(unit.Target.X.Raw);
                hash.AddLong(unit.Target.Y.Raw);
                hash.AddLong(unit.Speed.Raw);
                hash.AddBool(unit.HasTarget);
            }

            return hash.Digest;
        }

        private void ApplyCommands(int tick)
        {
            IReadOnlyList<SimCommand> commands = _commands.CommandsForTick(tick);
            for (int i = 0; i < commands.Count; i++)
            {
                SimCommand command = commands[i];
                switch (command.Kind)
                {
                    case CommandKind.Spawn:
                        ApplySpawn(command);
                        break;
                    case CommandKind.Move:
                        ApplyMove(command);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown command kind " + command.Kind + ".");
                }
            }

            _commands.RemoveTick(tick);
        }

        private void ApplySpawn(SimCommand command)
        {
            if (_indexById.ContainsKey(command.UnitId))
            {
                throw new InvalidOperationException("Unit " + command.UnitId + " already exists.");
            }

            SimUnit unit = new SimUnit
            {
                Id = command.UnitId,
                PlayerId = command.PlayerId,
                Position = new Fix64Vec2(command.Arg0, command.Arg1),
                Target = Fix64Vec2.Zero,
                Speed = command.Arg2,
                HasTarget = false
            };

            int insertAt = _units.BinarySearch(unit, UnitIdComparer.Instance);
            if (insertAt < 0)
            {
                insertAt = ~insertAt;
            }

            _units.Insert(insertAt, unit);
            RebuildIndex();
        }

        private void ApplyMove(SimCommand command)
        {
            int index = IndexOf(command.UnitId);
            SimUnit unit = _units[index];
            unit.Target = new Fix64Vec2(command.Arg0, command.Arg1);
            unit.HasTarget = true;
            _units[index] = unit;
        }

        private void IntegrateMovement()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                SimUnit unit = _units[i];
                if (!unit.HasTarget)
                {
                    continue;
                }

                Fix64 maxStep = unit.Speed * SimConfig.TickDuration;
                Fix64Vec2 delta = unit.Target - unit.Position;
                Fix64 distance = delta.Length();
                if (distance.Raw == 0 || distance <= maxStep)
                {
                    unit.Position = unit.Target;
                    unit.HasTarget = false;
                }
                else
                {
                    unit.Position += delta * (maxStep / distance);
                }

                _units[i] = unit;
            }
        }

        private int IndexOf(int unitId)
        {
            if (!_indexById.TryGetValue(unitId, out int index))
            {
                throw new InvalidOperationException("Unknown unit " + unitId + ".");
            }

            return index;
        }

        private void RebuildIndex()
        {
            _indexById.Clear();
            for (int i = 0; i < _units.Count; i++)
            {
                _indexById.Add(_units[i].Id, i);
            }
        }

        private sealed class UnitIdComparer : IComparer<SimUnit>
        {
            public static readonly UnitIdComparer Instance = new UnitIdComparer();

            public int Compare(SimUnit x, SimUnit y)
            {
                return x.Id.CompareTo(y.Id);
            }
        }
    }
}
