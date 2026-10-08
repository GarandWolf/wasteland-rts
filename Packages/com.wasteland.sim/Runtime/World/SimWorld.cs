#nullable disable
using System;
using System.Collections.Generic;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Math;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Random;
using Wasteland.Sim.Scenario;

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
        private readonly List<SimCommand> _hashCommands = new List<SimCommand>();
        private readonly List<SimCommand> _reservedScan = new List<SimCommand>();
        private readonly List<SeatState> _seats = new List<SeatState>();
        private BalanceCatalog _catalog;
        private MovementService _movement;
        private RoadGraph _roads;
        private int _nextBattalionId = 1;
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
            if (stamped.Kind == CommandKind.SpawnBattalion || stamped.Kind == CommandKind.RaiseBattalion)
            {
                NoteBattalionId(stamped.BattalionId);
            }

            return stamped;
        }

        /// <summary>
        /// Next battalion id. Monotonic, always above every live battalion, pending
        /// production job, and queued spawn or raise. Stored in sim state.
        /// </summary>
        public int AllocateBattalionId()
        {
            int highest = HighestReservedBattalionId();
            if (_nextBattalionId <= highest)
            {
                _nextBattalionId = highest + 1;
            }

            int id = _nextBattalionId;
            _nextBattalionId++;
            return id;
        }

        /// <summary>
        /// Bind designer data used by <see cref="CommandKind.RaiseBattalion"/>.
        /// </summary>
        public void BindCatalog(BalanceCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            _catalog = catalog;
        }

        /// <summary>
        /// Bind the road A* service Move follows. Without this, Move stays a straight cm line.
        /// </summary>
        public void BindRoads(MovementService movement, RoadGraph roads)
        {
            if (movement == null)
            {
                throw new ArgumentNullException(nameof(movement));
            }

            if (roads == null)
            {
                throw new ArgumentNullException(nameof(roads));
            }

            _movement = movement;
            _roads = roads;
        }

        public void BindSeat(SeatSetup setup)
        {
            if (_catalog == null)
            {
                throw new InvalidOperationException("Bind a balance catalog before binding a seat.");
            }

            if (FindSeat(setup.FactionId) >= 0)
            {
                throw new InvalidOperationException("Seat already bound for faction " + setup.FactionId + ".");
            }

            int[] raisable = new int[setup.RaisableUnits.Length];
            for (int i = 0; i < raisable.Length; i++)
            {
                string unitId = setup.RaisableUnits[i];
                if (unitId == null || !_catalog.TryGetUnitIndex(unitId, out int index))
                {
                    throw new ArgumentException("Raisable unit is not in the catalog.", nameof(setup));
                }

                raisable[i] = index;
            }

            Array.Sort(raisable);
            SeatState seat = new SeatState();
            seat.FactionId = setup.FactionId;
            seat.Slot = setup.Slot;
            seat.HomeTownId = setup.HomeTownId;
            seat.HqXCm = setup.HqXCm;
            seat.HqYCm = setup.HqYCm;
            seat.CurrentTier = setup.CurrentTier;
            seat.TierResearchCompleteTick = setup.TierResearchCompleteTick;
            seat.AutoResearchTech = new int[setup.AutoResearch.Length];
            seat.AutoResearchNotBefore = new int[setup.AutoResearch.Length];
            seat.AutoResearchCursor = 0;
            for (int i = 0; i < setup.AutoResearch.Length; i++)
            {
                string item = setup.AutoResearch[i].Item;
                if (item == null || !_catalog.TryGetTechIndex(item, out int techIndex))
                {
                    throw new ArgumentException("Auto research item is not in the catalog.", nameof(setup));
                }

                seat.AutoResearchTech[i] = techIndex;
                seat.AutoResearchNotBefore[i] = setup.AutoResearch[i].NotBeforeTick;
            }

            seat.Manpower = setup.Manpower;
            seat.Materials = setup.Materials;
            seat.Fuel = setup.Fuel;
            seat.FuelEnabled = setup.FuelEnabled;
            seat.PopUsed = 0;
            seat.PopCap = setup.PopCap;
            seat.Raisable = raisable;
            seat.Lanes = new List<ProductionLane>();

            int insertAt = _seats.Count;
            for (int i = 0; i < _seats.Count; i++)
            {
                if (_seats[i].FactionId > seat.FactionId)
                {
                    insertAt = i;
                    break;
                }
            }

            _seats.Insert(insertAt, seat);
        }

        /// <summary>
        /// Allocate a battalion id, then queue a raise. The rally is not the spawn position.
        /// </summary>
        public SimCommand SubmitRaiseBattalion(int factionId, string unitId, int rallyXCm, int rallyYCm)
        {
            return SubmitRaiseBattalion(factionId, AllocateBattalionId(), unitId, rallyXCm, rallyYCm);
        }

        /// <summary>
        /// Queue a raise with an explicit battalion id. Replays and tests use this.
        /// The rally is not the spawn position.
        /// </summary>
        public SimCommand SubmitRaiseBattalion(int factionId, int battalionId, string unitId, int rallyXCm, int rallyYCm)
        {
            int unitIndex = -1;
            if (_catalog != null && unitId != null && _catalog.TryGetUnitIndex(unitId, out int found))
            {
                unitIndex = found;
            }

            return Submit(SimCommand.RaiseBattalion(Tick, factionId, battalionId, unitIndex, rallyXCm, rallyYCm));
        }

        internal int FuelOf(int factionId)
        {
            int index = FindSeat(factionId);
            if (index < 0)
            {
                return 0;
            }

            return _seats[index].Fuel;
        }

        public bool TryGetSeat(int factionId, out SeatView view)
        {
            int index = FindSeat(factionId);
            if (index < 0)
            {
                view = default(SeatView);
                return false;
            }

            SeatState seat = _seats[index];
            view = new SeatView(
                seat.FactionId,
                seat.Slot,
                seat.HomeTownId,
                seat.HqXCm,
                seat.HqYCm,
                seat.CurrentTier,
                seat.TierResearchCompleteTick,
                seat.Manpower,
                seat.Materials,
                seat.PopUsed,
                seat.PopCap,
                VisibleStrength(factionId));
            return true;
        }

        /// <summary>
        /// Pending raises for the seat, ordered by producedAt building then FIFO.
        /// Each item is the balance unit id, the tick production starts, and the tick it spawns.
        /// </summary>
        public bool TryCopyProductionQueue(int factionId, List<ProductionQueueItem> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            int index = FindSeat(factionId);
            if (index < 0)
            {
                return false;
            }

            SeatState seat = _seats[index];
            if (seat.Lanes == null || _catalog == null)
            {
                return true;
            }

            for (int lane = 0; lane < seat.Lanes.Count; lane++)
            {
                List<ProductionItem> items = seat.Lanes[lane].Items;
                for (int i = 0; i < items.Count; i++)
                {
                    ProductionItem item = items[i];
                    into.Add(new ProductionQueueItem(
                        _catalog.UnitIdAt(item.UnitIndex),
                        item.StartTick,
                        item.CompleteTick));
                }
            }

            return true;
        }

        public bool TryGetBattalionView(int battalionId, out BattalionView view)
        {
            if (!_battalionIndexById.TryGetValue(battalionId, out int index))
            {
                view = default(BattalionView);
                return false;
            }

            SimBattalion battalion = _battalions[index];
            string unitId = string.Empty;
            if (_catalog != null && battalion.UnitIndex >= 0)
            {
                unitId = _catalog.UnitIdAt(battalion.UnitIndex);
            }

            view = new BattalionView(
                battalion.Id,
                battalion.OwnerId,
                battalion.PositionXCm,
                battalion.PositionYCm,
                unitId,
                battalion.StrengthPpt);
            return true;
        }

        /// <summary>
        /// Latest rejection whose subject is <paramref name="subjectId"/>.
        /// </summary>
        public bool TryGetRejection(int subjectId, out CommandRejectReason code, out int param0, out int param1)
        {
            for (int i = _events.Count - 1; i >= 0; i--)
            {
                SimEvent ev = _events[i];
                if (ev.Kind == SimEventKind.CommandRejected && ev.SubjectId == subjectId)
                {
                    code = ev.RejectReason;
                    param0 = ev.Param0;
                    param1 = ev.Param1;
                    return true;
                }
            }

            code = CommandRejectReason.None;
            param0 = 0;
            param1 = 0;
            return false;
        }

        /// <summary>
        /// Apply commands whose execute tick is <see cref="Tick"/>, after seat research.
        /// Ordering is tick, then faction id, then submit sequence.
        /// </summary>
        public void ProcessQueue()
        {
            AdvanceResearch();
            ApplyCommands(Tick);
            CompleteProduction();
        }

        public ulong Step()
        {
            ProcessQueue();
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
            hash.AddInt(_nextBattalionId);
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
                hash.AddInt(battalion.UnitIndex);
                hash.AddByte((byte)battalion.MoveClass);
                hash.AddInt(battalion.PathIndex);
                hash.AddInt(battalion.PathCount);
                if (battalion.PathCount > 0 && battalion.PathXCm != null && battalion.PathYCm != null)
                {
                    int n = battalion.PathCount;
                    if (n > battalion.PathXCm.Length)
                    {
                        n = battalion.PathXCm.Length;
                    }

                    if (n > battalion.PathYCm.Length)
                    {
                        n = battalion.PathYCm.Length;
                    }

                    for (int p = 0; p < n; p++)
                    {
                        hash.AddInt(battalion.PathXCm[p]);
                        hash.AddInt(battalion.PathYCm[p]);
                    }
                }
            }

            hash.AddInt(_seats.Count);
            for (int i = 0; i < _seats.Count; i++)
            {
                SeatState seat = _seats[i];
                hash.AddInt(seat.FactionId);
                hash.AddInt(seat.Slot);
                hash.AddInt(seat.HomeTownId);
                hash.AddInt(seat.HqXCm);
                hash.AddInt(seat.HqYCm);
                hash.AddInt(seat.CurrentTier);
                hash.AddInt(seat.TierResearchCompleteTick);
                hash.AddInt(seat.AutoResearchCursor);
                int researchCount = seat.AutoResearchTech == null ? 0 : seat.AutoResearchTech.Length;
                hash.AddInt(researchCount);
                for (int r = 0; r < researchCount; r++)
                {
                    hash.AddInt(seat.AutoResearchTech[r]);
                    hash.AddInt(seat.AutoResearchNotBefore[r]);
                }

                hash.AddInt(seat.Manpower);
                hash.AddInt(seat.Materials);
                hash.AddInt(seat.Fuel);
                hash.AddBool(seat.FuelEnabled);
                hash.AddInt(seat.PopUsed);
                hash.AddInt(seat.PopCap);
                int raisableCount = seat.Raisable == null ? 0 : seat.Raisable.Length;
                hash.AddInt(raisableCount);
                for (int r = 0; r < raisableCount; r++)
                {
                    hash.AddInt(seat.Raisable[r]);
                }

                int laneCount = seat.Lanes == null ? 0 : seat.Lanes.Count;
                hash.AddInt(laneCount);
                for (int lane = 0; lane < laneCount; lane++)
                {
                    ProductionLane production = seat.Lanes[lane];
                    hash.AddInt(production.BuildingIndex);
                    hash.AddInt(production.Items.Count);
                    for (int itemIndex = 0; itemIndex < production.Items.Count; itemIndex++)
                    {
                        ProductionItem item = production.Items[itemIndex];
                        hash.AddInt(item.UnitIndex);
                        hash.AddInt(item.BattalionId);
                        hash.AddInt(item.StartTick);
                        hash.AddInt(item.CompleteTick);
                        hash.AddInt(item.RallyXCm);
                        hash.AddInt(item.RallyYCm);
                    }
                }
            }

            _commands.CopyTo(_hashCommands);
            hash.AddInt(_hashCommands.Count);
            for (int i = 0; i < _hashCommands.Count; i++)
            {
                SimCommand command = _hashCommands[i];
                hash.AddInt(command.IssuedTick);
                hash.AddInt(command.ExecuteTick);
                hash.AddInt(command.IssuerId);
                hash.AddInt(command.Sequence);
                hash.AddInt(command.BattalionId);
                hash.AddByte((byte)command.Kind);
                hash.AddInt(command.Arg0);
                hash.AddInt(command.Arg1);
                hash.AddInt(command.Arg2);
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
                    case CommandKind.RaiseBattalion:
                        ApplyRaise(command);
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
                HasTarget = false,
                UnitIndex = -1,
                MoveClass = MovementClass.Wheeled,
                PathIndex = 0,
                PathCount = 0,
                PathXCm = null,
                PathYCm = null
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

            if (_movement != null && _roads != null)
            {
                ApplyRoadMove(ref battalion, command.Arg0, command.Arg1);
            }
            else
            {
                ClearPath(ref battalion);
                battalion.TargetXCm = command.Arg0;
                battalion.TargetYCm = command.Arg1;
                battalion.HasTarget = true;
            }

            _battalions[index] = battalion;
        }

        private void ApplyRaise(SimCommand command)
        {
            int seatIndex = FindSeat(command.IssuerId);
            if (seatIndex < 0)
            {
                Reject(command, CommandRejectReason.UnknownSeat, 0, 0);
                return;
            }

            if (IsBattalionIdTaken(command.BattalionId))
            {
                Reject(command, CommandRejectReason.DuplicateBattalion, 0, 0);
                return;
            }

            SeatState seat = _seats[seatIndex];
            int unitIndex = command.Arg0;
            if (_catalog == null || unitIndex < 0 || unitIndex >= _catalog.UnitCount)
            {
                Reject(command, CommandRejectReason.UnitNotRaisable, unitIndex, 0);
                return;
            }

            int requiredTier = _catalog.UnitTierAt(unitIndex);
            if (requiredTier > seat.CurrentTier)
            {
                Reject(command, CommandRejectReason.TierLocked, requiredTier, seat.CurrentTier);
                return;
            }

            if (!ContainsSorted(seat.Raisable, unitIndex))
            {
                Reject(command, CommandRejectReason.UnitNotRaisable, unitIndex, 0);
                return;
            }

            int popCost = _catalog.UnitPopAt(unitIndex);
            if (seat.PopUsed + popCost > seat.PopCap)
            {
                Reject(command, CommandRejectReason.PopCapReached, seat.PopCap, seat.PopUsed);
                return;
            }

            int manpowerCost = _catalog.UnitManpowerAt(unitIndex);
            if (seat.Manpower < manpowerCost)
            {
                Reject(command, CommandRejectReason.InsufficientManpower, manpowerCost, seat.Manpower);
                return;
            }

            int materielCost = _catalog.UnitMaterielAt(unitIndex);
            if (seat.Materials < materielCost)
            {
                Reject(command, CommandRejectReason.InsufficientMaterials, materielCost, seat.Materials);
                return;
            }

            int fuelCost = _catalog.UnitFuelAt(unitIndex);
            if (seat.FuelEnabled && seat.Fuel < fuelCost)
            {
                Reject(command, CommandRejectReason.InsufficientFuel, fuelCost, seat.Fuel);
                return;
            }

            seat.Manpower -= manpowerCost;
            seat.Materials -= materielCost;
            if (seat.FuelEnabled)
            {
                seat.Fuel -= fuelCost;
            }

            seat.PopUsed += popCost;
            _seats[seatIndex] = seat;

            int buildTicks = _catalog.UnitBuildTicksAt(unitIndex);
            int buildingIndex = _catalog.UnitProducedAtIndex(unitIndex);
            ProductionLane lane = LaneFor(seat, buildingIndex);
            int start = Tick;
            if (lane.Items.Count > 0)
            {
                int previousComplete = lane.Items[lane.Items.Count - 1].CompleteTick;
                if (previousComplete > start)
                {
                    start = previousComplete;
                }
            }

            int complete = checked(start + buildTicks);
            ProductionItem job = new ProductionItem();
            job.UnitIndex = unitIndex;
            job.BattalionId = command.BattalionId;
            job.StartTick = start;
            job.CompleteTick = complete;
            job.RallyXCm = command.Arg1;
            job.RallyYCm = command.Arg2;
            lane.Items.Add(job);
            Emit(SimEventKind.BattalionRaiseStarted, command.BattalionId, command.IssuerId, start, complete);
        }

        private void CompleteProduction()
        {
            for (int s = 0; s < _seats.Count; s++)
            {
                SeatState seat = _seats[s];
                if (seat.Lanes == null)
                {
                    continue;
                }

                for (int laneIndex = 0; laneIndex < seat.Lanes.Count; laneIndex++)
                {
                    ProductionLane lane = seat.Lanes[laneIndex];
                    while (lane.Items.Count > 0 && lane.Items[0].CompleteTick <= Tick)
                    {
                        ProductionItem item = lane.Items[0];
                        lane.Items.RemoveAt(0);
                        SpawnRaised(seat, item);
                    }
                }
            }
        }

        private void SpawnRaised(SeatState seat, ProductionItem item)
        {
            if (_catalog == null || IsBattalionIdTaken(item.BattalionId))
            {
                return;
            }

            MovementClass moveClass = _catalog.UnitMoveClassAt(item.UnitIndex);
            SimBattalion battalion = new SimBattalion
            {
                Id = item.BattalionId,
                OwnerId = seat.FactionId,
                PositionXCm = seat.HqXCm,
                PositionYCm = seat.HqYCm,
                TargetXCm = 0,
                TargetYCm = 0,
                SpeedCmPerSecond = _catalog.SpeedCmPerSecond(moveClass),
                StrengthPpt = SimConfig.PptScale,
                HasTarget = false,
                UnitIndex = item.UnitIndex,
                MoveClass = moveClass,
                PathIndex = 0,
                PathCount = 0,
                PathXCm = null,
                PathYCm = null
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
                Id = item.BattalionId,
                BattalionId = item.BattalionId,
                OwnerId = seat.FactionId
            };
            int unitAt = _units.BinarySearch(unit, UnitIdComparer.Instance);
            if (unitAt < 0)
            {
                unitAt = ~unitAt;
            }

            _units.Insert(unitAt, unit);
            RebuildUnitIndex();
            Emit(SimEventKind.BattalionSpawned, item.BattalionId, seat.FactionId);

            if (item.RallyXCm != seat.HqXCm || item.RallyYCm != seat.HqYCm)
            {
                ApplyMove(SimCommand.Move(Tick, seat.FactionId, item.BattalionId, item.RallyXCm, item.RallyYCm));
            }
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
                    if (!AdvanceWaypoint(ref battalion))
                    {
                        battalion.HasTarget = false;
                        _battalions[i] = battalion;
                        Emit(SimEventKind.BattalionArrived, battalion.Id, 0);
                        continue;
                    }

                    _battalions[i] = battalion;
                    continue;
                }

                Fixed64 speedPerTick = Fixed64.FromInt(battalion.SpeedCmPerSecond) * SimConfig.TickDuration;
                Fixed64Vec2 delta = new Fixed64Vec2(Fixed64.FromInt(dx), Fixed64.FromInt(dy));
                Fixed64 distance = delta.Length();
                if (distance <= speedPerTick)
                {
                    battalion.PositionXCm = battalion.TargetXCm;
                    battalion.PositionYCm = battalion.TargetYCm;
                    if (!AdvanceWaypoint(ref battalion))
                    {
                        battalion.HasTarget = false;
                        Emit(SimEventKind.BattalionArrived, battalion.Id, 0);
                    }
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

        private static bool AdvanceWaypoint(ref SimBattalion battalion)
        {
            if (battalion.PathCount <= 0 || battalion.PathXCm == null || battalion.PathYCm == null)
            {
                return false;
            }

            int next = battalion.PathIndex + 1;
            if (next >= battalion.PathCount || next >= battalion.PathXCm.Length || next >= battalion.PathYCm.Length)
            {
                return false;
            }

            battalion.PathIndex = next;
            battalion.TargetXCm = battalion.PathXCm[next];
            battalion.TargetYCm = battalion.PathYCm[next];
            battalion.HasTarget = true;
            return true;
        }

        private void ApplyRoadMove(ref SimBattalion battalion, int xCm, int yCm)
        {
            if (_roads.NodeCount < 1)
            {
                Emit(SimEventKind.CommandRejected, battalion.Id, (int)CommandRejectReason.NoPath, 0, 0);
                return;
            }

            int fromNode = NearestNodeId(battalion.PositionXCm, battalion.PositionYCm);
            int toNode = NearestNodeId(xCm, yCm);
            PathResult path = _movement.PathOnRoads(fromNode, toNode, battalion.MoveClass, RoadCostWeights.Uniform());
            if (!path.Reachable || path.Waypoints == null || path.Waypoints.Length == 0)
            {
                Emit(SimEventKind.CommandRejected, battalion.Id, (int)CommandRejectReason.NoPath, fromNode, toNode);
                return;
            }

            int start = 0;
            while (start < path.Waypoints.Length
                && path.Waypoints[start].XCm == battalion.PositionXCm
                && path.Waypoints[start].YCm == battalion.PositionYCm)
            {
                start++;
            }

            int last = path.Waypoints.Length - 1;
            int lastX = path.Waypoints[last].XCm;
            int lastY = path.Waypoints[last].YCm;
            int extra = (lastX != xCm || lastY != yCm) ? 1 : 0;
            int count = (path.Waypoints.Length - start) + extra;
            if (count <= 0)
            {
                ClearPath(ref battalion);
                battalion.TargetXCm = xCm;
                battalion.TargetYCm = yCm;
                battalion.HasTarget = false;
                return;
            }

            int[] xs = new int[count];
            int[] ys = new int[count];
            int w = 0;
            for (int i = start; i < path.Waypoints.Length; i++)
            {
                xs[w] = path.Waypoints[i].XCm;
                ys[w] = path.Waypoints[i].YCm;
                w++;
            }

            if (extra == 1)
            {
                xs[w] = xCm;
                ys[w] = yCm;
            }

            battalion.PathXCm = xs;
            battalion.PathYCm = ys;
            battalion.PathCount = count;
            battalion.PathIndex = 0;
            battalion.TargetXCm = xs[0];
            battalion.TargetYCm = ys[0];
            battalion.HasTarget = true;
        }

        private static void ClearPath(ref SimBattalion battalion)
        {
            battalion.PathIndex = 0;
            battalion.PathCount = 0;
            battalion.PathXCm = null;
            battalion.PathYCm = null;
        }

        // TODO: replace this linear scan with a spatial index when maps get large.
        private int NearestNodeId(int xCm, int yCm)
        {
            int bestIndex = 0;
            int bestId = _roads.GetNodeId(0);
            long bestDist = long.MaxValue;
            for (int i = 0; i < _roads.NodeCount; i++)
            {
                long dx = (long)_roads.GetNodeXCm(i) - xCm;
                long dy = (long)_roads.GetNodeYCm(i) - yCm;
                long dist = (dx * dx) + (dy * dy);
                int id = _roads.GetNodeId(i);
                if (dist < bestDist || (dist == bestDist && id < bestId))
                {
                    bestDist = dist;
                    bestId = id;
                    bestIndex = i;
                }
            }

            return _roads.GetNodeId(bestIndex);
        }

        private void AdvanceResearch()
        {
            for (int i = 0; i < _seats.Count; i++)
            {
                SeatState seat = _seats[i];
                if (seat.TierResearchCompleteTick >= 0 && Tick >= seat.TierResearchCompleteTick)
                {
                    seat.CurrentTier++;
                    seat.TierResearchCompleteTick = -1;
                    _seats[i] = seat;
                    Emit(SimEventKind.TierReached, seat.FactionId, seat.CurrentTier);
                }

                if (seat.TierResearchCompleteTick < 0 && TryStartAutoResearch(ref seat))
                {
                    _seats[i] = seat;
                }
            }
        }

        /// <summary>
        /// Start the next authored auto-research item when nothing is running,
        /// its not-before tick has arrived, and it targets CurrentTier + 1.
        /// Cost is charged on this tick the same way a raise is: manpower and
        /// materiel always, fuel only when the seat has fuel enabled.
        /// A short resource charges nothing and leaves the cursor in place
        /// so the next tick can try again.
        /// </summary>
        private bool TryStartAutoResearch(ref SeatState seat)
        {
            if (_catalog == null || seat.AutoResearchTech == null || seat.AutoResearchNotBefore == null)
            {
                return false;
            }

            int cursor = seat.AutoResearchCursor;
            if (cursor < 0 || cursor >= seat.AutoResearchTech.Length || cursor >= seat.AutoResearchNotBefore.Length)
            {
                return false;
            }

            if (seat.AutoResearchNotBefore[cursor] > Tick)
            {
                return false;
            }

            int techIndex = seat.AutoResearchTech[cursor];
            int targetTier = _catalog.TechTargetTierAt(techIndex);
            if (targetTier != seat.CurrentTier + 1)
            {
                return false;
            }

            int manpowerCost = _catalog.TechManpowerAt(techIndex);
            int materielCost = _catalog.TechMaterielAt(techIndex);
            int fuelCost = _catalog.TechFuelAt(techIndex);
            if (seat.Manpower < manpowerCost || seat.Materials < materielCost)
            {
                return false;
            }

            if (seat.FuelEnabled && seat.Fuel < fuelCost)
            {
                return false;
            }

            int complete = checked(Tick + _catalog.TechResearchTicksAt(techIndex));
            seat.Manpower -= manpowerCost;
            seat.Materials -= materielCost;
            if (seat.FuelEnabled)
            {
                seat.Fuel -= fuelCost;
            }

            seat.TierResearchCompleteTick = complete;
            seat.AutoResearchCursor = cursor + 1;
            Emit(SimEventKind.TierResearchStarted, seat.FactionId, targetTier, Tick, complete);
            return true;
        }

        internal void AddResources(int factionId, int manpower, int materials, int fuel)
        {
            if (manpower < 0 || materials < 0 || fuel < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(manpower), "Resource credit must be >= 0.");
            }

            int index = FindSeat(factionId);
            if (index < 0)
            {
                throw new InvalidOperationException("Unknown seat " + factionId + ".");
            }

            SeatState seat = _seats[index];
            seat.Manpower = checked(seat.Manpower + manpower);
            seat.Materials = checked(seat.Materials + materials);
            seat.Fuel = checked(seat.Fuel + fuel);
            _seats[index] = seat;
        }

        private void NoteBattalionId(int id)
        {
            if (id >= _nextBattalionId)
            {
                _nextBattalionId = checked(id + 1);
            }
        }

        private int HighestReservedBattalionId()
        {
            int highest = 0;
            for (int i = 0; i < _battalions.Count; i++)
            {
                if (_battalions[i].Id > highest)
                {
                    highest = _battalions[i].Id;
                }
            }

            for (int s = 0; s < _seats.Count; s++)
            {
                List<ProductionLane> lanes = _seats[s].Lanes;
                if (lanes == null)
                {
                    continue;
                }

                for (int lane = 0; lane < lanes.Count; lane++)
                {
                    List<ProductionItem> items = lanes[lane].Items;
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].BattalionId > highest)
                        {
                            highest = items[i].BattalionId;
                        }
                    }
                }
            }

            _commands.CopyTo(_reservedScan);
            for (int i = 0; i < _reservedScan.Count; i++)
            {
                SimCommand command = _reservedScan[i];
                if (command.Kind != CommandKind.SpawnBattalion && command.Kind != CommandKind.RaiseBattalion)
                {
                    continue;
                }

                if (command.BattalionId > highest)
                {
                    highest = command.BattalionId;
                }
            }

            return highest;
        }

        private bool IsBattalionIdTaken(int id)
        {
            if (_battalionIndexById.ContainsKey(id))
            {
                return true;
            }

            for (int s = 0; s < _seats.Count; s++)
            {
                List<ProductionLane> lanes = _seats[s].Lanes;
                if (lanes == null)
                {
                    continue;
                }

                for (int lane = 0; lane < lanes.Count; lane++)
                {
                    List<ProductionItem> items = lanes[lane].Items;
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i].BattalionId == id)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static ProductionLane LaneFor(SeatState seat, int buildingIndex)
        {
            List<ProductionLane> lanes = seat.Lanes;
            for (int i = 0; i < lanes.Count; i++)
            {
                if (lanes[i].BuildingIndex == buildingIndex)
                {
                    return lanes[i];
                }

                if (lanes[i].BuildingIndex > buildingIndex)
                {
                    ProductionLane lane = new ProductionLane();
                    lane.BuildingIndex = buildingIndex;
                    lanes.Insert(i, lane);
                    return lane;
                }
            }

            ProductionLane created = new ProductionLane();
            created.BuildingIndex = buildingIndex;
            lanes.Add(created);
            return created;
        }

        private int FindSeat(int factionId)
        {
            for (int i = 0; i < _seats.Count; i++)
            {
                if (_seats[i].FactionId == factionId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int VisibleStrength(int factionId)
        {
            int strength = 0;
            for (int i = 0; i < _battalions.Count; i++)
            {
                if (_battalions[i].OwnerId == factionId)
                {
                    strength += _battalions[i].StrengthPpt;
                }
            }

            return strength;
        }

        private static bool ContainsSorted(int[] values, int value)
        {
            if (values == null)
            {
                return false;
            }

            int lo = 0;
            int hi = values.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int have = values[mid];
                if (have == value)
                {
                    return true;
                }

                if (have < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return false;
        }

        private void Reject(SimCommand command, CommandRejectReason reason, int param0, int param1)
        {
            Emit(SimEventKind.CommandRejected, command.BattalionId, (int)reason, param0, param1);
        }

        private void Emit(SimEventKind kind, int subjectId, int auxId, int param0 = 0, int param1 = 0)
        {
            _events.Add(new SimEvent(_nextEventId, Tick, kind, subjectId, auxId, param0, param1));
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

        private struct SeatState
        {
            public int FactionId;
            public int Slot;
            public int HomeTownId;
            public int HqXCm;
            public int HqYCm;
            public int CurrentTier;
            public int TierResearchCompleteTick;
            public int AutoResearchCursor;
            public int[] AutoResearchTech;
            public int[] AutoResearchNotBefore;
            public int Manpower;
            public int Materials;
            public int Fuel;
            public bool FuelEnabled;
            public int PopUsed;
            public int PopCap;
            public int[] Raisable;
            public List<ProductionLane> Lanes;
        }

        private struct ProductionItem
        {
            public int UnitIndex;
            public int BattalionId;
            public int StartTick;
            public int CompleteTick;
            public int RallyXCm;
            public int RallyYCm;
        }

        private sealed class ProductionLane
        {
            public int BuildingIndex;
            public readonly List<ProductionItem> Items = new List<ProductionItem>();
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
