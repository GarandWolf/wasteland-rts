#nullable disable
using System;
using System.Collections.Generic;
using Wasteland.Sim.Ai;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Hashing;
using Wasteland.Sim.Scenario;

namespace Wasteland.Sim.World
{
    public sealed partial class SimWorld
    {
        private readonly List<TownState> _towns = new List<TownState>();
        private readonly List<TerritoryState> _territories = new List<TerritoryState>();
        private readonly List<ulong> _flippedKeys = new List<ulong>();
        private bool[] _seatCapture = Array.Empty<bool>();
        private bool[] _seatCombat = Array.Empty<bool>();
        private bool _holdingsBound;
        private bool _ownersChanged;

        public int TownCount
        {
            get { return _towns.Count; }
        }

        /// <summary>
        /// Events with tick in <c>[fromTick, toTick]</c>, in <see cref="SimEvent.EventId"/> order.
        /// Backed by a fixed history of the last <see cref="EventHistoryTicks"/> ticks.
        /// Derived state: not included in <see cref="HashState"/> and not written by save.
        /// Replay rebuilds it by stepping the world from tick 0.
        /// </summary>
        public void CopyEventsInRange(int fromTick, int toTick, List<SimEvent> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            if (toTick < fromTick)
            {
                throw new ArgumentOutOfRangeException(nameof(toTick));
            }

            into.Clear();
            for (int i = 0; i < _eventHistory.Count; i++)
            {
                SimEvent ev = _eventHistory[i];
                if (ev.Tick >= fromTick && ev.Tick <= toTick)
                {
                    into.Add(ev);
                }
            }
        }

        /// <summary>Events stamped with <paramref name="tick"/>, in event id order.</summary>
        public void CopyEventsOfTick(int tick, List<SimEvent> into)
        {
            CopyEventsInRange(tick, tick, into);
        }

        public bool TryGetTown(int townId, out TownView view)
        {
            int index = IndexOfTown(townId);
            if (index < 0)
            {
                view = default(TownView);
                return false;
            }

            view = ToTownView(_towns[index]);
            return true;
        }

        public void CopyTowns(List<TownView> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            for (int i = 0; i < _towns.Count; i++)
            {
                into.Add(ToTownView(_towns[i]));
            }
        }

        public bool TryGetTerritory(ulong key, out TerritoryView view)
        {
            for (int i = 0; i < _territories.Count; i++)
            {
                TerritoryState territory = _territories[i];
                if (territory.Key == key)
                {
                    view = new TerritoryView(territory.Key, territory.TerritoryId, territory.SeatTownId, territory.OwnerFactionId);
                    return true;
                }
            }

            view = default(TerritoryView);
            return false;
        }

        /// <summary>
        /// Load towns and territories from the map, check sim tiers against
        /// <c>derived.territories[].places</c>, then own the bound seats' holdings.
        /// Pop cap is recomputed from <c>popCap.hqBonus</c> plus owned holding bonuses.
        /// </summary>
        public void BindHoldings(SimScenario scenario, AiMapSnapshot map)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (_catalog == null)
            {
                throw new InvalidOperationException("Bind a balance catalog before holdings.");
            }

            if (_holdingsBound)
            {
                throw new InvalidOperationException("Holdings are already bound.");
            }

            CaptureBalance rules = _catalog.Capture;
            List<TerritoryState> territories = new List<TerritoryState>();
            for (int i = 0; i < map.TerritoryCount; i++)
            {
                AiTerritoryRecord record = map.TerritoryAt(i);
                if (record.Id == 0 && record.StableKey == 0UL)
                {
                    continue;
                }

                TerritoryState territory = new TerritoryState();
                territory.TerritoryId = record.Id;
                territory.SeatTownId = record.SeatTownId;
                territory.Key = record.StableKey;
                territory.OwnerFactionId = 0;
                territories.Add(territory);
            }

            territories.Sort(CompareTerritoryId);
            for (int i = 1; i < territories.Count; i++)
            {
                if (territories[i].TerritoryId == territories[i - 1].TerritoryId)
                {
                    throw new InvalidOperationException("Duplicate territory id " + territories[i].TerritoryId + ".");
                }
            }

            for (int i = 0; i < map.TownCount; i++)
            {
                AiTownRecord record = map.TownAt(i);
                int territoryIndex = IndexOfTerritoryId(territories, record.TerritoryId);
                if (territoryIndex < 0)
                {
                    throw new InvalidOperationException("Town " + record.Id + " has no territory " + record.TerritoryId + ".");
                }

                TerritoryState territory = territories[territoryIndex];
                int population = record.Population;
                for (int p = 0; p < scenario.Derived.PopulationOverrides.Length; p++)
                {
                    ScenarioPopulationPlace over = scenario.Derived.PopulationOverrides[p];
                    if (over.MapTownId == record.Id)
                    {
                        population = over.Population;
                        break;
                    }
                }

                int tier = rules.TierForPopulation(population);
                if (population >= rules.SceneryPopulationBelow && tier < 0)
                {
                    throw new InvalidOperationException("Town " + record.Id + " population " + population + " matches no holding tier.");
                }

                bool seat = record.Id == territory.SeatTownId;
                int zone = 0;
                if (seat)
                {
                    if (record.PopulationClass < 0 || record.PopulationClass >= CaptureBalance.TierCount)
                    {
                        throw new InvalidOperationException("Seat town " + record.Id + " has no zone class.");
                    }

                    zone = rules.ZoneRadiusCm(record.PopulationClass);
                }

                TownState town = new TownState();
                town.TownId = record.Id;
                town.TerritoryId = record.TerritoryId;
                town.TerritoryKey = territory.Key;
                town.IsSeatTown = seat;
                town.XCm = record.XCm;
                town.YCm = record.YCm;
                town.RoadNodeId = record.RoadNodeId;
                town.PopulationClass = record.PopulationClass;
                town.Tier = tier;
                town.ZoneRadiusCm = zone;
                town.FortificationLevel = 0;
                town.OwnerFactionId = 0;
                town.CaptureFactionId = 0;
                town.CaptureProgressTicks = 0;
                town.CaptureNeededTicks = tier >= 0 ? rules.CaptureTicks(tier, 0) : 0;
                town.CaptureAbsentTicks = 0;
                town.Contested = false;
                town.FightNoted = false;
                town.ContestSeatA = 0;
                town.ContestSeatB = 0;
                _towns.Add(town);
            }

            for (int i = 1; i < _towns.Count; i++)
            {
                if (_towns[i].TownId <= _towns[i - 1].TownId)
                {
                    throw new InvalidOperationException("Town ids must be ascending.");
                }
            }

            _territories.AddRange(territories);
            ValidatePlaceCounts(scenario, rules);
            ApplySeatHoldings(scenario, rules);
            SpawnNeutralGarrisons(scenario);
            RecomputePopCaps();
            _ownersChanged = false;
            _holdingsBound = true;
        }

        internal int SpawnCatalogUnit(int ownerId, string unitId, int xCm, int yCm)
        {
            if (_catalog == null || unitId == null || !_catalog.TryGetUnitIndex(unitId, out int unitIndex))
            {
                throw new ArgumentException("Unit is not in the catalog.", nameof(unitId));
            }

            int id = AllocateBattalionId();
            InsertCatalogBattalion(id, ownerId, xCm, yCm, unitIndex);
            return id;
        }

        internal void PlaceBattalion(int id, int xCm, int yCm)
        {
            int index = BattalionIndexOf(id);
            SimBattalion battalion = _battalions[index];
            battalion.PositionXCm = xCm;
            battalion.PositionYCm = yCm;
            battalion.HasTarget = false;
            battalion.PathIndex = 0;
            battalion.PathCount = 0;
            battalion.PathXCm = null;
            battalion.PathYCm = null;
            _battalions[index] = battalion;
        }

        /// <summary>Test hook: set every town owned by <paramref name="from"/> to <paramref name="to"/> without events.</summary>
        internal void ReassignTownsForTests(int from, int to)
        {
            for (int i = 0; i < _towns.Count; i++)
            {
                if (_towns[i].OwnerFactionId == from)
                {
                    TownState town = _towns[i];
                    town.OwnerFactionId = to;
                    _towns[i] = town;
                }
            }
        }

        internal void SetBattalionStrength(int id, int strengthPpt)
        {
            int index = BattalionIndexOf(id);
            SimBattalion battalion = _battalions[index];
            if (strengthPpt <= 0 || battalion.MaxHp <= 0)
            {
                battalion.Hp = 0;
                battalion.StrengthPpt = 0;
            }
            else
            {
                battalion.Hp = (int)((long)battalion.MaxHp * strengthPpt / 1000);
                if (battalion.Hp < 1)
                {
                    battalion.Hp = 1;
                }

                battalion.StrengthPpt = CombatMath.StrengthFromHp(battalion.Hp, battalion.MaxHp);
            }

            _battalions[index] = battalion;
        }

        private void ResolveCapture()
        {
            _flippedKeys.Clear();
            if (_towns.Count == 0 || _catalog == null)
            {
                return;
            }

            EnsureSeatScratch();
            CaptureBalance rules = _catalog.Capture;
            for (int t = 0; t < _towns.Count; t++)
            {
                TownState town = _towns[t];
                if (!town.IsSeatTown || town.ZoneRadiusCm <= 0)
                {
                    continue;
                }

                Array.Clear(_seatCapture, 0, _seats.Count);
                Array.Clear(_seatCombat, 0, _seats.Count);
                long radiusSq = (long)town.ZoneRadiusCm * town.ZoneRadiusCm;
                bool garrison = false;
                for (int b = 0; b < _battalions.Count; b++)
                {
                    SimBattalion battalion = _battalions[b];
                    if (battalion.Hp <= 0 || battalion.UnitIndex < 0)
                    {
                        continue;
                    }

                    if (!InsideSq(battalion.PositionXCm, battalion.PositionYCm, town.XCm, town.YCm, radiusSq))
                    {
                        continue;
                    }

                    if (battalion.OwnerId == NeutralOwnerId)
                    {
                        if (_catalog.UnitCanContestAt(battalion.UnitIndex))
                        {
                            garrison = true;
                        }

                        continue;
                    }

                    int seatIndex = FindSeat(battalion.OwnerId);
                    if (seatIndex < 0)
                    {
                        continue;
                    }

                    bool capture = _catalog.UnitCanCaptureAt(battalion.UnitIndex);
                    bool contest = _catalog.UnitCanContestAt(battalion.UnitIndex);
                    if (capture)
                    {
                        _seatCapture[seatIndex] = true;
                        _seatCombat[seatIndex] = true;
                    }
                    else if (contest)
                    {
                        _seatCombat[seatIndex] = true;
                    }
                }

                int captureCount = 0;
                int combatCount = 0;
                int soleCapture = 0;
                for (int s = 0; s < _seats.Count; s++)
                {
                    if (_seatCapture[s])
                    {
                        captureCount++;
                        soleCapture = _seats[s].FactionId;
                    }

                    if (_seatCombat[s])
                    {
                        combatCount++;
                    }
                }

                bool zoneContested = combatCount >= 2 || (garrison && combatCount >= 1);
                bool meterContested = zoneContested && town.CaptureFactionId != 0;
                int seatA;
                int seatB;
                ContestPair(garrison, out seatA, out seatB);
                if (zoneContested && seatA != seatB)
                {
                    if (!town.FightNoted)
                    {
                        EmitContest(SimEventKind.TownContestStarted, town, seatA, seatB);
                        town.FightNoted = true;
                        town.ContestSeatA = seatA;
                        town.ContestSeatB = seatB;
                    }
                    else if (town.ContestSeatA != seatA || town.ContestSeatB != seatB)
                    {
                        EmitContest(SimEventKind.TownContestEnded, town, town.ContestSeatA, town.ContestSeatB);
                        EmitContest(SimEventKind.TownContestStarted, town, seatA, seatB);
                        town.ContestSeatA = seatA;
                        town.ContestSeatB = seatB;
                    }
                }
                else if (town.FightNoted)
                {
                    EmitContest(SimEventKind.TownContestEnded, town, town.ContestSeatA, town.ContestSeatB);
                    town.FightNoted = false;
                    town.ContestSeatA = 0;
                    town.ContestSeatB = 0;
                }

                town.Contested = meterContested;
                if (meterContested)
                {
                    _towns[t] = town;
                    continue;
                }

                bool meterPresent = town.CaptureFactionId != 0 && SeatHasCapture(town.CaptureFactionId);
                if (town.CaptureFactionId != 0 && !meterPresent)
                {
                    town.CaptureAbsentTicks++;
                    if (town.CaptureAbsentTicks > rules.DecayStartsAfterTicks)
                    {
                        town.CaptureProgressTicks -= rules.DecayTicksPerTick;
                        if (town.CaptureProgressTicks < 0)
                        {
                            town.CaptureProgressTicks = 0;
                        }
                    }

                    if (town.CaptureProgressTicks == 0)
                    {
                        town.CaptureFactionId = 0;
                        town.CaptureAbsentTicks = 0;
                        town.Contested = false;
                    }

                    _towns[t] = town;
                    continue;
                }

                if (town.CaptureFactionId != 0 && meterPresent)
                {
                    town.CaptureAbsentTicks = 0;
                    if (town.CaptureProgressTicks >= town.CaptureNeededTicks)
                    {
                        _towns[t] = town;
                        FlipSeat(t, town.CaptureFactionId);
                    }
                    else
                    {
                        town.CaptureProgressTicks++;
                        _towns[t] = town;
                    }

                    continue;
                }

                if (town.CaptureFactionId == 0
                    && captureCount == 1
                    && !zoneContested
                    && soleCapture != town.OwnerFactionId)
                {
                    town.CaptureFactionId = soleCapture;
                    town.CaptureProgressTicks = 1;
                    town.CaptureAbsentTicks = 0;
                    town.Contested = false;
                }

                _towns[t] = town;
            }
        }

        private void FlipSeat(int townIndex, int newOwner)
        {
            TownState town = _towns[townIndex];
            int oldOwner = town.OwnerFactionId;
            int territoryId = town.TerritoryId;
            ulong key = town.TerritoryKey;
            for (int i = 0; i < _towns.Count; i++)
            {
                if (_towns[i].TerritoryId != territoryId)
                {
                    continue;
                }

                TownState other = _towns[i];
                other.OwnerFactionId = newOwner;
                if (other.TownId == town.TownId)
                {
                    other.CaptureFactionId = 0;
                    other.CaptureProgressTicks = 0;
                    other.CaptureAbsentTicks = 0;
                    other.Contested = false;
                }

                _towns[i] = other;
            }

            for (int i = 0; i < _territories.Count; i++)
            {
                if (_territories[i].TerritoryId == territoryId)
                {
                    TerritoryState territory = _territories[i];
                    territory.OwnerFactionId = newOwner;
                    _territories[i] = territory;
                }
            }

            _flippedKeys.Add(key);
            _ownersChanged = true;
            Emit(
                SimEventKind.TownOwnerChanged,
                town.TownId,
                newOwner,
                oldOwner,
                1,
                key);
        }

        private bool SeatHasCapture(int factionId)
        {
            int seatIndex = FindSeat(factionId);
            return seatIndex >= 0 && _seatCapture[seatIndex];
        }

        private void EnsureSeatScratch()
        {
            if (_seatCapture.Length < _seats.Count)
            {
                _seatCapture = new bool[_seats.Count];
                _seatCombat = new bool[_seats.Count];
            }
        }

        private void ValidatePlaceCounts(SimScenario scenario, CaptureBalance rules)
        {
            ScenarioDerivedTerritory[] derived = scenario.Derived.Territories;
            for (int i = 0; i < derived.Length; i++)
            {
                ScenarioDerivedTerritory row = derived[i];
                int territoryIndex = -1;
                for (int t = 0; t < _territories.Count; t++)
                {
                    if (_territories[t].Key == row.MapStableKey)
                    {
                        territoryIndex = t;
                        break;
                    }
                }

                if (territoryIndex < 0)
                {
                    throw new InvalidOperationException("Derived territory is not on the map.");
                }

                int[] counts = new int[CaptureBalance.TierCount];
                int seatTier = -1;
                int territoryId = _territories[territoryIndex].TerritoryId;
                for (int n = 0; n < _towns.Count; n++)
                {
                    TownState town = _towns[n];
                    if (town.TerritoryId != territoryId || town.Tier < 0)
                    {
                        continue;
                    }

                    counts[town.Tier]++;
                    if (town.IsSeatTown)
                    {
                        seatTier = town.Tier;
                    }
                }

                ScenarioPlaceCounts places = row.Places;
                if (counts[0] != places.Hamlet
                    || counts[1] != places.Village
                    || counts[2] != places.Town
                    || counts[3] != places.City)
                {
                    throw new InvalidOperationException(
                        "Territory " + territoryId + " place counts do not match derived.territories places.");
                }

                if (seatTier < 0 || rules.TierId(seatTier) != row.SeatTier)
                {
                    throw new InvalidOperationException(
                        "Territory " + territoryId + " seat tier does not match derived seatTier.");
                }
            }
        }

        private void ApplySeatHoldings(SimScenario scenario, CaptureBalance rules)
        {
            for (int s = 0; s < _seats.Count; s++)
            {
                SeatState seat = _seats[s];
                int scenarioIndex = -1;
                for (int i = 0; i < scenario.SeatCount; i++)
                {
                    if (scenario.SeatAt(i).HomeTownId == seat.HomeTownId)
                    {
                        scenarioIndex = i;
                        break;
                    }
                }

                if (scenarioIndex < 0)
                {
                    continue;
                }

                ScenarioSeat authored = scenario.SeatAt(scenarioIndex);
                seat.SupplyDepotAllowed = false;
                for (int b = 0; b < authored.BuildableBuildings.Length; b++)
                {
                    if (authored.BuildableBuildings[b] == "supply_depot")
                    {
                        seat.SupplyDepotAllowed = true;
                        break;
                    }
                }

                int fort = 0;
                for (int b = 0; b < authored.StartingBuildings.Length; b++)
                {
                    string buildingId = authored.StartingBuildings[b];
                    int level = _catalog.BuildingFortification(buildingId);
                    if (level < 0)
                    {
                        throw new InvalidOperationException("Starting building is not in the catalog.");
                    }

                    if (level > fort)
                    {
                        fort = level;
                    }
                }

                int home = IndexOfTown(seat.HomeTownId);
                if (home >= 0)
                {
                    TownState homeTown = _towns[home];
                    homeTown.FortificationLevel = fort;
                    if (homeTown.Tier >= 0)
                    {
                        homeTown.CaptureNeededTicks = rules.CaptureTicks(homeTown.Tier, fort);
                    }

                    _towns[home] = homeTown;
                }

                for (int h = 0; h < authored.StartingHoldings.Length; h++)
                {
                    ulong key = authored.StartingHoldings[h].TerritoryKey;
                    for (int t = 0; t < _territories.Count; t++)
                    {
                        if (_territories[t].Key != key)
                        {
                            continue;
                        }

                        TerritoryState territory = _territories[t];
                        territory.OwnerFactionId = seat.FactionId;
                        _territories[t] = territory;
                        for (int n = 0; n < _towns.Count; n++)
                        {
                            if (_towns[n].TerritoryId == territory.TerritoryId)
                            {
                                TownState owned = _towns[n];
                                owned.OwnerFactionId = seat.FactionId;
                                _towns[n] = owned;
                            }
                        }
                    }
                }

                _seats[s] = seat;
            }
        }

        private void RecomputePopCaps()
        {
            if (_catalog == null)
            {
                return;
            }

            CaptureBalance rules = _catalog.Capture;
            for (int s = 0; s < _seats.Count; s++)
            {
                SeatState seat = _seats[s];
                int bonus = 0;
                for (int t = 0; t < _territories.Count; t++)
                {
                    if (_territories[t].OwnerFactionId != seat.FactionId)
                    {
                        continue;
                    }

                    int territoryId = _territories[t].TerritoryId;
                    for (int n = 0; n < _towns.Count; n++)
                    {
                        TownState town = _towns[n];
                        if (town.TerritoryId == territoryId && town.Tier >= 0)
                        {
                            bonus += rules.PopCapBonus(town.Tier);
                        }
                    }
                }

                long cap = (long)_catalog.PopCapBase + _catalog.PopCapHqBonus + bonus;
                if (cap > _catalog.PopCapHardCap)
                {
                    cap = _catalog.PopCapHardCap;
                }

                if (cap < 0)
                {
                    cap = 0;
                }

                seat.PopCap = (int)cap;
                _seats[s] = seat;
            }
        }

        private void ApplyClaimTown(SimCommand command)
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

            if (battalion.Hp <= 0)
            {
                Reject(command, CommandRejectReason.UnknownBattalion, 0, 0);
                return;
            }

            if (_catalog == null
                || battalion.UnitIndex < 0
                || !_catalog.UnitCanCaptureAt(battalion.UnitIndex))
            {
                Reject(command, CommandRejectReason.NotCapturer, 0, 0);
                return;
            }

            int townIndex = IndexOfTown(command.Arg0);
            if (townIndex < 0)
            {
                Reject(command, CommandRejectReason.UnknownTown, command.Arg0, 0);
                return;
            }

            TownState town = _towns[townIndex];
            if (town.Tier < 0)
            {
                Reject(command, CommandRejectReason.UnknownTown, command.Arg0, 0);
                return;
            }

            if (!town.IsSeatTown)
            {
                Reject(command, CommandRejectReason.NotSeatTown, town.TownId, 0);
                return;
            }

            if (town.OwnerFactionId == command.IssuerId)
            {
                Reject(command, CommandRejectReason.AlreadyOwned, town.TownId, town.OwnerFactionId);
                return;
            }

            ApplyMove(SimCommand.Move(command.IssuedTick, command.IssuerId, command.BattalionId, town.XCm, town.YCm)
                .WithSchedule(command.ExecuteTick, command.Sequence));
        }

        private void HashCaptureState(XxHash64Builder hash)
        {
            hash.AddInt(_towns.Count);
            for (int i = 0; i < _towns.Count; i++)
            {
                TownState town = _towns[i];
                hash.AddInt(town.TownId);
                hash.AddInt(town.TerritoryId);
                hash.AddULong(town.TerritoryKey);
                hash.AddBool(town.IsSeatTown);
                hash.AddInt(town.XCm);
                hash.AddInt(town.YCm);
                hash.AddInt(town.RoadNodeId);
                hash.AddInt(town.PopulationClass);
                hash.AddInt(town.Tier);
                hash.AddInt(town.ZoneRadiusCm);
                hash.AddInt(town.FortificationLevel);
                hash.AddInt(town.OwnerFactionId);
                hash.AddInt(town.CaptureFactionId);
                hash.AddInt(town.CaptureProgressTicks);
                hash.AddInt(town.CaptureNeededTicks);
                hash.AddInt(town.CaptureAbsentTicks);
                hash.AddBool(town.Contested);
                hash.AddBool(town.FightNoted);
                hash.AddInt(town.ContestSeatA);
                hash.AddInt(town.ContestSeatB);
            }

            hash.AddInt(_territories.Count);
            for (int i = 0; i < _territories.Count; i++)
            {
                TerritoryState territory = _territories[i];
                hash.AddULong(territory.Key);
                hash.AddInt(territory.TerritoryId);
                hash.AddInt(territory.SeatTownId);
                hash.AddInt(territory.OwnerFactionId);
            }

            HashDepots(hash);
        }

        private static TownView ToTownView(TownState town)
        {
            return new TownView(
                town.TownId,
                town.TerritoryId,
                town.TerritoryKey,
                town.IsSeatTown,
                town.XCm,
                town.YCm,
                town.RoadNodeId,
                town.PopulationClass,
                town.Tier,
                town.ZoneRadiusCm,
                town.FortificationLevel,
                town.OwnerFactionId,
                town.CaptureFactionId,
                town.CaptureProgressTicks,
                town.CaptureNeededTicks,
                town.CaptureAbsentTicks,
                town.Contested);
        }

        private void EmitContest(SimEventKind kind, TownState town, int seatA, int seatB)
        {
            Emit(kind, town.TownId, seatA, seatB, town.OwnerFactionId, town.TerritoryKey);
        }

        private void ContestPair(bool garrison, out int seatA, out int seatB)
        {
            int low = 0;
            int high = 0;
            int count = 0;
            if (garrison)
            {
                low = NeutralOwnerId;
                high = NeutralOwnerId;
                count = 1;
            }

            for (int s = 0; s < _seats.Count; s++)
            {
                if (!_seatCombat[s])
                {
                    continue;
                }

                int id = _seats[s].FactionId;
                if (count == 0 || id < low)
                {
                    low = id;
                }

                if (count == 0 || id > high)
                {
                    high = id;
                }

                count++;
            }

            if (count < 2)
            {
                seatA = 0;
                seatB = 0;
                return;
            }

            seatA = low;
            seatB = high;
        }

        private void SpawnNeutralGarrisons(SimScenario scenario)
        {
            ScenarioGarrison[] garrisons = scenario.Garrisons;
            if (garrisons == null || garrisons.Length == 0 || _catalog == null)
            {
                return;
            }

            for (int i = 0; i < garrisons.Length; i++)
            {
                ScenarioGarrison garrison = garrisons[i];
                int territoryIndex = -1;
                for (int t = 0; t < _territories.Count; t++)
                {
                    if (_territories[t].Key == garrison.TerritoryKey)
                    {
                        territoryIndex = t;
                        break;
                    }
                }

                if (territoryIndex < 0)
                {
                    throw new InvalidOperationException("Garrison holding is not on the map.");
                }

                int seatTownId = _territories[territoryIndex].SeatTownId;
                int townIndex = IndexOfTown(seatTownId);
                if (townIndex < 0)
                {
                    throw new InvalidOperationException("Garrison holding has no seat town.");
                }

                TownState town = _towns[townIndex];
                ScenarioUnitCount[] units = garrison.Units;
                for (int u = 0; u < units.Length; u++)
                {
                    if (!_catalog.TryGetUnitIndex(units[u].UnitId, out int unitIndex))
                    {
                        throw new InvalidOperationException("Garrison unit is not in the catalog.");
                    }

                    for (int n = 0; n < units[u].Count; n++)
                    {
                        InsertCatalogBattalion(
                            AllocateBattalionId(),
                            NeutralOwnerId,
                            town.XCm,
                            town.YCm,
                            unitIndex);
                    }
                }
            }
        }

        private int IndexOfTown(int townId)
        {
            int lo = 0;
            int hi = _towns.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int id = _towns[mid].TownId;
                if (id == townId)
                {
                    return mid;
                }

                if (id < townId)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return -1;
        }

        private static int IndexOfTerritoryId(List<TerritoryState> territories, int territoryId)
        {
            int lo = 0;
            int hi = territories.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                int id = territories[mid].TerritoryId;
                if (id == territoryId)
                {
                    return mid;
                }

                if (id < territoryId)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return -1;
        }

        private static int CompareTerritoryId(TerritoryState left, TerritoryState right)
        {
            return left.TerritoryId.CompareTo(right.TerritoryId);
        }

        private static bool InsideSq(int xCm, int yCm, int originX, int originY, long radiusSq)
        {
            long dx = (long)xCm - originX;
            long dy = (long)yCm - originY;
            return (dx * dx) + (dy * dy) <= radiusSq;
        }

        private struct TownState
        {
            public int TownId;
            public int TerritoryId;
            public ulong TerritoryKey;
            public bool IsSeatTown;
            public int XCm;
            public int YCm;
            public int RoadNodeId;
            public int PopulationClass;
            public int Tier;
            public int ZoneRadiusCm;
            public int FortificationLevel;
            public int OwnerFactionId;
            public int CaptureFactionId;
            public int CaptureProgressTicks;
            public int CaptureNeededTicks;
            public int CaptureAbsentTicks;
            public bool Contested;
            public bool FightNoted;
            public int ContestSeatA;
            public int ContestSeatB;
        }

        private struct TerritoryState
        {
            public int TerritoryId;
            public int SeatTownId;
            public ulong Key;
            public int OwnerFactionId;
        }
    }
}
