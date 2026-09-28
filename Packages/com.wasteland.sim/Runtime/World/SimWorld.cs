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
    /// Minimal deterministic world: spawn squads, issue Move commands, step at
    /// 20 Hz. All mutation goes through the delayed command queue. Validation
    /// is authoritative at execute time; rejections emit
    /// <see cref="SimEventKind.CommandRejected"/>.
    /// </summary>
    public sealed class SimWorld
    {
        private readonly List<SimSquad> _squads = new List<SimSquad>();
        private readonly Dictionary<int, int> _squadIndexById = new Dictionary<int, int>();
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

        public int SquadCount
        {
            get { return _squads.Count; }
        }

        public int UnitCount
        {
            get { return _units.Count; }
        }

        public SimRandom Rng
        {
            get { return _rng; }
        }

        public IReadOnlyList<SimSquad> Squads
        {
            get { return _squads; }
        }

        public IReadOnlyList<SimUnit> Units
        {
            get { return _units; }
        }

        public IReadOnlyList<SimEvent> Events
        {
            get { return _events; }
        }

        public SimSquad GetSquad(int id)
        {
            return _squads[SquadIndexOf(id)];
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
                Emit(SimEventKind.CommandRejected, command.SquadId, (int)CommandRejectReason.TooLate);
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
            hash.AddULong(_rng.Stream);
            hash.AddInt(_squads.Count);

            for (int i = 0; i < _squads.Count; i++)
            {
                SimSquad squad = _squads[i];
                hash.AddInt(squad.Id);
                hash.AddInt(squad.OwnerId);
                hash.AddInt(squad.PositionXCm);
                hash.AddInt(squad.PositionYCm);
                hash.AddInt(squad.TargetXCm);
                hash.AddInt(squad.TargetYCm);
                hash.AddInt(squad.SpeedCmPerSecond);
                hash.AddInt(squad.StrengthPpt);
                hash.AddBool(squad.HasTarget);
            }

            hash.AddInt(_units.Count);
            for (int i = 0; i < _units.Count; i++)
            {
                SimUnit unit = _units[i];
                hash.AddInt(unit.Id);
                hash.AddInt(unit.SquadId);
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
                    case CommandKind.SpawnSquad:
                        ApplySpawn(command);
                        break;
                    case CommandKind.Move:
                        ApplyMove(command);
                        break;
                    default:
                        Emit(SimEventKind.CommandRejected, command.SquadId, (int)CommandRejectReason.UnknownKind);
                        break;
                }
            }

            _commands.RemoveTick(executeTick);
        }

        private void ApplySpawn(SimCommand command)
        {
            if (_squadIndexById.ContainsKey(command.SquadId))
            {
                Emit(SimEventKind.CommandRejected, command.SquadId, (int)CommandRejectReason.DuplicateSquad);
                return;
            }

            if (command.Arg2 < 0)
            {
                Emit(SimEventKind.CommandRejected, command.SquadId, (int)CommandRejectReason.NegativeSpeed);
                return;
            }

            SimSquad squad = new SimSquad
            {
                Id = command.SquadId,
                OwnerId = command.IssuerId,
                PositionXCm = command.Arg0,
                PositionYCm = command.Arg1,
                TargetXCm = 0,
                TargetYCm = 0,
                SpeedCmPerSecond = command.Arg2,
                StrengthPpt = SimConfig.PptScale,
                HasTarget = false
            };

            int insertAt = _squads.BinarySearch(squad, SquadIdComparer.Instance);
            if (insertAt < 0)
            {
                insertAt = ~insertAt;
            }

            _squads.Insert(insertAt, squad);
            RebuildSquadIndex();

            SimUnit unit = new SimUnit
            {
                Id = command.SquadId,
                SquadId = command.SquadId,
                OwnerId = command.IssuerId
            };
            int unitAt = _units.BinarySearch(unit, UnitIdComparer.Instance);
            if (unitAt < 0)
            {
                unitAt = ~unitAt;
            }

            _units.Insert(unitAt, unit);
            RebuildUnitIndex();

            Emit(SimEventKind.SquadSpawned, command.SquadId, command.IssuerId);
        }

        private void ApplyMove(SimCommand command)
        {
            if (!_squadIndexById.TryGetValue(command.SquadId, out int index))
            {
                Emit(SimEventKind.CommandRejected, command.SquadId, (int)CommandRejectReason.UnknownSquad);
                return;
            }

            SimSquad squad = _squads[index];
            if (squad.OwnerId != command.IssuerId)
            {
                Emit(SimEventKind.CommandRejected, command.SquadId, (int)CommandRejectReason.NotOwner);
                return;
            }

            squad.TargetXCm = command.Arg0;
            squad.TargetYCm = command.Arg1;
            squad.HasTarget = true;
            _squads[index] = squad;
        }

        private void IntegrateMovement()
        {
            for (int i = 0; i < _squads.Count; i++)
            {
                SimSquad squad = _squads[i];
                if (!squad.HasTarget)
                {
                    continue;
                }

                int dx = squad.TargetXCm - squad.PositionXCm;
                int dy = squad.TargetYCm - squad.PositionYCm;
                long distSq = ((long)dx * dx) + ((long)dy * dy);
                if (distSq == 0)
                {
                    squad.HasTarget = false;
                    _squads[i] = squad;
                    Emit(SimEventKind.SquadArrived, squad.Id, 0);
                    continue;
                }

                Fixed64 speedPerTick = Fixed64.FromInt(squad.SpeedCmPerSecond) * SimConfig.TickDuration;
                Fixed64Vec2 delta = new Fixed64Vec2(Fixed64.FromInt(dx), Fixed64.FromInt(dy));
                Fixed64 distance = delta.Length();
                if (distance <= speedPerTick)
                {
                    squad.PositionXCm = squad.TargetXCm;
                    squad.PositionYCm = squad.TargetYCm;
                    squad.HasTarget = false;
                    Emit(SimEventKind.SquadArrived, squad.Id, 0);
                }
                else
                {
                    Fixed64Vec2 step = delta * (speedPerTick / distance);
                    squad.PositionXCm += step.X.ToIntTowardZero();
                    squad.PositionYCm += step.Y.ToIntTowardZero();
                }

                _squads[i] = squad;
            }
        }

        private void Emit(SimEventKind kind, int subjectId, int auxId)
        {
            _events.Add(new SimEvent(_nextEventId, Tick, kind, subjectId, auxId));
            _nextEventId++;
        }

        private int SquadIndexOf(int squadId)
        {
            if (!_squadIndexById.TryGetValue(squadId, out int index))
            {
                throw new InvalidOperationException("Unknown squad " + squadId + ".");
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

        private void RebuildSquadIndex()
        {
            _squadIndexById.Clear();
            for (int i = 0; i < _squads.Count; i++)
            {
                _squadIndexById.Add(_squads[i].Id, i);
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

        private sealed class SquadIdComparer : IComparer<SimSquad>
        {
            public static readonly SquadIdComparer Instance = new SquadIdComparer();

            public int Compare(SimSquad x, SimSquad y)
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
