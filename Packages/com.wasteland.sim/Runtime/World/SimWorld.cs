using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Math;
using Wasteland.Sim.Random;

namespace Wasteland.Sim.World
{
    /// <summary>
    /// Minimal deterministic world: spawn battalions, issue Move commands, step at
    /// 20 Hz. All mutation goes through the delayed command queue. Validation
    /// is authoritative at execute time; rejections emit
    /// <see cref="SimEventKind.CommandRejected"/>.
    /// </summary>
    public sealed class SimWorld
    {
        private readonly List<SimBattalion> _battalions = new List<SimBattalion>();
        private readonly Dictionary<int, int> _battalionIndexById = new Dictionary<int, int>();
        private readonly List<SimUnit> _units = new List<SimUnit>();
        private readonly Dictionary<int, int> _unitIndexById = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _nextSequenceByIssuer = new Dictionary<int, int>();
        private readonly CommandQueue _commands = new CommandQueue();
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private SimRandom _rng;
        private readonly ulong _seed;
        private readonly int _commandDelayTicks;
        private int _nextEventId;

        public SimWorld(ulong seed)
            : this(MatchConfig.Default(seed))
        {
        }

        public SimWorld(MatchConfig config)
        {
            if (config.CommandDelayTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(config), "commandDelayTicks must be >= 0.");
            }

            _seed = config.Seed;
            _commandDelayTicks = config.CommandDelayTicks;
            _rng = new SimRandom(config.Seed);
            Tick = 0;
        }

        public int Tick { get; private set; }

        public ulong Seed
        {
            get { return _seed; }
        }

        public int CommandDelayTicks
        {
            get { return _commandDelayTicks; }
        }

        public int BattalionCount
        {
            get { return _battalions.Count; }
        }

        public int UnitCount
        {
            get { return _units.Count; }
        }

        public SimRandom Rng
        {
            get { return _rng; }
        }

        public IReadOnlyList<SimBattalion> Battalions
        {
            get { return _battalions; }
        }

        public IReadOnlyList<SimUnit> Units
        {
            get { return _units; }
        }

        public IReadOnlyList<SimEvent> Events
        {
            get { return _events; }
        }

        public SimBattalion GetBattalion(int id)
        {
            return _battalions[BattalionIndexOf(id)];
        }

        public SimUnit GetUnit(int id)
        {
            return _units[UnitIndexOf(id)];
        }

        /// <summary>
        /// Stamp execute tick and per-issuer sequence, then queue.
        /// Commands whose execute tick is already in the past emit
        /// <see cref="CommandRejectReason.TooLate"/> and are not queued.
        /// Gameplay legality is checked at execute time.
        /// </summary>
        public SimCommand Submit(SimCommand command)
        {
            int executeTick = checked(command.IssuedTick + _commandDelayTicks);
            if (executeTick < Tick)
            {
                Emit(SimEventKind.CommandRejected, command.BattalionId, (int)CommandRejectReason.TooLate);
                return command;
            }

            if (!_nextSequenceByIssuer.TryGetValue(command.IssuerId, out int sequence))
            {
                sequence = 0;
            }

            _nextSequenceByIssuer[command.IssuerId] = sequence + 1;
            SimCommand stamped = command.WithSchedule(executeTick, sequence);
            _commands.Enqueue(stamped);
            return stamped;
        }

        public ulong Step()
        {
            ApplyCommands(Tick);
            IntegrateMovement();
            Tick++;
            return HashState();
        }

        public ulong HashState()
        {
            XxHash64Builder hash = new XxHash64Builder();
            hash.AddInt(Tick);
            hash.AddULong(_seed);
            hash.AddInt(_commandDelayTicks);
            hash.AddULong(_rng.State);
            hash.AddULong(_rng.Increment);
            hash.AddInt(_battalions.Count);

            for (int i = 0; i < _battalions.Count; i++)
            {
                SimBattalion battalion = _battalions[i];
                hash.AddInt(battalion.Id);
                hash.AddInt(battalion.OwnerId);
                hash.AddInt(battalion.PositionXCm);
                hash.AddInt(battalion.PositionYCm);
                hash.AddInt(battalion.TargetXCm);
                hash.AddInt(battalion.TargetYCm);
                hash.AddInt(battalion.SpeedCmPerSecond);
                hash.AddInt(battalion.StrengthPpt);
                hash.AddBool(battalion.HasTarget);
            }

            hash.AddInt(_units.Count);
            for (int i = 0; i < _units.Count; i++)
            {
                SimUnit unit = _units[i];
                hash.AddInt(unit.Id);
                hash.AddInt(unit.BattalionId);
                hash.AddInt(unit.OwnerId);
            }

            return hash.Digest();
        }

        private void ApplyCommands(int executeTick)
        {
            IReadOnlyList<SimCommand> commands = _commands.CommandsForTick(executeTick);
            for (int i = 0; i < commands.Count; i++)
            {
                SimCommand command = commands[i];
                switch (command.Kind)
                {
                    case CommandKind.SpawnBattalion:
                        ApplySpawn(command);
                        break;
                    case CommandKind.Move:
                        ApplyMove(command);
                        break;
                    default:
                        Emit(SimEventKind.CommandRejected, command.BattalionId, (int)CommandRejectReason.UnknownKind);
                        break;
                }
            }

            _commands.RemoveTick(executeTick);
        }

        private void ApplySpawn(SimCommand command)
        {
            if (_battalionIndexById.ContainsKey(command.BattalionId))
            {
                Emit(SimEventKind.CommandRejected, command.BattalionId, (int)CommandRejectReason.DuplicateBattalion);
                return;
            }

            if (command.Arg2 < 0)
            {
                Emit(SimEventKind.CommandRejected, command.BattalionId, (int)CommandRejectReason.NegativeSpeed);
                return;
            }

            SimBattalion battalion = new SimBattalion
            {
                Id = command.BattalionId,
                OwnerId = command.IssuerId,
                PositionXCm = command.Arg0,
                PositionYCm = command.Arg1,
                TargetXCm = 0,
                TargetYCm = 0,
                SpeedCmPerSecond = command.Arg2,
                StrengthPpt = SimConfig.PptScale,
                HasTarget = false
            };

            int insertAt = _battalions.BinarySearch(battalion, BattalionIdComparer.Instance);
            if (insertAt < 0)
            {
                insertAt = ~insertAt;
            }

            _battalions.Insert(insertAt, battalion);
            RebuildBattalionIndex();

            SimUnit unit = new SimUnit
            {
                Id = command.BattalionId,
                BattalionId = command.BattalionId,
                OwnerId = command.IssuerId
            };
            int unitAt = _units.BinarySearch(unit, UnitIdComparer.Instance);
            if (unitAt < 0)
            {
                unitAt = ~unitAt;
            }

            _units.Insert(unitAt, unit);
            RebuildUnitIndex();

            Emit(SimEventKind.BattalionSpawned, command.BattalionId, command.IssuerId);
        }

        private void ApplyMove(SimCommand command)
        {
            if (!_battalionIndexById.TryGetValue(command.BattalionId, out int index))
            {
                Emit(SimEventKind.CommandRejected, command.BattalionId, (int)CommandRejectReason.UnknownBattalion);
                return;
            }

            SimBattalion battalion = _battalions[index];
            if (battalion.OwnerId != command.IssuerId)
            {
                Emit(SimEventKind.CommandRejected, command.BattalionId, (int)CommandRejectReason.NotOwner);
                return;
            }

            battalion.TargetXCm = command.Arg0;
            battalion.TargetYCm = command.Arg1;
            battalion.HasTarget = true;
            _battalions[index] = battalion;
        }

        private void IntegrateMovement()
        {
            for (int i = 0; i < _battalions.Count; i++)
            {
                SimBattalion battalion = _battalions[i];
                if (!battalion.HasTarget)
                {
                    continue;
                }

                int dx = battalion.TargetXCm - battalion.PositionXCm;
                int dy = battalion.TargetYCm - battalion.PositionYCm;
                long distSq = ((long)dx * dx) + ((long)dy * dy);
                if (distSq == 0)
                {
                    battalion.HasTarget = false;
                    _battalions[i] = battalion;
                    Emit(SimEventKind.BattalionArrived, battalion.Id, 0);
                    continue;
                }

                Fixed64 speedPerTick = Fixed64.FromInt(battalion.SpeedCmPerSecond) * SimConfig.TickDuration;
                Fixed64Vec2 delta = new Fixed64Vec2(Fixed64.FromInt(dx), Fixed64.FromInt(dy));
                Fixed64 distance = delta.Length();
                if (distance <= speedPerTick)
                {
                    battalion.PositionXCm = battalion.TargetXCm;
                    battalion.PositionYCm = battalion.TargetYCm;
                    battalion.HasTarget = false;
                    Emit(SimEventKind.BattalionArrived, battalion.Id, 0);
                }
                else
                {
                    Fixed64Vec2 step = delta * (speedPerTick / distance);
                    battalion.PositionXCm += step.X.ToIntTowardZero();
                    battalion.PositionYCm += step.Y.ToIntTowardZero();
                }

                _battalions[i] = battalion;
            }
        }

        private void Emit(SimEventKind kind, int subjectId, int auxId)
        {
            _events.Add(new SimEvent(_nextEventId, Tick, kind, subjectId, auxId));
            _nextEventId++;
        }

        private int BattalionIndexOf(int battalionId)
        {
            if (!_battalionIndexById.TryGetValue(battalionId, out int index))
            {
                throw new InvalidOperationException("Unknown battalion " + battalionId + ".");
            }

            return index;
        }

        private int UnitIndexOf(int unitId)
        {
            if (!_unitIndexById.TryGetValue(unitId, out int index))
            {
                throw new InvalidOperationException("Unknown unit " + unitId + ".");
            }

            return index;
        }

        private void RebuildBattalionIndex()
        {
            _battalionIndexById.Clear();
            for (int i = 0; i < _battalions.Count; i++)
            {
                _battalionIndexById.Add(_battalions[i].Id, i);
            }
        }

        private void RebuildUnitIndex()
        {
            _unitIndexById.Clear();
            for (int i = 0; i < _units.Count; i++)
            {
                _unitIndexById.Add(_units[i].Id, i);
            }
        }

        private sealed class BattalionIdComparer : IComparer<SimBattalion>
        {
            public static readonly BattalionIdComparer Instance = new BattalionIdComparer();

            public int Compare(SimBattalion x, SimBattalion y)
            {
                return x.Id.CompareTo(y.Id);
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
