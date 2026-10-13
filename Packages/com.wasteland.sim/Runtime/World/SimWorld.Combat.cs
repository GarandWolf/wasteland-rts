#nullable disable
using System;
using System.IO;
using Wasteland.Sim.Commands;
using Wasteland.Sim.Events;
using Wasteland.Sim.Movement;
using Wasteland.Sim.Scenario;

namespace Wasteland.Sim.World
{
    public sealed partial class SimWorld
    {
        /// <summary>Neutral garrison owner. Hostile to every seat and never issues orders.</summary>
        public const int NeutralOwnerId = -1;

        public const int MatchReasonHqCaptured = 1;
        public const int MatchReasonDraw = 2;

        /// <summary>World snapshot schema. A version 11 blob is refused.</summary>
        public const int WorldStateSchema = 13;

        private CombatTerrain _terrain;
        private int[] _pendingDamage = Array.Empty<int>();
        private bool _matchOver;
        private int _matchWinner = -1;
        private int _matchReason;
        private int _matchLoser = -1;
        private int _matchEndTick = -1;

        public void BindCombatTerrain(CombatTerrain terrain)
        {
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        }

        public byte[] Save()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                BinaryWriter writer = new BinaryWriter(stream);
                writer.Write(WorldStateSchema);
                writer.Write(Tick);
                writer.Write(_matchOver);
                writer.Write(_matchWinner);
                writer.Write(_matchReason);
                writer.Write(_matchLoser);
                writer.Write(_matchEndTick);
                writer.Write(_battalions.Count);
                for (int i = 0; i < _battalions.Count; i++)
                {
                    SimBattalion battalion = _battalions[i];
                    writer.Write(battalion.Id);
                    writer.Write(battalion.Hp);
                    writer.Write(battalion.MaxHp);
                    writer.Write(battalion.Cooldown);
                    writer.Write(battalion.AttackTargetId);
                    writer.Write(battalion.CutDetectedTick);
                    writer.Write(battalion.Penalized);
                    writer.Write(battalion.StrengthPpt);
                }

                writer.Write(_seats.Count);
                for (int i = 0; i < _seats.Count; i++)
                {
                    SeatState seat = _seats[i];
                    writer.Write(seat.FactionId);
                    writer.Write(seat.CurrentTier);
                    writer.Write(seat.TierResearchCompleteTick);
                    writer.Write(seat.ResearchStartTick);
                    writer.Write(seat.AutoResearchCursor);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        public void Load(byte[] blob)
        {
            if (blob == null)
            {
                throw new ArgumentNullException(nameof(blob));
            }

            using (MemoryStream stream = new MemoryStream(blob, false))
            {
                BinaryReader reader = new BinaryReader(stream);
                int schema = reader.ReadInt32();
                if (schema != WorldStateSchema)
                {
                    throw new InvalidDataException(
                        "World state schema version " + schema
                        + " is unsupported; expected " + WorldStateSchema + ".");
                }

                int tick = reader.ReadInt32();
                bool over = reader.ReadBoolean();
                int winner = reader.ReadInt32();
                int reason = reader.ReadInt32();
                int loser = reader.ReadInt32();
                int endTick = reader.ReadInt32();
                int count = reader.ReadInt32();
                if (count != _battalions.Count)
                {
                    throw new InvalidDataException("World save battalion count does not match.");
                }

                for (int i = 0; i < count; i++)
                {
                    int id = reader.ReadInt32();
                    int hp = reader.ReadInt32();
                    int maxHp = reader.ReadInt32();
                    int cooldown = reader.ReadInt32();
                    int attack = reader.ReadInt32();
                    int cut = reader.ReadInt32();
                    bool penalized = reader.ReadBoolean();
                    int strength = reader.ReadInt32();
                    if (!_battalionIndexById.TryGetValue(id, out int index))
                    {
                        throw new InvalidDataException("World save names missing battalion " + id + ".");
                    }

                    SimBattalion battalion = _battalions[index];
                    battalion.Hp = hp;
                    battalion.MaxHp = maxHp;
                    battalion.Cooldown = cooldown;
                    battalion.AttackTargetId = attack;
                    battalion.CutDetectedTick = cut;
                    battalion.Penalized = penalized;
                    battalion.StrengthPpt = strength;
                    _battalions[index] = battalion;
                }

                int seatCount = reader.ReadInt32();
                if (seatCount != _seats.Count)
                {
                    throw new InvalidDataException("World save seat count does not match.");
                }

                for (int i = 0; i < seatCount; i++)
                {
                    int factionId = reader.ReadInt32();
                    int tier = reader.ReadInt32();
                    int complete = reader.ReadInt32();
                    int start = reader.ReadInt32();
                    int cursor = reader.ReadInt32();
                    int seatIndex = FindSeat(factionId);
                    if (seatIndex < 0)
                    {
                        throw new InvalidDataException("World save names missing seat " + factionId + ".");
                    }

                    SeatState seat = _seats[seatIndex];
                    seat.CurrentTier = tier;
                    seat.TierResearchCompleteTick = complete;
                    seat.ResearchStartTick = start;
                    seat.AutoResearchCursor = cursor;
                    _seats[seatIndex] = seat;
                }

                Tick = tick;
                _matchOver = over;
                _matchWinner = winner;
                _matchReason = reason;
                _matchLoser = loser;
                _matchEndTick = endTick;
            }
        }

        private void ApplyAttack(SimCommand command)
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

            if (_catalog == null || battalion.UnitIndex < 0 || !_catalog.UnitArmedAt(battalion.UnitIndex))
            {
                Reject(command, CommandRejectReason.NotArmed, battalion.UnitIndex, 0);
                return;
            }

            if (command.Arg0 == battalion.Id)
            {
                Reject(command, CommandRejectReason.TargetIsSelf, command.Arg0, 0);
                return;
            }

            if (!_battalionIndexById.TryGetValue(command.Arg0, out int targetIndex))
            {
                Reject(command, CommandRejectReason.UnknownTarget, command.Arg0, 0);
                return;
            }

            SimBattalion target = _battalions[targetIndex];
            if (target.Hp <= 0)
            {
                Reject(command, CommandRejectReason.UnknownTarget, command.Arg0, 0);
                return;
            }

            if (target.OwnerId == battalion.OwnerId)
            {
                Reject(command, CommandRejectReason.TargetNotEnemy, target.Id, target.OwnerId);
                return;
            }

            battalion.AttackTargetId = target.Id;
            ClearPath(ref battalion);
            battalion.HasTarget = false;
            _battalions[index] = battalion;
        }

        private void SupplyUpdate()
        {
            if (_catalog == null)
            {
                return;
            }

            CombatRules rules = _catalog.Combat;
            if (rules.RecheckIntervalTicks < 1 || Tick % rules.RecheckIntervalTicks != 0)
            {
                return;
            }

            for (int i = 0; i < _battalions.Count; i++)
            {
                SimBattalion battalion = _battalions[i];
                if (battalion.Hp <= 0)
                {
                    continue;
                }

                if (battalion.OwnerId == NeutralOwnerId || IsInSupply(battalion))
                {
                    battalion.CutDetectedTick = -1;
                    battalion.Penalized = false;
                }
                else
                {
                    if (battalion.CutDetectedTick < 0)
                    {
                        battalion.CutDetectedTick = Tick;
                    }

                    if (Tick - battalion.CutDetectedTick >= rules.GraceTicks)
                    {
                        battalion.Penalized = true;
                    }
                }

                _battalions[i] = battalion;
            }
        }

        private bool IsInSupply(SimBattalion battalion)
        {
            if (_roads == null || _roads.NodeCount < 1 || _catalog == null)
            {
                return true;
            }

            int seatIndex = FindSeat(battalion.OwnerId);
            if (seatIndex < 0)
            {
                return true;
            }

            int nodeId = NearestNodeId(battalion.PositionXCm, battalion.PositionYCm);
            if (!_roads.TryGetNodeIndex(nodeId, out int nodeIndex))
            {
                return false;
            }

            long dx = (long)_roads.GetNodeXCm(nodeIndex) - battalion.PositionXCm;
            long dy = (long)_roads.GetNodeYCm(nodeIndex) - battalion.PositionYCm;
            long link = _catalog.Combat.UnitLinkMaxOffRoadCm;
            if ((dx * dx) + (dy * dy) > link * link)
            {
                return false;
            }

            FillRoadDistance(nodeId);
            return ConnectedToOwnSupply(seatIndex, battalion.OwnerId, _catalog.Capture);
        }

        private void ResolveCombat()
        {
            if (_catalog == null)
            {
                return;
            }

            int count = _battalions.Count;
            if (_pendingDamage.Length < count)
            {
                _pendingDamage = new int[count];
            }

            Array.Clear(_pendingDamage, 0, count);
            CombatRules rules = _catalog.Combat;
            for (int i = 0; i < count; i++)
            {
                SimBattalion attacker = _battalions[i];
                if (attacker.Hp <= 0 || attacker.UnitIndex < 0 || !_catalog.UnitArmedAt(attacker.UnitIndex))
                {
                    continue;
                }

                if (attacker.Cooldown > 0)
                {
                    continue;
                }

                int targetIndex = PickTarget(i);
                if (targetIndex < 0)
                {
                    continue;
                }

                _pendingDamage[targetIndex] += DamageBetween(i, targetIndex, rules);
                attacker.Cooldown = CombatMath.EffectiveReload(
                    _catalog.UnitReloadTicksAt(attacker.UnitIndex),
                    attacker.Penalized,
                    rules.FireRatePpt);
                _battalions[i] = attacker;
            }

            for (int i = 0; i < count; i++)
            {
                if (_pendingDamage[i] == 0)
                {
                    continue;
                }

                SimBattalion target = _battalions[i];
                target.Hp -= _pendingDamage[i];
                target.StrengthPpt = CombatMath.StrengthFromHp(target.Hp, target.MaxHp);
                _battalions[i] = target;
            }

            RemoveDead();
            for (int i = 0; i < _battalions.Count; i++)
            {
                SimBattalion battalion = _battalions[i];
                if (battalion.Hp > 0 && battalion.Cooldown > 0)
                {
                    battalion.Cooldown--;
                    _battalions[i] = battalion;
                }
            }
        }

        private int PickTarget(int attackerIndex)
        {
            SimBattalion attacker = _battalions[attackerIndex];
            if (attacker.AttackTargetId != 0
                && _battalionIndexById.TryGetValue(attacker.AttackTargetId, out int ordered)
                && ValidTarget(attackerIndex, ordered))
            {
                return ordered;
            }

            int best = -1;
            int bestPrefer = 0;
            int bestHp = 0;
            int bestId = 0;
            bool armedPrefer = false;
            int preferColumn = CombatMath.ColumnSoft;
            if (_catalog.UnitDamageVsArmoredAt(attacker.UnitIndex) != _catalog.UnitDamageVsSoftAt(attacker.UnitIndex))
            {
                armedPrefer = true;
                preferColumn = _catalog.UnitDamageVsArmoredAt(attacker.UnitIndex) > _catalog.UnitDamageVsSoftAt(attacker.UnitIndex)
                    ? CombatMath.ColumnArmored
                    : CombatMath.ColumnSoft;
            }

            for (int i = 0; i < _battalions.Count; i++)
            {
                if (!ValidTarget(attackerIndex, i))
                {
                    continue;
                }

                SimBattalion target = _battalions[i];
                int column = CombatMath.ColumnOf(TargetArmored(target));
                int prefer = !armedPrefer || column == preferColumn ? 0 : 1;
                bool better = best < 0
                    || prefer < bestPrefer
                    || (prefer == bestPrefer && target.Hp < bestHp)
                    || (prefer == bestPrefer && target.Hp == bestHp && target.Id < bestId);
                if (!better)
                {
                    continue;
                }

                best = i;
                bestPrefer = prefer;
                bestHp = target.Hp;
                bestId = target.Id;
            }

            return best;
        }

        private bool ValidTarget(int attackerIndex, int targetIndex)
        {
            if (attackerIndex == targetIndex)
            {
                return false;
            }

            SimBattalion attacker = _battalions[attackerIndex];
            SimBattalion target = _battalions[targetIndex];
            if (target.Hp <= 0 || target.OwnerId == attacker.OwnerId)
            {
                return false;
            }

            if (target.MoveClass == MovementClass.Air && !_catalog.UnitCanTargetAirAt(attacker.UnitIndex))
            {
                return false;
            }

            long dx = (long)attacker.PositionXCm - target.PositionXCm;
            long dy = (long)attacker.PositionYCm - target.PositionYCm;
            long distSq = (dx * dx) + (dy * dy);
            long range = _catalog.UnitRangeCmAt(attacker.UnitIndex);
            if (distSq > range * range)
            {
                return false;
            }

            long minRange = _catalog.UnitMinRangeCmAt(attacker.UnitIndex);
            if (minRange > 0 && distSq < minRange * minRange)
            {
                return false;
            }

            return true;
        }

        private bool TargetArmored(SimBattalion target)
        {
            if (target.UnitIndex < 0)
            {
                return false;
            }

            return _catalog.UnitTargetArmoredAt(target.UnitIndex);
        }

        private int DamageBetween(int attackerIndex, int targetIndex, CombatRules rules)
        {
            SimBattalion attacker = _battalions[attackerIndex];
            SimBattalion target = _battalions[targetIndex];
            bool armored = TargetArmored(target);
            int baseDamage = armored
                ? _catalog.UnitDamageVsArmoredAt(attacker.UnitIndex)
                : _catalog.UnitDamageVsSoftAt(attacker.UnitIndex);
            TerrainSample attackerCell = SampleTerrain(attacker.PositionXCm, attacker.PositionYCm);
            TerrainSample targetCell = SampleTerrain(target.PositionXCm, target.PositionYCm);
            bool targetUrban = targetCell.Urban || target.OwnerId == NeutralOwnerId;
            bool attackerArmored = attacker.UnitIndex >= 0 && _catalog.UnitTargetArmoredAt(attacker.UnitIndex);
            int attackPpt = CombatMath.AttackPpt(
                rules.IndirectVsStructureBonusPpt,
                rules.ArmourAttackInUrbanPpt,
                rules.AttackPptMax,
                _catalog.UnitIndirectAt(attacker.UnitIndex),
                false,
                attackerArmored,
                targetUrban);
            bool soft = !armored;
            int forts = FortificationUnder(target);
            int defense = CombatMath.DefensePpt(
                rules.InfantryInUrban,
                rules.InfantryInForest,
                rules.SlopeDefense,
                rules.PerFortificationLevel,
                rules.FordDefense,
                rules.DefenseMin,
                soft,
                targetUrban,
                targetCell.Forest,
                targetCell.SlopePpt,
                forts,
                targetCell.Ford);
            return CombatMath.Volley(
                baseDamage,
                attackPpt,
                target.UnitIndex >= 0 ? _catalog.UnitArmorPptAt(target.UnitIndex) : 0,
                defense,
                rules.MaxDefensePpt,
                attackerCell.ElevationDm,
                targetCell.ElevationDm,
                rules.HighGroundThresholdDm,
                rules.HighGroundPpt,
                rules.MinDamagePerVolley);
        }

        private TerrainSample SampleTerrain(int xCm, int yCm)
        {
            if (_terrain == null)
            {
                return new TerrainSample(0, 0, false, false, false);
            }

            return _terrain.Sample(xCm, yCm);
        }

        private int FortificationUnder(SimBattalion target)
        {
            if (target.OwnerId == NeutralOwnerId)
            {
                return 0;
            }

            int best = 0;
            for (int i = 0; i < _towns.Count; i++)
            {
                TownState town = _towns[i];
                if (!town.IsSeatTown || town.ZoneRadiusCm <= 0 || town.OwnerFactionId != target.OwnerId)
                {
                    continue;
                }

                long radius = town.ZoneRadiusCm;
                if (!InsideSq(target.PositionXCm, target.PositionYCm, town.XCm, town.YCm, radius * radius))
                {
                    continue;
                }

                if (town.FortificationLevel > best)
                {
                    best = town.FortificationLevel;
                }
            }

            return best;
        }

        private void RemoveDead()
        {
            int index = 0;
            while (index < _battalions.Count)
            {
                SimBattalion battalion = _battalions[index];
                if (battalion.Hp > 0)
                {
                    index++;
                    continue;
                }

                int id = battalion.Id;
                int owner = battalion.OwnerId;
                int unitIndex = battalion.UnitIndex;
                Emit(SimEventKind.BattalionDestroyed, id, owner, unitIndex, 0);
                if (TryDetachReserved(id, out DepotState depot))
                {
                    int seatIndex = FindSeat(depot.OwnerFactionId);
                    if (seatIndex >= 0)
                    {
                        _seats[seatIndex] = Credit(_seats[seatIndex], depot);
                    }

                    Emit(
                        SimEventKind.OrderRefunded,
                        depot.OwnerFactionId,
                        (int)CommandKind.BuildDepot,
                        depot.NodeId,
                        depot.MaterielCharged,
                        depot.TerritoryKey);
                }

                for (int other = 0; other < _battalions.Count; other++)
                {
                    if (_battalions[other].AttackTargetId != id)
                    {
                        continue;
                    }

                    SimBattalion aimed = _battalions[other];
                    aimed.AttackTargetId = 0;
                    _battalions[other] = aimed;
                }

                int ownerSeat = FindSeat(owner);
                if (ownerSeat >= 0 && unitIndex >= 0)
                {
                    int pop = _seats[ownerSeat].PopUsed - _catalog.UnitPopAt(unitIndex);
                    if (pop < 0)
                    {
                        pop = 0;
                    }

                    SeatState seat = _seats[ownerSeat];
                    seat.PopUsed = pop;
                    _seats[ownerSeat] = seat;
                }

                _battalions.RemoveAt(index);
                RebuildBattalionIndex();
                if (_unitIndexById.TryGetValue(id, out int unitAt))
                {
                    _units.RemoveAt(unitAt);
                    RebuildUnitIndex();
                }
            }
        }

        private void PrepareAttackMove(ref SimBattalion battalion)
        {
            if (battalion.AttackTargetId == 0 || battalion.Hp <= 0 || _catalog == null || battalion.UnitIndex < 0)
            {
                return;
            }

            if (!_catalog.UnitArmedAt(battalion.UnitIndex))
            {
                return;
            }

            if (!_battalionIndexById.TryGetValue(battalion.AttackTargetId, out int targetIndex))
            {
                return;
            }

            SimBattalion target = _battalions[targetIndex];
            if (target.Hp <= 0)
            {
                return;
            }

            long dx = (long)battalion.PositionXCm - target.PositionXCm;
            long dy = (long)battalion.PositionYCm - target.PositionYCm;
            long distSq = (dx * dx) + (dy * dy);
            long range = _catalog.UnitRangeCmAt(battalion.UnitIndex);
            long minRange = _catalog.UnitMinRangeCmAt(battalion.UnitIndex);
            bool tooFar = distSq > range * range;
            bool tooClose = minRange > 0 && distSq < minRange * minRange;
            if (!tooFar && !tooClose)
            {
                ClearPath(ref battalion);
                battalion.HasTarget = false;
                return;
            }

            if (!tooFar)
            {
                return;
            }

            if (AlreadyApproaching(battalion, target.PositionXCm, target.PositionYCm))
            {
                return;
            }

            if (_movement != null && _roads != null && _roads.NodeCount > 0)
            {
                TryRoadApproach(ref battalion, target.PositionXCm, target.PositionYCm);
                return;
            }

            ClearPath(ref battalion);
            battalion.TargetXCm = target.PositionXCm;
            battalion.TargetYCm = target.PositionYCm;
            battalion.HasTarget = true;
        }

        private static bool AlreadyApproaching(SimBattalion battalion, int xCm, int yCm)
        {
            if (!battalion.HasTarget)
            {
                return false;
            }

            if (battalion.PathCount > 0 && battalion.PathXCm != null && battalion.PathYCm != null)
            {
                int last = battalion.PathCount - 1;
                if (last < battalion.PathXCm.Length && last < battalion.PathYCm.Length)
                {
                    return battalion.PathXCm[last] == xCm && battalion.PathYCm[last] == yCm;
                }
            }

            return battalion.TargetXCm == xCm && battalion.TargetYCm == yCm;
        }

        private void TryRoadApproach(ref SimBattalion battalion, int xCm, int yCm)
        {
            int fromNode = NearestNodeId(battalion.PositionXCm, battalion.PositionYCm);
            int toNode = NearestNodeId(xCm, yCm);
            PathResult path = _movement.PathOnRoads(fromNode, toNode, battalion.MoveClass, RoadCostWeights.Uniform());
            if (!path.Reachable || path.Waypoints == null || path.Waypoints.Length == 0)
            {
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
            int extra = (path.Waypoints[last].XCm != xCm || path.Waypoints[last].YCm != yCm) ? 1 : 0;
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

        private void HqCheck()
        {
            if (_seats.Count == 0 || _towns.Count == 0)
            {
                return;
            }

            int still = 0;
            int stillId = -1;
            int lost = 0;
            int lostId = -1;
            for (int i = 0; i < _seats.Count; i++)
            {
                SeatState seat = _seats[i];
                int townIndex = IndexOfTown(seat.HomeTownId);
                bool owns = townIndex >= 0 && _towns[townIndex].OwnerFactionId == seat.FactionId;
                if (owns)
                {
                    still++;
                    stillId = seat.FactionId;
                }
                else
                {
                    lost++;
                    lostId = seat.FactionId;
                }
            }

            if (lost == 0)
            {
                return;
            }

            if (still == 0)
            {
                EndMatch(-1, MatchReasonDraw, -1);
                return;
            }

            if (still == 1)
            {
                EndMatch(stillId, MatchReasonHqCaptured, lost == 1 ? lostId : -1);
            }
        }

        private void EndMatch(int winner, int reason, int loser)
        {
            _matchOver = true;
            _matchWinner = winner;
            _matchReason = reason;
            _matchLoser = loser;
            _matchEndTick = Tick;
            _commands.Clear();
            Emit(SimEventKind.MatchEnded, winner, reason, loser, 0);
        }

        private void HashMatch(Wasteland.Sim.Hashing.XxHash64Builder hash)
        {
            hash.AddBool(_matchOver);
            hash.AddInt(_matchWinner);
            hash.AddInt(_matchReason);
            hash.AddInt(_matchLoser);
            hash.AddInt(_matchEndTick);
        }

        internal static void SyncSpawnVitals(ref SimBattalion battalion, int maxHp)
        {
            if (maxHp < 1)
            {
                maxHp = 1;
            }

            battalion.MaxHp = maxHp;
            battalion.Hp = maxHp;
            battalion.StrengthPpt = CombatMath.StrengthFromHp(battalion.Hp, battalion.MaxHp);
            battalion.Cooldown = 0;
            battalion.AttackTargetId = 0;
            battalion.CutDetectedTick = -1;
            battalion.Penalized = false;
        }
    }
}
