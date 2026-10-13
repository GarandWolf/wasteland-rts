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
    public sealed partial class SimWorld
    {
        private readonly List<SimBattalion> _battalions = new List<SimBattalion>();
        private readonly Dictionary<int, int> _battalionIndexById = new Dictionary<int, int>();
        private readonly List<SimUnit> _units = new List<SimUnit>();
        private readonly Dictionary<int, int> _unitIndexById = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _nextSequenceByIssuer = new Dictionary<int, int>();
        private readonly CommandQueue _commands = new CommandQueue();
        private readonly List<SimEvent> _events = new List<SimEvent>();
        private readonly List<SimEvent> _eventHistory = new List<SimEvent>();
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

        /// <summary>How many recent ticks <see cref="CopyEventsInRange"/> retains. Not hashed and not saved.</summary>
        internal const int EventHistoryTicks = 64;

        /// <summary>Test seam. The next road move rejects with <see cref="CommandRejectReason.NoPath"/>.</summary>
        internal bool ForceNextRoadNoPath;

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
            if (!_nextSequenceByIssuer.TryGetValue(command.IssuerId, out int sequence))
            {
                sequence = 0;
            }

            _nextSequenceByIssuer[command.IssuerId] = sequence + 1;
            SimCommand stamped = command.WithSchedule(executeTick, sequence);
            if (executeTick < Tick)
            {
                Reject(stamped, CommandRejectReason.TooLate, 0, 0);
                return stamped;
            }

            if (_matchOver)
            {
                return stamped;
            }

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
            seat.ResearchStartTick = -1;
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

            seat.EconomyFactionId = setup.FactionKey;
            seat.Manpower = setup.Manpower;
            seat.Materials = setup.Materials;
            seat.Fuel = setup.Fuel;
            seat.ManpowerCarry = 0;
            seat.MaterielCarry = 0;
            seat.FuelCarry = 0;
            seat.FuelEnabled = setup.FuelEnabled;
            seat.PopCap = setup.PopCap;
            seat.Raisable = raisable;
            seat.Lanes = new List<ProductionLane>();
            seat.PopUsed = SpawnStartingUnits(seat, setup.StartingUnits);

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

        internal bool FuelEnabledOf(int factionId)
        {
            int index = FindSeat(factionId);
            return index >= 0 && _seats[index].FuelEnabled;
        }

        internal void CopyIncomeCarry(int factionId, out int manpower, out int materiel, out int fuel)
        {
            int index = FindSeat(factionId);
            if (index < 0)
            {
                manpower = 0;
                materiel = 0;
                fuel = 0;
                return;
            }

            SeatState seat = _seats[index];
            manpower = seat.ManpowerCarry;
            materiel = seat.MaterielCarry;
            fuel = seat.FuelCarry;
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
                battalion.StrengthPpt,
                battalion.Hp,
                battalion.MaxHp,
                battalion.AttackTargetId);
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
            if (_matchOver)
            {
                _commands.Clear();
                return HashState();
            }

            AccrueSeatIncome();
            ProcessQueue();
            IntegrateMovement();
            StartDepotBuilds();
            SupplyUpdate();
            ResolveCombat();
            ResolveCapture();
            DestroyFlippedDepots();
            if (_ownersChanged)
            {
                RecomputePopCaps();
                _ownersChanged = false;
            }

            AdvanceDepotConstruction();
            HqCheck();
            Tick++;
            PruneEventHistory();
            return HashState();
        }

        public MatchResultView MatchResult
        {
            get { return new MatchResultView(_matchOver, _matchWinner, _matchReason, _matchLoser, _matchEndTick); }
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
                hash.AddInt(battalion.Hp);
                hash.AddInt(battalion.MaxHp);
                hash.AddInt(battalion.StrengthPpt);
                hash.AddInt(battalion.Cooldown);
                hash.AddInt(battalion.AttackTargetId);
                hash.AddInt(battalion.CutDetectedTick);
                hash.AddBool(battalion.Penalized);
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
                hash.AddInt(seat.ResearchStartTick);
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
                hash.AddInt(seat.ManpowerCarry);
                hash.AddInt(seat.MaterielCarry);
                hash.AddInt(seat.FuelCarry);
                hash.AddBool(seat.FuelEnabled);
                hash.AddInt(seat.PopUsed);
                hash.AddInt(seat.PopCap);
                hash.AddBool(seat.SupplyDepotAllowed);
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

            HashCaptureState(hash);
            HashMatch(hash);
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
                    case CommandKind.ClaimTown:
                        ApplyClaimTown(command);
                        break;
                    case CommandKind.BuildDepot:
                        ApplyBuildDepot(command);
                        break;
                    case CommandKind.Attack:
                        ApplyAttack(command);
                        break;
                    case CommandKind.ResearchTech:
                        ApplyResearchTech(command);
                        break;
                    default:
                        Reject(command, CommandRejectReason.UnknownKind, 0, 0);
                        break;
                }
            }

            _commands.RemoveTick(executeTick);
        }

        private void ApplySpawn(SimCommand command)
        {
            if (_battalionIndexById.ContainsKey(command.BattalionId))
            {
                Reject(command, CommandRejectReason.DuplicateBattalion, 0, 0);
                return;
            }

            if (command.Arg2 < 0)
            {
                Reject(command, CommandRejectReason.NegativeSpeed, 0, 0);
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
                HasTarget = false,
                UnitIndex = -1,
                MoveClass = MovementClass.Wheeled,
                PathIndex = 0,
                PathCount = 0,
                PathXCm = null,
                PathYCm = null
            };
            SyncSpawnVitals(ref battalion, 1000);

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
                Reject(command, CommandRejectReason.UnknownBattalion, 0, 0);
                return;
            }

            SimBattalion battalion = _battalions[index];
            if (battalion.OwnerId != command.IssuerId)
            {
                Reject(command, CommandRejectReason.NotOwner, 0, 0);
                return;
            }

            battalion.AttackTargetId = 0;
            DropReservedDepotForReorder(command);

            if (_movement != null && _roads != null)
            {
                ApplyRoadMove(ref battalion, command.Arg0, command.Arg1, command);
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

        /// <summary>
        /// One full-strength battalion per authored count, at the seat HQ.
        /// Catalog identity supplies unit index, move class, and speed.
        /// Pop is counted. Raise cost is not charged.
        /// </summary>
        private int SpawnStartingUnits(SeatState seat, ScenarioUnitCount[] units)
        {
            if (units == null)
            {
                throw new ArgumentNullException("setup");
            }

            int pop = 0;
            for (int i = 0; i < units.Length; i++)
            {
                string unitId = units[i].UnitId;
                int count = units[i].Count;
                if (unitId == null || !_catalog.TryGetUnitIndex(unitId, out int unitIndex))
                {
                    throw new ArgumentException("Starting unit is not in the catalog.", "setup");
                }

                if (count < 1)
                {
                    throw new ArgumentOutOfRangeException("setup", "Starting unit count must be >= 1.");
                }

                int popEach = _catalog.UnitPopAt(unitIndex);
                for (int n = 0; n < count; n++)
                {
                    InsertCatalogBattalion(
                        AllocateBattalionId(),
                        seat.FactionId,
                        seat.HqXCm,
                        seat.HqYCm,
                        unitIndex);
                    pop = checked(pop + popEach);
                }
            }

            return pop;
        }

        private void SpawnRaised(SeatState seat, ProductionItem item)
        {
            if (_catalog == null || IsBattalionIdTaken(item.BattalionId))
            {
                return;
            }

            InsertCatalogBattalion(item.BattalionId, seat.FactionId, seat.HqXCm, seat.HqYCm, item.UnitIndex);

            if (item.RallyXCm != seat.HqXCm || item.RallyYCm != seat.HqYCm)
            {
                ApplyMove(SimCommand.Move(Tick, seat.FactionId, item.BattalionId, item.RallyXCm, item.RallyYCm));
            }
        }

        private void InsertCatalogBattalion(int battalionId, int ownerId, int xCm, int yCm, int unitIndex)
        {
            MovementClass moveClass = _catalog.UnitMoveClassAt(unitIndex);
            SimBattalion battalion = new SimBattalion
            {
                Id = battalionId,
                OwnerId = ownerId,
                PositionXCm = xCm,
                PositionYCm = yCm,
                TargetXCm = 0,
                TargetYCm = 0,
                SpeedCmPerSecond = _catalog.SpeedCmPerSecond(moveClass),
                HasTarget = false,
                UnitIndex = unitIndex,
                MoveClass = moveClass,
                PathIndex = 0,
                PathCount = 0,
                PathXCm = null,
                PathYCm = null
            };
            int maxHp = unitIndex >= 0 ? _catalog.UnitMaxHpAt(unitIndex) : 1000;
            SyncSpawnVitals(ref battalion, maxHp);

            int insertAt = _battalions.BinarySearch(battalion, BattalionIdComparer.Instance);
            if (insertAt < 0)
            {
                insertAt = ~insertAt;
            }

            _battalions.Insert(insertAt, battalion);
            RebuildBattalionIndex();

            SimUnit unit = new SimUnit
            {
                Id = battalionId,
                BattalionId = battalionId,
                OwnerId = ownerId
            };
            int unitAt = _units.BinarySearch(unit, UnitIdComparer.Instance);
            if (unitAt < 0)
            {
                unitAt = ~unitAt;
            }

            _units.Insert(unitAt, unit);
            RebuildUnitIndex();
            Emit(SimEventKind.BattalionSpawned, battalionId, ownerId);
        }

        private void IntegrateMovement()
        {
            for (int i = 0; i < _battalions.Count; i++)
            {
                SimBattalion battalion = _battalions[i];
                PrepareAttackMove(ref battalion);
                if (!battalion.HasTarget)
                {
                    _battalions[i] = battalion;
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

                int speedCmPerSecond = battalion.SpeedCmPerSecond;
                if (battalion.Penalized && _catalog != null)
                {
                    speedCmPerSecond = (int)((long)speedCmPerSecond * _catalog.Combat.SpeedPpt / 1000);
                }

                Fixed64 speedPerTick = Fixed64.FromInt(speedCmPerSecond) * SimConfig.TickDuration;
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

                PrepareAttackMove(ref battalion);
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

        private bool ApplyRoadMove(ref SimBattalion battalion, int xCm, int yCm, SimCommand command)
        {
            if (ForceNextRoadNoPath)
            {
                ForceNextRoadNoPath = false;
                Reject(command, CommandRejectReason.NoPath, 0, 0);
                return false;
            }

            if (_roads.NodeCount < 1)
            {
                Reject(command, CommandRejectReason.NoPath, 0, 0);
                return false;
            }

            int fromNode = NearestNodeId(battalion.PositionXCm, battalion.PositionYCm);
            int toNode = NearestNodeId(xCm, yCm);
            PathResult path = _movement.PathOnRoads(fromNode, toNode, battalion.MoveClass, RoadCostWeights.Uniform());
            if (!path.Reachable || path.Waypoints == null || path.Waypoints.Length == 0)
            {
                Reject(command, CommandRejectReason.NoPath, fromNode, toNode);
                return false;
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
                return true;
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
            return true;
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
                    seat.ResearchStartTick = -1;
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

            if (ResearchShortfall(seat, techIndex, out _, out _, out _) != CommandRejectReason.None)
            {
                return false;
            }

            seat.AutoResearchCursor = cursor + 1;
            StartTierResearch(ref seat, techIndex);
            return true;
        }

        /// <summary>
        /// First missing resource for <paramref name="techIndex"/>, in reject order
        /// materiel, fuel (only when fuel is enabled), manpower. None when affordable.
        /// </summary>
        private CommandRejectReason ResearchShortfall(SeatState seat, int techIndex, out int need, out int have, out int unused)
        {
            unused = 0;
            int materielCost = _catalog.TechMaterielAt(techIndex);
            if (seat.Materials < materielCost)
            {
                need = materielCost;
                have = seat.Materials;
                return CommandRejectReason.InsufficientMaterials;
            }

            int fuelCost = _catalog.TechFuelAt(techIndex);
            if (seat.FuelEnabled && seat.Fuel < fuelCost)
            {
                need = fuelCost;
                have = seat.Fuel;
                return CommandRejectReason.InsufficientFuel;
            }

            int manpowerCost = _catalog.TechManpowerAt(techIndex);
            if (seat.Manpower < manpowerCost)
            {
                need = manpowerCost;
                have = seat.Manpower;
                return CommandRejectReason.InsufficientManpower;
            }

            need = 0;
            have = 0;
            return CommandRejectReason.None;
        }

        /// <summary>
        /// Shared by ResearchTech and auto-research. The caller has checked the
        /// target tier and affordability. Charges manpower and materiel always,
        /// fuel only when the seat has fuel enabled, then emits TierResearchStarted
        /// (subject faction, aux target tier, param0 start tick, param1 complete tick).
        /// </summary>
        private void StartTierResearch(ref SeatState seat, int techIndex)
        {
            int complete = checked(Tick + _catalog.TechResearchTicksAt(techIndex));
            seat.Manpower -= _catalog.TechManpowerAt(techIndex);
            seat.Materials -= _catalog.TechMaterielAt(techIndex);
            if (seat.FuelEnabled)
            {
                seat.Fuel -= _catalog.TechFuelAt(techIndex);
            }

            seat.TierResearchCompleteTick = complete;
            seat.ResearchStartTick = Tick;
            Emit(SimEventKind.TierResearchStarted, seat.FactionId, _catalog.TechTargetTierAt(techIndex), Tick, complete);
        }

        /// <summary>
        /// Player- or AI-issued tier research. Reject order: 13 UnknownSeat,
        /// 4 NotOwner, 31 ResearchInProgress, 32 TierAlreadyReached,
        /// 33 NoQualifyingTier, then 11 materiel / 12 fuel / 10 manpower.
        /// Cost is paid on this tick; completion runs in AdvanceResearch.
        /// </summary>
        private void ApplyResearchTech(SimCommand command)
        {
            int seatIndex = FindSeat(command.IssuerId);
            if (seatIndex < 0 || _catalog == null)
            {
                Reject(command, CommandRejectReason.UnknownSeat, 0, 0);
                return;
            }

            int techIndex = command.Arg0;
            if (techIndex < 0 || techIndex >= _catalog.TechCount)
            {
                Reject(command, CommandRejectReason.UnknownKind, techIndex, 0);
                return;
            }

            SeatState seat = _seats[seatIndex];
            if (seat.FactionId != command.IssuerId)
            {
                Reject(command, CommandRejectReason.NotOwner, 0, 0);
                return;
            }

            int targetTier = _catalog.TechTargetTierAt(techIndex);
            if (seat.TierResearchCompleteTick >= 0)
            {
                Reject(command, CommandRejectReason.ResearchInProgress, seat.TierResearchCompleteTick, 0);
                return;
            }

            if (seat.CurrentTier >= targetTier)
            {
                Reject(command, CommandRejectReason.TierAlreadyReached, targetTier, seat.CurrentTier);
                return;
            }

            if (targetTier != seat.CurrentTier + 1)
            {
                Reject(command, CommandRejectReason.TierLocked, targetTier - 1, seat.CurrentTier);
                return;
            }

            if (!HasQualifyingOwnedTier(seat, techIndex))
            {
                Reject(command, CommandRejectReason.NoQualifyingTier, _catalog.TechRequiredOwnedTierAt(techIndex), 0);
                return;
            }

            CommandRejectReason shortfall = ResearchShortfall(seat, techIndex, out int need, out int have, out _);
            if (shortfall != CommandRejectReason.None)
            {
                Reject(command, shortfall, need, have);
                return;
            }

            StartTierResearch(ref seat, techIndex);
            _seats[seatIndex] = seat;
        }

        /// <summary>
        /// True when the tech has no owned-tier requirement, no towns are bound,
        /// the seat owns a town at or above the required tier, or the rules let
        /// the HQ satisfy it and the seat still owns its seat town.
        /// </summary>
        private bool HasQualifyingOwnedTier(SeatState seat, int techIndex)
        {
            int required = _catalog.TechRequiredOwnedTierAt(techIndex);
            if (required < 0 || _towns.Count == 0)
            {
                return true;
            }

            bool hqCounts = _catalog.TechHqSatisfiesOwnedTierAt(techIndex);
            for (int i = 0; i < _towns.Count; i++)
            {
                TownState town = _towns[i];
                if (town.OwnerFactionId != seat.FactionId)
                {
                    continue;
                }

                if (town.Tier >= required || (hqCounts && town.TownId == seat.HomeTownId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Queue ResearchTech for <paramref name="techId"/>; an unknown id queues index -1 and rejects.</summary>
        public SimCommand SubmitResearchTech(int factionId, string techId)
        {
            int techIndex = -1;
            if (_catalog != null && techId != null && _catalog.TryGetTechIndex(techId, out int found))
            {
                techIndex = found;
            }

            return Submit(SimCommand.ResearchTech(Tick, factionId, techIndex));
        }

        /// <summary>
        /// Pay HQ income plus owned holding-tier income on
        /// <c>incomeAccrual.intervalTicks</c>, before commands.
        /// The tier is the sim band checked against
        /// <c>derived.territories[].places</c>, not map population class.
        /// Remainder carry uses integer ppt only.
        /// </summary>
        private void AccrueSeatIncome()
        {
            if (_catalog == null)
            {
                return;
            }

            CaptureBalance rules = _catalog.Capture;
            int interval = rules.AccrualIntervalTicks;
            if (Tick < 1 || interval < 1 || (Tick % interval) != 0)
            {
                return;
            }

            int periods = rules.AccrualIntervalsPerMinute;
            if (periods < 1)
            {
                return;
            }

            bool contestedPays = rules.ContestedHoldingPaysIncome;
            for (int s = 0; s < _seats.Count; s++)
            {
                SeatState seat = _seats[s];
                int manpower;
                int materiel;
                int fuel;
                SeatIncomePerMinute(seat, rules, contestedPays, out manpower, out materiel, out fuel);
                PayResource(ref seat.Manpower, ref seat.ManpowerCarry, manpower, periods);
                PayResource(ref seat.Materials, ref seat.MaterielCarry, materiel, periods);
                if (seat.FuelEnabled)
                {
                    PayResource(ref seat.Fuel, ref seat.FuelCarry, fuel, periods);
                }
                _seats[s] = seat;
            }
        }

        private void SeatIncomePerMinute(
            SeatState seat,
            CaptureBalance rules,
            bool contestedPays,
            out int manpower,
            out int materiel,
            out int fuel)
        {
            int hqManpower;
            int hqMateriel;
            int hqFuel;
            int manpowerPpt;
            int materielPpt;
            int fuelPpt;
            rules.TryHqIncome(
                seat.EconomyFactionId,
                out hqManpower,
                out hqMateriel,
                out hqFuel,
                out manpowerPpt,
                out materielPpt,
                out fuelPpt);
            manpower = hqManpower;
            materiel = hqMateriel;
            fuel = hqFuel;
            for (int i = 0; i < _towns.Count; i++)
            {
                TownState town = _towns[i];
                if (town.OwnerFactionId != seat.FactionId || town.Tier < 0)
                {
                    continue;
                }

                if (!contestedPays && town.Contested)
                {
                    continue;
                }

                manpower += rules.HoldingManpowerPerMin(town.Tier);
                materiel += rules.HoldingMaterielPerMin(town.Tier);
                fuel += rules.HoldingFuelPerMin(town.Tier);
            }

            manpower = ApplyIncomePpt(manpower, manpowerPpt);
            materiel = ApplyIncomePpt(materiel, materielPpt);
            fuel = ApplyIncomePpt(fuel, fuelPpt);
        }

        private static int ApplyIncomePpt(int value, int ppt)
        {
            if (ppt == SimConfig.PptScale)
            {
                return value;
            }

            return (int)(((long)value * ppt) / SimConfig.PptScale);
        }

        private static void PayResource(ref int stock, ref int carry, int perMinute, int periods)
        {
            if (perMinute == 0)
            {
                return;
            }

            carry = checked(carry + perMinute);
            int gained = carry / periods;
            if (gained == 0)
            {
                return;
            }

            stock = checked(stock + gained);
            carry -= gained * periods;
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
            Emit(SimEventKind.CommandRejected, command.BattalionId, (int)reason, param0, param1, RejectKey(command));
        }

        private static ulong RejectKey(SimCommand command)
        {
            return ((ulong)(uint)command.IssuerId << 32) | (uint)command.Sequence;
        }

        private void Emit(SimEventKind kind, int subjectId, int auxId, int param0 = 0, int param1 = 0)
        {
            Emit(kind, subjectId, auxId, param0, param1, 0UL);
        }

        private void Emit(SimEventKind kind, int subjectId, int auxId, int param0, int param1, ulong key)
        {
            SimEvent ev = new SimEvent(_nextEventId, Tick, kind, subjectId, auxId, param0, param1, key);
            _events.Add(ev);
            _eventHistory.Add(ev);
            _nextEventId++;
        }

        /// <summary>
        /// Derived read model for the AI. Not part of <see cref="HashState"/> and not saved.
        /// <see cref="Ai.AiMatchHost.ReplayTo"/> rebuilds it by stepping from tick 0.
        /// </summary>
        private void PruneEventHistory()
        {
            int oldest = Tick - EventHistoryTicks;
            int drop = 0;
            while (drop < _eventHistory.Count && _eventHistory[drop].Tick < oldest)
            {
                drop++;
            }

            if (drop > 0)
            {
                _eventHistory.RemoveRange(0, drop);
            }
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
            public int ResearchStartTick;
            public int AutoResearchCursor;
            public int[] AutoResearchTech;
            public int[] AutoResearchNotBefore;
            public string EconomyFactionId;
            public int Manpower;
            public int Materials;
            public int Fuel;
            public int ManpowerCarry;
            public int MaterielCarry;
            public int FuelCarry;
            public bool FuelEnabled;
            public int PopUsed;
            public int PopCap;
            public bool SupplyDepotAllowed;
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
